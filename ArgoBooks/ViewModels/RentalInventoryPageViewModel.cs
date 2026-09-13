using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Enums;
using ArgoBooks.Helpers;
using ArgoBooks.Services;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Rental Inventory page.
/// </summary>
public partial class RentalInventoryPageViewModel : SortablePageViewModelBase
{
    #region Responsive Header

    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    #endregion

    #region Statistics

    [ObservableProperty]
    private int _totalItems;

    [ObservableProperty]
    private int _availableItems;

    [ObservableProperty]
    private int _rentedOutItems;

    [ObservableProperty]
    private int _maintenanceItems;

    #endregion

    #region Column Visibility and Widths

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public RentalInventoryTableColumnWidths ColumnWidths => App.RentalInventoryColumnWidths;

    [ObservableProperty]
    private bool _showItemColumn = ColumnVisibilityHelper.Load("RentalInventory", "Item", true);

    [ObservableProperty]
    private bool _showStatusColumn = ColumnVisibilityHelper.Load("RentalInventory", "Status", true);

    [ObservableProperty]
    private bool _showInStockColumn = ColumnVisibilityHelper.Load("RentalInventory", "InStock", true);

    [ObservableProperty]
    private bool _showDailyRateColumn = ColumnVisibilityHelper.Load("RentalInventory", "DailyRate", true);

    [ObservableProperty]
    private bool _showWeeklyRateColumn = ColumnVisibilityHelper.Load("RentalInventory", "WeeklyRate", true);

    [ObservableProperty]
    private bool _showDepositColumn = ColumnVisibilityHelper.Load("RentalInventory", "Deposit", true);

