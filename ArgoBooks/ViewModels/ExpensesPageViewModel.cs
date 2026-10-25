using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private string _billsToPay = "$0.00";

    [ObservableProperty]
    private string _billsToPaySubtext = string.Empty;

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
    private string? _filterProductId;

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

        // The column widths outlive the page, so a page left in select mode must not leave room for checkboxes.
        ColumnWidths.SetColumnVisibility("Select", false);
        Selection = new TableSelection(() => Expenses);
        Selection.ActiveChanged += OnSelectionActiveChanged;

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
            if (App.ReturnsModalsViewModel != null)
            {
                App.ReturnsModalsViewModel.ReturnUndone += OnExpenseDeleted;
                App.ReturnsModalsViewModel.ReturnRecorded += OnExpenseDeleted;
            }
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
        ColumnWidths.SetColumnVisibility("Select", false);
        RecurringTransactionService.ExpensesGenerated -= OnRecurringGenerated;
        RecurringSchedules.Cleanup();
        Selection.Detach();
        if (App.ExpenseModalsViewModel != null)
        {
            App.ExpenseModalsViewModel.ExpenseSaved -= OnExpenseSaved;
            App.ExpenseModalsViewModel.ExpenseDeleted -= OnExpenseDeleted;
            if (App.ReturnsModalsViewModel != null)
            {
                App.ReturnsModalsViewModel.ReturnUndone -= OnExpenseDeleted;
                App.ReturnsModalsViewModel.ReturnRecorded -= OnExpenseDeleted;
            }
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
            FilterProductId = modals.FilterProductId;
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
        FilterProductId = null;
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

        // A supplier credit shows as its purchase being returned, as a customer refund shows on its sale.
        var creditIds = ReturnRefunds.SupplierCreditIds(companyData);
        _allExpenses.AddRange(companyData.Expenses.Where(e => !creditIds.Contains(e.Id)));
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

        // Each bill in the company's currency at its own date, as every other expense total is (Calculations.md Rule 4).
        // A bill returned in full owes nothing, and the credits for returned bills come off what is owed.
        var returned = companyData == null ? [] : TransactionReturns.ReturnedById(companyData, _allExpenses);
        var bills = _allExpenses.Where(e => !e.IsPaid && !returned.GetValueOrDefault(e.Id)).ToList();
        var overdue = bills.Where(e => e.IsOverdueOn(DateTime.Today)).ToList();
        var creditsByBill = (companyData?.Returns ?? [])
            .Where(r => r.ReturnType == "Expense" && r.RefundRecordId != null)
            .Select(r => (BillId: r.OriginalTransactionId, Credit: companyData!.Expenses.FirstOrDefault(e => e.Id == r.RefundRecordId)))
            .Where(c => c.Credit is { IsPaid: false })
            .ToLookup(c => c.BillId, c => c.Credit!);
        decimal Owed(IEnumerable<Expense> expenses) => expenses
            .SelectMany(e => creditsByBill[e.Id].Prepend(e))
            .Sum(e => CurrencyService.GetDisplayAmount(e.EffectiveTotalUSD, e.Date));
        BillsToPay = CurrencyService.Format(Owed(bills));
        BillsToPaySubtext = bills.Count == 0
            ? "Nothing owed".Translate()
            : overdue.Count > 0
                ? "{0} overdue".TranslateFormat(CurrencyService.Format(Owed(overdue)))
                : "{0} unpaid, none overdue".TranslateFormat(bills.Count);
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
        var returned = companyData == null ? [] : TransactionReturns.ReturnedById(companyData, _allExpenses);
        IEnumerable<Expense> filtered = _allExpenses;

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, p => [p.Id, p.Description, p.Notes, companyData?.GetSupplier(p.SupplierId ?? "")?.Name])
                .ToList();
        }

        if (FilterStatus != "All")
        {
            filtered = filtered.Where(p => GetStatusDisplay(p, lostDamagedIds, returned) == FilterStatus);
        }

        if (!string.IsNullOrEmpty(FilterSupplierId))
        {
            filtered = filtered.Where(p => p.SupplierId == FilterSupplierId);
        }

        // Any line with the product counts, so an expense or sale of several items is found by each of them.
        if (!string.IsNullOrEmpty(FilterProductId))
        {
            filtered = filtered.Where(p => p.LineItems.Any(li => li.ProductId == FilterProductId));
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
            var statusDisplay = purchase.IsPendingConversion ? "Pending" : GetStatusDisplay(purchase, lostDamagedIds, returned);
            var (productName, productMoreText) = Utilities.TransactionDisplay.ProductSummary(purchase);
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
                OriginalCurrency = purchase.OriginalCurrency,
                BillDueDate = purchase.IsPaid ? null : purchase.DueDate
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

        Selection.UpdateShown(displayItems.Select(d => d.Id), _allExpenses.Select(e => e.Id));

        var pagedExpenses = Paginate(displayItems, "expense");

        Expenses.ReplaceAll(pagedExpenses);
        Selection.MarkRows();
    }

    private static string GetStatusDisplay(Expense purchase, HashSet<string> lostDamagedIds, Dictionary<string, bool> returned)
    {
        if (lostDamagedIds.Contains(purchase.Id)) return "Lost / Damaged";
        if (returned.TryGetValue(purchase.Id, out var all)) return all ? "Returned" : "Partial Return";
        if (!purchase.IsPaid) return purchase.IsOverdueOn(DateTime.Today) ? "Overdue" : "Unpaid";
        return "Completed";
    }

    #endregion

    #region Bulk Edit

    public TableSelection Selection { get; }

    public ObservableCollection<NamedOption> BulkProductOptions { get; } = [];

    public ObservableCollection<NamedOption> BulkCounterpartyOptions { get; } = [];

    [ObservableProperty]
    private NamedOption? _bulkProduct;

    [ObservableProperty]
    private NamedOption? _bulkCounterparty;

    private void OnSelectionActiveChanged(bool active)
    {
        ColumnWidths.SetColumnVisibility("Select", active);
        if (active)
            LoadBulkOptions();
    }

    private void LoadBulkOptions()
    {
        var data = App.CompanyManager?.CompanyData;
        OptionLoader.Fill(BulkCounterpartyOptions,
            OptionLoader.Suppliers(data).AsOptions<NamedOption>(),
            new NamedOption { Id = string.Empty, Name = "(No supplier)".Translate() });

        // A product that tracks stock is left out: swapping it would move stock between products.
        BulkProductOptions.Clear();
        if (data == null) return;
        foreach (var product in data.Products.Where(p => !p.TrackInventory).OrderBy(p => p.Name))
        {
            var category = product.CategoryId != null ? data.GetCategory(product.CategoryId) : null;
            if (category?.Type == CategoryType.Revenue)
                continue;
            BulkProductOptions.Add(new NamedOption { Id = product.Id, Name = product.Name });
        }
    }

    partial void OnBulkProductChanged(NamedOption? value)
    {
        if (value?.Id is not { Length: > 0 } productId || App.ExpenseModalsViewModel is not { } modals) return;
        var result = modals.SetProduct(Selection.SelectedIds.ToList(), productId);
        Selection.ShowResult(result,
            result.Changed == 1
                ? "Changed the product of 1 expense to {0}.".TranslateFormat(value.Name)
                : "Changed the product of {0} expenses to {1}.".TranslateFormat(result.Changed, value.Name),
            "The selected expenses already had that.".Translate(), MadeElsewhereText);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => BulkProduct = null);
    }

    partial void OnBulkCounterpartyChanged(NamedOption? value)
    {
        if (value == null || App.ExpenseModalsViewModel is not { } modals) return;
        var counterpartyId = string.IsNullOrEmpty(value.Id) ? null : value.Id;
        var result = modals.SetCounterparty(Selection.SelectedIds.ToList(), counterpartyId);
        var text = counterpartyId == null
            ? result.Changed == 1
                ? "Removed the supplier from 1 expense.".Translate()
                : "Removed the supplier from {0} expenses.".TranslateFormat(result.Changed)
            : result.Changed == 1
                ? "Changed the supplier of 1 expense to {0}.".TranslateFormat(value.Name)
                : "Changed the supplier of {0} expenses to {1}.".TranslateFormat(result.Changed, value.Name);
        Selection.ShowResult(result, text, "The selected expenses already had that.".Translate(), MadeElsewhereText);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => BulkCounterparty = null);
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        var count = Selection.SelectedIds.Count;
        if (count == 0 || App.ExpenseModalsViewModel is not { } modals) return;

        if (!await ConfirmDeleteAsync(
                count == 1 ? "Delete Expense".Translate() : "Delete Expenses".Translate(),
                count == 1
                    ? "Delete the selected expense? You can undo this.".Translate()
                    : "Delete the {0} selected expenses? You can undo this.".TranslateFormat(count)))
            return;

        var result = modals.DeleteMany(Selection.SelectedIds.ToList());
        Selection.ShowResult(result,
            result.Changed == 1 ? "Deleted 1 expense.".Translate() : "Deleted {0} expenses.".TranslateFormat(result.Changed),
            string.Empty, MadeElsewhereText);
    }

    private static string MadeElsewhereText(int count) => "{0} from payroll".TranslateFormat(count);

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
    private void Duplicate(ExpenseDisplayItem? item)
    {
        App.ExpenseModalsViewModel?.OpenDuplicateModal(item);
    }

    [RelayCommand]
    private void MakeAsset(ExpenseDisplayItem? item)
    {
        if (item == null) return;
        App.AssetModalsViewModel?.OpenForExpense(item.Id);
    }

    [RelayCommand]
    private void OpenDeleteConfirm(ExpenseDisplayItem? item)
    {
        App.ExpenseModalsViewModel?.OpenDeleteConfirm(item);
    }

    [RelayCommand]
    private void MarkBillPaid(ExpenseDisplayItem? item) => App.ExpenseModalsViewModel?.MarkBillPaid(item);

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
        if (item != null)
            App.ReturnsModalsViewModel?.OpenUndoLatestReturn(item.Id);
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
        if (expense == null)
            return;

        var receipts = companyData!.ReceiptsFor(expense, "Expense");
        if (receipts.Count == 0)
            return;

        // The viewer pages between them, and renders only the one on screen.
        App.ReceiptViewerModal?.ShowReceiptSet(
            receipts.Count == 1 ? $"Receipt for {item.Id}" : $"Documents for {item.Id}", receipts);
    }

    #endregion
}

