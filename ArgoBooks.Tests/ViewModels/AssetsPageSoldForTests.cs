using ArgoBooks.Core.Models.Assets;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The Assets page's Sold for column, which shows what a sold asset brought in and nothing for the rest.
/// </summary>
public class AssetsPageSoldForTests : ModalViewModelTestBase
{
    private static FixedAsset Asset(string id, string name, AssetDisposalKind? kind = null, decimal salePrice = 0m) => new()
    {
        Id = id,
        Name = name,
        Type = AssetType.Vehicle,
        PurchaseDate = new DateTime(2023, 1, 1),
        Cost = 30000m,
        Currency = "USD",
        Method = DepreciationMethod.StraightLine,
        UsefulLifeYears = 5,
        DisposedDate = kind == null ? null : new DateTime(2024, 6, 1),
        DisposalKind = kind,
        SalePrice = salePrice
    };

    private AssetsPageViewModel Page()
    {
        Company.Assets.Add(Asset("AST-1", "Delivery Van"));
        Company.Assets.Add(Asset("AST-2", "Old Truck", AssetDisposalKind.Sold, 18000m));
        Company.Assets.Add(Asset("AST-3", "Broken Forklift", AssetDisposalKind.Scrapped, 500m));
        return new AssetsPageViewModel();
    }

    private static AssetDisplayItem Row(AssetsPageViewModel page, string id) => page.Assets.Single(a => a.Id == id);

    [Fact]
    public void ASoldAsset_ShowsWhatItWasSoldFor()
    {
        var page = Page();

        var sold = Row(page, "AST-2");
        Assert.Equal("Sold", sold.Status);
        Assert.Equal(18000m, sold.SoldFor);
        Assert.NotEqual("-", sold.SoldForFormatted);
        Assert.Contains("18,000", sold.SoldForFormatted);
        page.Cleanup();
    }

    [Theory]
    [InlineData("AST-1")]
    [InlineData("AST-3")]
    public void AnAssetInUseOrScrapped_ShowsNoSalePrice(string id)
    {
        var page = Page();

        var row = Row(page, id);
        Assert.Equal(0m, row.SoldFor);
        Assert.Equal("-", row.SoldForFormatted);
        page.Cleanup();
    }

    [Fact]
    public void SortingBySoldFor_CountsUnsoldAssetsAsLowerThanAnySale()
    {
        var page = Page();

        page.SortByCommand.Execute("SoldFor");
        Assert.Equal("AST-2", page.Assets[^1].Id);

        page.SortByCommand.Execute("SoldFor");
        Assert.Equal("AST-2", page.Assets[0].Id);
        page.Cleanup();
    }
}
