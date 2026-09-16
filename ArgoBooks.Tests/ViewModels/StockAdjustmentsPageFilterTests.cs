using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The stock adjustments filter modal reaching its page.
///
/// The modal sent an adjustment type that the page dropped, and Clear raised nothing at all, so the
/// page stayed filtered after the user cleared it and the next open re-seeded the old values.
/// </summary>
public class StockAdjustmentsPageFilterTests : ModalViewModelTestBase
{
    private (StockAdjustmentsPageViewModel Page, StockAdjustmentsModalsViewModel Modals) Wired()
    {
        Company.StockAdjustments.Add(new StockAdjustment { Id = "ADJ-1", AdjustmentType = AdjustmentType.Add, Quantity = 5, Timestamp = new DateTime(2026, 3, 1) });
        Company.StockAdjustments.Add(new StockAdjustment { Id = "ADJ-2", AdjustmentType = AdjustmentType.Remove, Quantity = 2, Timestamp = new DateTime(2026, 3, 2) });
        Company.StockAdjustments.Add(new StockAdjustment { Id = "ADJ-3", AdjustmentType = AdjustmentType.Set, Quantity = 9, Timestamp = new DateTime(2026, 3, 3) });

        var page = new StockAdjustmentsPageViewModel();
        var modals = new StockAdjustmentsModalsViewModel();

        // The page subscribes through the app shell, which tests don't build.
        modals.FiltersApplied += page.OnFiltersApplied;
        modals.FiltersCleared += page.OnFiltersCleared;
        return (page, modals);
    }

    private static List<string> Ids(StockAdjustmentsPageViewModel page) => [.. page.Adjustments.Select(a => a.Id).Order()];

    [Fact]
    public void TheTypeFilter_ShowsOnlyThatKindOfAdjustment()
    {
        var (page, modals) = Wired();
        modals.OpenFilterModal(page.ProductOptions, null, null, page.FilterProduct, page.FilterType);

        modals.FilterType = "Remove";
        modals.ApplyFiltersCommand.Execute(null);

        Assert.Equal(["ADJ-2"], Ids(page));
        Assert.Equal("Remove", page.FilterType);
    }

    [Fact]
    public void ClearingFilters_ShowsEverythingAgain()
    {
        var (page, modals) = Wired();
        modals.OpenFilterModal(page.ProductOptions, null, null, page.FilterProduct, page.FilterType);
        modals.FilterType = "Set";
        modals.ApplyFiltersCommand.Execute(null);
        Assert.Equal(["ADJ-3"], Ids(page));

        modals.OpenFilterModal(page.ProductOptions, null, null, page.FilterProduct, page.FilterType);
        Assert.Equal("Set", modals.FilterType);
        modals.ClearFiltersCommand.Execute(null);

        Assert.Equal(["ADJ-1", "ADJ-2", "ADJ-3"], Ids(page));
        Assert.Equal("All", page.FilterType);
        Assert.Equal("All", modals.FilterType);
        Assert.False(modals.IsFilterModalOpen);
    }
}
