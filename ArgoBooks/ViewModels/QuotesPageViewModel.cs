using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Helpers;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Quotes page.
/// Lists quotes offered to customers and the answers that came back.
/// </summary>
public partial class QuotesPageViewModel : SortablePageViewModelBase
{
    /// <summary>A quote inside this many days of expiring is worth chasing.</summary>
    private const int ExpiringSoonDays = 7;

    #region Statistics

    [ObservableProperty]
    private int _openQuotes;

    [ObservableProperty]
    private int _acceptedQuotes;

    [ObservableProperty]
    private int _declinedQuotes;

    [ObservableProperty]
    private int _expiringSoonQuotes;

    #endregion

    #region Table Column Widths

    /// <summary>Column widths manager for the table (shared across page navigations).</summary>
    public QuotesTableColumnWidths ColumnWidths => App.QuotesColumnWidths;

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Quotes", new Dictionary<string, bool>
    {
        ["QuoteNumber"] = true,
        ["Date"] = true,
        ["Customer"] = true,
        ["ValidUntil"] = true,
        ["Total"] = true,
        ["Status"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showQuoteNumberColumn = ColumnDefaults.Load("QuoteNumber");

    [ObservableProperty]
    private bool _showDateColumn = ColumnDefaults.Load("Date");

    [ObservableProperty]
    private bool _showCustomerColumn = ColumnDefaults.Load("Customer");

    [ObservableProperty]
    private bool _showValidUntilColumn = ColumnDefaults.Load("ValidUntil");

    [ObservableProperty]
    private bool _showTotalColumn = ColumnDefaults.Load("Total");

    [ObservableProperty]
    private bool _showStatusColumn = ColumnDefaults.Load("Status");

    #endregion

    #region Tabs

    [ObservableProperty]
    private string _activeTab = "All";

    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnActiveTabChanged(string value)
    {
        CurrentPage = 1;
        FilterQuotes();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        ActiveTab = value switch
        {
            0 => "All",
            1 => "Draft",
            2 => "Sent",
            3 => "Accepted",
            4 => "Declined",
            5 => "Converted",
            _ => "All"
        };
    }

    #endregion

    #region Search

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterQuotes();
        });

    #endregion

    #region Quotes Collection

    private readonly List<Quote> _allQuotes = [];

    /// <summary>Quotes for display in the table.</summary>
    public BatchObservableCollection<QuoteDisplayItem> Quotes { get; } = [];

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterQuotes();

    #endregion

    #region Constructor

    public QuotesPageViewModel()
    {
        SortColumn = "Date";
        SortDirection = SortDirection.Descending;

        LoadQuotes();

        EnableDeferredUndoRefresh(p => p == PageNames.Quotes, LoadQuotes);

        CurrencyService.CurrencyChanged += OnCurrencyChanged;

        if (App.QuotesModalsViewModel != null)
        {
            App.QuotesModalsViewModel.QuoteSaved += OnQuoteSaved;
            App.QuotesModalsViewModel.QuoteDeleted += OnQuoteDeleted;
            App.QuotesModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.QuotesModalsViewModel.FiltersCleared += OnFiltersCleared;
        }
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        CurrentPage = 1;
        FilterQuotes();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        SearchQuery = null;
        CurrentPage = 1;
        FilterQuotes();
    }

    private void OnCurrencyChanged(object? sender, EventArgs e) => FilterQuotes();

    private void OnQuoteSaved(object? sender, EventArgs e) => LoadQuotes();

    private void OnQuoteDeleted(object? sender, EventArgs e) => LoadQuotes();

