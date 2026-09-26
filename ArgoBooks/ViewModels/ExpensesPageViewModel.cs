using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels.Dashboard;
using ArgoBooks.Helpers;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Expenses page displaying expense transactions.
/// </summary>
public partial class ExpensesPageViewModel : SortablePageViewModelBase
{
    [ObservableProperty]
    private bool _hasPremium;

    #region Statistics

    [ObservableProperty]
    private string _totalMonthlyExpenses = "$0.00";

    [ObservableProperty]
    private int _transactionCount;

    [ObservableProperty]
    private int _receiptsOnFile;

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
            FilterExpenses();
        });

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string? _filterSupplierId;

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

    [ObservableProperty]
    private string _filterReceiptStatus = "All";

    #endregion

    #region Column Visibility and Widths

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public TableColumnWidths ColumnWidths => App.ExpensesColumnWidths;

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Expenses", new Dictionary<string, bool>
    {
        ["Id"] = true,
        ["Accountant"] = false,
        ["Product"] = true,
        ["Supplier"] = true,
        ["Date"] = true,
        ["Quantity"] = false,
        ["Amount"] = false,
        ["Tax"] = false,
        ["Shipping"] = false,
        ["Discount"] = false,
        ["Fee"] = false,
        ["Total"] = true,
        ["Receipt"] = true,
        ["Status"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showIdColumn = ColumnDefaults.Load("Id");

    [ObservableProperty]
    private bool _showAccountantColumn = ColumnDefaults.Load("Accountant");

    [ObservableProperty]
    private bool _showProductColumn = ColumnDefaults.Load("Product");

    [ObservableProperty]
    private bool _showSupplierColumn = ColumnDefaults.Load("Supplier");

    [ObservableProperty]
    private bool _showDateColumn = ColumnDefaults.Load("Date");

    [ObservableProperty]
    private bool _showQuantityColumn = ColumnDefaults.Load("Quantity");

    [ObservableProperty]
    private bool _showAmountColumn = ColumnDefaults.Load("Amount");

    [ObservableProperty]
    private bool _showTaxColumn = ColumnDefaults.Load("Tax");

    [ObservableProperty]
    private bool _showShippingColumn = ColumnDefaults.Load("Shipping");

    [ObservableProperty]
    private bool _showDiscountColumn = ColumnDefaults.Load("Discount");

    [ObservableProperty]
    private bool _showFeeColumn = ColumnDefaults.Load("Fee");

    [ObservableProperty]
    private bool _showTotalColumn = ColumnDefaults.Load("Total");

    [ObservableProperty]
    private bool _showReceiptColumn = ColumnDefaults.Load("Receipt");

    [ObservableProperty]
    private bool _showStatusColumn = ColumnDefaults.Load("Status");

    #endregion

    #region Expenses Collection

    private readonly List<Expense> _allExpenses = [];

    public BatchObservableCollection<ExpenseDisplayItem> Expenses { get; } = [];

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterExpenses();

    #endregion

    #region Constructor

        [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsRecurringTab => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value) => OnPropertyChanged(nameof(IsRecurringTab));

    /// <summary>Schedules for this side only, shown on the Recurring tab.</summary>
    public RecurringSchedulesViewModel RecurringSchedules { get; } = new(CategoryType.Expense);

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
        GeneratedBannerCount = data?.Expenses.Count(e => e.NeedsReview) ?? 0;
        HasGeneratedBanner = GeneratedBannerCount > 0;
    }

    private void WireRecurringBanner()
    {
        RecurringTransactionService.ExpensesGenerated += OnRecurringGenerated;
        RefreshReviewBanner();
    }

    private void OnRecurringGenerated(int count)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RefreshReviewBanner();
            LoadExpenses();
        });
    }

    [RelayCommand]
    private void AcceptGenerated(ExpenseDisplayItem? item)
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null || item == null) return;

        var entry = data.Expenses.FirstOrDefault(e => e.Id == item.Id);
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

        var accepted = data.Expenses.Where(e => e.NeedsReview).ToList();
        if (accepted.Count == 0) return;

        foreach (var entry in accepted)
            entry.NeedsReview = false;

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Accept {accepted.Count} generated entries",
            () => { foreach (var entry in accepted) entry.NeedsReview = true; },
            () => { foreach (var entry in accepted) entry.NeedsReview = false; }));

        RecurringTransactionService.ClearPendingExpenses();
        RefreshReviewBanner();
        App.CompanyManager?.MarkAsChanged();
        LoadExpenses();
    }

