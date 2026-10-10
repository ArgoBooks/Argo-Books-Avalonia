using ArgoBooks.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Single-step verification modal shown after initial portal registration.
/// The server emails a 6-digit code to the registered owner email; until the
/// user enters it, refund endpoints return 412 (email_not_verified).
/// </summary>
public partial class VerifyEmailModalViewModel : ObservableObject
{
    private readonly RefundService _refundService;
    private System.Timers.Timer? _resendCooldownTimer;

    /// <summary>Seconds the Resend button stays disabled after a code is sent (on open and after each resend).</summary>
    private const int ResendCooldownSecondsInitial = 60;

    /// <summary>Set by the coordinator so the in-modal X button can close it.</summary>
    public Action? RequestClose { get; set; }

    /// <summary>
    /// Invoked once when the code is confirmed. The set-initial-email flow
    /// uses this to mirror the now-verified email into local company data;
    /// until then nothing is persisted, so closing the modal sets nothing.
    /// </summary>
    public Action? OnVerified { get; set; }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    public VerifyEmailModalViewModel(RefundService refundService, string? maskedEmail)
    {
        _refundService = refundService;
        MaskedEmail = maskedEmail;

        // A code is always sent just before this modal opens, so Resend starts on cooldown rather than being clickable at once.
        StartResendCooldown(ResendCooldownSecondsInitial);
    }

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string? _maskedEmail;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isVerified;

    [ObservableProperty]
    private int _resendCooldownSeconds;

    public bool CanSubmit => !IsBusy && Code.Length == 6 && Code.All(char.IsDigit);
    public bool CanResend => !IsBusy && ResendCooldownSeconds == 0;
    public string ResendButtonText => ResendCooldownSeconds > 0
        ? $"Resend code ({ResendCooldownSeconds}s)"
        : "Resend code";

    partial void OnCodeChanged(string value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanSubmit)); OnPropertyChanged(nameof(CanResend)); }
    partial void OnResendCooldownSecondsChanged(int value) { OnPropertyChanged(nameof(CanResend)); OnPropertyChanged(nameof(ResendButtonText)); }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (!CanSubmit) return;
        IsBusy = true; ErrorMessage = null;
        try
        {
            var result = await _refundService.ConfirmRegistrationCodeAsync(Code);
            if (!result.Ok)
            {
                ErrorMessage = result.ErrorCode switch
                {
                    "WRONG_CODE" => "Wrong code. Try again.",
                    "EXPIRED" => "Code expired. Request a new one.",
                    "NO_ACTIVE_CODE" => "No active verification code. Request one to start over.",
                    "TOO_MANY_ATTEMPTS" => "Too many wrong attempts. Request a new code.",
                    _ => result.Message ?? "Could not verify the code.",
                };
                return;
            }
            IsVerified = true;
            StatusMessage = "Email verified.";
            OnVerified?.Invoke();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ResendAsync()
    {
        if (!CanResend) return;
        IsBusy = true; ErrorMessage = null;
        try
        {
            var result = await _refundService.RequestRegistrationCodeAsync();
            if (!result.Ok)
            {
                ErrorMessage = result.Message ?? result.ErrorCode ?? "Could not resend code.";
                return;
            }
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
}
