using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Suppliers table.
/// </summary>
public partial class SuppliersTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _supplierColumnWidth = 200;

    [ObservableProperty]
    private double _emailColumnWidth = 180;

    [ObservableProperty]
    private double _phoneColumnWidth = 130;

    [ObservableProperty]
    private double _addressColumnWidth = 180;

    [ObservableProperty]
    private double _countryColumnWidth = 130;

    [ObservableProperty]
    private double _productsColumnWidth = 100;

    [ObservableProperty]
    private double _actionsColumnWidth = 84;

    public SuppliersTableColumnWidths()
    {
        ColumnOrder = ["Supplier", "Email", "Phone", "Address", "Country", "Products", "Actions"];

        RegisterColumn("Supplier", new ColumnDef { StarValue = 1.5, MinWidth = 150, PreferredWidth = 200 }, w => SupplierColumnWidth = w);
        RegisterColumn("Email", new ColumnDef { StarValue = 1.2, MinWidth = 120, PreferredWidth = 180 }, w => EmailColumnWidth = w);
        RegisterColumn("Phone", new ColumnDef { StarValue = 0.9, MinWidth = 100, PreferredWidth = 130 }, w => PhoneColumnWidth = w);
        RegisterColumn("Address", new ColumnDef { StarValue = 1.2, MinWidth = 120, PreferredWidth = 180 }, w => AddressColumnWidth = w);
        RegisterColumn("Country", new ColumnDef { StarValue = 1.0, MinWidth = 100, PreferredWidth = 130 }, w => CountryColumnWidth = w);
        RegisterColumn("Products", new ColumnDef { StarValue = 0.8, MinWidth = 60, PreferredWidth = 100 }, w => ProductsColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(2), MinWidth = ActionsWidth(2) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
