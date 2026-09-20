using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Quotes table.
/// Columns: Quote Number | Date | Customer | Valid Until | Total | Status | Actions
/// </summary>
public partial class QuotesTableColumnWidths : TableColumnWidthsBase
{
    #region Column Width Properties

    [ObservableProperty]
    private double _quoteNumberColumnWidth = 150;

    [ObservableProperty]
    private double _dateColumnWidth = 110;

    [ObservableProperty]
    private double _customerColumnWidth = 180;

    [ObservableProperty]
    private double _validUntilColumnWidth = 110;

    [ObservableProperty]
    private double _totalColumnWidth = 110;

    [ObservableProperty]
    private double _statusColumnWidth = 120;

    [ObservableProperty]
    private double _actionsColumnWidth = 264;

    #endregion

    public QuotesTableColumnWidths()
    {
        ColumnOrder = ["QuoteNumber", "Date", "Customer", "ValidUntil", "Total", "Status", "Actions"];

        RegisterColumn("QuoteNumber", new ColumnDef
        {
            StarValue = 1.2,
            MinWidth = 120,
            PreferredWidth = 150
        }, w => QuoteNumberColumnWidth = w);

        RegisterColumn("Date", new ColumnDef
        {
            StarValue = 0.9,
            MinWidth = 90,
            PreferredWidth = 110
        }, w => DateColumnWidth = w);

        // Customer column - main identifier
        RegisterColumn("Customer", new ColumnDef
        {
            StarValue = 1.5,
            MinWidth = 150,
            PreferredWidth = 180
        }, w => CustomerColumnWidth = w);

        RegisterColumn("ValidUntil", new ColumnDef
        {
            StarValue = 0.9,
            MinWidth = 90,
            PreferredWidth = 110
        }, w => ValidUntilColumnWidth = w);

        // Total column (currency)
        RegisterColumn("Total", new ColumnDef
        {
            StarValue = 0.9,
            MinWidth = 90,
            PreferredWidth = 110
        }, w => TotalColumnWidth = w);

        RegisterColumn("Status", new ColumnDef
        {
            StarValue = 1.0,
            MinWidth = 100,
            PreferredWidth = 120
        }, w => StatusColumnWidth = w);

        // Actions column (fixed width - 7 buttons)
        RegisterColumn("Actions", new ColumnDef
        {
            IsFixed = true,
            FixedWidth = ActionsWidth(7),
            MinWidth = ActionsWidth(7)
        }, w => ActionsColumnWidth = w);

        // Initial calculation
        RecalculateWidths();
    }
}
