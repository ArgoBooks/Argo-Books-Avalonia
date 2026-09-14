using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Expenses table.
/// </summary>
public partial class TableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _idColumnWidth = 100;

    [ObservableProperty]
    private double _accountantColumnWidth = 80;

    [ObservableProperty]
    private double _productColumnWidth = 150;

    [ObservableProperty]
    private double _supplierColumnWidth = 100;

    [ObservableProperty]
    private double _dateColumnWidth = 90;

    [ObservableProperty]
    private double _quantityColumnWidth = 50;

    [ObservableProperty]
    private double _amountColumnWidth = 80;

    [ObservableProperty]
    private double _taxColumnWidth = 60;

    [ObservableProperty]
    private double _shippingColumnWidth = 70;

    [ObservableProperty]
    private double _discountColumnWidth = 70;

    [ObservableProperty]
    private double _feeColumnWidth = 70;

    [ObservableProperty]
    private double _totalColumnWidth = 80;

    [ObservableProperty]
    private double _receiptColumnWidth = 60;

    [ObservableProperty]
    private double _statusColumnWidth = 90;

    [ObservableProperty]
    private double _actionsColumnWidth = 156;

    public TableColumnWidths()
    {
        ColumnOrder =
        [
            "Id", "Accountant", "Product", "Supplier", "Date", "Quantity",
            "Amount", "Tax", "Shipping", "Discount", "Fee", "Total", "Receipt", "Status", "Actions"
        ];

        RegisterColumn("Id", new ColumnDef { StarValue = 1.2, MinWidth = 60, PreferredWidth = 100 }, w => IdColumnWidth = w);
        RegisterColumn("Accountant", new ColumnDef { StarValue = 1.0, MinWidth = 70, PreferredWidth = 120 }, w => AccountantColumnWidth = w);
        RegisterColumn("Product", new ColumnDef { StarValue = 1.5, MinWidth = 100, PreferredWidth = 200 }, w => ProductColumnWidth = w);
        RegisterColumn("Supplier", new ColumnDef { StarValue = 1.2, MinWidth = 80, PreferredWidth = 150 }, w => SupplierColumnWidth = w);
        RegisterColumn("Date", new ColumnDef { StarValue = 1.0, MinWidth = 80, PreferredWidth = 110 }, w => DateColumnWidth = w);
        RegisterColumn("Quantity", new ColumnDef { StarValue = 0.5, MinWidth = 40, PreferredWidth = 70 }, w => QuantityColumnWidth = w);
        RegisterColumn("Amount", new ColumnDef { StarValue = 0.8, MinWidth = 60, PreferredWidth = 100 }, w => AmountColumnWidth = w);
        RegisterColumn("Tax", new ColumnDef { StarValue = 0.6, MinWidth = 50, PreferredWidth = 80 }, w => TaxColumnWidth = w);
        RegisterColumn("Shipping", new ColumnDef { StarValue = 0.7, MinWidth = 60, PreferredWidth = 90 }, w => ShippingColumnWidth = w);
        RegisterColumn("Discount", new ColumnDef { StarValue = 0.7, MinWidth = 60, PreferredWidth = 90 }, w => DiscountColumnWidth = w);
        RegisterColumn("Fee", new ColumnDef { StarValue = 0.7, MinWidth = 60, PreferredWidth = 90 }, w => FeeColumnWidth = w);
        RegisterColumn("Total", new ColumnDef { StarValue = 0.8, MinWidth = 70, PreferredWidth = 110 }, w => TotalColumnWidth = w);
        RegisterColumn("Receipt", new ColumnDef { StarValue = 0.5, MinWidth = 50, PreferredWidth = 80 }, w => ReceiptColumnWidth = w);
        RegisterColumn("Status", new ColumnDef { StarValue = 0.9, MinWidth = 80, PreferredWidth = 110 }, w => StatusColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(5), MinWidth = ActionsWidth(5) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