public ExpensesPageViewModel()
    {
        WireRecurringBanner();

        // Set default sort values for expenses
        SortColumn = "Date";
        SortDirection = SortDirection.Descending;

        LoadExpenses();

        EnableDeferredUndoRefresh(p => p == PageNames.Expenses, LoadExpenses, beforeCheck: RefreshReviewBanner);

        // Subscribe to expense modal events to refresh data
        if (App.ExpenseModalsViewModel != null)
        {
            App.ExpenseModalsViewModel.ExpenseSaved += OnExpenseSaved;
            App.ExpenseModalsViewModel.ExpenseDeleted += OnExpenseDeleted;
            App.ExpenseModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.ExpenseModalsViewModel.FiltersCleared += OnFiltersCleared;
        }

        // Subscribe to date format changes to refresh date display
        DateFormatService.DateFormatChanged += OnDateFormatChanged;

        // Subscribe to currency changes to refresh currency display
        CurrencyService.CurrencyChanged += OnCurrencyChanged;
    }

    private void OnDateFormatChanged(object? sender, EventArgs e) => FilterExpenses();
    private void OnCurrencyChanged(object? sender, EventArgs e)
    {
        UpdateStatistics();
        FilterExpenses();
    }

    public override void Cleanup()
    {
        base.Cleanup();
        RecurringTransactionService.ExpensesGenerated -= OnRecurringGenerated;
        RecurringSchedules.Cleanup();
        if (App.ExpenseModalsViewModel != null)
        {
            App.ExpenseModalsViewModel.ExpenseSaved -= OnExpenseSaved;
            App.ExpenseModalsViewModel.ExpenseDeleted -= OnExpenseDeleted;
            App.ExpenseModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.ExpenseModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
        DateFormatService.DateFormatChanged -= OnDateFormatChanged;
        CurrencyService.CurrencyChanged -= OnCurrencyChanged;
    }

    private void OnExpenseSaved(object? sender, EventArgs e)
    {
        LoadExpenses();
    }

    private void OnExpenseDeleted(object? sender, EventArgs e)
    {
        LoadExpenses();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        var modals = App.ExpenseModalsViewModel;
        if (modals != null)
        {
            FilterStatus = modals.FilterStatus;
            FilterSupplierId = modals.FilterSupplierId;
            FilterCategoryId = modals.FilterCategoryId;
            FilterAmountMin = modals.FilterAmountMin;
            FilterAmountMax = modals.FilterAmountMax;
            FilterDateFrom = modals.FilterDateFrom;
            FilterDateTo = modals.FilterDateTo;
            FilterReceiptStatus = modals.FilterReceiptStatus;
            ActiveFilterCount = modals.ActiveFilterCount;
        }
        CurrentPage = 1;
        FilterExpenses();
    }

    protected override void ClearTableFilters() => App.ExpenseModalsViewModel?.ClearFiltersCommand.Execute(null);

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        ActiveFilterCount = 0;
        FilterStatus = "All";
        FilterSupplierId = null;
        FilterCategoryId = null;
        FilterAmountMin = null;
        FilterAmountMax = null;
        FilterDateFrom = null;
        FilterDateTo = null;
        FilterReceiptStatus = "All";
        SearchQuery = null;
        CurrentPage = 1;
        FilterExpenses();
    }

    #endregion

    #region Data Loading

    private void LoadExpenses()
    {
        _allExpenses.Clear();
        Expenses.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Expenses == null)
            return;

        _allExpenses.AddRange(companyData.Expenses);
        UpdateStatistics();
        FilterExpenses();
    }

    private void UpdateStatistics()
    {
        // The dashboard's Total Expenses card for This Month, so the two always match.
        var companyData = App.CompanyManager?.CompanyData;
        var (monthStart, monthEnd) = DashboardCalculations.ThisMonth();
        TotalMonthlyExpenses = companyData != null
            ? DashboardCalculations.FormatExpenses(companyData, monthStart, monthEnd)
            : CurrencyService.Format(0m);

        TransactionCount = _allExpenses.Count;

        ReceiptsOnFile = _allExpenses.Count(p => !string.IsNullOrEmpty(p.ReceiptId));

        // Returns count (linked to returns data)
        if (companyData?.Returns.Count > 0)
        {
            var expenseIds = new HashSet<string>(_allExpenses.Select(p => p.Id));
            ReturnsCount = companyData.Returns.Count(r => expenseIds.Contains(r.OriginalTransactionId));
        }
        else
        {
            ReturnsCount = 0;
        }
    }

    [RelayCommand]
    private void RefreshExpenses()
    {
        LoadExpenses();
    }

    private void FilterExpenses()
    {
        var companyData = App.CompanyManager?.CompanyData;

        var lostDamagedIds = new HashSet<string>(
            companyData?.LostDamaged.Select(ld => ld.InventoryItemId ?? "") ?? []);
        var returnedIds = new HashSet<string>(
            companyData?.Returns
                .Where(r => r.Status == ReturnStatus.Completed)
                .Select(r => r.OriginalTransactionId) ?? []);
        IEnumerable<Expense> filtered = _allExpenses;

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, p => [p.Id, p.Description, companyData?.GetSupplier(p.SupplierId ?? "")?.Name])
                .ToList();
        }

        if (FilterStatus != "All")
        {
            filtered = filtered.Where(p => GetStatusDisplay(p, lostDamagedIds, returnedIds) == FilterStatus);
        }

        if (!string.IsNullOrEmpty(FilterSupplierId))
        {
            filtered = filtered.Where(p => p.SupplierId == FilterSupplierId);
        }

        // Apply category filter (via line item product category)
        if (!string.IsNullOrEmpty(FilterCategoryId))
        {
            filtered = filtered.Where(p =>
            {
                var productId = p.LineItems.FirstOrDefault()?.ProductId;
                var product = productId != null ? companyData?.GetProduct(productId) : null;
                return product?.CategoryId == FilterCategoryId;
            });
        }

        if (decimal.TryParse(FilterAmountMin, out var minAmount))
        {
            filtered = filtered.Where(p => p.Total >= minAmount);
        }
        if (decimal.TryParse(FilterAmountMax, out var maxAmount))
        {
            filtered = filtered.Where(p => p.Total <= maxAmount);
        }

        if (FilterDateFrom.HasValue)
        {
            filtered = filtered.Where(p => p.Date >= FilterDateFrom.Value.DateTime);
        }
        if (FilterDateTo.HasValue)
        {
            filtered = filtered.Where(p => p.Date <= FilterDateTo.Value.DateTime);
        }

        if (FilterReceiptStatus != "All")
        {
            filtered = FilterReceiptStatus switch
            {
                "With Receipt" => filtered.Where(p => !string.IsNullOrEmpty(p.ReceiptId)),
                "No Receipt" => filtered.Where(p => string.IsNullOrEmpty(p.ReceiptId)),
                _ => filtered
            };
        }

        // Materialize filtered results
        var filteredList = filtered.ToList();

        var displayItems = filteredList.Select(purchase =>
        {
            var supplier = companyData?.GetSupplier(purchase.SupplierId ?? "");
            var productId = purchase.LineItems.FirstOrDefault()?.ProductId;
            var product = productId != null ? companyData?.GetProduct(productId) : null;
            var categoryId = product?.CategoryId;
            var category = categoryId != null ? companyData?.GetCategory(categoryId) : null;
            var accountant = companyData?.GetAccountant(purchase.AccountantId ?? "");
            var statusDisplay = purchase.IsPendingConversion ? "Pending" : GetStatusDisplay(purchase, lostDamagedIds, returnedIds);
            var (productName, productMoreText) = FormatProductDescription(purchase);
            var hasReceipt = !string.IsNullOrEmpty(purchase.ReceiptId);
            var receipt = hasReceipt ? companyData?.GetReceipt(purchase.ReceiptId!) : null;
            var receiptFilePath = receipt?.OriginalFilePath ?? string.Empty;

            return new ExpenseDisplayItem
            {
                Id = purchase.Id,
                NeedsReview = purchase.NeedsReview,
                IsRecurring = !string.IsNullOrEmpty(purchase.RecurringScheduleId),
                AccountantName = accountant?.Name ?? "System",
                ProductDescription = productName,
                ProductMoreText = productMoreText,
                CategoryName = category?.Name ?? "-",
                SupplierName = supplier?.Name ?? "-",
                Date = purchase.Date,
                Total = purchase.Total,
                TotalUSD = purchase.EffectiveTotalUSD,
                AmountUSD = purchase.Amount > 0 && purchase.Total > 0 ? purchase.EffectiveTotalUSD * (purchase.Amount / purchase.Total) : 0,
                TaxAmountUSD = purchase.TaxAmountUSD > 0 ? purchase.TaxAmountUSD : purchase.TaxAmount,
                ShippingCostUSD = purchase.EffectiveShippingCostUSD,
                DiscountUSD = purchase.DiscountUSD > 0 ? purchase.DiscountUSD : purchase.Discount,
                FeeUSD = purchase.FeeUSD > 0 ? purchase.FeeUSD : purchase.Fee,
                UnitPriceUSD = purchase.EffectiveUnitPriceUSD,
                HasReceipt = hasReceipt,
                ReceiptFilePath = receiptFilePath,
                StatusDisplay = statusDisplay,
                Notes = purchase.Notes,
                SupplierId = purchase.SupplierId,
                CategoryId = categoryId,
                Amount = purchase.Amount,
                TaxAmount = purchase.TaxAmount,
                TaxRate = purchase.TaxRate,
                ShippingCost = purchase.ShippingCost,
                Discount = purchase.Discount,
                Fee = purchase.Fee,
                Quantity = (int)purchase.Quantity,
                UnitPrice = purchase.UnitPrice,
                PaymentMethod = purchase.PaymentMethod,
                IsHighlighted = purchase.Id == HighlightTransactionId,
                IsPendingConversion = purchase.IsPendingConversion,
                OriginalCurrency = purchase.OriginalCurrency
            };
        }).ToList();

        // Apply sorting (only if not searching, since search has its own relevance sorting)
        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<ExpenseDisplayItem, object?>>
                {
                    ["Id"] = e => e.Id,
                    ["Accountant"] = e => e.AccountantName,
                    ["Product"] = e => e.ProductDescription,
                    ["Category"] = e => e.CategoryName,
                    ["Supplier"] = e => e.SupplierName,
                    ["Date"] = e => e.Date,
                    ["Total"] = e => e.Total,
                    ["Status"] = e => e.StatusDisplay
                },
                e => e.Date);
        }

        // Navigate to highlighted item if set (from dashboard click)
        NavigateToHighlightedItem(displayItems, x => x.Id);

        var pagedExpenses = Paginate(displayItems, "expense");

        Expenses.ReplaceAll(pagedExpenses);
    }

    private static (string name, string moreText) FormatProductDescription(Expense purchase)
    {
        if (purchase.LineItems.Count <= 1)
            return (purchase.Description, string.Empty);

        var firstName = purchase.LineItems[0].Description;
        if (string.IsNullOrEmpty(firstName))
            firstName = purchase.Description.Split(',')[0].Trim();

        var remaining = purchase.LineItems.Count - 1;
        return (firstName, $" +{remaining} more");
    }

    private static string GetStatusDisplay(Expense purchase, HashSet<string> lostDamagedIds, HashSet<string> returnedIds)
    {
        if (lostDamagedIds.Contains(purchase.Id)) return "Lost / Damaged";
        if (returnedIds.Contains(purchase.Id)) return "Returned";
        return "Completed";
    }

    #endregion

    #region Modal Commands

    [RelayCommand]
    private void OpenAddModal()
    {
        App.ExpenseModalsViewModel?.OpenAddModal();
    }

    [RelayCommand]
    private void OpenEditModal(ExpenseDisplayItem? item)
    {
        App.ExpenseModalsViewModel?.OpenEditModal(item);
    }

    [RelayCommand]
    private void OpenDeleteConfirm(ExpenseDisplayItem? item)
    {
        App.ExpenseModalsViewModel?.OpenDeleteConfirm(item);
    }

    [RelayCommand]
    private void MarkAsLostDamaged(ExpenseDisplayItem? item)
    {
        App.ExpenseModalsViewModel?.OpenMarkAsLostDamagedModal(item);
    }

    [RelayCommand]
    private void MarkAsReturned(ExpenseDisplayItem? item)
    {
        App.ExpenseModalsViewModel?.OpenMarkAsReturnedModal(item);
    }

    [RelayCommand]
    private void UndoLostDamaged(ExpenseDisplayItem? item)
    {
        App.ExpenseModalsViewModel?.OpenUndoLostDamagedModal(item);
    }

    [RelayCommand]
    private void UndoReturn(ExpenseDisplayItem? item)
    {
        App.ExpenseModalsViewModel?.OpenUndoReturnedModal(item);
    }

    [RelayCommand]
    private void OpenFilterModal()
    {
        App.ExpenseModalsViewModel?.OpenFilterModal();
    }

    #endregion

    #region Receipt Preview

    [RelayCommand]
    private void ViewReceipt(ExpenseDisplayItem? item)
    {
        if (item == null || !item.HasReceipt)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        var expense = companyData?.Expenses.FirstOrDefault(p => p.Id == item.Id);
        if (expense == null || string.IsNullOrEmpty(expense.ReceiptId))
            return;

        // The viewer renders all pages (PDFs) from the receipt's stored data.
        App.ReceiptViewerModal?.Show(expense.ReceiptId, $"Receipt for {item.Id}");
    }

    #endregion
}

