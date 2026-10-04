using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A workbook that defines its own customers and refers to them from its invoices. The check for
/// missing references has to read the sheets the way the import will: by their mapped columns and
/// detected type. Reading the raw ones, it took every customer for a missing one, created a
/// placeholder named after the id, and the real row was then skipped as already existing.
/// </summary>
public class SpreadsheetImportMappedReferenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"refs_{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ACustomerTheFileDefines_IsImportedInFull_NotReplacedByAPlaceholder()
    {
        using (var wb = new XLWorkbook())
        {
            var customers = wb.AddWorksheet("Client List");
            customers.Cell(1, 1).Value = "Customer ID"; customers.Cell(1, 2).Value = "Name"; customers.Cell(1, 3).Value = "Email";
            customers.Cell(2, 1).Value = "C001"; customers.Cell(2, 2).Value = "Acme Ltd"; customers.Cell(2, 3).Value = "ap@acme.test";

            var invoices = wb.AddWorksheet("Invoices");
            invoices.Cell(1, 1).Value = "Invoice #"; invoices.Cell(1, 2).Value = "Customer ID";
            invoices.Cell(1, 3).Value = "Issue Date"; invoices.Cell(1, 4).Value = "Total";
            invoices.Cell(2, 1).Value = "INV-1"; invoices.Cell(2, 2).Value = "C001";
            invoices.Cell(2, 3).Value = "2026-03-01"; invoices.Cell(2, 4).Value = 100;
            wb.SaveAs(_path);
        }

        var analysis = new SpreadsheetAnalysisResult
        {
            Sheets =
            [
                new SheetAnalysis
                {
                    SourceSheetName = "Client List",
                    DetectedType = SpreadsheetSheetType.Customers,
                    Tier = ProcessingTier.Tier1_Mapping,
                    IsIncluded = true,
                    ColumnMappings = [new ColumnMapping { SourceColumn = "Customer ID", TargetColumn = "ID" }]
                },
                new SheetAnalysis
                {
                    SourceSheetName = "Invoices",
                    DetectedType = SpreadsheetSheetType.Invoices,
                    Tier = ProcessingTier.Tier1_Mapping,
                    IsIncluded = true
                }
            ]
        };
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportWithMappingsAsync(
            _path, data, analysis, new ImportOptions { AutoCreateMissingReferences = true, SkipExistingRecords = true });

        var customer = Assert.Single(data.Customers);
        Assert.Equal("Acme Ltd", customer.Name);
        Assert.Equal("C001", Assert.Single(data.Invoices).CustomerId);
    }

    // The sheet that refers to a customer is recognised as invoices but called something else.
    // Its missing customer has to be noticed and created, not left as an id pointing nowhere.
    [Fact]
    public async Task AReferringSheetWithItsOwnName_HasItsMissingCustomerCreated()
    {
        using (var wb = new XLWorkbook())
        {
            var invoices = wb.AddWorksheet("Sales Invoices");
            invoices.Cell(1, 1).Value = "Invoice #"; invoices.Cell(1, 2).Value = "Customer ID";
            invoices.Cell(1, 3).Value = "Issue Date"; invoices.Cell(1, 4).Value = "Total";
            invoices.Cell(2, 1).Value = "INV-1"; invoices.Cell(2, 2).Value = "C009";
            invoices.Cell(2, 3).Value = "2026-03-01"; invoices.Cell(2, 4).Value = 100;
            wb.SaveAs(_path);
        }

        var analysis = new SpreadsheetAnalysisResult
        {
            Sheets =
            [
                new SheetAnalysis
                {
                    SourceSheetName = "Sales Invoices",
                    DetectedType = SpreadsheetSheetType.Invoices,
                    Tier = ProcessingTier.Tier1_Mapping,
                    IsIncluded = true
                }
            ]
        };
        var data = new CompanyData();
        var service = new SpreadsheetImportService();

        var validation = await service.ValidateWithMappingsAsync(_path, data, analysis);
        Assert.True(validation.HasMissingReferences);

        await service.ImportWithMappingsAsync(
            _path, data, analysis, new ImportOptions { AutoCreateMissingReferences = true, SkipExistingRecords = true });

        Assert.Equal("C009", Assert.Single(data.Customers).Id);
    }

    // A row left out for its date says so, rather than hiding among "missing fields".
    [Fact]
    public async Task ARowLeftOutForItsDate_IsReportedAsSuch()
    {
        using (var wb = new XLWorkbook())
        {
            var revenue = wb.AddWorksheet("Revenue");
            revenue.Cell(1, 1).Value = "ID"; revenue.Cell(1, 2).Value = "Date"; revenue.Cell(1, 3).Value = "Total";
            revenue.Cell(2, 1).Value = "R1"; revenue.Cell(2, 2).Value = "2026-03-01"; revenue.Cell(2, 3).Value = 10;
            revenue.Cell(3, 1).Value = "R2"; revenue.Cell(3, 2).Value = "Q1 2026"; revenue.Cell(3, 3).Value = 20;
            wb.SaveAs(_path);
        }

        var analysis = new SpreadsheetAnalysisResult
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

        var result = await new SpreadsheetImportService().ImportWithMappingsAsync(
            _path, new CompanyData(), analysis, new ImportOptions());

        Assert.Contains(result.SheetResults.Single().SkipReasons, r => r.Contains("unreadable date"));
    }
}
