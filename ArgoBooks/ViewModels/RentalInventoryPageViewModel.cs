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

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("RentalInventory", new Dictionary<string, bool>
    {
        ["Item"] = true,
        ["Status"] = true,
        ["InStock"] = true,
        ["DailyRate"] = true,
        ["WeeklyRate"] = true,
        ["Deposit"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showItemColumn = ColumnDefaults.Load("Item");

    [ObservableProperty]
    private bool _showStatusColumn = ColumnDefaults.Load("Status");

    [ObservableProperty]
    private bool _showInStockColumn = ColumnDefaults.Load("InStock");

    [ObservableProperty]
    private bool _showDailyRateColumn = ColumnDefaults.Load("DailyRate");

    [ObservableProperty]
    private bool _showWeeklyRateColumn = ColumnDefaults.Load("WeeklyRate");

    [ObservableProperty]
    private bool _showDepositColumn = ColumnDefaults.Load("Deposit");

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

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterItems();

    #endregion

    #region Constructor

    public RentalInventoryPageViewModel()
    {
        LoadItems();

        EnableDeferredUndoRefresh(p => p == PageNames.RentalInventory, LoadItems);

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
        if (App.RentalInventoryModalsViewModel != null)
        {
            App.RentalInventoryModalsViewModel.ItemSaved -= OnItemSaved;
            App.RentalInventoryModalsViewModel.ItemDeleted -= OnItemDeleted;
            App.RentalInventoryModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.RentalInventoryModalsViewModel.FiltersCleared -= OnFiltersCleared;
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
            FilterDailyRateMin = modals.FilterDailyRateMin;
            FilterDailyRateMax = modals.FilterDailyRateMax;
            FilterAvailability = modals.FilterAvailability;
            ActiveFilterCount = modals.ActiveFilterCount;
        }
        CurrentPage = 1;
        FilterItems();
    }

    protected override void ClearTableFilters() => App.RentalInventoryModalsViewModel?.ClearFiltersCommand.Execute(null);

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        ActiveFilterCount = 0;
        FilterStatus = "All";
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

        var rentals = companyData?.Rentals ?? [];
        var total = 0;
        var available = 0;
        var rentedOut = 0;
        var maintenance = 0;

        foreach (var item in _allItems)
        {
            var inStock = inventoryLookup.TryGetValue(item.InventoryItemId, out var inv) ? (int)inv.InStock : 0;
            var unitsOut = RentalBookings.UnitsOut(rentals, item.Id);
            total += inStock + unitsOut;
            rentedOut += unitsOut;
            if (item.Status == EntityStatus.Inactive)
                maintenance += inStock;
            else
                available += inStock;
        }

        TotalItems = total;
        AvailableItems = available;
        RentedOutItems = rentedOut;
        MaintenanceItems = maintenance;
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
            inventoryLookup.TryGetValue(item.InventoryItemId, out var inv) ? (int)inv.InStock : 0;

        var unitsOut = _allItems.GroupBy(i => i.Id)
            .ToDictionary(g => g.Key, g => RentalBookings.UnitsOut(companyData?.Rentals ?? [], g.Key));

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
                .RankBySearch(SearchQuery, i => [ResolveName(i), i.Id])
                .ToList();
        }

        if (FilterStatus != "All")
        {
            filtered = FilterStatus switch
            {
                "Available" => filtered.Where(i => ResolveInStock(i) > 0 && i.Status == EntityStatus.Active),
                "In Maintenance" => filtered.Where(i => i.Status == EntityStatus.Inactive),
                "All Rented" => filtered.Where(i => ResolveInStock(i) == 0 && unitsOut[i.Id] > 0 && i.Status == EntityStatus.Active),
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
                         inStock > 0 ? "Available" :
                         unitsOut[item.Id] > 0 ? "All Rented" : "Out of Stock";

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
                IsAvailable = isAvailable,
                RentedOut = unitsOut[item.Id],
                CanRentOut = item.Status == EntityStatus.Active && inStock + unitsOut[item.Id] > 0
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

        var pagedItems = Paginate(displayItems, "item");

        Items.ReplaceAll(pagedItems);
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

    [ObservableProperty]
    private int _rentedOut;

    [ObservableProperty]
    private bool _canRentOut;

    public string DailyRateFormatted => CurrencyService.Format(DailyRate);
    public string WeeklyRateFormatted => CurrencyService.Format(WeeklyRate);
    public string DepositFormatted => CurrencyService.Format(SecurityDeposit);
}
