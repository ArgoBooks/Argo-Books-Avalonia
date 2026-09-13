using ArgoBooks.Mobile.Services;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// Lock screen shown on cold start (when a company is paired) and when the app resumes from the
/// background past the grace period. Blocks any financial data from rendering until the user
/// authenticates via <see cref="AppLockService"/> (biometric, with device PIN/pattern fallback).
/// </summary>
public partial class LockViewModel : ViewModelBase
{
    private readonly AppLockService _lockService;

    [ObservableProperty]
    private bool _isAuthenticating;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Raised once the user successfully authenticates.</summary>
    public event Action? Unlocked;

    public LockViewModel(AppLockService lockService)
    {
        _lockService = lockService ?? throw new ArgumentNullException(nameof(lockService));
    }

    /// <summary>Prompts for authentication immediately (called once when the lock screen appears).</summary>
    public async Task TryUnlockAsync()
    {
        if (IsAuthenticating)
        {
            return;
        }

        IsAuthenticating = true;
        StatusMessage = null;
        try
        {
            var success = await _lockService.AuthenticateAsync("Unlock Argo Books");
            if (success)
            {
                Unlocked?.Invoke();
            }
            else
            {
                StatusMessage = "Authentication cancelled. Tap Unlock to try again.";
            }
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    [RelayCommand]
    private async Task UnlockAsync() => await TryUnlockAsync();
}
