using ArgoBooks.Localization;
using ArgoBooks.Services;
using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for payment modals, shared between PaymentsPage and AppShell.
/// </summary>
public partial class PaymentModalsViewModel : ViewModelBase
{
    #region Modal State

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private bool _isAddModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private bool _isEditModalOpen;

    /// <summary>Add and edit share one form, open while either flag is set.</summary>
    public bool IsFormOpen => IsAddModalOpen || IsEditModalOpen;

    // Set when the form opens and kept on close, so the title doesn't change while it closes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle), nameof(FormSaveText))]
    private bool _isEditMode;

    public string FormTitle => IsEditMode ? "Edit Payment".Translate() : "Record Payment".Translate();
    public string FormSaveText => IsEditMode ? "Save Changes".Translate() : "Record Payment".Translate();

    partial void OnIsAddModalOpenChanged(bool value)
    {
        if (value) IsEditMode = false;
    }

    partial void OnIsEditModalOpenChanged(bool value)
    {
        if (value) IsEditMode = true;
    }

    [RelayCommand]
    private Task RequestCloseFormAsync() => IsEditMode ? RequestCloseEditModalAsync() : RequestCloseAddModalAsync();

    [RelayCommand]
    private Task SaveFormAsync() => IsEditMode ? SaveEditedPayment() : SaveNewPayment();

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    #endregion

    #region Modal Form Fields

    [ObservableProperty]
    private string? _modalInvoiceId;

    [ObservableProperty]
    private string? _modalCustomerId;

    [ObservableProperty]
    private string _modalAmount = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _modalDate = DateTimeOffset.Now;

    [ObservableProperty]
    private string _modalPaymentMethod = "Cash";

    [ObservableProperty]
    private string _modalReferenceNumber = string.Empty;

    [ObservableProperty]
    private string _modalNotes = string.Empty;

    /// <summary>
    /// Whether the payment being edited is a portal (online) payment.
    /// When true, all fields except Notes are read-only.
    /// </summary>
    [ObservableProperty]
    private bool _isPortalPayment;

    [ObservableProperty]
    private string? _modalInvoiceError;

    [ObservableProperty]
    private string? _modalAmountError;

    /// <summary>
    /// The payment being edited (null for add).
    /// </summary>
    private Payment? _editingPayment;

    private sealed record EditState(string? InvoiceId, string Amount, string PaymentMethod, string ReferenceNumber, string Notes);

    // The form as the edit modal opened, for change detection.
    private EditState? _original;

    private EditState Capture() => new(ModalInvoiceId, ModalAmount, ModalPaymentMethod, ModalReferenceNumber, ModalNotes);

    /// <summary>
    /// Returns true if any data has been entered in the Add modal.
    /// </summary>
    public bool HasAddModalEnteredData =>
        !string.IsNullOrEmpty(ModalInvoiceId) ||
        !string.IsNullOrWhiteSpace(ModalAmount) ||
        !string.IsNullOrWhiteSpace(ModalReferenceNumber) ||
        !string.IsNullOrWhiteSpace(ModalNotes);

    /// <summary>
    /// Returns true if any changes have been made in the Edit modal.
    /// </summary>
    public bool HasEditModalChanges => Capture() != _original;

    #endregion

    #region Dropdown Options

    /// <summary>
    /// Payment method options for add/edit modal.
    /// </summary>
    public ObservableCollection<string> ModalPaymentMethodOptions { get; } = new(PaymentMethodExtensions.GetCommonOptions());

    /// <summary>
    /// Invoice options for add/edit modal (populated from company data).
    /// </summary>
    public ObservableCollection<InvoiceOption> InvoiceOptions { get; } = [];

    /// <summary>
    /// Selected invoice option for modal.
    /// </summary>
    [ObservableProperty]
    private InvoiceOption? _selectedInvoice;

