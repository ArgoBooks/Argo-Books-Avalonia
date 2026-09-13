namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// One row in the Capture tab's "Recent scans" list (see <see cref="CaptureViewModel.RecentScans"/>).
/// Immutable display snapshot of a confirmed scan - vendor/customer, amount, when it was confirmed,
/// and whether it reached the desktop queue or is still waiting in the outbox for a retry.
/// </summary>
public sealed class RecentScanViewModel
{
    public string VendorText { get; }

    public string AmountText { get; }

    public string TimeText { get; }

    public string StatusText { get; }

    public RecentScanViewModel(string vendorText, string amountText, string timeText, string statusText)
    {
        VendorText = vendorText;
        AmountText = amountText;
        TimeText = timeText;
        StatusText = statusText;
    }
}
