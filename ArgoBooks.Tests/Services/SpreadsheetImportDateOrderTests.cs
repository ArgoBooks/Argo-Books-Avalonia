using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A column of written dates is read one way for every row. Reading each value on its own took
/// 15/03/2026 as the 15th of March (it cannot be anything else) and 05/03/2026 in the same UK
/// sheet as the 3rd of May, so rows landed in the wrong month and were priced at the wrong day's
/// exchange rate.
/// </summary>
public class SpreadsheetImportDateOrderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dates_{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private void WriteDayFirstRevenueSheet()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Revenue");
        ws.Cell(1, 1).Value = "ID"; ws.Cell(1, 2).Value = "Date"; ws.Cell(1, 3).Value = "Total";
        ws.Cell(2, 1).Value = "R1"; ws.Cell(2, 2).Value = "15/03/2026"; ws.Cell(2, 3).Value = 10;
        ws.Cell(3, 1).Value = "R2"; ws.Cell(3, 2).Value = "05/03/2026"; ws.Cell(3, 3).Value = 20;
        wb.SaveAs(_path);
    }

    private static SpreadsheetAnalysisResult RevenueAnalysis() => new()
    {
        Sheets =
        [
            new SheetAnalysis
            {
                SourceSheetName = "Revenue",
                DetectedType = SpreadsheetSheetType.Revenue,
                Tier = ProcessingTier.Tier1_Mapping,
                IsIncluded = true
            }
        ]
    };

    [Fact]
    public async Task MappedImport_ReadsAnAmbiguousDateTheSameWayAsTheRestOfItsColumn()
    {
        WriteDayFirstRevenueSheet();
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportWithMappingsAsync(_path, data, RevenueAnalysis(), new ImportOptions());

        Assert.Equal(new DateTime(2026, 3, 15), data.Revenues.Single(r => r.Id == "R1").Date);
        Assert.Equal(new DateTime(2026, 3, 5), data.Revenues.Single(r => r.Id == "R2").Date);
    }

    [Fact]
    public async Task PlainImport_ReadsAnAmbiguousDateTheSameWayAsTheRestOfItsColumn()
    {
        WriteDayFirstRevenueSheet();
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(_path, data);

        Assert.Equal(new DateTime(2026, 3, 5), data.Revenues.Single(r => r.Id == "R2").Date);
    }

    [Fact]
    public void RatePrefetch_AsksForTheDatesTheImportWillUse()
    {
        // The rate gate fetches a rate for each date before import. Reading 05/03 as the 3rd of
        // May fetched the wrong day, and the row then imported pending for want of its own.
        WriteDayFirstRevenueSheet();

        var dates = new SpreadsheetImportService().CollectTransactionDates(_path, RevenueAnalysis());

        Assert.Contains(new DateTime(2026, 3, 5), dates);
        Assert.DoesNotContain(new DateTime(2026, 5, 3), dates);
    }
}