    partial void OnSelectedInvoiceChanged(InvoiceOption? value)
    {
        ModalInvoiceId = value?.Id;
        // Clear errors when user selects an invoice
        ModalInvoiceError = null;
        // Auto-fill customer when invoice is selected
        if (value?.Id != null)
        {
            var companyData = App.CompanyManager?.CompanyData;
            var invoice = companyData?.GetInvoice(value.Id);
            if (invoice != null)
            {
                ModalCustomerId = invoice.CustomerId;
                // Auto-fill amount with remaining due if empty
                if (string.IsNullOrEmpty(ModalAmount) || ModalAmount == "0")
                {
                    var totalPaid = companyData?.Payments
                        .Where(p => p.InvoiceId == invoice.Id && p.Amount > 0)
                        .Sum(p => p.Amount) ?? 0;
                    var remaining = invoice.Total - totalPaid;
                    if (remaining > 0)
                    {
                        ModalAmount = remaining.ToString("F2");
                    }
                }
            }
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Fired when a payment is saved (added or edited).
    /// </summary>
    public event EventHandler? PaymentSaved;

    /// <summary>
    /// Fired when a payment is deleted.
    /// </summary>
    public event EventHandler? PaymentDeleted;

    #endregion

    #region Add Payment

    [RelayCommand]
    public void OpenAddModal()
    {
        _editingPayment = null;
        ClearModalFields();
        LoadInvoiceOptions();
        IsAddModalOpen = true;
    }

    /// <summary>
    /// Opens the add-payment modal with an invoice already chosen, for recording a
    /// payment from the invoices page.
    /// </summary>
    /// <remarks>
    /// Selecting the option (rather than setting ModalInvoiceId) runs
    /// OnSelectedInvoiceChanged, which fills in the customer and the remaining
    /// balance, so this behaves exactly as if the user had picked it by hand.
    /// </remarks>
    public void OpenAddModalForInvoice(string invoiceId)
    {
        OpenAddModal();
        SelectedInvoice = InvoiceOptions.FirstOrDefault(i => i.Id == invoiceId);
    }

    [RelayCommand]
    public void CloseAddModal()
    {
        IsAddModalOpen = false;
        ClearModalFields();
    }

    /// <summary>
    /// Requests to close the Add modal, showing confirmation if data was entered.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseAddModalAsync()
    {
        if (HasAddModalEnteredData)
        {
            if (!await ConfirmDiscardNewAsync())
                return;
        }

        CloseAddModal();
    }

    /// <summary>
    /// Opens the create invoice modal on top of the current modal.
    /// </summary>
    // One-shot handler for the "create entity from this modal" flow. Stored so a cancelled create
    // (which never raises the *Saved event) can be detached before the next attempt, instead of
    // leaking onto the singleton create-modal VMs. See CreateModalSubscription.
    private EventHandler? _invoiceSavedHandler;

    [RelayCommand]
    private void OpenCreateInvoice()
    {
        var invoiceModals = App.InvoiceModalsViewModel;
        if (invoiceModals == null) return;

        CreateModalSubscription.RearmOnce(ref _invoiceSavedHandler,
            h => invoiceModals.InvoiceSaved += h,
            h => invoiceModals.InvoiceSaved -= h,
            () =>
            {
                LoadInvoiceOptions();

                // Auto-select the invoice the user just created.
                var newInvoice = InvoiceOptions.FirstOrDefault(i => i.Id == invoiceModals.LastSavedInvoiceId);
                if (newInvoice != null)
                    SelectedInvoice = newInvoice;
            });
        invoiceModals.OpenCreateModal();
    }

    /// <summary>
    /// The currency a payment must be tagged with. A payment settles the invoice (or revenue) it is
    /// linked to, in THAT record's currency: <see cref="InvoiceTotalsService"/> only counts a payment
    /// toward an invoice when their currencies match (docs/Calculations.md §5), so stamping the
    /// company's display currency instead silently drops payments on any invoice not in that currency.
    /// Unlinked payments fall back to the company display currency.
    /// </summary>
    private string ResolvePaymentCurrency(Core.Data.CompanyData companyData)
    {
        if (!string.IsNullOrEmpty(ModalInvoiceId))
        {
            var invoice = companyData.Invoices.FirstOrDefault(i => i.Id == ModalInvoiceId);
            if (invoice != null && !string.IsNullOrEmpty(invoice.OriginalCurrency))
                return invoice.OriginalCurrency;
        }
        return CurrencyService.CurrentCurrencyCode;
    }

    /// <summary>
    /// The payment's USD figure at its own date's rate, or pending when there is none: future
    /// dated, offline, or no rate service (docs/Calculations.md Rule 3a).
    /// </summary>
    private static async Task<(decimal AmountUSD, bool IsPending)> ConvertToUsdAsync(decimal amount, string currency, DateTime date)
    {
        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
            return (amount, false);

        var rate = ExchangeRateService.Instance is { } exchangeService
            ? await exchangeService.GetExchangeRateAsync(currency, "USD", date)
            : 0m;

        // USD base stored full-precision (no 2dp round); display rounds. See Calculations.md Rule 3.
        return rate > 0 ? (amount * rate, false) : (0m, true);
    }

    /// <summary>
    /// Keeps the payment's conversion queue entry in step with it: there while it waits for a
    /// rate, gone once it doesn't. The service drops an entry whose payment is missing, so undo
    /// and redo have to move the entry with the payment or a redone payment waits forever.
    /// </summary>
    private static void QueueConversion(Core.Data.CompanyData companyData, Payment payment)
    {
        companyData.PendingConversions.RemoveAll(p => p.TransactionId == payment.Id);
        if (payment.IsPendingConversion)
        {
            companyData.PendingConversions.Add(new PendingConversion
            {
                TransactionId = payment.Id,
                TransactionType = "Payment",
                OriginalCurrency = payment.OriginalCurrency,
                TransactionDate = payment.Date,
                Total = payment.Amount
            });
        }

        _ = PendingConversionService.Instance?.MirrorAsync(companyData, [payment.Id]);
    }

    private static void ForgetConversion(Core.Data.CompanyData companyData, Payment payment)
    {
        companyData.PendingConversions.RemoveAll(p => p.TransactionId == payment.Id);
        _ = PendingConversionService.Instance?.MirrorAsync(companyData, [payment.Id]);
    }

    [RelayCommand]
    public async Task SaveNewPayment()
    {
        if (!ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        companyData.IdCounters.Payment++;
        var newId = $"PAY-{companyData.IdCounters.Payment:D3}";

        var paymentMethod = PaymentMethodExtensions.ParseDisplayName(ModalPaymentMethod);

        var parsedAmount = decimal.Parse(ModalAmount);
        // Tag the payment with the linked invoice's/revenue's currency, not the company display
        // currency, so it counts toward that invoice's totals (docs/Calculations.md §5).
        var currentCurrency = ResolvePaymentCurrency(companyData);
        var paymentDate = ModalDate?.DateTime ?? DateTime.Today;

        var (amountUSD, isPending) = await ConvertToUsdAsync(parsedAmount, currentCurrency, paymentDate);

        var newPayment = new Payment
        {
            Id = newId,
            InvoiceId = ModalInvoiceId ?? string.Empty,
            CustomerId = ModalCustomerId ?? string.Empty,
            Date = paymentDate,
            Amount = parsedAmount,
            PaymentMethod = paymentMethod,
            ReferenceNumber = string.IsNullOrWhiteSpace(ModalReferenceNumber) ? null : ModalReferenceNumber.Trim(),
            Notes = ModalNotes.Trim(),
            CreatedAt = DateTime.UtcNow,
            OriginalCurrency = currentCurrency,
            AmountUSD = amountUSD,
            IsPendingConversion = isPending
        };

        companyData.Payments.Add(newPayment);
        QueueConversion(companyData, newPayment);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.PaymentRecorded);
        // Recalc the affected invoice's totals + status. Without this the
        // invoice's AmountPaid / Balance / Status drift out of sync with
        // the Payment list. See docs/Calculations.md §5.
        RecalcInvoiceTotals(companyData, newPayment.InvoiceId);
        companyData.MarkAsModified();

        var paymentToUndo = newPayment;
        var invoiceIdForRecalc = newPayment.InvoiceId;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Record payment '{newPayment.Id}'",
            () =>
            {
                companyData.Payments.Remove(paymentToUndo);
                ForgetConversion(companyData, paymentToUndo);
                RecalcInvoiceTotals(companyData, invoiceIdForRecalc);
                companyData.MarkAsModified();
                PaymentSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                companyData.Payments.Add(paymentToUndo);
                QueueConversion(companyData, paymentToUndo);
                RecalcInvoiceTotals(companyData, invoiceIdForRecalc);
                companyData.MarkAsModified();
                PaymentSaved?.Invoke(this, EventArgs.Empty);
            }));

        PaymentSaved?.Invoke(this, EventArgs.Empty);
        CloseAddModal();
    }

