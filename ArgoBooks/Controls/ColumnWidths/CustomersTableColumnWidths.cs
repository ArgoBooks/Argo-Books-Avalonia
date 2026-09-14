using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Customers table.
/// </summary>
public partial class CustomersTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _customerColumnWidth = 180;

    [ObservableProperty]
    private double _emailColumnWidth = 150;

    [ObservableProperty]
    private double _phoneColumnWidth = 110;

    [ObservableProperty]
    private double _addressColumnWidth = 180;

    [ObservableProperty]
    private double _countryColumnWidth = 120;

    [ObservableProperty]
    private double _actionsColumnWidth = 120;

    public CustomersTableColumnWidths()
    {
        ColumnOrder = ["Customer", "Email", "Phone", "Address", "Country", "Actions"];

        RegisterColumn("Customer", new ColumnDef { StarValue = 1.2, MinWidth = 120, PreferredWidth = 180 }, w => CustomerColumnWidth = w);
        RegisterColumn("Email", new ColumnDef { StarValue = 1.0, MinWidth = 100, PreferredWidth = 150 }, w => EmailColumnWidth = w);
        RegisterColumn("Phone", new ColumnDef { StarValue = 0.8, MinWidth = 80, PreferredWidth = 110 }, w => PhoneColumnWidth = w);
        RegisterColumn("Address", new ColumnDef { StarValue = 1.2, MinWidth = 120, PreferredWidth = 180 }, w => AddressColumnWidth = w);
        RegisterColumn("Country", new ColumnDef { StarValue = 0.8, MinWidth = 80, PreferredWidth = 120 }, w => CountryColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(3), MinWidth = ActionsWidth(3) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
