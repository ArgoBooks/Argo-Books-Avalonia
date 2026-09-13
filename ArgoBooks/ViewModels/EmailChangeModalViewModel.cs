using ArgoBooks.Core.Services;
using ArgoBooks.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// 4-step email change wizard. The owner email is locked from in-place edit;
/// changing it requires:
///   Step 1: enter new email
///   Step 2: enter the .argo file password (only shown if encrypted)
///   Step 3: enter the 6-digit code emailed to the OLD address
///   Step 4: enter the 6-digit code emailed to the NEW address
/// On success the OLD address receives a notification with a 30-day revert link.
/// </summary>
public partial class EmailChangeModalViewModel : ObservableObject
{
    public enum Step
    {
        EnterNewEmail,
        EnterPassword,
        EnterOldCode,
        EnterNewCode,
        Success,
        Failure,
    }

    private readonly RefundService _refundService;
    private readonly bool _fileIsEncrypted;
    private readonly Func<string, bool> _verifyFilePassword;
    private long _changeId;

    private System.Timers.Timer? _resendCooldownTimer;

    /// <summary>Seconds the Resend button stays disabled after a code is sent (on each code step and after each resend).</summary>
    private const int ResendCooldownSecondsInitial = 60;

    /// <summary>Set by the coordinator so the in-modal X button can close it.</summary>
    public Action? RequestClose { get; set; }

    /// <summary>
    /// Server-authoritative new owner email, populated when the change
    /// completes successfully. Differs from <see cref="NewEmail"/> (raw
    /// user input) by any normalisation the server applied (trim/case).
    /// Coordinator reads this for the completion callback.
    /// </summary>
    public string? ConfirmedNewEmail { get; private set; }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    public string CurrentOwnerEmail { get; }

    public EmailChangeModalViewModel(
        RefundService refundService,
        string currentOwnerEmail,
        bool fileIsEncrypted,
        Func<string, bool> verifyFilePassword,
        bool currentEmailVerified = true)
    {
        _refundService = refundService;
        CurrentOwnerEmail = currentOwnerEmail;
        _fileIsEncrypted = fileIsEncrypted;
        _verifyFilePassword = verifyFilePassword;
        CurrentEmailCanReceiveCode = currentEmailVerified;
    }

    /// <summary>
    /// Whether the current address was ever verified. When it was not, the server sends no
    /// code to it, so promising one would be wrong: the sample company's fake address is the
    /// obvious case.
    /// </summary>
    public bool CurrentEmailCanReceiveCode { get; }

    /// <summary>Inverse of <see cref="CurrentEmailCanReceiveCode"/>, for binding.</summary>
    public bool CurrentEmailCannotReceiveCode => !CurrentEmailCanReceiveCode;

    [ObservableProperty]
    private Step _currentStep = Step.EnterNewEmail;

    public bool IsEnterNewEmail => CurrentStep == Step.EnterNewEmail;
    public bool IsEnterPassword => CurrentStep == Step.EnterPassword;
    public bool IsEnterOldCode  => CurrentStep == Step.EnterOldCode;
    public bool IsEnterNewCode  => CurrentStep == Step.EnterNewCode;
    public bool IsSuccess       => CurrentStep == Step.Success;
    public bool IsFailure       => CurrentStep == Step.Failure;

