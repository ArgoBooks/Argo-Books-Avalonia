using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Common;
using ClosedXML.Excel;

namespace ArgoBooks.Core.Services;

/// <summary>
/// One distinct ambiguous currency symbol found in a file's amount cells, awaiting user resolution.
/// </summary>
public sealed class AmbiguousSymbolPrompt
{
    public required string Symbol { get; init; }
    public required IReadOnlyList<string> Candidates { get; init; }
    /// <summary>How many amount rows across the file use this symbol (for "appears in N rows").</summary>
    public int RowCount { get; set; }
}

/// <summary>
/// The outcome of scanning a workbook's amount cells for in-cell currency.
/// </summary>
public sealed class CurrencyScanResult
{
    /// <summary>Sheet name -> (0-based data-row ordinal -> resolved ISO code).</summary>
    public Dictionary<string, Dictionary<int, string>> Resolved { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Distinct ambiguous symbols present in the file, for the resolution dialog.</summary>
    public List<AmbiguousSymbolPrompt> Ambiguities { get; } = [];

    /// <summary>Sheet -> (row ordinal -> raw ambiguous symbol), resolved later via the user's choice.</summary>
    internal Dictionary<string, Dictionary<int, string>> PendingAmbiguous { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Deterministic pre-pass that scans the amount columns of the financial sheets and resolves the
/// per-row currency from in-cell symbols/codes BEFORE import. Rows with an explicit code or an
/// unambiguous symbol are resolved immediately; rows with an ambiguous symbol (e.g. "$") are held
/// until the user resolves the symbol (see <see cref="ApplyResolution"/>). The resulting per-row
/// map is fed to the Tier 1 importer via <see cref="ImportOptions.RowCurrencyBySheet"/>.
/// </summary>
public static class CurrencyImportPreparer
{
    /// <summary>Target column names that hold a transaction amount worth inspecting for currency.</summary>
    private static readonly HashSet<string> MoneyTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Total", "Amount", "Unit Price", "Subtotal"
    };

    /// <summary>Sheet types that carry a currency + USD representation (the financial records).</summary>
    private static readonly HashSet<SpreadsheetSheetType> FinancialTypes =
    [
        SpreadsheetSheetType.Revenue,
        SpreadsheetSheetType.Expenses,
        SpreadsheetSheetType.Payments,
        SpreadsheetSheetType.Invoices,
        SpreadsheetSheetType.PurchaseOrders
    ];

    /// <summary>
    /// Scans the financial, Tier 1 sheets of <paramref name="filePath"/> for in-cell currency.
    /// CSV files are not scanned here (single sheet, handled by the caller's CSV path if needed).
    /// </summary>
    public static CurrencyScanResult ScanWorkbook(string filePath, SpreadsheetAnalysisResult analysis)
    {
        var result = new CurrencyScanResult();
        if (filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return result;

        // Aggregate ambiguous symbols across sheets (symbol -> count) so the user is asked once.
        var ambiguousCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Read-share so the scan works even if the file is open in Excel (mirrors the importer).
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);

        foreach (var sheet in analysis.Sheets)
        {
            if (!sheet.IsIncluded
                || sheet.UnsupportedReason != null
                || sheet.Tier != ProcessingTier.Tier1_Mapping
                || !FinancialTypes.Contains(sheet.DetectedType))
                continue;

            if (!workbook.TryGetWorksheet(sheet.SourceSheetName, out var ws))
                continue;

            ScanSheet(ws, sheet, result, ambiguousCounts);
        }

        foreach (var (symbol, count) in ambiguousCounts)
        {
            result.Ambiguities.Add(new AmbiguousSymbolPrompt
            {
                Symbol = symbol,
                Candidates = CurrencyInfo.CandidatesForSymbol(symbol),
                RowCount = count
            });
        }

        return result;
    }

    /// <summary>
    /// Per-row currency scan for a single already-mapped table, used by the CSV import path (which
    /// ClosedXML's workbook scan doesn't cover). <paramref name="mappedHeaders"/> are the target
    /// column names; <paramref name="rows"/> are the data rows in import order, so the returned map is
    /// keyed by the same row index the importer uses. Resolves a "Currency" column code, an explicit
    /// in-cell code, an unambiguous symbol, or one disambiguated by its decimals. Ambiguous symbols
    /// (e.g. a bare "$") are left unresolved (the row imports in the company currency) because the CSV
    /// path has no symbol-resolution dialog.
    /// </summary>
    public static Dictionary<int, string> ScanRows(
        IReadOnlyList<string> mappedHeaders, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var resolved = new Dictionary<int, string>();

        var amountCols = new List<int>(); // 0-based column indices
        int currencyCol = -1;
        for (int i = 0; i < mappedHeaders.Count; i++)
        {
            if (MoneyTargets.Contains(mappedHeaders[i])) amountCols.Add(i);
            else if (string.Equals(mappedHeaders[i], "Currency", StringComparison.OrdinalIgnoreCase)) currencyCol = i;
        }
        if (amountCols.Count == 0 && currencyCol < 0)
            return resolved;

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            string? code = null;

            if (currencyCol >= 0 && currencyCol < row.Count
                && row[currencyCol]?.ToString() is { Length: > 0 } ctext
                && CurrencyCellDetector.Detect(ctext).Code is { } colCode)
                code = colCode;

            if (code == null)
            {
                foreach (var col in amountCols)
                {
                    if (col >= row.Count) continue;
                    var text = row[col]?.ToString();
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    var d = CurrencyCellDetector.Detect(text);
                    if (d.Code != null) { code = d.Code; break; }
                    if (d.AmbiguousSymbol != null)
                    {
                        var byFormat = DisambiguateByDecimals(d.AmbiguousSymbol, text);
                        if (byFormat != null) { code = byFormat; break; }
                    }
                }
            }

            if (code != null)
                resolved[r] = code;
        }

        return resolved;
    }

    private static void ScanSheet(
        IXLWorksheet ws, SheetAnalysis sheet, CurrencyScanResult result,
        Dictionary<string, int> ambiguousCounts)
    {
        var headerRow = SpreadsheetRowReader.FindHeaderRow(ws);
        var headers = SpreadsheetRowReader.GetHeaders(ws, headerRow);
        if (headers.Count == 0)
            return;

        // Rename headers to their mapped target so we can spot the amount columns by index.
        var mapped = new List<string>(headers);
        foreach (var m in sheet.ColumnMappings)
        {
            var idx = mapped.FindIndex(h => string.Equals(h, m.SourceColumn, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) mapped[idx] = m.TargetColumn;
        }

        var amountCols = new List<int>(); // 1-based worksheet column numbers
        for (int i = 0; i < mapped.Count; i++)
            if (MoneyTargets.Contains(mapped[i]))
                amountCols.Add(i + 1);

        // A dedicated per-row currency column (schema target "Currency") takes precedence over
        // in-cell symbols, so a sheet can carry clean numeric amounts plus an explicit ISO-code column.
        int currencyCol = 0; // 1-based; 0 = none
        for (int i = 0; i < mapped.Count; i++)
            if (string.Equals(mapped[i], "Currency", StringComparison.OrdinalIgnoreCase)) { currencyCol = i + 1; break; }

        if (amountCols.Count == 0 && currencyCol == 0)
            return;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        int ordinal = 0; // matches SpreadsheetRowReader.GetDataRows (empty rows skipped, not counted)

        for (int rowNum = headerRow + 1; rowNum <= lastRow; rowNum++)
        {
            var xlRow = ws.Row(rowNum);

            // GetDataRows skips rows where all of the first headers.Count cells are empty.
            bool allEmpty = true;
            for (int col = 1; col <= headers.Count; col++)
            {
                if (!xlRow.Cell(col).IsEmpty()) { allEmpty = false; break; }
            }
            if (allEmpty)
                continue;

            // A dedicated currency column with an explicit ISO code wins outright; otherwise the
            // first resolved code on any amount cell wins, else we remember an ambiguous symbol.
            string? resolved = null;
            string? pendingSymbol = null;

            if (currencyCol != 0)
            {
                var ctext = ReadFormatted(xlRow.Cell(currencyCol));
                if (!string.IsNullOrWhiteSpace(ctext) && CurrencyCellDetector.Detect(ctext).Code is { } colCode)
                    resolved = colCode;
            }

            if (resolved == null)
            {
                foreach (var col in amountCols)
                {
                    var text = ReadFormatted(xlRow.Cell(col));
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    var d = CurrencyCellDetector.Detect(text);
                    if (d.Code != null) { resolved = d.Code; break; }
                    if (d.AmbiguousSymbol != null)
                    {
                        // A symbol shared by currencies with different decimal conventions (e.g. "¥" =
                        // JPY with 0 decimals or CNY with 2) is resolved from the cell's own formatting
                        // when the displayed decimal count picks exactly one candidate, so it doesn't
                        // need to prompt and won't default to the wrong currency.
                        var byFormat = DisambiguateByDecimals(d.AmbiguousSymbol, text);
                        if (byFormat != null) { resolved = byFormat; break; }
                        pendingSymbol ??= d.AmbiguousSymbol;
                    }
                }
            }

            if (resolved != null)
            {
                AddResolved(result.Resolved, sheet.SourceSheetName, ordinal, resolved);
            }
            else if (pendingSymbol != null)
            {
                AddResolved(result.PendingAmbiguous, sheet.SourceSheetName, ordinal, pendingSymbol);
                ambiguousCounts[pendingSymbol] = ambiguousCounts.GetValueOrDefault(pendingSymbol) + 1;
            }

            ordinal++;
        }
    }

    /// <summary>
    /// After the user resolves each ambiguous symbol to a code, fills in the held rows so the
    /// per-row map is complete. <paramref name="symbolToCode"/> is "$" -> "CAD" etc.
    /// </summary>
    public static void ApplyResolution(CurrencyScanResult scan, IReadOnlyDictionary<string, string> symbolToCode)
    {
        foreach (var (sheetName, rows) in scan.PendingAmbiguous)
        {
            foreach (var (ordinal, symbol) in rows)
            {
                if (symbolToCode.TryGetValue(symbol, out var code) && !string.IsNullOrWhiteSpace(code))
                    AddResolved(scan.Resolved, sheetName, ordinal, code);
            }
        }
    }

    private static void AddResolved(Dictionary<string, Dictionary<int, string>> map, string sheet, int ordinal, string code)
    {
        if (!map.TryGetValue(sheet, out var rows))
        {
            rows = [];
            map[sheet] = rows;
        }
        rows[ordinal] = code;
    }

    /// <summary>
    /// Resolves an ambiguous currency symbol from the cell's displayed decimal count when that
    /// uniquely identifies one candidate (e.g. "¥95,000" -> JPY (0 dp), "¥1,200.00" -> CNY (2 dp)).
    /// Returns <c>null</c> when the symbol is unknown, has a single candidate, or more than one
    /// candidate shares the displayed decimal count (e.g. "$" -> USD/CAD/AUD are all 2 dp).
    /// </summary>
    private static string? DisambiguateByDecimals(string symbol, string formattedText)
    {
        var candidates = CurrencyInfo.CandidatesForSymbol(symbol);
        if (candidates.Count < 2)
            return null;

        var shown = DisplayedDecimalPlaces(formattedText);
        string? match = null;
        foreach (var code in candidates)
        {
            if (CurrencyInfo.GetByCode(code).DecimalPlaces != shown)
                continue;
            if (match != null)
                return null; // more than one candidate shares this decimal count
            match = code;
        }
        return match;
    }

    /// <summary>Counts the fractional digits shown in a formatted amount, handling both US
    /// ("¥1,200.00" -> 2) and European ("¥1.200,00" -> 2) grouping. The decimal separator is
    /// whichever of '.'/',' appears last; with only one separator present, a 3-digit run is
    /// treated as a thousands group ("¥95,000" -> 0) rather than a fraction.</summary>
    private static int DisplayedDecimalPlaces(string text)
    {
        var lastDot = text.LastIndexOf('.');
        var lastComma = text.LastIndexOf(',');
        var sep = Math.Max(lastDot, lastComma);
        if (sep < 0)
            return 0;

        var count = 0;
        for (var i = sep + 1; i < text.Length && char.IsDigit(text[i]); i++)
            count++;

        // When only one separator type is present we can't tell a thousands group from a
        // fraction; a run of exactly 3 digits is far more likely a thousands group.
        var oneSeparatorOnly = (lastDot < 0) ^ (lastComma < 0);
        if (oneSeparatorOnly && count == 3)
            return 0;

        return count;
    }

    /// <summary>
    /// Reads the cell's displayed text so currency carried in the NUMBER FORMAT (e.g. a numeric
    /// 100 shown as "£100.00") is visible, not just text like "£100". Falls back to the raw string.
    /// </summary>
    private static string ReadFormatted(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        try { return cell.GetFormattedString(); }
        catch { return cell.GetString(); }
    }
}