    #endregion

    #region Edit Payment

    public void OpenEditModal(PaymentDisplayItem? item)
    {
        if (item == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        var payment = companyData?.Payments.FirstOrDefault(p => p.Id == item.Id);
        if (payment == null)
            return;

        _editingPayment = payment;
        LoadInvoiceOptions();

        ModalInvoiceId = payment.InvoiceId;
        SelectedInvoice = InvoiceOptions.FirstOrDefault(i => i.Id == payment.InvoiceId);
        ModalCustomerId = payment.CustomerId;
        ModalAmount = payment.Amount.ToString("F2");
        ModalDate = new DateTimeOffset(payment.Date);
        IsPortalPayment = payment.Source == PaymentSource.Online;
        ModalPaymentMethod = payment.PaymentMethod.GetDisplayName();
        ModalReferenceNumber = payment.ReferenceNumber ?? string.Empty;
        ModalNotes = payment.Notes;

        _original = Capture();

        ClearModalErrors();
        IsEditModalOpen = true;
    }

    [RelayCommand]
    public void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingPayment = null;
        ClearModalFields();
    }

    /// <summary>
    /// Requests to close the Edit modal, showing confirmation if changes were made.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseEditModalAsync()
    {
        if (HasEditModalChanges)
        {
            if (!await ConfirmDiscardEditsAsync())
                return;
        }

        CloseEditModal();
    }

