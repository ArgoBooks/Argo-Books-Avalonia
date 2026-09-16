using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Manages column widths for the Products table, which has two tabs with different column
/// sets selected via <see cref="SetTabMode"/>:
/// Expenses (Name, Type, Description, Category, Supplier, Reorder, Overstock, TrackInventory,
/// Actions) and Revenue (Name, Type, Description, Category, Actions). Columns can carry a
/// different star weight per tab, and the inventory/supplier columns only exist on Expenses.
/// </summary>
public partial class ProductsTableColumnWidths : TableColumnWidthsBase
{
    private bool _isExpensesTab = true;

    [ObservableProperty]
    private double _nameColumnWidth = 150;

    [ObservableProperty]
    private double _typeColumnWidth = 80;

    [ObservableProperty]
    private double _descriptionColumnWidth = 150;

    [ObservableProperty]
    private double _categoryColumnWidth = 100;

    [ObservableProperty]
    private double _supplierColumnWidth = 100;

    [ObservableProperty]
    private double _reorderColumnWidth = 80;

    [ObservableProperty]
    private double _overstockColumnWidth = 80;

    [ObservableProperty]
    private double _trackInventoryColumnWidth = 80;

    [ObservableProperty]
    private double _actionsColumnWidth = 84;

    public ProductsTableColumnWidths()
    {
        ColumnOrder = ["Name", "Type", "Description", "Category", "Supplier", "Reorder", "Overstock", "TrackInventory", "Actions"];

        RegisterColumn("Name", new TabColumnDef { ExpensesStar = 1.2, RevenueStar = 1.5, MinWidth = 120, PreferredWidth = 150 }, w => NameColumnWidth = w);
        RegisterColumn("Type", new TabColumnDef { ExpensesStar = 0.6, RevenueStar = 0.8, MinWidth = 60, PreferredWidth = 80 }, w => TypeColumnWidth = w);
        RegisterColumn("Description", new TabColumnDef { ExpensesStar = 1.2, RevenueStar = 2.0, MinWidth = 84, PreferredWidth = 150 }, w => DescriptionColumnWidth = w);
        RegisterColumn("Category", new TabColumnDef { ExpensesStar = 0.8, RevenueStar = 1.0, MinWidth = 80, PreferredWidth = 100 }, w => CategoryColumnWidth = w);
        RegisterColumn("Supplier", new TabColumnDef { ExpensesStar = 0.8, RevenueStar = 0, InRevenue = false, MinWidth = 80, PreferredWidth = 100 }, w => SupplierColumnWidth = w);
        RegisterColumn("Reorder", new TabColumnDef { ExpensesStar = 0.6, RevenueStar = 0, InRevenue = false, MinWidth = 60, PreferredWidth = 80 }, w => ReorderColumnWidth = w);
        RegisterColumn("Overstock", new TabColumnDef { ExpensesStar = 0.6, RevenueStar = 0, InRevenue = false, MinWidth = 60, PreferredWidth = 80 }, w => OverstockColumnWidth = w);
        RegisterColumn("TrackInventory", new TabColumnDef { ExpensesStar = 0.6, RevenueStar = 0, InRevenue = false, MinWidth = 60, PreferredWidth = 80 }, w => TrackInventoryColumnWidth = w);
        RegisterColumn("Actions", new ColumnDef { IsFixed = true, FixedWidth = ActionsWidth(2), MinWidth = ActionsWidth(2) }, w => ActionsColumnWidth = w);

        RecalculateWidths();
    }

    /// <summary>
    /// Switches between the Expenses and Revenue column sets and re-fits the columns.
    /// </summary>
    public void SetTabMode(bool isExpensesTab)
    {
        if (_isExpensesTab == isExpensesTab) return;
        _isExpensesTab = isExpensesTab;
        ResetWidths(); // clears any manual-overflow state and re-fits to the new set
    }

    /// <inheritdoc />
    protected override double GetStarValue(ColumnDef col) =>
        col is TabColumnDef t ? (_isExpensesTab ? t.ExpensesStar : t.RevenueStar) : col.StarValue;

    /// <inheritdoc />
    protected override bool IsInActiveSet(ColumnDef col) =>
        col is not TabColumnDef t || (_isExpensesTab ? t.InExpenses : t.InRevenue);

    /// <summary>
    /// A column definition that carries per-tab star weights and per-tab membership.
    /// </summary>
    private sealed class TabColumnDef : ColumnDef
    {
        public double ExpensesStar { get; init; }
        public double RevenueStar { get; init; }
        public bool InExpenses { get; init; } = true;
        public bool InRevenue { get; init; } = true;
    }
}