/// <summary>
/// Display model for expenses in the UI.
/// </summary>
public partial class ExpenseDisplayItem : ObservableObject, ISelectableRow
{
    /// <summary>Ticked in select mode.</summary>
    [ObservableProperty]
    private bool _isSelected;

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
    public string TotalFormatted => Money(Total, TotalUSD);
    public string AmountFormatted => Money(Amount, AmountUSD);
    public string TaxAmountFormatted => Money(TaxAmount, TaxAmountUSD);
    public string TaxRateFormatted => $"{TaxRate:N1}%";
    public string ShippingCostFormatted => Money(ShippingCost, ShippingCostUSD);
    public string DiscountFormatted => $"-{Money(Discount, DiscountUSD)}";
    public string FeeFormatted => Money(Fee, FeeUSD);
    public string UnitPriceFormatted => Money(UnitPrice, UnitPriceUSD);

    /// <summary>Money coming back to the business, a refund from a supplier, which the page shows like income: in green, without a minus.</summary>
    public bool IsMoneyBack => Total < 0;

    // Amounts show without their sign, since the colour of the total says which way the money went.
    private string Money(decimal amount, decimal amountUSD) => IsPendingConversion
        ? CurrencyService.Format(Math.Abs(amount))
        : CurrencyService.FormatWithOriginal(Math.Abs(amount), OriginalCurrency, Math.Abs(amountUSD), Date);