    [RelayCommand]
    public async Task SaveEditedPayment()
    {
        if (_editingPayment == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // For portal payments, only notes can be edited
        if (IsPortalPayment)
        {
            var oldNotes = _editingPayment.Notes;
            var newNotes = ModalNotes.Trim();

            if (oldNotes == newNotes)
            {
                CloseEditModal();
                return;
            }

            var paymentToEdit = _editingPayment;
            var changes = new Dictionary<string, FieldChange>
            {
                ["Notes"] = new FieldChange { OldValue = oldNotes, NewValue = newNotes }
            };
            App.EventLogService?.SetPendingChanges(changes);
            paymentToEdit.Notes = newNotes;

            companyData.MarkAsModified();

            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Edit payment '{paymentToEdit.Id}'",
                () =>
                {
                    paymentToEdit.Notes = oldNotes;
                    companyData.MarkAsModified();
                    PaymentSaved?.Invoke(this, EventArgs.Empty);
                },
                () =>
                {
                    paymentToEdit.Notes = newNotes;
                    companyData.MarkAsModified();
                    PaymentSaved?.Invoke(this, EventArgs.Empty);
                }));

            PaymentSaved?.Invoke(this, EventArgs.Empty);
            CloseEditModal();
            return;
        }

        if (!ValidateModal())
            return;

        var oldInvoiceId = _editingPayment.InvoiceId;
        var oldCustomerId = _editingPayment.CustomerId;
        var oldDate = _editingPayment.Date;
        var oldAmount = _editingPayment.Amount;
        var oldPaymentMethod = _editingPayment.PaymentMethod;
        var oldReferenceNumber = _editingPayment.ReferenceNumber;
        var oldNotesVal = _editingPayment.Notes;

        var newInvoiceId = ModalInvoiceId ?? string.Empty;
        var newCustomerId = ModalCustomerId ?? string.Empty;
        var newDate = ModalDate?.DateTime ?? DateTime.Today;
        var newAmount = decimal.Parse(ModalAmount);

        // Convert to USD for consistent reporting. Tag the payment with the linked invoice's/revenue's
        // currency, not the company display currency, so it counts toward that invoice's totals (§5).
        var editCurrentCurrency = ResolvePaymentCurrency(companyData);
        var (newAmountUSD, newIsPending) = await ConvertToUsdAsync(newAmount, editCurrentCurrency, newDate);

        var newPaymentMethod = PaymentMethodExtensions.ParseDisplayName(ModalPaymentMethod);
        var newReferenceNumber = string.IsNullOrWhiteSpace(ModalReferenceNumber) ? null : ModalReferenceNumber.Trim();
        var newNotesVal = ModalNotes.Trim();

