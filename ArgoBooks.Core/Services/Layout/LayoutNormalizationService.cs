using ArgoBooks.Core.Models.Telemetry;
using ClosedXML.Excel;

namespace ArgoBooks.Core.Services.Layout;

/// <summary>
/// Normalizes a workbook's <i>layout</i> before it enters the rest of the import
/// pipeline (analysis and import). Messy sheets (long preambles, merged/multi-row
/// headers, cross-tabs, stacked tables) are rewritten into clean single-header-row
/// tables; clean sheets are left untouched.
///
/// <para>
/// The integration uses the same safe "rewrite to a temp xlsx" pattern as
/// <see cref="LegacyXlsConverter"/>: the rest of the pipeline only ever sees a
/// normal <c>.xlsx</c> and remains completely unchanged. We open the source
/// workbook, decide per sheet whether the cheap local <see cref="LayoutGate"/>
/// flags it as messy, and:
/// </para>
/// <list type="bullet">
///   <item>If <b>no</b> sheet needs interpretation, we return the ORIGINAL path
///   unchanged. Clean files pay zero added cost and produce no temp file.</item>
///   <item>If <b>any</b> sheet needs interpretation, we write a fresh temp workbook:
///   clean sheets are copied faithfully (values + types preserved); messy sheets are
///   replaced with the AI-interpreted clean table.</item>
/// </list>
///
/// <para>
/// <b>No silent data loss.</b> A failed or empty AI descriptor (or any per-sheet
/// error) NEVER drops a sheet: we fall back to copying the original sheet as-is. If
/// the whole operation fails, we degrade to returning the original path.
/// </para>
///
/// <para>
/// This is Phase 3, Task 5 part A (the pipeline integration). There is intentionally
/// no feature flag, settings, or UI here; that is part B. This service does not wire
/// itself into the import flow.
/// </para>
/// </summary>
public sealed class LayoutNormalizationService(IGeminiService geminiService, IErrorLogger? errorLogger = null)
{
    private readonly SpreadsheetLayoutService _layoutService = new(geminiService, errorLogger);

    /// <summary>
    /// Examines every worksheet in the workbook at <paramref name="xlsxPath"/> and, if
    /// at least one sheet needs AI layout interpretation, writes a normalized copy to a
    /// fresh temp <c>.xlsx</c> and returns its path. If no sheet needs interpretation
    /// (or the whole operation fails), returns the original <paramref name="xlsxPath"/>
    /// unchanged.
    /// </summary>
    /// <param name="xlsxPath">Path to the source <c>.xlsx</c> workbook.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The original path when nothing needs interpreting; otherwise the path to a new
    /// temp <c>.xlsx</c> with messy sheets rewritten to clean tables.
    /// </returns>
    public async Task<string> NormalizeAsync(string xlsxPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(xlsxPath))
            return xlsxPath;

        // Best-effort sweep of temp files left by previous imports so they don't accumulate.
        // Only files older than an hour are removed, so an in-flight (or concurrent) run's file
        // is never deleted out from under it.
        CleanupStaleTempFiles();

