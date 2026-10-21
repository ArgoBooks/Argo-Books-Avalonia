using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Assets table.
/// Columns: Asset | Type | Purchased | Cost | This year | Book value | Status | Actions
/// </summary>
public partial class AssetsTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _assetColumnWidth = 200;

    [ObservableProperty]
    private double _typeColumnWidth = 130;

    [ObservableProperty]
    private double _purchasedColumnWidth = 110;

    [ObservableProperty]
    private double _costColumnWidth = 110;

    [ObservableProperty]
    private double _thisYearColumnWidth = 110;

    [ObservableProperty]
    private double _bookValueColumnWidth = 110;

    [ObservableProperty]
    private double _statusColumnWidth = 100;

    [ObservableProperty]
    private double _actionsColumnWidth = 120;

    public AssetsTableColumnWidths()
    {
        ColumnOrder = ["Asset", "Type", "Purchased", "Cost", "ThisYear", "BookValue", "Status", "Actions"];

        RegisterColumn("Asset", new ColumnDef { StarValue = 1.6, MinWidth = 150, PreferredWidth = 200 }, w => AssetColumnWidth = w);
        RegisterColumn("Type", new ColumnDef { StarValue = 1.0, MinWidth = 100, PreferredWidth = 130 }, w => TypeColumnWidth = w);
        RegisterColumn("Purchased", new ColumnDef { StarValue = 0.9, MinWidth = 90, PreferredWidth = 110 }, w => PurchasedColumnWidth = w);
        RegisterColumn("Cost", new ColumnDef { StarValue = 0.9, MinWidth = 80, PreferredWidth = 110 }, w => CostColumnWidth = w);
        RegisterColumn("ThisYear", new ColumnDef { StarValue = 0.9, MinWidth = 80, PreferredWidth = 110 }, w => ThisYearColumnWidth = w);
        RegisterColumn("BookValue", new ColumnDef { StarValue = 0.9, MinWidth = 80, PreferredWidth = 110 }, w => BookValueColumnWidth = w);
        RegisterColumn("Status", new ColumnDef { StarValue = 0.8, MinWidth = 80, PreferredWidth = 100 }, w => StatusColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(3), MinWidth = ActionsWidth(3) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
