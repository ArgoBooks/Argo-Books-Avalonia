using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Employees table.
/// Columns: Employee | Province | Pay type | Pay rate | Frequency | Status | Actions
/// </summary>
public partial class EmployeesTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _employeeColumnWidth = 200;

    [ObservableProperty]
    private double _provinceColumnWidth = 110;

    [ObservableProperty]
    private double _payTypeColumnWidth = 110;

    [ObservableProperty]
    private double _payRateColumnWidth = 130;

    [ObservableProperty]
    private double _frequencyColumnWidth = 130;

    [ObservableProperty]
    private double _statusColumnWidth = 110;

    [ObservableProperty]
    private double _actionsColumnWidth = 84;

    public EmployeesTableColumnWidths()
    {
        ColumnOrder = ["Employee", "Province", "PayType", "PayRate", "Frequency", "Status", "Actions"];

        RegisterColumn("Employee", new ColumnDef { StarValue = 1.6, MinWidth = 150, PreferredWidth = 200 }, w => EmployeeColumnWidth = w);
        RegisterColumn("Province", new ColumnDef { StarValue = 0.7, MinWidth = 90, PreferredWidth = 110 }, w => ProvinceColumnWidth = w);
        RegisterColumn("PayType", new ColumnDef { StarValue = 0.7, MinWidth = 90, PreferredWidth = 110 }, w => PayTypeColumnWidth = w);
        RegisterColumn("PayRate", new ColumnDef { StarValue = 0.9, MinWidth = 110, PreferredWidth = 130 }, w => PayRateColumnWidth = w);
        RegisterColumn("Frequency", new ColumnDef { StarValue = 0.9, MinWidth = 110, PreferredWidth = 130 }, w => FrequencyColumnWidth = w);
        RegisterColumn("Status", new ColumnDef { StarValue = 0.7, MinWidth = 90, PreferredWidth = 110 }, w => StatusColumnWidth = w);
        // Three: edit, Record of Employment, archive.
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(3), MinWidth = ActionsWidth(3) }, w => ActionsColumnWidth = w);
    }
}
