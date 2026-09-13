using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Integrations;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using ArgoBooks.Helpers;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Revenue page displaying revenue transactions.
/// </summary>
public partial class RevenuePageViewModel : SortablePageViewModelBase
{
    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    [ObservableProperty]
    private bool _hasPremium;

    #region Statistics

    [ObservableProperty]
    private string _totalMonthlyRevenue = "$0.00";

    [ObservableProperty]
    private int _salesCount;

    [ObservableProperty]
    private int _uniqueCustomers;

    [ObservableProperty]
    private int _returnsCount;

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterRevenue();
        });

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string? _filterCustomerId;

    [ObservableProperty]
    private string? _filterCategoryId;

    [ObservableProperty]
    private string? _filterAmountMin;

    [ObservableProperty]
    private string? _filterAmountMax;

    [ObservableProperty]
    private DateTimeOffset? _filterDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _filterDateTo;

    #endregion

    #region Column Visibility

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public RevenueTableColumnWidths ColumnWidths => App.RevenueColumnWidths;

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    [ObservableProperty]
    private bool _showIdColumn = ColumnVisibilityHelper.Load("Revenue", "Id", true);

    [ObservableProperty]
    private bool _showAccountantColumn = ColumnVisibilityHelper.Load("Revenue", "Accountant", false);

    [ObservableProperty]
    private bool _showCustomerColumn = ColumnVisibilityHelper.Load("Revenue", "Customer", true);

    [ObservableProperty]
    private bool _showProductColumn = ColumnVisibilityHelper.Load("Revenue", "Product", true);

    [ObservableProperty]
    private bool _showDateColumn = ColumnVisibilityHelper.Load("Revenue", "Date", true);

    [ObservableProperty]
    private bool _showQuantityColumn = ColumnVisibilityHelper.Load("Revenue", "Quantity", false);

    [ObservableProperty]
    private bool _showAmountColumn = ColumnVisibilityHelper.Load("Revenue", "Amount", false);

    [ObservableProperty]
    private bool _showTaxColumn = ColumnVisibilityHelper.Load("Revenue", "Tax", false);

    [ObservableProperty]
    private bool _showShippingColumn = ColumnVisibilityHelper.Load("Revenue", "Shipping", false);

    [ObservableProperty]
    private bool _showDiscountColumn = ColumnVisibilityHelper.Load("Revenue", "Discount", false);

    [ObservableProperty]
    private bool _showTotalColumn = ColumnVisibilityHelper.Load("Revenue", "Total", true);

    [ObservableProperty]
    private bool _showStatusColumn = ColumnVisibilityHelper.Load("Revenue", "Status", true);

    [ObservableProperty]
    private bool _showReceiptColumn = ColumnVisibilityHelper.Load("Revenue", "Receipt", true);

    [ObservableProperty]
    private bool _showInvoiceColumn = ColumnVisibilityHelper.Load("Revenue", "Invoice", true);

    partial void OnShowIdColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Id", value); ColumnVisibilityHelper.Save("Revenue", "Id", value); }
    partial void OnShowAccountantColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Accountant", value); ColumnVisibilityHelper.Save("Revenue", "Accountant", value); }
    partial void OnShowCustomerColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Customer", value); ColumnVisibilityHelper.Save("Revenue", "Customer", value); }
    partial void OnShowProductColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Product", value); ColumnVisibilityHelper.Save("Revenue", "Product", value); }
    partial void OnShowDateColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Date", value); ColumnVisibilityHelper.Save("Revenue", "Date", value); }
    partial void OnShowQuantityColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Quantity", value); ColumnVisibilityHelper.Save("Revenue", "Quantity", value); }
    partial void OnShowAmountColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Amount", value); ColumnVisibilityHelper.Save("Revenue", "Amount", value); }
    partial void OnShowTaxColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Tax", value); ColumnVisibilityHelper.Save("Revenue", "Tax", value); }
    partial void OnShowShippingColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Shipping", value); ColumnVisibilityHelper.Save("Revenue", "Shipping", value); }
    partial void OnShowDiscountColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Discount", value); ColumnVisibilityHelper.Save("Revenue", "Discount", value); }
    partial void OnShowTotalColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Total", value); ColumnVisibilityHelper.Save("Revenue", "Total", value); }
    partial void OnShowReceiptColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Receipt", value); ColumnVisibilityHelper.Save("Revenue", "Receipt", value); }
    partial void OnShowStatusColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Status", value); ColumnVisibilityHelper.Save("Revenue", "Status", value); }
    partial void OnShowInvoiceColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Invoice", value); ColumnVisibilityHelper.Save("Revenue", "Invoice", value); }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("Revenue");
        ShowIdColumn = true;
        ShowAccountantColumn = false;
        ShowCustomerColumn = true;
        ShowProductColumn = true;
        ShowDateColumn = true;
        ShowQuantityColumn = false;
        ShowAmountColumn = false;
        ShowTaxColumn = false;
        ShowShippingColumn = false;
        ShowDiscountColumn = false;
        ShowTotalColumn = true;
        ShowReceiptColumn = true;
        ShowStatusColumn = true;
        ShowInvoiceColumn = true;
    }

    #endregion

    #region Revenue Collection

    private readonly List<Revenue> _allRevenue = [];

    public BatchObservableCollection<RevenueDisplayItem> Revenue { get; } = [];

    #endregion

    #region Pagination

    [ObservableProperty]
    private string _paginationText = "0 sales";

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterRevenue();

    #endregion

    #region Constructor

        [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsRecurringTab => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value) => OnPropertyChanged(nameof(IsRecurringTab));

    /// <summary>Schedules for this side only, shown on the Recurring tab.</summary>
    public RecurringSchedulesViewModel RecurringSchedules { get; } = new(CategoryType.Revenue);

    [ObservableProperty]
    private int _generatedBannerCount;

    [ObservableProperty]
    private bool _hasGeneratedBanner;

    /// <summary>StringFormat cannot pluralise, so the whole sentence is built here.</summary>
    public string GeneratedBannerText => GeneratedBannerCount == 1
        ? "1 entry was generated from your recurring schedules and needs review.".Translate()
        : "{0} entries were generated from your recurring schedules and need review.".TranslateFormat(GeneratedBannerCount);

    partial void OnGeneratedBannerCountChanged(int value) => OnPropertyChanged(nameof(GeneratedBannerText));

    /// <summary>
    /// Subscribes for the live case and reads any pending count for the common case where
    /// generation ran on company open, before this page existed.
    /// </summary>
    /// <summary>
    /// The banner is derived from the data rather than tracked alongside it, so an undo of an
    /// accept brings it back without every caller having to remember to.
    /// </summary>
    private void RefreshReviewBanner()
    {
        var data = App.CompanyManager?.CompanyData;
        GeneratedBannerCount = data?.Revenues.Count(e => e.NeedsReview) ?? 0;
        HasGeneratedBanner = GeneratedBannerCount > 0;
    }

    private void WireRecurringBanner()
    {
        RecurringTransactionService.RevenuesGenerated += OnRecurringGenerated;
        RefreshReviewBanner();
    }

    private void OnRecurringGenerated(int count)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RefreshReviewBanner();
            LoadRevenue();
        });
    }

    [RelayCommand]
    private void AcceptGenerated(RevenueDisplayItem? item)
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null || item == null) return;

        var entry = data.Revenues.FirstOrDefault(e => e.Id == item.Id);
        if (entry == null || !entry.NeedsReview) return;

        entry.NeedsReview = false;
        item.NeedsReview = false;

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Accept {entry.Id}",
            () => { entry.NeedsReview = true; item.NeedsReview = true; },
            () => { entry.NeedsReview = false; item.NeedsReview = false; }));

        RefreshReviewBanner();
        App.CompanyManager?.MarkAsChanged();
    }

    [RelayCommand]
    private void MarkReviewed()
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        var accepted = data.Revenues.Where(e => e.NeedsReview).ToList();
        if (accepted.Count == 0) return;

        foreach (var entry in accepted)
            entry.NeedsReview = false;

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Accept {accepted.Count} generated entries",
            () => { foreach (var entry in accepted) entry.NeedsReview = true; },
            () => { foreach (var entry in accepted) entry.NeedsReview = false; }));

        RecurringTransactionService.ClearPendingRevenues();
        RefreshReviewBanner();
        App.CompanyManager?.MarkAsChanged();
        LoadRevenue();
    }

