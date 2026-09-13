using ArgoBooks.Mobile.Services;
using ArgoBooks.Mobile.ViewModels;
using ArgoBooks.Mobile.Views;
using ArgoBooks.Shared.Mobile;
using ArgoBooks.Shared.Sync;

namespace ArgoBooks.Mobile;

public partial class App : Application
{
    private static App? _current;

    private ISingleViewApplicationLifetime? _singleView;

    // Avalonia's Android host captures ISingleViewApplicationLifetime.MainView once at startup, so
    // reassigning MainView later is silently ignored (nothing navigates). Instead MainView is set
    // ONCE to this persistent container and every Show* swaps its Content.
    private ContentControl? _rootView;

    // The lock screen is an overlay: swapping MainView back to whatever was showing before the
    // lock (rather than always rebuilding the shell) keeps the shell's navigation state intact
    // across a resume-triggered lock/unlock.
    private Control? _contentBeforeLock;
    private bool _isLockShowing;

    // Kept so MainActivity.OnResume (via NotifyForegrounded) can trigger draining the
    // offline-capture outbox without rebuilding the shell - null until ShowShellAsync completes.
    private ShellViewModel? _shellViewModel;

    // Guards the resume-triggered lock check against the very first OnResume, which Android
    // fires immediately after OnCreate/OnFrameworkInitializationCompleted on cold start - that
    // path is handled by TryResumeSessionAsync itself, so the resume handler no-ops until the
    // initial cold-start decision has actually been made.
    private bool _initialFlowComplete;

