using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A CSV goes through the same steps before import as a workbook: its customers are checked and
/// created, and the exchange rates for its dates are fetched.
/// </summary>
public class CsvImportReferenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"StackCommerce_{Guid.NewGuid():N}.csv");

    public CsvImportReferenceTests() => File.WriteAllText(_path,
        "ID,Date,Customer ID,Product,Quantity,Unit Price,Total\n" +
        ",2026-10-28,StackCommerce,Argo Books Premium Lifetime,1,21.66,21.66\n" +
        ",2026-11-02,StackCommerce,Argo Books Premium Lifetime,1,18.41,18.41\n");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private SpreadsheetAnalysisResult Analysis() => new()
    {
        Sheets =
        [
            new SheetAnalysis
            {
                SourceSheetName = Path.GetFileNameWithoutExtension(_path),
                DetectedType = SpreadsheetSheetType.Revenue,
                Tier = ProcessingTier.Tier1_Mapping,
                IsIncluded = true
            }
        ]
    };

    // The same order the app runs them in: validate, turn on creating what is missing, import.
    private async Task ImportAsync(CompanyData data)
    {
        var service = new SpreadsheetImportService();
        var validation = await service.ValidateWithMappingsAsync(_path, data, Analysis());
        await service.ImportWithMappingsAsync(_path, data, Analysis(),
            new ImportOptions { AutoCreateMissingReferences = validation.HasMissingReferences });
    }

    [Fact]
    public async Task ACustomerTheCompanyDoesNotHave_IsCreated()
    {
        var data = new CompanyData();

        await ImportAsync(data);

        var customer = Assert.Single(data.Customers);
        Assert.Equal("StackCommerce", customer.Name);
        Assert.All(data.Revenues, r => Assert.Equal(customer.Id, r.CustomerId));
    }

    [Fact]
    public async Task ACustomerTheCompanyHas_IsLinkedByName()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "CUS-001", Name = "StackCommerce" });

        await ImportAsync(data);

        Assert.Single(data.Customers);
        Assert.All(data.Revenues, r => Assert.Equal("CUS-001", r.CustomerId));
    }

    [Fact]
    public void EveryDate_IsCollectedSoItsRateCanBeFetched()
    {
        var dates = new SpreadsheetImportService().CollectTransactionDates(_path, Analysis());

        Assert.Equal([new DateTime(2026, 10, 28), new DateTime(2026, 11, 2)], dates);
    }
}
