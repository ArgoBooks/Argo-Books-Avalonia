using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Sales Tax table.
/// Columns: Period | Collected | Claimed back | Net | Due | Status | Actions
/// </summary>
public partial class SalesTaxTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _periodColumnWidth = 200;

    [ObservableProperty]
    private double _collectedColumnWidth = 120;

    [ObservableProperty]
    private double _claimedBackColumnWidth = 120;

    [ObservableProperty]
    private double _netColumnWidth = 120;

    [ObservableProperty]
    private double _dueColumnWidth = 120;

    [ObservableProperty]
    private double _statusColumnWidth = 100;

    [ObservableProperty]
    private double _actionsColumnWidth = 120;

    public SalesTaxTableColumnWidths()
    {
        ColumnOrder = ["Period", "Collected", "ClaimedBack", "Net", "Due", "Status", "Actions"];

        RegisterColumn("Period", new ColumnDef { StarValue = 1.6, MinWidth = 150, PreferredWidth = 200 }, w => PeriodColumnWidth = w);
        RegisterColumn("Collected", new ColumnDef { StarValue = 1.0, MinWidth = 90, PreferredWidth = 120 }, w => CollectedColumnWidth = w);
        RegisterColumn("ClaimedBack", new ColumnDef { StarValue = 1.0, MinWidth = 90, PreferredWidth = 120 }, w => ClaimedBackColumnWidth = w);
        RegisterColumn("Net", new ColumnDef { StarValue = 1.0, MinWidth = 90, PreferredWidth = 120 }, w => NetColumnWidth = w);
        RegisterColumn("Due", new ColumnDef { StarValue = 1.0, MinWidth = 90, PreferredWidth = 120 }, w => DueColumnWidth = w);
        RegisterColumn("Status", new ColumnDef { StarValue = 0.8, MinWidth = 80, PreferredWidth = 100 }, w => StatusColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(2), MinWidth = ActionsWidth(2) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
