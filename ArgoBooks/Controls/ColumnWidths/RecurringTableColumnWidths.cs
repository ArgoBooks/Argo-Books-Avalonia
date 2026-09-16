using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Recurring invoices table.
/// Columns: Customer | Amount | Frequency | Next Invoice | Status | Actions
/// </summary>
public partial class RecurringTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _customerColumnWidth = 260;

    [ObservableProperty]
    private double _amountColumnWidth = 150;

    [ObservableProperty]
    private double _frequencyColumnWidth = 140;

    [ObservableProperty]
    private double _nextInvoiceColumnWidth = 150;

    [ObservableProperty]
    private double _statusColumnWidth = 120;

    [ObservableProperty]
    private double _actionsColumnWidth = 120;

    public RecurringTableColumnWidths()
    {
        ColumnOrder = ["Customer", "Amount", "Frequency", "NextInvoice", "Status", "Actions"];

        RegisterColumn("Customer", new ColumnDef { StarValue = 1.6, MinWidth = 160, PreferredWidth = 260 }, w => CustomerColumnWidth = w);
        RegisterColumn("Amount", new ColumnDef { StarValue = 1.0, MinWidth = 110, PreferredWidth = 150 }, w => AmountColumnWidth = w);
        RegisterColumn("Frequency", new ColumnDef { StarValue = 0.9, MinWidth = 110, PreferredWidth = 140 }, w => FrequencyColumnWidth = w);
        RegisterColumn("NextInvoice", new ColumnDef { StarValue = 1.0, MinWidth = 120, PreferredWidth = 150 }, w => NextInvoiceColumnWidth = w);
        RegisterColumn("Status", new ColumnDef { StarValue = 0.8, MinWidth = 100, PreferredWidth = 120 }, w => StatusColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(3), MinWidth = ActionsWidth(3) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
