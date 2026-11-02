using ClosedXML.Excel;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Rewrites a QuickBooks Open Invoices report as a plain table. The report names each customer on a
/// heading row above their invoices, with "Total for" rows closing each group and sub-customers nested
/// inside their parent, and only following that nesting says whose invoice each row is.
/// </summary>
public static class QuickBooksReportFlattener
{
    private const string TotalPrefix = "Total for ";

    /// <summary>
    /// Writes the plain table into <paramref name="outputFolder"/> under the report's own file name and
    /// returns its path, or null when the file is not an Open Invoices report in this layout.
    /// </summary>
    public static string? FlattenOpenInvoices(string path, string outputFolder)
    {
        if (!path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return null;

        using var source = new XLWorkbook(path);
        var sheet = source.Worksheets.First();
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;

        var headerRow = Enumerable.Range(1, lastRow).FirstOrDefault(r => IsHeader(sheet, r, lastColumn));
        if (headerRow == 0)
            return null;

        var groups = new List<string>();
        var invoices = new List<(string Customer, List<XLCellValue> Cells)>();
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var first = sheet.Cell(row, 1).GetString().Trim();
            var restEmpty = Enumerable.Range(2, lastColumn - 1).All(c => sheet.Cell(row, c).IsEmpty());

            if (first.StartsWith(TotalPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (groups.Count > 0)
                    groups.RemoveAt(groups.Count - 1);
            }
            else if (first.Equals("TOTAL", StringComparison.OrdinalIgnoreCase))
                break;
            else if (first.Length > 0 && restEmpty)
                groups.Add(first);
            else if (first.Length == 0 && !restEmpty && groups.Count > 0)
                invoices.Add((string.Join(":", groups), Enumerable.Range(2, lastColumn - 1).Select(c => sheet.Cell(row, c).Value).ToList()));
        }

        if (invoices.Count == 0)
            return null;

        using var output = new XLWorkbook();
        var table = output.AddWorksheet("Open Invoices");
        table.Cell(1, 1).Value = "Customer";
        for (var c = 2; c <= lastColumn; c++)
            table.Cell(1, c).Value = sheet.Cell(headerRow, c).GetString();
        for (var n = 0; n < invoices.Count; n++)
        {
            table.Cell(n + 2, 1).Value = invoices[n].Customer;
            for (var c = 0; c < invoices[n].Cells.Count; c++)
                table.Cell(n + 2, c + 2).Value = invoices[n].Cells[c];
        }

        Directory.CreateDirectory(outputFolder);
        var written = Path.Combine(outputFolder, Path.GetFileName(path));
        output.SaveAs(written);
        return written;
    }

    private static bool IsHeader(IXLWorksheet sheet, int row, int lastColumn)
    {
        var cells = Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(row, c).GetString().Trim()).ToList();
        return cells.Contains("Open balance", StringComparer.OrdinalIgnoreCase) && cells.Contains("Num", StringComparer.OrdinalIgnoreCase);
    }
}
