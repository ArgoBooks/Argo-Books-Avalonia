using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.InvoiceTemplates;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Quotes modals (Add/Edit, View, Filter, Send, Delete).
/// </summary>
public partial class QuotesModalsViewModel : ViewModelBase
{
    /// <summary>The server caps the personal note at 500 characters; so does the box.</summary>
    public const int MaxPersonalMessageLength = 500;

    private bool _undoRefreshWired;

    #region Events

    /// <summary>Raised when a quote is added, edited or its status changed.</summary>
    public event EventHandler? QuoteSaved;

    /// <summary>Raised when a quote is deleted.</summary>
    public event EventHandler? QuoteDeleted;

    /// <summary>Raised when filters are applied.</summary>
    public event EventHandler? FiltersApplied;

    /// <summary>Raised when filters are cleared.</summary>
    public event EventHandler? FiltersCleared;

    #endregion

    #region Editor State

    /// <summary>
    /// The create/edit editor: the quote itself as editable paper, with a sidebar beside it. The
    /// same surface the invoice editor uses, driven by this view model's own state so nothing an
    /// invoice does (revenue, recurring schedules, portal invoice publishing) can reach a quote.
    /// </summary>
    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _modalTitle = "New Quote";

    /// <summary>The rendered quote paper, bound to the document control.</summary>
    [ObservableProperty]
    private string _previewHtml = string.Empty;

    /// <summary>
    /// Preview mode renders the paper clean (no edit outlines, no add-line, no pickers) so the user
    /// sees exactly what the customer gets before sending.
    /// </summary>
    [ObservableProperty]
    private bool _isEditorPreviewing;

    /// <summary>
    /// True while a create-product / create-customer modal is open over the editor. The document
    /// control draws above Avalonia content and would cover it, so it is hidden meanwhile.
    /// </summary>
    [ObservableProperty]
    private bool _isNestedModalOpen;

    private string? _editingQuoteId;

    /// <summary>The currency the quote in the editor is priced in, which is not always the one
    /// the app is currently showing amounts in.</summary>
    private string _editorCurrencyCode = CurrencyService.CurrentCurrencyCode;

    /// <summary>
    /// The save button. A quote the customer already has is not going back to being a draft, so
    /// saving it is saving changes.
    /// </summary>
    [ObservableProperty]
    private string _saveButtonText = "Save as draft";

    /// <summary>The send button. A quote the customer already has is being sent again.</summary>
    [ObservableProperty]
    private string _sendButtonText = "Send quote";

    /// <summary>
    /// A quote is priced once and keeps that currency. Opening one later, while the app is showing
    /// a different currency, must not relabel its figures on the paper.
    /// </summary>
    private void SetEditorCurrency(string? code)
    {
        _editorCurrencyCode = string.IsNullOrEmpty(code) ? CurrencyService.CurrentCurrencyCode : code;
        OnPropertyChanged(nameof(TotalsConfigJson));
    }

    [ObservableProperty]
    private CustomerOption? _selectedCustomer;

    [ObservableProperty]
    private DateTimeOffset? _issueDate = new DateTimeOffset(DateTime.Today);

    /// <summary>
    /// The date the price stops being offered. It takes the slot the invoice paper gives the due
    /// date, so the paper's due-date editor writes here.
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset? _validUntil = new DateTimeOffset(DateTime.Today.AddDays(30));

    [ObservableProperty]
    private InvoiceTemplate? _selectedTemplate;

    [ObservableProperty]
    private string _modalNotes = string.Empty;

    // Totals fields, all edited directly in the totals block on the paper.
    [ObservableProperty]
    private decimal _taxRate;

    [ObservableProperty]
    private bool _taxIsFixed;

    [ObservableProperty]
    private decimal _shippingAmount;

    [ObservableProperty]
    private decimal _discountAmount;

    [ObservableProperty]
    private bool _discountIsPercent;

    [ObservableProperty]
    private string _customFeeLabel = string.Empty;

    [ObservableProperty]
    private decimal _customFeeAmount;

    [ObservableProperty]
    private bool _customFeeIsPercent;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationMessage;

    [ObservableProperty]
    private bool _hasCustomerError;

    /// <summary>Line items for the quote being written, edited in place on the paper.</summary>
    public ObservableCollection<QuoteLineViewModel> LineItems { get; } = [];

    public ObservableCollection<CustomerOption> CustomerOptions { get; } = [];

    public ObservableCollection<ProductOption> ProductOptions { get; } = [];

    /// <summary>The invoice templates, which quotes share so both documents look the same.</summary>
    public ObservableCollection<InvoiceTemplate> TemplateOptions { get; } = [];

