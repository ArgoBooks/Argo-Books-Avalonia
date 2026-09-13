using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A master-data / entity sheet with no ID column (or blank ID cells) must still import every
/// distinct row. The fix mints a unique id for blank-ID rows and skips fully-empty rows so trailing template blanks aren't imported as junk.
/// </summary>
public class ImportBlankIdCollapseTests
{
    private static async Task<CompanyData> ImportAsync(string csv, SpreadsheetSheetType type, params (string src, string dst)[] mappings)
    {
        var path = Path.Combine(Path.GetTempPath(), $"imp_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, csv);
        try
        {
            var analysis = new SpreadsheetAnalysisResult();
            var sheet = new SheetAnalysis
            {
                SourceSheetName = type.ToString(),
                DetectedType = type,
                Tier = ProcessingTier.Tier1_Mapping,
                IsIncluded = true
            };
            foreach (var (src, dst) in mappings)
                sheet.ColumnMappings.Add(new ColumnMapping { SourceColumn = src, TargetColumn = dst });
            analysis.Sheets.Add(sheet);

            var data = new CompanyData();
            var svc = new SpreadsheetImportService();
            // SkipExistingRecords ON reproduces the original report (blank IDs collapsed to one).
            await svc.ImportCsvWithMappingsAsync(path, data, analysis, new ImportOptions { SkipExistingRecords = true });
            return data;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportCustomers_NoIdColumn_ImportsEveryRow()
    {
        var data = await ImportAsync(
            "Name,Email\nAcme Corp,a@x.com\nGlobex,b@x.com\nInitech,c@x.com\n",
            SpreadsheetSheetType.Customers,
            ("Name", "Name"), ("Email", "Email"));

        Assert.Equal(3, data.Customers.Count);
        Assert.Equal(3, data.Customers.Select(c => c.Id).Distinct().Count());
        Assert.All(data.Customers, c => Assert.False(string.IsNullOrWhiteSpace(c.Id)));
    }

    [Fact]
    public async Task ImportSuppliers_NoIdColumn_ImportsEveryRow()
    {
        var data = await ImportAsync(
            "Name,Email\nStaples,a@x.com\nVerizon,b@x.com\nAmazon,c@x.com\n",
            SpreadsheetSheetType.Suppliers,
            ("Name", "Name"), ("Email", "Email"));

        Assert.Equal(3, data.Suppliers.Count);
        Assert.Equal(3, data.Suppliers.Select(s => s.Id).Distinct().Count());
        Assert.All(data.Suppliers, s => Assert.False(string.IsNullOrWhiteSpace(s.Id)));
    }

    [Fact]
    public async Task ImportCategories_NoIdColumn_ImportsEveryRow()
    {
        var data = await ImportAsync(
            "Name,Type\nOffice Supplies,Expenses\nUtilities,Expenses\nConsulting,Revenue\n",
            SpreadsheetSheetType.Categories,
            ("Name", "Name"), ("Type", "Type"));

        Assert.Equal(3, data.Categories.Count);
        Assert.Equal(3, data.Categories.Select(c => c.Id).Distinct().Count());
        Assert.All(data.Categories, c => Assert.False(string.IsNullOrWhiteSpace(c.Id)));
    }

    [Fact]
    public async Task ImportLocations_NoIdColumn_ImportsEveryRow()
    {
        var data = await ImportAsync(
            "Name,City\nMain Warehouse,Denver\nEast Depot,Boston\nWest Depot,Seattle\n",
            SpreadsheetSheetType.Locations,
            ("Name", "Name"), ("City", "City"));

        Assert.Equal(3, data.Locations.Count);
        Assert.Equal(3, data.Locations.Select(l => l.Id).Distinct().Count());
        Assert.All(data.Locations, l => Assert.False(string.IsNullOrWhiteSpace(l.Id)));
    }

    [Fact]
    public async Task ImportProducts_NoIdColumn_ImportsEveryRow()
    {
        var data = await ImportAsync(
            "Name,Type\nWidget A,Revenue\nWidget B,Revenue\nWidget C,Revenue\n",
            SpreadsheetSheetType.Products,
            ("Name", "Name"), ("Type", "Type"));

        Assert.Equal(3, data.Products.Count);
        Assert.Equal(3, data.Products.Select(p => p.Id).Distinct().Count());
        Assert.All(data.Products, p => Assert.False(string.IsNullOrWhiteSpace(p.Id)));
    }

    [Fact]
    public async Task ImportInventory_NoIdColumn_ImportsEveryRow()
    {
        var data = await ImportAsync(
            "Product ID,Location ID,In Stock\nPRD-001,LOC-001,10\nPRD-002,LOC-001,20\nPRD-003,LOC-001,30\n",
            SpreadsheetSheetType.Inventory,
            ("Product ID", "Product ID"), ("Location ID", "Location ID"), ("In Stock", "In Stock"));

        Assert.Equal(3, data.Inventory.Count);
        Assert.Equal(3, data.Inventory.Select(i => i.Id).Distinct().Count());
        Assert.All(data.Inventory, i => Assert.False(string.IsNullOrWhiteSpace(i.Id)));
    }

    [Fact]
    public async Task ImportPurchaseOrders_NoIdColumn_ImportsEveryRow()
    {
        var data = await ImportAsync(
            "Supplier ID,Total\nSUP-001,100\nSUP-002,200\nSUP-003,300\n",
            SpreadsheetSheetType.PurchaseOrders,
            ("Supplier ID", "Supplier ID"), ("Total", "Total"));

        Assert.Equal(3, data.PurchaseOrders.Count);
        Assert.Equal(3, data.PurchaseOrders.Select(p => p.Id).Distinct().Count());
        Assert.All(data.PurchaseOrders, p => Assert.False(string.IsNullOrWhiteSpace(p.Id)));
    }

    [Fact]
    public async Task ImportMasterData_TrailingBlankRows_AreNotImportedAsJunk()
    {
        // Two real customers followed by two fully-blank rows (as a spreadsheet often has).
        var data = await ImportAsync(
            "Name,Email\nAcme Corp,a@x.com\nGlobex,b@x.com\n,\n,\n",
            SpreadsheetSheetType.Customers,
            ("Name", "Name"), ("Email", "Email"));

        Assert.Equal(2, data.Customers.Count);
    }
}
