using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.InvoiceTemplates;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using ArgoBooks.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArgoBooks.Helpers;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Invoices page.
/// </summary>
public partial class InvoicesPageViewModel : SortablePageViewModelBase
{
    #region Statistics

    [ObservableProperty]
    private string _totalOutstanding = "$0.00";

    [ObservableProperty]
    private string _paidThisMonth = "$0.00";

    [ObservableProperty]
    private string _overdueAmount = "$0.00";

    [ObservableProperty]
    private int _dueThisWeekCount;

    #endregion

    #region Recurring Generation Banner

    [NotifyPropertyChangedFor(nameof(GeneratedBannerText))]
    [ObservableProperty]
    private int _generatedBannerCount;

    [ObservableProperty]
    private bool _hasGeneratedBanner;

    public string GeneratedBannerText => GeneratedBannerCount == 1
        ? "1 invoice was generated from your recurring schedules."
        : $"{GeneratedBannerCount} invoices were generated from your recurring schedules.";

    [RelayCommand]
    private void ReviewGenerated()
    {
        HasGeneratedBanner = false;
        RecurringInvoiceService.ClearPendingGenerated();
        SelectedTabIndex = 3; // jump to the Recurring tab, where generated drafts wait to be sent
    }

    [RelayCommand]
    private void DismissGeneratedBanner()
    {
        HasGeneratedBanner = false;
        RecurringInvoiceService.ClearPendingGenerated();
    }