        // Check if anything actually changed
        var hasChanges = oldInvoiceId != newInvoiceId ||
                         oldCustomerId != newCustomerId ||
                         oldDate != newDate ||
                         oldAmount != newAmount ||
                         oldPaymentMethod != newPaymentMethod ||
                         oldReferenceNumber != newReferenceNumber ||
                         oldNotesVal != newNotesVal;

        // If nothing changed, just close the modal without recording an action
        if (!hasChanges)
        {
            CloseEditModal();
            return;
        }

        var paymentToEdit2 = _editingPayment;
        var changes2 = new Dictionary<string, FieldChange>();
        if (oldDate != newDate) changes2["Date"] = new FieldChange { OldValue = oldDate.ToString("d"), NewValue = newDate.ToString("d") };
        if (oldAmount != newAmount) changes2["Amount"] = new FieldChange { OldValue = oldAmount.ToString("F2"), NewValue = newAmount.ToString("F2") };
        if (oldPaymentMethod != newPaymentMethod) changes2["Payment Method"] = new FieldChange { OldValue = oldPaymentMethod.ToString(), NewValue = newPaymentMethod.ToString() };
        if (oldReferenceNumber != newReferenceNumber) changes2["Reference"] = new FieldChange { OldValue = oldReferenceNumber ?? "", NewValue = newReferenceNumber ?? "" };
        if (oldNotesVal != newNotesVal) changes2["Notes"] = new FieldChange { OldValue = oldNotesVal, NewValue = newNotesVal };
        if (changes2.Count > 0) App.EventLogService?.SetPendingChanges(changes2);
        var oldOriginalCurrency = paymentToEdit2.OriginalCurrency;
        var oldAmountUSD = paymentToEdit2.AmountUSD;
        var oldIsPending = paymentToEdit2.IsPendingConversion;

        paymentToEdit2.InvoiceId = newInvoiceId;
        paymentToEdit2.CustomerId = newCustomerId;
        paymentToEdit2.Date = newDate;
        paymentToEdit2.Amount = newAmount;
        paymentToEdit2.PaymentMethod = newPaymentMethod;
        paymentToEdit2.ReferenceNumber = newReferenceNumber;
        paymentToEdit2.Notes = newNotesVal;
        paymentToEdit2.OriginalCurrency = editCurrentCurrency;
        paymentToEdit2.AmountUSD = newAmountUSD;
        paymentToEdit2.IsPendingConversion = newIsPending;
        QueueConversion(companyData, paymentToEdit2);

