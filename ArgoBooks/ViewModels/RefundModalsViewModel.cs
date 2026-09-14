using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Transactions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Coordinator view-model for the three refund-feature modals (refund,
/// email-verify, email-change). Mirrors the existing ModalsViewModel pattern
/// (CategoryModalsViewModel, InvoiceModalsViewModel, etc.): bound to a
/// <see cref="ArgoBooks.Modals.RefundModals"/> UserControl that's hosted at
/// AppShell level so the ModalOverlay can dim the whole window and own
/// backdrop-click + Escape closing.
/// </summary>
public partial class RefundModalsViewModel : ObservableObject
{
    // ----- Refund modal -----

    [ObservableProperty]
    private bool _isRefundModalOpen;

    [ObservableProperty]
    private RefundModalViewModel? _activeRefundVm;

    /// <summary>
    /// Optional callback invoked after the refund modal closes (any reason)
    /// so the caller can refresh its display (e.g. the invoices list).
    /// </summary>
    private Action? _onRefundClosed;

    /// <summary>
    /// Opens the refund window for an invoice. The verification code goes to the owner email, so a missing
    /// one is caught here rather than halfway through. <paramref name="depositOnly"/> selects only the
    /// deposit, at that amount.
    /// </summary>
    public async Task OpenForInvoiceAsync(CompanyData companyData, Invoice invoice, Action? onClosed = null,
        decimal? depositOnly = null, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(companyData.Settings.Company.Email))
        {
            await App.ShowWarningMessageBoxAsync(
                "Owner email required",
                "You need to set your portal owner email before issuing a refund. The verification code is sent to that address.\n\nOpen Settings → Payment Portal and set your owner email, then try again.");
            return;
        }

        var payments = companyData.Payments.Where(p => p.InvoiceId == invoice.Id).ToList();
        var customerName = companyData.GetCustomer(invoice.CustomerId)?.Name ?? string.Empty;
        OpenRefundModal(invoice, payments, customerName, onClosed, depositOnly, reason);
    }

    public void OpenRefundModal(Invoice invoice, IEnumerable<Payment> invoicePayments, string customerName, Action? onClosed = null,
        decimal? depositOnly = null, string? reason = null)
    {
        var refundService = App.RefundService;
        if (refundService == null) return;

        ActiveRefundVm?.Dispose();
        ActiveRefundVm = new RefundModalViewModel(refundService, invoice, invoicePayments, customerName, depositOnly, reason)
        {
            RequestClose = CloseRefundModal,
        };
        _onRefundClosed = onClosed;
        IsRefundModalOpen = true;
    }

    [RelayCommand]
    private void CloseRefundModal()
    {
        IsRefundModalOpen = false;
        ActiveRefundVm?.Dispose();
        ActiveRefundVm = null;
        _onRefundClosed?.Invoke();
        _onRefundClosed = null;
    }

    /// <summary>
    /// Bound to ModalOverlay.ClosingCommand, fires on backdrop click and Esc.
    /// If the user is mid-flow on a refund (entered code, etc.) we still
    /// just close, the server-side request is left in pending_code and the
    /// hourly cleanup cron will cancel it. No data loss.
    /// </summary>
    [RelayCommand]
    private void RequestCloseRefundModal() => CloseRefundModal();

    // ----- Email verification modal (post-registration) -----

    [ObservableProperty]
    private bool _isVerifyEmailModalOpen;

    [ObservableProperty]
    private VerifyEmailModalViewModel? _activeVerifyEmailVm;

    public void OpenVerifyEmailModal(string? maskedEmail, Action? onVerified = null)
    {
        var refundService = App.RefundService;
        if (refundService == null) return;

        ActiveVerifyEmailVm?.Dispose();
        ActiveVerifyEmailVm = new VerifyEmailModalViewModel(refundService, maskedEmail)
        {
            RequestClose = CloseVerifyEmailModal,
            OnVerified = onVerified,
        };
        IsVerifyEmailModalOpen = true;
    }

    [RelayCommand]
    private void CloseVerifyEmailModal()
    {
        IsVerifyEmailModalOpen = false;
        ActiveVerifyEmailVm?.Dispose();
        ActiveVerifyEmailVm = null;
    }

    [RelayCommand]
    private void RequestCloseVerifyEmailModal() => CloseVerifyEmailModal();

    // ----- Email change modal (4-step) -----

    [ObservableProperty]
    private bool _isEmailChangeModalOpen;

    [ObservableProperty]
    private EmailChangeModalViewModel? _activeEmailChangeVm;

    private Action<string>? _onEmailChangeCompleted;

    public void OpenEmailChangeModal(
        string currentOwnerEmail,
        bool fileIsEncrypted,
        Func<string, bool> verifyFilePassword,
        Action<string>? onCompleted = null,
        bool currentEmailVerified = true)
    {
        var refundService = App.RefundService;
        if (refundService == null) return;

        ActiveEmailChangeVm?.Dispose();
        ActiveEmailChangeVm = new EmailChangeModalViewModel(
            refundService, currentOwnerEmail, fileIsEncrypted, verifyFilePassword, currentEmailVerified)
        {
            // Close runs through CloseEmailChangeModalAsync via fire-and-forget so
            // we get the same "abort in-flight + invoke onCompleted" semantics
            // whether the user clicks X, presses Esc, or clicks the backdrop.
            RequestClose = () => _ = CloseEmailChangeModalAsync(),
        };
        _onEmailChangeCompleted = onCompleted;
        IsEmailChangeModalOpen = true;
    }

    [RelayCommand]
    private async Task CloseEmailChangeModalAsync()
    {
        var vm = ActiveEmailChangeVm;
        IsEmailChangeModalOpen = false;
        if (vm != null)
        {
            // Mid-flow close: best-effort cancel the in-flight server request
            if (vm.CurrentStep != EmailChangeModalViewModel.Step.Success
                && vm.CurrentStep != EmailChangeModalViewModel.Step.Failure)
            {
                try { await vm.CancelChangeCommand.ExecuteAsync(null); } catch { /* swallow */ }
            }
            else if (vm.CurrentStep == EmailChangeModalViewModel.Step.Success)
            {
                // Prefer the server-confirmed value over raw user input,
                // since the server may have normalized (trim / casing).
                var confirmed = vm.ConfirmedNewEmail?.Trim();
                _onEmailChangeCompleted?.Invoke(
                    string.IsNullOrEmpty(confirmed) ? vm.NewEmail.Trim() : confirmed);
            }
        }
        vm?.Dispose(); // stop the resend-cooldown timer so it doesn't keep firing after the modal closes
        ActiveEmailChangeVm = null;
        _onEmailChangeCompleted = null;
    }

    [RelayCommand]
    private async Task RequestCloseEmailChangeModalAsync() => await CloseEmailChangeModalAsync();
}