        try
        {
            // First pass: open the source and decide which sheets (if any) are messy.
            // We materialize the grids up front so we can both decide and reuse them.
            List<(IXLWorksheet Sheet, SheetGrid Grid, bool NeedsInterpretation)> sheets;
            using (var probe = new XLWorkbook(xlsxPath))
            {
                sheets = new List<(IXLWorksheet, SheetGrid, bool)>();
                foreach (var ws in probe.Worksheets)
                {
                    SheetGrid grid;
                    bool needs;
                    try
                    {
                        grid = SheetGrid.FromWorksheet(ws);
                        needs = LayoutGate.NeedsInterpretation(grid);
                    }
                    catch (Exception ex)
                    {
                        // A bad sheet must never abort the run; treat it as "clean" so it
                        // is copied through faithfully later.
                        errorLogger?.LogError(ex, ErrorCategory.Import,
                            $"Layout gate failed for sheet '{ws.Name}'; treating as clean");
                        grid = SheetGrid.FromWorksheet(ws);
                        needs = false;
                    }

                    sheets.Add((ws, grid, needs));
                }
            }

            // Fast path: nothing is messy -> return the original file unchanged. No temp
            // file, no LLM call. Clean workbooks stay on the existing path at zero cost.
            if (sheets.Count == 0 || sheets.All(s => !s.NeedsInterpretation))
                return xlsxPath;

            // At least one sheet is messy. Re-open the source (a fresh workbook so we can
            // use ClosedXML's cross-workbook CopyTo) and build the normalized temp workbook.
            using var source = new XLWorkbook(xlsxPath);
            using var output = new XLWorkbook();

            // Re-pair the source worksheets (from the re-opened workbook) with the
            // decisions we made, by name. Worksheet order/names are stable across opens.
            var sourceSheets = source.Worksheets.ToList();
            for (int i = 0; i < sourceSheets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var srcSheet = sourceSheets[i];
                // Match by index (worksheets enumerate in the same order); fall back to name.
                var decision = i < sheets.Count
                    ? sheets[i]
                    : sheets.FirstOrDefault(s => s.Sheet.Name == srcSheet.Name);

                bool needs = decision.Grid is not null && decision.NeedsInterpretation;
                var grid = decision.Grid ?? SheetGrid.FromWorksheet(srcSheet);

                if (!needs)
                {
                    CopySheetFaithfully(srcSheet, output);
                    continue;
                }

                await WriteInterpretedSheetAsync(srcSheet, grid, output, ct);
            }

            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"argo-layout-{Guid.NewGuid():N}.xlsx");