    /// <summary>The quote number for the paper: the existing one when editing, else the next.</summary>
    public string QuoteNumberDisplay
    {
        get
        {
            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null) return string.Empty;
            if (!string.IsNullOrEmpty(_editingQuoteId))
            {
                var existing = companyData.Quotes.FirstOrDefault(q => q.Id == _editingQuoteId);
                if (existing != null) return existing.QuoteNumber;
            }
            var next = companyData.IdCounters.Quote + 1;
            return $"#QUO-{DateTime.UtcNow.Year}-{next:D5}";
        }
    }

    private sealed record LineState(string? ProductId, string Description, decimal? Quantity, decimal? UnitPrice, decimal Discount);

    private sealed record EditState(
        string? CustomerId, DateTimeOffset? IssueDate, DateTimeOffset? ValidUntil,
        decimal TaxRate, bool TaxIsFixed, decimal Shipping, decimal Discount, bool DiscountIsPercent,
        string FeeLabel, decimal FeeAmount, bool FeeIsPercent,
        string Notes, string? TemplateId, Helpers.EquatableArray<LineState> LineItems);

    // The form as the editor opened, for change detection.
    private EditState? _original;

    private EditState Capture() => new(
        SelectedCustomer?.Id, IssueDate, ValidUntil,
        TaxRate, TaxIsFixed, ShippingAmount, DiscountAmount, DiscountIsPercent,
        CustomFeeLabel, CustomFeeAmount, CustomFeeIsPercent,
        ModalNotes, SelectedTemplate?.Id,
        new Helpers.EquatableArray<LineState>(LineItems.Select(li =>
            new LineState(li.ProductId, li.Description, li.Quantity, li.UnitPrice, li.Discount))));

    /// <summary>True when anything has been written into a fresh quote.</summary>
    public bool HasEnteredData =>
        !IsEditMode && (
            SelectedCustomer != null ||
            TaxRate != 0 || ShippingAmount != 0 || DiscountAmount != 0 || CustomFeeAmount != 0 ||
            !string.IsNullOrWhiteSpace(ModalNotes) ||
            LineItems.Any(li => !string.IsNullOrWhiteSpace(li.Description) || (li.UnitPrice ?? 0) != 0));

    /// <summary>True when the editor differs from what it opened with.</summary>
    public bool HasEditorChanges => IsEditMode && Capture() != _original;

    partial void OnSelectedCustomerChanged(CustomerOption? value)
    {
        if (value != null && !string.IsNullOrEmpty(value.Id))
            HasCustomerError = false;
        RegeneratePaper();
    }

    partial void OnSelectedTemplateChanged(InvoiceTemplate? value) => RegeneratePaper();
    partial void OnIssueDateChanged(DateTimeOffset? value) => RegeneratePaper();
    partial void OnValidUntilChanged(DateTimeOffset? value) => RegeneratePaper();
    // Tax, shipping, discount and fee amounts are typed straight into the paper, the same as a
    // line's description or rate, so they must NOT re-render: rebuilding the page mid-keystroke
    // recreates the field the caret is in and the next character goes nowhere. The paper keeps
    // its own totals up to date as you type, and the figures are read back out of it on blur.
    partial void OnTaxRateChanged(decimal value) { }
    partial void OnShippingAmountChanged(decimal value) { }
    partial void OnDiscountAmountChanged(decimal value) { }
    partial void OnCustomFeeAmountChanged(decimal value) { }

    // Percent against fixed is a click on the swap button, not typing, so a re-render is what
    // puts the new symbol on the paper.
    partial void OnTaxIsFixedChanged(bool value) => RegeneratePaper();
    partial void OnDiscountIsPercentChanged(bool value) => RegeneratePaper();
    partial void OnCustomFeeIsPercentChanged(bool value) => RegeneratePaper();
    partial void OnValidationMessageChanged(string value) => HasValidationMessage = !string.IsNullOrEmpty(value);

    // ---- Sending. The editor's preview step IS the send step: there is no second screen. ----

    /// <summary>Where the quote goes. Defaults to the customer's address, and is saved back to them.</summary>
    [ObservableProperty]
    private string _sendRecipientEmail = string.Empty;

    /// <summary>The optional personal note the server puts in the email.</summary>
    [ObservableProperty]
    private string _sendMessage = string.Empty;

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private bool _isSendSuccess;

    [ObservableProperty]
    private string? _sendError;

    /// <summary>
    /// The success card's headline. A send and a "they had already answered" are both successes as
    /// far as the request goes, and telling the user "Quote sent." for the second would be a lie.
    /// </summary>
    [ObservableProperty]
    private string _sendSuccessTitle = "Quote sent.";

    [ObservableProperty]
    private string _sendSuccessDetail = string.Empty;

    /// <summary>
    /// False when the payment portal isn't set up. The server writes and sends the quote email and
    /// hosts the page the customer answers on, so without it there is nothing to send.
    /// </summary>
    [ObservableProperty]
    private bool _isPortalReady;

    private CancellationTokenSource? _sendCts;

    public bool HasSendError => !string.IsNullOrEmpty(SendError);

    /// <summary>"120 / 500", so the cap is visible before the server enforces it.</summary>
    public string SendMessageCounter => $"{SendMessage?.Length ?? 0} / {MaxPersonalMessageLength}";

    /// <summary>The paper and its sidebar, replaced by the sending and success cards.</summary>
    public bool ShowEditorBody => !IsSending && !IsSendSuccess;

    partial void OnSendErrorChanged(string? value) => OnPropertyChanged(nameof(HasSendError));

    partial void OnSendMessageChanged(string value) => OnPropertyChanged(nameof(SendMessageCounter));

    partial void OnIsSendingChanged(bool value) => OnPropertyChanged(nameof(ShowEditorBody));

    partial void OnIsSendSuccessChanged(bool value) => OnPropertyChanged(nameof(ShowEditorBody));

    #endregion

    #region View Modal State

    [ObservableProperty]
    private bool _isViewModalOpen;

    [ObservableProperty]
    private QuoteDisplayItem? _viewingQuote;

    /// <summary>The quote rendered with its own template, shown in the read-only preview.</summary>
    [ObservableProperty]
    private string? _viewPreviewHtml;

    #endregion

    #region Delete

    /// <summary>
    /// Confirms and deletes a quote. A quote that reached the portal is cancelled there first, so
    /// its link stops accepting answers; a failed cancel doesn't block the delete, because the
    /// record the user asked to remove is the local one.
    /// </summary>
    public async void OpenDeleteConfirm(QuoteDisplayItem item)
    {
        try
        {
            if (!await ConfirmDeleteAsync("Delete Quote".Translate(),
                    "Are you sure you want to delete this quote?\n\nQuote #: {0}\nTotal: {1}".TranslateFormat(item.QuoteNumber, item.TotalDisplay)))
                return;

            var companyData = App.CompanyManager?.CompanyData;
            var quote = companyData?.Quotes.FirstOrDefault(q => q.Id == item.Id);
            if (companyData == null || quote == null) return;

            if (quote.HasBeenPublished)
                await CancelPublishedQuoteAsync(quote, companyData);

            RemoveWithUndo(companyData, companyData.Quotes, quote, $"Delete quote '{item.QuoteNumber}'",
                () => QuoteDeleted?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Quote.OpenDeleteConfirm");
        }
    }

    private static async Task CancelPublishedQuoteAsync(Quote quote, CompanyData companyData)
    {
        if (!PortalSettings.IsConfigured) return;
        if (App.PaymentPortalService is not { } portalService) return;

        try
        {
            await portalService.PublishQuoteAsync(quote, companyData, template: null,
                sendEmail: false, message: null, status: "cancelled");
        }
        catch (Exception ex)
        {
            // Best effort: the user asked to delete the quote, not to wait on the portal.
            App.ErrorLogger?.LogWarning($"Could not cancel quote {quote.Id} on the portal: {ex.Message}", "Quote.Cancel");
        }
    }

    #endregion

    #region Editor Commands

    /// <summary>Opens the editor on a blank quote.</summary>
    [RelayCommand]
    public void OpenEditor()
    {
        ResetEditor();
        IsEditMode = false;
        _editingQuoteId = null;
        ModalTitle = "New Quote";

        AddLineItem();

        IsEditorOpen = true;
        RegeneratePaper();
    }

    /// <summary>Opens the editor on an existing quote.</summary>
    public void OpenEditorFor(QuoteDisplayItem item)
    {
        var companyData = App.CompanyManager?.CompanyData;
        var quote = companyData?.Quotes.FirstOrDefault(q => q.Id == item.Id);
        if (quote == null) return;

        ResetEditor();

        IsEditMode = true;
        _editingQuoteId = quote.Id;
        ModalTitle = "Edit Quote";
        SetEditorCurrency(quote.OriginalCurrency);
        SaveButtonText = quote.HasBeenPublished ? "Save changes" : "Save as draft";
        SendButtonText = quote.HasBeenPublished ? "Resend quote" : "Send quote";
        SelectedCustomer = CustomerOptions.FirstOrDefault(c => c.Id == quote.CustomerId);
        IssueDate = new DateTimeOffset(quote.IssueDate);
        ValidUntil = new DateTimeOffset(quote.ValidUntil);
        ModalNotes = quote.Notes;
        TaxRate = quote.TaxRate;
        TaxIsFixed = quote.TaxIsFixed;
        ShippingAmount = quote.ShippingAmount;
        DiscountAmount = quote.DiscountAmount;
        DiscountIsPercent = quote.DiscountIsPercent;
        CustomFeeLabel = quote.CustomFeeLabel;
        CustomFeeAmount = quote.CustomFeeAmount;
        CustomFeeIsPercent = quote.CustomFeeIsPercent;
        SelectedTemplate = TemplateOptions.FirstOrDefault(t => t.Id == quote.TemplateId)
            ?? TemplateOptions.FirstOrDefault(t => t.IsDefault)
            ?? TemplateOptions.FirstOrDefault();

        foreach (var line in quote.LineItems)
            AddLine(new QuoteLineViewModel
            {
                ProductId = line.ProductId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Discount = line.Discount,
                TaxRate = line.TaxRate
            });

        if (LineItems.Count == 0) AddLineItem();

        _original = Capture();

        IsEditorOpen = true;
        RegeneratePaper();
    }

    /// <summary>
    /// Opens the editor on a quote already in its send step, so sending from the list is one
    /// screen and not two.
    /// </summary>
    public void OpenEditorForSend(QuoteDisplayItem item)
    {
        OpenEditorFor(item);
        if (!IsEditorOpen) return;
        ShowEditorPreview();
    }

    [RelayCommand]
    private void CloseEditor()
    {
        IsEditorOpen = false;
        ResetEditor();
    }

    /// <summary>Closes the editor, asking first when there is something to lose.</summary>
    [RelayCommand]
    public async Task RequestCloseEditorAsync()
    {
        // A send in flight owns the modal; the Cancel button on the sending card is the way out.
        if (IsSending) return;

        // After a send there is nothing left to lose: the quote is saved and the customer has it.
        if (IsSendSuccess)
        {
            CloseEditor();
            return;
        }

        // The personal note counts: it is written on the send step, and closing from there used to
        // throw it away without asking.
        var hasChanges = (IsEditMode ? HasEditorChanges : HasEnteredData)
                         || !string.IsNullOrWhiteSpace(SendMessage);
        if (hasChanges)
        {
            bool confirmed;
            IsNestedModalOpen = true;
            try
            {
                confirmed = IsEditMode
                    ? await ConfirmDiscardEditsAsync()
                    : await ConfirmDiscardNewAsync();
            }
            finally
            {
                IsNestedModalOpen = false;
            }

            if (!confirmed)
                return;
        }

        CloseEditor();
    }

    /// <summary>
    /// Moves to the send step: the paper renders exactly as the customer will see it, and the
    /// sidebar swaps its fields for the email address and the personal note.
    /// </summary>
    [RelayCommand]
    private void ShowEditorPreview()
    {
        ValidationMessage = string.Empty;
        SendError = null;
        IsPortalReady = PortalSettings.IsConfigured;

        // Default the address to the customer's, but never overwrite one the user typed.
        if (string.IsNullOrWhiteSpace(SendRecipientEmail))
        {
            var customer = App.CompanyManager?.CompanyData?.GetCustomer(SelectedCustomer?.Id ?? string.Empty);
            SendRecipientEmail = customer?.Email ?? string.Empty;
        }

        IsEditorPreviewing = true;
        RegeneratePaper();
    }

    [RelayCommand]
    private void BackToEditor()
    {
        IsEditorPreviewing = false;
        RegeneratePaper();
    }

    /// <summary>
    /// Saves the quote as a draft and closes the editor. A draft is not going anywhere, so it
    /// saves whatever is on the paper: nothing here is required until the quote is sent.
    /// </summary>
    [RelayCommand]
    private void SaveAsDraft()
    {
        var savedId = TrySaveQuote(validate: false);
        if (savedId == null) return;

        // Editing a quote the customer already has changes nothing on their end until it goes out
        // again, and nothing else on screen would tell them that.
        var saved = App.CompanyManager?.CompanyData?.Quotes.FirstOrDefault(q => q.Id == savedId);
        if (saved?.HasBeenPublished == true)
        {
            App.AddNotification(
                "Quote saved".Translate(),
                "Your customer still sees the version you sent. Resend the quote to give them this one.".Translate(),
                NotificationType.Info);
        }

        QuoteSaved?.Invoke(this, EventArgs.Empty);
        CloseEditor();
    }

    /// <summary>
    /// The checks a quote must pass before a customer can receive it. Separate from saving so the
    /// send can complain, and be confirmed, before anything is written.
    /// </summary>
    private bool ValidateForSend()
    {
        ValidationMessage = string.Empty;
        HasCustomerError = false;

        var errors = new List<string>();

        if (SelectedCustomer == null || string.IsNullOrEmpty(SelectedCustomer.Id))
        {
            HasCustomerError = true;
            errors.Add("Choose a customer in the Bill To box.".Translate());
        }

        // A line the user never filled in is not an error, it is an empty row on the paper.
        // Only the absence of ANY priced line is.
        if (BuildLineItems().Count == 0)
            errors.Add("Add at least one line with a description.".Translate());

        if (ValidUntil?.DateTime.Date < IssueDate?.DateTime.Date)
            errors.Add("The valid-until date cannot be before the issue date.".Translate());

        if (errors.Count == 0) return true;

        ValidationMessage = string.Join("\n", errors);
        return false;
    }

    /// <summary>
    /// Persists the quote. Returns the saved id, or null when it could not be saved.
    /// </summary>
    /// <param name="validate">
    /// False for a draft, which saves as-is. The checks below are about what a customer must
    /// never receive, so they belong on the send, not on a save the user may come back to.
    /// </param>
    private string? TrySaveQuote(bool validate = true)
    {
        if (validate && !ValidateForSend()) return null;

        ValidationMessage = string.Empty;
        HasCustomerError = false;

        var lines = BuildLineItems();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return null;

        return IsEditMode && !string.IsNullOrEmpty(_editingQuoteId)
            ? SaveEditedQuote(companyData, lines)
            : SaveNewQuote(companyData, lines);
    }

    private string SaveNewQuote(CompanyData companyData, List<LineItem> lines)
    {
        var ids = new IdGenerator(companyData);
        var quote = new Quote
        {
            Id = ids.NextQuoteId(),
            QuoteNumber = ids.NextQuoteNumber(),
            CustomerId = SelectedCustomer?.Id ?? string.Empty,
            IssueDate = IssueDate?.DateTime ?? DateTime.Today,
            ValidUntil = ValidUntil?.DateTime ?? DateTime.Today.AddDays(30),
            Status = QuoteStatus.Draft,
            TemplateId = SelectedTemplate?.Id ?? string.Empty,
            OriginalCurrency = _editorCurrencyCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ApplyEditorToQuote(quote, lines);
        quote.History.Add(new InvoiceHistoryEntry { Action = "Created", Timestamp = DateTime.UtcNow });

        companyData.Quotes.Add(quote);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.QuoteCreated);
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Create quote '{quote.QuoteNumber}'",
            () =>
            {
                companyData.Quotes.Remove(quote);
                companyData.MarkAsModified();
                QuoteSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.Quotes.Add(quote);
                companyData.MarkAsModified();
                QuoteSaved?.Invoke(this, EventArgs.Empty);
            }));

        return quote.Id;
    }

    private string? SaveEditedQuote(CompanyData companyData, List<LineItem> lines)
    {
        var quote = companyData.Quotes.FirstOrDefault(q => q.Id == _editingQuoteId);
        if (quote == null) return null;

        var before = Snapshot(quote);

        quote.CustomerId = SelectedCustomer?.Id ?? string.Empty;
        quote.IssueDate = IssueDate?.DateTime ?? DateTime.Today;
        quote.ValidUntil = ValidUntil?.DateTime ?? DateTime.Today.AddDays(30);
        quote.TemplateId = SelectedTemplate?.Id ?? string.Empty;
        quote.UpdatedAt = DateTime.UtcNow;
        ApplyEditorToQuote(quote, lines);
        companyData.MarkAsModified();

        var after = Snapshot(quote);
        var edited = quote;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit quote '{quote.QuoteNumber}'",
            () =>
            {
                Restore(edited, before);
                companyData.MarkAsModified();
                QuoteSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                Restore(edited, after);
                companyData.MarkAsModified();
                QuoteSaved?.Invoke(this, EventArgs.Empty);
            }));

        return quote.Id;
    }

    /// <summary>
    /// Writes the editor's figures onto a quote, using the same arithmetic the paper's live
    /// recompute and the invoice totals use, so what the customer saw is what gets stored.
    /// </summary>
    private void ApplyEditorToQuote(Quote quote, List<LineItem> lines)
    {
        quote.LineItems = lines;
        quote.Notes = ModalNotes;
        quote.TaxRate = TaxRate;
        quote.TaxIsFixed = TaxIsFixed;
        quote.ShippingAmount = ShippingAmount;
        quote.DiscountAmount = DiscountAmount;
        quote.DiscountIsPercent = DiscountIsPercent;
        quote.CustomFeeLabel = CustomFeeLabel;
        quote.CustomFeeAmount = CustomFeeAmount;
        quote.CustomFeeIsPercent = CustomFeeIsPercent;

        quote.Subtotal = InvoiceMath.Subtotal(lines);
        var discount = InvoiceMath.Discount(quote.Subtotal, DiscountAmount, DiscountIsPercent);
        var fee = InvoiceMath.CustomFee(quote.Subtotal, CustomFeeAmount, CustomFeeIsPercent);
        var taxableBase = InvoiceMath.TaxableBase(quote.Subtotal, discount, fee, ShippingAmount);
        quote.TaxAmount = InvoiceMath.Tax(taxableBase, TaxRate, TaxIsFixed);
        quote.Total = InvoiceMath.Total(taxableBase, quote.TaxAmount, securityDeposit: 0m);
    }

    private sealed record QuoteSnapshot(
        string CustomerId, DateTime IssueDate, DateTime ValidUntil, List<LineItem> LineItems,
        decimal Subtotal, decimal TaxRate, bool TaxIsFixed, decimal TaxAmount, decimal Total,
        decimal Shipping, decimal Discount, bool DiscountIsPercent,
        string FeeLabel, decimal FeeAmount, bool FeeIsPercent,
        string Notes, string TemplateId);

    private static QuoteSnapshot Snapshot(Quote quote) => new(
        quote.CustomerId, quote.IssueDate, quote.ValidUntil, [.. quote.LineItems],
        quote.Subtotal, quote.TaxRate, quote.TaxIsFixed, quote.TaxAmount, quote.Total,
        quote.ShippingAmount, quote.DiscountAmount, quote.DiscountIsPercent,
        quote.CustomFeeLabel, quote.CustomFeeAmount, quote.CustomFeeIsPercent,
        quote.Notes, quote.TemplateId);

    private static void Restore(Quote quote, QuoteSnapshot state)
    {
        quote.CustomerId = state.CustomerId;
        quote.IssueDate = state.IssueDate;
        quote.ValidUntil = state.ValidUntil;
        quote.LineItems = [.. state.LineItems];
        quote.Subtotal = state.Subtotal;
        quote.TaxRate = state.TaxRate;
        quote.TaxIsFixed = state.TaxIsFixed;
        quote.TaxAmount = state.TaxAmount;
        quote.Total = state.Total;
        quote.ShippingAmount = state.Shipping;
        quote.DiscountAmount = state.Discount;
        quote.DiscountIsPercent = state.DiscountIsPercent;
        quote.CustomFeeLabel = state.FeeLabel;
        quote.CustomFeeAmount = state.FeeAmount;
        quote.CustomFeeIsPercent = state.FeeIsPercent;
        quote.Notes = state.Notes;
        quote.TemplateId = state.TemplateId;
        quote.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The priced lines, dropping rows the user left blank. A line needs only a description: quotes
    /// often price work that is not a product, and the picker is a shortcut, not a requirement.
    /// </summary>
    private List<LineItem> BuildLineItems() =>
        [.. LineItems
            .Where(li => !string.IsNullOrWhiteSpace(li.Description))
            .Select(li => new LineItem
            {
                ProductId = li.ProductId,
                Description = li.Description,
                Quantity = li.Quantity ?? 0,
                UnitPrice = li.UnitPrice ?? 0,
                Discount = li.Discount,
                TaxRate = li.TaxRate
            })];

    private void ResetEditor()
    {
        EnsureUndoRefreshWired();
        LoadCustomerOptions();
        LoadProductOptions();
        LoadTemplateOptions();

        // The pickers built into the paper read these, and the control only rebuilds them when the
        // property changes. Without this the document opens believing there are no customers.
        OnPropertyChanged(nameof(CustomersJson));
        OnPropertyChanged(nameof(ProductsJson));

        foreach (var li in LineItems)
            li.PropertyChanged -= OnLinePropertyChanged;
        LineItems.Clear();

        SelectedCustomer = null;
        IssueDate = new DateTimeOffset(DateTime.Today);
        ValidUntil = new DateTimeOffset(DateTime.Today.AddDays(30));
        ModalNotes = string.Empty;
        TaxRate = 0;
        TaxIsFixed = false;
        ShippingAmount = 0;
        DiscountAmount = 0;
        DiscountIsPercent = false;
        CustomFeeLabel = string.Empty;
        CustomFeeAmount = 0;
        CustomFeeIsPercent = false;
        ValidationMessage = string.Empty;
        HasCustomerError = false;
        IsEditorPreviewing = false;
        IsNestedModalOpen = false;
        IsEditMode = false;
        _editingQuoteId = null;
        _original = null;
        _paperLogo = null;
        SetEditorCurrency(null);
        SaveButtonText = "Save as draft";
        SendButtonText = "Send quote";

        AbortSend();
        SendRecipientEmail = string.Empty;
        SendMessage = string.Empty;
        SendError = null;
        IsSendSuccess = false;
        SendSuccessTitle = "Quote sent.";
        SendSuccessDetail = "You will see their answer here once they accept or decline.";
        IsPortalReady = PortalSettings.IsConfigured;
    }

    // Drop an in-flight send rather than leave a request that flips the quote to Sent after the
    // user has moved on.
    private void AbortSend()
    {
        _sendCts?.Cancel();
        _sendCts?.Dispose();
        _sendCts = null;
        IsSending = false;
    }

    private void AddLine(QuoteLineViewModel line)
    {
        line.PropertyChanged += OnLinePropertyChanged;
        LineItems.Add(line);
    }

    private void AddLineItem() => AddLine(new QuoteLineViewModel());

    // Description, quantity and rate are typed straight into the page and must NOT re-render, that
    // would interrupt typing. Only a product pick (which rewrites two fields at once) re-renders,
    // and it does so from its own handler.
    private void OnLinePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
    }

    /// <summary>
    /// Keeps the open editor honest when the user undoes a create behind it. Wired on first
    /// open rather than in a constructor because the undo manager belongs to the shell.
    /// </summary>
    private void EnsureUndoRefreshWired()
    {
        if (_undoRefreshWired) return;
        _undoRefreshWired = true;

        App.UndoRedoManager.StateChanged += (_, _) => RefreshOptionsAfterUndoRedo();
    }

    private void RefreshOptionsAfterUndoRedo()
    {
        if (!IsEditorOpen) return;

        LoadCustomerOptions();
        LoadProductOptions();
        OnPropertyChanged(nameof(CustomersJson));
        OnPropertyChanged(nameof(ProductsJson));

        if (SelectedCustomer != null && CustomerOptions.All(c => c.Id != SelectedCustomer.Id))
            SelectedCustomer = null;

        // A line keeps whatever was typed; it just stops claiming to be a product that is gone.
        foreach (var line in LineItems)
        {
            if (!string.IsNullOrEmpty(line.ProductId) && ProductOptions.All(p => p.Id != line.ProductId))
                line.ProductId = null;
        }

        RegeneratePaper();
    }

    private void LoadCustomerOptions() =>
        OptionLoader.Fill(CustomerOptions,
            OptionLoader.Customers(App.CompanyManager?.CompanyData).AsOptions<CustomerOption>());

    private void LoadProductOptions()
    {
        ProductOptions.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Products == null) return;

        // Revenue products only: a quote offers something to a customer, not something bought.
        foreach (var product in companyData.Products
                     .Where(p => p.Type == CategoryType.Revenue)
                     .OrderBy(p => p.Name))
        {
            ProductOptions.Add(new ProductOption
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                UnitPrice = product.UnitPrice
            });
        }
    }

    private void LoadTemplateOptions()
    {
        TemplateOptions.Clear();
        foreach (var template in ResolveTemplates(App.CompanyManager?.CompanyData))
            TemplateOptions.Add(template);

        if (SelectedTemplate == null || TemplateOptions.All(t => t.Id != SelectedTemplate.Id))
            SelectedTemplate = TemplateOptions.FirstOrDefault(t => t.IsDefault) ?? TemplateOptions.FirstOrDefault();
    }

    /// <summary>
    /// The company's invoice templates, or the built-in defaults when it has none saved yet, so a
    /// brand-new company can still write a quote that looks like something.
    /// </summary>
    private static List<InvoiceTemplate> ResolveTemplates(CompanyData? companyData)
    {
        var templates = companyData?.InvoiceTemplates ?? [];
        return templates.Count > 0 ? [.. templates] : InvoiceTemplateFactory.CreateDefaultTemplates();
    }

    /// <summary>
    /// The template a quote should be rendered with: the one it was created with, else the
    /// company default, else a built-in.
    /// </summary>
    internal static InvoiceTemplate? ResolveTemplate(CompanyData? companyData, string? templateId)
    {
        var templates = ResolveTemplates(companyData);
        return (!string.IsNullOrEmpty(templateId)
                   ? templates.FirstOrDefault(t => t.Id == templateId)
                   : null)
               ?? templates.FirstOrDefault(t => t.IsDefault)
               ?? templates.FirstOrDefault();
    }

    #endregion

    #region Paper Bridge

    /// <summary>
    /// Re-renders the editable paper from current editor state. Structured changes (customer,
    /// dates, template, totals modes, lines added or removed) call this. Text typed into a line or
    /// the notes must NOT, a reload would interrupt typing.
    /// </summary>
    private void RegeneratePaper()
    {
        if (!IsEditorOpen) return;

        var companyData = App.CompanyManager?.CompanyData;
        var template = SelectedTemplate ?? ResolveTemplate(companyData, null);
        if (companyData == null || template == null)
        {
            PreviewHtml = "<p>Unable to generate preview</p>";
            return;
        }

        var quote = BuildPreviewQuote();
        var renderer = new InvoiceHtmlRenderer();
        // Customer-facing surfaces show the quote in the currency it was priced in, not the user's
        // display currency (Calculations.md §2).
        PreviewHtml = renderer.RenderInvoice(
            quote.ToRenderableInvoice(), template, companyData,
            CurrencyService.GetSymbol(quote.OriginalCurrency),
            editable: !IsEditorPreviewing,
            DocumentLabels.ForQuote(template.HeaderText));
    }

    /// <summary>The quote as it stands in the editor, for rendering only.</summary>
    private Quote BuildPreviewQuote()
    {
        var quote = new Quote
        {
            Id = _editingQuoteId ?? "QUO-PREVIEW",
            QuoteNumber = QuoteNumberDisplay,
            CustomerId = SelectedCustomer?.Id ?? string.Empty,
            IssueDate = IssueDate?.DateTime ?? DateTime.Today,
            ValidUntil = ValidUntil?.DateTime ?? DateTime.Today.AddDays(30),
            TemplateId = SelectedTemplate?.Id ?? string.Empty,
            OriginalCurrency = _editorCurrencyCode
        };

        // Every row, including ones still blank, so the paper keeps the empty line the user is
        // about to type into. BuildLineItems drops the blanks only when saving.
        var lines = LineItems.Select(li => new LineItem
        {
            ProductId = li.ProductId,
            Description = li.Description,
            Quantity = li.Quantity ?? 0,
            UnitPrice = li.UnitPrice ?? 0,
            Discount = li.Discount,
            TaxRate = li.TaxRate
        }).ToList();

        ApplyEditorToQuote(quote, lines);
        return quote;
    }

    /// <summary>
    /// Applies a single edit made directly on the paper back into the editor state. Numbers parse
    /// leniently, because the paper shows formatted, currency-prefixed values.
    /// </summary>
    public void ApplyPaperEdit(string field, int? index, string value)
    {
        switch (field)
        {
            case "notes":
                // The paper falls back to the template's footer when the document has no notes of
                // its own, so a commit handing that same text back is the fallback, not typing.
                // Taking it would make an untouched document look edited and stop it following
                // the template.
                if (value != (SelectedTemplate?.FooterText ?? string.Empty))
                    ModalNotes = value;
                break;
            case "description":
                if (index is int di && di >= 0 && di < LineItems.Count)
                    LineItems[di].Description = value;
                break;
            case "quantity":
                if (index is int qi && qi >= 0 && qi < LineItems.Count && TryParsePaperNumber(value, out var q))
                    LineItems[qi].Quantity = q;
                break;
            case "rate":
                if (index is int ri && ri >= 0 && ri < LineItems.Count && TryParsePaperNumber(value, out var r))
                    LineItems[ri].UnitPrice = r;
                break;
            // An empty box means the placeholder is showing, i.e. zero (not "leave unchanged").
            case "taxValue":
                if (string.IsNullOrWhiteSpace(value)) TaxRate = 0;
                else if (TryParsePaperNumber(value, out var tax)) TaxRate = tax;
                break;
            case "shippingValue":
                if (string.IsNullOrWhiteSpace(value)) ShippingAmount = 0;
                else if (TryParsePaperNumber(value, out var ship)) ShippingAmount = ship;
                break;
            case "discountValue":
                if (string.IsNullOrWhiteSpace(value)) DiscountAmount = 0;
                else if (TryParsePaperNumber(value, out var disc)) DiscountAmount = disc;
                break;
            case "feeValue":
                if (string.IsNullOrWhiteSpace(value)) CustomFeeAmount = 0;
                else if (TryParsePaperNumber(value, out var fee)) CustomFeeAmount = fee;
                break;
        }
    }

    /// <summary>Toggles a totals field between percent and fixed from the paper's swap button.</summary>
    public void ToggleTotalsMode(string which)
    {
        switch (which)
        {
            case "tax": TaxIsFixed = !TaxIsFixed; break;
            case "discount": DiscountIsPercent = !DiscountIsPercent; break;
            case "fee": CustomFeeIsPercent = !CustomFeeIsPercent; break;
        }
    }

    // Pulls a number out of a value that may carry a currency symbol / thousands separators.
    private static bool TryParsePaperNumber(string raw, out decimal result)
    {
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    /// <summary>The product options, as JSON, for the line picker built inside the paper.</summary>
    public string ProductsJson =>
        System.Text.Json.JsonSerializer.Serialize(
            ProductOptions.Select(p => new { id = p.Id, name = p.Name, price = p.UnitPrice }));

    /// <summary>The customer options, as JSON, for the Bill To picker built inside the paper.</summary>
    public string CustomersJson =>
        System.Text.Json.JsonSerializer.Serialize(CustomerOptions.Select(c => new { id = c.Id, name = c.Name }));

    /// <summary>
    /// Config the paper's live totals recompute needs. A quote asks for no money, so there is no
    /// deposit, nothing paid and no processing fee: the portal flags stay off and the amount-to-pay
    /// block the template would otherwise carry is not rendered at all.
    /// </summary>
    public string TotalsConfigJson
    {
        get
        {
            var code = _editorCurrencyCode;
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                symbol = CurrencyService.GetSymbol(code),
                code,
                decimals = Core.Models.Common.CurrencyInfo.GetByCode(code).DecimalPlaces,
                deposit = 0m,
                portal = false,
                passFee = false,
                paid = 0m
            });
        }
    }

    /// <summary>Fills a line from the paper's product picker, then re-renders.</summary>
    public void SelectProductForLine(int index, string productId)
    {
        if (index < 0 || index >= LineItems.Count) return;
        var product = ProductOptions.FirstOrDefault(p => p.Id == productId);
        if (product == null) return;
        LineItems[index].SelectedProduct = product;
        RegeneratePaper();
    }

    /// <summary>Opens the create-product modal from the paper picker's "create new".</summary>
    public void CreateProductForLine(int index)
    {
        if (index < 0 || index >= LineItems.Count) return;
        OpenCreateProduct(LineItems[index]);
    }

    /// <summary>Adds a blank line from the paper's "+ Add line item" and re-renders.</summary>
    public void AddLineFromPaper()
    {
        AddLineItem();
        RegeneratePaper();
    }

    /// <summary>Removes a line from the paper's "x" and re-renders (keeps at least one).</summary>
    public void RemoveLineFromPaper(int index)
    {
        if (index < 0 || index >= LineItems.Count || LineItems.Count <= 1) return;
        LineItems[index].PropertyChanged -= OnLinePropertyChanged;
        LineItems.RemoveAt(index);
        RegeneratePaper();
    }

    /// <summary>Selects a customer from the paper's Bill To picker; re-renders via the handler.</summary>
    public void SelectCustomerFromPaper(string customerId)
    {
        var customer = CustomerOptions.FirstOrDefault(c => c.Id == customerId);
        if (customer != null) SelectedCustomer = customer;
    }

    /// <summary>Opens the create-customer modal from the paper's Bill To picker.</summary>
    public void CreateCustomerFromPaper() => OpenCreateCustomer();

    /// <summary>
    /// Sets a date from the paper's date editor (yyyy-MM-dd). The paper gives the second date slot
    /// to the due date; on a quote that slot is the valid-until date.
    /// </summary>
    public void SetDateFromPaper(string field, string iso)
    {
        if (!DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return;
        var dto = new DateTimeOffset(date);
        if (field == "issueDate") IssueDate = dto;
        else if (field == "dueDate") ValidUntil = dto;
    }

    // The logo the user set on the paper this session. null = untouched; "" = explicitly removed.
    private string? _paperLogo;

    /// <summary>Sets the template's logo (raw base64) from the paper's logo click, re-rendering.</summary>
    public void SetLogoFromPaper(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return;
        _paperLogo = base64;
        ApplyPaperLogo();
        RegeneratePaper();
    }

    /// <summary>Removes the logo when the user clicks the hover "x" on the paper, re-rendering.</summary>
    public void DeleteLogoFromPaper()
    {
        _paperLogo = string.Empty;
        ApplyPaperLogo();
        RegeneratePaper();
    }

    // The logo is a single company-wide choice shared with invoices: apply it to every template so
    // it shows on all of them and survives closing the editor, not just the selected one.
    private void ApplyPaperLogo()
    {
        if (_paperLogo == null) return;
        var remove = _paperLogo.Length == 0;
        var companyData = App.CompanyManager?.CompanyData;
        foreach (var template in TemplateOptions)
        {
            // Anything already sent under the outgoing logo keeps it.
            if (companyData != null)
                LogoHistory.RetireLogo(companyData, template, remove ? null : _paperLogo);

            if (remove)
            {
                template.LogoBase64 = null;
                template.ShowLogo = false;
            }
            else
            {
                template.LogoBase64 = _paperLogo;
                template.LogoWidth = 150;
                template.ShowLogo = true;
            }
        }
        App.CompanyManager?.MarkAsChanged();
    }

    // One-shot handlers for the create-entity flows, detached before the next attempt so a
    // cancelled create doesn't leak onto the singleton create-modal view models.
    private EventHandler? _customerSavedHandler;
    private EventHandler? _productSavedHandler;

    // Hide the document control while a modal is open on top of it; restore when it closes. Only
    // the named open flag is watched: opening a create modal resets its other fields first, and
    // reacting to those would clear this before the modal is even shown.
    private void HideWebViewWhileModalOpen(System.ComponentModel.INotifyPropertyChanged modalVm, string openFlagName, Func<bool> isOpen)
    {
        IsNestedModalOpen = true;
        System.ComponentModel.PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == openFlagName && !isOpen())
            {
                IsNestedModalOpen = false;
                modalVm.PropertyChanged -= handler;
            }
        };
        modalVm.PropertyChanged += handler;
    }

    [RelayCommand]
    private void OpenCreateCustomer()
    {
        var customerModals = App.CustomerModalsViewModel;
        if (customerModals == null) return;

        CreateModalSubscription.RearmOnce(ref _customerSavedHandler,
            h => customerModals.CustomerSaved += h,
            h => customerModals.CustomerSaved -= h,
            () =>
            {
                LoadCustomerOptions();
                OnPropertyChanged(nameof(CustomersJson));

                var newCustomer = CustomerOptions.FirstOrDefault(c => c.Id == customerModals.LastSavedCustomerId);
                if (newCustomer != null)
                    SelectedCustomer = newCustomer;
            });
        HideWebViewWhileModalOpen(customerModals, nameof(customerModals.IsAddModalOpen), () => customerModals.IsAddModalOpen);
        customerModals.OpenAddModal();
    }

    private void OpenCreateProduct(QuoteLineViewModel? line)
    {
        var productModals = App.ProductModalsViewModel;
        if (productModals == null) return;

        CreateModalSubscription.RearmOnce(ref _productSavedHandler,
            h => productModals.ProductSaved += h,
            h => productModals.ProductSaved -= h,
            () =>
            {
                LoadProductOptions();
                OnPropertyChanged(nameof(ProductsJson));

                if (line != null)
                {
                    var newProduct = ProductOptions.FirstOrDefault(p => p.Id == productModals.LastSavedProductId);
                    if (newProduct != null)
                        line.SelectedProduct = newProduct;
                }
                RegeneratePaper();
            });
        HideWebViewWhileModalOpen(productModals, nameof(productModals.IsAddModalOpen), () => productModals.IsAddModalOpen);
        productModals.OpenAddModal();
    }

    #endregion

    #region View Modal Commands

    /// <summary>Opens the read-only quote preview.</summary>
    public void OpenViewModal(QuoteDisplayItem item)
    {
        var companyData = App.CompanyManager?.CompanyData;
        var quote = companyData?.Quotes.FirstOrDefault(q => q.Id == item.Id);
        if (companyData == null || quote == null) return;

        ViewingQuote = item;
        ViewPreviewHtml = RenderQuoteHtml(quote, companyData);
        IsViewModalOpen = true;
    }

    [RelayCommand]
    private void CloseViewModal()
    {
        IsViewModalOpen = false;
        ViewingQuote = null;
        ViewPreviewHtml = null;
    }

    [RelayCommand]
    private async Task OpenViewInBrowserAsync()
    {
        if (string.IsNullOrEmpty(ViewPreviewHtml)) return;
        await InvoicePreviewService.PreviewInBrowserAsync(ViewPreviewHtml, "quote-preview");
    }

    [RelayCommand]
    private async Task OpenEditorPreviewInBrowserAsync()
    {
        if (string.IsNullOrEmpty(PreviewHtml)) return;
        await InvoicePreviewService.PreviewInBrowserAsync(PreviewHtml, "quote-preview");
    }

    /// <summary>
    /// The quote as the customer sees it. Customer-facing surfaces use the currency the quote was
    /// priced in, not the user's display currency (Calculations.md §2).
    /// </summary>
    private static string? RenderQuoteHtml(Quote quote, CompanyData companyData)
    {
        var template = ResolveTemplate(companyData, quote.TemplateId);
        if (template == null) return null;

        var renderer = new InvoiceHtmlRenderer();
        return renderer.RenderQuote(quote, template, companyData, CurrencyService.GetSymbol(quote.OriginalCurrency));
    }

    #endregion

    #region Sending

    /// <summary>
    /// Saves the quote and publishes it, straight from the editor's send step. There is no
    /// separate send screen: what the user is looking at is what goes out.
    /// </summary>
    [RelayCommand]
    private async Task SendQuoteAsync()
    {
        SendError = null;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var recipient = SendRecipientEmail?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(recipient) || !recipient.Contains('@'))
        {
            SendError = "Please enter a valid recipient email address.".Translate();
            return;
        }

        if ((SendMessage?.Length ?? 0) > MaxPersonalMessageLength)
        {
            SendError = "Your message is longer than {0} characters.".TranslateFormat(MaxPersonalMessageLength);
            return;
        }

        // The server writes and sends the email and hosts the page the customer answers on, so
        // there is no offline path.
        var portalService = PortalSettings.IsConfigured ? App.PaymentPortalService : null;
        if (portalService == null)
        {
            SendError = "Connect the payment portal in Settings to send quotes.".Translate();
            return;
        }

        // Check the paper before asking, so the dialog is never followed by a complaint.
        if (!ValidateForSend()) return;

        // Confirm before anything is written: saying no to a brand new quote must leave no row
        // behind on the list. The figures come from the editor, which is what the user is
        // looking at and what is about to be saved.
        //
        // Sending over an answer the customer already gave is destructive, and the server only
        // does it when the request says so. This confirmation is what says so.
        var existing = IsEditMode && !string.IsNullOrEmpty(_editingQuoteId)
            ? companyData.Quotes.FirstOrDefault(q => q.Id == _editingQuoteId)
            : null;
        var currentStatus = existing?.Status ?? QuoteStatus.Draft;
        var isRevision = currentStatus is QuoteStatus.Accepted or QuoteStatus.Declined;
        if (!await ConfirmSendAsync(
                SelectedCustomer?.Name ?? string.Empty, recipient,
                BuildPreviewQuote().Total, isRevision, currentStatus))
            return;

        // Save what is on the paper, so the stored quote and the emailed one can never disagree.
        var savedId = TrySaveQuote();
        if (savedId == null) return;

        QuoteSaved?.Invoke(this, EventArgs.Empty);

        var quote = companyData.Quotes.FirstOrDefault(q => q.Id == savedId);
        if (quote == null) return;

        // Adopt the saved quote, so trying again after a failed send updates it instead of
        // writing a second one.
        _editingQuoteId = savedId;
        IsEditMode = true;
        _original = Capture();

        // Save the address the user actually typed, so the next send starts there.
        var customer = companyData.GetCustomer(quote.CustomerId);
        if (customer != null && !string.Equals(customer.Email.Trim(), recipient, StringComparison.OrdinalIgnoreCase))
        {
            customer.Email = recipient;
            customer.UpdatedAt = DateTime.UtcNow;
            companyData.MarkAsModified();
        }

        _sendCts?.Dispose();
        _sendCts = new CancellationTokenSource();
        var ct = _sendCts.Token;

        IsSending = true;
        try
        {
            var template = ResolveTemplate(companyData, quote.TemplateId);
            var response = await portalService.PublishQuoteAsync(
                quote, companyData, template,
                CurrencyService.GetSymbol(quote.OriginalCurrency),
                sendEmail: true,
                message: string.IsNullOrWhiteSpace(SendMessage) ? null : SendMessage.Trim(),
                status: "sent",
                revision: isRevision,
                cancellationToken: ct);

            // The request went out and the reply did not come back. Record it as published before
            // anything else returns: the customer may be holding this quote, and only a quote the
            // app believes is out there gets its link cancelled when it is deleted.
            if (!response.Success && response.MayHavePublished)
            {
                MarkMaybePublished(quote, companyData, recipient);
                QuoteSaved?.Invoke(this, EventArgs.Empty);
            }

            // The user cancelled mid-flight. A server that already accepted the send cannot be
            // un-sent, so only the failure path is swallowed.
            if (!response.Success && ct.IsCancellationRequested) return;

            if (!response.Success)
            {
                SendError = SendFailureMessage(response);
                return;
            }

            if (IsForeignAnswer(quote, response))
            {
                SendError = ForeignAnswerMessage(quote);
                return;
            }

            var wasSent = ApplySendResult(quote, companyData, recipient, response);
            if (wasSent)
                _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.QuoteSent);

            QuoteSaved?.Invoke(this, EventArgs.Empty);
            IsSendSuccess = true;
        }
        catch (ServerRateLimitedException ex)
        {
            // The server's own wording says how long the wait is; a generic failure doesn't.
            SendError = ex.Message;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Quote.Send");
            SendError = "Failed to send: {0}".TranslateFormat(ex.Message);
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>
    /// Sends a quote the customer already has again, without opening the editor. The paper is
    /// unchanged and goes to the address already on the customer, so there is nothing to fill in
    /// first: a confirmation is the whole flow, the same as resending an invoice.
    /// </summary>
    public async Task ResendQuoteAsync(QuoteDisplayItem item)
    {
        var companyData = App.CompanyManager?.CompanyData;
        var quote = companyData?.Quotes.FirstOrDefault(q => q.Id == item.Id);
        if (companyData == null || quote == null) return;

        var portalService = PortalSettings.IsConfigured ? App.PaymentPortalService : null;
        if (portalService == null)
        {
            await App.ShowInfoMessageBoxAsync(
                "Payment portal not connected".Translate(),
                "Connect the payment portal in Settings to send quotes.".Translate());
            return;
        }

        var customer = companyData.GetCustomer(quote.CustomerId);
        var recipient = customer?.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(recipient))
        {
            await App.ShowErrorMessageBoxAsync(
                "No email address".Translate(),
                "This quote's customer has no email address, so there is nowhere to send it.".Translate());
            return;
        }

        // Sending over an answer the customer already gave is destructive, and the server only
        // does it when the request says so. The confirmation below is what says so.
        var isRevision = quote.Status is QuoteStatus.Accepted or QuoteStatus.Declined;
        if (!await ConfirmSendAsync(customer?.Name ?? item.CustomerName, recipient, quote.Total, isRevision, quote.Status))
            return;

        App.ShowBusyOverlay("Sending quote...".Translate());
        try
        {
            var template = ResolveTemplate(companyData, quote.TemplateId);
            var response = await portalService.PublishQuoteAsync(
                quote, companyData, template,
                CurrencyService.GetSymbol(quote.OriginalCurrency),
                sendEmail: true,
                message: null,
                status: "sent",
                revision: isRevision);

            if (!response.Success)
            {
                if (response.MayHavePublished)
                {
                    MarkMaybePublished(quote, companyData, recipient);
                    QuoteSaved?.Invoke(this, EventArgs.Empty);
                }

                await App.ShowErrorMessageBoxAsync(
                    "Failed to resend quote".Translate(), SendFailureMessage(response));
                return;
            }

            if (IsForeignAnswer(quote, response))
            {
                await App.ShowErrorMessageBoxAsync(
                    "Failed to resend quote".Translate(), ForeignAnswerMessage(quote));
                return;
            }

            if (ApplySendResult(quote, companyData, recipient, response))
                _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.QuoteSent);

            QuoteSaved?.Invoke(this, EventArgs.Empty);

            // ApplySendResult writes the wording, so a resend and a send from the editor say the
            // same thing, including when the server reports an answer instead of a send.
            await App.ShowInfoMessageBoxAsync(SendSuccessTitle, SendSuccessDetail);
        }
        catch (ServerRateLimitedException ex)
        {
            // The server's own wording says how long the wait is; a generic failure doesn't.
            await App.ShowErrorMessageBoxAsync("Failed to resend quote".Translate(), ex.Message);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Quote.Resend");
            await App.ShowErrorMessageBoxAsync("Failed to resend quote".Translate(), ex.Message);
        }
        finally
        {
            App.HideBusyOverlay();
        }
    }

    /// <summary>
    /// True when the portal answered with an accept or decline for a quote this app has never
    /// sent. Nobody can have answered a document that was never delivered, so the portal is
    /// holding an older quote that used this same number, and applying its answer would mark a
    /// brand new quote accepted with a stranger's note on it.
    /// </summary>
    private static bool IsForeignAnswer(Quote quote, PortalQuotePublishResponse response) =>
        !quote.SentAt.HasValue && PaymentPortalService.ParseQuoteAnswer(response.Status) != null;

    private static string ForeignAnswerMessage(Quote quote) =>
        ("Quote {0} is already on the portal with an answer on it, so nothing was emailed. That "
         + "answer belongs to an earlier quote that used this number.").TranslateFormat(quote.QuoteNumber);

    /// <summary>
    /// The wording for a send that did not come back with a success. When the request may have
    /// reached the server, the user has to know before they try again, or the customer gets the
    /// same quote twice.
    /// </summary>
    private static string SendFailureMessage(PortalQuotePublishResponse response)
    {
        var detail = string.IsNullOrEmpty(response.Message)
            ? "The payment portal rejected the request.".Translate()
            : response.Message;

        return response.MayHavePublished
            ? detail + "\n\n" + "It may still have reached your customer. Check with them before sending it again.".Translate()
            : detail;
    }

    /// <summary>
    /// Records that a customer-facing link probably exists, for a send whose reply never arrived.
    /// Without this the quote looks unsent: deleting it would leave the link live, and sending it
    /// again would read as a first send rather than a second copy.
    /// </summary>
    private static void MarkMaybePublished(Quote quote, CompanyData companyData, string recipient)
    {
        if (quote.SentAt.HasValue) return;

        quote.SentAt = DateTime.UtcNow;
        quote.UpdatedAt = DateTime.UtcNow;
        quote.History.Add(new InvoiceHistoryEntry
        {
            Action = "Send unconfirmed",
            Details = $"The portal did not answer. The quote may have reached {recipient}.",
            Timestamp = DateTime.UtcNow
        });
        companyData.MarkAsModified();
    }

    /// <summary>Aborts an in-flight send from the sending card.</summary>
    [RelayCommand]
    private void CancelSend() => _sendCts?.Cancel();

    [RelayCommand]
    private void DismissSendError() => SendError = null;

    /// <summary>
    /// Confirms an irreversible send, naming who it goes to and for how much. On a quote the
    /// customer has already answered it carries the revision warning too, because agreeing here is
    /// what lets the server clear their answer. One dialog either way: a second one in a row is the
    /// extra step this flow just got rid of.
    /// </summary>
    /// <remarks>
    /// Sets <see cref="IsNestedModalOpen"/> so the native paper, which draws above Avalonia
    /// content, steps aside instead of covering the dialog.
    /// </remarks>
    private async Task<bool> ConfirmSendAsync(
        string customerName, string recipientEmail, decimal total, bool isRevision, QuoteStatus current)
    {
        var dialog = App.ConfirmationDialog;
        if (dialog == null) return false;

        IsNestedModalOpen = true;
        try
        {
            var amount = CurrencyService.Format(total, includeCode: true);
            var who = string.IsNullOrWhiteSpace(customerName)
                ? recipientEmail
                : $"{customerName} ({recipientEmail})";

            var message = "This quote for {0} will be emailed to {1}.".TranslateFormat(amount, who);
            if (isRevision)
            {
                message += "\n\n" + (current == QuoteStatus.Accepted
                        ? "They already accepted it. Sending a revised quote clears their answer and asks them again."
                        : "They already declined it. Sending a revised quote clears their answer and asks them again.")
                    .Translate();
            }

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = (isRevision ? "Send a revised quote?" : "Send quote?").Translate(),
                Message = message,
                PrimaryButtonText = (isRevision ? "Send revision" : "Send quote").Translate(),
                CancelButtonText = "Cancel".Translate()
            });

            return result == ConfirmationResult.Primary;
        }
        finally
        {
            IsNestedModalOpen = false;
        }
    }

    /// <summary>
    /// Records the outcome of a publish. The status the server returns is the truth: when the
    /// customer answered between the last sync and this send, the server keeps that answer and
    /// emails nobody, so the quote must show the answer rather than a send that never happened.
    /// </summary>
    /// <returns>True when the quote actually went out.</returns>
    private bool ApplySendResult(Quote quote, CompanyData companyData, string recipient, PortalQuotePublishResponse response)
    {
        var oldStatus = quote.Status;
        var oldSentAt = quote.SentAt;
        var oldRespondedAt = quote.RespondedAt;
        var oldResponseNote = quote.ResponseNote;

        var answered = PaymentPortalService.ApplyQuoteAnswer(
            quote, response.Status, response.RespondedAt, response.ResponseNote);

        // The server kept an existing answer: nothing was emailed, so SentAt stays where it was
        // and the history entry describes the answer, not a send.
        if (answered)
        {
            SendSuccessTitle = quote.Status == QuoteStatus.Accepted
                ? "Already accepted"
                : "Already declined";

            // When they answered matters most here: without it this reads as an answer given in
            // the moment the send went out.
            var verb = quote.Status == QuoteStatus.Accepted
                ? "Nothing was emailed. Your customer accepted this quote {0}."
                : "Nothing was emailed. Your customer declined this quote {0}.";
            var when = quote.RespondedAt.HasValue
                ? "on {0}".TranslateFormat(quote.RespondedAt.Value.ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt"))
                : "before this send".Translate();

            SendSuccessDetail = verb.TranslateFormat(when);
            if (!string.IsNullOrWhiteSpace(quote.ResponseNote))
                SendSuccessDetail += "\n\n" + "They said: {0}".TranslateFormat(quote.ResponseNote);
        }
        else
        {
            quote.SentAt = DateTime.UtcNow;
            quote.Status = QuoteStatus.Sent;
            quote.RespondedAt = null;
            quote.ResponseNote = null;
            quote.UpdatedAt = DateTime.UtcNow;
            quote.History.Add(new InvoiceHistoryEntry
            {
                Action = oldStatus == QuoteStatus.Draft ? "Sent" : "Resent",
                Details = $"Quote sent to {recipient}",
                Timestamp = DateTime.UtcNow
            });
            SendSuccessTitle = "Quote sent.";
            SendSuccessDetail = "You will see their answer here once they accept or decline.";
        }
        companyData.MarkAsModified();

        var sentQuote = quote;
        var newStatus = quote.Status;
        var newSentAt = quote.SentAt;
        var newRespondedAt = quote.RespondedAt;
        var newResponseNote = quote.ResponseNote;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Send quote '{quote.QuoteNumber}'",
            () =>
            {
                sentQuote.Status = oldStatus;
                sentQuote.SentAt = oldSentAt;
                sentQuote.RespondedAt = oldRespondedAt;
                sentQuote.ResponseNote = oldResponseNote;
                companyData.MarkAsModified();
                QuoteSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                sentQuote.Status = newStatus;
                sentQuote.SentAt = newSentAt;
                sentQuote.RespondedAt = newRespondedAt;
                sentQuote.ResponseNote = newResponseNote;
                companyData.MarkAsModified();
                QuoteSaved?.Invoke(this, EventArgs.Empty);
            }));

        return !answered;
    }

    #endregion

    #region Filter Modal

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private DateTimeOffset? _filterStartDate;

    [ObservableProperty]
    private DateTimeOffset? _filterEndDate;

    [ObservableProperty]
    private string _filterCustomer = "All";

    [ObservableProperty]
    private string _filterStatus = "All";

    /// <summary>Customer options for the filter dropdown.</summary>
    public ObservableCollection<string> FilterCustomerOptions { get; } = ["All"];

    /// <summary>Status options for the filter dropdown.</summary>
    public ObservableCollection<string> FilterStatusOptions { get; } =
        new(QuoteStatusExtensions.GetFilterOptions());

    private sealed record FilterValues(DateTimeOffset? StartDate, DateTimeOffset? EndDate, string Customer, string Status)
    {
        public static readonly FilterValues Default = new(null, null, "All", "All");
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterStartDate, FilterEndDate, FilterCustomer, FilterStatus),
        v =>
        {
            FilterStartDate = v.StartDate;
            FilterEndDate = v.EndDate;
            FilterCustomer = v.Customer;
            FilterStatus = v.Status;
        });

    public void OpenFilterModal()
    {
        LoadFilterCustomerOptions();
        Filters.Capture();
        IsFilterModalOpen = true;
    }

    private void CloseFilterModal() => IsFilterModalOpen = false;

    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (await Filters.ConfirmDiscardAsync(ConfirmDiscardFiltersAsync))
            CloseFilterModal();
    }

    /// <summary>How many filters are applied, for the page's Filter button.</summary>
    public int ActiveFilterCount { get; private set; }

    [RelayCommand]
    private void ApplyFilters()
    {
        ActiveFilterCount = Filters.ActiveCount;
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Filters.Reset();
        ActiveFilterCount = 0;
        FiltersCleared?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    private void LoadFilterCustomerOptions()
    {
        OptionLoader.Fill(FilterCustomerOptions,
            OptionLoader.Customers(App.CompanyManager?.CompanyData).Select(c => c.Name), "All");
    }

    #endregion
}

