using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private bool _showNameColumn = ColumnVisibilityHelper.Load("Products", "Name", true);

    [ObservableProperty]
    private bool _showTypeColumn = ColumnVisibilityHelper.Load("Products", "Type", true);

    [ObservableProperty]
    private bool _showDescriptionColumn = ColumnVisibilityHelper.Load("Products", "Description", true);

    [ObservableProperty]
    private bool _showCategoryColumn = ColumnVisibilityHelper.Load("Products", "Category", true);

    [ObservableProperty]
    private bool _showSupplierColumn = ColumnVisibilityHelper.Load("Products", "Supplier", true);

    [ObservableProperty]
    private bool _showReorderColumn = ColumnVisibilityHelper.Load("Products", "Reorder", false);

    [ObservableProperty]
    private bool _showOverstockColumn = ColumnVisibilityHelper.Load("Products", "Overstock", false);

    [ObservableProperty]
    private bool _showTrackInventoryColumn = ColumnVisibilityHelper.Load("Products", "TrackInventory", false);

    partial void OnShowNameColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Name", value); ColumnVisibilityHelper.Save("Products", "Name", value); }
    partial void OnShowTypeColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Type", value); ColumnVisibilityHelper.Save("Products", "Type", value); }
    partial void OnShowDescriptionColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Description", value); ColumnVisibilityHelper.Save("Products", "Description", value); }
    partial void OnShowCategoryColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Category", value); ColumnVisibilityHelper.Save("Products", "Category", value); }
    partial void OnShowSupplierColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Supplier", value); ColumnVisibilityHelper.Save("Products", "Supplier", value); }
    partial void OnShowReorderColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Reorder", value); ColumnVisibilityHelper.Save("Products", "Reorder", value); }
    partial void OnShowOverstockColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Overstock", value); ColumnVisibilityHelper.Save("Products", "Overstock", value); }
    partial void OnShowTrackInventoryColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("TrackInventory", value); ColumnVisibilityHelper.Save("Products", "TrackInventory", value); }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("Products");
        ShowNameColumn = true;
        ShowTypeColumn = true;
        ShowDescriptionColumn = true;
        ShowCategoryColumn = true;
        ShowSupplierColumn = true;
        ShowReorderColumn = false;
        ShowOverstockColumn = false;
        ShowTrackInventoryColumn = false;
    }

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

    [ObservableProperty]
    private string? _filterCategory;

    [ObservableProperty]
    private string? _filterSupplier;

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

    /// <summary>
    /// Available categories for filter/modal dropdown.
    /// </summary>
    public ObservableCollection<CategoryOption> AvailableCategories { get; } = [];

    /// <summary>
    /// Available suppliers for filter/modal dropdown.
    /// </summary>
    public ObservableCollection<SupplierOption> AvailableSuppliers { get; } = [];

    #endregion

    #region Pagination

    [ObservableProperty]
    private string _paginationText = "0 products";

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

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

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
        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
        if (App.ProductModalsViewModel != null)
        {
            App.ProductModalsViewModel.ProductSaved -= OnProductSaved;
            App.ProductModalsViewModel.ProductDeleted -= OnProductDeleted;
            App.ProductModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.ProductModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
    }

    /// <summary>
    /// Handles undo/redo state changes by refreshing the products.
    /// </summary>
    private bool _needsRefresh;

    /// <summary>
    /// The sidebar opens this page on a tab, under its own page name.
    /// </summary>
    private static bool IsThisPage(string? pageName) =>
        pageName is PageNames.Products or PageNames.ExpenseProducts or PageNames.RevenueProducts;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (!IsThisPage(App.NavigationService?.CurrentPageName))
        {
            _needsRefresh = true;
            return;
        }
        LoadProducts();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (IsThisPage(e.PageName) && _needsRefresh)
        {
            _needsRefresh = false;
            LoadProducts();
        }
    }

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
            FilterCategory = modals.FilterCategory;
            FilterSupplier = modals.FilterSupplier;
        }
        CurrentPage = 1;
        FilterProducts();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterItemType = "All";
        FilterCategory = null;
        FilterSupplier = null;
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
        UpdateDropdownOptions();
        FilterProducts();
    }

    /// <summary>
    /// Updates the dropdown options from company data.
    /// </summary>
    private void UpdateDropdownOptions()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Update categories
        AvailableCategories.Clear();
        AvailableCategories.Add(new CategoryOption { Id = null, Name = "All Categories" });

        var targetType = IsExpensesTabSelected ? CategoryType.Expense : CategoryType.Revenue;
        var categories = companyData.Categories
            .Where(c => c.Type == targetType)
            .OrderBy(c => c.Name);

        foreach (var cat in categories)
        {
            AvailableCategories.Add(new CategoryOption { Id = cat.Id, Name = cat.Name });
        }

        // Update suppliers
        AvailableSuppliers.Clear();
        AvailableSuppliers.Add(new SupplierOption { Id = null, Name = "All Suppliers" });

        foreach (var supplier in companyData.Suppliers.OrderBy(s => s.Name))
        {
            AvailableSuppliers.Add(new SupplierOption { Id = supplier.Id, Name = supplier.Name });
        }
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

        if (!string.IsNullOrWhiteSpace(FilterCategory) && FilterCategory != "All Categories")
        {
            var categoryOption = AvailableCategories.FirstOrDefault(c => c.Name == FilterCategory);
            if (categoryOption?.Id != null)
            {
                filtered = filtered.Where(p => p.CategoryId == categoryOption.Id);
            }
        }

        if (!string.IsNullOrWhiteSpace(FilterSupplier) && FilterSupplier != "All Suppliers")
        {
            var supplierOption = AvailableSuppliers.FirstOrDefault(s => s.Name == FilterSupplier);
            if (supplierOption?.Id != null)
            {
                filtered = filtered.Where(p => p.SupplierId == supplierOption.Id);
            }
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

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePaginationText(totalCount);

        // Apply pagination and add to collection
        var pagedProducts = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        targetCollection.ReplaceAll(pagedProducts);

        OnPropertyChanged(nameof(CurrentProducts));
    }

    private void UpdatePaginationText(int totalCount)
    {
        PaginationText = PaginationTextHelper.FormatPaginationText(
            totalCount, CurrentPage, PageSize, TotalPages, "product");
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
public class CategoryOption
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>
/// Supplier option for dropdown.
/// </summary>
public class SupplierOption
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}
