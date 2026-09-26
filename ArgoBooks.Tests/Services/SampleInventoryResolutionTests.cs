using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Regression: every stock adjustment in the sample company must resolve to a real product
/// through InventoryItemId -> InventoryItem.ProductId -> Product. Guards against the importer
/// collapsing two real, distinctly-id'd products that happen to share a name.
/// </summary>
public class SampleInventoryResolutionTests
{
    internal static string SampleXlsxPath()
    {
        // Walk up from the test bin dir to the repo root and find the embedded sample workbook.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ArgoBooks", "Resources", "SampleCompanyData.xlsx");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return string.Empty;
    }

    [Fact]
    public async Task SampleAdjustments_AllResolveToAProduct()
    {
        var path = SampleXlsxPath();
        Assert.True(File.Exists(path), $"Sample workbook not found from {Directory.GetCurrentDirectory()}");

        var data = new CompanyData();
        var svc = new SpreadsheetImportService();
        await svc.ImportFromExcelAsync(path, data, new ImportOptions { AutoCreateMissingReferences = true });

        var unresolved = data.StockAdjustments.Select(a =>
        {
            var inv = data.Inventory.FirstOrDefault(i => i.Id == a.InventoryItemId);
            var prod = inv != null ? data.Products.FirstOrDefault(p => p.Id == inv.ProductId) : null;
            return (a.Id, a.InventoryItemId, InvFound: inv != null, ProdFound: prod != null);
        }).Where(x => !x.ProdFound).ToList();

        var detail = string.Join("; ", unresolved.Select(u => $"{u.Id}->{u.InventoryItemId} (inv:{u.InvFound})"));
        Assert.True(unresolved.Count == 0, $"{unresolved.Count}/{data.StockAdjustments.Count} unresolved: {detail}");
    }
}