/// <summary>
/// A line on the quote paper. Quantity and price are nullable so a line the user has not touched
/// renders as an empty box rather than a "0" they have to clear.
/// </summary>
public partial class QuoteLineViewModel : ObservableObject
{
    /// <summary>The product this line came from, null when the line is typed freehand.</summary>
    [ObservableProperty]
    private string? _productId;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private decimal? _quantity = 1m;

    [ObservableProperty]
    private decimal? _unitPrice;

    [ObservableProperty]
    private decimal _discount;

    [ObservableProperty]
    private decimal _taxRate;

    /// <summary>
    /// Set from the paper's product picker, which fills the description and price. Write-only in
    /// practice: the getter is null so re-rendering the paper never re-triggers the fill.
    /// </summary>
    public ProductOption? SelectedProduct
    {
        get => null;
        set
        {
            if (value == null) return;
            ProductId = value.Id;
            Description = value.Name;
            UnitPrice = value.UnitPrice;
            if ((Quantity ?? 0) <= 0) Quantity = 1m;
            OnPropertyChanged();
        }
    }

    // A line typed over after a product was picked is no longer that product, so it stops
    // claiming to be one and carries nothing into the converted invoice.
    partial void OnDescriptionChanged(string value)
    {
        if (!string.IsNullOrEmpty(ProductId) && !string.IsNullOrWhiteSpace(value))
        {
            var product = App.QuotesModalsViewModel?.ProductOptions.FirstOrDefault(p => p.Id == ProductId);
            if (product != null && product.Name != value) ProductId = null;
        }
    }

    /// <summary>Calculated line total, the same formula the paper and the totals use.</summary>
    public decimal Total => LineItem.SubtotalOf(Quantity ?? 0m, UnitPrice ?? 0m, Discount);
}