            output.SaveAs(tempPath);
            return tempPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Whole-operation failure: degrade to "return original path" so the existing
            // pipeline still runs on the unmodified file. Never lose the import.
            errorLogger?.LogError(ex, ErrorCategory.Import,
                "Layout normalization failed; returning original path unchanged");
            return xlsxPath;
        }
    }

    /// <summary>Deletes layout temp files older than an hour, ignoring any that can't be removed.</summary>
    private static void CleanupStaleTempFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);
            foreach (var file in Directory.EnumerateFiles(Path.GetTempPath(), "argo-layout-*.xlsx"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch { /* file in use or already gone; ignore */ }
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Writes an AI-interpreted clean table for <paramref name="srcSheet"/> into
    /// <paramref name="output"/>. If interpretation yields no usable table (null
    /// descriptor or no tables), falls back to copying the original sheet faithfully so
    /// the sheet is never dropped.
    /// </summary>
    private async Task WriteInterpretedSheetAsync(
        IXLWorksheet srcSheet, SheetGrid grid, XLWorkbook output, CancellationToken ct)
    {
        try
        {
            var descriptor = await _layoutService.GetLayoutDescriptorAsync(grid, ct);

            if (descriptor is null || descriptor.Tables.Count == 0)
            {
                // AI failed or found no table -> fall back, do NOT lose the sheet.
                errorLogger?.LogWarning(
                    $"No usable layout descriptor for sheet '{srcSheet.Name}'; copying as-is",
                    nameof(LayoutNormalizationService));
                CopySheetFaithfully(srcSheet, output);
                return;
            }

            // v1: one table per sheet — use the first region.
            var (headers, rows) = GridExtractor.Extract(grid, descriptor.Tables[0]);

            if (rows.Count == 0)
            {
                // No data rows survived extraction (e.g. a header-only result, or a wide region
                // whose key columns were missing so every row was skipped). A header-only table is
                // useless for import, so fall back to a faithful copy rather than emit a blank sheet
                // and silently lose the data.
                errorLogger?.LogWarning(
                    $"Layout extraction produced an empty table for sheet '{srcSheet.Name}'; copying as-is",
                    nameof(LayoutNormalizationService));
                CopySheetFaithfully(srcSheet, output);
                return;
            }

            var target = output.Worksheets.Add(UniqueSheetName(srcSheet.Name, output));

            // Row 1: clean headers.
            for (int c = 0; c < headers.Count; c++)
                target.Cell(1, c + 1).Value = headers[c];

            // Subsequent rows: extracted data as strings (the downstream pipeline parses
            // strings into the appropriate types from here, same as a normal import).
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                for (int c = 0; c < row.Count; c++)
                    target.Cell(r + 2, c + 1).Value = row[c];
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Any per-sheet error must fall back to a faithful copy, never drop the sheet.
            errorLogger?.LogError(ex, ErrorCategory.Import,
                $"Interpreting sheet '{srcSheet.Name}' failed; copying as-is");
            try
            {
                CopySheetFaithfully(srcSheet, output);
            }
            catch (Exception copyEx)
            {
                errorLogger?.LogError(copyEx, ErrorCategory.Import,
                    $"Fallback copy of sheet '{srcSheet.Name}' also failed; sheet skipped");
            }
        }
    }

    /// <summary>
    /// Copies <paramref name="srcSheet"/> into <paramref name="output"/> preserving cell
    /// values and types. Prefers ClosedXML's cross-workbook <see cref="IXLWorksheet.CopyTo(XLWorkbook, string)"/>
    /// (which preserves values, data types, and styles); on failure, falls back to a
    /// manual value+type copy so dates/numbers/strings survive.
    /// </summary>
    private void CopySheetFaithfully(IXLWorksheet srcSheet, XLWorkbook output)
    {
        var name = UniqueSheetName(srcSheet.Name, output);
        try
        {
            srcSheet.CopyTo(output, name);
        }
        catch (Exception ex)
        {
            errorLogger?.LogWarning(
                $"CopyTo failed for sheet '{srcSheet.Name}' ({ex.Message}); using manual value copy",
                nameof(LayoutNormalizationService));
            CopySheetValuesManually(srcSheet, output, name);
        }
    }

    /// <summary>
    /// Manual fallback copy: writes each used cell's value into a new sheet, preserving
    /// the <see cref="XLDataType"/> so dates remain dates and numbers remain numbers.
    /// </summary>
    private static void CopySheetValuesManually(IXLWorksheet srcSheet, XLWorkbook output, string targetName)
    {
        var target = output.Worksheets.Add(targetName);

        var lastRow = srcSheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = srcSheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0 || lastCol == 0)
            return;

        for (int r = 1; r <= lastRow; r++)
        {
            for (int c = 1; c <= lastCol; c++)
            {
                var src = srcSheet.Cell(r, c);
                if (src.IsEmpty())
                    continue;

                var dst = target.Cell(r, c);
                switch (src.DataType)
                {
                    case XLDataType.DateTime:
                        dst.Value = src.GetDateTime();
                        dst.Style.NumberFormat.Format = src.Style.NumberFormat.Format;
                        break;
                    case XLDataType.Number:
                        dst.Value = src.GetDouble();
                        break;
                    case XLDataType.Boolean:
                        dst.Value = src.GetBoolean();
                        break;
                    default:
                        dst.Value = src.GetString();
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Returns a sheet name unique within <paramref name="workbook"/>, honoring Excel's
    /// 31-char cap and duplicate-name constraints.
    /// </summary>
    private static string UniqueSheetName(string? rawName, XLWorkbook workbook)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? "Sheet1" : rawName.Trim();

        foreach (var ch in new[] { '\\', '/', '*', '?', ':', '[', ']' })
            name = name.Replace(ch, '_');

        if (name.Length > 31)
            name = name[..31];

        var candidate = name;
        var suffix = 1;
        while (workbook.Worksheets.Any(w => string.Equals(w.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            var tail = $"_{suffix++}";
            candidate = name.Length + tail.Length > 31
                ? name[..(31 - tail.Length)] + tail
                : name + tail;
        }

        return candidate;
    }
}
