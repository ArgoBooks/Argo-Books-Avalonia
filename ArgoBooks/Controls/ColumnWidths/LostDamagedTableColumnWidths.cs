using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Lost/Damaged table.
/// Columns: ID | Product | Date | Reason | Loss | Actions
/// </summary>
public partial class LostDamagedTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _idColumnWidth = 90;

    [ObservableProperty]
    private double _productColumnWidth = 150;

    [ObservableProperty]
    private double _dateColumnWidth = 85;

    [ObservableProperty]
    private double _reasonColumnWidth = 100;

    [ObservableProperty]
    private double _lossColumnWidth = 80;

    [ObservableProperty]
    private double _actionsColumnWidth = 84;

    public LostDamagedTableColumnWidths()
    {
        ColumnOrder = ["Id", "Product", "Date", "Reason", "Loss", "Actions"];

        RegisterColumn("Id", new ColumnDef { StarValue = 0.7, MinWidth = 70, PreferredWidth = 90 }, w => IdColumnWidth = w);
        RegisterColumn("Product", new ColumnDef { StarValue = 1.2, MinWidth = 100, PreferredWidth = 150 }, w => ProductColumnWidth = w);
        RegisterColumn("Date", new ColumnDef { StarValue = 0.7, MinWidth = 70, PreferredWidth = 85 }, w => DateColumnWidth = w);
        RegisterColumn("Reason", new ColumnDef { StarValue = 0.8, MinWidth = 80, PreferredWidth = 100 }, w => ReasonColumnWidth = w);
        RegisterColumn("Loss", new ColumnDef { StarValue = 0.6, MinWidth = 60, PreferredWidth = 80 }, w => LossColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(2), MinWidth = ActionsWidth(2) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
