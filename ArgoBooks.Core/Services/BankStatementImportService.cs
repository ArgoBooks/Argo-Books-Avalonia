using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Telemetry;
using ClosedXML.Excel;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Parses a bank statement spreadsheet/CSV into a list of <see cref="BankStatementLine"/>.
/// Local header heuristics handle the common case; when they cannot find the essential columns,
/// the caller falls back to the AI-mapped overloads (which apply the smart importer's column
/// mappings first). Never commits anything to CompanyData; produces reference data only.
/// </summary>
public class BankStatementImportService(IErrorLogger? errorLogger = null)
{
    // Canonical column names the row reader looks up after the headers are normalised.
    private const string ColDate = "Date";
    private const string ColDescription = "Description";
    private const string ColAmount = "Amount";
    private const string ColDebit = "Debit";
    private const string ColCredit = "Credit";
    private const string ColBalance = "Balance";
    private const string ColReference = "Reference";

    #region Local (header-heuristics) parsing

    /// <summary>Parses an Excel bank statement using local header detection.</summary>
    public Task<List<BankStatementLine>> ParseExcelAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => ParseExcelCore(filePath, MapBankHeaders, requireEssentials: true, cancellationToken), cancellationToken);

    /// <summary>Parses a CSV bank statement using local header detection (tolerates preamble rows).</summary>
    public async Task<List<BankStatementLine>> ParseCsvAsync(string filePath, CancellationToken cancellationToken = default) =>
        await ParseCsvCore(filePath, detectHeaderRow: true, MapBankHeaders, requireEssentials: true, cancellationToken);

    #endregion

    #region AI-mapped parsing (backup)

    /// <summary>
    /// Parses an Excel bank statement using the smart importer's AI column mappings, then normalises
    /// to canonical bank columns. Used as a backup when local detection finds no usable columns.
    /// </summary>
    public Task<List<BankStatementLine>> ParseExcelWithAnalysisAsync(string filePath, SheetAnalysis analysis, CancellationToken cancellationToken = default) =>
        Task.Run(() => ParseExcelCore(filePath, headers =>
        {
            SpreadsheetImportService.ApplyColumnMapping(headers, analysis);
            MapBankHeaders(headers);
        }, requireEssentials: false, cancellationToken), cancellationToken);

    /// <summary>
    /// Parses a CSV bank statement using the smart importer's AI column mappings, then normalises to
    /// canonical bank columns. The analysis was produced against the first row, so the first row is
    /// treated as the header here.
    /// </summary>
    public async Task<List<BankStatementLine>> ParseCsvWithAnalysisAsync(string filePath, SheetAnalysis analysis, CancellationToken cancellationToken = default) =>
        await ParseCsvCore(filePath, detectHeaderRow: false, headers =>
        {
            SpreadsheetImportService.ApplyColumnMapping(headers, analysis);
            MapBankHeaders(headers);
        }, requireEssentials: false, cancellationToken);

    #endregion

    private List<BankStatementLine> ParseExcelCore(string filePath, Action<List<string>> normalize, bool requireEssentials, CancellationToken cancellationToken)
    {
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(fileStream);

            var worksheet = workbook.Worksheets.FirstOrDefault(w => SpreadsheetRowReader.GetHeaders(w).Count > 0)
                            ?? workbook.Worksheets.FirstOrDefault();
            if (worksheet == null) return [];

            // When essentials are required (a bank statement), scan for the row that actually yields a
            // Date + money column rather than the first row with >= 2 cells. Many banks put metadata
            // rows ("Account:", "Statement Period:") above the real header; without this the preamble
            // would be mistaken for the header and the whole statement would import as zero lines.
            // The CSV path does the same via FindHeaderRowIndex.
            var headerRow = requireEssentials
                ? FindExcelHeaderRow(worksheet, normalize)
                : SpreadsheetRowReader.FindHeaderRow(worksheet);

            var headers = SpreadsheetRowReader.GetHeaders(worksheet, headerRow);
            if (headers.Count == 0) return [];

            var rows = SpreadsheetRowReader.GetDataRows(worksheet, headers.Count, headerRow);
            normalize(headers);
            if (requireEssentials && !HasEssentialColumns(headers)) return [];

            return BuildLines(headers, rows, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errorLogger?.LogError(ex, ErrorCategory.Import, $"Failed to parse bank statement: {Path.GetFileName(filePath)}");
            return [];
        }
    }

    private async Task<List<BankStatementLine>> ParseCsvCore(string filePath, bool detectHeaderRow, Action<List<string>> normalize, bool requireEssentials, CancellationToken cancellationToken)
    {
        try
        {
            // FileShare.ReadWrite so a statement still imports while it's open in Excel/LibreOffice.
            var collected = new List<string>();
            await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                    collected.Add(line);
            }
            var lines = collected.ToArray();
            if (lines.Length < 2) return [];

            var delimiter = SpreadsheetAnalysisService.DetectCsvDelimiter(lines[0]);
            var parsed = lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => SpreadsheetAnalysisService.ParseCsvLine(l, delimiter))
                .ToList();
            if (parsed.Count < 2) return [];

            var headerIndex = detectHeaderRow ? FindHeaderRowIndex(parsed) : 0;
            var headers = parsed[headerIndex];
            normalize(headers);
            if (requireEssentials && !HasEssentialColumns(headers)) return [];

            var rows = parsed.Skip(headerIndex + 1).Select(r => r.Cast<object?>().ToList()).ToList();
            return BuildLines(headers, rows, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errorLogger?.LogError(ex, ErrorCategory.Import, $"Failed to parse bank statement CSV: {Path.GetFileName(filePath)}");
            return [];
        }
    }

    /// <summary>A statement is usable only if it has a date and at least one money column.</summary>
    private static bool HasEssentialColumns(List<string> headers) =>
        headers.Contains(ColDate) && (headers.Contains(ColAmount) || headers.Contains(ColDebit) || headers.Contains(ColCredit));

    /// <summary>
    /// Finds the row most likely to be the header by scanning the first rows for one that yields a
    /// Date column plus at least one money column. Falls back to the first row.
    /// </summary>
    private static int FindHeaderRowIndex(List<List<string>> rows)
    {
        var limit = Math.Min(rows.Count, 10);
        for (int i = 0; i < limit; i++)
        {
            var copy = new List<string>(rows[i]);
            MapBankHeaders(copy);
            if (HasEssentialColumns(copy))
                return i;
        }
        return 0;
    }

    /// <summary>
    /// Excel counterpart of <see cref="FindHeaderRowIndex"/>: scans the first rows for the one whose
    /// mapped headers yield a Date plus a money column, so metadata/preamble rows above the real
    /// header don't get mistaken for it. Falls back to the generic first-non-empty-row heuristic.
    /// </summary>
    private static int FindExcelHeaderRow(IXLWorksheet worksheet, Action<List<string>> normalize)
    {
        var lastRow = Math.Min(worksheet.LastRowUsed()?.RowNumber() ?? 1, 10);
        for (int rowNum = 1; rowNum <= lastRow; rowNum++)
        {
            var copy = new List<string>(SpreadsheetRowReader.GetHeaders(worksheet, rowNum));
            normalize(copy);
            if (HasEssentialColumns(copy))
                return rowNum;
        }
        return SpreadsheetRowReader.FindHeaderRow(worksheet);
    }

    /// <summary>
    /// Renames recognised bank-statement headers to canonical names in place, using keyword
    /// heuristics. More specific columns are claimed before generic ones (e.g. Balance and
    /// Debit/Credit before Amount) so each source header is mapped at most once.
    /// </summary>
    internal static void MapBankHeaders(List<string> headers)
    {
        var used = new HashSet<int>();

        int Pick(Func<string, bool> predicate)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (predicate(headers[i].Trim().ToLowerInvariant())) { used.Add(i); return i; }
            }
            return -1;
        }

        void Assign(string canonical, Func<string, bool> predicate)
        {
            var i = Pick(predicate);
            if (i >= 0) headers[i] = canonical;
        }

        Assign(ColDate, h => h.Contains("date") || h.Contains("posted"));
        Assign(ColDebit, h => h.Contains("debit") || h.Contains("withdraw") || h.Contains("paid out") || h.Contains("money out"));
        Assign(ColCredit, h => h.Contains("credit") || h.Contains("deposit") || h.Contains("paid in") || h.Contains("money in"));
        Assign(ColBalance, h => h.Contains("balance") || h.Contains("bal"));
        Assign(ColAmount, h => h is "amount" or "value" or "amt" || (h.Contains("amount") && !h.Contains("tax")));
        Assign(ColReference, h => h.Contains("reference") || h is "ref" || h.Contains("ref no") || h.Contains("cheque") || h.Contains("check") || h.Contains("trans id") || h.Contains("transaction id"));
        Assign(ColDescription, h => h.Contains("desc") || h.Contains("memo") || h.Contains("narrative") || h.Contains("detail") || h.Contains("particular") || h.Contains("payee") || h.Contains("name") || h.Contains("transaction"));
    }

    private static List<BankStatementLine> BuildLines(List<string> headers, List<List<object?>> rows, CancellationToken cancellationToken)
    {
        var result = new List<BankStatementLine>(rows.Count);

        // Decided for the whole column: parsing each row on its own read 05/03 as 3 May and 15/03
        // as 15 March in the same UK/EU file.
        var dateOrder = SpreadsheetRowReader.DetectDateOrder(rows, headers, ColDate);

        for (int i = 0; i < rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[i];

            var date = SpreadsheetRowReader.GetDateTime(row, headers, ColDate, dateOrder);
            var description = SpreadsheetRowReader.GetString(row, headers, ColDescription);
            var debit = SpreadsheetRowReader.GetNullableDecimal(row, headers, ColDebit);
            var credit = SpreadsheetRowReader.GetNullableDecimal(row, headers, ColCredit);
            var signedAmount = SpreadsheetRowReader.GetNullableDecimal(row, headers, ColAmount);
            var balance = SpreadsheetRowReader.GetNullableDecimal(row, headers, ColBalance);
            var reference = SpreadsheetRowReader.GetString(row, headers, ColReference);

            // Skip rows with neither a date nor any monetary value (e.g. blank/summary rows).
            if (date == DateTime.MinValue && signedAmount is null && debit is null && credit is null)
                continue;

            // Canonical signed amount: negative = money out, positive = money in.
            // Prefer an explicit signed Amount column; otherwise derive from Credit - Debit.
            var amount = signedAmount ?? ((credit ?? 0m) - (debit ?? 0m));

            result.Add(new BankStatementLine
            {
                Id = Guid.NewGuid().ToString("N"),
                Date = date,
                Description = description,
                Amount = amount,
                Debit = debit,
                Credit = credit,
                Balance = balance,
                RawReference = reference,
                MatchStatus = BankLineMatchStatus.Unmatched,
                SourceRowIndex = i
            });
        }

        return result;
    }
}