public RevenuePageViewModel()
    {
        WireRecurringBanner();

        // Set default sort values for revenue
        SortColumn = "Date";
        SortDirection = SortDirection.Descending;

        InitializeColumnVisibility();
        LoadRevenue();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

        // Subscribe to revenue modal events to refresh data
        if (App.RevenueModalsViewModel != null)
        {
            App.RevenueModalsViewModel.RevenueSaved += OnRevenueSaved;
            App.RevenueModalsViewModel.RevenueDeleted += OnRevenueDeleted;
            App.RevenueModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.RevenueModalsViewModel.FiltersCleared += OnFiltersCleared;
        }

        // Subscribe to invoice events to refresh when invoices are generated from revenue
        if (App.InvoiceModalsViewModel != null)
        {
            App.InvoiceModalsViewModel.InvoiceSaved += OnInvoiceSaved;
        }

        // Subscribe to date format changes to refresh date display
        DateFormatService.DateFormatChanged += OnDateFormatChanged;

        // Subscribe to currency changes to refresh currency display
        CurrencyService.CurrencyChanged += OnCurrencyChanged;
    }

    private void OnDateFormatChanged(object? sender, EventArgs e) => FilterRevenue();
    private void OnCurrencyChanged(object? sender, EventArgs e)
    {
        UpdateStatistics();
        FilterRevenue();
    }

    public override void Cleanup()
    {
        base.Cleanup();
        RecurringTransactionService.RevenuesGenerated -= OnRecurringGenerated;
        RecurringSchedules.Cleanup();
        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
        if (App.RevenueModalsViewModel != null)
        {
            App.RevenueModalsViewModel.RevenueSaved -= OnRevenueSaved;
            App.RevenueModalsViewModel.RevenueDeleted -= OnRevenueDeleted;
            App.RevenueModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.RevenueModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
        if (App.InvoiceModalsViewModel != null)
            App.InvoiceModalsViewModel.InvoiceSaved -= OnInvoiceSaved;
        DateFormatService.DateFormatChanged -= OnDateFormatChanged;
        CurrencyService.CurrencyChanged -= OnCurrencyChanged;
    }

    private void InitializeColumnVisibility()
    {
        // Set initial visibility for columns
        ColumnWidths.SetColumnVisibility("Id", ShowIdColumn);
        ColumnWidths.SetColumnVisibility("Accountant", ShowAccountantColumn);
        ColumnWidths.SetColumnVisibility("Customer", ShowCustomerColumn);
        ColumnWidths.SetColumnVisibility("Product", ShowProductColumn);
        ColumnWidths.SetColumnVisibility("Date", ShowDateColumn);
        ColumnWidths.SetColumnVisibility("Quantity", ShowQuantityColumn);
        ColumnWidths.SetColumnVisibility("Amount", ShowAmountColumn);
        ColumnWidths.SetColumnVisibility("Tax", ShowTaxColumn);
        ColumnWidths.SetColumnVisibility("Shipping", ShowShippingColumn);
        ColumnWidths.SetColumnVisibility("Discount", ShowDiscountColumn);
        ColumnWidths.SetColumnVisibility("Total", ShowTotalColumn);
        ColumnWidths.SetColumnVisibility("Receipt", ShowReceiptColumn);
        ColumnWidths.SetColumnVisibility("Status", ShowStatusColumn);
        ColumnWidths.SetColumnVisibility("Invoice", ShowInvoiceColumn);
        ColumnWidths.SetColumnVisibility("Actions", true);

        ColumnWidths.RecalculateWidths();
    }

    private bool _needsRefresh;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        RefreshReviewBanner();

        if (App.NavigationService?.CurrentPageName != PageNames.Revenue)
        {
            _needsRefresh = true;
            return;
        }
        LoadRevenue();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName != PageNames.Revenue) return;

        if (_needsRefresh)
        {
            _needsRefresh = false;
            LoadRevenue();
        }

        _ = CheckStripePendingAsync();
        _ = CheckArgoApiPendingAsync();
    }

    private void OnRevenueSaved(object? sender, EventArgs e)
    {
        LoadRevenue();
    }

    private void OnInvoiceSaved(object? sender, EventArgs e)
    {
        LoadRevenue();
    }

    private void OnRevenueDeleted(object? sender, EventArgs e)
    {
        LoadRevenue();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        var modals = App.RevenueModalsViewModel;
        if (modals != null)
        {
            FilterStatus = modals.FilterStatus;
            FilterCustomerId = modals.FilterCustomerId;
            FilterCategoryId = modals.FilterCategoryId;
            FilterAmountMin = modals.FilterAmountMin;
            FilterAmountMax = modals.FilterAmountMax;
            FilterDateFrom = modals.FilterDateFrom;
            FilterDateTo = modals.FilterDateTo;
        }
        CurrentPage = 1;
        FilterRevenue();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterStatus = "All";
        FilterCustomerId = null;
        FilterCategoryId = null;
        FilterAmountMin = null;
        FilterAmountMax = null;
        FilterDateFrom = null;
        FilterDateTo = null;
        SearchQuery = null;
        CurrentPage = 1;
        FilterRevenue();
    }

    #endregion

    #region Data Loading

    private void LoadRevenue()
    {
        _allRevenue.Clear();
        Revenue.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Revenues == null)
            return;

        _allRevenue.AddRange(companyData.Revenues);
        UpdateStatistics();
        FilterRevenue();
    }

    private void UpdateStatistics()
    {
        var now = DateTime.Now;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);
        var companyData = App.CompanyManager?.CompanyData;

        // Total monthly revenue, net of refunds (cash-basis: refund counts on
        // the day it was issued). Mirrors the dashboard's stat-card semantics
        // exactly so the two never drift: paid-only, capped at end of month.
        // Convert each row/refund at its OWN date before summing (Calculations.md §3a Phase 2).
        var grossComplete = CurrencyService.TrySumDisplayFromUSD(
            RevenueAggregator.OnlyCollected(
                _allRevenue.Where(s => s.Date >= startOfMonth && s.Date <= endOfMonth)),
            s => s.Total, s => s.OriginalCurrency, s => s.TotalUSD, s => s.Date, out var monthlyGrossDisplay);
        var refundsComplete = true;
        var monthlyRefundsDisplay = 0m;
        if (companyData?.Payments != null)
            refundsComplete = CurrencyService.TrySumDisplayFromUSD(
                companyData.Payments.Where(p => p.IsRefund && p.Date >= startOfMonth && p.Date <= endOfMonth),
                p => Math.Abs(p.Amount) * p.RevenueShare, p => p.OriginalCurrency, p => Math.Abs(p.AmountUSD) * p.RevenueShare, p => p.Date, out monthlyRefundsDisplay);
        // Pending if any component is still awaiting its rate, so the total isn't shown partial.
        TotalMonthlyRevenue = grossComplete && refundsComplete
            ? CurrencyService.Format(monthlyGrossDisplay - monthlyRefundsDisplay)
            : CurrencyService.PendingMarker;

        SalesCount = _allRevenue.Count;

        UniqueCustomers = _allRevenue
            .Where(s => !string.IsNullOrEmpty(s.CustomerId))
            .Select(s => s.CustomerId)
            .Distinct()
            .Count();

        if (companyData?.Returns.Count > 0)
        {
            var revenueIds = new HashSet<string>(_allRevenue.Select(s => s.Id));
            ReturnsCount = companyData.Returns.Count(r => revenueIds.Contains(r.OriginalTransactionId));
        }
        else
        {
            ReturnsCount = 0;
        }
    }

    [RelayCommand]
    private void RefreshRevenue()
    {
        LoadRevenue();
    }

    private void FilterRevenue()
    {
        var companyData = App.CompanyManager?.CompanyData;

        var lostDamagedIds = new HashSet<string>(
            companyData?.LostDamaged.Select(ld => ld.InventoryItemId ?? "") ?? []);
        var returnedIds = new HashSet<string>(
            companyData?.Returns
                .Where(r => r.Status == ReturnStatus.Completed)
                .Select(r => r.OriginalTransactionId) ?? []);

        IEnumerable<Revenue> filtered = _allRevenue;

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .Select(s => new
                {
                    Revenue = s,
                    IdScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, s.Id),
                    DescScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, s.Description),
                    CustomerScore = LevenshteinDistance.ComputeSearchScore(SearchQuery,
                        companyData?.GetCustomer(s.CustomerId ?? "")?.Name ?? "")
                })
                .Where(x => x.IdScore >= 0 || x.DescScore >= 0 || x.CustomerScore >= 0)
                .OrderByDescending(x => Math.Max(Math.Max(x.IdScore, x.DescScore), x.CustomerScore))
                .Select(x => x.Revenue)
                .ToList();
        }

        // Refund totals and online-payment invoice ids, computed once. Both the status filter and the
        // display build need per-revenue refund totals and a portal-payment check; doing them per row
        // rescans the whole Payments list each time (O(rows x payments)). Precompute keyed by invoice id.
        var paymentsForRefunds = companyData?.Payments ?? new List<Payment>();
        var refundedByInvoiceId = paymentsForRefunds
            .Where(p => p.IsRefund && !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(p => Math.Abs(p.Amount) * p.RevenueShare));
        var onlinePaymentInvoiceIds = paymentsForRefunds
            .Where(p => p.Source == PaymentSource.Online && !string.IsNullOrEmpty(p.InvoiceId))
            .Select(p => p.InvoiceId)
            .ToHashSet();

        // An invoice's refunds belong to the revenue created from it, not to a deposit kept on it later.
        decimal RefundedForRevenue(Revenue r) =>
            !r.IsKeptDeposit && !string.IsNullOrEmpty(r.InvoiceId) && refundedByInvoiceId.TryGetValue(r.InvoiceId, out var amt) ? amt : 0m;

        if (FilterStatus != "All")
        {
            filtered = filtered.Where(s =>
                GetStatusDisplay(s, lostDamagedIds, returnedIds, RefundedForRevenue(s)) == FilterStatus);
        }

        if (!string.IsNullOrEmpty(FilterCustomerId))
        {
            filtered = filtered.Where(s => s.CustomerId == FilterCustomerId);
        }

        // Apply category filter (via line item product category)
        if (!string.IsNullOrEmpty(FilterCategoryId))
        {
            filtered = filtered.Where(s =>
            {
                var productId = s.LineItems.FirstOrDefault()?.ProductId;
                var product = productId != null ? companyData?.GetProduct(productId) : null;
                return product?.CategoryId == FilterCategoryId;
            });
        }

        if (decimal.TryParse(FilterAmountMin, out var minAmount))
        {
            filtered = filtered.Where(s => s.Total >= minAmount);
        }
        if (decimal.TryParse(FilterAmountMax, out var maxAmount))
        {
            filtered = filtered.Where(s => s.Total <= maxAmount);
        }

        if (FilterDateFrom.HasValue)
        {
            filtered = filtered.Where(s => s.Date >= FilterDateFrom.Value.DateTime);
        }
        if (FilterDateTo.HasValue)
        {
            filtered = filtered.Where(s => s.Date <= FilterDateTo.Value.DateTime);
        }

        // Materialize filtered results
        var filteredList = filtered.ToList();

        var displayItems = filteredList.Select(revenue =>
        {
            var customer = companyData?.GetCustomer(revenue.CustomerId ?? "");
            var productId = revenue.LineItems.FirstOrDefault()?.ProductId;
            var product = productId != null ? companyData?.GetProduct(productId) : null;
            var categoryId = product?.CategoryId;
            var category = categoryId != null ? companyData?.GetCategory(categoryId) : null;
            var accountant = companyData?.GetAccountant(revenue.AccountantId ?? "");
            var refundedAmount = RefundedForRevenue(revenue);
            var netTotal = Math.Max(0, revenue.Total - refundedAmount);
            var statusDisplay = revenue.IsPendingConversion ? "Pending" : GetStatusDisplay(revenue, lostDamagedIds, returnedIds, refundedAmount);
            var isFromPortal = !string.IsNullOrEmpty(revenue.InvoiceId) &&
                onlinePaymentInvoiceIds.Contains(revenue.InvoiceId);
            var (productName, productMoreText) = FormatProductDescription(revenue);

            var hasReceipt = !string.IsNullOrEmpty(revenue.ReceiptId);
            var receipt = hasReceipt ? companyData?.GetReceipt(revenue.ReceiptId!) : null;
            var receiptFilePath = receipt?.OriginalFilePath ?? string.Empty;
            var customerAvatar = AvatarBitmapLoader.LoadCustomer(customer);

            return new RevenueDisplayItem
            {
                Id = revenue.Id,
                NeedsReview = revenue.NeedsReview,
                IsRecurring = !string.IsNullOrEmpty(revenue.RecurringScheduleId),
                AccountantName = accountant?.Name ?? "System",
                CustomerName = customer?.Name ?? "-",
                ProductDescription = productName,
                ProductMoreText = productMoreText,
                CategoryName = category?.Name ?? "-",
                Date = revenue.Date,
                Total = netTotal,
                TotalUSD = revenue.EffectiveTotalUSD,
                AmountUSD = revenue.Amount > 0 ? revenue.EffectiveTotalUSD * (revenue.Amount / revenue.Total) : 0,
                TaxAmountUSD = revenue.TaxAmountUSD > 0 ? revenue.TaxAmountUSD : revenue.TaxAmount,
                ShippingCostUSD = revenue.EffectiveShippingCostUSD,
                DiscountUSD = revenue.DiscountUSD > 0 ? revenue.DiscountUSD : revenue.Discount,
                UnitPriceUSD = revenue.EffectiveUnitPriceUSD,
                StatusDisplay = statusDisplay,
                Paid = RevenueAggregator.IsCollected(revenue),
                Notes = revenue.Notes,
                CustomerId = revenue.CustomerId,
                CategoryId = categoryId,
                Amount = revenue.Amount,
                TaxAmount = revenue.TaxAmount,
                TaxRate = revenue.TaxRate,
                ShippingCost = revenue.ShippingCost,
                Discount = revenue.Discount,
                Quantity = (int)revenue.Quantity,
                UnitPrice = revenue.UnitPrice,
                PaymentMethod = revenue.PaymentMethod,
                HasReceipt = hasReceipt,
                ReceiptFilePath = receiptFilePath,
                IsHighlighted = revenue.Id == HighlightTransactionId,
                InvoiceId = revenue.InvoiceId ?? string.Empty,
                IsPendingConversion = revenue.IsPendingConversion,
                OriginalCurrency = revenue.OriginalCurrency,
                CustomerAvatarBitmap = customerAvatar,
                HasCustomerAvatar = customerAvatar != null,
                IsFromPortal = isFromPortal
            };
        }).ToList();

        // Apply sorting (only if not searching, since search has its own relevance sorting)
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<RevenueDisplayItem, object?>>
                {
                    ["Id"] = r => r.Id,
                    ["Accountant"] = r => r.AccountantName,
                    ["Customer"] = r => r.CustomerName,
                    ["Product"] = r => r.ProductDescription,
                    ["Category"] = r => r.CategoryName,
                    ["Date"] = r => r.Date,
                    ["Total"] = r => r.Total,
                    ["Status"] = r => r.StatusDisplay,
                    ["Invoice"] = r => r.InvoiceId
                },
                r => r.Date);
        }

        // Navigate to highlighted item if set (from dashboard click)
        NavigateToHighlightedItem(displayItems, x => x.Id);

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Apply pagination and add to collection
        var pagedRevenue = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Revenue.ReplaceAll(pagedRevenue);
    }

    private static (string name, string moreText) FormatProductDescription(Revenue revenue)
    {
        if (revenue.LineItems.Count <= 1)
            return (revenue.Description, string.Empty);

        var firstName = revenue.LineItems[0].Description;
        if (string.IsNullOrEmpty(firstName))
            firstName = revenue.Description.Split(',')[0].Trim();

        var remaining = revenue.LineItems.Count - 1;
        return (firstName, $" +{remaining} more");
    }

    private static string GetStatusDisplay(Revenue revenue, HashSet<string> lostDamagedIds, HashSet<string> returnedIds, decimal refundedAmount)
    {
        if (lostDamagedIds.Contains(revenue.Id)) return "Lost / Damaged";
        if (returnedIds.Contains(revenue.Id)) return "Returned";
        if (refundedAmount > 0)
        {
            // Treat the revenue as fully refunded once the refund covers Total
            // (within a cent, float drift safety). Otherwise it's partial.
            return refundedAmount + 0.01m >= revenue.Total ? "Refunded" : "Partially Refunded";
        }
        if (!RevenueAggregator.IsCollected(revenue)) return "Unpaid";
        return "Completed";
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
            totalCount, CurrentPage, PageSize, TotalPages, "revenue");
    }

    #endregion

    #region Modal Commands

    [RelayCommand]
    private void OpenAddModal()
    {
        App.RevenueModalsViewModel?.OpenAddModal();
    }

    [RelayCommand]
    private void OpenEditModal(RevenueDisplayItem? item)
    {
        App.RevenueModalsViewModel?.OpenEditModal(item);
    }

    [RelayCommand]
    private void OpenDeleteConfirm(RevenueDisplayItem? item)
    {
        App.RevenueModalsViewModel?.OpenDeleteConfirm(item);
    }

    [RelayCommand]
    private void MarkAsLostDamaged(RevenueDisplayItem? item)
    {
        App.RevenueModalsViewModel?.OpenMarkAsLostDamagedModal(item);
    }

    [RelayCommand]
    private void MarkAsReturned(RevenueDisplayItem? item)
    {
        App.RevenueModalsViewModel?.OpenMarkAsReturnedModal(item);
    }

    [RelayCommand]
    private void UndoLostDamaged(RevenueDisplayItem? item)
    {
        App.RevenueModalsViewModel?.OpenUndoLostDamagedModal(item);
    }

    [RelayCommand]
    private void UndoReturn(RevenueDisplayItem? item)
    {
        App.RevenueModalsViewModel?.OpenUndoReturnedModal(item);
    }

    [RelayCommand]
    private void ViewInvoice(string? invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId)) return;
        App.InvoiceModalsViewModel?.OpenViewInvoice(invoiceId);
    }

    [RelayCommand]
    private void GenerateInvoice(RevenueDisplayItem? item)
    {
        if (item == null) return;
        App.InvoiceModalsViewModel?.OpenCreateFromRevenue(item.Id);
    }

    [RelayCommand]
    private void OpenFilterModal()
    {
        App.RevenueModalsViewModel?.OpenFilterModal();
    }

    #endregion

    #region Receipt Preview

    [RelayCommand]
    private void ViewReceipt(RevenueDisplayItem? item)
    {
        if (item == null || !item.HasReceipt)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        var revenue = companyData?.Revenues.FirstOrDefault(s => s.Id == item.Id);
        if (revenue == null || string.IsNullOrEmpty(revenue.ReceiptId))
            return;

        // The viewer renders all pages (PDFs) from the receipt's stored data.
        App.ReceiptViewerModal?.Show(revenue.ReceiptId, $"Receipt for {item.Id}");
    }

    #endregion

    #region Stripe sync banner

    [ObservableProperty]
    private bool _stripeBannerVisible;

    [ObservableProperty]
    private int _stripePendingCount;

    public string StripeBannerText => "Stripe: new sales are ready to sync.".Translate();

    partial void OnStripePendingCountChanged(int value) => OnPropertyChanged(nameof(StripeBannerText));

    private int _lastNotifiedStripePending;

    /// <summary>
    /// Checks whether new Stripe activity is waiting to be imported and shows the Revenue-page
    /// banner if so. Called each time the user navigates to this page.
    /// Network errors are swallowed: a Stripe outage must never break loading the Revenue page.
    /// </summary>
    private async Task CheckStripePendingAsync()
    {
        var data = App.CompanyManager?.CompanyData;
        var stripe = data?.Settings.Integrations.Stripe;
        if (data == null || stripe == null || !stripe.Connected || App.SharedHttpClient == null)
            return;

        try
        {
            var svc = new StripeSyncService(new StripeApiClient(App.SharedHttpClient));
            var preview = await svc.PreviewAsync(data);
            var count = preview.Charges.Count;

            StripePendingCount = count;
            StripeBannerVisible = count > 0;

            if (count > 0 && count != _lastNotifiedStripePending)
            {
                _lastNotifiedStripePending = count;
                App.AddNotification(
                    "Stripe".Translate(),
                    "New Stripe sales are ready to sync.".Translate(),
                    NotificationType.Info,
                    () => App.NavigationService?.NavigateTo(PageNames.Revenue));
            }
            else if (count == 0)
            {
                _lastNotifiedStripePending = 0;
            }
        }
        catch
        {
            // Stripe unreachable or key invalid: leave the banner hidden rather than
            // surface an error on a page that isn't about Stripe connectivity.
        }
    }

    /// <summary>
    /// True while a sync is running, so the banner can say so.
    ///
    /// A disabled button was the only sign anything was happening, and on a busy
    /// account the import spends fifteen seconds fetching exchange rates before it
    /// writes a single row. That reads as the app having hung.
    /// </summary>
    [ObservableProperty]
    private bool _isSyncingStripe;

    [ObservableProperty]
    private string _stripeSyncStatus = string.Empty;

    [RelayCommand]
    private async Task SyncFromBannerAsync()
    {
        var data = App.CompanyManager?.CompanyData;
        var stripe = data?.Settings.Integrations.Stripe;
        if (data == null || stripe == null || !stripe.Connected || App.SharedHttpClient == null) return;

        IsSyncingStripe = true;
        StripeSyncStatus = "Checking Stripe for new activity...".Translate();
        try
        {
            var svc = new StripeSyncService(new StripeApiClient(App.SharedHttpClient));
            var preview = await svc.PreviewAsync(data);
            if (!preview.HasActivity)
            {
                App.AddNotification("Stripe".Translate(), "You're already up to date.".Translate());
                await CheckStripePendingAsync();
                return;
            }

            if (App.ConfirmationDialog == null) return; // never import without a review step
            var confirmed = await App.ConfirmationDialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Import from Stripe".Translate(),
                Message = "Import your Stripe activity: {0} in sales and {1} in fees?"
                    .TranslateFormat(preview.TotalRevenue.ToString("C2"), preview.TotalFees.ToString("C2")),
                PrimaryButtonText = "Import".Translate(),
                CancelButtonText = "Cancel".Translate()
            }) == ConfirmationResult.Primary;
            if (!confirmed) return;

            StripeSyncStatus = "Importing...".Translate();
            var creation = await svc.ImportPreviewAsync(data, preview, RateProgress(v => StripeSyncStatus = v));
            if (creation.AnyCreated)
                App.UndoRedoManager.RecordAction(new DelegateAction(
                    "Import from Stripe".Translate(),
                    () => { creation.Undo(data); App.CompanyManager?.MarkAsChanged(); LoadRevenue(); },
                    () => { creation.Redo(data); App.CompanyManager?.MarkAsChanged(); LoadRevenue(); }));
            App.CompanyManager?.MarkAsChanged();
            LoadRevenue();
            App.AddNotification("Stripe".Translate(),
                "Imported {0} sales and {1} expense entries from Stripe.".TranslateFormat(creation.RevenuesCreated, creation.ExpensesCreated),
                NotificationType.Success);

            await CheckStripePendingAsync();
        }
        catch
        {
            // Leave the banner as-is; the user can retry the sync manually.
        }
        finally
        {
            IsSyncingStripe = false;
            StripeSyncStatus = string.Empty;
        }
    }

    /// <summary>
    /// Turns the rate fetch's 0-100 into the line the banner shows. Only the fetch
    /// reports a percentage, so the wording names that phase rather than the import
    /// as a whole, which would sit at 100% for the part that actually writes rows.
    /// </summary>
    private static IProgress<int> RateProgress(Action<string> set)
        => new Progress<int>(pct => set("Fetching exchange rates... {0}%".TranslateFormat(pct)));

    [RelayCommand]
    private void DismissStripeBanner() => StripeBannerVisible = false;

    #endregion

    #region Argo Books API sync banner

    [ObservableProperty]
    private bool _argoApiBannerVisible;

    [ObservableProperty]
    private int _argoApiPendingCount;

    public string ArgoApiBannerText =>
        "Argo Books API: {0} items are waiting to be imported.".TranslateFormat(ArgoApiPendingCount);

    partial void OnArgoApiPendingCountChanged(int value) => OnPropertyChanged(nameof(ArgoApiBannerText));

    private int _lastNotifiedArgoApiPending;

    /// <summary>
    /// Checks whether a connected app has sent anything that is still waiting,
    /// and shows the Revenue-page banner if so. Called on every navigation here,
    /// which matters more for this than it does for Stripe: a developer pushes on
    /// their own schedule, so without a banner nothing would tell the merchant
    /// anything had arrived at all.
    ///
    /// GET /v1/account returns per-type counts in a single call, so this is a much
    /// lighter check than the Stripe one, which fetches every charge to count them.
    ///
    /// Errors are swallowed. A server the merchant does not control being briefly
    /// unreachable must never stop the Revenue page loading.
    /// </summary>
    private async Task CheckArgoApiPendingAsync()
    {
        var data = App.CompanyManager?.CompanyData;
        var api = data?.Settings.Integrations.ArgoApi;
        if (data == null || api == null || !api.Enabled
            || string.IsNullOrWhiteSpace(api.DesktopKey) || App.SharedHttpClient == null)
        {
            return;
        }

        try
        {
            var client = new ArgoApiClient(App.SharedHttpClient);
            var account = await client.GetAccountAsync(api.DesktopKey!);
            // Pending is absent rather than empty if the server ever omits it.
            var count = account?.Pending?.Values.Sum() ?? 0;

            ArgoApiPendingCount = count;
            ArgoApiBannerVisible = count > 0;

            if (count > 0 && count != _lastNotifiedArgoApiPending)
            {
                _lastNotifiedArgoApiPending = count;
                App.AddNotification(
                    "Argo Books API".Translate(),
                    "New data is waiting to be imported.".Translate(),
                    NotificationType.Info,
                    () => App.NavigationService?.NavigateTo(PageNames.Revenue));
            }
            else if (count == 0)
            {
                _lastNotifiedArgoApiPending = 0;
            }
        }
        catch
        {
            // Unreachable or the key was revoked: leave the banner hidden rather
            // than raise connectivity errors on a page that is not about that.
        }
    }

    [ObservableProperty]
    private bool _isSyncingArgoApi;

    [ObservableProperty]
    private string _argoApiSyncStatus = string.Empty;

    [RelayCommand]
    private async Task SyncArgoApiFromBannerAsync()
    {
        var data = App.CompanyManager?.CompanyData;
        var api = data?.Settings.Integrations.ArgoApi;
        if (data == null || api == null || !api.Enabled || App.SharedHttpClient == null) return;

        IsSyncingArgoApi = true;
        ArgoApiSyncStatus = "Checking for new data...".Translate();
        try
        {
            var svc = new ArgoApiSyncService(new ArgoApiClient(App.SharedHttpClient));
            var preview = await svc.PreviewAsync(data);
            if (!preview.HasActivity)
            {
                App.AddNotification("Argo Books API".Translate(), "You're already up to date.".Translate());
                await CheckArgoApiPendingAsync();
                return;
            }

            if (App.ConfirmationDialog == null) return; // never import without a review step
            var confirmed = await App.ConfirmationDialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Import from the Argo Books API".Translate(),
                Message = "Import {0} items sent by your connected apps: {1} in revenue and {2} in expenses?"
                    .TranslateFormat(
                        preview.TotalObjects,
                        preview.TotalRevenue.ToString("C2"),
                        preview.TotalExpenses.ToString("C2")),
                PrimaryButtonText = "Import".Translate(),
                CancelButtonText = "Cancel".Translate()
            }) == ConfirmationResult.Primary;
            if (!confirmed) return;

            ArgoApiSyncStatus = "Importing...".Translate();
            var creation = await svc.ImportPreviewAsync(data, preview, RateProgress(v => ArgoApiSyncStatus = v));
            if (creation.AnyCreated)
            {
                App.UndoRedoManager.RecordAction(new DelegateAction(
                    "Import from the Argo Books API".Translate(),
                    () =>
                    {
                        creation.Undo(data);
                        App.CompanyManager?.MarkAsChanged();
                        LoadRevenue();
                        // Hand the objects back on the server too, or the queue keeps
                        // reporting as imported what is no longer in the books. The
                        // banner is refreshed afterwards rather than before, so it
                        // reflects the queue as it is once the release has landed.
                        _ = ReleaseThenRefreshAsync(svc, data, creation.BatchId);
                    },
                    () =>
                    {
                        creation.Redo(data);
                        App.CompanyManager?.MarkAsChanged();
                        LoadRevenue();
                        // Undo gave the objects back. Without claiming them again the
                        // books hold rows the server still calls pending, and the next
                        // sync imports every one of them a second time.
                        _ = ReclaimThenRefreshAsync(svc, data, creation);
                    }));
            }

            App.CompanyManager?.MarkAsChanged();
            LoadRevenue();
            App.AddNotification("Argo Books API".Translate(),
                "Imported {0} sales and {1} expense entries.".TranslateFormat(creation.RevenuesCreated, creation.ExpensesCreated),
                NotificationType.Success);

            await CheckArgoApiPendingAsync();
        }
        catch
        {
            // Leave the banner as-is; the user can retry from here or from Settings.
        }
        finally
        {
            IsSyncingArgoApi = false;
            ArgoApiSyncStatus = string.Empty;
        }
    }

    /// <summary>
    /// Release the batch after an undo, then re-read the queue so the banner comes
    /// back on its own. Without the refresh the merchant has to leave the page and
    /// return before anything tells them the data is waiting again.
    /// </summary>
    private async Task ReleaseThenRefreshAsync(ArgoApiSyncService svc, CompanyData data, string? batchId)
    {
        if (batchId != null)
            await svc.TryReleaseBatchAsync(data, batchId);

        // The count is about to return to what it was before the import, so without
        // this the "new data is waiting" notice would be suppressed as a repeat.
        _lastNotifiedArgoApiPending = 0;
        await CheckArgoApiPendingAsync();
    }

    /// <summary>
    /// Claim the objects again after a redo, and say so if that fails.
    ///
    /// Failure is not cosmetic: the rows are back in the books while the server still
    /// lists the objects as waiting, so the next sync would offer them again and
    /// importing would duplicate them. It cannot be fixed from here, since the reason
    /// it failed is usually that something else already took them, so the honest move
    /// is to say what happened rather than fail quietly and let the duplicate arrive
    /// later with no explanation.
    /// </summary>
    private async Task ReclaimThenRefreshAsync(ArgoApiSyncService svc, CompanyData data, ArgoApiImportCreation creation)
    {
        var reclaimed = await svc.TryReclaimBatchAsync(data, creation);

        // Redo re-recorded the old batch id. If the claim did not land, that id names a
        // batch the server has reverted, so leaving it recorded is worse than nothing.
        if (!reclaimed && creation.BatchId != null)
        {
            data.Settings.Integrations.ArgoApi.ImportedBatches.Remove(creation.BatchId);
            creation.BatchId = null;
            App.CompanyManager?.MarkAsChanged();

            await App.ShowWarningMessageBoxAsync(
                "Argo Books API".Translate(),
                ("The restored items are back in your books, but the server could not be told they were taken. " +
                 "They may still show as waiting on your next sync. Importing them again would create duplicates, " +
                 "so check before you do.").Translate());
        }

        await CheckArgoApiPendingAsync();
    }

    [RelayCommand]
    private void DismissArgoApiBanner() => ArgoApiBannerVisible = false;

    #endregion
}

