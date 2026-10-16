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
    private readonly ResendCooldown _resendCooldown = new();

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
        _resendCooldown.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanResend));
            OnPropertyChanged(nameof(ResendButtonText));
        };

        _refundService = refundService;
        MaskedEmail = maskedEmail;

        // A code is always sent just before this modal opens, so Resend starts on cooldown rather than being clickable at once.
        _resendCooldown.Start();
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

    public bool CanSubmit => !IsBusy && Code.Length == 6 && Code.All(char.IsDigit);
    public bool CanResend => !IsBusy && !_resendCooldown.IsRunning;
    public string ResendButtonText => _resendCooldown.ButtonText;

    partial void OnCodeChanged(string value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanSubmit)); OnPropertyChanged(nameof(CanResend)); }

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
            _resendCooldown.Start();
            StatusMessage = "Code re-sent.";
        }
        finally { IsBusy = false; }
    }

    public void Dispose() => _resendCooldown.Stop();
}
