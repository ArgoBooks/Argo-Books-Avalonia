using ArgoBooks.Controls.ColumnWidths;
using Xunit;

namespace ArgoBooks.Tests.Controls;

/// <summary>
/// Tests for the Products table column-width distribution, specifically that columns
/// hidden in the UI are excluded from the proportional star distribution so they do
/// not reserve empty space.
/// </summary>
public class ProductsTableColumnWidthsTests
{
    private const double TableWidth = 1248;
    // Header/row horizontal padding (24 + 24) baked into the width math.
    private const double Padding = 48;

    /// <summary>
    /// When the inventory columns are still considered visible by the
    /// width manager (the desync), they steal proportional width from the shown columns.
    /// </summary>
    [Fact]
    public void HiddenColumnsNotSynced_StealWidthFromVisibleColumns()
    {
        var widths = new ProductsTableColumnWidths();
        widths.SetAvailableWidth(TableWidth);

        // All 9 columns counted -> Name gets only ~209px and the 6 shown columns
        // leave a large empty gap.
        Assert.True(widths.NameColumnWidth < 230,
            $"Expected the buggy narrow Name width (<230), got {widths.NameColumnWidth:F0}");

        double shownTotal = widths.NameColumnWidth + widths.TypeColumnWidth
            + widths.DescriptionColumnWidth + widths.CategoryColumnWidth
            + widths.SupplierColumnWidth + widths.ActionsColumnWidth + Padding;

        // The three inventory columns (hidden in the default UI) reserve ~314px -> gap.
        Assert.True(TableWidth - shownTotal > 250,
            $"Expected a large empty gap, but shown columns nearly filled the table (gap {TableWidth - shownTotal:F0})");
    }

    /// <summary>
    /// Once the inventory columns are reported hidden (what
    /// ProductsPageViewModel.SyncColumnVisibility does), the shown columns expand to fill
    /// the full table width with no gap.
    /// </summary>
    [Fact]
    public void HiddenColumnsSynced_VisibleColumnsFillWidth()
    {
        var widths = new ProductsTableColumnWidths();
        widths.SetAvailableWidth(TableWidth);

        // Mirror SyncColumnVisibility for the inventory columns hidden by default.
        widths.SetColumnVisibility("Reorder", false);
        widths.SetColumnVisibility("Overstock", false);
        widths.SetColumnVisibility("TrackInventory", false);

        // Name now expands to roughly 291px.
        Assert.True(widths.NameColumnWidth > 270,
            $"Expected the Name column to expand (>270), got {widths.NameColumnWidth:F0}");

        double shownTotal = widths.NameColumnWidth + widths.TypeColumnWidth
            + widths.DescriptionColumnWidth + widths.CategoryColumnWidth
            + widths.SupplierColumnWidth + widths.ActionsColumnWidth + Padding;

        // The shown columns now fill the table (within a few px of rounding).
        Assert.True(Math.Abs(TableWidth - shownTotal) < 5,
            $"Expected shown columns to fill the table width, gap was {TableWidth - shownTotal:F0}");
    }

    /// <summary>
    /// Measures the Revenue tab (Name, Type, Description, Category, Actions) to check it
    /// fills the table without overflowing into a horizontal scrollbar.
    /// </summary>
    [Fact]
    public void RevenueTab_FillsWidth_NoOverflow()
    {
        var widths = new ProductsTableColumnWidths();
        widths.SetColumnVisibility("Reorder", false);
        widths.SetColumnVisibility("Overstock", false);
        widths.SetColumnVisibility("TrackInventory", false);
        widths.SetAvailableWidth(TableWidth);
        widths.SetTabMode(false); // Revenue tab

        double shownTotal = widths.NameColumnWidth + widths.TypeColumnWidth
            + widths.DescriptionColumnWidth + widths.CategoryColumnWidth
            + widths.ActionsColumnWidth + Padding;

        var detail = $"name={widths.NameColumnWidth:F0} type={widths.TypeColumnWidth:F0} "
            + $"desc={widths.DescriptionColumnWidth:F0} cat={widths.CategoryColumnWidth:F0} "
            + $"actions={widths.ActionsColumnWidth:F0} total={shownTotal:F0} "
            + $"scroll={widths.NeedsHorizontalScroll}";

        Assert.False(widths.NeedsHorizontalScroll, $"Unexpected scroll. {detail}");
        Assert.True(Math.Abs(TableWidth - shownTotal) < 5, detail);
    }

    /// <summary>
    /// When the window/table gets narrower, the columns should re-fit to the new width,
    /// not stay wide and force a horizontal scrollbar.
    /// </summary>
    [Fact]
    public void NarrowingWindow_RefitsColumns_NoOverflow()
    {
        var widths = new ProductsTableColumnWidths();
        widths.SetColumnVisibility("Reorder", false);
        widths.SetColumnVisibility("Overstock", false);
        widths.SetColumnVisibility("TrackInventory", false);
        widths.SetTabMode(false); // Revenue tab

        widths.SetAvailableWidth(1400); // wide window first
        widths.SetAvailableWidth(TableWidth); // window shrinks to 1248

        double shownTotal = widths.NameColumnWidth + widths.TypeColumnWidth
            + widths.DescriptionColumnWidth + widths.CategoryColumnWidth
            + widths.ActionsColumnWidth + Padding;

        var detail = $"name={widths.NameColumnWidth:F0} type={widths.TypeColumnWidth:F0} "
            + $"desc={widths.DescriptionColumnWidth:F0} cat={widths.CategoryColumnWidth:F0} "
            + $"actions={widths.ActionsColumnWidth:F0} total={shownTotal:F0} "
            + $"scroll={widths.NeedsHorizontalScroll}";

        Assert.False(widths.NeedsHorizontalScroll, $"Columns did not re-fit after narrowing. {detail}");
        Assert.True(Math.Abs(TableWidth - shownTotal) < 5, detail);
    }
}
