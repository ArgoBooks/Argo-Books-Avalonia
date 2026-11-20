using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A workbook laid out the way Argo Books exports one imports straight away, without asking the AI
/// what its columns mean.
/// </summary>
public class ExportLayoutMatcherTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"layout_{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private static readonly string[] EverySheet =
    [
        "Customers", "Invoices", "Invoice Line Items", "Expenses", "Products", "Inventory", "Payments",
        "Suppliers", "Revenue", "Rental Inventory", "Rental Records", "Categories", "Locations",
        "Recurring Invoices", "Stock Adjustments", "Purchase Orders", "Purchase Order Line Items",
        "Employees", "Pay Runs", "Assets", "Sales Tax Filings",
    ];

    private void Workbook(params (string Sheet, string[] Headers, object[][] Rows)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, headers, rows) in sheets)
        {
            var ws = workbook.AddWorksheet(name);
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
        }
        workbook.SaveAs(_path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Canada")]
    [InlineData("United States")]
    public async Task EverySheetTheExportWrites_IsMatched(string? country)
    {
        var data = new CompanyData();
        data.Settings.Company.Country = country;
        await new SpreadsheetExportService().ExportToExcelAsync(_path, data, [.. EverySheet], null, null);

        var match = ExportLayoutMatcher.Match(_path, country);

        Assert.NotNull(match);
        Assert.Equal(EverySheet.Length, match.Sheets.Count);
        Assert.All(match.Sheets, s => Assert.Equal(ProcessingTier.Tier1_Mapping, s.Tier));
    }

    [Fact]
    public async Task PayRunsAndTaxFilings_AreLeftOut_SinceTheyAreOnlyExported()
    {
        await new SpreadsheetExportService().ExportToExcelAsync(_path, new CompanyData(), ["Expenses", "Pay Runs", "Sales Tax Filings"], null, null);

        var match = ExportLayoutMatcher.Match(_path);

        Assert.NotNull(match);
        Assert.True(match.Sheets.Single(s => s.SourceSheetName == "Expenses").IsIncluded);
        Assert.All(match.Sheets.Where(s => s.SourceSheetName != "Expenses"), s =>
        {
            Assert.False(s.IsIncluded);
            Assert.NotNull(s.UnsupportedReason);
        });
    }

    [Fact]
    public void SomeOfASheetsColumns_InAnyOrder_IsMatched()
    {
        Workbook(("Expenses", ["Total", "Date", "Product"], [[12.5, new DateTime(2026, 5, 4), "Paper"]]));

        var match = ExportLayoutMatcher.Match(_path);

        Assert.NotNull(match);
        var sheet = Assert.Single(match.Sheets);
        Assert.Equal(SpreadsheetSheetType.Expenses, sheet.DetectedType);
        Assert.Equal(1, sheet.RowCount);
        Assert.Equal(["Total", "Date", "Product"], sheet.ColumnMappings.Select(m => m.TargetColumn));
    }

    [Theory]
    [InlineData("Notes")]
    [InlineData("date")]
    [InlineData("")]
    public void AColumnTheImportDoesNotKnow_LeavesItToTheAi(string header)
    {
        Workbook(("Expenses", ["Date", "Product", header, "Total"], [[new DateTime(2026, 5, 4), "Paper", "x", 12.5]]));

        Assert.Null(ExportLayoutMatcher.Match(_path));
    }

    [Fact]
    public void ASheetNameTheImportDoesNotKnow_LeavesItToTheAi()
    {
        Workbook(
            ("Expenses", ["Date", "Product", "Total"], [[new DateTime(2026, 5, 4), "Paper", 12.5]]),
            ("Receipts 2026", ["Date", "Product", "Total"], [[new DateTime(2026, 5, 4), "Paper", 12.5]]));

        Assert.Null(ExportLayoutMatcher.Match(_path));
    }

    [Fact]
    public void ABankStatementSheet_LeavesItToTheAi()
    {
        Workbook(("Bank Statement", ["Date", "Description", "Amount"], [[new DateTime(2026, 5, 4), "Coffee", -4.5]]));

        Assert.Null(ExportLayoutMatcher.Match(_path));
    }

    [Fact]
    public async Task TheCurrencyColumn_IsReadFromAMatchedSheet()
    {
        Workbook(("Expenses", ["Date", "Product", "Unit Price", "Total", "Currency"],
            [[new DateTime(2026, 5, 4), "Paper", 12.5, 12.5, "CAD"]]));
        var match = ExportLayoutMatcher.Match(_path)!;

        var scan = CurrencyImportPreparer.ScanWorkbook(_path, match);
        var data = new CompanyData();
        data.Settings.Localization.Currency = "CAD";
        await new SpreadsheetImportService().ImportWithMappingsAsync(_path, data, match,
            new ImportOptions { RowCurrencyBySheet = scan.Resolved });

        Assert.Equal("CAD", Assert.Single(data.Expenses).OriginalCurrency);
    }
}