    /// <inheritdoc />
    public override void Cleanup()
    {
        base.Cleanup();
        CurrencyService.CurrencyChanged -= OnCurrencyChanged;
        if (App.QuotesModalsViewModel != null)
        {
            App.QuotesModalsViewModel.QuoteSaved -= OnQuoteSaved;
            App.QuotesModalsViewModel.QuoteDeleted -= OnQuoteDeleted;
            App.QuotesModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.QuotesModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
    }

    #endregion

    #region Data Loading

    private void LoadQuotes()
    {
        _allQuotes.Clear();
        Quotes.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Quotes == null)
            return;

        _allQuotes.AddRange(companyData.Quotes);

        UpdateStatistics();
        FilterQuotes();
    }

    private void UpdateStatistics()
    {
        // Open is what the user can still win: drafts they haven't sent, and sent quotes the
        // customer can still answer. An expired quote is neither.
        OpenQuotes = _allQuotes.Count(q => q.Status == QuoteStatus.Draft
                                           || (q.Status == QuoteStatus.Sent && !q.IsExpired));
        AcceptedQuotes = _allQuotes.Count(q => q.Status == QuoteStatus.Accepted);
        DeclinedQuotes = _allQuotes.Count(q => q.Status == QuoteStatus.Declined);
        ExpiringSoonQuotes = _allQuotes.Count(q => q.Status == QuoteStatus.Sent
                                                   && !q.IsExpired
                                                   && q.ValidUntil.Date <= DateTime.Today.AddDays(ExpiringSoonDays));
    }

    [RelayCommand]
    private void RefreshQuotes() => LoadQuotes();

    private void FilterQuotes()
    {
        var companyData = App.CompanyManager?.CompanyData;
        var customers = companyData?.Customers ?? [];

        IEnumerable<Quote> filtered = _allQuotes;

        if (ActiveTab != "All")
        {
            var tabStatus = QuoteStatusExtensions.ParseQuoteStatus(ActiveTab);
            if (tabStatus.HasValue)
                filtered = filtered.Where(q => q.Status == tabStatus.Value);
        }

        var modals = App.QuotesModalsViewModel;
        var startDate = modals?.FilterStartDate?.DateTime;
        var endDate = modals?.FilterEndDate?.DateTime;
        var filterCustomer = modals?.FilterCustomer ?? "All";
        var filterStatus = modals?.FilterStatus ?? "All";

        if (startDate.HasValue)
            filtered = filtered.Where(q => q.IssueDate.Date >= startDate.Value.Date);
        if (endDate.HasValue)
            filtered = filtered.Where(q => q.IssueDate.Date <= endDate.Value.Date);

        if (!string.IsNullOrEmpty(filterCustomer) && filterCustomer != "All")
        {
            filtered = filtered.Where(q =>
                customers.FirstOrDefault(c => c.Id == q.CustomerId)?.Name == filterCustomer);
        }

        if (!string.IsNullOrEmpty(filterStatus) && filterStatus != "All")
        {
            // Expired isn't a stored status, so it filters on the derived flag instead.
            if (filterStatus == QuoteStatusExtensions.Expired)
            {
                filtered = filtered.Where(q => q.IsExpired);
            }
            else if (QuoteStatusExtensions.ParseQuoteStatus(filterStatus) is { } statusEnum)
            {
                filtered = filtered.Where(q => q.Status == statusEnum);
            }
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered
                .RankBySearch(SearchQuery, q => [q.Id, q.QuoteNumber,
                    customers.FirstOrDefault(c => c.Id == q.CustomerId)?.Name, q.Notes])
                .ToList();
        }

        var displayItems = filtered.Select(quote =>
        {
            var customer = customers.FirstOrDefault(c => c.Id == quote.CustomerId);

            return new QuoteDisplayItem
            {
                Id = quote.Id,
                QuoteNumber = string.IsNullOrEmpty(quote.QuoteNumber) ? quote.Id : quote.QuoteNumber,
                IssueDate = quote.IssueDate,
                DateDisplay = quote.IssueDate.ToString("MMM dd, yyyy"),
                CustomerId = quote.CustomerId,
                CustomerName = customer?.Name ?? "Unknown Customer",
                CustomerEmail = customer?.Email ?? string.Empty,
                ValidUntil = quote.ValidUntil,
                ValidUntilDisplay = quote.ValidUntil.ToString("MMM dd, yyyy"),
                ItemCount = quote.LineItems.Count,
                Total = quote.Total,
                // A quote is priced in one currency and shown in that currency: it carries no USD
                // conversion, so there is nothing to restate it from.
                TotalDisplay = CurrencyService.GetSymbol(quote.OriginalCurrency) +
                               quote.Total.ToString("N2", System.Globalization.CultureInfo.InvariantCulture),
                OriginalCurrency = quote.OriginalCurrency,
                Status = quote.Status,
                IsExpired = quote.IsExpired,
                HasBeenPublished = quote.HasBeenPublished,
                ConvertedInvoiceId = quote.ConvertedInvoiceId,
                ResponseNote = quote.ResponseNote,
                Notes = quote.Notes,
                IsHighlighted = quote.Id == HighlightTransactionId
            };
        }).ToList();

        if (string.IsNullOrWhiteSpace(SearchQuery) || SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<QuoteDisplayItem, object?>>
                {
                    ["QuoteNumber"] = q => q.QuoteNumber,
                    ["Date"] = q => q.IssueDate,
                    ["Customer"] = q => q.CustomerName,
                    ["ValidUntil"] = q => q.ValidUntil,
                    ["Total"] = q => q.Total,
                    ["Status"] = q => q.StatusDisplay
                },
                q => q.IssueDate);
        }

