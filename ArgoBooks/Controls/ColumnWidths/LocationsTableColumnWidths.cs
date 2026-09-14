using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Locations table.
/// Columns: Location | Type | Address | Manager | Actions
/// </summary>
public partial class LocationsTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _locationColumnWidth = 200;

    [ObservableProperty]
    private double _typeColumnWidth = 120;

    [ObservableProperty]
    private double _addressColumnWidth = 200;

    [ObservableProperty]
    private double _managerColumnWidth = 150;

    [ObservableProperty]
    private double _actionsColumnWidth = 84;

    public LocationsTableColumnWidths()
    {
        ColumnOrder = ["Location", "Type", "Address", "Manager", "Actions"];

        RegisterColumn("Location", new ColumnDef { StarValue = 1.4, MinWidth = 150, PreferredWidth = 200 }, w => LocationColumnWidth = w);
        RegisterColumn("Type", new ColumnDef { StarValue = 0.8, MinWidth = 100, PreferredWidth = 120 }, w => TypeColumnWidth = w);
        RegisterColumn("Address", new ColumnDef { StarValue = 1.4, MinWidth = 150, PreferredWidth = 200 }, w => AddressColumnWidth = w);
        RegisterColumn("Manager", new ColumnDef { StarValue = 1.0, MinWidth = 120, PreferredWidth = 150 }, w => ManagerColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(2), MinWidth = ActionsWidth(2) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
