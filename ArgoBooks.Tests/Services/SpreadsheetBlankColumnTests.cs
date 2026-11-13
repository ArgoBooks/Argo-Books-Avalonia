using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A sheet with an empty column must still be read to its last column, because counting only the
/// columns that hold data stops short and drops whatever sits in the rightmost ones.
/// </summary>
public class SpreadsheetBlankColumnTests
{
    private static XLWorkbook SheetWithEmptyFirstColumn(out IXLWorksheet sheet)
    {
        var workbook = new XLWorkbook();
        sheet = workbook.AddWorksheet("Data");
        string[] headers = ["Date", "Description", "Amount", "Category", "Supplier", "Reference", "Notes"];
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 2).Value = headers[i];
            sheet.Cell(2, i + 2).Value = $"value {i}";
        }
        return workbook;
    }

    [Fact]
    public void AnalysisHeaders_IncludeTheLastColumn_WhenColumnAIsEmpty()
    {
        using var workbook = SheetWithEmptyFirstColumn(out var sheet);

        var headers = SpreadsheetAnalysisService.GetHeaders(sheet);

        Assert.Equal(8, headers.Count);
        Assert.Equal("Notes", headers[^1]);
    }

    [Fact]
    public void AnalysisRows_IncludeTheLastColumn_WhenColumnAIsEmpty()
    {
        using var workbook = SheetWithEmptyFirstColumn(out var sheet);

        var headers = SpreadsheetAnalysisService.GetHeaders(sheet);
        var rows = SpreadsheetAnalysisService.GetAllRowsAsStrings(sheet, headers.Count);

        Assert.Single(rows);
        Assert.Equal("value 6", rows[0][^1]);
    }

    [Fact]
    public void FindHeaderRow_FindsAHeaderThatStartsPastTheFirstColumns()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Data");
        sheet.Cell(1, 4).Value = "Bank statement";
        sheet.Cell(3, 4).Value = "Date";
        sheet.Cell(3, 5).Value = "Amount";
        sheet.Cell(4, 4).Value = "2026-01-05";
        sheet.Cell(4, 5).Value = 12.5;

        Assert.Equal(3, SpreadsheetRowReader.FindHeaderRow(sheet));
    }
}
