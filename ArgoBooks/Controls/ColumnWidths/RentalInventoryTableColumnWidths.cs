using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Rental Inventory table.
/// </summary>
public partial class RentalInventoryTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _itemColumnWidth = 180;

    [ObservableProperty]
    private double _statusColumnWidth = 110;

    [ObservableProperty]
    private double _inStockColumnWidth = 80;

    [ObservableProperty]
    private double _dailyRateColumnWidth = 90;

    [ObservableProperty]
    private double _weeklyRateColumnWidth = 90;

    [ObservableProperty]
    private double _depositColumnWidth = 90;

    [ObservableProperty]
    private double _actionsColumnWidth = 160;

    public RentalInventoryTableColumnWidths()
    {
        ColumnOrder = ["Item", "Status", "InStock", "DailyRate", "WeeklyRate", "Deposit", "Actions"];

        RegisterColumn("Item", new ColumnDef { StarValue = 1.6, MinWidth = 140, PreferredWidth = 180 }, w => ItemColumnWidth = w);
        RegisterColumn("Status", new ColumnDef { StarValue = 0.9, MinWidth = 90, PreferredWidth = 110 }, w => StatusColumnWidth = w);
        RegisterColumn("InStock", new ColumnDef { StarValue = 0.6, MinWidth = 60, PreferredWidth = 80 }, w => InStockColumnWidth = w);
        RegisterColumn("DailyRate", new ColumnDef { StarValue = 0.7, MinWidth = 70, PreferredWidth = 90 }, w => DailyRateColumnWidth = w);
        RegisterColumn("WeeklyRate", new ColumnDef { StarValue = 0.7, MinWidth = 70, PreferredWidth = 90 }, w => WeeklyRateColumnWidth = w);
        RegisterColumn("Deposit", new ColumnDef { StarValue = 0.7, MinWidth = 70, PreferredWidth = 90 }, w => DepositColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(4), MinWidth = ActionsWidth(4) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