        NavigateToHighlightedItem(displayItems, x => x.Id);

        var paged = Paginate(displayItems, "quote");

        Quotes.ReplaceAll(paged);
    }

    #endregion

    #region Modal Commands

    [RelayCommand]
    private void OpenAddModal() => App.QuotesModalsViewModel?.OpenEditor();

    [RelayCommand]
    private void OpenFilterModal() => App.QuotesModalsViewModel?.OpenFilterModal();

    [RelayCommand]
    private void ViewQuote(QuoteDisplayItem? item)
    {
        if (item == null) return;
        App.QuotesModalsViewModel?.OpenViewModal(item);
    }

    [RelayCommand]
    private void EditQuote(QuoteDisplayItem? item)
    {
        if (item == null) return;
        App.QuotesModalsViewModel?.OpenEditorFor(item);
    }

    /// <summary>
    /// Sends a quote for the first time, or resends one the customer already has. A resend has
    /// nothing to fill in, so it asks in a message box rather than reopening the editor.
    /// </summary>
    [RelayCommand]
    private async Task SendQuoteAsync(QuoteDisplayItem? item)
    {
        if (item == null) return;

        if (item.HasBeenPublished)
        {
            if (App.QuotesModalsViewModel is { } modals)
                await modals.ResendQuoteAsync(item);
            return;
        }

        App.QuotesModalsViewModel?.OpenEditorForSend(item);
    }

    /// <summary>Quotes render with the invoice templates, so this is the same designer.</summary>
    [RelayCommand]
    private void OpenTemplateDesigner() => App.InvoiceTemplateDesignerViewModel?.OpenTemplateList();

    [RelayCommand]
    private void OpenDeleteConfirm(QuoteDisplayItem? item)
    {
        if (item == null) return;
        App.QuotesModalsViewModel?.OpenDeleteConfirm(item);
    }

    [RelayCommand]
    private Task MarkAcceptedAsync(QuoteDisplayItem? item) => SetStatusAsync(item, QuoteStatus.Accepted);

    [RelayCommand]
    private Task MarkDeclinedAsync(QuoteDisplayItem? item) => SetStatusAsync(item, QuoteStatus.Declined);

    /// <summary>
    /// Records an answer the customer gave off the portal (over the phone, by reply). Mirrors what
    /// a synced answer writes, so the two look the same afterwards.
    /// </summary>
    /// <remarks>
    /// Asks first: this is the user speaking for the customer, and on a quote that already has an
    /// answer it replaces one.
    /// </remarks>
    private async Task SetStatusAsync(QuoteDisplayItem? item, QuoteStatus status)
    {
        if (item == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        var quote = companyData?.Quotes.FirstOrDefault(q => q.Id == item.Id);
        if (companyData == null || quote == null) return;

        var oldStatus = quote.Status;
        var oldRespondedAt = quote.RespondedAt;
        if (oldStatus == status) return;

        if (!await ConfirmAnswerAsync(item, quote, status, oldStatus)) return;

        quote.Status = status;
        quote.RespondedAt = DateTime.UtcNow;
        quote.UpdatedAt = DateTime.UtcNow;
        quote.History.Add(new InvoiceHistoryEntry
        {
            Action = status == QuoteStatus.Accepted ? "Accepted" : "Declined",
            Details = "Recorded in Argo Books",
            Timestamp = DateTime.UtcNow
        });
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Mark quote '{item.QuoteNumber}' {status.ToString().ToLowerInvariant()}",
            () =>
            {
                quote.Status = oldStatus;
                quote.RespondedAt = oldRespondedAt;
                companyData.MarkAsModified();
                LoadQuotes();
            },
            () =>
            {
                quote.Status = status;
                companyData.MarkAsModified();
                LoadQuotes();
            }));

        LoadQuotes();
    }

    /// <summary>
    /// Confirms recording an answer by hand, naming the quote and who it is for. Nothing is
    /// emailed either way, so the dialog says where the answer is going.
    /// </summary>
    private static Task<bool> ConfirmAnswerAsync(
        QuoteDisplayItem item, Quote quote, QuoteStatus status, QuoteStatus oldStatus)
    {
        var accepting = status == QuoteStatus.Accepted;
        var who = string.IsNullOrWhiteSpace(item.CustomerName) ? "the customer".Translate() : item.CustomerName;
        var amount = CurrencyService.Format(quote.Total, includeCode: true);

        var message = (accepting
                ? "Record that {0} accepted quote {1} for {2}?"
                : "Record that {0} declined quote {1} for {2}?")
            .TranslateFormat(who, item.QuoteNumber, amount);
        message += "\n\n" + "Nothing is emailed. Use this when they gave you their answer directly, rather than on the quote page.".Translate();

        if (oldStatus is QuoteStatus.Accepted or QuoteStatus.Declined)
        {
            message += "\n\n" + (oldStatus == QuoteStatus.Accepted
                ? "This replaces the accepted answer already on the quote."
                : "This replaces the declined answer already on the quote.").Translate();
        }

        return App.ConfirmMessageBoxAsync(
            (accepting ? "Mark as accepted?" : "Mark as declined?").Translate(),
            message,
            (accepting ? "Mark accepted" : "Mark declined").Translate(),
            "Cancel".Translate());
    }

    /// <summary>
    /// Turns a quote into a DRAFT invoice with the same customer, lines and totals, and links the
    /// two. This is the only point a quote reaches the books, and it stops at a draft: no revenue,
    /// no recurring schedule, no portal publish. The user reviews and sends it themselves.
    /// </summary>
    [RelayCommand]
    private async Task ConvertToInvoiceAsync(QuoteDisplayItem? item)
    {
        if (item == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        var quote = companyData?.Quotes.FirstOrDefault(q => q.Id == item.Id);
        if (companyData == null || quote == null) return;

        if (quote.Status == QuoteStatus.Converted)
        {
            await App.ShowInfoMessageBoxAsync(
                "Already converted".Translate(),
                "This quote is already invoice {0}.".TranslateFormat(quote.ConvertedInvoiceId ?? string.Empty));
            return;
        }

        var oldStatus = quote.Status;
        var oldConvertedId = quote.ConvertedInvoiceId;

        var invoice = QuoteConversionService.Convert(quote, companyData);
        if (invoice == null) return;
        companyData.MarkAsModified();
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.QuoteConverted);

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Convert quote '{item.QuoteNumber}' to invoice",
            () =>
            {
                companyData.Invoices.Remove(invoice);
                quote.Status = oldStatus;
                quote.ConvertedInvoiceId = oldConvertedId;
                companyData.MarkAsModified();
                LoadQuotes();
            },
            () =>
            {
                companyData.Invoices.Add(invoice);
                quote.Status = QuoteStatus.Converted;
                quote.ConvertedInvoiceId = invoice.Id;
                companyData.MarkAsModified();
                LoadQuotes();
            }));

        LoadQuotes();

        // Hand the user straight to the draft, open for editing, so they can adjust it and send it
        // rather than only look at it.
        App.NavigationService?.NavigateTo("Invoices", new TransactionNavigationParameter(invoice.Id));
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            App.InvoiceModalsViewModel?.ContinueDraftInvoice(new InvoiceDisplayItem { Id = invoice.Id }));
    }

    #endregion
}

