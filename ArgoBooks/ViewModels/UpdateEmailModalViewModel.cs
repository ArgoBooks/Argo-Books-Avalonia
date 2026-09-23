using ArgoBooks.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Asks for an address to send product updates to. The app needs no account, so this is the one
/// optional place someone can choose to be reachable, and nothing depends on the answer.
/// </summary>
public partial class UpdateEmailModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isSending;

    /// <summary>Swaps the form for the "check your inbox" state once a link has been sent.</summary>
    [ObservableProperty]
    private bool _isDone;

    /// <summary>Raised after a successful sign-up so the dashboard banner can drop away.</summary>
    public event EventHandler? Subscribed;

    [RelayCommand]
    public void Open()
    {
        Email = string.Empty;
        Message = string.Empty;
        IsDone = false;
        IsSending = false;
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private async Task SubscribeAsync()
    {
        if (IsSending) return;

        IsSending = true;
        Message = string.Empty;
        try
        {
            using var service = new UpdateEmailService(App.ErrorLogger);
            var result = await service.SubscribeAsync(Email);
            Message = result.Message;

            if (!result.Success) return;

            IsDone = true;

            var settings = App.SettingsService;
            if (settings != null)
            {
                settings.GlobalSettings.UpdateEmail.Submitted = true;
                await settings.SaveGlobalSettingsAsync();
            }

            Subscribed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsSending = false;
        }
    }
}
