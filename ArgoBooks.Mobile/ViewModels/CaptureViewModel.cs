using ArgoBooks.Core.Services.Sync;
using ArgoBooks.Mobile.Services;
using ArgoBooks.Shared.Mobile;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// The Capture tab's root screen: shows which paired company the scan will land in and a
/// best-effort local scan counter, then a shutter plus an "Import from photos" affordance. Both
/// buttons launch the same ML Kit DocumentScanner - its own UI exposes gallery import once
/// SetGalleryImportAllowed is set (see DocumentScanner's doc comment), so there's no separate
/// gallery-only entry point to call. A captured image hands off to ShellViewModel's
/// StartScanFlowAsync callback, which pushes the ScanningView and drives the AI call.
/// Also owns the "Recent scans" list (see <see cref="AddRecentScan"/>): once a scan is confirmed
/// on the review screen and CaptureDeliveryCoordinator has (or hasn't yet) delivered it to the
/// desktop queue, ShellViewModel records it here so the user has some visible confirmation - the
/// phone has no ledger of its own and can't poll for the desktop's ingest, so this is local-only
/// and never checked back against what actually landed on the desktop.
/// Edge states: once the local free-scan counter (<see cref="ScanUsageStore"/>/
/// <see cref="ScanQuota"/>) reaches the monthly limit, <see cref="IsOverLimit"/> flips and the view
/// swaps to an upgrade prompt (<see cref="UpgradeCommand"/> opens the marketing site in the system
/// browser - no in-app purchase). If a capture happens with no network, the cropped image is
/// queued in <see cref="PendingScanOutbox"/> instead of starting the AI scan flow; once online, the
/// Capture screen shows a "N receipts ready to review" prompt (<see cref="HasPendingOfflineScans"/>)
/// and the user walks each queued receipt through the normal review flow - nothing is auto-posted.
/// Captures waiting on a company that is no longer paired are counted separately
/// (<see cref="StrandedCount"/>): they are neither reviewable nor sendable until it is paired back,
/// and the prompt says so rather than offering a review that could not go anywhere.
/// </summary>
public partial class CaptureViewModel : ViewModelBase
{
    private static readonly Uri UpgradeUri = new("https://argorobots.com");

    private readonly ISecureStore _secureStore;
    private readonly Func<byte[], Task> _onImageCaptured;
    private readonly PendingScanOutbox _pendingScanOutbox;
    private readonly PairedCompanyStore _pairedCompanyStore;
    private readonly Func<Task> _onReviewOfflineScans;

    /// <summary>Set by ShellViewModel (via <see cref="SetActiveCompanyLabel"/>) whenever the
    /// active company changes, so the "Scanning into X" bar always reflects it.</summary>
    [ObservableProperty]
    private string _activeCompanyLabel = string.Empty;

    [ObservableProperty]
    private int _scansUsedThisMonth;

    /// <summary>True once <see cref="ScansUsedThisMonth"/> has reached the free monthly limit
    /// (<see cref="ScanQuota.FreeMonthlyLimit"/>). Swaps the Capture screen from the viewfinder to
    /// the "used your N free scans" upgrade prompt.</summary>
    [ObservableProperty]
    private bool _isOverLimit;

    /// <summary>The free-tier monthly scan limit, for the upgrade prompt's message.</summary>
    public int FreeScanLimit => ScanQuota.FreeMonthlyLimit;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Most recent scan first. Populated by <see cref="AddRecentScan"/>.</summary>
    public ObservableCollection<RecentScanViewModel> RecentScans { get; } = new();

    /// <summary>True once at least one scan has been confirmed this session, so the "Recent
    /// scans" section only shows up once there's something to show.</summary>
    public bool HasRecentScans => RecentScans.Count > 0;

    [ObservableProperty]
    private bool _isConfirmationVisible;

    [ObservableProperty]
    private string _confirmationMessage = string.Empty;