/// <summary>
/// Display model for quotes in the UI.
/// </summary>
public partial class QuoteDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _quoteNumber = string.Empty;

    [ObservableProperty]
    private DateTime _issueDate;

    [ObservableProperty]
    private string _dateDisplay = string.Empty;

    [ObservableProperty]
    private string _customerId = string.Empty;

    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _customerEmail = string.Empty;

    [ObservableProperty]
    private DateTime _validUntil;

    [ObservableProperty]
    private string _validUntilDisplay = string.Empty;

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    private decimal _total;

    [ObservableProperty]
    private string _totalDisplay = string.Empty;

    [ObservableProperty]
    private string _originalCurrency = "USD";

    [ObservableProperty]
    private QuoteStatus _status;

    [ObservableProperty]
    private bool _isExpired;

    [ObservableProperty]
    private bool _hasBeenPublished;

    [ObservableProperty]
    private string? _convertedInvoiceId;

    [ObservableProperty]
    private string? _responseNote;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>The badge text. Expired is a view of a sent quote, never a stored status.</summary>
    public string StatusDisplay => IsExpired ? QuoteStatusExtensions.Expired : Status.ToString();

    public string StatusColor => IsExpired ? AppColors.GrayText : Status switch
    {
        QuoteStatus.Draft => AppColors.GrayText,
        QuoteStatus.Sent => AppColors.PrimaryText,
        QuoteStatus.Accepted => AppColors.SuccessText,
        QuoteStatus.Declined => AppColors.Error,
        QuoteStatus.Converted => AppColors.VioletHover,
        _ => AppColors.GrayText
    };

    public string StatusBackground => IsExpired ? AppColors.GrayLightest : Status switch
    {
        QuoteStatus.Draft => AppColors.GrayLightest,
        QuoteStatus.Sent => AppColors.PrimaryLight,
        QuoteStatus.Accepted => AppColors.SuccessLight,
        QuoteStatus.Declined => AppColors.ErrorLight,
        QuoteStatus.Converted => AppColors.VioletLight,
        _ => AppColors.GrayLightest
    };

    public string ItemsDisplay => ItemCount == 1 ? "1 item" : $"{ItemCount} items";

    /// <summary>Only a draft is still the user's own document to change.</summary>
    public bool CanEdit => Status == QuoteStatus.Draft;

    /// <summary>A converted quote is settled; everything else can go out (again).</summary>
    public bool CanSend => Status != QuoteStatus.Converted;

    /// <summary>The Send button says "Resend" once the customer already has it.</summary>
    public string SendTooltip => HasBeenPublished ? "Resend Quote" : "Send Quote";

    public bool CanConvert => Status != QuoteStatus.Converted;

    public bool CanMarkAccepted => Status is QuoteStatus.Draft or QuoteStatus.Sent or QuoteStatus.Declined;

    public bool CanMarkDeclined => Status is QuoteStatus.Draft or QuoteStatus.Sent or QuoteStatus.Accepted;
}
