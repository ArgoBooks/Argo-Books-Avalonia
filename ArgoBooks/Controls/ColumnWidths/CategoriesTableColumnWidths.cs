using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Categories table.
/// </summary>
public partial class CategoriesTableColumnWidths : TableColumnWidthsBase
{
    [ObservableProperty]
    private double _nameColumnWidth = 200;

    [ObservableProperty]
    private double _descriptionColumnWidth = 200;

    [ObservableProperty]
    private double _productCountColumnWidth = 120;

    [ObservableProperty]
    private double _actionsColumnWidth = 156;

    public CategoriesTableColumnWidths()
    {
        ColumnOrder = ["Name", "Description", "ProductCount", "Actions"];

        RegisterColumn("Name", new ColumnDef { StarValue = 1.5, MinWidth = 150, PreferredWidth = 200 }, w => NameColumnWidth = w);
        RegisterColumn("Description", new ColumnDef { StarValue = 1.5, MinWidth = 150, PreferredWidth = 200 }, w => DescriptionColumnWidth = w);
        RegisterColumn("ProductCount", new ColumnDef { StarValue = 0.8, MinWidth = 80, PreferredWidth = 120 }, w => ProductCountColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(4), MinWidth = ActionsWidth(4) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }
}
