using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Returns table.
/// Columns: ID | Product | Supplier/Customer | Date | Reason | Refund | Actions
/// </summary>
public partial class ReturnsTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _idColumnWidth = 100;

    [ObservableProperty]
    private double _productColumnWidth = 150;

    [ObservableProperty]
    private double _supplierCustomerColumnWidth = 120;

    [ObservableProperty]
    private double _dateColumnWidth = 85;

    [ObservableProperty]
    private double _reasonColumnWidth = 120;

    [ObservableProperty]
    private double _refundColumnWidth = 80;

    [ObservableProperty]
    private double _actionsColumnWidth = 84;

    public ReturnsTableColumnWidths()
    {
        ColumnOrder = ["Id", "Product", "SupplierCustomer", "Date", "Reason", "Refund", "Actions"];

        RegisterColumn("Id", new ColumnDef { StarValue = 0.8, MinWidth = 80, PreferredWidth = 100 }, w => IdColumnWidth = w);
        RegisterColumn("Product", new ColumnDef { StarValue = 1.2, MinWidth = 100, PreferredWidth = 150 }, w => ProductColumnWidth = w);
        RegisterColumn("SupplierCustomer", new ColumnDef { StarValue = 1.0, MinWidth = 100, PreferredWidth = 120 }, w => SupplierCustomerColumnWidth = w);
        RegisterColumn("Date", new ColumnDef { StarValue = 0.7, MinWidth = 70, PreferredWidth = 85 }, w => DateColumnWidth = w);
        RegisterColumn("Reason", new ColumnDef { StarValue = 1.0, MinWidth = 80, PreferredWidth = 120 }, w => ReasonColumnWidth = w);
        RegisterColumn("Refund", new ColumnDef { StarValue = 0.6, MinWidth = 60, PreferredWidth = 80 }, w => RefundColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(2), MinWidth = ActionsWidth(2) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
