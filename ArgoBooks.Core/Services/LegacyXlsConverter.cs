using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Converts a legacy Excel <c>.xls</c> (BIFF8/HSSF) workbook into an equivalent
/// <c>.xlsx</c> file written to a temporary path.
///
/// <para>
/// This is a deliberately self-contained shim so the rest of the import pipeline
/// (analysis, mapping, import) only ever sees <c>.xlsx</c>/<c>.csv</c> and remains
/// completely unchanged. We read the old BIFF format with NPOI (the only mature
/// .NET reader for it) and re-emit a modern workbook with ClosedXML, which the
/// pipeline already depends on.
/// </para>
///
/// <para>
/// Cell values are preserved with their types where it matters: strings, numerics
/// (date-formatted numerics are written as dates), booleans, and formula results
/// (we write the cached/evaluated value, not the formula text, so downstream reads
/// see a concrete value). Blank cells and ragged rows are tolerated.
/// </para>
/// </summary>
public static class LegacyXlsConverter
{
    private static readonly byte[] BinaryWorkbookSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Opens the given <c>.xls</c> file, copies every sheet/row/cell into a new
    /// <c>.xlsx</c> workbook, writes it to a fresh temp file and returns that path.
    /// A file named <c>.xls</c> that is really an HTML page or an <c>.xlsx</c> is read as what it is.
    /// </summary>
    /// <param name="xlsPath">Path to the source legacy <c>.xls</c> file.</param>
    /// <returns>The path to the newly written temporary <c>.xlsx</c> file.</returns>
    public static string ConvertXlsToTempXlsx(string xlsPath)
    {
        if (string.IsNullOrWhiteSpace(xlsPath))
            throw new ArgumentException("Path must not be empty.", nameof(xlsPath));

        // FileShare.ReadWrite so a .xls still converts/imports while it's open in Excel.
        byte[] content;
        using (var inStream = new FileStream(xlsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var buffer = new MemoryStream())
        {
            inStream.CopyTo(buffer);
            content = buffer.ToArray();
        }

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"argo-xls-{Guid.NewGuid():N}.xlsx");

        if (content.AsSpan().StartsWith(ZipSignature))
        {
            File.WriteAllBytes(tempPath, content);
            return tempPath;
        }

        using var output = !content.AsSpan().StartsWith(BinaryWorkbookSignature) && HtmlTableReader.LooksLikeHtmlTable(content)
            ? FromHtml(content)
            : FromBinaryWorkbook(content);

        output.SaveAs(tempPath);
        return tempPath;
    }

    private static XLWorkbook FromHtml(byte[] content)
    {
        var output = new XLWorkbook();
        var sheet = output.Worksheets.Add("Sheet1");

        var rows = HtmlTableReader.ReadRows(content);
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                if (rows[r][c].Length > 0)
                    sheet.Cell(r + 1, c + 1).Value = rows[r][c];
            }
        }

        return output;
    }

    private static XLWorkbook FromBinaryWorkbook(byte[] content)
    {
        using var source = new HSSFWorkbook(new MemoryStream(content));

        var output = new XLWorkbook();

        for (int s = 0; s < source.NumberOfSheets; s++)
        {
            var srcSheet = source.GetSheetAt(s);

            // ClosedXML rejects empty/duplicate names; fall back to a positional name.
            var sheetName = ExcelSheetNames.CleanAndUnique(output, srcSheet.SheetName, $"Sheet{s + 1}");
            var dstSheet = output.Worksheets.Add(sheetName);

            // Rows can be sparse; iterate by physical index range and skip nulls.
            for (int r = srcSheet.FirstRowNum; r <= srcSheet.LastRowNum; r++)
            {
                var srcRow = srcSheet.GetRow(r);
                if (srcRow == null)
                    continue;

                for (int c = srcRow.FirstCellNum; c < srcRow.LastCellNum; c++)
                {
                    if (c < 0)
                        continue;

                    var srcCell = srcRow.GetCell(c);
                    if (srcCell == null)
                        continue;

                    // ClosedXML is 1-based; NPOI is 0-based.
                    var dstCell = dstSheet.Cell(r + 1, c + 1);
                    CopyCellValue(srcCell, dstCell);
                }
            }
        }

        return output;
    }

    private static void CopyCellValue(ICell srcCell, IXLCell dstCell)
    {
        var type = srcCell.CellType;

        // For formulas, resolve to the cached result type so we write a value,
        // not the formula text.
        if (type == CellType.Formula)
            type = srcCell.CachedFormulaResultType;

        switch (type)
        {
            case CellType.Numeric:
                if (DateUtil.IsCellDateFormatted(srcCell))
                {
                    // Preserve the value as a true date so downstream date parsing works.
                    dstCell.Value = srcCell.DateCellValue ?? default(DateTime);
                    dstCell.Style.NumberFormat.Format = "yyyy-mm-dd";
                }
                else
                {
                    dstCell.Value = srcCell.NumericCellValue;
                }
                break;

            case CellType.Boolean:
                dstCell.Value = srcCell.BooleanCellValue;
                break;

            case CellType.String:
                dstCell.Value = srcCell.StringCellValue ?? string.Empty;
                break;

            case CellType.Blank:
            case CellType.Error:
            default:
                // Leave the destination cell empty. We deliberately do not try to
                // reproduce error values; a blank is the safest neutral default.
                break;
        }
    }
}
