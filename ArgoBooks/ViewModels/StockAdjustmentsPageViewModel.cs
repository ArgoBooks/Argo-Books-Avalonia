using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core;
using ArgoBooks.Helpers;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Stock Adjustments page.
/// Displays history of all stock level adjustments.
/// </summary>
public partial class StockAdjustmentsPageViewModel : SortablePageViewModelBase
{
    #region Statistics

    [ObservableProperty]
    private int _totalAdjustments;

    [ObservableProperty]
    private decimal _totalAdded;

    [ObservableProperty]
    private decimal _totalRemoved;

    [ObservableProperty]
    private decimal _netChange;

    #endregion

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
    public StockAdjustmentsTableColumnWidths ColumnWidths => App.StockAdjustmentsColumnWidths;

    #endregion

    #region Column Visibility

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("StockAdjustments", new Dictionary<string, bool>
    {
        ["Date"] = true,
        ["Reference"] = true,
        ["Product"] = true,
        ["Location"] = true,
        ["Quantity"] = true,
        ["Previous"] = true,
        ["New"] = true,
        ["Reason"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showDateColumn = ColumnDefaults.Load("Date");

    [ObservableProperty]
    private bool _showReferenceColumn = ColumnDefaults.Load("Reference");

    [ObservableProperty]
    private bool _showProductColumn = ColumnDefaults.Load("Product");

    [ObservableProperty]
    private bool _showLocationColumn = ColumnDefaults.Load("Location");

    [ObservableProperty]
    private bool _showQuantityColumn = ColumnDefaults.Load("Quantity");

    [ObservableProperty]
    private bool _showPreviousColumn = ColumnDefaults.Load("Previous");

    [ObservableProperty]
    private bool _showNewColumn = ColumnDefaults.Load("New");

    [ObservableProperty]
    private bool _showReasonColumn = ColumnDefaults.Load("Reason");

    #endregion

    #region Tabs

    [ObservableProperty]
    private string _activeTab = "All";

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Tab options for filtering.
    /// </summary>
    public ObservableCollection<string> TabOptions { get; } = new(AdjustmentTypeExtensions.GetFilterOptions());

    partial void OnActiveTabChanged(string value)
    {
        CurrentPage = 1;
        FilterAdjustments();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        ActiveTab = value switch
        {
            0 => "All",
            1 => "Add",
            2 => "Remove",
            3 => "Set",
            _ => "All"
        };
    }

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterAdjustments();
        });

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private string _filterLocation = "All";

    [ObservableProperty]
    private string _filterProduct = "All";

    #endregion

    #region Adjustments Collection

    /// <summary>
    /// All adjustments (unfiltered).
    /// </summary>
    private readonly List<StockAdjustment> _allAdjustments = [];

    /// <summary>
    /// Adjustments for display in the table.
    /// </summary>
    public BatchObservableCollection<StockAdjustmentDisplayItem> Adjustments { get; } = [];

    /// <summary>
    /// Location options for filter.
    /// </summary>
    public ObservableCollection<string> LocationOptions { get; } = ["All"];

    /// <summary>
    /// Product options for filter.
    /// </summary>
    public ObservableCollection<string> ProductOptions { get; } = ["All"];

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterAdjustments();

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public StockAdjustmentsPageViewModel()
    {
        // Set default sort to date descending (most recent first)
        SortColumn = "Date";
        SortDirection = SortDirection.Descending;

        LoadAdjustments();

        EnableDeferredUndoRefresh(p => p == PageNames.StockAdjustments, LoadAdjustments);

        // Subscribe to modal events to refresh when adjustments are made
        if (App.StockLevelsModalsViewModel != null)
        {
            App.StockLevelsModalsViewModel.ItemSaved += OnAdjustmentMade;
        }

        // Subscribe to stock adjustments modal events
        if (App.StockAdjustmentsModalsViewModel != null)
        {
            App.StockAdjustmentsModalsViewModel.AdjustmentSaved += OnAdjustmentMade;
            App.StockAdjustmentsModalsViewModel.AdjustmentDeleted += OnAdjustmentMade;
            App.StockAdjustmentsModalsViewModel.FiltersApplied += OnFiltersApplied;
        }

        // Subscribe to timezone/time format changes to refresh time display. Use a named handler (not
        // a lambda) so Cleanup can unsubscribe it; TimeZoneService is static, so a leaked lambda would
        // keep this VM alive for the whole process.
        TimeZoneService.TimeSettingsChanged += OnTimeSettingsChanged;
    }

    private void OnTimeSettingsChanged(object? sender, EventArgs e) => FilterAdjustments();

    /// <summary>
    /// Unsubscribes from the events wired up in the constructor so the VM isn't kept alive (and
    /// reacting) after a company switch.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.StockLevelsModalsViewModel != null)
            App.StockLevelsModalsViewModel.ItemSaved -= OnAdjustmentMade;
        if (App.StockAdjustmentsModalsViewModel != null)
        {
            App.StockAdjustmentsModalsViewModel.AdjustmentSaved -= OnAdjustmentMade;
            App.StockAdjustmentsModalsViewModel.AdjustmentDeleted -= OnAdjustmentMade;
            App.StockAdjustmentsModalsViewModel.FiltersApplied -= OnFiltersApplied;
        }
        TimeZoneService.TimeSettingsChanged -= OnTimeSettingsChanged;
    }