        // Recalc both invoices when the payment moves between them; recalc
        // just the one when only the amount/details changed. See §5.
        RecalcInvoiceTotals(companyData, oldInvoiceId);
        if (newInvoiceId != oldInvoiceId)
            RecalcInvoiceTotals(companyData, newInvoiceId);
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit payment '{paymentToEdit2.Id}'",
            () =>
            {
                paymentToEdit2.InvoiceId = oldInvoiceId;
                paymentToEdit2.CustomerId = oldCustomerId;
                paymentToEdit2.Date = oldDate;
                paymentToEdit2.Amount = oldAmount;
                paymentToEdit2.PaymentMethod = oldPaymentMethod;
                paymentToEdit2.ReferenceNumber = oldReferenceNumber;
                paymentToEdit2.Notes = oldNotesVal;
                paymentToEdit2.OriginalCurrency = oldOriginalCurrency;
                paymentToEdit2.AmountUSD = oldAmountUSD;
                paymentToEdit2.IsPendingConversion = oldIsPending;
                QueueConversion(companyData, paymentToEdit2);
                RecalcInvoiceTotals(companyData, newInvoiceId);
                if (newInvoiceId != oldInvoiceId)
                    RecalcInvoiceTotals(companyData, oldInvoiceId);
                companyData.MarkAsModified();
                PaymentSaved?.Invoke(this, EventArgs.Empty);
            },
            () =>
            {
                paymentToEdit2.InvoiceId = newInvoiceId;
                paymentToEdit2.CustomerId = newCustomerId;
                paymentToEdit2.Date = newDate;
                paymentToEdit2.Amount = newAmount;
                paymentToEdit2.PaymentMethod = newPaymentMethod;
                paymentToEdit2.ReferenceNumber = newReferenceNumber;
                paymentToEdit2.Notes = newNotesVal;
                paymentToEdit2.OriginalCurrency = editCurrentCurrency;
                paymentToEdit2.AmountUSD = newAmountUSD;
                paymentToEdit2.IsPendingConversion = newIsPending;
                QueueConversion(companyData, paymentToEdit2);
                RecalcInvoiceTotals(companyData, oldInvoiceId);
                if (newInvoiceId != oldInvoiceId)
                    RecalcInvoiceTotals(companyData, newInvoiceId);
                companyData.MarkAsModified();
                PaymentSaved?.Invoke(this, EventArgs.Empty);
            }));

        PaymentSaved?.Invoke(this, EventArgs.Empty);
        CloseEditModal();
    }

    #endregion

    #region Delete Payment

    public async void OpenDeleteConfirm(PaymentDisplayItem? item)
    {
        try
        {
            if (item == null)
                return;

            if (!await ConfirmDeleteAsync("Delete Payment".Translate(),
                    "Are you sure you want to delete this payment?\n\nPayment ID: {0}\nAmount: {1}".TranslateFormat(item.Id, item.AmountFormatted)))
                return;

            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null)
                return;

            var payment = companyData.Payments.FirstOrDefault(p => p.Id == item.Id);
            if (payment == null)
            {
                PaymentDeleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            var invoiceIdForRecalc = payment.InvoiceId;
            RemoveWithUndo(companyData, companyData.Payments, payment, $"Delete payment '{payment.Id}'",
                () => PaymentDeleted?.Invoke(this, EventArgs.Empty),
                onRemove: () =>
                {
                    ForgetConversion(companyData, payment);
                    RecalcInvoiceTotals(companyData, invoiceIdForRecalc);
                },
                onRestore: () =>
                {
                    QueueConversion(companyData, payment);
                    RecalcInvoiceTotals(companyData, invoiceIdForRecalc);
                });
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Payment.OpenDeleteConfirm");
        }
    }

    /// <summary>
    /// Re-derives invoice.AmountPaid / AmountRefunded / Balance / BalanceUSD
    /// and the stored Status from the current Payments list, and whether the
    /// invoice's revenue counts as collected. Called after
    /// any add / edit / delete of a Payment row, including undo / redo.
    /// No-op when invoiceId is null/empty or the invoice can't be found
    /// (the payment isn't tied to an invoice).
    /// </summary>
    private static void RecalcInvoiceTotals(Core.Data.CompanyData companyData, string? invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId)) return;
        var invoice = companyData.GetInvoice(invoiceId);
        if (invoice == null) return;
        InvoiceTotalsService.Recalculate(invoice, companyData.Payments);
        InvoiceTotalsService.SyncLinkedRevenueStatus(invoice, companyData.Revenues);

        // Every add / edit / delete of a payment funnels through here, and so do
        // the undo and redo lambdas, so this one call covers them all. Without
        // it the portal keeps believing a cash-paid invoice is unpaid and the
        // reminder cron chases a customer who has already paid. Debounced and
        // best-effort; the periodic reconcile catches anything dropped.
        App.PortalBalanceSyncService?.Queue(invoiceId);
    }

    #endregion

    #region Data Loading

    private void LoadInvoiceOptions()
    {
        InvoiceOptions.Clear();
        InvoiceOptions.Add(new InvoiceOption { Id = null, Display = "No Invoice" });

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Invoices == null)
            return;

        // Payments are recorded on sent invoices, as the invoice row offers. A payment on a draft
        // marked it paid while its revenue, created when it is sent, never existed. A draft that
        // already has a payment keeps it linked when that payment is edited.
        foreach (var invoice in companyData.Invoices
                     .Where(i => i.Status != InvoiceStatus.Draft || i.Id == _editingPayment?.InvoiceId)
                     .OrderByDescending(i => i.IssueDate))
        {
            var customer = companyData.GetCustomer(invoice.CustomerId);
            var customerName = customer?.Name ?? "Unknown";
            var totalPaid = companyData.Payments
                .Where(p => p.InvoiceId == invoice.Id && p.Amount > 0)
                .Sum(p => p.Amount);
            var amountDue = invoice.Total - totalPaid;

            InvoiceOptions.Add(new InvoiceOption
            {
                Id = invoice.Id,
                Display = $"{invoice.Id} - {customerName} (${amountDue:N2} due)",
                AmountDue = amountDue
            });
        }
    }

    #endregion

    #region Property Changed Handlers

    partial void OnModalAmountChanged(string value)
    {
        if (decimal.TryParse(value, out var amount) && amount != 0)
        {
            ModalAmountError = null;
        }
    }

    #endregion

    #region Modal Helpers

    private void ClearModalFields()
    {
        ModalInvoiceId = null;
        SelectedInvoice = null;
        ModalCustomerId = null;
        ModalAmount = string.Empty;
        ModalDate = DateTimeOffset.Now;
        ModalPaymentMethod = "Cash";
        ModalReferenceNumber = string.Empty;
        ModalNotes = string.Empty;
        IsPortalPayment = false;
        ClearModalErrors();
    }

    private void ClearModalErrors()
    {
        ModalInvoiceError = null;
        ModalAmountError = null;
    }

    private bool ValidateModal()
    {
        ClearModalErrors();
        var isValid = true;

        // Payments belong to an invoice. Money taken without one is recorded as a
        // Revenue instead, which already counts as income on its own.
        if (string.IsNullOrEmpty(ModalInvoiceId))
        {
            ModalInvoiceError = "Choose the invoice this payment is for.".Translate();
            isValid = false;
        }

        // Validate amount (required and must be a valid number)
        if (string.IsNullOrWhiteSpace(ModalAmount))
        {
            ModalAmountError = "Amount is required.".Translate();
            isValid = false;
        }
        else if (!decimal.TryParse(ModalAmount, out var amount) || amount == 0)
        {
            ModalAmountError = "Please enter a valid amount.".Translate();
            isValid = false;
        }

        return isValid;
    }

    #endregion
}