/// <summary>
/// Display model for expenses in the UI.
/// </summary>
public partial class ExpenseDisplayItem : ObservableObject
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
    private string _productDescription = string.Empty;

    [ObservableProperty]
    private string _productMoreText = string.Empty;

    [ObservableProperty]
    private string _categoryName = string.Empty;

    [ObservableProperty]
    private string _supplierName = string.Empty;

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
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
    private decimal _feeUSD;

    [ObservableProperty]
    private decimal _unitPriceUSD;

    [ObservableProperty]
    private bool _hasReceipt;

    [ObservableProperty]
    private string _receiptFilePath = string.Empty;

    [ObservableProperty]
    private string _statusDisplay = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string? _supplierId;

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
    private decimal _fee;

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
    public string FeeFormatted => IsPendingConversion
        ? CurrencyService.Format(Fee)
        : CurrencyService.FormatWithOriginal(Fee, OriginalCurrency, FeeUSD, Date);
    public string UnitPriceFormatted => IsPendingConversion
        ? CurrencyService.Format(UnitPrice)
        : CurrencyService.FormatWithOriginal(UnitPrice, OriginalCurrency, UnitPriceUSD, Date);

    /// <summary>Friendly explanation for the info tooltip next to the "Pending" status badge.</summary>
    public string PendingConversionHint => CurrencyService.BuildPendingConversionHint(Total, OriginalCurrency, Date);

    public bool IsReturned => StatusDisplay == "Returned";
    public bool IsPartialReturn => StatusDisplay == "Partial Return";
    public bool IsLostDamaged => StatusDisplay == "Lost / Damaged";
    public bool CanMarkAsReturned => !IsReturned && !IsLostDamaged;
    public bool CanMarkAsLostDamaged => !IsReturned && !IsLostDamaged;

    [ObservableProperty]
    private bool _isHighlighted;
}