    public App()
    {
        _current = this;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Android only uses the single-view application lifetime.
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            _singleView = singleViewPlatform;

            // Set the persistent root ONCE; all later navigation swaps its Content (see _rootView).
            _rootView = new ContentControl
            {
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            };
            _singleView.MainView = _rootView;

            ShowPairing();

            // If a company is already paired (returning user), skip straight to the shell -
            // behind the lock screen first if the biometric app lock is on.
            _ = TryResumeSessionAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Called from MainActivity.OnPause: records when the app went to the background.</summary>
    public static void NotifyBackgrounded()
    {
        if (_current?._initialFlowComplete == true)
        {
            AppLockSettings.RecordBackgrounded();
        }
    }

    /// <summary>
    /// Called from MainActivity.OnResume: shows the lock screen if the grace period has elapsed
    /// (or hasn't started yet) since the app was last backgrounded. No-ops during the initial
    /// cold-start flow (already handled by TryResumeSessionAsync) and while the lock screen is
    /// already showing.
    /// </summary>
    public static void NotifyForegrounded()
    {
        _ = _current?.EvaluateResumeLockAsync();

        // Refresh on foreground so returning to the app picks up new data and detects a desktop-side
        // revocation (which drops back to the pairing screen) without waiting for a manual pull.
        _ = _current?._shellViewModel?.RefreshCommand.ExecuteAsync(null);

        // Refresh the "captured while offline" review prompt as soon as the app comes back
        // to the foreground, rather than waiting for the user to pull-to-refresh. Nothing is
        // auto-posted - the user reviews each queued receipt (see ShellViewModel.StartOfflineReviewAsync).
        _ = _current?._shellViewModel?.RefreshOfflineQueueAsync();
    }

    private async Task EvaluateResumeLockAsync()
    {
        if (!_initialFlowComplete || _isLockShowing)
        {
            return;
        }

        var isEnabled = await AppLockSettings.IsEnabledAsync();
        var pairedCompanyStore = new PairedCompanyStore(new MauiSecureStore());
        var active = await pairedCompanyStore.GetActiveAsync();

        var shouldLock = AppLockPolicy.ShouldLock(
            isEnabled,
            active != null,
            AppLockSettings.LastBackgroundedUtc,
            DateTime.UtcNow,
            AppLockSettings.GracePeriod);

        if (shouldLock)
        {
            Dispatcher.UIThread.Post(ShowLockOverlay);
        }
    }

    private void ShowPairing()
    {
        var pairingViewModel = new PairingViewModel();
        pairingViewModel.Paired += companyLabel => Dispatcher.UIThread.Post(() => { _ = ShowShellAsync(pairingViewModel); });

        _rootView!.Content = new PairingView
        {
            DataContext = pairingViewModel
        };
    }

    private async Task TryResumeSessionAsync()
    {
        var pairedCompanyStore = new PairedCompanyStore(new MauiSecureStore());
        var active = await pairedCompanyStore.GetActiveAsync();
        if (active != null)
        {
            if (await AppLockSettings.IsEnabledAsync())
            {
                ShowLockThenShell();
            }
            else
            {
                await ShowShellAsync();
            }
        }

        _initialFlowComplete = true;
    }

    /// <summary>Shows the lock screen first (cold start); proceeds to the shell once unlocked.</summary>
    private void ShowLockThenShell()
    {
        _isLockShowing = true;
        _contentBeforeLock = null;

        var lockViewModel = new LockViewModel(new AppLockService());
        lockViewModel.Unlocked += () => Dispatcher.UIThread.Post(() =>
        {
            AppLockSettings.RecordUnlocked();
            _isLockShowing = false;
            _ = ShowShellAsync();
        });

        Dispatcher.UIThread.Post(() =>
        {
            _rootView!.Content = new LockView { DataContext = lockViewModel };
        });
    }

    /// <summary>Overlays the lock screen on resume; restores the prior content once unlocked.</summary>
    private void ShowLockOverlay()
    {
        _isLockShowing = true;
        _contentBeforeLock = _rootView!.Content as Control;

        var lockViewModel = new LockViewModel(new AppLockService());
        lockViewModel.Unlocked += () => Dispatcher.UIThread.Post(() =>
        {
            AppLockSettings.RecordUnlocked();
            _isLockShowing = false;
            _rootView!.Content = _contentBeforeLock ?? _rootView.Content;
            _contentBeforeLock = null;
        });

        _rootView!.Content = new LockView { DataContext = lockViewModel };
    }

    private async Task ShowShellAsync(PairingViewModel? pairingViewModel = null)
    {
        ShellViewModel shellViewModel;
        try
        {
            var client = new MobileSyncClient(null, MobileApiConfig.BaseUrl);
            var secureStore = new MauiSecureStore();
            var pairedCompanyStore = new PairedCompanyStore(secureStore);
            var cache = new FileSnapshotCache(FileSystem.Current.AppDataDirectory);
            var snapshotStore = new SnapshotStore(client, pairedCompanyStore, cache);
            var deviceApiAuth = await DeviceApiAuth.CreateAsync(secureStore);
            var pendingScanOutbox = new PendingScanOutbox(new FilePendingScanStorage(FileSystem.Current.AppDataDirectory));

            shellViewModel = new ShellViewModel(snapshotStore, pairedCompanyStore, secureStore, deviceApiAuth, client, pendingScanOutbox);
        }
        catch (Exception ex)
        {
            // Building the shell (secure-store reads, app-data paths, sync client) can fail. Without
            // this guard the fire-and-forget caller would swallow it, stranding the user on the
            // pairing screen's "Connected" label with no feedback. Log it and, on the pairing path,
            // surface it so the failure is visible instead of a dead screen.
            Android.Util.Log.Error("ArgoBooks", $"Shell setup failed: {ex}");
            if (pairingViewModel != null)
                Dispatcher.UIThread.Post(() => pairingViewModel.ReportShellOpenFailed(ex.Message));
            return;
        }

        _shellViewModel = shellViewModel;

        // Unpairing the last remaining company (Settings > Unpair this phone) drops back to the
        // full pairing screen, same as a fresh install.
        shellViewModel.RequestPairing += () => Dispatcher.UIThread.Post(() =>
        {
            _shellViewModel = null;
            ShowPairing();
        });

        // Navigate to the shell first so a successful pairing always lands on the dashboard. The
        // shell has its own loading / "waiting for first sync" states, so the initial snapshot load
        // running (or failing) after this doesn't strand the user on the pairing screen with a
        // "Connected" label.
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _rootView!.Content = new ShellView
                {
                    DataContext = shellViewModel
                };
            }
            catch (Exception ex)
            {
                // A binding/resource error while building ShellView would otherwise be swallowed by
                // the dispatcher, leaving the user on the pairing screen's "Connected" label.
                Android.Util.Log.Error("ArgoBooks", $"Shell view creation failed: {ex}");
                pairingViewModel?.ReportShellOpenFailed(ex.Message);
            }
        });

        try
        {
            await shellViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("ArgoBooks", $"Shell initialize failed: {ex}");
        }
    }
}
