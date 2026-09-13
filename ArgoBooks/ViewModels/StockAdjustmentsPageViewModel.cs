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

    [ObservableProperty]
    private bool _showDateColumn = ColumnVisibilityHelper.Load("StockAdjustments", "Date", true);

    [ObservableProperty]
    private bool _showReferenceColumn = ColumnVisibilityHelper.Load("StockAdjustments", "Reference", true);

    [ObservableProperty]
    private bool _showProductColumn = ColumnVisibilityHelper.Load("StockAdjustments", "Product", true);

    [ObservableProperty]
    private bool _showLocationColumn = ColumnVisibilityHelper.Load("StockAdjustments", "Location", true);

    [ObservableProperty]
    private bool _showQuantityColumn = ColumnVisibilityHelper.Load("StockAdjustments", "Quantity", true);

    [ObservableProperty]
    private bool _showPreviousColumn = ColumnVisibilityHelper.Load("StockAdjustments", "Previous", true);

    [ObservableProperty]
    private bool _showNewColumn = ColumnVisibilityHelper.Load("StockAdjustments", "New", true);

    [ObservableProperty]
    private bool _showReasonColumn = ColumnVisibilityHelper.Load("StockAdjustments", "Reason", true);

    partial void OnShowDateColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Date", value); ColumnVisibilityHelper.Save("StockAdjustments", "Date", value); }
    partial void OnShowReferenceColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Reference", value); ColumnVisibilityHelper.Save("StockAdjustments", "Reference", value); }
    partial void OnShowProductColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Product", value); ColumnVisibilityHelper.Save("StockAdjustments", "Product", value); }
    partial void OnShowLocationColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Location", value); ColumnVisibilityHelper.Save("StockAdjustments", "Location", value); }
    partial void OnShowQuantityColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Quantity", value); ColumnVisibilityHelper.Save("StockAdjustments", "Quantity", value); }
    partial void OnShowPreviousColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Previous", value); ColumnVisibilityHelper.Save("StockAdjustments", "Previous", value); }
    partial void OnShowNewColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("New", value); ColumnVisibilityHelper.Save("StockAdjustments", "New", value); }
    partial void OnShowReasonColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Reason", value); ColumnVisibilityHelper.Save("StockAdjustments", "Reason", value); }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("StockAdjustments");
        ShowDateColumn = true;
        ShowReferenceColumn = true;
        ShowProductColumn = true;
        ShowLocationColumn = true;
        ShowQuantityColumn = true;
        ShowPreviousColumn = true;
        ShowNewColumn = true;
        ShowReasonColumn = true;
    }

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

    [ObservableProperty]
    private string _paginationText = "0 adjustments";

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

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

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
        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
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
    /// Handles undo/redo state changes by refreshing the adjustments.
    /// </summary>
    private bool _needsRefresh;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (App.NavigationService?.CurrentPageName != PageNames.StockAdjustments)
        {
            _needsRefresh = true;
            return;
        }
        LoadAdjustments();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName == PageNames.StockAdjustments && _needsRefresh)
        {
            _needsRefresh = false;
            LoadAdjustments();
        }
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
                .Select(a =>
                {
                    var invItem = inventory.FirstOrDefault(i => i.Id == a.InventoryItemId);
                    var product = invItem != null ? products.FirstOrDefault(p => p.Id == invItem.ProductId) : null;
                    var location = invItem != null ? locations.FirstOrDefault(l => l.Id == invItem.LocationId) : null;

                    return new
                    {
                        Adjustment = a,
                        IdScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, a.Id),
                        ProductScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, product?.Name ?? ""),
                        LocationScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, location?.Name ?? ""),
                        ReasonScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, a.Reason),
                        RefScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, a.ReferenceNumber ?? "")
                    };
                })
                .Where(x => x.IdScore >= 0 || x.ProductScore >= 0 || x.LocationScore >= 0 || x.ReasonScore >= 0 || x.RefScore >= 0)
                .OrderByDescending(x => Math.Max(Math.Max(x.IdScore, x.ProductScore), Math.Max(Math.Max(x.LocationScore, x.ReasonScore), x.RefScore)))
                .Select(x => x.Adjustment)
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

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Apply pagination and add to collection
        var pagedAdjustments = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Adjustments.ReplaceAll(pagedAdjustments);
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
            totalCount, CurrentPage, PageSize, TotalPages, "adjustment");
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
