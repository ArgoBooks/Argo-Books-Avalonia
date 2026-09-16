using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Invoices table.
/// </summary>
public partial class InvoicesTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _idColumnWidth = 100;

    [ObservableProperty]
    private double _customerColumnWidth = 180;

    [ObservableProperty]
    private double _issueDateColumnWidth = 110;

    [ObservableProperty]
    private double _dueDateColumnWidth = 110;

    [ObservableProperty]
    private double _amountColumnWidth = 100;

    [ObservableProperty]
    private double _statusColumnWidth = 100;

    [ObservableProperty]
    private double _actionsColumnWidth = 116;

    public InvoicesTableColumnWidths()
    {
        ColumnOrder = ["Id", "Customer", "IssueDate", "DueDate", "Amount", "Status", "Actions"];

        RegisterColumn("Id", new ColumnDef { StarValue = 0.8, MinWidth = 80, PreferredWidth = 100 }, w => IdColumnWidth = w);
        RegisterColumn("Customer", new ColumnDef { StarValue = 1.4, MinWidth = 140, PreferredWidth = 180 }, w => CustomerColumnWidth = w);
        RegisterColumn("IssueDate", new ColumnDef { StarValue = 0.9, MinWidth = 90, PreferredWidth = 110 }, w => IssueDateColumnWidth = w);
        RegisterColumn("DueDate", new ColumnDef { StarValue = 0.9, MinWidth = 90, PreferredWidth = 110 }, w => DueDateColumnWidth = w);
        RegisterColumn("Amount", new ColumnDef { StarValue = 0.8, MinWidth = 80, PreferredWidth = 100 }, w => AmountColumnWidth = w);
        RegisterColumn("Status", new ColumnDef { StarValue = 0.8, MinWidth = 80, PreferredWidth = 100 }, w => StatusColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(4), MinWidth = ActionsWidth(4) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
