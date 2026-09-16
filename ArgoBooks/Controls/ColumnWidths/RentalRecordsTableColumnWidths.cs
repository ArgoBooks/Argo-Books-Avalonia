using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Rental Records table.
/// </summary>
public partial class RentalRecordsTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _idColumnWidth = 80;

    [ObservableProperty]
    private double _itemColumnWidth = 150;

    [ObservableProperty]
    private double _customerColumnWidth = 150;

    [ObservableProperty]
    private double _quantityColumnWidth = 60;

    [ObservableProperty]
    private double _startDateColumnWidth = 95;

    [ObservableProperty]
    private double _dueDateColumnWidth = 95;

    [ObservableProperty]
    private double _statusColumnWidth = 90;

    [ObservableProperty]
    private double _totalColumnWidth = 80;

    [ObservableProperty]
    private double _depositColumnWidth = 80;

    [ObservableProperty]
    private double _paidColumnWidth = 55;

    [ObservableProperty]
    private double _invoiceColumnWidth = 90;

    [ObservableProperty]
    private double _actionsColumnWidth = 152;

    public RentalRecordsTableColumnWidths()
    {
        ColumnOrder = ["Id", "Item", "Customer", "Quantity", "StartDate", "DueDate", "Status", "Total", "Deposit", "Paid", "Invoice", "Actions"];

        RegisterColumn("Id", new ColumnDef { StarValue = 0.6, MinWidth = 50, PreferredWidth = 80 }, w => IdColumnWidth = w);
        RegisterColumn("Item", new ColumnDef { StarValue = 1.0, MinWidth = 90, PreferredWidth = 150 }, w => ItemColumnWidth = w);
        RegisterColumn("Customer", new ColumnDef { StarValue = 1.0, MinWidth = 90, PreferredWidth = 150 }, w => CustomerColumnWidth = w);
        RegisterColumn("Quantity", new ColumnDef { StarValue = 0.4, MinWidth = 40, PreferredWidth = 60 }, w => QuantityColumnWidth = w);
        RegisterColumn("StartDate", new ColumnDef { StarValue = 0.7, MinWidth = 70, PreferredWidth = 95 }, w => StartDateColumnWidth = w);
        RegisterColumn("DueDate", new ColumnDef { StarValue = 0.7, MinWidth = 70, PreferredWidth = 95 }, w => DueDateColumnWidth = w);
        RegisterColumn("Status", new ColumnDef { StarValue = 0.7, MinWidth = 65, PreferredWidth = 90 }, w => StatusColumnWidth = w);
        RegisterColumn("Total", new ColumnDef { StarValue = 0.6, MinWidth = 55, PreferredWidth = 80 }, w => TotalColumnWidth = w);
        RegisterColumn("Deposit", new ColumnDef { StarValue = 0.6, MinWidth = 55, PreferredWidth = 80 }, w => DepositColumnWidth = w);
        RegisterColumn("Paid", new ColumnDef { StarValue = 0.4, MinWidth = 40, PreferredWidth = 55 }, w => PaidColumnWidth = w);
        RegisterColumn("Invoice", new ColumnDef { StarValue = 0.7, MinWidth = 65, PreferredWidth = 90 }, w => InvoiceColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(5), MinWidth = ActionsWidth(5) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
