using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core;
using ArgoBooks.Helpers;
using ArgoBooks.Services;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Purchase Orders page.
/// Displays and manages purchase orders to suppliers.
/// </summary>
public partial class PurchaseOrdersPageViewModel : SortablePageViewModelBase
{
    #region Statistics

    [ObservableProperty]
    private int _totalOrders;

    [ObservableProperty]
    private int _pendingOrders;

    [ObservableProperty]
    private int _onOrderCount;

    [ObservableProperty]
    private string _totalValue = "$0";

    #endregion

    #region Table Column Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public PurchaseOrdersTableColumnWidths ColumnWidths => App.PurchaseOrdersColumnWidths;

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("PurchaseOrders", new Dictionary<string, bool>
    {
        ["PONumber"] = true,
        ["Date"] = true,
        ["Supplier"] = true,
        ["Items"] = true,
        ["Total"] = true,
        ["Status"] = true,
        ["Expected"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showPONumberColumn = ColumnDefaults.Load("PONumber");

    [ObservableProperty]
    private bool _showDateColumn = ColumnDefaults.Load("Date");

    [ObservableProperty]
    private bool _showSupplierColumn = ColumnDefaults.Load("Supplier");

    [ObservableProperty]
    private bool _showItemsColumn = ColumnDefaults.Load("Items");

    [ObservableProperty]
    private bool _showTotalColumn = ColumnDefaults.Load("Total");

    [ObservableProperty]
    private bool _showStatusColumn = ColumnDefaults.Load("Status");

    [ObservableProperty]
    private bool _showExpectedColumn = ColumnDefaults.Load("Expected");

    #endregion

    #region Tabs

    [ObservableProperty]
    private string _activeTab = "All";

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Tab options for filtering.
    /// </summary>
    public ObservableCollection<string> TabOptions { get; } = ["All", "Pending", "Approved", "On Order", "Received"];

    partial void OnActiveTabChanged(string value)
    {
        CurrentPage = 1;
        FilterOrders();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        ActiveTab = value switch
        {
            0 => "All",
            1 => "Pending",
            2 => "Approved",
            3 => "On Order",
            4 => "Received",
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
            FilterOrders();
        });

    #endregion

    #region Orders Collection

    /// <summary>
    /// All orders (unfiltered).
    /// </summary>
    private readonly List<PurchaseOrder> _allOrders = [];

    /// <summary>
    /// Orders for display in the table.
    /// </summary>
    public BatchObservableCollection<PurchaseOrderDisplayItem> Orders { get; } = [];

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterOrders();

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public PurchaseOrdersPageViewModel()
    {
        // Set default sort to date descending (most recent first)
        SortColumn = "Date";
        SortDirection = SortDirection.Descending;

        LoadOrders();

        EnableDeferredUndoRefresh(p => p == PageNames.PurchaseOrders, LoadOrders);

        // Refresh totals + row displays when the display currency changes (mirrors the Payments page),
        // so the currency-aware TotalDisplay/TotalValue recompute instead of showing stale amounts.
        CurrencyService.CurrencyChanged += OnCurrencyChanged;

        // Subscribe to modal events to refresh when orders are saved
        if (App.PurchaseOrdersModalsViewModel != null)
        {
            App.PurchaseOrdersModalsViewModel.OrderSaved += OnOrderSaved;
            App.PurchaseOrdersModalsViewModel.OrderDeleted += OnOrderDeleted;
            App.PurchaseOrdersModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.PurchaseOrdersModalsViewModel.FiltersCleared += OnFiltersCleared;
        }
    }

    /// <summary>
    /// Handles filters applied event from modals.
    /// </summary>
    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        ActiveFilterCount = App.PurchaseOrdersModalsViewModel?.ActiveFilterCount ?? 0;
        CurrentPage = 1;
        FilterOrders();
    }

    protected override void ClearTableFilters() => App.PurchaseOrdersModalsViewModel?.ClearFiltersCommand.Execute(null);

    /// <summary>
    /// Handles filters cleared event from modals.
    /// </summary>
    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        ActiveFilterCount = 0;
        SearchQuery = null;
        CurrentPage = 1;
        FilterOrders();
    }

    /// <summary>
    /// Refreshes the total stat and the per-row displays when the display currency changes, so the
    /// currency-aware TotalValue/TotalDisplay reconvert to the new currency.
    /// </summary>
    private void OnCurrencyChanged(object? sender, EventArgs e)
    {
        UpdateStatistics();
        FilterOrders();
    }

    /// <summary>
    /// Handles order saved events from modals.
    /// </summary>
    private void OnOrderSaved(object? sender, EventArgs e)
    {
        LoadOrders();
    }

    /// <summary>
    /// Handles order deleted events from modals.
    /// </summary>
    private void OnOrderDeleted(object? sender, EventArgs e)
    {
        LoadOrders();
    }

    /// <summary>
    /// Unsubscribes from the events wired up in the constructor so the VM isn't kept alive (and
    /// reacting) after a company switch. Mirrors the Payments/Revenue/Expenses pages.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        CurrencyService.CurrencyChanged -= OnCurrencyChanged;
        if (App.PurchaseOrdersModalsViewModel != null)
        {
            App.PurchaseOrdersModalsViewModel.OrderSaved -= OnOrderSaved;
            App.PurchaseOrdersModalsViewModel.OrderDeleted -= OnOrderDeleted;
            App.PurchaseOrdersModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.PurchaseOrdersModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Loads orders from the company data.
    /// </summary>
    private void LoadOrders()
    {
        _allOrders.Clear();
        Orders.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.PurchaseOrders == null)
            return;

        _allOrders.AddRange(companyData.PurchaseOrders);

        UpdateStatistics();
        FilterOrders();
    }

    /// <summary>
    /// Updates the statistics based on current data.
    /// </summary>
    private void UpdateStatistics()
    {
        TotalOrders = _allOrders.Count;
        PendingOrders = _allOrders.Count(o => o.Status == PurchaseOrderStatus.Pending);
        OnOrderCount = _allOrders.Count(o => o.Status == PurchaseOrderStatus.OnOrder || o.Status == PurchaseOrderStatus.Sent);
        // Sum in USD (the normalized base) so mixed-currency POs aren't added as if same-currency,
        // then render in the display currency at today's rate. Pending POs contribute 0 until they
        // heal (Calculations.md §3).
        // Convert each PO at its OWN order date before summing (Calculations.md Rule 3a).
        TotalValue = CurrencyService.TrySumDisplayFromUSD(
            _allOrders, o => o.Total, o => o.OriginalCurrency, o => o.TotalUSD, o => o.OrderDate, out var poTotalDisplay)
            ? CurrencyService.Format(poTotalDisplay)
            : CurrencyService.PendingMarker;
    }

    /// <summary>
    /// Filters orders based on search query, tab, and filters.
    /// </summary>
    private void FilterOrders()
    {
        var companyData = App.CompanyManager?.CompanyData;
        var suppliers = companyData?.Suppliers ?? [];

        IEnumerable<PurchaseOrder> filtered = _allOrders;

        // Apply tab filter
        if (ActiveTab != "All")
        {
            var tabStatus = ActiveTab switch
            {
                "Pending" => new[] { PurchaseOrderStatus.Pending },
                "Approved" => new[] { PurchaseOrderStatus.Approved },
                "On Order" => new[] { PurchaseOrderStatus.OnOrder, PurchaseOrderStatus.Sent },
                "Received" => new[] { PurchaseOrderStatus.Received, PurchaseOrderStatus.PartiallyReceived },
                _ => Array.Empty<PurchaseOrderStatus>()
            };

            if (tabStatus.Length > 0)
            {
                filtered = filtered.Where(o => tabStatus.Contains(o.Status));
            }
        }

        // Get filter values from modals ViewModel
        var modals = App.PurchaseOrdersModalsViewModel;
        var startDate = modals?.FilterStartDate?.DateTime;
        var endDate = modals?.FilterEndDate?.DateTime;
        var filterSupplier = modals?.FilterSupplier ?? "All";
        var filterStatus = modals?.FilterStatus ?? "All";

        // Apply date range filter
        if (startDate.HasValue)
        {
            filtered = filtered.Where(o => o.OrderDate.Date >= startDate.Value.Date);
        }
        if (endDate.HasValue)
        {
            filtered = filtered.Where(o => o.OrderDate.Date <= endDate.Value.Date);
        }

        if (!string.IsNullOrEmpty(filterSupplier) && filterSupplier != "All")
        {
            filtered = filtered.Where(o =>
            {
                var supplier = suppliers.FirstOrDefault(s => s.Id == o.SupplierId);
                return supplier?.Name == filterSupplier;
            });
        }

        if (!string.IsNullOrEmpty(filterStatus) && filterStatus != "All")
        {
            var statusEnum = PurchaseOrderStatusExtensions.ParsePurchaseOrderStatus(filterStatus);

            if (statusEnum.HasValue)
            {
                filtered = filtered.Where(o => o.Status == statusEnum.Value);
            }
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, o => [o.Id, o.PoNumber, suppliers.FirstOrDefault(s => s.Id == o.SupplierId)?.Name, o.Notes])
                .ToList();
        }

        var displayItems = filtered.Select(order =>
        {
            var supplier = suppliers.FirstOrDefault(s => s.Id == order.SupplierId);

            return new PurchaseOrderDisplayItem
            {
                Id = order.Id,
                PoNumber = string.IsNullOrEmpty(order.PoNumber) ? order.Id : order.PoNumber,
                OrderDate = order.OrderDate,
                DateDisplay = order.OrderDate.ToString("MMM dd, yyyy"),
                SupplierId = order.SupplierId,
                SupplierName = supplier?.Name ?? "Unknown Supplier",
                ItemCount = order.LineItems.Count,
                Subtotal = order.Subtotal,
                ShippingCost = order.ShippingCost,
                Total = order.Total,
                // Currency-aware like the Payments list: convert the order's original-currency total
                // to the display currency at its order date, and show "Pending" when that exact-date
                // rate is unavailable (a future-dated PO whose conversion hasn't healed yet).
                TotalDisplay = CurrencyService.FormatWithOriginal(
                    order.Total, order.OriginalCurrency, order.EffectiveTotalUSD, order.OrderDate),
                OriginalCurrency = order.OriginalCurrency,
                Status = order.Status,
                StatusDisplay = FormatStatus(order.Status),
                ExpectedDeliveryDate = order.ExpectedDeliveryDate,
                ExpectedDisplay = order.ExpectedDeliveryDate.ToString("MMM dd, yyyy"),
                Notes = order.Notes,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
                IsHighlighted = order.Id == HighlightTransactionId
            };
        }).ToList();

        // Apply sorting
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<PurchaseOrderDisplayItem, object?>>
                {
                    ["PONumber"] = o => o.PoNumber,
                    ["Date"] = o => o.OrderDate,
                    ["Supplier"] = o => o.SupplierName,
                    ["Items"] = o => o.ItemCount,
                    ["Total"] = o => o.Total,
                    ["Status"] = o => o.StatusDisplay,
                    ["Expected"] = o => o.ExpectedDeliveryDate
                },
                o => o.OrderDate);
        }

        NavigateToHighlightedItem(displayItems, x => x.Id);

        var pagedOrders = Paginate(displayItems, "order");

        Orders.ReplaceAll(pagedOrders);
    }

    private static string FormatStatus(PurchaseOrderStatus status)
    {
        return status switch
        {
            PurchaseOrderStatus.Draft => "Draft",
            PurchaseOrderStatus.Pending => "Pending",
            PurchaseOrderStatus.Approved => "Approved",
            PurchaseOrderStatus.Sent => "Sent",
            PurchaseOrderStatus.OnOrder => "On Order",
            PurchaseOrderStatus.PartiallyReceived => "Partial",
            PurchaseOrderStatus.Received => "Received",
            PurchaseOrderStatus.Cancelled => "Cancelled",
            _ => status.ToString()
        };
    }

    #endregion

    #region Modal Commands

    /// <summary>
    /// Opens the Create Purchase Order modal.
    /// </summary>
    [RelayCommand]
    private void OpenAddModal()
    {
        App.PurchaseOrdersModalsViewModel?.OpenAddModal();
    }

    /// <summary>
    /// Opens the View Order modal.
    /// </summary>
    [RelayCommand]
    private void ViewOrder(PurchaseOrderDisplayItem? item)
    {
        if (item == null) return;
        App.PurchaseOrdersModalsViewModel?.OpenViewModal(item);
    }

    /// <summary>
    /// Opens the Edit Order modal.
    /// </summary>
    [RelayCommand]
    private void EditOrder(PurchaseOrderDisplayItem? item)
    {
        if (item == null) return;
        App.PurchaseOrdersModalsViewModel?.OpenEditModal(item);
    }

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void OpenDeleteConfirm(PurchaseOrderDisplayItem? item)
    {
        if (item == null) return;
        App.PurchaseOrdersModalsViewModel?.OpenDeleteConfirm(item);
    }

    /// <summary>
    /// Approves a purchase order.
    /// </summary>
    [RelayCommand]
    private void ApproveOrder(PurchaseOrderDisplayItem? item)
    {
        if (item == null) return;

        var companyData = App.CompanyManager?.CompanyData;

        var order = companyData?.PurchaseOrders.FirstOrDefault(o => o.Id == item.Id);
        if (order == null) return;

        var oldStatus = order.Status;
        order.Status = PurchaseOrderStatus.Approved;
        order.UpdatedAt = DateTime.UtcNow;
        companyData?.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Approve order '{item.PoNumber}'",
            () =>
            {
                order.Status = oldStatus;
                companyData?.MarkAsModified();
                LoadOrders();
            },
            () =>
            {
                order.Status = PurchaseOrderStatus.Approved;
                companyData?.MarkAsModified();
                LoadOrders();
            }));

        LoadOrders();
    }

    /// <summary>
    /// Marks an order as received.
    /// </summary>
    [RelayCommand]
    private void ReceiveOrder(PurchaseOrderDisplayItem? item)
    {
        if (item == null) return;
        App.PurchaseOrdersModalsViewModel?.OpenReceiveModal(item);
    }

    /// <summary>
    /// Opens the Send modal for the given order.
    /// </summary>
    [RelayCommand]
    private void SendOrder(PurchaseOrderDisplayItem? item)
    {
        if (item == null) return;
        App.PurchaseOrdersModalsViewModel?.OpenSendModal(item);
    }

    #endregion

    #region Filter Modal

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    [RelayCommand]
    private void OpenFilterModal()
    {
        App.PurchaseOrdersModalsViewModel?.OpenFilterModal();
    }

    #endregion
}

