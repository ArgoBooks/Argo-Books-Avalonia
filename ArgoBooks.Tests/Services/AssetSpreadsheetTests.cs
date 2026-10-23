using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Exporting and importing the Assets sheet, and the sample company's assets, which come from that sheet.
/// </summary>
public class AssetSpreadsheetTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var file in _files)
        {
            try { File.Delete(file); }
            catch { /* A temp file left behind is not worth failing a test run over. */ }
        }
        GC.SuppressFinalize(this);
    }

    private string TempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"argo-assets-{Guid.NewGuid():N}.xlsx");
        _files.Add(path);
        return path;
    }

    private static FixedAsset Van() => new()
    {
        Id = "AST-001",
        Name = "Delivery van",
        Type = AssetType.Vehicle,
        PurchaseDate = new DateTime(2025, 2, 20),
        Cost = 32000m,
        Currency = "CAD",
        ExpenseId = "PUR-2025-037",
        Method = DepreciationMethod.DecliningBalance,
        RatePercent = 30m,
        CcaClass = "10",
        FirstYearFactor = 1.5m,
        Notes = "White, 2024 model"
    };

    [Fact]
    public async Task ExportThenImport_GivesBackTheSameAsset()
    {
        var source = new CompanyData();
        var sold = Van();
        sold.Id = "AST-002";
        sold.DisposedDate = new DateTime(2026, 5, 1);
        sold.DisposalKind = AssetDisposalKind.Sold;
        sold.SalePrice = 21000m;
        source.Assets.Add(Van());
        source.Assets.Add(sold);
        var path = TempPath();

        await new SpreadsheetExportService().ExportToExcelAsync(path, source, ["Assets"], null, null);
        var target = new CompanyData();
        await new SpreadsheetImportService().ImportFromExcelAsync(path, target);

        Assert.Equal(2, target.Assets.Count);
        var van = target.Assets.Single(a => a.Id == "AST-001");
        Assert.Equal("Delivery van", van.Name);
        Assert.Equal(AssetType.Vehicle, van.Type);
        Assert.Equal(new DateTime(2025, 2, 20), van.PurchaseDate.Date);
        Assert.Equal(32000m, van.Cost);
        Assert.Equal("PUR-2025-037", van.ExpenseId);
        Assert.Equal("10", van.CcaClass);
        Assert.Equal(1.5m, van.FirstYearFactor);
        Assert.True(van.FollowsCca);
        Assert.False(van.IsDisposed);

        var back = target.Assets.Single(a => a.Id == "AST-002");
        Assert.Equal(AssetDisposalKind.Sold, back.DisposalKind);
        Assert.Equal(21000m, back.SalePrice);
        Assert.Equal(new DateTime(2026, 5, 1), back.DisposedDate?.Date);
    }

    [Fact]
    public async Task Import_ASheetNamingOnlyTheClass_TakesCrasRateAndFirstYearShare()
    {
        var path = TempPath();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Assets");
            string[] headers = ["ID", "Name", "Type", "Purchase Date", "Cost", "Method", "CRA Class"];
            string[] row = ["AST-001", "Shop furniture", "Furniture and fixtures", "2026-03-01", "4500", "Declining balance", "8"];
            for (var c = 0; c < headers.Length; c++)
            {
                sheet.Cell(1, c + 1).Value = headers[c];
                sheet.Cell(2, c + 1).Value = row[c];
            }
            workbook.SaveAs(path);
        }

        var data = new CompanyData();
        data.Settings.Localization.Currency = "CAD";
        await new SpreadsheetImportService().ImportFromExcelAsync(path, data);

        var asset = Assert.Single(data.Assets);
        var rates = CcaRateService.BuiltIn()!;
        Assert.Equal(AssetType.FurnitureAndFixtures, asset.Type);
        Assert.Equal(DepreciationMethod.DecliningBalance, asset.Method);
        Assert.Equal(20m, asset.RatePercent);
        Assert.Equal(rates.FirstYearFactorOn("8", asset.PurchaseDate), asset.FirstYearFactor);
        Assert.Equal("CAD", asset.Currency);
    }

    [Fact]
    public async Task Import_AColumnTheSheetLeavesOut_KeepsTheStoredValue()
    {
        var data = new CompanyData();
        data.Assets.Add(Van());
        var path = TempPath();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Assets");
            sheet.Cell(1, 1).Value = "ID";
            sheet.Cell(1, 2).Value = "Notes";
            sheet.Cell(2, 1).Value = "AST-001";
            sheet.Cell(2, 2).Value = "Repainted";
            workbook.SaveAs(path);
        }

        await new SpreadsheetImportService().ImportFromExcelAsync(path, data);

        var van = Assert.Single(data.Assets);
        Assert.Equal("Repainted", van.Notes);
        Assert.Equal(32000m, van.Cost);
        Assert.Equal("10", van.CcaClass);
    }

    [Fact]
    public async Task SampleWorkbook_ImportsItsAssets_EachBoughtWithItsWholeExpense()
    {
        var path = SampleInventoryResolutionTests.SampleXlsxPath();
        Assert.True(File.Exists(path), $"Sample workbook not found from {Directory.GetCurrentDirectory()}");

        var data = new CompanyData();
        data.Settings.Company.Country = "Canada";
        data.Settings.Localization.Currency = "CAD";
        await new SpreadsheetImportService().ImportFromExcelAsync(path, data, new ImportOptions { AutoCreateMissingReferences = true });

        Assert.Equal(6, data.Assets.Count);
        Assert.Single(data.Assets, a => a.Type == AssetType.Land && a.ExpenseId == null);
        Assert.Single(data.Assets, a => a.IsDisposed);
        foreach (var asset in data.Assets.Where(a => a.ExpenseId != null))
        {
            var expense = Assert.Single(data.Expenses, e => e.Id == asset.ExpenseId);
            Assert.Equal(1m, AssetDepreciation.CapitalizedShare(expense, data.Assets, PurchaseTax.RuleFor(data)));
            Assert.True(asset.FollowsCca, $"{asset.Name} should follow CRA's method");
        }
    }
}
