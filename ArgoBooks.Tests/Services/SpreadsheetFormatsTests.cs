using System.IO.Compression;
using System.Text;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class SpreadsheetFormatsTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files)
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    private string TempPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"formats-{Guid.NewGuid():N}{extension}");
        _files.Add(path);
        return path;
    }

    /// <summary>An .ods holding the given tables, laid out as LibreOffice writes one.</summary>
    internal static void WriteOds(string path, string tables)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var mimetype = new StreamWriter(zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open()))
            mimetype.Write("application/vnd.oasis.opendocument.spreadsheet");
        using (var content = new StreamWriter(zip.CreateEntry("content.xml").Open(), new UTF8Encoding(false)))
            content.Write($"""
                <?xml version="1.0" encoding="UTF-8"?>
                <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                    xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
                    xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" office:version="1.3">
                  <office:body><office:spreadsheet>{tables}</office:spreadsheet></office:body>
                </office:document-content>
                """);
    }

    private XLWorkbook ConvertOds(string tables)
    {
        var ods = TempPath(".ods");
        WriteOds(ods, tables);
        var xlsx = OdsConverter.ConvertOdsToTempXlsx(ods);
        _files.Add(xlsx);
        return new XLWorkbook(xlsx);
    }

    [Fact]
    public void AnOdsCell_KeepsItsType()
    {
        using var workbook = ConvertOds("""
            <table:table table:name="Expenses">
              <table:table-row>
                <table:table-cell office:value-type="string"><text:p>Date</text:p></table:table-cell>
                <table:table-cell office:value-type="string"><text:p>Amount</text:p></table:table-cell>
                <table:table-cell office:value-type="string"><text:p>Paid</text:p></table:table-cell>
              </table:table-row>
              <table:table-row>
                <table:table-cell office:value-type="date" office:date-value="2025-05-06"><text:p>06/05/2025</text:p></table:table-cell>
                <table:table-cell office:value-type="currency" office:currency="CAD" office:value="-1284.9"><text:p>-$1,284.90</text:p></table:table-cell>
                <table:table-cell office:value-type="boolean" office:boolean-value="true"><text:p>TRUE</text:p></table:table-cell>
              </table:table-row>
            </table:table>
            """);

        var sheet = workbook.Worksheet("Expenses");
        Assert.Equal(new DateTime(2025, 5, 6), sheet.Cell(2, 1).GetDateTime());
        Assert.Equal(-1284.9, sheet.Cell(2, 2).GetDouble());
        Assert.True(sheet.Cell(2, 3).GetBoolean());
    }

    [Fact]
    public void RepeatedOdsCellsAndRows_AreWrittenOut_AndTheEmptyFillerIsNot()
    {
        using var workbook = ConvertOds("""
            <table:table table:name="Sheet1">
              <table:table-row table:number-rows-repeated="2">
                <table:table-cell table:number-columns-repeated="3" office:value-type="float" office:value="7"><text:p>7</text:p></table:table-cell>
                <table:table-cell table:number-columns-repeated="16380"/>
              </table:table-row>
              <table:table-row table:number-rows-repeated="1048574"><table:table-cell table:number-columns-repeated="16384"/></table:table-row>
            </table:table>
            """);

        var sheet = workbook.Worksheet(1);
        Assert.Equal(7, sheet.Cell(2, 3).GetDouble());
        Assert.Equal(2, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(3, sheet.LastColumnUsed()!.ColumnNumber());
    }

    [Fact]
    public void OdsText_KeepsItsSpacesAndParagraphs()
    {
        using var workbook = ConvertOds("""
            <table:table table:name="Sheet1">
              <table:table-row>
                <table:table-cell office:value-type="string"><text:p>A<text:s text:c="2"/>B <text:span>&amp; C</text:span></text:p><text:p>Second line</text:p></table:table-cell>
              </table:table-row>
            </table:table>
            """);

        Assert.Equal("A  B & C\nSecond line", workbook.Worksheet(1).Cell(1, 1).GetString());
    }

    [Fact]
    public void EveryOdsSheet_IsConverted_WithItsName()
    {
        using var workbook = ConvertOds("""
            <table:table table:name="Customers"><table:table-row><table:table-cell office:value-type="string"><text:p>Name</text:p></table:table-cell></table:table-row></table:table>
            <table:table table:name="Products/Services"><table:table-row><table:table-cell office:value-type="string"><text:p>Item</text:p></table:table-cell></table:table-row></table:table>
            """);

        Assert.Equal(["Customers", "Products_Services"], workbook.Worksheets.Select(w => w.Name));
    }

    [Fact]
    public async Task AnOdsBankStatement_Imports()
    {
        var ods = TempPath(".ods");
        WriteOds(ods, """
            <table:table table:name="Statement">
              <table:table-row>
                <table:table-cell office:value-type="string"><text:p>Date</text:p></table:table-cell>
                <table:table-cell office:value-type="string"><text:p>Description</text:p></table:table-cell>
                <table:table-cell office:value-type="string"><text:p>Amount</text:p></table:table-cell>
              </table:table-row>
              <table:table-row>
                <table:table-cell office:value-type="date" office:date-value="2025-05-06"/>
                <table:table-cell office:value-type="string"><text:p>GRAINGER</text:p></table:table-cell>
                <table:table-cell office:value-type="float" office:value="-1284.9"/>
              </table:table-row>
            </table:table>
            """);

        var line = Assert.Single(await new BankStatementImportService().ParseExcelAsync(ods));

        Assert.Equal(new DateTime(2025, 5, 6), line.Date);
        Assert.Equal("GRAINGER", line.Description);
        Assert.Equal(-1284.9m, line.Amount);
    }

    [Fact]
    public async Task ATabSeparatedBankStatement_Imports()
    {
        var tsv = TempPath(".tsv");
        File.WriteAllText(tsv, "Date\tDescription\tAmount\n2025-05-06\tGRAINGER, INC\t-1284.90\n");

        var line = Assert.Single(await new BankStatementImportService().ParseCsvAsync(tsv));

        Assert.Equal("GRAINGER, INC", line.Description);
        Assert.Equal(-1284.90m, line.Amount);
    }

    [Theory]
    [InlineData("a.csv", true, false)]
    [InlineData("a.TSV", true, false)]
    [InlineData("a.txt", true, false)]
    [InlineData("a.xlsx", false, false)]
    [InlineData("a.xlsm", false, false)]
    [InlineData("a.xls", false, true)]
    [InlineData("a.ods", false, true)]
    public void EachFormat_IsReadTheRightWay(string path, bool delimited, bool converted)
    {
        Assert.Equal(delimited, SpreadsheetFiles.IsDelimitedText(path));
        Assert.Equal(converted, SpreadsheetFiles.NeedsConversion(path));
    }
}
