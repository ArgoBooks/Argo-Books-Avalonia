using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The Analytics view model is reused across visits and the page factory calls Initialize on each
/// one. Subscribing to data changes on every visit reloaded the page once per past visit.
/// </summary>
public class AnalyticsPageReloadTests : ModalViewModelTestBase
{
    [Fact]
    public void RepeatedInitialize_ReloadsOncePerDataChange()
    {
        var manager = App.CompanyManager!;
        var vm = new AnalyticsPageViewModel();
        try
        {
            for (var visit = 0; visit < 3; visit++)
                vm.Initialize(manager);

            var reloads = CountChanges(vm, nameof(AnalyticsPageViewModel.ExpensesTrendsSeries));
            manager.NotifyDataChanged();

            Assert.Equal(1, reloads());
        }
        finally
        {
            vm.Cleanup();
        }
    }

    [Fact]
    public void DataChange_LoadsOnlyTheSelectedTab_AndTheRestWhenSelected()
    {
        var manager = App.CompanyManager!;
        var vm = new AnalyticsPageViewModel();
        try
        {
            vm.Initialize(manager);
            var taxLoads = CountChanges(vm, nameof(AnalyticsPageViewModel.TaxCollectedVsPaidSeries));

            manager.NotifyDataChanged();
            Assert.Equal(0, taxLoads());

            vm.SelectedTabIndex = 6; // Taxes
            Assert.Equal(1, taxLoads());

            // Back and forth with nothing changed doesn't reload it.
            vm.SelectedTabIndex = 0;
            vm.SelectedTabIndex = 6;
            Assert.Equal(1, taxLoads());
        }
        finally
        {
            vm.Cleanup();
        }
    }

    private static Func<int> CountChanges(AnalyticsPageViewModel vm, string propertyName)
    {
        var count = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == propertyName)
                count++;
        };
        return () => count;
    }
}