    /// <summary>
    /// Handles filter applied events from the modals.
    /// </summary>
    private void OnFiltersApplied(object? sender, AdjustmentsFilterAppliedEventArgs e)
    {
        StartDate = e.StartDate?.DateTime;
        EndDate = e.EndDate?.DateTime;
        FilterProduct = e.Product;
        // Note: FilterType maps to adjustment type filter
        CurrentPage = 1;
        FilterAdjustments();
    }

    /// <summary>
    /// Handles adjustment events from modals.
    /// </summary>
    private void OnAdjustmentMade(object? sender, EventArgs e)
    {
        LoadAdjustments();
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Loads adjustments from the company data.
    /// </summary>
    private void LoadAdjustments()
    {
        _allAdjustments.Clear();
        Adjustments.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.StockAdjustments == null)
            return;

        _allAdjustments.AddRange(companyData.StockAdjustments);

        LoadFilterOptions();
        UpdateStatistics();
        FilterAdjustments();
    }

    /// <summary>
    /// Loads filter options from the data.
    /// </summary>
    private void LoadFilterOptions()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        LocationOptions.Clear();
        LocationOptions.Add("All");
        var locations = companyData.Locations.Select(l => l.Name).Distinct().OrderBy(n => n).ToList();
        foreach (var location in locations)
        {
            LocationOptions.Add(location);
        }