    partial void OnShowItemColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Item", value); ColumnVisibilityHelper.Save("RentalInventory", "Item", value); }
    partial void OnShowStatusColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Status", value); ColumnVisibilityHelper.Save("RentalInventory", "Status", value); }
    partial void OnShowInStockColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("InStock", value); ColumnVisibilityHelper.Save("RentalInventory", "InStock", value); }
    partial void OnShowDailyRateColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("DailyRate", value); ColumnVisibilityHelper.Save("RentalInventory", "DailyRate", value); }
    partial void OnShowWeeklyRateColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("WeeklyRate", value); ColumnVisibilityHelper.Save("RentalInventory", "WeeklyRate", value); }
    partial void OnShowDepositColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Deposit", value); ColumnVisibilityHelper.Save("RentalInventory", "Deposit", value); }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("RentalInventory");
        ShowItemColumn = true;
        ShowStatusColumn = true;
        ShowInStockColumn = true;
        ShowDailyRateColumn = true;
        ShowWeeklyRateColumn = true;
        ShowDepositColumn = true;
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
    private string _filterStatus = "All";

    [ObservableProperty]
    private string? _filterSupplier;

    [ObservableProperty]
    private string? _filterDailyRateMin;

    [ObservableProperty]
    private string? _filterDailyRateMax;

    [ObservableProperty]
    private string _filterAvailability = "All";

    #endregion

    #region Items Collection

    private readonly List<RentalItem> _allItems = [];

    public BatchObservableCollection<RentalItemDisplayItem> Items { get; } = [];

    public ObservableCollection<string> StatusOptions { get; } = ["All", "Available", "In Maintenance", "All Rented"];

    public ObservableCollection<string> AvailabilityOptions { get; } = ["All", "Available Only", "Unavailable Only"];

    #endregion

    #region Pagination

    [ObservableProperty]
    private string _paginationText = "0 items";

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterItems();

    #endregion

    #region Constructor

    public RentalInventoryPageViewModel()
    {
        LoadItems();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

        if (App.RentalInventoryModalsViewModel != null)
        {
            App.RentalInventoryModalsViewModel.ItemSaved += OnItemSaved;
            App.RentalInventoryModalsViewModel.ItemDeleted += OnItemDeleted;
            App.RentalInventoryModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.RentalInventoryModalsViewModel.FiltersCleared += OnFiltersCleared;
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
        if (App.RentalInventoryModalsViewModel != null)
        {
            App.RentalInventoryModalsViewModel.ItemSaved -= OnItemSaved;
            App.RentalInventoryModalsViewModel.ItemDeleted -= OnItemDeleted;
            App.RentalInventoryModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.RentalInventoryModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
    }

    private bool _needsRefresh;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (App.NavigationService?.CurrentPageName != PageNames.RentalInventory)
        {
            _needsRefresh = true;
            return;
        }
        LoadItems();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName == PageNames.RentalInventory && _needsRefresh)
        {
            _needsRefresh = false;
            LoadItems();
        }
    }

    private void OnItemSaved(object? sender, EventArgs e)
    {
        LoadItems();
    }

    private void OnItemDeleted(object? sender, EventArgs e)
    {
        LoadItems();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        var modals = App.RentalInventoryModalsViewModel;
        if (modals != null)
        {
            FilterStatus = modals.FilterStatus;
            FilterSupplier = modals.FilterSupplier;
            FilterDailyRateMin = modals.FilterDailyRateMin;
            FilterDailyRateMax = modals.FilterDailyRateMax;
            FilterAvailability = modals.FilterAvailability;
        }
        CurrentPage = 1;
        FilterItems();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterStatus = "All";
        FilterSupplier = null;
        FilterDailyRateMin = null;
        FilterDailyRateMax = null;
        FilterAvailability = "All";
        SearchQuery = null;
        CurrentPage = 1;
        FilterItems();
    }

    #endregion

    #region Data Loading

    private void LoadItems()
    {
        _allItems.Clear();
        Items.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.RentalInventory == null)
            return;

        _allItems.AddRange(companyData.RentalInventory);
        UpdateStatistics();
        FilterItems();
    }

    private void UpdateStatistics()
    {
        var companyData = App.CompanyManager?.CompanyData;
        var inventoryLookup = companyData?.Inventory.ToDictionary(inv => inv.Id) ?? [];

        var totalInStock = 0;
        var activeInStock = 0;
        var maintenanceInStock = 0;

        foreach (var item in _allItems)
        {
            var inStock = inventoryLookup.TryGetValue(item.InventoryItemId, out var inv) ? inv.InStock : 0;
            totalInStock += inStock;
            if (item.Status == EntityStatus.Inactive)
                maintenanceInStock += inStock;
            else
                activeInStock += inStock;
        }

        TotalItems = totalInStock;
        AvailableItems = activeInStock;
        RentedOutItems = 0; // Rented quantity is not tracked on the item
        MaintenanceItems = maintenanceInStock;
    }

    [RelayCommand]
    private void RefreshItems()
    {
        LoadItems();
    }

    private void FilterItems()
    {
        var companyData = App.CompanyManager?.CompanyData;
        var inventoryLookup = companyData?.Inventory.ToDictionary(inv => inv.Id) ?? [];
        var productLookup = companyData?.Products.ToDictionary(p => p.Id) ?? [];

        // Helper to resolve the product name through the chain:
        // RentalItem -> InventoryItem -> Product -> Name
        string ResolveName(RentalItem item)
        {
            if (!inventoryLookup.TryGetValue(item.InventoryItemId, out var inv)) return "Unknown";
            if (!productLookup.TryGetValue(inv.ProductId, out var product)) return "Unknown";
            return product.Name;
        }

        // Helper to get InStock from linked InventoryItem
        int ResolveInStock(RentalItem item) =>
            inventoryLookup.TryGetValue(item.InventoryItemId, out var inv) ? inv.InStock : 0;

        // Helper to get SupplierId from linked Product
        string? ResolveSupplierId(RentalItem item)
        {
            if (!inventoryLookup.TryGetValue(item.InventoryItemId, out var inv)) return null;
            if (!productLookup.TryGetValue(inv.ProductId, out var product)) return null;
            return product.SupplierId;
        }

        IEnumerable<RentalItem> filtered = _allItems;

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .Select(i => new
                {
                    Item = i,
                    NameScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, ResolveName(i)),
                    IdScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, i.Id)
                })
                .Where(x => x.NameScore >= 0 || x.IdScore >= 0)
                .OrderByDescending(x => Math.Max(x.NameScore, x.IdScore))
                .Select(x => x.Item)
                .ToList();
        }

        if (FilterStatus != "All")
        {
            filtered = FilterStatus switch
            {
                "Available" => filtered.Where(i => ResolveInStock(i) > 0 && i.Status == EntityStatus.Active),
                "In Maintenance" => filtered.Where(i => i.Status == EntityStatus.Inactive),
                "All Rented" => filtered.Where(i => ResolveInStock(i) == 0 && i.Status == EntityStatus.Active),
                _ => filtered
            };
        }

        if (FilterAvailability != "All")
        {
            filtered = FilterAvailability switch
            {
                "Available Only" => filtered.Where(i => ResolveInStock(i) > 0 && i.Status == EntityStatus.Active),
                "Unavailable Only" => filtered.Where(i => ResolveInStock(i) == 0 || i.Status != EntityStatus.Active),
                _ => filtered
            };
        }

        if (!string.IsNullOrWhiteSpace(FilterSupplier) && FilterSupplier != "All Suppliers")
        {
            var supplier = companyData?.Suppliers.FirstOrDefault(s => s.Name == FilterSupplier);
            if (supplier != null)
            {
                filtered = filtered.Where(i => ResolveSupplierId(i) == supplier.Id);
            }
        }

        if (decimal.TryParse(FilterDailyRateMin, out var minRate))
        {
            filtered = filtered.Where(i => i.DailyRate >= minRate);
        }
        if (decimal.TryParse(FilterDailyRateMax, out var maxRate))
        {
            filtered = filtered.Where(i => i.DailyRate <= maxRate);
        }

        var displayItems = filtered.Select(item =>
        {
            var inStock = ResolveInStock(item);
            var supplierName = "-";
            var supplierId = ResolveSupplierId(item);
            if (supplierId != null)
            {
                var supplier = companyData?.Suppliers.FirstOrDefault(s => s.Id == supplierId);
                supplierName = supplier?.Name ?? "-";
            }

            var isAvailable = inStock > 0 && item.Status == EntityStatus.Active;
            var status = item.Status == EntityStatus.Inactive ? "In Maintenance" :
                         inStock == 0 ? "All Rented" : "Available";

            return new RentalItemDisplayItem
            {
                Id = item.Id,
                Name = ResolveName(item),
                SupplierName = supplierName,
                Status = status,
                InStock = inStock,
                DailyRate = item.DailyRate,
                WeeklyRate = item.WeeklyRate,
                MonthlyRate = item.MonthlyRate,
                SecurityDeposit = item.SecurityDeposit,
                IsAvailable = isAvailable
            };
        }).ToList();

        // Apply sorting (only if not searching, since search has its own relevance sorting)
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<RentalItemDisplayItem, object?>>
                {
                    ["Name"] = i => i.Name,
                    ["Supplier"] = i => i.SupplierName,
                    ["Status"] = i => i.Status,
                    ["InStock"] = i => i.InStock,
                    ["DailyRate"] = i => i.DailyRate,
                    ["WeeklyRate"] = i => i.WeeklyRate,
                    ["Deposit"] = i => i.SecurityDeposit
                },
                i => i.Name);
        }

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Apply pagination
        var pagedItems = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Items.ReplaceAll(pagedItems);
    }

    protected override void UpdatePageNumbers()
    {
        PageNumbers.Clear();
        var startPage = Math.Max(1, CurrentPage - 2);
        var endPage = Math.Min(TotalPages, startPage + 4);
        startPage = Math.Max(1, endPage - 4);

        for (var i = startPage; i <= endPage; i++)
        {
            PageNumbers.Add(i);
        }
    }

    private void UpdatePaginationText(int totalCount)
    {
        PaginationText = PaginationTextHelper.FormatPaginationText(
            totalCount, CurrentPage, PageSize, TotalPages, "item");
    }

    #endregion

    #region Modal Commands

    [RelayCommand]
    private void OpenAddModal()
    {
        App.RentalInventoryModalsViewModel?.OpenAddModal();
    }

    [RelayCommand]
    private void OpenEditModal(RentalItemDisplayItem? item)
    {
        App.RentalInventoryModalsViewModel?.OpenEditModal(item);
    }

    [RelayCommand]
    private void OpenDeleteConfirm(RentalItemDisplayItem? item)
    {
        App.RentalInventoryModalsViewModel?.OpenDeleteConfirm(item);
    }

    [RelayCommand]
    private void OpenFilterModal()
    {
        App.RentalInventoryModalsViewModel?.OpenFilterModal();
    }

    [RelayCommand]
    private void OpenRentOutModal(RentalItemDisplayItem? item)
    {
        App.RentalInventoryModalsViewModel?.OpenRentOutModal(item);
    }

    [RelayCommand]
    private void OpenAvailabilityModal(RentalItemDisplayItem? item)
    {
        App.RentalAvailabilityModalViewModel?.OpenForItem(item);
    }

    #endregion
}

/// <summary>
/// Display model for rental items in the UI.
/// </summary>
public partial class RentalItemDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _supplierName = string.Empty;

    [ObservableProperty]
    private string _status = "Available";

    [ObservableProperty]
    private int _inStock;

    [ObservableProperty]
    private decimal _dailyRate;

    [ObservableProperty]
    private decimal _weeklyRate;

    [ObservableProperty]
    private decimal _monthlyRate;

    [ObservableProperty]
    private decimal _securityDeposit;

    [ObservableProperty]
    private bool _isAvailable;

    public string DailyRateFormatted => CurrencyService.Format(DailyRate);
    public string WeeklyRateFormatted => CurrencyService.Format(WeeklyRate);
    public string DepositFormatted => CurrencyService.Format(SecurityDeposit);
}