/// <summary>
/// Display model for payments in the UI.
/// </summary>
public partial class PaymentDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvoiceId))]
    private string _invoiceId = string.Empty;

    [ObservableProperty]
    private string _invoiceDisplay = string.Empty;

    /// <summary>
    /// True when this payment has a linked invoice, drives the Invoice
    /// column's hyperlink visibility. Manual payments without an invoice
    /// fall back to a "-" placeholder.
    /// </summary>
    public bool HasInvoiceId => !string.IsNullOrEmpty(InvoiceId);

    [ObservableProperty]
    private string _customerId = string.Empty;

    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    private PaymentMethod _paymentMethod;

    [ObservableProperty]
    private string _paymentMethodDisplay = string.Empty;

    [ObservableProperty]
    private decimal _amount;

    [ObservableProperty]
    private decimal _amountUSD;

    [ObservableProperty]
    private string _originalCurrency = "USD";

    [ObservableProperty]
    private string _status = "Completed";

    [ObservableProperty]
    private string? _referenceNumber;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _isFromPortal;

    public string DateFormatted => Date.ToString("MMM d, yyyy");

    public string AmountFormatted => AmountUSD < 0
        ? $"-{CurrencyService.FormatWithOriginal(Math.Abs(Amount), OriginalCurrency, Math.Abs(AmountUSD), Date)}"
        : CurrencyService.FormatWithOriginal(Amount, OriginalCurrency, AmountUSD, Date);

    /// <summary>
    /// True when the amount is showing the pending-conversion marker (its display-currency value
    /// isn't available yet). Drives the info glyph + tooltip next to the "Pending" text.
    /// </summary>
    public bool IsAmountPending => AmountFormatted == CurrencyService.PendingMarker;

    /// <summary>Friendly explanation shown in the info tooltip when <see cref="IsAmountPending"/>.</summary>
    public string PendingConversionHint => CurrencyService.BuildPendingConversionHint(Amount, OriginalCurrency, Date);

    /// <summary>
    /// Gets whether the amount is negative (refund).
    /// </summary>
    public bool IsRefund => Amount < 0;

    [ObservableProperty]
    private bool _isHighlighted;
}

/// <summary>
/// Invoice option for dropdown.
/// </summary>
public class InvoiceOption
{
    public string? Id { get; set; }
    public string Display { get; set; } = string.Empty;
    public decimal AmountDue { get; set; }

    public override string ToString() => Display;
}