        ProductOptions.Clear();
        ProductOptions.Add("All");
        var products = companyData.Products.Select(p => p.Name).Distinct().OrderBy(n => n).ToList();
        foreach (var product in products)
        {
            ProductOptions.Add(product);
        }
    }

    /// <summary>
    /// Updates the statistics based on current data.
    /// </summary>
    private void UpdateStatistics()
    {
        TotalAdjustments = _allAdjustments.Count;

        var adds = _allAdjustments.Where(a => a.AdjustmentType == AdjustmentType.Add);
        var removes = _allAdjustments.Where(a => a.AdjustmentType == AdjustmentType.Remove);

        TotalAdded = adds.Sum(a => a.Quantity);
        TotalRemoved = removes.Sum(a => a.Quantity);
        NetChange = TotalAdded - TotalRemoved;
    }

    /// <summary>
    /// Refreshes the adjustments from the data source.
    /// </summary>
    [RelayCommand]
    private void RefreshAdjustments()
    {
        LoadAdjustments();
    }

    /// <summary>
    /// Filters adjustments based on search query, tab, and filters.
    /// </summary>
    private void FilterAdjustments()
    {
        var companyData = App.CompanyManager?.CompanyData;
        var inventory = companyData?.Inventory ?? [];
        var products = companyData?.Products ?? [];
        var locations = companyData?.Locations ?? [];

        IEnumerable<StockAdjustment> filtered = _allAdjustments;

        // Apply tab filter
        if (ActiveTab != "All")
        {
            var tabType = ActiveTab switch
            {
                "Add" => AdjustmentType.Add,
                "Remove" => AdjustmentType.Remove,
                "Set" => AdjustmentType.Set,
                _ => (AdjustmentType?)null
            };

            if (tabType.HasValue)
            {
                filtered = filtered.Where(a => a.AdjustmentType == tabType.Value);
            }
        }

        // Apply date range filter
        if (StartDate.HasValue)
        {
            filtered = filtered.Where(a => a.Timestamp.Date >= StartDate.Value.Date);
        }
        if (EndDate.HasValue)
        {
            filtered = filtered.Where(a => a.Timestamp.Date <= EndDate.Value.Date);
        }

        if (FilterLocation != "All")
        {
            filtered = filtered.Where(a =>
            {
                var invItem = inventory.FirstOrDefault(i => i.Id == a.InventoryItemId);
                if (invItem == null) return false;
                var location = locations.FirstOrDefault(l => l.Id == invItem.LocationId);
                return location?.Name == FilterLocation;
            });
        }

        if (FilterProduct != "All")
        {
            filtered = filtered.Where(a =>
            {
                var invItem = inventory.FirstOrDefault(i => i.Id == a.InventoryItemId);
                if (invItem == null) return false;
                var product = products.FirstOrDefault(p => p.Id == invItem.ProductId);
                return product?.Name == FilterProduct;
            });
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, a =>
                {
                    var invItem = inventory.FirstOrDefault(i => i.Id == a.InventoryItemId);
                    var product = invItem != null ? products.FirstOrDefault(p => p.Id == invItem.ProductId) : null;
                    var location = invItem != null ? locations.FirstOrDefault(l => l.Id == invItem.LocationId) : null;
                    return [a.Id, product?.Name, location?.Name, a.Reason, a.ReferenceNumber];
                })
                .ToList();
        }

        var displayItems = filtered.Select(adjustment =>
        {
            var invItem = inventory.FirstOrDefault(i => i.Id == adjustment.InventoryItemId);
            var product = invItem != null ? products.FirstOrDefault(p => p.Id == invItem.ProductId) : null;
            var location = invItem != null ? locations.FirstOrDefault(l => l.Id == invItem.LocationId) : null;

            // Convert UTC timestamp to user's timezone and format using user's time format preference
            var localTime = TimeZoneService.ConvertToUserTimeZone(adjustment.Timestamp);

            return new StockAdjustmentDisplayItem
            {
                Id = adjustment.Id,
                Date = adjustment.Timestamp,
                DateDisplay = localTime.ToString("MMM dd, yyyy"),
                TimeDisplay = TimeZoneService.FormatTime(localTime),
                Reference = adjustment.ReferenceNumber ?? "-",
                ProductId = invItem?.ProductId ?? "",
                ProductName = product?.Name ?? "Unknown Product",
                ProductSku = invItem?.Sku ?? product?.Sku ?? "",
                LocationId = invItem?.LocationId ?? "",
                LocationName = location?.Name ?? "Default",
                AdjustmentType = adjustment.AdjustmentType,
                TypeDisplay = adjustment.AdjustmentType.ToString(),
                Quantity = adjustment.Quantity,
                PreviousStock = adjustment.PreviousStock,
                NewStock = adjustment.NewStock,
                Reason = string.IsNullOrWhiteSpace(adjustment.Reason) ? "-" : adjustment.Reason,
                UserId = adjustment.UserId ?? "",
                UserDisplay = string.IsNullOrWhiteSpace(adjustment.UserId) ? "System" : adjustment.UserId,
                IsAutoGenerated = adjustment.IsAutoGenerated
            };
        }).ToList();

        // Apply sorting
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<StockAdjustmentDisplayItem, object?>>
                {
                    ["Date"] = a => a.Date,
                    ["Reference"] = a => a.Reference,
                    ["Product"] = a => a.ProductName,
                    ["Location"] = a => a.LocationName,
                    ["Type"] = a => a.TypeDisplay,
                    ["Quantity"] = a => a.Quantity,
                    ["Previous"] = a => a.PreviousStock,
                    ["New"] = a => a.NewStock,
                    ["Reason"] = a => a.Reason,
                    ["User"] = a => a.UserDisplay
                },
                a => a.Date);
        }

        var pagedAdjustments = Paginate(displayItems, "adjustment");

        Adjustments.ReplaceAll(pagedAdjustments);
    }

    #endregion

    #region Modal Commands

    /// <summary>
    /// Opens the Add Adjustment modal.
    /// </summary>
    [RelayCommand]
    private void OpenAddModal()
    {
        App.StockAdjustmentsModalsViewModel?.OpenAddModal();
    }

    /// <summary>
    /// Opens the View Adjustment modal.
    /// </summary>
    [RelayCommand]
    private void ViewAdjustment(StockAdjustmentDisplayItem? item)
    {
        if (item == null) return;
        App.StockAdjustmentsModalsViewModel?.OpenViewModal(item);
    }

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void OpenDeleteConfirm(StockAdjustmentDisplayItem? item)
    {
        if (item == null) return;
        App.StockAdjustmentsModalsViewModel?.OpenDeleteConfirm(item);
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Opens the filter modal via the modals ViewModel.
    /// </summary>
    [RelayCommand]
    private void OpenFilterModal()
    {
        App.StockAdjustmentsModalsViewModel?.OpenFilterModal(
            ProductOptions,
            StartDate,
            EndDate,
            FilterProduct,
            "All");
    }

    #endregion

    #region Tab Commands

    /// <summary>
    /// Switches to the specified tab.
    /// </summary>
    [RelayCommand]
    private void SwitchTab(string tab)
    {
        ActiveTab = tab;
    }

    #endregion
}