    /// <summary>Friendly explanation for the info tooltip next to the "Pending" status badge.</summary>
    public string PendingConversionHint => CurrencyService.BuildPendingConversionHint(Total, OriginalCurrency, Date);

    public bool IsReturned => StatusDisplay == "Returned";
    public bool IsPartialReturn => StatusDisplay == "Partial Return";
    public bool IsLostDamaged => StatusDisplay == "Lost / Damaged";

    /// <summary>When the bill is due, while it is unpaid.</summary>
    public DateTime? BillDueDate { get; init; }

    public bool IsUnpaidBill => BillDueDate != null;

    public string BillDueHint => BillDueDate is not { } due
        ? string.Empty
        : (due - DateTime.Today).Days switch
        {
            < 0 and var d => "Overdue by {0} days (due {1})".TranslateFormat(-d, DateFormatService.Format(due)),
            0 => "Due today".Translate(),
            var d => "Due in {0} days ({1})".TranslateFormat(d, DateFormatService.Format(due))
        };
    public bool HasReturn => IsReturned || IsPartialReturn;
    public bool CanMarkAsReturned => !IsReturned && !IsLostDamaged;
    public bool CanMarkAsLostDamaged => !HasReturn && !IsLostDamaged;

    /// <summary>A purchase with a return keeps its lines as they were, since the return points at them.</summary>
    public bool CanEdit => !HasReturn;

    [ObservableProperty]
    private bool _isHighlighted;
}
