using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Helpers;
using Xunit;

namespace ArgoBooks.Tests.Controls;

/// <summary>
/// Tests for <see cref="ColumnVisibilityHelper.SyncToManager"/>, which reflects a page view
/// model's Show{Column}Column properties into its column-width manager so hidden columns do
/// not reserve empty width.
/// </summary>
public class ColumnVisibilitySyncTests
{
    /// <summary>
    /// A stand-in for a products page view model: a ColumnWidths manager plus the
    /// Show{Column}Column convention, with the inventory columns reported hidden.
    /// </summary>
    private sealed class FakeProductsViewModel
    {
        public ProductsTableColumnWidths ColumnWidths { get; } = new();

        // ReSharper disable UnusedMember.Local
        public bool ShowNameColumn => true;
        public bool ShowTypeColumn => true;
        public bool ShowDescriptionColumn => true;
        public bool ShowCategoryColumn => true;
        public bool ShowSupplierColumn => true;
        public bool ShowReorderColumn => false;
        public bool ShowOverstockColumn => false;
        public bool ShowTrackInventoryColumn => false;
        // ReSharper restore UnusedMember.Local
    }

    [Fact]
    public void SyncToManager_AppliesHiddenColumns_SoVisibleOnesFillWidth()
    {
        var vm = new FakeProductsViewModel();

        ColumnVisibilityHelper.SyncToManager(vm);
        vm.ColumnWidths.SetAvailableWidth(1248);

        // With Reorder/Overstock/TrackInventory reported hidden, the shown columns expand to
        // fill the table (Name ~291) instead of leaving a gap (Name ~209).
        Assert.True(vm.ColumnWidths.NameColumnWidth > 270,
            $"Name did not expand; got {vm.ColumnWidths.NameColumnWidth:F0}");
    }

    [Fact]
    public void SyncToManager_ObjectWithoutColumnWidths_DoesNothing()
    {
        // Must not throw for view models (modals, dialogs) that have no ColumnWidths property.
        ColumnVisibilityHelper.SyncToManager(new object());
    }
}