/// <summary>
/// Display model for stock adjustments in the UI.
/// </summary>
public partial class StockAdjustmentDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    private string _dateDisplay = string.Empty;

    [ObservableProperty]
    private string _timeDisplay = string.Empty;

    [ObservableProperty]
    private string _reference = string.Empty;

    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _productName = string.Empty;

    [ObservableProperty]
    private string _productSku = string.Empty;

    [ObservableProperty]
    private string _locationId = string.Empty;

    [ObservableProperty]
    private string _locationName = string.Empty;

    [ObservableProperty]
    private AdjustmentType _adjustmentType;

    [ObservableProperty]
    private string _typeDisplay = string.Empty;

    [ObservableProperty]
    private decimal _quantity;

    [ObservableProperty]
    private decimal _previousStock;

    [ObservableProperty]
    private decimal _newStock;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private string _userId = string.Empty;

    [ObservableProperty]
    private string _userDisplay = string.Empty;

    /// <summary>
    /// Whether this adjustment was auto-created by the system (not manually deletable).
    /// </summary>
    [ObservableProperty]
    private bool _isAutoGenerated;

    /// <summary>
    /// Gets the type badge color based on adjustment type.
    /// </summary>
    public string TypeColor => AdjustmentType switch
    {
        AdjustmentType.Add => AppColors.Success,
        AdjustmentType.Remove => AppColors.ExpenseRed,
        AdjustmentType.Set => AppColors.Primary,
        _ => AppColors.GrayMedium
    };

    /// <summary>
    /// Gets the type badge background color based on adjustment type.
    /// </summary>
    public string TypeBackground => AdjustmentType switch
    {
        AdjustmentType.Add => AppColors.SuccessLight,
        AdjustmentType.Remove => AppColors.ErrorLight,
        AdjustmentType.Set => AppColors.PrimaryLight,
        _ => AppColors.GrayLightest
    };

    /// <summary>
    /// Gets the quantity change display with sign.
    /// </summary>
    public string QuantityDisplay => AdjustmentType switch
    {
        AdjustmentType.Add => $"+{StockUnits.Format(Quantity)}",
        AdjustmentType.Remove => $"-{StockUnits.Format(Quantity)}",
        _ => StockUnits.Format(Quantity)
    };

    public string PreviousStockText => StockUnits.Format(PreviousStock);

    public string NewStockText => StockUnits.Format(NewStock);

    /// <summary>
    /// Gets the quantity display color.
    /// </summary>
    public string QuantityColor => AdjustmentType switch
    {
        AdjustmentType.Add => AppColors.Success,
        AdjustmentType.Remove => AppColors.ExpenseRed,
        _ => AppColors.GrayMedium
    };
}