/// <summary>
/// Display model for purchase orders in the UI.
/// </summary>
public partial class PurchaseOrderDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _poNumber = string.Empty;

    [ObservableProperty]
    private DateTime _orderDate;

    [ObservableProperty]
    private string _dateDisplay = string.Empty;

    [ObservableProperty]
    private string _supplierId = string.Empty;

    [ObservableProperty]
    private string _supplierName = string.Empty;

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    private decimal _subtotal;

    [ObservableProperty]
    private decimal _shippingCost;

    [ObservableProperty]
    private decimal _total;

    [ObservableProperty]
    private string _totalDisplay = string.Empty;

    [ObservableProperty]
    private string _originalCurrency = "USD";

    [ObservableProperty]
    private PurchaseOrderStatus _status;

    [ObservableProperty]
    private string _statusDisplay = string.Empty;

    [ObservableProperty]
    private DateTime _expectedDeliveryDate;

    [ObservableProperty]
    private string _expectedDisplay = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private DateTime _updatedAt;

    /// <summary>
    /// True when the total is showing the pending-conversion marker (its display-currency value
    /// isn't available yet). Drives the info glyph + tooltip next to the "Pending" text.
    /// </summary>
    public bool IsTotalPending => TotalDisplay == CurrencyService.PendingMarker;

    /// <summary>Friendly explanation shown in the info tooltip when <see cref="IsTotalPending"/>.</summary>
    public string PendingConversionHint => CurrencyService.BuildPendingConversionHint(Total, OriginalCurrency, OrderDate);

    /// <summary>
    /// Gets the status badge color based on status.
    /// </summary>
    public string StatusColor => Status switch
    {
        PurchaseOrderStatus.Draft => AppColors.GrayText,
        PurchaseOrderStatus.Pending => AppColors.WarningText,
        PurchaseOrderStatus.Approved => AppColors.PrimaryText,
        PurchaseOrderStatus.Sent => AppColors.VioletHover,
        PurchaseOrderStatus.OnOrder => AppColors.VioletHover,
        PurchaseOrderStatus.PartiallyReceived => AppColors.WarningText,
        PurchaseOrderStatus.Received => AppColors.SuccessText,
        PurchaseOrderStatus.Cancelled => AppColors.Error,
        _ => AppColors.GrayText
    };

    /// <summary>
    /// Gets the status badge background color.
    /// </summary>
    public string StatusBackground => Status switch
    {
        PurchaseOrderStatus.Draft => AppColors.GrayLightest,
        PurchaseOrderStatus.Pending => AppColors.WarningLight,
        PurchaseOrderStatus.Approved => AppColors.PrimaryLight,
        PurchaseOrderStatus.Sent => AppColors.VioletLight,
        PurchaseOrderStatus.OnOrder => AppColors.VioletLight,
        PurchaseOrderStatus.PartiallyReceived => AppColors.WarningLight,
        PurchaseOrderStatus.Received => AppColors.SuccessLight,
        PurchaseOrderStatus.Cancelled => AppColors.ErrorLight,
        _ => AppColors.GrayLightest
    };

    /// <summary>
    /// Items count display.
    /// </summary>
    public string ItemsDisplay => ItemCount == 1 ? "1 item" : $"{ItemCount} items";

    /// <summary>
    /// Whether the order can be approved.
    /// </summary>
    public bool CanApprove => Status == PurchaseOrderStatus.Pending || Status == PurchaseOrderStatus.Draft;

    /// <summary>
    /// Whether the order can be sent to the supplier by email.
    /// </summary>
    public bool CanSend => Status == PurchaseOrderStatus.Draft
        || Status == PurchaseOrderStatus.Pending
        || Status == PurchaseOrderStatus.Approved;

    /// <summary>
    /// Whether the order can be received.
    /// </summary>
    public bool CanReceive => Status == PurchaseOrderStatus.OnOrder || Status == PurchaseOrderStatus.Sent
        || Status == PurchaseOrderStatus.Approved || Status == PurchaseOrderStatus.PartiallyReceived;

    /// <summary>
    /// Whether the order can be edited.
    /// </summary>
    public bool CanEdit => Status == PurchaseOrderStatus.Draft || Status == PurchaseOrderStatus.Pending;

    [ObservableProperty]
    private bool _isHighlighted;
}