/// <summary>
/// Display model for revenue/sales in the UI.
/// </summary>
public partial class RevenueDisplayItem : ObservableObject
{
    /// <summary>Set on entries a recurring schedule produced, until the user accepts them.</summary>
    [ObservableProperty]
    private bool _needsReview;

    /// <summary>Stays true after the entry is accepted, so its origin is still visible.</summary>
    [ObservableProperty]
    private bool _isRecurring;

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _accountantName = string.Empty;

    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _productDescription = string.Empty;

    [ObservableProperty]
    private string _productMoreText = string.Empty;

    [ObservableProperty]
    private string _categoryName = string.Empty;

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTotalAsPaid))]
    private decimal _total;

    [ObservableProperty]
    private decimal _totalUSD;

    [ObservableProperty]
    private decimal _amountUSD;

    [ObservableProperty]
    private decimal _taxAmountUSD;

    [ObservableProperty]
    private decimal _shippingCostUSD;

    [ObservableProperty]
    private decimal _discountUSD;

    [ObservableProperty]
    private decimal _unitPriceUSD;

    [ObservableProperty]
    private string _statusDisplay = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTotalAsPaid))]
    private bool _paid;

    /// <summary>
    /// Whether the Total column should render in the success/paid color.
    /// False when a fully-refunded revenue has netted to zero, at that
    /// point the row is "paid" historically but there's nothing left to
    /// celebrate, and the green styling reads as a balance still owed.
    /// </summary>
    public bool ShowTotalAsPaid => Paid && Total > 0;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string? _customerId;

    [ObservableProperty]
    private string? _categoryId;

    [ObservableProperty]
    private decimal _amount;

    [ObservableProperty]
    private decimal _taxAmount;

    [ObservableProperty]
    private decimal _taxRate;

    [ObservableProperty]
    private decimal _shippingCost;

    [ObservableProperty]
    private decimal _discount;

    [ObservableProperty]
    private int _quantity;

    [ObservableProperty]
    private decimal _unitPrice;

    [ObservableProperty]
    private PaymentMethod _paymentMethod;

    [ObservableProperty]
    private bool _isPendingConversion;

    [ObservableProperty]
    private string _originalCurrency = "USD";

    [ObservableProperty]
    private Bitmap? _customerAvatarBitmap;

    [ObservableProperty]
    private bool _hasCustomerAvatar;

    public string DateFormatted => DateFormatService.Format(Date);
    public string TotalFormatted => IsPendingConversion
        ? CurrencyService.Format(Total)
        : CurrencyService.FormatWithOriginal(Total, OriginalCurrency, TotalUSD, Date);
    public string AmountFormatted => IsPendingConversion
        ? CurrencyService.Format(Amount)
        : CurrencyService.FormatWithOriginal(Amount, OriginalCurrency, AmountUSD, Date);
    public string TaxAmountFormatted => IsPendingConversion
        ? CurrencyService.Format(TaxAmount)
        : CurrencyService.FormatWithOriginal(TaxAmount, OriginalCurrency, TaxAmountUSD, Date);
    public string TaxRateFormatted => $"{TaxRate:N1}%";
    public string ShippingCostFormatted => IsPendingConversion
        ? CurrencyService.Format(ShippingCost)
        : CurrencyService.FormatWithOriginal(ShippingCost, OriginalCurrency, ShippingCostUSD, Date);
    public string DiscountFormatted => IsPendingConversion
        ? $"-{CurrencyService.Format(Discount)}"
        : $"-{CurrencyService.FormatWithOriginal(Discount, OriginalCurrency, DiscountUSD, Date)}";
    public string UnitPriceFormatted => IsPendingConversion
        ? CurrencyService.Format(UnitPrice)
        : CurrencyService.FormatWithOriginal(UnitPrice, OriginalCurrency, UnitPriceUSD, Date);

    /// <summary>Friendly explanation for the info tooltip next to the "Pending" status badge.</summary>
    public string PendingConversionHint => CurrencyService.BuildPendingConversionHint(Total, OriginalCurrency, Date);

    public string CustomerInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CustomerName) || CustomerName == "-")
                return "?";

            var parts = CustomerName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();

            return parts.Length > 0 && parts[0].Length > 0
                ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
                : "?";
        }
    }

    public bool IsReturned => StatusDisplay == "Returned";
    public bool IsPartialReturn => StatusDisplay == "Partial Return";
    public bool IsLostDamaged => StatusDisplay == "Lost / Damaged";
    public bool IsRefunded => StatusDisplay == "Refunded" || StatusDisplay == "Partially Refunded";

    // Hide Lost/Returned controls for refunded rows, refund is its own
    // terminal state and shouldn't be intermixed with returns/loss tracking.
    public bool CanMarkAsReturned => !IsReturned && !IsLostDamaged && !IsRefunded;
    public bool CanMarkAsLostDamaged => !IsReturned && !IsLostDamaged && !IsRefunded;

    // Revenue rows generated by a portal payment must not be hand-edited or
    // they'll drift from the server-of-record. Edit button is hidden when
    // this is true.
    [ObservableProperty]
    private bool _isFromPortal;

    [ObservableProperty]
    private bool _hasReceipt;

    [ObservableProperty]
    private string _receiptFilePath = string.Empty;

    [ObservableProperty]
    private bool _isHighlighted;

    [ObservableProperty]
    private string _invoiceId = string.Empty;

    public bool HasInvoiceId => !string.IsNullOrEmpty(InvoiceId);
    public bool CanGenerateInvoice => !Paid && !HasInvoiceId;

    // Receipt-column glyphs: invoice-backed revenues will never have a paper
    // receipt (the invoice IS the receipt). Show a neutral dash instead of
    // the "missing" X to avoid implying something is wrong.
    public bool ShowReceiptDash => !HasReceipt && HasInvoiceId;
    public bool ShowReceiptX => !HasReceipt && !HasInvoiceId;
}