    private void OnRecurringInvoicesGenerated(int count)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            GeneratedBannerCount = count;
            HasGeneratedBanner = true;
            LoadInvoices();
            // Generation advances each schedule's next date (and may complete some), so refresh the
            // schedules table too, not just the invoice/waiting lists.
            LoadSchedules();
        });
    }

    #endregion

    #region Tab Navigation

    [ObservableProperty]
    private string _selectedTab = "All";

    public int SelectedTabIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                var tab = value switch
                {
                    0 => "All",
                    1 => "Drafts",
                    2 => "Sent",
                    3 => "Recurring",
                    _ => "All"
                };
                SelectTabInternal(tab);
            }
        }
    }

    [ObservableProperty]
    private bool _isAllInvoicesTab = true;

    [ObservableProperty]
    private bool _isDraftsTab;

    [ObservableProperty]
    private bool _isSentTab;

    [ObservableProperty]
    private bool _isRecurringTab;

    [ObservableProperty]
    private string _emptyStateTitle = "No invoices found.";

    [ObservableProperty]
    private string _emptyStateMessage = "Create your first invoice to start tracking your billing.";

    [RelayCommand]
    private void SelectTab(string tab)
    {
        SelectTabInternal(tab);
    }

    private void SelectTabInternal(string tab)
    {
        SelectedTab = tab;
        IsAllInvoicesTab = tab == "All";
        IsDraftsTab = tab == "Drafts";
        IsSentTab = tab == "Sent";
        IsRecurringTab = tab == "Recurring";

        // Update empty state messages based on tab
        EmptyStateTitle = tab switch
        {
            "Drafts" => "No Draft Invoices",
            "Sent" => "No Sent Invoices",
            "Recurring" => "No Recurring Invoices",
            _ => "No invoices found."
        };

        EmptyStateMessage = tab switch
        {
            "Drafts" => "Draft invoices you're working on will appear here.",
            "Sent" => "Invoices you've sent will appear here.",
            "Recurring" => "Set up recurring invoices to automate your billing.",
            _ => "Create your first invoice to start tracking your billing."
        };

        CurrentPage = 1;
        if (tab == "Recurring")
            LoadSchedules();
        FilterInvoices();
    }

    #endregion

    #region Plan Status and Invoice Limits

    [ObservableProperty]
    private bool _hasPremium;

    /// <summary>
    /// Whether the sample company is open. Read live rather than cached, since the
    /// page view model outlives a company switch.
    /// </summary>
    public bool IsSampleCompany => App.CompanyManager?.IsSampleCompany == true;

    /// <summary>Bound to the resend button's IsEnabled.</summary>
    public bool CanResendInvoice => !IsSampleCompany;

    /// <summary>
    /// Re-reads the two properties above. Reading live is not enough on its own: a binding
    /// only looks again when told to, and Save As turns the sample company into the user's own
    /// without closing anything or navigating, so resend stayed greyed out on the page already
    /// on screen.
    /// </summary>
    public void RefreshSampleCompanyState()
    {
        OnPropertyChanged(nameof(IsSampleCompany));
        OnPropertyChanged(nameof(CanResendInvoice));
    }

    [ObservableProperty]
    private int _sentInvoicesThisMonthCount;

    [ObservableProperty]
    private int _invoiceMonthlyLimit = FreePlanLimits.InvoiceMonthly;

    /// <summary>
    /// Gets remaining invoices the user can send (on the Free plan).
    /// </summary>
    public int RemainingInvoices => Math.Max(0, InvoiceMonthlyLimit - SentInvoicesThisMonthCount);

    /// <summary>
    /// Gets whether the user can send more invoices.
    /// </summary>
    public bool CanSendInvoice => HasPremium || RemainingInvoices > 0;

    /// <summary>
    /// Gets the text showing remaining invoices for the current month.
    /// </summary>
    public string RemainingInvoicesText => $"{RemainingInvoices} of {InvoiceMonthlyLimit} remaining this month";

    /// <summary>
    /// Gets whether to show the remaining invoices label (only on Free plan).
    /// </summary>
    public bool ShowRemainingInvoices => !HasPremium;

    /// <summary>
    /// Gets whether to show the upgrade button (when limit is reached).
    /// </summary>
    public bool ShowInvoiceUpgradeButton => !HasPremium && !CanSendInvoice;

    /// <summary>
    /// Event raised when the upgrade button is clicked.
    /// </summary>
    public event EventHandler? UpgradeRequested;

    partial void OnSentInvoicesThisMonthCountChanged(int value) => NotifyLimitProperties();
    partial void OnInvoiceMonthlyLimitChanged(int value) => NotifyLimitProperties();

    partial void OnHasPremiumChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSendInvoice));
        OnPropertyChanged(nameof(ShowRemainingInvoices));
        OnPropertyChanged(nameof(ShowInvoiceUpgradeButton));
    }

    private void NotifyLimitProperties()
    {
        OnPropertyChanged(nameof(RemainingInvoices));
        OnPropertyChanged(nameof(RemainingInvoicesText));
        OnPropertyChanged(nameof(CanSendInvoice));
        OnPropertyChanged(nameof(ShowInvoiceUpgradeButton));
    }

    [RelayCommand]
    private void Upgrade()
    {
        UpgradeRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Counts non-draft, non-cancelled invoices created in the current calendar month.
    /// </summary>
    private void UpdateSentInvoicesCount()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Invoices == null)
        {
            SentInvoicesThisMonthCount = 0;
            return;
        }

        var now = DateTime.Now;
        SentInvoicesThisMonthCount = companyData.Invoices.Count(i =>
            i.Status != InvoiceStatus.Draft &&
            i.Status != InvoiceStatus.Cancelled &&
            i.CreatedAt.Year == now.Year &&
            i.CreatedAt.Month == now.Month);
    }

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterInvoices();
        });

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string? _filterCustomerId;

    [ObservableProperty]
    private string? _filterAmountMin;

    [ObservableProperty]
    private string? _filterAmountMax;

    [ObservableProperty]
    private DateTimeOffset? _filterIssueDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _filterIssueDateTo;

    [ObservableProperty]
    private DateTimeOffset? _filterDueDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _filterDueDateTo;

    #endregion

    #region Column Visibility and Widths

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public InvoicesTableColumnWidths ColumnWidths => App.InvoicesColumnWidths;

    public RecurringTableColumnWidths RecurringColumnWidths => App.RecurringColumnWidths;

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Invoices", new Dictionary<string, bool>
    {
        ["Id"] = true,
        ["Accountant"] = true,
        ["Customer"] = true,
        ["IssueDate"] = true,
        ["DueDate"] = true,
        ["Amount"] = true,
        ["Status"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showIdColumn = ColumnDefaults.Load("Id");

    [ObservableProperty]
    private bool _showAccountantColumn = ColumnDefaults.Load("Accountant");

    [ObservableProperty]
    private bool _showCustomerColumn = ColumnDefaults.Load("Customer");

    [ObservableProperty]
    private bool _showIssueDateColumn = ColumnDefaults.Load("IssueDate");

    [ObservableProperty]
    private bool _showDueDateColumn = ColumnDefaults.Load("DueDate");

    [ObservableProperty]
    private bool _showAmountColumn = ColumnDefaults.Load("Amount");

    [ObservableProperty]
    private bool _showStatusColumn = ColumnDefaults.Load("Status");

    #endregion

    #region Invoices Collection

    private readonly List<Invoice> _allInvoices = [];

    public BatchObservableCollection<InvoiceDisplayItem> Invoices { get; } = [];

    /// <summary>The current page of recurring invoice schedules shown on the Recurring tab.</summary>
    public BatchObservableCollection<RecurringScheduleDisplayItem> Schedules { get; } = [];

    /// <summary>All schedules (unpaged); Schedules holds the current page slice of this.</summary>
    private readonly List<RecurringScheduleDisplayItem> _allSchedules = [];

    public bool HasSchedules => Schedules.Count > 0;

    // Independent pagination for the schedules table (the base-class pagination drives the invoice list).
    [ObservableProperty] private int _scheduleCurrentPage = 1;
    [ObservableProperty] private int _schedulePageSize = 10;
    [ObservableProperty] private int _scheduleTotalPages = 1;
    [ObservableProperty] private string _schedulePaginationText = string.Empty;

    /// <summary>Recurring invoices generated as drafts that still need to be sent, shown in the
    /// "Waiting to send" section of the Recurring tab (and hidden from All/Drafts until sent).</summary>
    public BatchObservableCollection<InvoiceDisplayItem> PendingRecurringInvoices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingRecurring))]
    private int _pendingRecurringCount;

    public bool HasPendingRecurring => PendingRecurringCount > 0;

    public ObservableCollection<string> StatusOptions { get; } = new(InvoiceStatusExtensions.GetFilterOptions());

    public ObservableCollection<CustomerOption> CustomerOptions { get; } = [];

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterInvoices();

    #endregion

    #region Constructor

    public InvoicesPageViewModel()
    {
        // Set default sort values for invoices
        SortColumn = "IssueDate";
        SortDirection = SortDirection.Descending;

        LoadInvoices();
        LoadCustomerOptions();

        InitializePortalState();
        UpdateOnlineStatistics();
        _ = CheckPortalStatusAsync();

        EnableDeferredUndoRefresh(p => p == PageNames.Invoices, LoadInvoices);

        // Subscribe to invoice modal events to refresh data
        if (App.InvoiceModalsViewModel != null)
        {
            App.InvoiceModalsViewModel.InvoiceSaved += OnInvoiceSaved;
            App.InvoiceModalsViewModel.InvoiceDeleted += OnInvoiceDeleted;
            App.InvoiceModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.InvoiceModalsViewModel.FiltersCleared += OnFiltersCleared;
        }

        // Subscribe to currency changes to refresh currency display
        CurrencyService.CurrencyChanged += OnCurrencyChanged;

        // Subscribe to payment provider changes so invoice display reflects current state
        // and the create button / warning banner update immediately
        PaymentProviderService.ProvidersChanged += OnProvidersChanged;

        // Subscribe to company data changes (e.g. payment sync updating invoice status)
        if (App.CompanyManager != null)
        {
            App.CompanyManager.CompanyDataChanged += OnCompanyDataChanged;
        }

        // Subscribe to plan status changes so we update when user upgrades
        App.PlanStatusChanged += OnPlanStatusChanged;

        // Recurring invoices generated on company open: show a banner. Subscribe for the live case
        // and read any pending count for the common case where generation ran before this page existed.
        RecurringInvoiceService.InvoicesGenerated += OnRecurringInvoicesGenerated;
        if (RecurringInvoiceService.PendingGeneratedCount > 0)
        {
            GeneratedBannerCount = RecurringInvoiceService.PendingGeneratedCount;
            HasGeneratedBanner = true;
        }

        // Auto-sync online payments so invoice statuses reflect portal payments
        _ = App.AutoSyncPortalPaymentsAsync();

        // Fetch server-side usage limits (updates InvoiceMonthlyLimit + SentInvoicesThisMonthCount)
        _ = RefreshUsageFromServerAsync();
    }

    /// <summary>
    /// Fetches the invoice send limit and current count from the server.
    /// </summary>
    private async Task RefreshUsageFromServerAsync()
    {
        var usageService = App.InvoiceUsageService;
        if (usageService == null || HasPremium) return;

        var result = await usageService.CheckUsageAsync();
        if (!result.Success) return;

        // Server returns monthly_limit = -1 as a sentinel for Premium /
        // unlimited. If we update SendCount but not the limit, the UI ends
        // up with SendCount > stale-default-limit and falsely flags
        // "limit reached". Treat the sentinel
        // as a Premium-equivalent: mark HasPremium so RemainingInvoices is
        // ignored downstream.
        if (result.MonthlyLimit < 0 || string.Equals(result.Tier, "premium", StringComparison.OrdinalIgnoreCase))
        {
            HasPremium = true;
            return;
        }

        if (result.MonthlyLimit > 0)
            InvoiceMonthlyLimit = result.MonthlyLimit;
        SentInvoicesThisMonthCount = result.SendCount;
    }

    private void OnCurrencyChanged(object? sender, EventArgs e)
    {
        UpdateStatistics();
        FilterInvoices();
    }

    private void OnProvidersChanged(object? sender, EventArgs e)
    {
        CheckPortalConfiguration();
        FilterInvoices();
    }

    private void OnCompanyDataChanged(object? sender, EventArgs e)
    {
        LoadInvoices();
    }

    private void OnPlanStatusChanged(object? sender, PlanStatusChangedEventArgs e)
    {
        HasPremium = e.HasPremium;
    }

    /// <summary>
    /// Cleans up event subscriptions.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (_paymentSavedHandler != null && App.PaymentModalsViewModel != null)
        {
            App.PaymentModalsViewModel.PaymentSaved -= _paymentSavedHandler;
            _paymentSavedHandler = null;
        }
        if (App.InvoiceModalsViewModel != null)
        {
            App.InvoiceModalsViewModel.InvoiceSaved -= OnInvoiceSaved;
            App.InvoiceModalsViewModel.InvoiceDeleted -= OnInvoiceDeleted;
            App.InvoiceModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.InvoiceModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
        CurrencyService.CurrencyChanged -= OnCurrencyChanged;
        PaymentProviderService.ProvidersChanged -= OnProvidersChanged;
        if (App.CompanyManager != null)
            App.CompanyManager.CompanyDataChanged -= OnCompanyDataChanged;
        App.PlanStatusChanged -= OnPlanStatusChanged;
        RecurringInvoiceService.InvoicesGenerated -= OnRecurringInvoicesGenerated;
    }

    private void OnInvoiceSaved(object? sender, EventArgs e)
    {
        LoadInvoices();
        // Saving/sending an invoice may have created a recurring schedule, so refresh the Recurring
        // tab too. Without this the new schedule only appeared after navigating away and back.
        LoadSchedules();
    }

    private void OnInvoiceDeleted(object? sender, EventArgs e)
    {
        LoadInvoices();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        var modals = App.InvoiceModalsViewModel;
        if (modals != null)
        {
            FilterStatus = modals.FilterStatus;
            FilterCustomerId = modals.FilterCustomerId;
            FilterAmountMin = modals.FilterAmountMin;
            FilterAmountMax = modals.FilterAmountMax;
            FilterIssueDateFrom = modals.FilterIssueDateFrom;
            FilterIssueDateTo = modals.FilterIssueDateTo;
            FilterDueDateFrom = modals.FilterDueDateFrom;
            FilterDueDateTo = modals.FilterDueDateTo;
        }
        CurrentPage = 1;
        FilterInvoices();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        FilterStatus = "All";
        FilterCustomerId = null;
        FilterAmountMin = null;
        FilterAmountMax = null;
        FilterIssueDateFrom = null;
        FilterIssueDateTo = null;
        FilterDueDateFrom = null;
        FilterDueDateTo = null;
        SearchQuery = null;
        CurrentPage = 1;
        FilterInvoices();
    }

    #endregion

    #region Data Loading

    private void LoadInvoices()
    {
        _allInvoices.Clear();
        Invoices.Clear();
        CheckPortalConfiguration();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Invoices == null)
            return;

        _allInvoices.AddRange(companyData.Invoices);
        UpdateStatistics();
        FilterInvoices();
        LoadPendingRecurring();
        UpdateSentInvoicesCount();
    }

    /// <summary>
    /// Builds the "Waiting to send" list for the Recurring tab: recurring-generated drafts that
    /// haven't been sent yet. Newest first. Drives the tab count badge too.
    /// </summary>
    private void LoadPendingRecurring()
    {
        var companyData = App.CompanyManager?.CompanyData;
        var items = new List<InvoiceDisplayItem>();
        if (companyData?.Invoices != null)
        {
            foreach (var invoice in companyData.Invoices.Where(IsPendingRecurring).OrderByDescending(i => i.IssueDate))
            {
                var customer = companyData.GetCustomer(invoice.CustomerId);
                items.Add(new InvoiceDisplayItem
                {
                    Id = invoice.Id,
                    InvoiceNumber = invoice.InvoiceNumber,
                    CustomerId = invoice.CustomerId,
                    CustomerName = customer?.Name ?? "Unknown Customer",
                    CustomerInitials = Helpers.InitialsHelper.From(customer?.Name),
                    IssueDate = invoice.IssueDate,
                    DueDate = invoice.DueDate,
                    Total = invoice.Total,
                    TotalUSD = invoice.EffectiveTotalUSD,
                    OriginalCurrency = invoice.OriginalCurrency,
                    Status = invoice.Status,
                    StatusDisplay = GetStatusDisplay(invoice),
                    IsRecurring = true
                });
            }
        }
        PendingRecurringInvoices.ReplaceAll(items);
        PendingRecurringCount = items.Count;
    }

    private void LoadCustomerOptions()
    {
        OptionLoader.Fill(CustomerOptions,
            OptionLoader.Customers(App.CompanyManager?.CompanyData).AsOptions<CustomerOption>(),
            new CustomerOption { Name = "All Customers" });
    }

    private void UpdateStatistics()
    {
        var now = DateTime.Now;
        var endOfWeek = now.AddDays(7);
        // Compare against a local month start: the save/send paths stamp UpdatedAt with DateTime.Now
        // (local), which is the dominant case, so a local cutoff is consistent for it. (UpdatedAt is
        // mixed-Kind - the portal payment path writes UtcNow - so no cutoff is perfect at the boundary.)
        var startOfMonth = new DateTime(now.Year, now.Month, 1);

        // Total outstanding (unpaid invoices) - calculate in USD, convert for display. Drafts are excluded:
        // a never-sent draft isn't money a customer owes. Convert each at its OWN issue date (Calculations.md §3a).
        TotalOutstanding = CurrencyService.FormatSumDisplayFromUSD(
            _allInvoices.Where(i => i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled
                && i.Status != InvoiceStatus.Draft),
            i => i.Balance, i => i.OriginalCurrency, i => i.BalanceUSD, i => i.IssueDate);

        PaidThisMonth = CurrencyService.FormatSumDisplayFromUSD(
            _allInvoices.Where(i => i.Status == InvoiceStatus.Paid && i.UpdatedAt >= startOfMonth),
            i => i.Total, i => i.OriginalCurrency, i => i.TotalUSD, i => i.IssueDate);

        // Overdue amount - drafts excluded (a never-sent draft past its due date isn't overdue money owed).
        OverdueAmount = CurrencyService.FormatSumDisplayFromUSD(
            _allInvoices.Where(i => (i.IsOverdue || i.Status == InvoiceStatus.Overdue) && i.Status != InvoiceStatus.Draft),
            i => i.Balance, i => i.OriginalCurrency, i => i.BalanceUSD, i => i.IssueDate);

        // Due this week - drafts excluded (not yet billed).
        DueThisWeekCount = _allInvoices
            .Count(i => i.DueDate >= now.Date && i.DueDate <= endOfWeek &&
                       i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled &&
                       i.Status != InvoiceStatus.Draft);
    }

    [RelayCommand]
    private void RefreshInvoices()
    {
        LoadInvoices();
        LoadCustomerOptions();
    }

    /// <summary>
    /// A recurring-generated invoice still sitting as an un-sent draft. These are surfaced in the
    /// Recurring tab's "Waiting to send" list instead of the All/Drafts tabs, so the user actions
    /// them there. Once sent they become normal Sent invoices and appear in the usual tabs.
    /// </summary>
    private static bool IsPendingRecurring(Invoice invoice) =>
        invoice.Status == InvoiceStatus.Draft && !string.IsNullOrEmpty(invoice.RecurringInvoiceId);

    private void FilterInvoices()
    {
        var companyData = App.CompanyManager?.CompanyData;
        // Waiting-to-send recurring drafts live only in the Recurring tab, not All/Drafts.
        IEnumerable<Invoice> filtered = _allInvoices.Where(i => !IsPendingRecurring(i));

        // Apply tab filter
        filtered = SelectedTab switch
        {
            "Drafts" => filtered.Where(i => i.Status == InvoiceStatus.Draft),
            // "Sent" = anything that has actually been sent, i.e. left the Draft stage (and isn't
            // Cancelled). Matching only exactly-Sent hid invoices once they became Viewed/Partial/Paid/etc.
            "Sent" => filtered.Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled),
            _ => filtered
        };

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, i => [i.Id, companyData?.GetCustomer(i.CustomerId)?.Name, i.InvoiceNumber])
                .ToList();
        }

        if (FilterStatus != "All")
        {
            if (Enum.TryParse<InvoiceStatus>(FilterStatus, out var status))
            {
                filtered = filtered.Where(i => i.Status == status ||
                    (FilterStatus == "Overdue" && i.IsOverdue));
            }
        }

        if (!string.IsNullOrEmpty(FilterCustomerId))
        {
            filtered = filtered.Where(i => i.CustomerId == FilterCustomerId);
        }

        if (decimal.TryParse(FilterAmountMin, System.Globalization.CultureInfo.InvariantCulture, out var minAmount))
        {
            filtered = filtered.Where(i => i.Total >= minAmount);
        }
        if (decimal.TryParse(FilterAmountMax, System.Globalization.CultureInfo.InvariantCulture, out var maxAmount))
        {
            filtered = filtered.Where(i => i.Total <= maxAmount);
        }

        if (FilterIssueDateFrom.HasValue)
        {
            filtered = filtered.Where(i => i.IssueDate >= FilterIssueDateFrom.Value.DateTime);
        }
        if (FilterIssueDateTo.HasValue)
        {
            filtered = filtered.Where(i => i.IssueDate <= FilterIssueDateTo.Value.DateTime);
        }

        if (FilterDueDateFrom.HasValue)
        {
            filtered = filtered.Where(i => i.DueDate >= FilterDueDateFrom.Value.DateTime);
        }
        if (FilterDueDateTo.HasValue)
        {
            filtered = filtered.Where(i => i.DueDate <= FilterDueDateTo.Value.DateTime);
        }

        // Create display items
        var allPayments = companyData?.Payments ?? new List<Payment>();
        var displayItems = filtered.Select(invoice =>
        {
            var customer = companyData?.GetCustomer(invoice.CustomerId);
            var accountant = !string.IsNullOrEmpty(invoice.AccountantId)
                ? companyData?.Accountants.FirstOrDefault(a => a.Id == invoice.AccountantId)
                : null;
            var statusDisplay = GetStatusDisplay(invoice);

            var avatarBitmap = AvatarBitmapLoader.LoadCustomer(customer);

            // Sum of processor fees the customer actually paid on top of
            // the invoice (pass_processing_fee portal payments). Lets the
            // Amount column reflect the gross customer charge (what they
            // were actually billed) rather than only the line-item total.
            // Revenue page intentionally does NOT include this (fees are
            // not revenue, just pass-through).
            var invoiceCurrency = string.IsNullOrEmpty(invoice.OriginalCurrency)
                ? "USD" : invoice.OriginalCurrency;
            decimal processorFeesPaid = 0m;
            decimal processorFeesPaidUSD = 0m;
            foreach (var p in allPayments)
            {
                if (p.InvoiceId != invoice.Id || p.IsRefund || p.ProcessingFee <= 0)
                    continue;
                var paymentCurrency = string.IsNullOrEmpty(p.OriginalCurrency) ? "USD" : p.OriginalCurrency;
                if (!string.Equals(paymentCurrency, invoiceCurrency, StringComparison.OrdinalIgnoreCase))
                    continue;
                processorFeesPaid += p.ProcessingFee;
                if (invoice.TotalUSD > 0 && invoice.Total > 0)
                    processorFeesPaidUSD += Math.Round(p.ProcessingFee * (invoice.TotalUSD / invoice.Total), 2);
                else
                    processorFeesPaidUSD += p.ProcessingFee;
            }

            return new InvoiceDisplayItem
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                AccountantName = accountant?.Name ?? "System",
                CustomerId = invoice.CustomerId,
                CustomerName = customer?.Name ?? "Unknown Customer",
                CustomerInitials = Helpers.InitialsHelper.From(customer?.Name),
                CustomerAvatarBitmap = avatarBitmap,
                HasCustomerAvatar = avatarBitmap != null,
                IssueDate = invoice.IssueDate,
                DueDate = invoice.DueDate,
                Subtotal = invoice.Subtotal,
                TaxAmount = invoice.TaxAmount,
                Total = invoice.Total,
                TotalUSD = invoice.EffectiveTotalUSD,
                ProcessorFeesPaid = processorFeesPaid,
                ProcessorFeesPaidUSD = processorFeesPaidUSD,
                AmountPaid = invoice.AmountPaid,
                Balance = invoice.Balance,
                BalanceUSD = invoice.EffectiveBalanceUSD,
                Status = invoice.Status,
                StatusDisplay = statusDisplay,
                Notes = invoice.Notes,
                OriginalCurrency = invoice.OriginalCurrency,
                IsRecurring = !string.IsNullOrEmpty(invoice.RecurringInvoiceId),
                IsHighlighted = invoice.Id == HighlightTransactionId,
                CanRefund = ComputeCanRefund(invoice, companyData)
            };
        }).ToList();

        // Apply sorting (only if not searching, since search has its own relevance sorting)
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<InvoiceDisplayItem, object?>>
                {
                    ["Id"] = i => i.Id,
                    ["Customer"] = i => i.CustomerName,
                    ["IssueDate"] = i => i.IssueDate,
                    ["DueDate"] = i => i.DueDate,
                    ["Amount"] = i => i.Total + i.ProcessorFeesPaid,
                    ["Status"] = i => i.StatusDisplay
                },
                i => i.IssueDate);
        }

        NavigateToHighlightedItem(displayItems, x => x.Id);

        var pagedInvoices = Paginate(displayItems, "invoice");

        Invoices.ReplaceAll(pagedInvoices);
    }

    private static string GetStatusDisplay(Invoice invoice)
    {
        if (invoice.IsOverdue && invoice.Status != InvoiceStatus.Paid && invoice.Status != InvoiceStatus.Cancelled)
            return "Overdue";

        // Self-heal: even if invoice.Status is stale (PartiallyRefunded
        // persisted from before the comparison-against-Total fix), derive
        // the correct refund status fresh at display time, by the rule the
        // sync recompute uses.
        if (invoice.AmountRefunded > 0 && invoice.Total > 0)
        {
            return InvoiceTotalsService.RefundedStatus(invoice) == InvoiceStatus.Refunded
                ? "Refunded"
                : "Partially Refunded";
        }

        return invoice.Status switch
        {
            InvoiceStatus.Draft => "Draft",
            InvoiceStatus.Pending => "Pending",
            InvoiceStatus.Sent => "Sent",
            InvoiceStatus.Viewed => "Viewed",
            InvoiceStatus.Partial => "Partial",
            InvoiceStatus.Paid => "Paid",
            InvoiceStatus.Overdue => "Overdue",
            InvoiceStatus.Cancelled => "Cancelled",
            InvoiceStatus.PartiallyRefunded => "Partially Refunded",
            InvoiceStatus.Refunded => "Refunded",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// True when the row's Refund icon button should be visible.
    /// Returns true when either money is still refundable OR a past refund
    /// exists (so the user can open the modal to view refund history).
    /// </summary>
    private static bool ComputeCanRefund(Invoice invoice, CompanyData? companyData)
    {
        if (companyData == null) return false;
        if (invoice.Status == InvoiceStatus.Draft || invoice.Status == InvoiceStatus.Cancelled)
            return false;

        var portalPayments = companyData.Payments
            .Where(p => p.InvoiceId == invoice.Id
                        && !p.IsRefund
                        && p.Source == PaymentSource.Online
                        && !string.IsNullOrEmpty(p.ProviderPaymentId))
            .ToList();
        if (portalPayments.Count == 0) return false;

        // Any past refund on this invoice keeps the button visible so the
        // modal can show details, even if nothing's left to refund.
        var hasRefundHistory = companyData.Payments
            .Any(r => r.InvoiceId == invoice.Id && r.IsRefund);
        if (hasRefundHistory) return true;

        foreach (var p in portalPayments)
        {
            var refunded = companyData.Payments
                .Where(r => r.IsRefund && r.RefundedFromPaymentId == p.Id)
                .Sum(r => Math.Abs(r.Amount));
            if (p.Amount - refunded > 0.01m) return true;
        }
        return false;
    }

    #endregion

    #region Portal Configuration

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCreateModalCommand))]
    private bool _isPortalConfigured;

    private void CheckPortalConfiguration() => IsPortalConfigured = PaymentProviderService.IsPortalReady();

    [RelayCommand]
    private void OpenPortalSettings()
    {
        App.SettingsModalViewModel?.OpenWithTab(SettingsTab.PaymentPortal);
    }

    #endregion

    #region Modal Commands

    private bool CanOpenCreateModal() => IsPortalConfigured;

    [RelayCommand(CanExecute = nameof(CanOpenCreateModal))]
    private void OpenCreateModal()
    {
        App.InvoiceModalsViewModel?.OpenCreateModal();
    }

    [RelayCommand(CanExecute = nameof(CanOpenCreateModal))]
    private void NewRecurringInvoice()
    {
        App.InvoiceModalsViewModel?.OpenCreateRecurringModal();
    }

    /// <summary>Rebuilds the full Recurring-tab schedule list from CompanyData, then shows the current page.</summary>
    private void LoadSchedules()
    {
        _allSchedules.Clear();
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData != null)
        {
            _allSchedules.AddRange(companyData.RecurringInvoices.Select(s =>
            {
                var customer = companyData.GetCustomer(s.CustomerId);
                var currency = s.Template?.OriginalCurrency ?? "USD";
                // The most recent invoice this schedule produced (occurrence #1 or a later generated one),
                // so the row's "View invoice" action can open it.
                var latestInvoice = companyData.Invoices
                    .Where(i => i.RecurringInvoiceId == s.Id)
                    .OrderByDescending(i => i.CreatedAt)
                    .FirstOrDefault();
                return new RecurringScheduleDisplayItem
                {
                    Id = s.Id,
                    CustomerName = customer?.Name ?? "Unknown Customer",
                    AmountFormatted = CurrencyService.FormatWithOriginal(
                        s.Amount, currency, s.Template?.TotalUSD ?? s.Amount, s.NextInvoiceDate),
                    FrequencyDisplay = s.Frequency.ToString(),
                    NextInvoiceFormatted = s.NextInvoiceDate.ToString("MMM d, yyyy"),
                    StatusDisplay = s.Status.ToString(),
                    IsPaused = s.Status == RecurringInvoiceStatus.Paused,
                    InvoiceId = latestInvoice?.Id ?? string.Empty
                };
            }));
        }
        RefreshSchedulesPage();
    }

    /// <summary>Slices the full schedule list into the current page and updates the pagination state.</summary>
    private void RefreshSchedulesPage()
    {
        var totalCount = _allSchedules.Count;
        ScheduleTotalPages = PaginationMath.TotalPages(totalCount, SchedulePageSize);
        ScheduleCurrentPage = PaginationMath.ClampPage(ScheduleCurrentPage, ScheduleTotalPages);

        Schedules.ReplaceAll(PaginationMath.Slice(_allSchedules, ScheduleCurrentPage, SchedulePageSize));
        SchedulePaginationText = PaginationTextHelper.FormatPaginationText(
            totalCount, ScheduleCurrentPage, SchedulePageSize, ScheduleTotalPages, "schedule");
        OnPropertyChanged(nameof(HasSchedules));
    }

    partial void OnScheduleCurrentPageChanged(int value) => RefreshSchedulesPage();

    partial void OnSchedulePageSizeChanged(int value)
    {
        ScheduleCurrentPage = 1;
        RefreshSchedulesPage();
    }

    [RelayCommand]
    private void ScheduleGoToPreviousPage()
    {
        if (ScheduleCurrentPage > 1) ScheduleCurrentPage--;
    }

    [RelayCommand]
    private void ScheduleGoToNextPage()
    {
        if (ScheduleCurrentPage < ScheduleTotalPages) ScheduleCurrentPage++;
    }

    [RelayCommand]
    private void ScheduleGoToPage(int page)
    {
        if (page >= 1 && page <= ScheduleTotalPages && page != ScheduleCurrentPage)
            ScheduleCurrentPage = page;
    }

    [RelayCommand]
    private async Task PauseResumeSchedule(RecurringScheduleDisplayItem? item)
    {
        if (item == null) return;
        var companyData = App.CompanyManager?.CompanyData;
        var schedule = companyData?.RecurringInvoices.FirstOrDefault(s => s.Id == item.Id);
        if (companyData == null || schedule == null) return;

        var pausing = schedule.Status != RecurringInvoiceStatus.Paused;
        // Confirm first: the buttons sit right next to each other and are easy to hit by accident.
        var confirmed = await ConfirmScheduleActionAsync(
            pausing ? "Pause recurring invoice?" : "Resume recurring invoice?",
            pausing
                ? "It will stop generating new invoices until you resume it."
                : "It will start generating invoices again on its schedule.",
            pausing ? "Pause" : "Resume");
        if (!confirmed) return;

        var oldStatus = schedule.Status;
        var newStatus = pausing ? RecurringInvoiceStatus.Paused : RecurringInvoiceStatus.Active;

        // Generation catches up from the next date, so resuming with it still in the paused months
        // would create a draft for each of them.
        var oldDate = schedule.NextInvoiceDate;
        var newDate = pausing
            ? oldDate
            : RecurrenceSchedule.FirstOnOrAfter(oldDate, schedule.Frequency, schedule.StartDate.Day, DateTime.Today);

        schedule.Status = newStatus;
        schedule.NextInvoiceDate = newDate;
        companyData.MarkAsModified();
        LoadSchedules();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            pausing ? "Pause recurring invoice" : "Resume recurring invoice",
            () => { schedule.Status = oldStatus; schedule.NextInvoiceDate = oldDate; companyData.MarkAsModified(); LoadSchedules(); },
            () => { schedule.Status = newStatus; schedule.NextInvoiceDate = newDate; companyData.MarkAsModified(); LoadSchedules(); }));
    }

    [RelayCommand]
    private async Task DeleteSchedule(RecurringScheduleDisplayItem? item)
    {
        if (item == null) return;
        var companyData = App.CompanyManager?.CompanyData;
        var schedule = companyData?.RecurringInvoices.FirstOrDefault(s => s.Id == item.Id);
        if (companyData == null || schedule == null) return;

        // Unsent drafts this schedule already generated. On delete we unlink them so they become
        // ordinary drafts (visible in the Drafts tab) rather than being stranded in "Waiting to send"
        // with no schedule behind them.
        var orphanedDrafts = companyData.Invoices
            .Where(i => i.Status == InvoiceStatus.Draft && i.RecurringInvoiceId == schedule.Id)
            .ToList();

        var draftWord = orphanedDrafts.Count == 1 ? "draft" : "drafts";
        var confirmed = await ConfirmScheduleActionAsync(
            "Delete recurring invoice?",
            orphanedDrafts.Count > 0
                ? $"This stops it from generating new invoices. Its {orphanedDrafts.Count} unsent {draftWord} will move to the Drafts tab."
                : "This stops it from generating new invoices. Invoices it already created are kept.",
            "Delete");
        if (!confirmed) return;

        var deleted = schedule;
        var index = companyData.RecurringInvoices.IndexOf(schedule);

        void ApplyDelete()
        {
            companyData.RecurringInvoices.Remove(deleted);
            foreach (var d in orphanedDrafts) d.RecurringInvoiceId = string.Empty;
            companyData.MarkAsModified();
            LoadInvoices();
            LoadSchedules();
        }
        void UndoDelete()
        {
            if (!companyData.RecurringInvoices.Contains(deleted))
            {
                if (index >= 0 && index <= companyData.RecurringInvoices.Count)
                    companyData.RecurringInvoices.Insert(index, deleted);
                else
                    companyData.RecurringInvoices.Add(deleted);
            }
            foreach (var d in orphanedDrafts) d.RecurringInvoiceId = deleted.Id;
            companyData.MarkAsModified();
            LoadInvoices();
            LoadSchedules();
        }

        ApplyDelete();
        App.UndoRedoManager.RecordAction(new DelegateAction("Delete recurring invoice", UndoDelete, ApplyDelete));
    }

    /// <summary>Shows a Primary/Cancel confirmation dialog; returns true only if the user confirmed.</summary>
    private static async Task<bool> ConfirmScheduleActionAsync(string title, string message, string primaryButton)
    {
        var dialog = App.ConfirmationDialog;
        if (dialog == null) return true;
        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = title,
            Message = message,
            PrimaryButtonText = primaryButton,
            CancelButtonText = "Cancel"
        });
        return result == ConfirmationResult.Primary;
    }

    [RelayCommand]
    private void ViewInvoice(InvoiceDisplayItem? item)
    {
        if (item == null) return;
        App.InvoiceModalsViewModel?.OpenViewInvoice(item.Id);
    }

    /// <summary>Opens the invoice most recently produced by a recurring schedule (the row's View action).</summary>
    [RelayCommand]
    private void ViewScheduleInvoice(RecurringScheduleDisplayItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.InvoiceId)) return;
        App.InvoiceModalsViewModel?.OpenViewInvoice(item.InvoiceId);
    }

    [RelayCommand]
    private void ContinueDraftInvoice(InvoiceDisplayItem? item)
    {
        App.InvoiceModalsViewModel?.ContinueDraftInvoice(item);
    }

    [RelayCommand]
    private void OpenDeleteConfirm(InvoiceDisplayItem? item)
    {
        App.InvoiceModalsViewModel?.OpenDeleteConfirm(item);
    }

    [RelayCommand]
    private void OpenFilterModal()
    {
        App.InvoiceModalsViewModel?.OpenFilterModal();
    }

    [RelayCommand]
    private void OpenHistoryModal(InvoiceDisplayItem? item)
    {
        App.InvoiceModalsViewModel?.OpenHistoryModal(item);
    }

    [RelayCommand]
    private async Task OpenRefundModal(InvoiceDisplayItem? item)
    {
        if (item == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        var invoice = companyData?.GetInvoice(item.Id);
        if (companyData == null || invoice == null) return;

        // The invoice list refreshes when the window closes, so a newly-arrived refund Payment appears.
        if (App.RefundModalsViewModel is { } refunds)
            await refunds.OpenForInvoiceAsync(companyData, invoice, onClosed: LoadInvoices);
    }

    [RelayCommand]
    private void OpenTemplateDesigner()
    {
        App.InvoiceTemplateDesignerViewModel?.OpenTemplateList();
    }

    #region Payment Portal

    [ObservableProperty]
    private string _lastSyncTime = "Never";

    [ObservableProperty]
    private bool _isPortalConnected;

    // IsPortalConfigured is declared in the Portal Configuration region above.

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _onlineReceivedThisMonth = "$0.00";

    private void InitializePortalState()
    {
        var portalSettings = App.CompanyManager?.CompanyData?.Settings.PaymentPortal;
        var hasKey = PortalSettings.IsConfigured;
        var hasConnectedProvider = PaymentProviderService.GetConnectedMethods().Count > 0;
        IsPortalConfigured = hasKey && hasConnectedProvider;
        // Assume connected until the async check says otherwise, to avoid
        // a brief flash of the "not configured" warning banner.
        IsPortalConnected = IsPortalConfigured;

        if (portalSettings?.LastSyncTime.HasValue == true)
        {
            LastSyncTime = FormatTimeSince(portalSettings.LastSyncTime.Value);
        }
    }

    private async Task CheckPortalStatusAsync()
    {
        if (!PortalSettings.IsConfigured)
        {
            IsPortalConnected = false;
            IsPortalConfigured = false;
            return;
        }

        var portalService = App.PaymentPortalService;
        if (portalService == null) return;

        var status = await portalService.CheckStatusAsync();
        IsPortalConnected = status.Connected;

        var portalSettings = App.CompanyManager?.CompanyData?.Settings.PaymentPortal;
        if (portalSettings != null && status.ConnectedProviders != null)
        {
            portalSettings.ConnectedAccounts = status.ConnectedProviders;
        }

        // Require at least one connected provider to consider the portal configured
        IsPortalConfigured = PaymentProviderService.GetConnectedMethods().Count > 0;
    }

    /// <summary>
    /// Pulls new online payments from the server.
    /// </summary>
    [RelayCommand]
    private async Task SyncPortal()
    {
        if (IsSyncing) return;

        var portalService = App.PaymentPortalService;
        var companyData = App.CompanyManager?.CompanyData;
        if (portalService == null || companyData == null) return;

        if (!PortalSettings.IsConfigured)
        {
            IsPortalConnected = false;
            return;
        }

        // The replies describe the company whose key asked; once another is open they must not land in it.
        bool CompanyChanged() => !ReferenceEquals(App.CompanyManager?.CompanyData, companyData);

        IsSyncing = true;
        try
        {
            var portalSettings = companyData.Settings.PaymentPortal;

            // force=true recovers payments confirmed server-side but never saved locally.
            var syncResponse = await portalService.SyncPaymentsAsync(since: null, force: true);
            if (CompanyChanged()) return;

            if (!syncResponse.Success)
            {
                IsPortalConnected = false;
                return;
            }

            IsPortalConnected = true;

            if (syncResponse.Payments.Count > 0)
            {
                var syncResult = PaymentPortalService.ProcessSyncedPayments(
                    syncResponse.Payments, companyData);
                var newPayments = syncResult.NewPayments;

                // Only confirm payments that were actually processed locally. Skipped
                // ones (e.g. invoice not found) stay unconfirmed so the server returns
                // them next time.
                var processedPortalIds = newPayments
                    .Where(p => p.PortalPaymentId != null)
                    .Select(p => int.Parse(p.PortalPaymentId!))
                    .ToList();
                if (processedPortalIds.Count > 0)
                {
                    await portalService.ConfirmSyncAsync(processedPortalIds);
                    if (CompanyChanged()) return;
                }

                // Also save when only existing rows were backfilled, or the in-memory
                // ProcessingFee update is lost on restart. Only auto-persist when the
                // user has no unsaved edits, so a sync can't quietly commit their
                // in-progress work.
                if ((newPayments.Count > 0 || syncResult.BackfilledRows > 0) && !(App.CompanyManager?.HasUnsavedChanges ?? false))
                {
                    try { await App.CompanyManager!.SavePaymentSyncAsync(companyData); }
                    catch { /* non-fatal */ }
                }
            }

            portalSettings.LastSyncTime = syncResponse.SyncTimestamp ?? DateTime.UtcNow;
            LastSyncTime = "Just now";

            LoadInvoices();
            UpdateOnlineStatistics();
        }
        catch (Exception)
        {
            IsPortalConnected = false;
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private void UpdateOnlineStatistics()
    {
        var now = DateTime.Now;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        var payments = App.CompanyManager?.CompanyData?.Payments ?? [];

        // Convert each payment at its OWN date before summing (Calculations.md §3a Phase 2).
        OnlineReceivedThisMonth = CurrencyService.FormatSumDisplayFromUSD(
            payments.Where(p => p.Date >= startOfMonth && p.Source == PaymentSource.Online && p.Amount > 0),
            p => p.Amount, p => p.OriginalCurrency, p => p.AmountUSD, p => p.Date);
    }

    private static string FormatTimeSince(DateTime utcTime)
    {
        var elapsed = DateTime.UtcNow - utcTime;

        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return $"{(int)elapsed.TotalDays}d ago";
    }

    #endregion

    /// <summary>Held so a cancelled modal's subscription can be detached on the next open.</summary>
    private EventHandler? _paymentSavedHandler;

    /// <summary>
    /// Records a payment against this invoice, with the invoice already chosen.
    /// </summary>
    [RelayCommand]
    private void RecordPayment(InvoiceDisplayItem? item)
    {
        if (item == null || IsSampleCompany) return;

        PaymentModalsViewModel? payments = App.PaymentModalsViewModel;
        if (payments == null) return;

        // Detach whatever a previous open left behind. PaymentSaved only fires on save, so
        // cancelling the modal left its handler subscribed and every later payment reloaded the
        // list once more per cancellation.
        if (_paymentSavedHandler != null)
        {
            payments.PaymentSaved -= _paymentSavedHandler;
            _paymentSavedHandler = null;
        }

        // Refresh the list once the payment lands so the invoice's status and
        // balance reflect it without waiting for a navigation.
        void OnSaved(object? _, EventArgs __)
        {
            if (_paymentSavedHandler != null)
            {
                payments.PaymentSaved -= _paymentSavedHandler;
                _paymentSavedHandler = null;
            }

            LoadInvoices();
        }

        _paymentSavedHandler = OnSaved;
        payments.PaymentSaved += _paymentSavedHandler;
        payments.OpenAddModalForInvoice(item.Id);
    }

    /// <summary>
    /// Re-sends an already-sent invoice to its customer.
    /// </summary>
    /// <remarks>
    /// Free-tier usage is deliberately not checked or incremented: the customer
    /// already received this invoice once, so a resend is usually a delivery
    /// failure rather than new billable activity.
    /// </remarks>
    [RelayCommand]
    private async Task ResendInvoice(InvoiceDisplayItem? item)
    {
        if (item == null || IsSampleCompany) return;

        CompanyData? companyData = App.CompanyManager?.CompanyData;
        Invoice? invoice = companyData?.GetInvoice(item.Id);
        if (companyData == null || invoice == null) return;

        Customer? customer = companyData.GetCustomer(invoice.CustomerId);
        string? customerEmail = customer?.Email;
        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            await ShowResendMessageAsync(
                "No email address",
                "This invoice's customer has no email address, so there is nowhere to send it.",
                isError: true);
            return;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not MainWindow mainWindow
            || mainWindow.MessageBoxService is not { } mbox)
        {
            return;
        }

        bool confirmed = await mbox.ConfirmAsync(
            "Resend invoice".Translate(),
            "Send invoice {0} to {1} again?".TranslateFormat(invoice.InvoiceNumber, customerEmail),
            "Send".Translate(),
            "Cancel".Translate());
        if (!confirmed) return;

        // Same layout the customer was originally sent, matching the view and
        // publish paths, rather than whatever template is default today.
        InvoiceTemplate? template = (!string.IsNullOrEmpty(invoice.TemplateId)
                ? companyData.InvoiceTemplates.FirstOrDefault(t => t.Id == invoice.TemplateId)
                : null)
            ?? companyData.InvoiceTemplates.FirstOrDefault(t => t.IsDefault);
        if (template == null)
        {
            List<InvoiceTemplate> defaults = InvoiceTemplateFactory.CreateDefaultTemplates();
            template = defaults.FirstOrDefault(t => t.IsDefault) ?? defaults.FirstOrDefault();
        }
        if (template == null) return;

        string currencySymbol = CurrencyService.GetSymbol(invoice.OriginalCurrency);
        string failure = string.Empty;

        try
        {
            // Mirrors the original send: the portal delivers the email when it is
            // configured, otherwise the desktop sends it directly.
            if (PortalSettings.IsConfigured && App.PaymentPortalService is { } portalService)
            {
                PortalPublishResponse response = await portalService.PublishInvoiceAsync(
                    invoice, companyData, template, currencySymbol);
                if (!response.Success)
                {
                    failure = string.IsNullOrEmpty(response.Message)
                        ? "The payment portal rejected the request."
                        : response.Message;
                }
            }
            else
            {
                var emailService = new InvoiceEmailService();
                InvoiceEmailResponse response = await emailService.SendInvoiceAsync(
                    invoice, template, companyData, companyData.Settings.InvoiceEmail, currencySymbol);
                if (!response.Success)
                {
                    failure = response.Message;
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        if (!string.IsNullOrEmpty(failure))
        {
            await ShowResendMessageAsync("Failed to resend invoice", failure, isError: true);
            return;
        }

        invoice.History.Add(new InvoiceHistoryEntry
        {
            Action = "Resent",
            Details = $"Invoice re-sent to {customerEmail}",
            Timestamp = DateTime.UtcNow
        });
        App.CompanyManager?.MarkAsChanged();

        LoadInvoices();

        // Same success screen the original send shows, rather than a toast.
        App.InvoiceModalsViewModel?.ShowSentSuccess(customer?.Name ?? item.CustomerName, customerEmail);
    }

    private static async Task ShowResendMessageAsync(string title, string message, bool isError)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not MainWindow mainWindow
            || mainWindow.MessageBoxService is not { } mbox)
        {
            return;
        }

        if (isError)
        {
            await mbox.ShowErrorAsync(title.Translate(), message.Translate());
        }
    }

    #endregion
}

/// <summary>
/// Display model for invoices in the UI.
/// </summary>
public partial class InvoiceDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _invoiceNumber = string.Empty;

    [ObservableProperty]
    private string _accountantName = string.Empty;

    [ObservableProperty]
    private string _customerId = string.Empty;

    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _customerInitials = string.Empty;

    [ObservableProperty]
    private Bitmap? _customerAvatarBitmap;

    [ObservableProperty]
    private bool _hasCustomerAvatar;

    [ObservableProperty]
    private DateTime _issueDate;

    [ObservableProperty]
    private DateTime _dueDate;

    [ObservableProperty]
    private decimal _subtotal;

    [ObservableProperty]
    private decimal _taxAmount;

    [ObservableProperty]
    private decimal _total;

    [ObservableProperty]
    private decimal _totalUSD;

    /// <summary>
    /// Sum of <see cref="Payment.ProcessingFee"/> the customer paid on top
    /// of the invoice, in the invoice's currency. Zero unless they paid
    /// online with <c>pass_processing_fee</c> enabled. Added to
    /// <see cref="Total"/> when formatting the Amount column so the column
    /// reflects the gross charge the customer absorbed.
    /// </summary>
    [ObservableProperty]
    private decimal _processorFeesPaid;

    [ObservableProperty]
    private decimal _processorFeesPaidUSD;

    [ObservableProperty]
    private decimal _amountPaid;

    [ObservableProperty]
    private decimal _balance;

    [ObservableProperty]
    private decimal _balanceUSD;

    [ObservableProperty]
    private InvoiceStatus _status;

    [ObservableProperty]
    private string _statusDisplay = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _isRecurring;

    [ObservableProperty]
    private string _originalCurrency = "USD";

    public string IssueDateFormatted => IssueDate.ToString("MMM d, yyyy");
    public string DueDateFormatted => DueDate.ToString("MMM d, yyyy");
    public string TotalFormatted => CurrencyService.FormatWithOriginal(
        Total + ProcessorFeesPaid,
        OriginalCurrency,
        TotalUSD + ProcessorFeesPaidUSD,
        IssueDate);

    /// <summary>
    /// Whether this invoice is a draft.
    /// </summary>
    public bool IsDraft => Status == InvoiceStatus.Draft;

    /// <summary>
    /// Whether this invoice can be sent via email (not drafts or cancelled).
    /// </summary>
    public bool CanSend => Status != InvoiceStatus.Draft && Status != InvoiceStatus.Cancelled;

    /// <summary>
    /// True when this invoice has at least one refundable portal payment with
    /// money still available to return. Set by the parent VM when constructing
    /// the display item from the underlying Invoice + Payment list.
    /// </summary>
    [ObservableProperty]
    private bool _canRefund;

    [ObservableProperty]
    private bool _isHighlighted;
}

/// <summary>
/// Display model for a recurring invoice schedule row on the Recurring tab.
/// </summary>
public partial class RecurringScheduleDisplayItem : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _customerName = string.Empty;
    [ObservableProperty] private string _amountFormatted = string.Empty;
    [ObservableProperty] private string _frequencyDisplay = string.Empty;
    [ObservableProperty] private string _nextInvoiceFormatted = string.Empty;

    [NotifyPropertyChangedFor(nameof(PauseResumeLabel))]
    [ObservableProperty] private string _statusDisplay = string.Empty;

    [NotifyPropertyChangedFor(nameof(PauseResumeLabel))]
    [ObservableProperty] private bool _isPaused;

    // Id of the invoice this schedule most recently produced, used by the "View invoice" action.
    // Empty when no invoice is linked yet (the button is hidden in that case).
    [NotifyPropertyChangedFor(nameof(HasInvoice))]
    [ObservableProperty] private string _invoiceId = string.Empty;

    public bool HasInvoice => !string.IsNullOrEmpty(InvoiceId);

    public string PauseResumeLabel => IsPaused ? "Resume" : "Pause";
}

/// <summary>
/// Customer option for dropdown.
/// </summary>
public class CustomerOption : NamedOption
{
}
