using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Converts an OpenDocument spreadsheet (<c>.ods</c>, as LibreOffice saves and Google Sheets
/// downloads) into an <c>.xlsx</c> written to a temporary path, so the import pipeline only ever
/// reads <c>.xlsx</c>, as <see cref="LegacyXlsConverter"/> does for <c>.xls</c>.
/// </summary>
public static class OdsConverter
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    /// <summary>Repeats past this many are the empty filler LibreOffice writes out to the sheet's edge.</summary>
    private const int MaxRepeat = 1000;

    public static string ConvertOdsToTempXlsx(string odsPath)
    {
        XDocument content;
        using (var stream = new FileStream(odsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            var entry = zip.GetEntry("content.xml") ?? throw new InvalidDataException("The file is not an OpenDocument spreadsheet.");
            using var entryStream = entry.Open();
            content = XDocument.Load(entryStream);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"argo-ods-{Guid.NewGuid():N}.xlsx");
        using var output = new XLWorkbook();

        var tables = content.Descendants(Table + "table").ToList();
        for (var t = 0; t < tables.Count; t++)
        {
            var sheet = output.Worksheets.Add(ExcelSheetNames.CleanAndUnique(output, (string?)tables[t].Attribute(Table + "name"), $"Sheet{t + 1}"));
            CopyTable(tables[t], sheet);
        }

        if (tables.Count == 0)
            output.Worksheets.Add("Sheet1");

        output.SaveAs(tempPath);
        return tempPath;
    }

    private static void CopyTable(XElement table, IXLWorksheet sheet)
    {
        var rowNumber = 1;
        foreach (var row in table.Descendants(Table + "table-row"))
        {
            var cells = row.Elements().Where(e => e.Name == Table + "table-cell" || e.Name == Table + "covered-table-cell").ToList();
            var rowRepeat = Repeat(row, "number-rows-repeated");

            if (cells.All(c => ReadValue(c) is null))
            {
                rowNumber += rowRepeat;
                continue;
            }

            for (var copy = 0; copy < Math.Min(rowRepeat, MaxRepeat); copy++)
            {
                var columnNumber = 1;
                foreach (var cell in cells)
                {
                    var value = ReadValue(cell);
                    var cellRepeat = Repeat(cell, "number-columns-repeated");
                    if (value is { } v)
                    {
                        for (var c = 0; c < Math.Min(cellRepeat, MaxRepeat); c++)
                            Write(sheet.Cell(rowNumber, columnNumber + c), v);
                    }
                    columnNumber += cellRepeat;
                }
                rowNumber++;
            }
        }
    }

    private static int Repeat(XElement element, string attribute) =>
        int.TryParse((string?)element.Attribute(Table + attribute), out var n) && n > 1 ? n : 1;

    /// <summary>The cell's value by its declared type, or null for an empty cell.</summary>
    private static object? ReadValue(XElement cell)
    {
        var type = (string?)cell.Attribute(Office + "value-type");
        switch (type)
        {
            case "float" or "percentage" or "currency":
                return double.TryParse((string?)cell.Attribute(Office + "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : CellText(cell);
            case "date":
                return DateTime.TryParse((string?)cell.Attribute(Office + "date-value"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : CellText(cell);
            case "boolean":
                return string.Equals((string?)cell.Attribute(Office + "boolean-value"), "true", StringComparison.OrdinalIgnoreCase);
            default:
                var text = CellText(cell);
                return text.Length > 0 ? text : null;
        }
    }

    /// <summary>The cell's paragraphs joined by line breaks, with OpenDocument's space, tab and line-break markers put back.</summary>
    private static string CellText(XElement cell)
    {
        var text = new StringBuilder();
        foreach (var paragraph in cell.Elements(Text + "p"))
        {
            if (text.Length > 0) text.Append('\n');
            AppendText(paragraph, text);
        }
        return text.ToString().Trim();
    }

    private static void AppendText(XElement element, StringBuilder text)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText plain)
                text.Append(plain.Value);
            else if (node is XElement child)
            {
                if (child.Name == Text + "s")
                    text.Append(' ', int.TryParse((string?)child.Attribute(Text + "c"), out var spaces) ? spaces : 1);
                else if (child.Name == Text + "tab")
                    text.Append('\t');
                else if (child.Name == Text + "line-break")
                    text.Append('\n');
                else
                    AppendText(child, text);
            }
        }
    }

    private static void Write(IXLCell cell, object value)
    {
        switch (value)
        {
            case double number:
                cell.Value = number;
                break;
            case DateTime date:
                cell.Value = date;
                cell.Style.NumberFormat.Format = "yyyy-mm-dd";
                break;
            case bool flag:
                cell.Value = flag;
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }
}
