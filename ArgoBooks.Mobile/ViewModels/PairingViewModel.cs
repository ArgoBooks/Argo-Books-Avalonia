using ArgoBooks.Mobile.Services;
using ArgoBooks.Shared.Mobile;
using ArgoBooks.Shared.Sync;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// "Connect to desktop" screen. Offers two ways to get the pairing token - the ML Kit QR
/// scanner (see QrScanner) or typing the short human-readable code shown on the desktop's sync
/// settings - then hands it to PairingCoordinator (the unit-tested logic) to redeem + store.
/// </summary>
public partial class PairingViewModel : ViewModelBase
{
    private readonly PairingCoordinator _coordinator;

    [ObservableProperty]
    private string _enteredCode = string.Empty;

    [ObservableProperty]
    private string _deviceLabel = DefaultDeviceLabel();

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isSuccess;

    /// <summary>
    /// False on the welcome screen (Scan QR + a de-emphasized "enter a code" link); true on the
    /// second screen that holds the manual code + device-name fields. QR scanning is the primary
    /// path, so the code entry lives one step back.
    /// </summary>
    [ObservableProperty]
    private bool _showCodeEntry;

    /// <summary>Raised after a successful pairing, with the paired company's label.</summary>
    public event Action<string>? Paired;

    /// <summary>Welcome screen -> manual code entry screen.</summary>
    [RelayCommand]
    private void OpenCodeEntry()
    {
        StatusMessage = null;
        ShowCodeEntry = true;
    }

    /// <summary>Manual code entry screen -> welcome screen.</summary>
    [RelayCommand]
    private void BackToScan()
    {
        StatusMessage = null;
        ShowCodeEntry = false;
    }

    /// <summary>
    /// Called by the host when pairing succeeded but the dashboard failed to open, so the user
    /// sees why instead of being stranded on the "Connected" message with no feedback.
    /// </summary>
    public void ReportShellOpenFailed(string? detail)
    {
        IsBusy = false;
        IsSuccess = false;
        StatusMessage = string.IsNullOrWhiteSpace(detail)
            ? "Connected, but the dashboard couldn't open. Please reopen the app and try again."
            : $"Connected, but the dashboard couldn't open: {detail}";
    }

    public PairingViewModel()
        : this(new PairingCoordinator(new MobileSyncClient(null, MobileApiConfig.BaseUrl), new PairedCompanyStore(new MauiSecureStore())))
    {
    }

    public PairingViewModel(PairingCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    [RelayCommand]
    private async Task ScanQrAsync()
    {
        StatusMessage = null;
        var payload = await QrScanner.ScanAsync();
        if (string.IsNullOrWhiteSpace(payload))
        {
            StatusMessage = "Scan cancelled. You can enter a code instead.";
            return;
        }

        await RunPairingAsync(ct => _coordinator.PairFromPayloadAsync(payload.Trim(), DeviceLabel, ct));
    }

    [RelayCommand]
    private async Task ConnectWithCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(EnteredCode))
        {
            StatusMessage = "Enter the code shown on your computer, under Settings > Mobile app.";
            return;
        }

        await RunPairingAsync(ct => _coordinator.PairFromCodeAsync(EnteredCode, DeviceLabel, ct));
    }

    /// <summary>Shared by both pairing paths: runs the given coordinator call and maps its
    /// <see cref="PairingOutcome"/> onto <see cref="StatusMessage"/>/<see cref="IsSuccess"/>/<see cref="Paired"/>.</summary>
    private async Task RunPairingAsync(Func<CancellationToken, Task<PairingOutcome>> pair)
    {
        IsBusy = true;
        IsSuccess = false;
        StatusMessage = null;

        try
        {
            var outcome = await pair(CancellationToken.None);

            if (outcome.Success)
            {
                IsSuccess = true;
                StatusMessage = $"Connected to {outcome.CompanyLabel}.";
                Paired?.Invoke(outcome.CompanyLabel ?? string.Empty);
            }
            else
            {
                IsSuccess = false;
                StatusMessage = outcome.Error ?? "Could not connect. Try again.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string DefaultDeviceLabel()
    {
        try
        {
            var model = Android.OS.Build.Model;
            return string.IsNullOrWhiteSpace(model) ? "My Phone" : model;
        }
        catch
        {
            return "My Phone";
        }
    }
}
