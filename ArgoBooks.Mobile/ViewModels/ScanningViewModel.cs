using ArgoBooks.Core.Services;
using ArgoBooks.Shared.Mobile;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// Scanning progress screen: shown immediately after DocumentScanner returns a cropped image,
/// while ReceiptScanCoordinator posts it to the AI proxy in the background. The edge-detect/crop/
/// straighten already happened live in ML Kit, so the steps shown here ("Reading the receipt",
/// "Auto-suggesting products &amp; supplier", "Checking the totals") are just the AI read - there's
/// no real per-step progress from the server, they're shown together while the call is in flight.
/// On success, hands the ReceiptScanResult to <see cref="_onSuccess"/> (ShellViewModel pushes the
/// Review screen); on failure, this same screen switches to a failed state (<see cref="IsFailed"/>)
/// with a Retry action, rather than navigating to a separate view.
/// </summary>
public partial class ScanningViewModel : ViewModelBase
{
    private readonly byte[] _imageBytes;
    private readonly ReceiptScanCoordinator _coordinator;
    private readonly Action<ReceiptScanResult> _onSuccess;
    private readonly Action _onRetry;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private string? _errorMessage;

    public ScanningViewModel(byte[] imageBytes, ReceiptScanCoordinator coordinator, Action<ReceiptScanResult> onSuccess, Action onRetry)
    {
        _imageBytes = imageBytes ?? throw new ArgumentNullException(nameof(imageBytes));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _onSuccess = onSuccess ?? throw new ArgumentNullException(nameof(onSuccess));
        _onRetry = onRetry ?? throw new ArgumentNullException(nameof(onRetry));
        _ = RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        IsFailed = false;
        ErrorMessage = null;

        var result = await _coordinator.ScanAsync(_imageBytes, "scan.jpg");
        if (result.IsSuccess)
        {
            _onSuccess(result);
        }
        else
        {
            ErrorMessage = result.ErrorMessage ?? "We couldn't read that receipt. Try again with better lighting.";
            IsFailed = true;
        }
    }

    [RelayCommand]
    private void Retry() => _onRetry();
}