    /// <summary>How many receipts were captured while offline and are waiting to be reviewed. Drives
    /// the "N receipts ready to review" prompt (see <see cref="HasPendingOfflineScans"/>).</summary>
    [ObservableProperty]
    private int _pendingOfflineCount;

    /// <summary>How many captures are waiting on a company that is no longer paired. They are kept
    /// rather than sent to the company that happens to be paired now, so the prompt tells the user
    /// what would get them moving again.</summary>
    [ObservableProperty]
    private int _strandedCount;

    /// <summary>True when there's at least one offline-captured receipt waiting for review, so the
    /// prompt only shows when there's something to do.</summary>
    public bool HasPendingOfflineScans => PendingOfflineCount > 0;

    /// <summary>True when at least one capture is waiting on an unpaired company.</summary>
    public bool HasStrandedScans => StrandedCount > 0;

    /// <summary>Whether the outbox prompt shows at all: either something to review, or something
    /// stuck waiting on a company.</summary>
    public bool IsOutboxPromptVisible => HasPendingOfflineScans || HasStrandedScans;

    partial void OnPendingOfflineCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasPendingOfflineScans));
        OnPropertyChanged(nameof(IsOutboxPromptVisible));
    }

    partial void OnStrandedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasStrandedScans));
        OnPropertyChanged(nameof(IsOutboxPromptVisible));
    }

    public CaptureViewModel(ISecureStore secureStore, Func<byte[], Task> onImageCaptured, PendingScanOutbox pendingScanOutbox, PairedCompanyStore pairedCompanyStore, Func<Task> onReviewOfflineScans)
    {
        _secureStore = secureStore ?? throw new ArgumentNullException(nameof(secureStore));
        _onImageCaptured = onImageCaptured ?? throw new ArgumentNullException(nameof(onImageCaptured));
        _pendingScanOutbox = pendingScanOutbox ?? throw new ArgumentNullException(nameof(pendingScanOutbox));
        _pairedCompanyStore = pairedCompanyStore ?? throw new ArgumentNullException(nameof(pairedCompanyStore));
        _onReviewOfflineScans = onReviewOfflineScans ?? throw new ArgumentNullException(nameof(onReviewOfflineScans));
        _ = RefreshScanUsageAsync();
        _ = RefreshOutboxAsync();
    }

    /// <summary>Reloads what the capture outbox is holding: receipts still to review, and receipts
    /// waiting on a company that is no longer paired. Called by ShellViewModel after every snapshot
    /// refresh, after each offline review, and on NavigateCapture so the prompt reflects the queue
    /// whenever the tab is shown.</summary>
    public async Task RefreshOutboxAsync()
    {
        var counts = await _pendingScanOutbox.GetCountsAsync(await GetPairedCompanyUidsAsync());
        PendingOfflineCount = counts.AwaitingReview;
        StrandedCount = counts.Stranded;
    }

    private async Task<IReadOnlyCollection<string>> GetPairedCompanyUidsAsync() =>
        (await _pairedCompanyStore.GetAllAsync()).Select(c => c.CompanyUid).ToList();

    /// <summary>
    /// "Review now" on the offline-capture prompt: if there's a network, hands off to ShellViewModel
    /// to walk the queued receipts through the normal review flow; if still offline, tells the user
    /// they need a connection first (the AI scan can't run without one).
    /// </summary>
    [RelayCommand]
    private async Task ReviewOfflineScansAsync()
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            ConfirmationMessage = "Connect to the internet to review these";
            IsConfirmationVisible = true;
            _ = HideConfirmationAfterDelayAsync();
            return;
        }

        await _onReviewOfflineScans();
    }

    /// <summary>Updates the "Scanning into X" label. Called by ShellViewModel after every
    /// snapshot refresh/company switch.</summary>
    public void SetActiveCompanyLabel(string label) => ActiveCompanyLabel = label;

    /// <summary>Reloads the local scan counter (and <see cref="IsOverLimit"/> with it); called on
    /// every NavigateCapture so a scan recorded while this tab wasn't visible shows up
    /// immediately.</summary>
    public async Task RefreshScanUsageAsync()
    {
        ScansUsedThisMonth = await ScanUsageStore.GetCountAsync(_secureStore);
        IsOverLimit = ScanQuota.IsOverLimit(ScansUsedThisMonth);
    }

    /// <summary>"Upgrade on the web" button on the over-limit prompt: opens the marketing site in
    /// the system browser. No in-app purchase flow exists on the phone.</summary>
    [RelayCommand]
    private async Task UpgradeAsync()
    {
        try
        {
            await Browser.Default.OpenAsync(UpgradeUri, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception)
        {
            // No browser available, or the intent couldn't be resolved - not worth surfacing an
            // error for; the user can still find the upgrade link themselves on the web.
        }
    }

    /// <summary>
    /// Called by ShellViewModel.OnReviewConfirmedAsync right after CaptureDeliveryCoordinator has
    /// tried to deliver the confirmed <paramref name="transaction"/>. Adds a "Recent scans" row and
    /// shows a brief confirmation banner - "sent to your desktop" if it got through, or "waiting to
    /// send" if it didn't (no active company, offline, server error), which is literally what
    /// happens: the reviewed transaction is held in the outbox and re-sent by the background retry,
    /// so nothing the user typed has to be entered again.
    /// </summary>
    public void AddRecentScan(CapturedTransaction transaction, bool delivered)
    {
        if (transaction == null) throw new ArgumentNullException(nameof(transaction));

        var vendor = string.IsNullOrWhiteSpace(transaction.SupplierOrCustomer)
            ? "Unnamed scan"
            : transaction.SupplierOrCustomer;
        var amountText = transaction.Total.ToString("C", CultureInfo.CurrentCulture);
        var timeText = DateTime.Now.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var statusText = delivered ? "Sent to your desktop" : "Waiting to send - it will go on its own";

        RecentScans.Insert(0, new RecentScanViewModel(vendor, amountText, timeText, statusText));
        OnPropertyChanged(nameof(HasRecentScans));

        ConfirmationMessage = delivered
            ? "Added and sent to your desktop"
            : "Added - it will send to your desktop as soon as it can";
        IsConfirmationVisible = true;
        _ = HideConfirmationAfterDelayAsync();
    }

    private async Task HideConfirmationAfterDelayAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        IsConfirmationVisible = false;
    }

    /// <summary>
    /// Shared by both the shutter and "Import from photos" buttons: DocumentScanner's own UI
    /// offers gallery import once SetGalleryImportAllowed is set (see DocumentScanner's doc
    /// comment), so there's no separate gallery-only code path. If there's no network once the
    /// image is cropped, the AI scan can't run yet - the cropped bytes are queued in
    /// <see cref="PendingScanOutbox"/> instead of starting the Scanning screen, and a toast tells
    /// the user it'll scan once they're back online (ShellViewModel drains the queue on the next
    /// connectivity/foreground check).
    /// </summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var imageBytes = await DocumentScanner.ScanAsync();
            if (imageBytes == null || imageBytes.Length == 0)
            {
                // User cancelled, or the scanner/Play Services module isn't available - stay put.
                return;
            }

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                // Bound to the company showing in the "Scanning into" bar, so switching company
                // before reviewing it can't redirect the receipt into the other company's books.
                var active = await _pairedCompanyStore.GetActiveAsync();
                await _pendingScanOutbox.EnqueueAsync(imageBytes, active?.CompanyUid);
                await RefreshOutboxAsync();
                ShowOfflineQueuedMessage();
                return;
            }

            await _onImageCaptured(imageBytes);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowOfflineQueuedMessage()
    {
        ConfirmationMessage = "Saved - review it when you're back online";
        IsConfirmationVisible = true;
        _ = HideConfirmationAfterDelayAsync();
    }
}
