using System.Collections.ObjectModel;
using ArgoBooks.Localization;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Stock Levels page.
/// Displays inventory levels for products across locations.
/// </summary>
public partial class StockLevelsPageViewModel : SortablePageViewModelBase
{
    #region Responsive Header

    /// <summary>
    /// Helper for responsive header layout.
    /// </summary>
    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    #endregion

    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public StockLevelsTableColumnWidths ColumnWidths => App.StockLevelsColumnWidths;

    #endregion

    #region Column Visibility

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("StockLevels", new Dictionary<string, bool>
    {
        ["Product"] = true,
        ["Sku"] = true,
        ["Category"] = true,
        ["Location"] = true,
        ["InStock"] = true,
        ["Reserved"] = true,
        ["Available"] = true,
        ["ReorderPoint"] = true,
        ["Status"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showProductColumn = ColumnDefaults.Load("Product");

    [ObservableProperty]
    private bool _showSkuColumn = ColumnDefaults.Load("Sku");

    [ObservableProperty]
    private bool _showCategoryColumn = ColumnDefaults.Load("Category");

    [ObservableProperty]
    private bool _showLocationColumn = ColumnDefaults.Load("Location");

    [ObservableProperty]
    private bool _showInStockColumn = ColumnDefaults.Load("InStock");

    [ObservableProperty]
    private bool _showReservedColumn = ColumnDefaults.Load("Reserved");

    [ObservableProperty]
    private bool _showAvailableColumn = ColumnDefaults.Load("Available");

    [ObservableProperty]
    private bool _showReorderPointColumn = ColumnDefaults.Load("ReorderPoint");

    [ObservableProperty]
    private bool _showStatusColumn = ColumnDefaults.Load("Status");

    #endregion

    #region Tab Selection

    [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsAllItemsTabSelected => SelectedTabIndex == 0;

    public bool IsLowStockTabSelected => SelectedTabIndex == 1;

    public bool IsOutOfStockTabSelected => SelectedTabIndex == 2;

    public bool IsOverstockTabSelected => SelectedTabIndex == 3;

    /// <summary>
    /// The empty state's title. Only All Items being empty means nothing has been added; the other tabs
    /// being empty is good news about stock.
    /// </summary>
    public string EmptyStateTitle => SelectedTabIndex switch
    {
        1 => "Nothing is running low".Translate(),
        2 => "Nothing is out of stock".Translate(),
        3 => "Nothing is overstocked".Translate(),
        _ => "No inventory items found".Translate()
    };

    public string EmptyStateMessage => SelectedTabIndex switch
    {
        1 => "Items at or below their reorder point will appear here.".Translate(),
        2 => "Items with no stock left will appear here.".Translate(),
        3 => "Items above their overstock threshold will appear here.".Translate(),
        _ => "Add products with inventory tracking to see stock levels here.".Translate()
    };

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAllItemsTabSelected));
        OnPropertyChanged(nameof(IsLowStockTabSelected));
        OnPropertyChanged(nameof(IsOutOfStockTabSelected));
        OnPropertyChanged(nameof(IsOverstockTabSelected));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
        CurrentPage = 1;
        FilterItems();
    }

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterItems();
        });

    [ObservableProperty]
    private string _filterCategory = "All";

    [ObservableProperty]
    private string _filterLocation = "All";

    [ObservableProperty]
    private string _filterStatus = "All";

    #endregion

    #region Statistics

    [ObservableProperty]
    private decimal _totalItems;

    [ObservableProperty]
    private int _inStockCount;

    [ObservableProperty]
    private int _lowStockCount;

    [ObservableProperty]
    private int _outOfStockCount;

    [ObservableProperty]
    private int _overstockCount;

    #endregion

    #region Data Collections

    /// <summary>
    /// All inventory items (unfiltered).
    /// </summary>
    private readonly List<InventoryItem> _allItems = [];

    /// <summary>
    /// Filtered display items for the current view.
    /// </summary>
    public BatchObservableCollection<StockLevelDisplayItem> DisplayItems { get; } = [];

    /// <summary>
    /// Available categories for filter dropdown.
    /// </summary>
    public ObservableCollection<string> AvailableCategories { get; } = ["All"];

    /// <summary>
    /// Available locations for filter dropdown.
    /// </summary>
    public ObservableCollection<string> AvailableLocations { get; } = ["All"];

    /// <summary>
    /// Available status options for filter dropdown.
    /// </summary>
    public ObservableCollection<string> StatusOptions { get; } = new(InventoryStatusExtensions.GetFilterOptions());

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterItems();

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public StockLevelsPageViewModel()
    {
        LoadItems();

        EnableDeferredUndoRefresh(p => p == PageNames.StockLevels, LoadItems);

        // Subscribe to modal events to refresh when items are saved
        if (App.StockLevelsModalsViewModel != null)
        {
            App.StockLevelsModalsViewModel.ItemSaved += OnModalItemSaved;
            App.StockLevelsModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.StockLevelsModalsViewModel.FiltersCleared += OnFiltersCleared;
        }
    }

    /// <summary>
    /// Unsubscribes from the events wired up in the constructor so the VM isn't kept alive (and
    /// reacting) after a company switch.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.StockLevelsModalsViewModel != null)
        {
            App.StockLevelsModalsViewModel.ItemSaved -= OnModalItemSaved;
            App.StockLevelsModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.StockLevelsModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
    }

    /// <summary>
    /// Handles filter applied events from the modals.
    /// </summary>
    private void OnFiltersApplied(object? sender, FilterAppliedEventArgs e)
    {
        FilterCategory = e.Category;
        FilterLocation = e.Location;
        FilterStatus = e.Status;
        CurrentPage = 1;
        FilterItems();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterCategory = "All";
        FilterLocation = "All";
        FilterStatus = "All";
        CurrentPage = 1;
        FilterItems();
    }

    /// <summary>
    /// Handles item saved events from the modals.
    /// </summary>
    private void OnModalItemSaved(object? sender, EventArgs e)
    {
        LoadItems();
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Loads inventory items from the company data.
    /// </summary>
    private void LoadItems()
    {
        _allItems.Clear();
        DisplayItems.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        _allItems.AddRange(companyData.Inventory);

        UpdateStatistics();
        UpdateDropdownOptions();
        FilterItems();
    }

    /// <summary>
    /// Updates the statistics based on current data.
    /// </summary>
    private void UpdateStatistics()
    {
        TotalItems = _allItems.Sum(i => i.InStock);
        InStockCount = _allItems.Count(i => i.CalculateStatus() == InventoryStatus.InStock);
        LowStockCount = _allItems.Count(i => i.CalculateStatus() == InventoryStatus.LowStock);
        OutOfStockCount = _allItems.Count(i => i.CalculateStatus() == InventoryStatus.OutOfStock);
        OverstockCount = _allItems.Count(i => i.CalculateStatus() == InventoryStatus.Overstock);
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
        AvailableCategories.Add("All");

        var categories = companyData.Categories
            .Select(c => c.Name)
            .Distinct()
            .OrderBy(c => c);

        foreach (var category in categories)
        {
            AvailableCategories.Add(category);
        }

        // Update locations
        AvailableLocations.Clear();
        AvailableLocations.Add("All");

        var locations = companyData.Locations
            .Select(l => l.Name)
            .Distinct()
            .OrderBy(l => l);

        foreach (var location in locations)
        {
            AvailableLocations.Add(location);
        }
    }

    /// <summary>
    /// Refreshes the items from the data source.
    /// </summary>
    [RelayCommand]
    private void RefreshItems()
    {
        LoadItems();
    }

    /// <summary>
    /// Filters items based on current tab, search query, and filters.
    /// </summary>
    private void FilterItems()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        IEnumerable<InventoryItem> filtered = _allItems;

        // Apply tab filter
        filtered = SelectedTabIndex switch
        {
            1 => filtered.Where(i => i.CalculateStatus() == InventoryStatus.LowStock),
            2 => filtered.Where(i => i.CalculateStatus() == InventoryStatus.OutOfStock),
            3 => filtered.Where(i => i.CalculateStatus() == InventoryStatus.Overstock),
            _ => filtered
        };

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, i => companyData.GetProduct(i.ProductId) is { } product ? [product.Name, product.Id, i.Sku] : [])
                .ToList();
        }

        if (FilterCategory != "All")
        {
            var categoryProducts = (companyData.Categories)
                .Where(c => c.Name == FilterCategory)
                .SelectMany(c => (companyData.Products).Where(p => p.CategoryId == c.Id))
                .Select(p => p.Id)
                .ToHashSet();

            filtered = filtered.Where(i => categoryProducts.Contains(i.ProductId));
        }

        if (FilterLocation != "All")
        {
            var locationId = (companyData.Locations)
                .FirstOrDefault(l => l.Name == FilterLocation)?.Id;

            if (locationId != null)
            {
                filtered = filtered.Where(i => i.LocationId == locationId);
            }
        }

        // Apply status filter (from filter modal, not tab)
        if (FilterStatus != "All")
        {
            var targetStatus = FilterStatus switch
            {
                "In Stock" => InventoryStatus.InStock,
                "Low Stock" => InventoryStatus.LowStock,
                "Out of Stock" => InventoryStatus.OutOfStock,
                "Overstock" => InventoryStatus.Overstock,
                _ => (InventoryStatus?)null
            };

            if (targetStatus.HasValue)
            {
                filtered = filtered.Where(i => i.CalculateStatus() == targetStatus.Value);
            }
        }

        // Create display items (using CompanyData's O(1) cached lookups instead of scanning the
        // full Products/Locations/Categories lists per inventory row).
        var displayItems = filtered.Select(item =>
        {
            var product = companyData.GetProduct(item.ProductId);
            var location = companyData.GetLocation(item.LocationId);
            var category = product?.CategoryId is { Length: > 0 } categoryId ? companyData.GetCategory(categoryId) : null;
            var status = item.CalculateStatus();

            return new StockLevelDisplayItem
            {
                Id = item.Id,
                ProductId = item.ProductId,
                ProductName = product?.Name ?? "Unknown Product",
                Sku = item.Sku,
                CategoryName = category?.Name ?? "-",
                LocationName = location?.Name ?? "Default",
                UnitOfMeasure = product?.UnitOfMeasure ?? StockUnits.Each,
                InStock = item.InStock,
                Reserved = item.Reserved,
                Available = item.Available,
                ReorderPoint = item.ReorderPoint,
                Status = status,
                StatusText = GetStatusText(status),
                StatusColor = GetStatusColor(status),
                StatusBackground = GetStatusBackground(status),
                LastUpdated = item.LastUpdated,
                IsHighlighted = item.Id == HighlightTransactionId
            };
        }).ToList();

        // Apply sorting
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<StockLevelDisplayItem, object?>>
                {
                    ["Product"] = i => i.ProductName,
                    ["Sku"] = i => i.Sku,
                    ["Category"] = i => i.CategoryName,
                    ["Location"] = i => i.LocationName,
                    ["InStock"] = i => i.InStock,
                    ["Reserved"] = i => i.Reserved,
                    ["Available"] = i => i.Available,
                    ["ReorderPoint"] = i => i.ReorderPoint,
                    ["Status"] = i => i.Status
                },
                i => i.ProductName);
        }

        NavigateToHighlightedItem(displayItems, x => x.Id);

        var pagedItems = Paginate(displayItems, "item");

        DisplayItems.ReplaceAll(pagedItems);
    }

    private static string GetStatusText(InventoryStatus status) => status switch
    {
        InventoryStatus.InStock => "In Stock",
        InventoryStatus.LowStock => "Low Stock",
        InventoryStatus.OutOfStock => "Out of Stock",
        InventoryStatus.Overstock => "Overstock",
        _ => "Unknown"
    };

    private static string GetStatusColor(InventoryStatus status) => status switch
    {
        InventoryStatus.InStock => AppColors.SuccessText,
        InventoryStatus.LowStock => AppColors.WarningText,
        InventoryStatus.OutOfStock => AppColors.Error,
        InventoryStatus.Overstock => AppColors.VioletHover,
        _ => AppColors.GrayText
    };

    private static string GetStatusBackground(InventoryStatus status) => status switch
    {
        InventoryStatus.InStock => AppColors.SuccessLight,
        InventoryStatus.LowStock => AppColors.WarningLight,
        InventoryStatus.OutOfStock => AppColors.ErrorLight,
        InventoryStatus.Overstock => AppColors.VioletLight,
        _ => AppColors.GrayLightest
    };

    #endregion

    #region Adjust Stock

    /// <summary>
    /// Opens the adjust stock modal for an item.
    /// </summary>
    [RelayCommand]
    private void OpenAdjustStockModal(StockLevelDisplayItem? item)
    {
        if (item == null) return;
        App.StockLevelsModalsViewModel?.OpenAdjustStockModal(item.Id, item.ProductName, item.InStock);
    }

    /// <summary>
    /// Opens the transfer modal to move this row's stock to another location.
    /// </summary>
    [RelayCommand]
    private void OpenTransferStockModal(StockLevelDisplayItem? item)
    {
        if (item == null) return;
        App.StockLevelsModalsViewModel?.OpenTransferStockModal(item.Id);
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Opens the filter modal via the modals ViewModel.
    /// </summary>
    [RelayCommand]
    private void OpenFilterModal()
    {
        App.StockLevelsModalsViewModel?.OpenFilterModal(
            AvailableCategories,
            AvailableLocations,
            FilterCategory,
            FilterLocation,
            FilterStatus);
    }

    #endregion

    #region Add Item Modal

    /// <summary>
    /// Opens the add item modal.
    /// </summary>
    [RelayCommand]
    private void OpenAddItemModal()
    {
        App.StockLevelsModalsViewModel?.OpenAddItemModalCommand.Execute(null);
    }

    #endregion
}

/// <summary>
/// Display model for stock level items in the UI.
/// </summary>
public partial class StockLevelDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _productName = string.Empty;

    [ObservableProperty]
    private string _sku = string.Empty;

    [ObservableProperty]
    private string _categoryName = string.Empty;

    [ObservableProperty]
    private string _locationName = string.Empty;

    [ObservableProperty]
    private decimal _inStock;

    [ObservableProperty]
    private decimal _reserved;

    [ObservableProperty]
    private decimal _available;

    [ObservableProperty]
    private decimal _reorderPoint;

    [ObservableProperty]
    private string _unitOfMeasure = StockUnits.Each;

    public string InStockText => StockUnits.Format(InStock, UnitOfMeasure);
    public string ReservedText => StockUnits.Format(Reserved);
    public string AvailableText => StockUnits.Format(Available, UnitOfMeasure);
    public string ReorderPointText => StockUnits.Format(ReorderPoint);

    [ObservableProperty]
    private InventoryStatus _status;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _statusColor = string.Empty;

    [ObservableProperty]
    private string _statusBackground = string.Empty;

    [ObservableProperty]
    private DateTime _lastUpdated;

    [ObservableProperty]
    private bool _isHighlighted;
}