    partial void OnCurrentStepChanged(Step value)
    {
        OnPropertyChanged(nameof(IsEnterNewEmail));
        OnPropertyChanged(nameof(IsEnterPassword));
        OnPropertyChanged(nameof(IsEnterOldCode));
        OnPropertyChanged(nameof(IsEnterNewCode));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsFailure));
        OnPropertyChanged(nameof(ShowSharedStatusMessage));
    }

    [ObservableProperty]
    private string _newEmail = string.Empty;

    [ObservableProperty]
    private string _filePassword = string.Empty;

    [ObservableProperty]
    private string _oldCode = string.Empty;

    [ObservableProperty]
    private string _newCode = string.Empty;

    [ObservableProperty]
    private string? _maskedOldEmail;

    [ObservableProperty]
    private string? _maskedNewEmail;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _resendCooldownSeconds;

    /// <summary>Resend is allowed only when no request is in flight and the cooldown has elapsed.</summary>
    public bool CanResend => !IsBusy && ResendCooldownSeconds == 0;

    public string ResendButtonText => ResendCooldownSeconds > 0
        ? $"Resend code ({ResendCooldownSeconds}s)"
        : "Resend code";

    partial void OnResendCooldownSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(ResendButtonText));
    }

    /// <summary>
    /// The shared status line is shown on every step except Success, which renders its own
    /// centered copy of the message (so the success detail isn't duplicated left-aligned below).
    /// </summary>
    public bool ShowSharedStatusMessage => !IsSuccess && !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(ShowSharedStatusMessage));

    public bool CanContinueFromNewEmail
        => !IsBusy && DataValidator.IsValidEmail(NewEmail) && !string.Equals(NewEmail.Trim(), CurrentOwnerEmail, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// User-facing reason the Continue button is disabled, or null when valid.
    /// Empty input is silent (don't shout at someone who just hasn't typed yet).
    /// </summary>
    public string? NewEmailValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NewEmail)) return null;
            if (string.Equals(NewEmail.Trim(), CurrentOwnerEmail, StringComparison.OrdinalIgnoreCase))
                return "That's already the email on file. Enter a different address.";
            if (!DataValidator.IsValidEmail(NewEmail))
                return "Enter a valid email address.";
            return null;
        }
    }

    public bool CanContinueFromPassword => !IsBusy && FilePassword.Length > 0;

    [ObservableProperty]
    private bool _isFilePasswordVisible;

    public string FilePasswordVisibilityIcon => IsFilePasswordVisible ? Icons.EyeOff : Icons.Eye;

    /// <summary>
    /// Mask character for the file password box, cleared while the password is revealed.
    ///
    /// Revealing is done by dropping the mask character rather than by setting
    /// RevealPassword, because Avalonia 12.0.5 treats any box with a mask character as a
    /// password box and silently disables Ctrl+Arrow word movement, Ctrl+Shift+Arrow
    /// selection and Ctrl+Backspace, regardless of RevealPassword. Clearing the character
    /// makes it an ordinary text box again, so those shortcuts work while it is revealed.
    /// </summary>
    public char FilePasswordMaskChar => IsFilePasswordVisible ? '\0' : '•';

    partial void OnIsFilePasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(FilePasswordVisibilityIcon));
        OnPropertyChanged(nameof(FilePasswordMaskChar));
    }

    [RelayCommand]
    private void ToggleFilePasswordVisibility() => IsFilePasswordVisible = !IsFilePasswordVisible;
    public bool CanSubmitOldCode => !IsBusy && OldCode.Length == 6 && OldCode.All(char.IsDigit);
    public bool CanSubmitNewCode => !IsBusy && NewCode.Length == 6 && NewCode.All(char.IsDigit);

    partial void OnNewEmailChanged(string value)
    {
        OnPropertyChanged(nameof(CanContinueFromNewEmail));
        OnPropertyChanged(nameof(NewEmailValidationMessage));
    }
    partial void OnFilePasswordChanged(string value)=> OnPropertyChanged(nameof(CanContinueFromPassword));
    partial void OnOldCodeChanged(string value)     => OnPropertyChanged(nameof(CanSubmitOldCode));
    partial void OnNewCodeChanged(string value)     => OnPropertyChanged(nameof(CanSubmitNewCode));
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanContinueFromNewEmail));
        OnPropertyChanged(nameof(CanContinueFromPassword));
        OnPropertyChanged(nameof(CanSubmitOldCode));
        OnPropertyChanged(nameof(CanSubmitNewCode));
        OnPropertyChanged(nameof(CanResend));
    }

    [RelayCommand]
    private async Task ContinueFromNewEmailAsync()
    {
        if (!CanContinueFromNewEmail) return;
        ErrorMessage = null;
        if (_fileIsEncrypted)
        {
            // Password step is local; only the password command itself sends
            // a server request, so a straight step transition is safe here.
            CurrentStep = Step.EnterPassword;
            return;
        }
        // Unencrypted file: send the change request and let it advance to
        // EnterOldCode on success. Awaiting (instead of fire-and-forget)
        // ensures we don't sit on the code-entry step when the request
        // failed (network error, EMAIL_IN_USE, COOLDOWN_ACTIVE, etc.).
        await RequestEmailChangeAsync(passwordVerified: false);
    }

    [RelayCommand]
    private async Task ContinueFromPasswordAsync()
    {
        if (!CanContinueFromPassword) return;
        IsBusy = true; ErrorMessage = null;
        try
        {
            bool ok;
            try { ok = _verifyFilePassword(FilePassword); }
            catch { ok = false; }
            if (!ok)
            {
                ErrorMessage = "Wrong file password.";
                return;
            }
            FilePassword = string.Empty; // clear from memory once verified
            IsFilePasswordVisible = false;
            await RequestEmailChangeAsync(passwordVerified: true);
        }
        finally { IsBusy = false; }
    }

    private async Task RequestEmailChangeAsync(bool passwordVerified)
    {
        IsBusy = true; ErrorMessage = null;
        try
        {
            var result = await _refundService.RequestEmailChangeAsync(NewEmail.Trim(), passwordVerified);
            if (!result.Ok)
            {
                ErrorMessage = result.ErrorCode switch
                {
                    "EMAIL_IN_USE" => "That email is already used by another portal account.",
                    "COOLDOWN_ACTIVE" => "Email was changed less than 24h ago. Try again later.",
                    "INVALID_EMAIL" => "Invalid email address.",
                    _ => result.Message ?? "Could not start the email change.",
                };
                return;
            }
            _changeId = result.ChangeId;
            MaskedOldEmail = result.MaskedOldEmail;

            // An address that was never verified cannot receive anything, so the server skips
            // it and sends straight to the new one. Asking for a code from an unreachable
            // inbox is an unopenable lock.
            if (!result.OldEmailVerificationRequired)
            {
                MaskedNewEmail = result.MaskedNewEmail;
                CurrentStep = Step.EnterNewCode;
                StatusMessage = $"Code sent to {result.MaskedNewEmail}.";
            }
            else
            {
                CurrentStep = Step.EnterOldCode;
                StatusMessage = $"Code sent to {result.MaskedOldEmail}.";
            }

            StartResendCooldown(ResendCooldownSecondsInitial);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SubmitOldCodeAsync()
    {
        if (!CanSubmitOldCode) return;
        IsBusy = true; ErrorMessage = null;
        try
        {
            var result = await _refundService.ConfirmEmailChangeOldAsync(_changeId, OldCode);
            if (!result.Ok)
            {
                ErrorMessage = result.ErrorCode == "WRONG_CODE" ? "Wrong code. Try again." : (result.Message ?? "Could not verify code.");
                return;
            }
            MaskedNewEmail = result.MaskedNewEmail;
            CurrentStep = Step.EnterNewCode;
            StatusMessage = $"Code sent to {result.MaskedNewEmail}.";
            StartResendCooldown(ResendCooldownSecondsInitial); // a code was just sent to the new address
            OldCode = string.Empty;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SubmitNewCodeAsync()
    {
        if (!CanSubmitNewCode) return;
        IsBusy = true; ErrorMessage = null;
        try
        {
            var result = await _refundService.ConfirmEmailChangeNewAsync(_changeId, NewCode);
            if (!result.Ok)
            {
                ErrorMessage = result.ErrorCode == "WRONG_CODE" ? "Wrong code. Try again." : (result.Message ?? "Could not verify code.");
                return;
            }

            // Mirror the new email into local CompanyData and persist (scoped
            // save (only appSettings.json) so other in-memory edits stay
            // un-flushed). Without this, a restart leaves the UI showing
            // the OLD email even though the server has the new one.
            var companyData = App.CompanyManager?.CompanyData;
            if (companyData != null && !string.IsNullOrWhiteSpace(result.NewEmail))
            {
                companyData.Settings.Company.Email = result.NewEmail;
                companyData.ChangesMade = true;
                if (App.CompanyManager != null)
                {
                    try { await App.CompanyManager.SaveSettingsOnlyAsync(); }
                    catch (Exception ex) { App.ErrorLogger?.LogWarning($"Failed to persist email change: {ex.Message}", "OwnerEmail"); }
                }
            }

            // Capture the server-authoritative value for the coordinator's
            // completion callback (don't fall back to the user's NewEmail
            // string, which may differ by trim/case).
            ConfirmedNewEmail = result.NewEmail;
            CurrentStep = Step.Success;
            StatusMessage = $"Owner email is now {result.NewEmail}. The OLD address received a revert link valid for 30 days.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ResendOldCodeAsync()
    {
        if (_changeId == 0 || !CanResend) return;
        IsBusy = true; ErrorMessage = null;
        try
        {
            var result = await _refundService.ResendEmailChangeCodeAsync(_changeId, "old");
            if (!result.Ok) { ErrorMessage = result.Message ?? "Could not resend code."; return; }
            StartResendCooldown(ResendCooldownSecondsInitial);
            StatusMessage = "Code re-sent.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ResendNewCodeAsync()
    {
        if (_changeId == 0 || !CanResend) return;
        IsBusy = true; ErrorMessage = null;
        try
        {
            var result = await _refundService.ResendEmailChangeCodeAsync(_changeId, "new");
            if (!result.Ok) { ErrorMessage = result.Message ?? "Could not resend code."; return; }
            StartResendCooldown(ResendCooldownSecondsInitial);
            StatusMessage = "Code re-sent.";
        }
        finally { IsBusy = false; }
    }

    private void StartResendCooldown(int seconds)
    {
        StopResendCooldown();
        ResendCooldownSeconds = seconds;
        _resendCooldownTimer = new System.Timers.Timer(1000) { AutoReset = true };
        _resendCooldownTimer.Elapsed += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (ResendCooldownSeconds > 0) ResendCooldownSeconds--;
                if (ResendCooldownSeconds == 0) StopResendCooldown();
            });
        };
        _resendCooldownTimer.Start();
    }

    private void StopResendCooldown()
    {
        _resendCooldownTimer?.Stop();
        _resendCooldownTimer?.Dispose();
        _resendCooldownTimer = null;
    }

    public void Dispose() => StopResendCooldown();

    [RelayCommand]
    private async Task CancelChangeAsync()
    {
        if (_changeId == 0) return;
        try { await _refundService.CancelEmailChangeAsync(_changeId); } catch { /* best-effort */ }
    }
}
