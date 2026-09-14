using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Helpers;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Products/Services page.
/// </summary>
public partial class ProductsPageViewModel : SortablePageViewModelBase
{
    #region Responsive Header

    /// <summary>
    /// Responsive header helper for adaptive layout.
    /// </summary>
    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    #endregion

    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public ProductsTableColumnWidths ColumnWidths => App.ProductsColumnWidths;

    #endregion

    #region Column Visibility

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Products", new Dictionary<string, bool>
    {
        ["Name"] = true,
        ["Type"] = true,
        ["Description"] = true,
        ["Category"] = true,
        ["Supplier"] = true,
        ["Reorder"] = false,
        ["Overstock"] = false,
        ["TrackInventory"] = false,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showNameColumn = ColumnDefaults.Load("Name");

    [ObservableProperty]
    private bool _showTypeColumn = ColumnDefaults.Load("Type");

    [ObservableProperty]
    private bool _showDescriptionColumn = ColumnDefaults.Load("Description");

    [ObservableProperty]
    private bool _showCategoryColumn = ColumnDefaults.Load("Category");

    [ObservableProperty]
    private bool _showSupplierColumn = ColumnDefaults.Load("Supplier");

    [ObservableProperty]
    private bool _showReorderColumn = ColumnDefaults.Load("Reorder");

    [ObservableProperty]
    private bool _showOverstockColumn = ColumnDefaults.Load("Overstock");

    [ObservableProperty]
    private bool _showTrackInventoryColumn = ColumnDefaults.Load("TrackInventory");

    #endregion

    #region Tab Selection

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Gets whether the Expenses tab is selected (products/services purchased).
    /// </summary>
    public bool IsExpensesTabSelected => SelectedTabIndex == 0;

    /// <summary>
    /// Gets whether the Revenue tab is selected (products/services sold).
    /// </summary>
    public bool IsRevenueTabSelected => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsExpensesTabSelected));
        OnPropertyChanged(nameof(IsRevenueTabSelected));
        OnPropertyChanged(nameof(CanAddProduct));
        ColumnWidths.SetTabMode(IsExpensesTabSelected);
        FilterProducts();
    }

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterProducts();
        });

    [ObservableProperty]
    private string _filterItemType = "All";

    /// <summary>Category chosen in the filter modal; null means all.</summary>
    [ObservableProperty]
    private string? _filterCategoryId;

    /// <summary>Supplier chosen in the filter modal; null means all.</summary>
    [ObservableProperty]
    private string? _filterSupplierId;

    #endregion

    #region Plan Status and Product Limits

    /// <summary>
    /// Products are always unlimited, no free-tier limit.
    /// </summary>
    public bool CanAddProduct => true;

    #endregion

    #region Products Collection

    /// <summary>
    /// All products (unfiltered).
    /// </summary>
    private readonly List<Product> _allProducts = [];

    /// <summary>
    /// Expense products (purchased) for display.
    /// </summary>
    public BatchObservableCollection<ProductDisplayItem> ExpenseProducts { get; } = [];

    /// <summary>
    /// Revenue products (sold) for display.
    /// </summary>
    public BatchObservableCollection<ProductDisplayItem> RevenueProducts { get; } = [];

    /// <summary>
    /// Gets the current tab's products for display.
    /// </summary>
    public BatchObservableCollection<ProductDisplayItem> CurrentProducts =>
        IsExpensesTabSelected ? ExpenseProducts : RevenueProducts;

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterProducts();

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public ProductsPageViewModel()
    {
        LoadProducts();

        EnableDeferredUndoRefresh(IsThisPage, LoadProducts);

        // Subscribe to product modal events to refresh data
        if (App.ProductModalsViewModel != null)
        {
            App.ProductModalsViewModel.ProductSaved += OnProductSaved;
            App.ProductModalsViewModel.ProductDeleted += OnProductDeleted;
            App.ProductModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.ProductModalsViewModel.FiltersCleared += OnFiltersCleared;
        }
    }

    /// <summary>
    /// Unsubscribes from the events wired up in the constructor so the VM isn't kept alive (and
    /// reacting) after a company switch.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.ProductModalsViewModel != null)
        {
            App.ProductModalsViewModel.ProductSaved -= OnProductSaved;
            App.ProductModalsViewModel.ProductDeleted -= OnProductDeleted;
            App.ProductModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.ProductModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
    }

    /// <summary>
    /// The sidebar opens this page on a tab, under its own page name.
    /// </summary>
    private static bool IsThisPage(string? pageName) =>
        pageName is PageNames.Products or PageNames.ExpenseProducts or PageNames.RevenueProducts;

    private void OnProductSaved(object? sender, EventArgs e)
    {
        LoadProducts();
    }

    private void OnProductDeleted(object? sender, EventArgs e)
    {
        LoadProducts();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        var modals = App.ProductModalsViewModel;
        if (modals != null)
        {
            FilterItemType = modals.FilterItemType;
            FilterCategoryId = modals.FilterCategory?.Id;
            FilterSupplierId = modals.FilterSupplier?.Id;
        }
        CurrentPage = 1;
        FilterProducts();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterItemType = "All";
        FilterCategoryId = null;
        FilterSupplierId = null;
        SearchQuery = null;
        CurrentPage = 1;
        FilterProducts();
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Loads products from the company data.
    /// </summary>
    private void LoadProducts()
    {
        _allProducts.Clear();
        ExpenseProducts.Clear();
        RevenueProducts.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Products == null)
            return;

        _allProducts.AddRange(companyData.Products);
        FilterProducts();
    }

    /// <summary>
    /// Filters products based on current tab, search query, and filters.
    /// </summary>
    private void FilterProducts()
    {
        var targetType = IsExpensesTabSelected ? CategoryType.Expense : CategoryType.Revenue;
        var targetCollection = IsExpensesTabSelected ? ExpenseProducts : RevenueProducts;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
        {
            targetCollection.Clear();
            return;
        }

        // Get categories for the current tab type
        var categoryIds = companyData.Categories
            .Where(c => c.Type == targetType)
            .Select(c => c.Id)
            .ToHashSet();

        // Filter products by category type
        IEnumerable<Product> filtered = _allProducts
            .Where(p => string.IsNullOrEmpty(p.CategoryId) || categoryIds.Contains(p.CategoryId));

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, p => [p.Name, p.Id, p.Sku, p.Description])
                .ToList();
        }

        // Apply item type filter on the product's own type
        if (FilterItemType != "All")
        {
            filtered = filtered.Where(p => p.ItemType == FilterItemType);
        }

        // A category picked on the other tab doesn't narrow this one.
        if (FilterCategoryId != null && categoryIds.Contains(FilterCategoryId))
        {
            filtered = filtered.Where(p => p.CategoryId == FilterCategoryId);
        }

        if (FilterSupplierId != null)
        {
            filtered = filtered.Where(p => p.SupplierId == FilterSupplierId);
        }

        var displayItems = filtered.Select(product =>
        {
            var category = companyData.Categories.FirstOrDefault(c => c.Id == product.CategoryId);
            var supplier = companyData.Suppliers.FirstOrDefault(s => s.Id == product.SupplierId);

            return new ProductDisplayItem
            {
                Id = product.Id,
                Name = product.Name,
                Sku = product.Sku,
                Description = string.IsNullOrWhiteSpace(product.Description) ? "-" : product.Description,
                ItemType = product.ItemType,
                CategoryName = category?.Name ?? "-",
                SupplierName = supplier?.Name ?? "-",
                ReorderPoint = product.TrackInventory && product.ReorderPoint > 0 ? product.ReorderPoint.ToString() : "-",
                OverstockThreshold = product.TrackInventory && product.OverstockThreshold > 0 ? product.OverstockThreshold.ToString() : "-",
                UnitPrice = product.UnitPrice,
                CostPrice = product.CostPrice,
                TrackInventory = product.TrackInventory,
                IsHighlighted = product.Id == HighlightTransactionId
            };
        }).ToList();

        // Apply sorting (only if not searching, since search has its own relevance sorting)
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<ProductDisplayItem, object?>>
                {
                    ["Name"] = p => p.Name,
                    ["Type"] = p => p.ItemType,
                    ["Description"] = p => p.Description,
                    ["Category"] = p => p.CategoryName,
                    ["Supplier"] = p => p.SupplierName
                },
                p => p.Name);
        }

        NavigateToHighlightedItem(displayItems, x => x.Id);

        var pagedProducts = Paginate(displayItems, "product");

        targetCollection.ReplaceAll(pagedProducts);

        OnPropertyChanged(nameof(CurrentProducts));
    }

    #endregion

    #region Add Product

    /// <summary>
    /// Opens the Add Product modal.
    /// </summary>
    [RelayCommand]
    private void OpenAddModal()
    {
        App.ProductModalsViewModel?.OpenAddModal(IsExpensesTabSelected);
    }

    #endregion

    #region Edit Product

    /// <summary>
    /// Opens the Edit Product modal.
    /// </summary>
    [RelayCommand]
    private void OpenEditModal(ProductDisplayItem? item)
    {
        App.ProductModalsViewModel?.OpenEditModal(item, IsExpensesTabSelected);
    }

    #endregion

    #region Delete Product

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void OpenDeleteConfirm(ProductDisplayItem? item)
    {
        App.ProductModalsViewModel?.OpenDeleteConfirm(item);
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    [RelayCommand]
    private void OpenFilterModal()
    {
        App.ProductModalsViewModel?.OpenFilterModal(IsExpensesTabSelected);
    }

    #endregion
}

/// <summary>
/// Display model for products in the UI.
/// </summary>
public partial class ProductDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _sku = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _itemType = "Product";

    [ObservableProperty]
    private string _categoryName = string.Empty;

    [ObservableProperty]
    private string _supplierName = string.Empty;

    [ObservableProperty]
    private string _reorderPoint = string.Empty;

    [ObservableProperty]
    private string _overstockThreshold = string.Empty;

    [ObservableProperty]
    private decimal _unitPrice;

    [ObservableProperty]
    private decimal _costPrice;

    [ObservableProperty]
    private bool _trackInventory;

    [ObservableProperty]
    private bool _isHighlighted;
}

/// <summary>
/// Category option for dropdown.
/// </summary>
public class CategoryOption : NamedOption
{
}

/// <summary>
/// Supplier option for dropdown.
/// </summary>
public class SupplierOption : NamedOption
{
}
