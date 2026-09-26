using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Layout;
using ArgoBooks.Core.Services.Sync;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using ArgoBooks.ViewModels;
using ArgoBooks.Views;

namespace ArgoBooks;

public partial class App : Application
{
    /// <summary>
    /// Gets or sets the update service. Set by the platform-specific host (e.g., Desktop)
    /// before the application starts. Null on platforms that don't support auto-update.
    /// </summary>
    public static IUpdateService? UpdateService { get; set; }

    /// <summary>
    /// Builds the platform's update service. Set by the platform entry point, which cannot
    /// hand over an instance without paying for it before Avalonia has started: constructing
    /// NetSparkle loads its assemblies and signature crypto, and that landed squarely in the
    /// window where the screen is still empty. Nothing reads UpdateService until the shell
    /// view model is built, long after the splash is up.
    /// </summary>
    public static Func<IUpdateService?>? UpdateServiceFactory { get; set; }

    /// <summary>
    /// Gets the navigation service instance.
    /// </summary>
    public static NavigationService? NavigationService { get; private set; }

    /// <summary>
    /// Gets the company manager instance.
    /// </summary>
    public static CompanyManager? CompanyManager { get; private set; }

    /// <summary>
    /// Test-only hook to inject an in-memory <see cref="Core.Services.CompanyManager"/> so unit tests
    /// can drive ViewModels that read <c>App.CompanyManager.CompanyData</c>. Not used in production.
    /// </summary>
    internal static void SetCompanyManagerForTesting(CompanyManager? manager) => CompanyManager = manager;
    internal static void SetPaymentPortalServiceForTesting(PaymentPortalService? service) => PaymentPortalService = service;

    /// <summary>
    /// Gets the global settings service instance.
    /// </summary>
    public static GlobalSettingsService? SettingsService { get; private set; }

    /// <summary>
    /// Gets the file service instance for sample company creation.
    /// </summary>
    private static FileService? _fileService;

    /// <summary>
    /// Gets the payment portal service instance for online payment integration.
    /// </summary>
    public static PaymentPortalService? PaymentPortalService { get; private set; }

    /// <summary>
    /// Pushes locally-recorded invoice payments up to the portal, so the server
    /// stops treating cash-paid invoices as unpaid and chasing the customer.
    /// </summary>
    public static PortalBalanceSyncService? PortalBalanceSyncService { get; private set; }

    /// <summary>
    /// Gets the mobile-sync service instance used to upload company snapshots and pull
    /// the capture queue from the phone-pairing backend.
    /// </summary>
    public static SyncService? SyncService { get; private set; }

    /// <summary>
    /// Client for the portal refund + email-verification + email-change endpoints.
    /// Created once at startup; reads the active per-company API key on each call.
    /// </summary>
    public static RefundService? RefundService { get; private set; }

    /// <summary>
    /// The long-lived <see cref="HttpClient"/> shared across services created at startup
    /// (RefundService, SyncService, telemetry, source-survey). Reuse this instead of
    /// creating a new HttpClient per call.
    /// </summary>
    public static HttpClient? SharedHttpClient { get; private set; }

    /// <summary>
    /// Coordinator for the refund / email-verify / email-change modals.
    /// Hosted at AppShell level so the ModalOverlay can dim the whole window.
    /// </summary>
    public static RefundModalsViewModel? RefundModalsViewModel => _appShellViewModel?.RefundModalsViewModel;

    /// <summary>
    /// The Company-details editor. Exposed so the portal email sync can tell
    /// whether the owner email is being hand-edited and avoid clobbering an
    /// in-flight edit.
    /// </summary>
    public static EditCompanyModalViewModel? EditCompanyModalViewModel => _appShellViewModel?.EditCompanyModalViewModel;

    /// <summary>
    /// Gets the invoice usage service for tracking free-tier send limits.
    /// </summary>
    public static UsageLimitService? InvoiceUsageService { get; private set; }

    /// <summary>
    /// Gets the license service instance for secure license storage.
    /// </summary>
    public static LicenseService? LicenseService { get; private set; }

    /// <summary>
    /// Gets the error logger instance for centralized error logging.
    /// </summary>
    public static IErrorLogger? ErrorLogger { get; private set; }

    /// <summary>
    /// Gets the telemetry manager instance for anonymous usage tracking.
    /// </summary>
    public static ITelemetryManager? TelemetryManager { get; private set; }

    /// <summary>
    /// Gets the reporter used by the post-onboarding source survey overlay.
    /// </summary>
    public static SourceSurveyReporter? SourceSurveyReporter { get; private set; }

    /// <summary>
    /// Gets the service that fetches the source survey's answer options from the
    /// website, so new options appear without an app release.
    /// </summary>
    public static SourceSurveyOptionsService? SourceSurveyOptionsService { get; private set; }

    /// <summary>
    /// Gets the shared undo/redo manager instance.
    /// </summary>
    public static UndoRedoManager UndoRedoManager => HeaderViewModel.SharedUndoRedoManager;

    /// <summary>
    /// Gets the customer modals view model for shared access.
    /// </summary>
    public static CustomerModalsViewModel? CustomerModalsViewModel => _appShellViewModel?.CustomerModalsViewModel;

    /// <summary>
    /// Gets the product modals view model for shared access.
    /// </summary>
    public static ProductModalsViewModel? ProductModalsViewModel => _appShellViewModel?.ProductModalsViewModel;

    /// <summary>
    /// Gets the category modals view model for shared access.
    /// </summary>
    public static CategoryModalsViewModel? CategoryModalsViewModel => _appShellViewModel?.CategoryModalsViewModel;

    /// <summary>
    /// Gets the supplier modals view model for shared access.
    /// </summary>
    public static SupplierModalsViewModel? SupplierModalsViewModel => _appShellViewModel?.SupplierModalsViewModel;

    /// <summary>
    /// Gets the rental inventory modals view model for shared access.
    /// </summary>
    public static RentalInventoryModalsViewModel? RentalInventoryModalsViewModel => _appShellViewModel?.RentalInventoryModalsViewModel;

    /// <summary>
    /// Gets the rental availability modal view model for shared access (per-item calendar).
    /// </summary>
    public static RentalAvailabilityModalViewModel? RentalAvailabilityModalViewModel => _appShellViewModel?.RentalAvailabilityModalViewModel;

    /// <summary>
    /// Gets the rental records modals view model for shared access.
    /// </summary>
    public static RentalRecordsModalsViewModel? RentalRecordsModalsViewModel => _appShellViewModel?.RentalRecordsModalsViewModel;

    /// <summary>
    /// Gets the payment modals view model for shared access.
    /// </summary>
    public static PaymentModalsViewModel? PaymentModalsViewModel => _appShellViewModel?.PaymentModalsViewModel;

    /// <summary>
    /// Gets the invoice modals view model for shared access.
    /// </summary>
    public static InvoiceModalsViewModel? InvoiceModalsViewModel => _appShellViewModel?.InvoiceModalsViewModel;

    /// <summary>
    /// Gets the quotes modals view model for shared access.
    /// </summary>
    public static QuotesModalsViewModel? QuotesModalsViewModel => _appShellViewModel?.QuotesModalsViewModel;

    /// <summary>
    /// Gets the invoice template designer view model for shared access.
    /// </summary>
    public static InvoiceTemplateDesignerViewModel? InvoiceTemplateDesignerViewModel => _appShellViewModel?.InvoiceTemplateDesignerViewModel;

    /// <summary>
    /// Gets the expense modals view model for shared access.
    /// </summary>
    public static ExpenseModalsViewModel? ExpenseModalsViewModel => _appShellViewModel?.ExpenseModalsViewModel;

    /// <summary>
    /// Gets the revenue modals view model for shared access.
    /// </summary>
    public static RevenueModalsViewModel? RevenueModalsViewModel => _appShellViewModel?.RevenueModalsViewModel;

    /// <summary>
    /// Gets the stock levels modals view model for shared access.
    /// </summary>
    public static StockLevelsModalsViewModel? StockLevelsModalsViewModel => _appShellViewModel?.StockLevelsModalsViewModel;

    /// <summary>
    /// Gets the locations modals view model for shared access.
    /// </summary>
    public static LocationsModalsViewModel? LocationsModalsViewModel => _appShellViewModel?.LocationsModalsViewModel;
    public static PayrollModalsViewModel? PayrollModalsViewModel => _appShellViewModel?.PayrollModalsViewModel;

    public static PayRunModalsViewModel? PayRunModalsViewModel => _appShellViewModel?.PayRunModalsViewModel;

    public static YearEndModalViewModel? YearEndModalViewModel => _appShellViewModel?.YearEndModalViewModel;
    public static SendToAccountantModalViewModel? SendToAccountantModalViewModel => _appShellViewModel?.SendToAccountantModalViewModel;

    /// <summary>
    /// Gets the stock adjustments modals view model for shared access.
    /// </summary>
    public static StockAdjustmentsModalsViewModel? StockAdjustmentsModalsViewModel => _appShellViewModel?.StockAdjustmentsModalsViewModel;

    public static BankMatchingModalsViewModel? BankMatchingModalsViewModel => _appShellViewModel?.BankMatchingModalsViewModel;

    /// <summary>
    /// Gets the bank statement import modal view model for shared access.
    /// </summary>
    public static BankStatementImportModalViewModel? BankStatementImportModalViewModel => _appShellViewModel?.BankStatementImportModalViewModel;

    /// <summary>
    /// Gets the import format picker view model for shared access.
    /// </summary>
    public static ImportModalViewModel? ImportModalViewModel => _appShellViewModel?.ImportModalViewModel;

    /// <summary>
    /// Gets the product-update sign-up modal for shared access.
    /// </summary>
    public static UpdateEmailModalViewModel? UpdateEmailModalViewModel => _appShellViewModel?.UpdateEmailModalViewModel;

    /// <summary>
    /// Re-applies the optional sidebar sections after the feature toggles or the industry change.
    /// </summary>
    public static void ApplyFeatureVisibility(CompanySettings? settings) =>
        _appShellViewModel?.ApplyFeatureVisibility(settings);

    /// <summary>
    /// Gets the purchase orders modals view model for shared access.
    /// </summary>
    public static PurchaseOrdersModalsViewModel? PurchaseOrdersModalsViewModel => _appShellViewModel?.PurchaseOrdersModalsViewModel;

    /// <summary>
    /// Gets the receipts modals view model for shared access.
    /// </summary>
    public static ReceiptsModalsViewModel? ReceiptsModalsViewModel => _appShellViewModel?.ReceiptsModalsViewModel;

    /// <summary>
    /// Gets the lost/damaged modals view model for shared access.
    /// </summary>
    public static LostDamagedModalsViewModel? LostDamagedModalsViewModel => _appShellViewModel?.LostDamagedModalsViewModel;

    /// <summary>
    /// Gets the returns modals view model for shared access.
    /// </summary>
    public static ReturnsModalsViewModel? ReturnsModalsViewModel => _appShellViewModel?.ReturnsModalsViewModel;

    /// <summary>
    /// Gets the report modals view model for shared access.
    /// </summary>
    public static ReportModalsViewModel? ReportModalsViewModel => _appShellViewModel?.ReportModalsViewModel;

    /// <summary>
    /// Gets the prediction info modal view model for shared access.
    /// </summary>
    public static PredictionInfoModalViewModel? PredictionInfoModalViewModel => _appShellViewModel?.PredictionInfoModalViewModel;

    /// <summary>
    /// Gets the past predictions modal view model for shared access.
    /// </summary>
    public static PastPredictionsModalViewModel? PastPredictionsModalViewModel => _appShellViewModel?.PastPredictionsModalViewModel;

    /// <summary>
    /// Gets the settings modal view model for shared access.
    /// </summary>
    public static SettingsModalViewModel? SettingsModalViewModel => _appShellViewModel?.SettingsModalViewModel;

    /// <summary>
    /// Gets the header view model for shared access.
    /// </summary>
    public static HeaderViewModel? HeaderViewModel => _appShellViewModel?.HeaderViewModel;

    /// <summary>
    /// Gets the version history modal view model for shared access.
    /// </summary>
    public static VersionHistoryModalViewModel? VersionHistoryModalViewModel => _appShellViewModel?.VersionHistoryModalViewModel;

    /// <summary>
    /// Gets the event log service for version history tracking.
    /// </summary>
    public static EventLogService? EventLogService { get; private set; }

    /// <summary>
    /// Gets the categories tutorial view model for first-visit tutorial.
    /// </summary>
    public static PageTutorialViewModel? CategoriesTutorialViewModel => _mainWindowViewModel?.CategoriesTutorialViewModel;

    /// <summary>
    /// Gets the products tutorial view model for first-visit tutorial.
    /// </summary>
    public static PageTutorialViewModel? ProductsTutorialViewModel => _mainWindowViewModel?.ProductsTutorialViewModel;

    /// <summary>
    /// Adds a notification to the notification panel.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="type">The notification type.</param>
    /// <param name="onClick">Runs when the notification is clicked, if given.</param>
    public static void AddNotification(string title, string message, NotificationType type = NotificationType.Info,
        Action? onClick = null)
    {
        _appShellViewModel?.AddNotification(title, message, type, onClick);
    }

    /// <summary>Navigates to the Invoices page and selects its Recurring tab (tab index 3).</summary>
    public static void OpenRecurringInvoicesTab()
    {
        NavigationService?.NavigateTo("Invoices", new Dictionary<string, object?> { ["selectedTabIndex"] = 3 });
    }

    /// <summary>Shows an error notice with an OK button.</summary>
    internal static Task ShowErrorDialogAsync(string title, string message) =>
        ShowNoticeAsync(DialogIcon.Error, title, message);

    /// <summary>Shows an information notice with an OK button.</summary>
    internal static Task ShowInfoDialogAsync(string title, string message) =>
        ShowNoticeAsync(DialogIcon.Info, title, message);

    /// <summary>Shows a warning notice with an OK button.</summary>
    public static Task ShowWarningDialogAsync(string title, string message) =>
        ShowNoticeAsync(DialogIcon.Warning, title, message);

    private static async Task ShowNoticeAsync(DialogIcon icon, string title, string message)
    {
        if (ConfirmationDialog is { } dialog)
            await dialog.ShowNoticeAsync(icon, title, message);
    }

    /// <summary>
    /// Asks the user to confirm, returning false when the dialog cannot be shown so a caller
    /// never takes an irreversible action unasked.
    /// </summary>
    internal static async Task<bool> ConfirmDialogAsync(
        string title, string message, string confirmText, string cancelText)
    {
        if (ConfirmationDialog is not { } dialog)
            return false;

        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = title,
            Message = message,
            PrimaryButtonText = confirmText,
            CancelButtonText = cancelText
        });
        return result == ConfirmationResult.Primary;
    }

    /// <summary>
    /// Shows the one, consistent connectivity-error dialog (same title, body, and style)
    /// used everywhere an online action fails because the device is offline or the server
    /// is unreachable. A warning rather than an error, since being offline is a normal,
    /// recoverable situation.
    /// </summary>
    public static Task ShowConnectivityErrorAsync(string? message = null) =>
        ShowWarningDialogAsync(
            ConnectivityMessage.Title.Translate(),
            // Localize the known connectivity constants (they're translation keys) instead of showing
            // them verbatim; leave any other caller-supplied message untouched.
            string.IsNullOrWhiteSpace(message)
                ? ConnectivityMessage.NoInternet.Translate()
                : (ConnectivityMessage.IsConnectivityMessage(message) ? message.Translate() : message));

    /// <summary>
    /// Shows the global indeterminate loading overlay. Exposed so view models that can't reach the
    /// main window view model directly (e.g. modal VMs) can give instant feedback during slow work.
    /// </summary>
    internal static void ShowBusyOverlay(string message) => _mainWindowViewModel?.ShowLoading(message);

    /// <summary>Hides the global loading overlay shown by <see cref="ShowBusyOverlay"/>.</summary>
    internal static void HideBusyOverlay() => _mainWindowViewModel?.HideLoading();


    /// <summary>
    /// Puts text on the clipboard from the main window's top level.
    ///
    /// Used for values a message box shows but the user cannot select out of it, an
    /// API secret above all: it is shown exactly once and never recoverable, so
    /// telling someone to retype it from a dialog is not an option.
    /// </summary>
    internal static async Task<bool> CopyToClipboardAsync(string text)
    {
        try
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { } window
                && TopLevel.GetTopLevel(window)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                return true;
            }
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogWarning($"Copying to the clipboard failed: {ex.Message}", "Clipboard");
        }

        return false;
    }


    private static int _isAutoSyncing;
    private static Timer? _portalSyncTimer;
    private static int _portalSyncTimerMinutes;

    /// <summary>
    /// Runs the periodic portal sync at the open company's chosen interval, or not at all for
    /// "Manual" (0). A timer already running at that interval is left alone, so saving other
    /// settings doesn't push the next sync back.
    /// </summary>
    internal static void ApplyPortalSyncInterval()
    {
        var minutes = CompanyManager?.IsCompanyOpen == true
            ? CompanyManager.CompanyData?.Settings.PaymentPortal.AutoSyncIntervalMinutes ?? 0
            : 0;
        if (_portalSyncTimer != null && minutes == _portalSyncTimerMinutes) return;

        StopPortalSyncTimer();
        if (minutes <= 0) return;

        // Run the sync on the UI thread, not the timer's: it adds payments, bumps the payment id
        // counter, updates invoices and saves, all of which the UI thread does too.
        var interval = TimeSpan.FromMinutes(minutes);
        _portalSyncTimer = new Timer(
            state => Avalonia.Threading.Dispatcher.UIThread.Post(() => { _ = AutoSyncPortalPaymentsAsync(); }),
            null, interval, interval);
        _portalSyncTimerMinutes = minutes;
    }

    private static void StopPortalSyncTimer()
    {
        _portalSyncTimer?.Dispose();
        _portalSyncTimer = null;
        _portalSyncTimerMinutes = 0;
    }

    /// <summary>
    /// Auto-syncs online payments from the portal so invoice statuses stay up-to-date.
    /// Safe to call from multiple places, concurrent calls are deduplicated.
    /// </summary>
    internal static async Task AutoSyncPortalPaymentsAsync()
    {
        if (Interlocked.CompareExchange(ref _isAutoSyncing, 1, 0) != 0) return;
        try
        {
            var portalService = PaymentPortalService;
            var companyData = CompanyManager?.CompanyData;
            if (portalService == null || companyData == null || !PortalSettings.IsConfigured)
                return;

            var portalSettings = companyData.Settings.PaymentPortal;

            // The replies describe the company whose key asked. Opening another swaps both the key
            // and CompanyData, and nothing from these replies may then land in it.
            bool CompanyChanged() => !ReferenceEquals(CompanyManager?.CompanyData, companyData);

            // Use force sync to also recover any payments that were previously
            // confirmed on the server but never saved locally (e.g. due to app crash).
            // Duplicate prevention in ProcessSyncedPayments handles efficiency.
            var syncResponse = await portalService.SyncPaymentsAsync(since: null, force: true);

            if (!syncResponse.Success || CompanyChanged())
                return;

            // Always advance the sync timestamp on success to avoid re-querying the same window
            portalSettings.LastSyncTime = syncResponse.SyncTimestamp ?? DateTime.UtcNow;

            // Sweep locally-recorded balances up to the server while we already
            // have it on the line, so payments taken in cash (or on another
            // machine, or while this one was offline) stop the reminder cron
            // chasing an invoice that is already settled.
            //
            // Deliberately ABOVE the no-new-payments return below: having
            // nothing to pull down is the common case, and it says nothing
            // about whether we have something to push up.
            if (PortalBalanceSyncService != null)
            {
                await PortalBalanceSyncService.ReconcileAsync();
                if (CompanyChanged()) return;
            }

            if (syncResponse.Payments.Count == 0)
                return;

            var syncResult = PaymentPortalService.ProcessSyncedPayments(
                syncResponse.Payments, companyData);
            var newPayments = syncResult.NewPayments;

            // Only confirm payments that were actually processed into local records.
            // Unprocessed payments (e.g. invoice not found locally) must NOT be
            // confirmed so the server returns them on the next sync attempt.
            var processedPortalIds = newPayments
                .Where(p => p.PortalPaymentId != null)
                .Select(p => int.Parse(p.PortalPaymentId!))
                .ToList();
            if (processedPortalIds.Count > 0)
            {
                await portalService.ConfirmSyncAsync(processedPortalIds);
                if (CompanyChanged()) return;
            }

            // Persist when there are new rows OR existing rows were backfilled
            // with previously-missing fields (e.g. ProcessingFee on pre-fix
            // payments). Without the backfill arm, the in-memory update gets
            // lost on next app launch and the fee disappears again.
            if (newPayments.Count > 0 || syncResult.BackfilledRows > 0)
            {
                // Only auto-persist when the user has no unsaved edits. The sync flow never flags the
                // company dirty (adding payments and setting LastSyncTime don't call MarkAsModified),
                // so this reflects ONLY the user's own edits - including any made while the sync was in
                // flight. If they have edits, the synced payments stay in memory and persist on the
                // next explicit save (or are re-fetched next open via the force sync), so a background
                // sync can't quietly commit the user's in-progress edits.
                if (!CompanyManager!.HasUnsavedChanges)
                {
                    try { await CompanyManager.SavePaymentSyncAsync(companyData); }
                    catch (Exception ex)
                    {
                        ErrorLogger?.LogWarning($"Failed to persist synced payments: {ex.Message}", "PortalSync");
                    }

                    if (CompanyChanged()) return;
                }

                // Refresh any already-instantiated page ViewModels so the UI reflects the new data
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _invoicesPageViewModel?.RefreshInvoicesCommand.Execute(null);
                    _revenuePageViewModel?.RefreshRevenueCommand.Execute(null);

                    // Send "Payment Received" notification if enabled. Skipped
                    // for the backfill-only path (no new payments), there's
                    // nothing the user just received to be notified about.
                    if (newPayments.Count > 0 && companyData.Settings.PaymentPortal.NotifyOnPayment)
                    {
                        var total = CurrencyService.FormatSumDisplayFromUSD(
                            newPayments, p => p.Amount, p => p.OriginalCurrency, p => p.EffectiveAmountUSD, p => p.Date);
                        var message = newPayments.Count == 1
                            ? "{0} online payment received ({1})".TranslateFormat(newPayments.Count, total)
                            : "{0} online payments received ({1})".TranslateFormat(newPayments.Count, total);

                        AddNotification(
                            "Payment Received".Translate(),
                            message,
                            NotificationType.Success);
                    }
                });
            }
        }
        catch
        {
            // Auto-sync failures are non-critical; silently ignore
        }
        finally
        {
            Interlocked.Exchange(ref _isAutoSyncing, 0);
        }

        // After the payments, on the same trigger and the same connection: an accepted quote is
        // as time-sensitive as a paid invoice, and nothing else pulls it. Outside the guard above
        // so a payment sync that bailed early still lets the quote answers through.
        await AutoSyncPortalQuoteResponsesAsync();
    }

    /// <summary>
    /// Pulls the accept / decline answers customers left on the portal and applies them to the
    /// local quotes.
    /// </summary>
    private static int _isSyncingQuotes;

    private static async Task AutoSyncPortalQuoteResponsesAsync()
    {
        // Its own guard: this runs outside the payment sync's, and two passes over the same
        // answers would apply them twice and notify twice.
        if (Interlocked.CompareExchange(ref _isSyncingQuotes, 1, 0) != 0)
            return;

        try
        {
            var portalService = PaymentPortalService;
            var companyData = CompanyManager?.CompanyData;
            if (portalService == null || companyData == null || !PortalSettings.IsConfigured)
                return;

            // The replies describe the company whose key asked. Opening another swaps both the key
            // and CompanyData, and nothing from these replies may then land in it.
            bool CompanyChanged() => !ReferenceEquals(CompanyManager?.CompanyData, companyData);

            var response = await portalService.SyncQuoteResponsesAsync();
            if (!response.Success || response.Quotes.Count == 0 || CompanyChanged())
                return;

            var (settledIds, localIds, applied) = PaymentPortalService.ApplyQuoteResponses(response.Quotes, companyData);

            // No local quote to lose, so confirming costs nothing, and leaving them unconfirmed
            // brings a quote deleted here back on every sync for ever.
            if (settledIds.Count > 0)
            {
                await portalService.ConfirmQuoteSyncAsync(settledIds);
                if (CompanyChanged()) return;
            }

            if (localIds.Count == 0) return;

            // Same rule as the payment sync: only auto-persist when the user has no edits of their
            // own in flight, so a background sync can't commit their half-finished work.
            var persisted = false;
            if (!CompanyManager!.HasUnsavedChanges)
            {
                try
                {
                    await CompanyManager.SavePaymentSyncAsync(companyData);
                    persisted = true;
                }
                catch (Exception ex)
                {
                    ErrorLogger?.LogWarning($"Failed to persist synced quote responses: {ex.Message}", "PortalSync");
                }

                if (CompanyChanged()) return;
            }

            // Only once the answer is in the file. Confirming an answer that is still only in
            // memory would lose it outright if the app closed without saving: the server drops it
            // on confirm and never offers it again. Unconfirmed, it simply arrives again next pass.
            if (persisted)
            {
                await portalService.ConfirmQuoteSyncAsync(localIds);
                if (CompanyChanged()) return;
            }

            // Only what this pass changed. A repeat pass over answers that could not be confirmed
            // yet has already been announced.
            if (applied == 0) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _quotesPageViewModel?.RefreshQuotesCommand.Execute(null);

                var message = applied == 1
                    ? "{0} quote was answered by a customer.".TranslateFormat(applied)
                    : "{0} quotes were answered by customers.".TranslateFormat(applied);

                AddNotification("Quote response".Translate(), message, NotificationType.Info);
            });
        }
        catch
        {
            // Auto-sync failures are non-critical; silently ignore
        }
        finally
        {
            Interlocked.Exchange(ref _isSyncingQuotes, 0);
        }
    }

    private static int _isMobileSyncing;

    /// <summary>
    /// Auto-syncs with the paired mobile app: uploads a fresh company snapshot so the phone
    /// has current data, then pulls any receipts/expenses the phone captured while offline
    /// and ingests them into the local company. Safe to call from multiple places, concurrent
    /// calls are deduplicated.
    /// </summary>
    internal static async Task AutoMobileSyncAsync()
    {
        if (Interlocked.CompareExchange(ref _isMobileSyncing, 1, 0) != 0) return;
        try
        {
            var syncService = SyncService;
            var companyData = CompanyManager?.CompanyData;
            if (syncService == null || companyData == null || !companyData.Settings.MobileSync.IsConfigured)
                return;

            var mobileSync = companyData.Settings.MobileSync;
            var companyUid = mobileSync.CompanyUid!;
            var syncKey = mobileSync.SyncKeyBase64!;
            var ct = CancellationToken.None;

            // Opening another company replaces CompanyData. Captures applied to the one that was
            // closed are never saved, so they must stay in the queue for its next sync.
            bool CompanyChanged() => !ReferenceEquals(CompanyManager?.CompanyData, companyData);

            // UPLOAD: push a fresh snapshot so the phone always has current data to browse offline.
            // Wrapped in its own try/catch so an upload/network failure doesn't skip the pull/ingest
            // below - the two directions are independent and one failing shouldn't block the other.
            try
            {
                var snap = SnapshotBuilder.Build(companyData);
                var bytes = SnapshotBuilder.Serialize(snap);
                var cipher = SyncCrypto.Encrypt(bytes, syncKey);
                await syncService.UploadSnapshotAsync(companyUid, cipher, ct);
            }
            catch (Exception ex)
            {
                ErrorLogger?.LogWarning($"Failed to upload mobile-sync snapshot: {ex.Message}", "MobileSync");
            }

            if (CompanyChanged()) return;

            // PULL + INGEST: drain the phone's capture queue into local Expenses/Revenue/Receipts.
            // CaptureIngestService de-dupes on CapturedTransaction.ScanUid via the persisted
            // CompanyData.IngestedScanUids list, so a re-delivered-but-still-pending item is
            // safely skipped even across app restarts (not just within this session).
            var items = await syncService.PullQueueAsync(companyUid, ct);
            if (CompanyChanged()) return;

            var malformedIds = new List<int>();
            var ingestedIds = new List<int>();
            var duplicateIds = new List<int>();
            foreach (var item in items)
            {
                try
                {
                    var plain = SyncCrypto.Decrypt(item.Ciphertext, syncKey);
                    var tx = System.Text.Json.JsonSerializer.Deserialize<CapturedTransaction>(plain);
                    var newId = CaptureIngestService.Ingest(companyData, tx!);
                    if (newId == null)
                        // Already ingested in a prior cycle: nothing new to add, but see below for
                        // when it is safe to ack.
                        duplicateIds.Add(item.Id);
                    else
                        ingestedIds.Add(item.Id);
                }
                catch (Exception ex)
                {
                    // A permanently-malformed item (bad ciphertext, unparsable JSON, invalid
                    // transaction) must still be acked so it stops being re-delivered forever.
                    // A transient failure (e.g. this process crashing mid-loop) simply re-appears
                    // on the next pull since it was never acked.
                    ErrorLogger?.LogError(ex, ErrorCategory.Parsing, "MobileSync: failed to ingest queue item");
                    malformedIds.Add(item.Id);
                }
            }

            // PERSIST before ACK: an item that added new data must never be acknowledged (and thus
            // deleted server-side) before that data is actually saved locally. If the save is skipped
            // (the user has unsaved edits in memory), writes nothing or throws, the newly-ingested items must NOT be
            // acked - they stay in the server queue and are re-delivered each cycle, where
            // CaptureIngestService's ScanUid check no-ops them, until a save has written them.
            var saved = false;
            if (ingestedIds.Count > 0 && CompanyManager != null)
            {
                if (!CompanyManager.HasUnsavedChanges)
                {
                    try
                    {
                        saved = await CompanyManager.SavePaymentSyncAsync(companyData);
                    }
                    catch (Exception ex)
                    {
                        ErrorLogger?.LogWarning($"Failed to persist mobile-sync captures: {ex.Message}", "MobileSync");
                    }
                }

                // Left in memory, the captures go out with the user's next save, so the file must read
                // as changed: that is what makes quitting ask, and what the duplicate check below reads.
                if (!saved)
                    companyData.MarkAsModified();
            }

            // A duplicate is only known to be a duplicate from its ScanUid in memory, which may come
            // from an earlier cycle whose save was skipped. Acking deletes the server's copy, so wait
            // until nothing is unsaved: from then on the capture is in the file.
            var toAck = new List<int>(malformedIds);
            if (saved || !companyData.ChangesMade)
            {
                toAck.AddRange(duplicateIds);
            }
            if (saved)
            {
                toAck.AddRange(ingestedIds);
            }

            if (toAck.Count > 0)
            {
                await syncService.AckQueueAsync(companyUid, toAck, ct);
            }

            mobileSync.LastSyncTime = DateTime.UtcNow;

            var ingestedCount = ingestedIds.Count;
            if (ingestedCount > 0 && !CompanyChanged())
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!saved)
                        CompanyManager?.MarkAsChanged();

                    _receiptsPageViewModel?.RefreshReceiptsCommand.Execute(null);
                    _expensesPageViewModel?.RefreshExpensesCommand.Execute(null);
                    _revenuePageViewModel?.RefreshRevenueCommand.Execute(null);

                    if (mobileSync.NotifyOnCapture)
                    {
                        AddNotification(
                            "Receipts from your phone".Translate(),
                            "{0} receipts added from your phone".TranslateFormat(ingestedCount),
                            NotificationType.Success);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            // Mobile-sync failures are non-critical; log and continue.
            ErrorLogger?.LogWarning($"Mobile sync failed: {ex.Message}", "MobileSync");
        }
        finally
        {
            Interlocked.Exchange(ref _isMobileSyncing, 0);
        }
    }

    /// <summary>
    /// Checks for low stock items, out of stock items, overdue invoices, and overdue rentals,
    /// and sends notifications if enabled. Only sends once per day to avoid duplicates.
    /// </summary>
    private static void CheckAndSendNotifications()
    {
        var companyData = CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Never for the sample company. Its data is time-shifted to look recent on every open, so
        // its invoices and rentals are permanently "overdue" and its stock permanently low, which
        // means someone opening the sample to look around is greeted by a stack of warnings about
        // a business that does not exist. Gated here rather than at each alert because this is the
        // one entry point for all of them, and the notifications a user's own action produces
        // ("Password has been set") should still appear.
        if (CompanyManager?.IsSampleCompany == true)
            return;

        var settings = companyData.Settings.Notifications;

        // Only send startup notifications once per day to avoid duplicates
        var today = DateTime.Today;
        if (settings.LastAlertCheckDate.HasValue && settings.LastAlertCheckDate.Value.Date == today)
            return;

        // Update the last check date
        settings.LastAlertCheckDate = today;

        // Check for low stock items
        if (settings.LowStockAlert)
        {
            var lowStockItems = companyData.Inventory
                .Where(item => item.CalculateStatus() == InventoryStatus.LowStock)
                .ToList();

            if (lowStockItems.Count > 0)
            {
                var message = lowStockItems.Count == 1
                    ? "1 item is running low on stock.".Translate()
                    : "{0} items are running low on stock.".TranslateFormat(lowStockItems.Count);

                AddNotification(
                    "Low Stock Alert".Translate(),
                    message,
                    NotificationType.Warning);
            }
        }

        // Check for out of stock items
        if (settings.OutOfStockAlert)
        {
            var outOfStockItems = companyData.Inventory
                .Where(item => item.CalculateStatus() == InventoryStatus.OutOfStock)
                .ToList();

            if (outOfStockItems.Count > 0)
            {
                var message = outOfStockItems.Count == 1
                    ? "1 item is out of stock.".Translate()
                    : "{0} items are out of stock.".TranslateFormat(outOfStockItems.Count);

                AddNotification(
                    "Out of Stock Alert".Translate(),
                    message,
                    NotificationType.Warning);
            }
        }

        // Check for overdue invoices
        if (settings.InvoiceOverdueAlert)
        {
            var overdueInvoices = companyData.Invoices.Where(invoice => invoice.IsOverdue).ToList();

            if (overdueInvoices.Count > 0)
            {
                var message = overdueInvoices.Count == 1
                    ? "1 invoice is overdue.".Translate()
                    : "{0} invoices are overdue.".TranslateFormat(overdueInvoices.Count);

                AddNotification(
                    "Invoice Overdue".Translate(),
                    message,
                    NotificationType.Warning);
            }
        }

        // Check for overdue rentals
        if (settings.RentalOverdueAlert)
        {
            var overdueRentals = companyData.Rentals
                .Where(rental => rental.IsOverdue)
                .ToList();

            if (overdueRentals.Count > 0)
            {
                var message = overdueRentals.Count == 1
                    ? "1 rental is overdue.".Translate()
                    : "{0} rentals are overdue.".TranslateFormat(overdueRentals.Count);

                AddNotification(
                    "Rental Overdue".Translate(),
                    message,
                    NotificationType.Warning);
            }
        }
    }

    /// <summary>
    /// Checks if an inventory item is low stock or out of stock and sends a notification if enabled.
    /// Call this after saving a stock adjustment.
    /// </summary>
    /// <param name="item">The inventory item to check.</param>
    public static void CheckAndNotifyStockStatus(InventoryItem item)
    {
        var settings = CompanyManager?.CompanyData?.Settings.Notifications;
        if (settings == null)
            return;

        var status = item.CalculateStatus();

        if (settings.OutOfStockAlert && status == InventoryStatus.OutOfStock)
        {
            var product = CompanyManager?.CompanyData?.GetProduct(item.ProductId);
            var productName = product?.Name ?? "Item";
            AddNotification(
                "Out of Stock".Translate(),
                "{0} is now out of stock.".TranslateFormat(productName),
                NotificationType.Warning);
        }
        else if (settings.LowStockAlert && status == InventoryStatus.LowStock)
        {
            var product = CompanyManager?.CompanyData?.GetProduct(item.ProductId);
            var productName = product?.Name ?? "Item";
            AddNotification(
                "Low Stock".Translate(),
                "{0} is running low on stock.".TranslateFormat(productName),
                NotificationType.Warning);
        }
    }

    /// <summary>
    /// Like <see cref="CheckAndNotifyStockStatus(InventoryItem)"/>, but only alerts when this change
    /// lowered stock into a new status. Transactions call this on every stock change, so without the
    /// check each sale from an already-low item would repeat the same alert.
    /// </summary>
    public static void CheckAndNotifyStockStatus(InventoryItem item, decimal previousStock)
    {
        if (item.InStock >= previousStock || item.CalculateStatus(previousStock) == item.CalculateStatus())
            return;

        CheckAndNotifyStockStatus(item);
    }

    /// <summary>
    /// Checks if a rental is overdue and sends a notification if enabled.
    /// Call this after saving a rental record.
    /// </summary>
    /// <param name="rental">The rental record to check.</param>
    public static void CheckAndNotifyRentalOverdue(RentalRecord rental)
    {
        var settings = CompanyManager?.CompanyData?.Settings.Notifications;
        if (settings == null || !settings.RentalOverdueAlert)
            return;

        if (rental.IsOverdue)
        {
            var customer = CompanyManager?.CompanyData?.GetCustomer(rental.CustomerId);
            var customerName = customer?.Name ?? "Customer";
            AddNotification(
                "Rental Overdue".Translate(),
                "Rental for {0} is overdue.".TranslateFormat(customerName),
                NotificationType.Warning);
        }
    }

    #region Plan Status Events

    /// <summary>
    /// Event raised when the plan status changes (e.g., user upgrades).
    /// </summary>
    public static event EventHandler<PlanStatusChangedEventArgs>? PlanStatusChanged;

    /// <summary>
    /// Raises the PlanStatusChanged event.
    /// </summary>
    public static void RaisePlanStatusChanged(bool hasPremium)
    {
        PlanStatusChanged?.Invoke(null, new PlanStatusChangedEventArgs(hasPremium));
    }

    /// <summary>
    /// Opens the upgrade modal from anywhere in the app.
    /// </summary>
    /// <param name="source">Where it was opened from, recorded so a limit someone hit is
    /// separable from a deliberate visit.</param>
    public static void OpenUpgradeModal(string source = "unknown")
    {
        _ = TelemetryManager?.TrackFeatureAsync(FeatureName.UpgradeModalOpened, source);
        _appShellViewModel?.UpgradeModalViewModel.OpenCommand.Execute(null);
    }

    #endregion

    #region Shared Table Column Widths

    /// <summary>
    /// Gets the shared column widths for the Revenue table.
    /// </summary>
    public static Controls.ColumnWidths.RevenueTableColumnWidths RevenueColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Expenses table.
    /// </summary>
    public static Controls.ColumnWidths.TableColumnWidths ExpensesColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Invoices table.
    /// </summary>
    public static Controls.ColumnWidths.InvoicesTableColumnWidths InvoicesColumnWidths { get; } = new();

    public static Controls.ColumnWidths.RecurringTableColumnWidths RecurringColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Customers table.
    /// </summary>
    public static Controls.ColumnWidths.CustomersTableColumnWidths CustomersColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Suppliers table.
    /// </summary>
    public static Controls.ColumnWidths.SuppliersTableColumnWidths SuppliersColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Products table.
    /// </summary>
    public static Controls.ColumnWidths.ProductsTableColumnWidths ProductsColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Categories table.
    /// </summary>
    public static Controls.ColumnWidths.CategoriesTableColumnWidths CategoriesColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Stock Levels table.
    /// </summary>
    public static Controls.ColumnWidths.StockLevelsTableColumnWidths StockLevelsColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Receipts table.
    /// </summary>
    public static Controls.ColumnWidths.ReceiptsTableColumnWidths ReceiptsColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Returns table.
    /// </summary>
    public static Controls.ColumnWidths.ReturnsTableColumnWidths ReturnsColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Lost/Damaged table.
    /// </summary>
    public static Controls.ColumnWidths.LostDamagedTableColumnWidths LostDamagedColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Rental Records table.
    /// </summary>
    public static Controls.ColumnWidths.RentalRecordsTableColumnWidths RentalRecordsColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Rental Inventory table.
    /// </summary>
    public static Controls.ColumnWidths.RentalInventoryTableColumnWidths RentalInventoryColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Locations table.
    /// </summary>
    public static Controls.ColumnWidths.LocationsTableColumnWidths LocationsColumnWidths { get; } = new();
    public static Controls.ColumnWidths.EmployeesTableColumnWidths EmployeesColumnWidths { get; } = new();

    public static Controls.ColumnWidths.PayRunsTableColumnWidths PayRunsColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Stock Adjustments table.
    /// </summary>
    public static Controls.ColumnWidths.StockAdjustmentsTableColumnWidths StockAdjustmentsColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Purchase Orders table.
    /// </summary>
    public static Controls.ColumnWidths.PurchaseOrdersTableColumnWidths PurchaseOrdersColumnWidths { get; } = new();

    /// <summary>
    /// Gets the shared column widths for the Quotes table.
    /// </summary>
    public static Controls.ColumnWidths.QuotesTableColumnWidths QuotesColumnWidths { get; } = new();

    #endregion

    // View models stored for event wiring
    private static MainWindowViewModel? _mainWindowViewModel;

    /// <summary>
    /// Read-only view of the main window's view model, for the handful of call sites that
    /// need to nudge window-level state from deeper in the app. Kept internal and
    /// get-only so nothing outside can swap it out.
    /// </summary>
    internal static MainWindowViewModel? MainWindowViewModel => _mainWindowViewModel;
    private static AppShellViewModel? _appShellViewModel;
    private static WelcomeScreenViewModel? _welcomeScreenViewModel;
    private static IdleDetectionService? _idleDetectionService;
    private static Timer? _pendingConversionTimer;

    // Captured in OnFrameworkInitializationCompleted; InitializeAsync runs later and the
    // lifetime is not reachable from there.
    private static string[] _startupArgs = [];

    // macOS hands a double-clicked document to a running app as an Apple Event rather than
    // on the command line, so _startupArgs is always empty there. The event can arrive
    // before the shell is built, which is what _macActivationFile holds it for.
    private static string? _macActivationFile;
    private static bool _startupCompanyHandled;
    private static string? _fileToOpenAfterUpdate;

    // Cached page ViewModels to improve performance and prevent memory leaks from event subscriptions
    private static DashboardPageViewModel? _dashboardPageViewModel;
    private static AnalyticsPageViewModel? _analyticsPageViewModel;
    private static InsightsPageViewModel? _insightsPageViewModel;
    private static ReportsPageViewModel? _reportsPageViewModel;
    private static RevenuePageViewModel? _revenuePageViewModel;
    private static ExpensesPageViewModel? _expensesPageViewModel;
    private static InvoicesPageViewModel? _invoicesPageViewModel;
    private static QuotesPageViewModel? _quotesPageViewModel;
    private static BankMatchingPageViewModel? _bankMatchingPageViewModel;
    private static ProductsPageViewModel? _productsPageViewModel;
    private static StockLevelsPageViewModel? _stockLevelsPageViewModel;
    private static LocationsPageViewModel? _locationsPageViewModel;
    private static EmployeesPageViewModel? _employeesPageViewModel;
    private static PayRunsPageViewModel? _payRunsPageViewModel;
    private static StockAdjustmentsPageViewModel? _stockAdjustmentsPageViewModel;
    private static PurchaseOrdersPageViewModel? _purchaseOrdersPageViewModel;
    private static CategoriesPageViewModel? _categoriesPageViewModel;
    private static CustomersPageViewModel? _customersPageViewModel;
    private static SuppliersPageViewModel? _suppliersPageViewModel;
    private static RentalInventoryPageViewModel? _rentalInventoryPageViewModel;
    private static RentalRecordsPageViewModel? _rentalRecordsPageViewModel;
    private static ReturnsPageViewModel? _returnsPageViewModel;
    private static LostDamagedPageViewModel? _lostDamagedPageViewModel;
    private static ReceiptsPageViewModel? _receiptsPageViewModel;

    /// <summary>
    /// Clears all cached page ViewModels to ensure fresh state when opening a new company.
    /// </summary>
    private static void ClearPageCaches()
    {
        // Tear down each cached page VM's event subscriptions (the static LanguageChanged and
        // CurrencyChanged events, modal events, etc.) before dropping it. Without this, a company
        // switch leaves the orphaned VMs rooted by those static events and still reacting to them.
        // Cast to ICleanupViewModel rather than SortablePageViewModelBase: the Dashboard, Analytics,
        // and Reports VMs have Cleanup() but extend a different base, so a SortablePageViewModelBase
        // cast silently skipped them and leaked their subscriptions on every company switch.
        foreach (var vm in new object?[]
        {
            _dashboardPageViewModel, _analyticsPageViewModel, _insightsPageViewModel, _reportsPageViewModel,
            _revenuePageViewModel, _expensesPageViewModel, _invoicesPageViewModel, _quotesPageViewModel,
            _bankMatchingPageViewModel, _productsPageViewModel, _stockLevelsPageViewModel, _locationsPageViewModel,
            _stockAdjustmentsPageViewModel, _purchaseOrdersPageViewModel, _categoriesPageViewModel,
            _customersPageViewModel, _suppliersPageViewModel, _rentalInventoryPageViewModel,
            _rentalRecordsPageViewModel, _returnsPageViewModel, _lostDamagedPageViewModel, _receiptsPageViewModel,
            _employeesPageViewModel, _payRunsPageViewModel
        })
            (vm as ICleanupViewModel)?.Cleanup();

        _dashboardPageViewModel = null;
        _analyticsPageViewModel = null;
        _insightsPageViewModel = null;
        _reportsPageViewModel = null;
        _revenuePageViewModel = null;
        _expensesPageViewModel = null;
        _invoicesPageViewModel = null;
        _quotesPageViewModel = null;
        _bankMatchingPageViewModel = null;
        _productsPageViewModel = null;
        _stockLevelsPageViewModel = null;
        _locationsPageViewModel = null;
        _stockAdjustmentsPageViewModel = null;
        _purchaseOrdersPageViewModel = null;
        _categoriesPageViewModel = null;
        _customersPageViewModel = null;
        _suppliersPageViewModel = null;
        _rentalInventoryPageViewModel = null;
        _rentalRecordsPageViewModel = null;
        _returnsPageViewModel = null;
        _lostDamagedPageViewModel = null;
        _receiptsPageViewModel = null;
        _employeesPageViewModel = null;
        _payRunsPageViewModel = null;

        // The "N recurring invoices were generated" banner count is process-wide static state. Clear
        // it on a company switch/close so a count produced for the previous company can't surface as a
        // phantom banner on the next company (whose own generation run may have produced nothing).
        RecurringInvoiceService.ClearPendingGenerated();
        RecurringTransactionService.ClearPendingExpenses();
        RecurringTransactionService.ClearPendingRevenues();
    }

    // File watchers for recent companies - watches directories containing recent company files
    private static readonly Dictionary<string, FileSystemWatcher> RecentCompanyWatchers = new();

    // When true, the CompanySaved event handler skips showing the "Saved" indicator
    private static bool _suppressSavedFeedback;
    private static bool _isOpeningCompany;

    // When true, a new company is being created. Like _isOpeningCompany, this keeps the loading
    // overlay up across the close-then-open transition so the welcome screen doesn't flash between
    // closing the current company and opening the new one.
    private static bool _isCreatingCompany;

    /// <summary>
    /// Suppresses the "Saved" feedback label for the next save operation.
    /// </summary>
    public static void SuppressNextSavedFeedback() => _suppressSavedFeedback = true;

    /// <summary>
    /// Gets the confirmation dialog ViewModel for showing confirmation dialogs from anywhere.
    /// </summary>
    public static ConfirmationDialogViewModel? ConfirmationDialog { get; private set; }

    /// <summary>The one recurring schedule editor, shared by the Expenses and Revenue tabs.</summary>
    public static RecurringScheduleEditorViewModel? RecurringScheduleEditor =>
        _appShellViewModel?.RecurringScheduleEditorViewModel;

    /// <summary>
    /// Gets the unsaved changes dialog ViewModel for showing save prompts with change lists.
    /// </summary>
    public static UnsavedChangesDialogViewModel? UnsavedChangesDialog { get; private set; }

    /// <summary>
    /// Gets the receipt viewer modal ViewModel for viewing receipt images.
    /// </summary>
    public static ReceiptViewerModalViewModel? ReceiptViewerModal { get; private set; }

    /// <summary>
    /// Gets the custom date range modal ViewModel for date range selection from anywhere.
    /// </summary>
    public static CustomDateRangeModalViewModel? CustomDateRangeModal { get; private set; }

    /// <summary>
    /// Checks if the reports page has unsaved changes.
    /// </summary>
    public static bool HasReportsPageUnsavedChanges => _appShellViewModel?.HasReportsPageUnsavedChanges ?? false;

    /// <summary>
    /// Shows a confirmation dialog for reports unsaved changes and returns whether to proceed.
    /// </summary>
    public static Task<bool> ConfirmReportsUnsavedChangesAsync()
    {
        return _appShellViewModel?.ConfirmReportsUnsavedChangesAsync() ?? Task.FromResult(true);
    }

    /// <summary>
    /// Gets the pending conversion service for processing offline transactions.
    /// </summary>
    public static PendingConversionService? PendingConversionService { get; private set; }

    /// <summary>
    /// Gets the PDF bank statement extractor (premium-gated AI service).
    /// </summary>
    public static IPdfStatementExtractor? PdfStatementExtractor { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _startupArgs = desktop.Args ?? [];
            WireMacFileActivation();
            _ = InstallMacApplicationMenuAsync();

            // Initialize error logging first so it's available for all services
            var errorLogger = new ErrorLogger();
            ErrorLogger = errorLogger;

            // Let crash reports captured by the global handlers include recent log
            // entries as breadcrumbs.
            CrashReporter.SetBreadcrumbSource(errorLogger);

            // A web view that cannot start throws from an async void attach handler, which
            // lands on the dispatcher as an unhandled exception and ends the process. Install
            // the guard before any window exists so the first attach is already covered.
            WebViewEnvironment.InstallDispatcherGuard(errorLogger);

            // Before any window exists: RequestedThemeVariant is "Default" (follows the OS)
            // until the saved theme is applied, so applying it late flashes the wrong palette.
            // Both calls are cheap and synchronous.
            SettingsService = new GlobalSettingsService(errorLogger);
            try
            {
                SettingsService.LoadGlobalSettings();
                ThemeService.Instance.SetGlobalSettingsService(SettingsService);
                ThemeService.Instance.Initialize();
            }
            catch (Exception ex)
            {
                errorLogger.LogWarning($"Failed to apply saved theme during startup: {ex.Message}", "Startup");
            }

            // Also before any XAML is built: {loc:Loc} reads the language once, when the view is
            // created, so applying it after the window opens shows English and then rewrites every
            // label. A language not yet on disk is downloaded later, in InitializeAsync.
            try
            {
                ApplyStartupLanguageFromCache();
            }
            catch (Exception ex)
            {
                errorLogger.LogWarning($"Failed to apply saved language during startup: {ex.Message}", "Startup");
            }

            // Show a splash straight away. Avalonia only shows MainWindow once this method
            // returns, and everything below builds the service graph first, so without this
            // the screen stays empty for several seconds. Users read that as a failed launch
            // and click the shortcut again, which is how we end up with concurrent instances.
            //
            // Wrapped defensively: the splash is a nicety and must never be able to stop the
            // app from starting.
            SplashWindow? splash;
            try
            {
                splash = new SplashWindow();
                splash.Show();

                // Show() only queues the window. Without pumping the dispatcher the UI thread
                // goes straight into the construction below, so layout and render never run and
                // the window stays blank: present and marked visible to the OS, but showing
                // nothing. Force those jobs through now, while there is still a gap to do it in.
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // Marked after RunJobs, not after Show(), because Show() alone only queues the
                // window. This is the first moment the user has something to look at, which is
                // what the measurement is for.
                StartupTimeline.MarkFirstPaint();
            }
            catch (Exception ex)
            {
                errorLogger.LogWarning($"Failed to show splash window: {ex.Message}", "Startup");
                splash = null;
            }

            // Now that there is something on screen, pay for the update service.
            if (UpdateService == null && UpdateServiceFactory != null)
            {
                try
                {
                    UpdateService = UpdateServiceFactory();
                }
                catch (Exception ex)
                {
                    errorLogger.LogWarning(
                        $"Could not create the update service: {ex.Message}",
                        "Startup", Core.Models.Telemetry.ErrorCategory.Network,
                        "UpdateServiceCreateFailed");
                }
            }

            // Initialize core services
            var compressionService = new CompressionService();
            var footerService = new FooterService();
            var encryptionService = new EncryptionService();
            _fileService = new FileService(compressionService, footerService, encryptionService);
            // SettingsService is built and loaded above; re-creating it would discard those settings.
            LicenseService = new LicenseService(encryptionService, SettingsService, errorLogger);
            CompanyManager = new CompanyManager(_fileService, SettingsService, footerService, errorLogger);

            // Initialize telemetry services
            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            SharedHttpClient = httpClient;
            var geoLocationService = new GeoLocationService(httpClient, errorLogger);
            var telemetryStorageService = new TelemetryStorageService(errorLogger: errorLogger);
            var appVersion = AppInfo.VersionNumber;
            var telemetryUploadService = new TelemetryUploadService(telemetryStorageService, httpClient, errorLogger, appVersion);
            TelemetryManager = new TelemetryManager(
                telemetryStorageService,
                telemetryUploadService,
                geoLocationService,
                errorLogger,
                appVersion);

            // Initialize payment portal service
            PaymentPortalService = new PaymentPortalService();
            // Resolves the company lazily: this is built once at startup but the
            // open company changes over the session.
            PortalBalanceSyncService = new PortalBalanceSyncService(
                PaymentPortalService,
                () => CompanyManager?.CompanyData,
                errorLogger);
            InvoiceUsageService = new UsageLimitService(UsageLimit.InvoiceSends, LicenseService, ErrorLogger);

            // Initialize mobile-sync service (shares the same long-lived HttpClient)
            SyncService = new SyncService(httpClient);

            // Initialize refund service (uses the same shared HttpClient)
            RefundService = new RefundService(httpClient);

            // Source survey reporter shares the long-lived telemetry HttpClient. The
            // survey may fire long after first run (after the user finishes the setup
            // checklist), so we keep the reporter alive rather than scoping it to a
            // one-shot task like FirstRunReporter.
            SourceSurveyReporter = new SourceSurveyReporter(httpClient, appVersion, errorLogger);

            // Fetches the survey's answer options from the website (shares the same
            // long-lived HttpClient). Falls back to a bundled default list offline.
            SourceSurveyOptionsService = new SourceSurveyOptionsService(httpClient, errorLogger);

            // Create navigation service
            NavigationService = new NavigationService();

            // Everything above is the service graph; everything below builds view models.
            StartupTimeline.MarkServicesReady();

            _mainWindowViewModel = new MainWindowViewModel(SettingsService);
            ConfirmationDialog = new ConfirmationDialogViewModel();
            UnsavedChangesDialog = new UnsavedChangesDialogViewModel();
            ReceiptViewerModal = new ReceiptViewerModalViewModel();
            PendingConversionService = new PendingConversionService(errorLogger)
            {
                CurrentCompany = () => CompanyManager?.CompanyData
            };
            PdfStatementExtractor = new PdfStatementExtractor(ErrorLogger);
            _idleDetectionService = new IdleDetectionService();

            // Create app shell with navigation service and optional update service
            _appShellViewModel = new AppShellViewModel(NavigationService, UpdateService);
            CustomDateRangeModal = _appShellViewModel.CustomDateRangeModalViewModel;

            // Ensure no unsaved changes indicator on startup
            _mainWindowViewModel.HasUnsavedChanges = false;
            _appShellViewModel.HeaderViewModel.HasUnsavedChanges = false;

            var appShell = new AppShell
            {
                DataContext = _appShellViewModel
            };

            // Register pages with navigation service
            RegisterPages(NavigationService);

            // Set navigation callback to update current page in AppShell
            NavigationService.SetNavigationCallback(page => _appShellViewModel.CurrentPage = page);

            // Dismiss tutorial completion guidance when user navigates
            NavigationService.Navigated += (_, _) =>
            {
                TutorialService.Instance.DismissCompletionGuidance();
            };

            // Consecutive repeats are dropped: re-navigating to the current page is a
            // no-op to the user and would only pad the timeline.
            var lastTrackedPage = string.Empty;
            NavigationService.Navigated += (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.PageName) || e.PageName == lastTrackedPage)
                {
                    return;
                }

                lastTrackedPage = e.PageName;
                // Raises the PageView event for the page being left, timed and idle-aware.
                TelemetryManager?.NoteCurrentPage(e.PageName);

                // The registered page name; Reports has no PageNames constant.
                if (e.PageName == "Reports")
                    _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ReportOpened);
            };

            // Chart text (axis labels, titles, legends) is drawn by LiveCharts with
            // baked-in SkiaSharp paints. Page view models recolor their charts on
            // ThemeChanged, but LiveCharts does not repaint an already-rendered chart
            // when only the paint colors change, so the text keeps the old theme's
            // color until the chart control is recreated. Rebuild the current page
            // (the same thing navigating away and back does) so charts render fresh
            // with the new colors. Only the Dashboard and Analytics pages host live
            // charts, so leave every other page alone. Posted so it runs after the
            // page view models' own ThemeChanged handlers have updated their colors.
            ThemeService.Instance.ThemeChanged += (_, _) =>
            {
                var page = NavigationService.CurrentPageName;
                if (page is PageNames.Dashboard or PageNames.Analytics)
                    Avalonia.Threading.Dispatcher.UIThread.Post(NavigationService.RefreshCurrentPage);
            };

            // Set initial view
            _mainWindowViewModel.NavigateTo(appShell);

            // Wire up company manager events
            WireCompanyManagerEvents();

            // Wire up pending conversion events
            PendingConversionService.PendingConversionsProcessed += (_, args) =>
            {
                var message = args.ConvertedCount == 1
                    ? "1 pending transaction has been processed successfully.".Translate()
                    : string.Format("{0} pending transactions have been processed successfully.".Translate(), args.ConvertedCount);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // The sample converts its demo rows on every open; announcing that is noise.
                    if (args.ConvertedCount > 0 && CompanyManager?.IsSampleCompany != true)
                    {
                        AddNotification(
                            "Back Online".Translate(),
                            message,
                            NotificationType.Success);
                    }

                    // Refresh ViewModels so converted transactions show updated status and amounts
                    _expensesPageViewModel?.RefreshExpensesCommand.Execute(null);
                    _revenuePageViewModel?.RefreshRevenueCommand.Execute(null);
                    NavigationService.RefreshCurrentPage();
                });
            };

            // Wire up modal change events (separate from company manager)
            WireModalChangeEvents();

            // Wire up the post-onboarding source survey trigger
            WireSourceSurveyEvents();

            // Sync HasUnsavedChanges with undo/redo state (both MainWindow and Header)
            UndoRedoManager.StateChanged += (_, _) =>
            {
                var hasChanges = !UndoRedoManager.IsAtSavedState;
                _mainWindowViewModel.HasUnsavedChanges = hasChanges;
                _appShellViewModel.HeaderViewModel.HasUnsavedChanges = hasChanges;
            };

            // After any undo/redo, fire CompanyDataChanged so pages
            // subscribed to it (Dashboard, Analytics, Invoices, etc.) refresh
            // their derived data. Individual undo callbacks only call
            // companyData.MarkAsModified() which doesn't fire the event.
            //
            // NotifyDataChanged fires CompanyDataChanged, whose handler force-sets
            // HasUnsavedChanges = true. That's wrong after an undo/redo: the undo manager
            // is the authority on the saved state, so re-sync the flag from IsAtSavedState
            // afterwards. Otherwise undoing back to the saved state leaves the asterisk on.
            void RefreshDerivedDataAfterUndoRedo()
            {
                CompanyManager?.NotifyDataChanged();
                var hasChanges = !UndoRedoManager.IsAtSavedState;
                if (_mainWindowViewModel != null)
                    _mainWindowViewModel.HasUnsavedChanges = hasChanges;
                if (_appShellViewModel != null)
                    _appShellViewModel.HeaderViewModel.HasUnsavedChanges = hasChanges;
            }
            UndoRedoManager.ActionUndone += (_, _) => RefreshDerivedDataAfterUndoRedo();
            UndoRedoManager.ActionRedone += (_, _) => RefreshDerivedDataAfterUndoRedo();

            // Wire up file menu events
            WireFileMenuEvents(desktop);

            // Wire up create company wizard events
            WireCreateCompanyEvents(desktop);

            // Wire up welcome screen events
            WireWelcomeScreenEvents(desktop);

            // Wire up company switcher events
            WireCompanySwitcherEvents(desktop);

            // Wire up settings modal events
            WireSettingsModalEvents();

            // Wire up export as modal events
            WireExportEvents(desktop);

            // Wire up import modal events
            WireImportEvents(desktop);

            // Wire up header save request
            _appShellViewModel.HeaderViewModel.SaveRequested += async (_, _) =>
            {
                if (CompanyManager?.IsCompanyOpen == true)
                {
                    // Sample company cannot be saved directly - redirect to Save As
                    if (CompanyManager.IsSampleCompany)
                    {
                        var saved = await SaveCompanyAsDialogAsync(desktop);
                        if (!saved)
                            _appShellViewModel.HeaderViewModel.ShowSavingIndicator = false;
                        return;
                    }

                    try
                    {
                        var saved = await SaveCompanyWithSecurityGuidanceAsync();
                        if (!saved)
                            _appShellViewModel.HeaderViewModel.ShowSavingIndicator = false;
                    }
                    catch (Exception ex)
                    {
                        _appShellViewModel.HeaderViewModel.ShowSavingIndicator = false;
                        ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to save company on close");
                        await ShowErrorDialogAsync("Error".Translate(), GetFriendlySaveErrorMessage(ex));
                    }
                }
            };

            // Share CreateCompanyViewModel with MainWindow for full-screen overlay
            _mainWindowViewModel.CreateCompanyViewModel = _appShellViewModel.CreateCompanyViewModel;

            // Share WelcomeScreenViewModel with MainWindow for full-screen overlay
            _mainWindowViewModel.WelcomeScreenViewModel = _welcomeScreenViewModel;

            // Share PasswordPromptModalViewModel with MainWindow for password dialog overlay
            _mainWindowViewModel.PasswordPromptModalViewModel = _appShellViewModel.PasswordPromptModalViewModel;
            _mainWindowViewModel.CheckForUpdateModalViewModel = _appShellViewModel.CheckForUpdateModalViewModel;

            // Share ConfirmationDialogViewModel with MainWindow for confirmation dialogs
            _mainWindowViewModel.ConfirmationDialogViewModel = ConfirmationDialog;

            // Share UnsavedChangesDialogViewModel with MainWindow for unsaved changes dialogs
            _mainWindowViewModel.UnsavedChangesDialogViewModel = UnsavedChangesDialog;

            // Share ReceiptViewerModalViewModel with MainWindow for receipt viewing
            _mainWindowViewModel.ReceiptViewerModalViewModel = ReceiptViewerModal;

            // Initialize tutorial ViewModels for first-time user experience
            _mainWindowViewModel.TutorialWelcomeViewModel = new TutorialWelcomeViewModel();
            _mainWindowViewModel.AppTourViewModel = new AppTourViewModel();
            _mainWindowViewModel.CategoriesTutorialViewModel = new PageTutorialViewModel(TutorialService.Pages.Categories, PageTutorialViewModel.CategoriesSteps);
            _mainWindowViewModel.ProductsTutorialViewModel = new PageTutorialViewModel(TutorialService.Pages.Products, PageTutorialViewModel.ProductsSteps);

            // Wire up tutorial flow: Welcome -> App Tour
            _mainWindowViewModel.TutorialWelcomeViewModel.StartTourRequested += (_, _) =>
            {
                _mainWindowViewModel.AppTourViewModel?.StartTour();
            };

            // Final reset of unsaved changes before window is shown - ensures clean startup state
            _mainWindowViewModel.HasUnsavedChanges = false;
            _appShellViewModel.HeaderViewModel.HasUnsavedChanges = false;
            SyncSampleCompanyState();

            // Apply saved sidebar collapsed state after settings are loaded from disk.
            var savedCollapsed = SettingsService.GlobalSettings.Ui.SidebarCollapsed;
            if (savedCollapsed)
            {
                _appShellViewModel.SidebarViewModel.IsCollapsed = true;
            }

            StartupTimeline.MarkViewModelsReady();

            // Before the window exists, so it opens at the saved size and state rather than
            // opening at the default and then resizing in front of the user.
            _mainWindowViewModel.LoadWindowState();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainWindowViewModel
            };

            // Close the splash only once the main window is actually on screen. ShutdownMode
            // is left at its default of OnLastWindowClose, so closing the splash while the
            // main window is still unshown would leave zero windows open and exit the app.
            if (splash != null)
            {
                desktop.MainWindow.Opened += (_, _) =>
                {
                    try
                    {
                        splash.Close();
                    }
                    catch (Exception ex)
                    {
                        errorLogger.LogWarning($"Failed to close splash window: {ex.Message}", "Startup");
                    }
                };
            }

            // Record how long this launch took, once the main window is genuinely on screen.
            // Separate from the splash handler above so it still runs on the path where the
            // splash failed to open, which is exactly the launch worth knowing about.
            desktop.MainWindow.Opened += (_, _) =>
            {
                if (!StartupTimeline.TryClaimReport())
                {
                    return;
                }

                _ = TelemetryManager?.TrackStartupAsync(
                    StartupTimeline.ToFirstPaintMs,
                    StartupTimeline.ToServicesReadyMs,
                    StartupTimeline.ToViewModelsReadyMs,
                    StartupTimeline.ToReadyMs(),
                    StartupTimeline.IsColdStart);
            };

            // Process pending conversions when window is activated (e.g., user returns after going offline)
            desktop.MainWindow.Activated += async (_, _) =>
            {
                await TryProcessPendingConversionsAsync();
            };

            // Wire up idle detection for auto-logout (needs MainWindow to exist)
            WireIdleDetection(desktop);

            // Load settings and recent companies asynchronously after window is shown
            _ = InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Settles which language this launch uses and switches to it when its translations are
    /// already cached. Runs before any window is built.
    /// </summary>
    private static void ApplyStartupLanguageFromCache()
    {
        if (SettingsService == null)
            return;

        var language = SettingsService.GlobalSettings.Ui.Language;

        // A fresh install starts in the machine's own language when we have that translation.
        // Only on the very first run: after that the setting is the user's answer, and a
        // machine whose language changes later must not overrule it.
        if (SettingsService.IsFirstRun
            && Data.Languages.MatchSystemLanguage(System.Globalization.CultureInfo.CurrentUICulture.Name)
                is { } detected
            && detected != language)
        {
            language = detected;
            SettingsService.GlobalSettings.Ui.Language = detected;
            _ = SettingsService.SaveGlobalSettingsAsync();
        }

        if (!string.IsNullOrEmpty(language) && language != "English")
        {
            LanguageService.Instance.TrySetCachedLanguage(language);
        }
    }

    /// <summary>
    /// Performs async initialization after the main window is displayed.
    /// </summary>
    private static async Task InitializeAsync()
    {
        try
        {
            // Decrypting the license is a deliberately slow key-stretching pass, so it runs off the
            // UI thread alongside the work below. Anything else that needs the key while it runs
            // (usage checks, crash upload) waits on the same cached result.
            var licenseLoad = LicenseService is { } licenseService
                ? Task.Run(() => licenseService.LoadLicense())
                : Task.FromResult(false);

            // Load recent companies asynchronously (footer reads from .argo files)
            await LoadRecentCompaniesAsync();

            await ClearLegacyFileAssociationsAsync();

            // Converted logo and avatar picks from earlier runs. Each one was copied into a
            // company at the time, so the staged file has no reader left.
            Helpers.ImageFileLoader.ClearConvertedCache();

            // Open the file the shell handed us (.argo double-click), or the last company
            // after an update restart
            await TryOpenStartupCompanyAsync();

            // Initialize services that depend on settings.
            if (SettingsService != null)
            {
                // Initialize theme service with settings
                ThemeService.Instance.SetGlobalSettingsService(SettingsService);
                ThemeService.Instance.Initialize();

                // Initialize tutorial service with settings
                TutorialService.Instance.SetGlobalSettingsService(SettingsService);

                // Initialize welcome screen tutorial mode after tutorial service is set up
                _welcomeScreenViewModel?.InitializeTutorialMode();
            }

            // Initialize telemetry session
            if (TelemetryManager != null)
            {
                await TelemetryManager.InitializeAsync();

                // After InitializeAsync, or the event has no session to land in.
                if (_mainWindowViewModel?.ShowWelcomeScreen == true)
                    MainWindowViewModel.ReportWelcomeShown();
            }

            // Deliver anything a previous run left behind because it didn't close
            // cleanly: crash reports written by the global handlers, and telemetry
            // events whose on-close upload never ran (force-quit / crash). Both are
            // best-effort and run off the UI thread so launch isn't blocked.
            try
            {
                var flushVersion = AppInfo.VersionNumber;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (SharedHttpClient is { } crashClient)
                        {
                            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                            await CrashReporter.UploadPendingAsync(crashClient, flushVersion, timeout.Token);
                        }
                    }
                    catch
                    {
                        // Best-effort; never disrupt launch.
                    }

                    try
                    {
                        if (TelemetryManager != null)
                        {
                            await TelemetryManager.UploadPendingDataAsync();
                        }
                    }
                    catch
                    {
                        // Best-effort; the next clean close will retry.
                    }
                });
            }
            catch (Exception ex)
            {
                ErrorLogger?.LogWarning(
                    $"Failed to start startup flush: {ex.Message}",
                    context: "App.InitializeAsync");
            }

            // Report first-run install for referral funnel attribution. Fire-and-forget
            // so app startup isn't blocked on network I/O. The reporter writes a marker
            // after a successful POST so subsequent launches are no-ops.
            try
            {
                var appVersion = AppInfo.VersionNumber;
                var capturedErrorLogger = ErrorLogger;
                if (SharedHttpClient is { } firstRunClient)
                {
                    _ = Task.Run(async () =>
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        var firstRunReporter = new FirstRunReporter(
                            firstRunClient,
                            appVersion,
                            capturedErrorLogger);
                        await firstRunReporter.ReportIfFirstRunAsync(timeout.Token);
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorLogger?.LogWarning(
                    $"Failed to start FirstRunReporter: {ex.Message}",
                    context: "App.OnFrameworkInitializationCompleted");
            }

            // Remove stale cached receipt preview/render files from temp (fire-and-forget).
            _ = ReceiptTempCleanup.CleanOldFilesAsync();

            // Initialize language service for localization
            LanguageService.Instance.Initialize();

            // Apply global language setting
            if (SettingsService != null)
            {
                var language = SettingsService.GlobalSettings.Ui.Language;

                // Only reached when startup found no cached file for it, so this downloads.
                if (!string.IsNullOrEmpty(language) && language != "English"
                    && LanguageService.Instance.CurrentLanguage != language)
                {
                    await LanguageService.Instance.SetLanguageAsync(language);
                }

                // Refresh cached translations once per app version. Without this, users
                // never see translations added after their first language download because
                // DownloadAndCacheLanguageAsync skips when a cached file exists.
                var currentVersion = AppInfo.VersionNumber;
                if (SettingsService.GlobalSettings.Ui.LastLanguageVersion != currentVersion)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var refreshed = await LanguageService.Instance.UpdateAllCachedTranslationsAsync();
                            if (refreshed)
                            {
                                SettingsService.GlobalSettings.Ui.LastLanguageVersion = currentVersion;
                                await SettingsService.SaveGlobalSettingsAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            NetworkFailure.Report(ErrorLogger, ex, "Translation cache refresh failed");
                        }
                    });
                }
            }

            // Initialize exchange rate service for currency conversion
            await InitializeExchangeRateServiceAsync();

            // Initialize AI operation timing (pooled duration priors that drive accurate
            // progress bars). Non-blocking: loads the disk cache, refreshes priors in the
            // background, and falls back to seed priors when offline.
            InitializeOperationTimingService();

            // Load and apply saved license status
            if (LicenseService != null && _appShellViewModel != null)
            {
                var hasPremium = await licenseLoad;
                if (hasPremium)
                {
                    _appShellViewModel.SetPlanStatus(hasPremium);

                    // Validate license online in the background
                    _ = ValidateLicenseOnStartupAsync();
                }
                else
                {
                    // Fetch plan details from API so the upgrade modal is ready
                    _ = _appShellViewModel.UpgradeModalViewModel.FetchPlansAsync();
                }
            }

            // Check for updates in the background (desktop only)
            await CheckForUpdatesInBackgroundAsync();
        }
        catch (Exception ex)
        {
            // Log error but don't crash the app
            ErrorLogger?.LogError(ex, ErrorCategory.Unknown, "Error during async initialization");
        }
    }

    /// <summary>
    /// Validates the stored license key online on startup.
    /// Checks subscription status and device ownership. If invalid, clears premium and shows a message.
    /// </summary>
    private static async Task ValidateLicenseOnStartupAsync()
    {
        try
        {
            if (LicenseService == null || _appShellViewModel == null)
                return;

            var result = await LicenseService.ValidateLicenseOnlineAsync();

            switch (result.Status)
            {
                case LicenseValidationStatus.Valid:
                    // License is valid, nothing to do
                    return;

                case LicenseValidationStatus.NetworkError:
                    // No internet or server unreachable, allow offline use
                    return;

                case LicenseValidationStatus.RateLimited:
                    // The check could not be completed, which is not a verdict on the licence. Allow offline use.
                    ErrorLogger?.LogWarning(
                        "Startup license validation was rate limited; keeping the stored license.",
                        category: ErrorCategory.License);
                    return;

                case LicenseValidationStatus.InvalidKey:
                    await LicenseService.ClearLicenseAsync();
                    _appShellViewModel.SetPlanStatus(false);
                    await ShowErrorDialogAsync(
                        "License Issue".Translate(),
                        "Your license key is no longer valid. Please contact support or enter a new key.".Translate());
                    break;

                case LicenseValidationStatus.ExpiredSubscription:
                    await LicenseService.ClearLicenseAsync();
                    _appShellViewModel.SetPlanStatus(false);
                    await ShowErrorDialogAsync(
                        "Subscription Expired".Translate(),
                        "Your premium subscription has expired. Please renew your subscription to continue using premium features.".Translate());
                    break;

                case LicenseValidationStatus.WrongDevice:
                    await LicenseService.ClearLicenseAsync();
                    _appShellViewModel.SetPlanStatus(false);
                    await ShowErrorDialogAsync(
                        "License Deactivated".Translate(),
                        "Your license key has been activated on a different device. Premium features have been deactivated on this device. You can re-enter your key in the Upgrade menu to reactivate.".Translate());
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogError(ex, ErrorCategory.License, "Error during startup license validation");
        }
    }

    /// <summary>
    /// Performs a background check for updates on startup. If a newer version is
    /// available, shows the update banner at the top of the app shell ("A new
    /// version is available" with a Download now button) and primes the
    /// CheckForUpdate modal so the banner's button can start the download right away.
    /// </summary>
    private static async Task CheckForUpdatesInBackgroundAsync()
    {
        if (UpdateService == null || _appShellViewModel == null)
            return;

        // Wire the ApplyingUpdate event to save user data before the app exits
        UpdateService.ApplyingUpdate += (_, _) =>
        {
            try
            {
                // The installer restarts the app, so this is flushed with the rest of the
                // shutdown rather than uploaded from the version being replaced.
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.UpdateApplied, AppInfo.VersionNumber);

                if (CompanyManager?.IsCompanyOpen == true)
                {
                    // Use synchronous wait since we must complete before the process exits
                    Task.Run(async () => await CompanyManager.SaveCompanyAsync())
                        .GetAwaiter().GetResult();
                }

                if (SettingsService != null)
                {
                    // Flag that we're updating so we can auto-reopen the company after restart
                    SettingsService.GlobalSettings.Updates.AutoOpenRecentAfterUpdate = true;
                    SettingsService.GlobalSettings.Updates.FileToOpenAfterUpdate = _fileToOpenAfterUpdate;
                    Task.Run(async () => await SettingsService.SaveGlobalSettingsAsync())
                        .GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                ErrorLogger?.LogWarning($"Failed to save data before update: {ex.Message}", "AutoUpdate");
            }
        };

        try
        {
            var update = await UpdateService.CheckForUpdateAsync();
            if (update != null)
            {
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.UpdateOffered, update.Version);
                _appShellViewModel.CheckForUpdateModalViewModel.NotifyUpdateAvailable(update);
                _appShellViewModel.ShowUpdateBanner($"V.{update.Version}");
            }
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogWarning($"Background update check failed: {ex.Message}", "AutoUpdate");
        }
    }


    /// <summary>
    /// Gives macOS the application menu it expects.
    ///
    /// Avalonia supplies a bare default with little more than Quit, which leaves the app with
    /// no About item, no Settings under its own name where every Mac app keeps it, and no File
    /// menu at all. The commands are the ones the in-app UI already uses, so the menu is
    /// another way in rather than a second implementation.
    ///
    /// Deferred until the shell exists: the view models it binds to are built during startup,
    /// and a menu wired to nulls would be a row of dead items.
    /// </summary>
    private async Task InstallMacApplicationMenuAsync()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        // Poll rather than hook a "ready" event, because the shell is assembled across several
        // async steps and there is no single point that means "all of these exist".
        for (var attempt = 0; attempt < 100 && _appShellViewModel == null; attempt++)
            await Task.Delay(100);

        if (_appShellViewModel is not { } shell)
            return;

        NativeMenuItem Item(string header, string? gesture, Action action)
        {
            var item = new NativeMenuItem(header.Translate());
            if (gesture != null)
                item.Gesture = KeyGesture.Parse(gesture);
            item.Click += (_, _) => action();
            return item;
        }

        var appMenu = new NativeMenuItem("Argo Books")
        {
            Menu =
            [
                // No About item: the app has no About screen to open, and a menu entry that
                // opens something else would be worse than its absence.
                Item("Settings...", "Cmd+,", () => shell.SettingsModalViewModel.OpenCommand.Execute(null)),
            ],
        };

        var fileMenu = new NativeMenuItem("File".Translate())
        {
            Menu =
            [
                Item("New Company", null, () => _ = RequestCreateNewCompanyAsync()),
                Item("Save", "Cmd+S", () => shell.HeaderViewModel.SaveCommand.Execute(null)),
                Item("Save As...", "Cmd+Shift+S", () => shell.FileMenuPanelViewModel.SaveAsCommand.Execute(null)),
            ],
        };

        NativeMenu.SetMenu(this, [appMenu, fileMenu]);
    }

    /// <summary>
    /// Routes a .argo file double-clicked in Finder to the same open path the file menu uses.
    ///
    /// Windows and Linux pass the path in argv, which StartupFileArgs already reads. macOS
    /// does not: Launch Services sends an Apple Event instead, both for a cold launch and for
    /// a file opened while the app is already running, so without this the app came up on the
    /// welcome screen and the CFBundleDocumentTypes entry in Info.plist advertised a handler
    /// that did nothing.
    /// </summary>
    private void WireMacFileActivation()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        // Not a cast on ApplicationLifetime: ClassicDesktopStyleApplicationLifetime implements
        // IClassicDesktopStyleApplicationLifetime, IControlledApplicationLifetime,
        // IApplicationLifetime and IDisposable, and none of them is IActivatableLifetime. That
        // cast fails on every platform, which is what left this handler unsubscribed and a
        // double-clicked file opening the app onto the welcome screen. Activation is a platform
        // feature, and on macOS it resolves to Avalonia.Native.MacOSActivatableLifetime.
        if (TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime activatable)
            return;

        activatable.Activated += (_, e) =>
        {
            if (e is not FileActivatedEventArgs fileArgs)
                return;

            string? path = null;
            foreach (var file in fileArgs.Files)
            {
                // Reuse the argv validation: extension, full path, and existence.
                path = StartupFileArgs.GetCompanyFilePath([file.Path.LocalPath]);
                if (path != null)
                    break;
            }

            if (path == null)
                return;

            // A cold launch fires this before the shell exists, and OpenCompanyWithRetryAsync
            // silently no-ops until then, so hold the path for TryOpenStartupCompanyAsync.
            if (!_startupCompanyHandled)
            {
                _macActivationFile = path;
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() => { _ = OpenFromFinderAsync(path); });
        };
    }

    private static async Task OpenFromFinderAsync(string path)
    {
        try
        {
            await OpenCompanyWithRetryAsync(path);
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogWarning($"Failed to open company from Finder: {ex.Message}", "Startup");
        }
    }

    /// <summary>
    /// Opens a company at startup instead of landing on the welcome screen, from either
    /// source: a file path the Windows shell passed for a double-clicked .argo file, or the
    /// AutoOpenRecentAfterUpdate flag set before an update restart. The command-line file
    /// wins; the flag is consumed either way so it can't fire on a later launch.
    /// </summary>
    private static async Task TryOpenStartupCompanyAsync()
    {
        var requestedFile = StartupFileArgs.GetCompanyFilePath(_startupArgs) ?? _macActivationFile;

        // Past this point the shell is up, so an Apple Event that lands later opens directly
        // instead of being parked in _macActivationFile where nothing would read it.
        _startupCompanyHandled = true;

        var reopenAfterUpdate = false;
        string? fileFromUpdatePrompt = null;
        if (SettingsService != null)
        {
            var updateSettings = SettingsService.GlobalSettings.Updates;
            reopenAfterUpdate = updateSettings.AutoOpenRecentAfterUpdate;
            fileFromUpdatePrompt = updateSettings.FileToOpenAfterUpdate;
            if (reopenAfterUpdate || fileFromUpdatePrompt != null)
            {
                updateSettings.AutoOpenRecentAfterUpdate = false;
                updateSettings.FileToOpenAfterUpdate = null;
                await SettingsService.SaveGlobalSettingsAsync();
            }
        }

        try
        {
            if (requestedFile == null && reopenAfterUpdate)
            {
                // The file that needed the update comes first: it is the one the user was
                // trying to open, and a failed open never added it to the recent list.
                requestedFile = fileFromUpdatePrompt is { } pending && File.Exists(pending)
                    ? pending
                    : SettingsService?.GetValidRecentCompanies().FirstOrDefault(File.Exists);
            }

            if (requestedFile == null)
                return;

            await OpenCompanyWithRetryAsync(requestedFile);
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogWarning($"Failed to open company at startup: {ex.Message}", "Startup");
        }
    }

    /// <summary>
    /// Initializes the exchange rate service for currency conversion.
    /// </summary>
    private static async Task InitializeExchangeRateServiceAsync()
    {
        try
        {
            // Pass the logger/telemetry so the rate-fetch diagnostics (batch outcome, per-date
            // fallback, unpriced dates) are actually recorded when an import can't get rates.
            var exchangeService = new ExchangeRateService(ErrorLogger, TelemetryManager);
            await exchangeService.InitializeAsync();
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogError(ex, ErrorCategory.Unknown, "Failed to initialize exchange rate service");
        }
    }

    private static void InitializeOperationTimingService()
    {
        try
        {
            var timing = new OperationTimingService(ErrorLogger);
            _ = timing.InitializeAsync();
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogError(ex, ErrorCategory.Unknown, "Failed to initialize operation timing service");
        }
    }

    /// <summary>
    /// Starts a periodic timer that checks for pending conversions and processes them
    /// when connectivity is restored. Runs every 15 seconds.
    /// </summary>
    private static void StartPendingConversionTimer()
    {
        // Dispose any existing timer
        _pendingConversionTimer?.Dispose();

        // Run the processing on the UI thread (the network fetch is awaited, so it doesn't block):
        // it mutates CompanyData.PendingConversions, which the save paths also mutate on the UI thread.
        // Without this the System.Threading.Timer callback would touch that List concurrently with a
        // save. Matches the window-Activated path, which already calls this on the UI thread.
        _pendingConversionTimer = new Timer(
            state => Avalonia.Threading.Dispatcher.UIThread.Post(() => { _ = TryProcessPendingConversionsAsync(); }),
            null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// Attempts to process pending conversions if online and there are pending items.
    /// Called by the timer and on window activation for faster reconnect detection.
    /// </summary>
    private static async Task TryProcessPendingConversionsAsync()
    {
        try
        {
            if (PendingConversionService == null || !PendingConversionService.HasPendingConversions)
                return;

            if (CompanyManager?.CompanyData == null)
                return;

            // Check if we're online before attempting to process
            var connectivityService = new ConnectivityService();
            var isOnline = await connectivityService.IsInternetAvailableAsync();
            if (!isOnline)
                return;

            await PendingConversionService.ProcessPendingConversionsAsync(CompanyManager.CompanyData);
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogWarning($"Pending conversion processing error: {ex.Message}", "App");
        }
    }

    /// <summary>
    /// Stops the pending conversion timer.
    /// </summary>
    private static void StopPendingConversionTimer()
    {
        _pendingConversionTimer?.Dispose();
        _pendingConversionTimer = null;
    }

    /// <summary>
    /// Removes the per-user .argo registrations written by 2.0.13 and earlier. They take
    /// precedence over the installer's per-machine association, so until they are gone the
    /// extension keeps pointing at whichever build last ran.
    /// </summary>
    private static async Task ClearLegacyFileAssociationsAsync()
    {
        if (SettingsService == null || SettingsService.GlobalSettings.LegacyFileAssociationsCleared)
            return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                ArgoFiles.RemoveLegacyRegistrations();
            }

            SettingsService.GlobalSettings.LegacyFileAssociationsCleared = true;
            await SettingsService.SaveGlobalSettingsAsync();
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogWarning($"Failed to clear legacy file associations: {ex.Message}", "FileAssociation");
        }
    }

    /// <summary>
    /// Syncs the event log to CompanyData before saving. Call this before any SaveCompanyAsync().
    /// </summary>
    private static void SyncEventLogBeforeSave()
    {
        if (EventLogService != null && CompanyManager?.CompanyData != null)
        {
            // Commit pending events: remove events for actions that were undone,
            // and mark remaining unsaved events as saved
            EventLogService.CommitPendingEvents(UndoRedoManager.UndoHistory);

            EventLogService.SyncToCompanyData(CompanyManager.CompanyData);
        }
    }

    /// <summary>
    /// Generates a stable file ID for biometric password storage from a file path.
    /// </summary>
    private static string GetBiometricFileId(string filePath)
    {
        // Use a hash of the normalized file path as the ID
        var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalizedPath));
        return Convert.ToHexString(bytes)[..32]; // First 32 hex chars (16 bytes)
    }

    /// <summary>
    /// Shows the OS image-picker, decodes the result into a Bitmap, and hands the
    /// pair off to the caller. Centralized so customer and supplier avatar pickers
    /// share the file-type filter and error handling.
    /// </summary>
    private static async Task PickAvatarAsync(string title, Action<string, Bitmap> onPicked, string errorTag)
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var files = await desktop.MainWindow!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [Utilities.FilePickerTypes.ImageFileType]
        });

        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        var prepared = await Task.Run(() => Helpers.ImageFileLoader.TryPrepare(path));
        if (prepared == null)
        {
            ErrorLogger?.LogWarning($"Could not read avatar image: {path}", errorTag);
            await ShowErrorDialogAsync(
                "Image Not Supported".Translate(),
                "That image could not be read. Try a PNG or JPEG.".Translate());
            return;
        }

        onPicked(prepared.Path, prepared.Bitmap);
    }

    /// <summary>
    /// Creates and opens a sample company with pre-populated demo data.
    /// </summary>
    /// <summary>
    /// Freezes the sample's insights for a free user, straight after it opens and before anything
    /// in it can change. Never fails the open: the page falls back to its teaser.
    /// </summary>
    private static async Task CaptureSampleInsightsAsync()
    {
        if (CompanyManager?.CompanyData is not { } data || _appShellViewModel?.SidebarViewModel.HasPremium == true)
            return;

        // The overlay stays up until the snapshot is taken, so nothing can go into the sample first.
        _mainWindowViewModel?.ShowLoading("Opening sample company...".Translate());
        try
        {
            // A row still waiting for its rate counts as zero in the insights, and the forecast cards
            // convert at today's rate, so both are settled before anything is frozen.
            if (PendingConversionService != null && data.PendingConversions.Count > 0)
            {
                PendingConversionService.ReconcileWithCompanyData(data);
                await PendingConversionService.ProcessPendingConversionsAsync(data);
                data.MarkAsSaved();
                if (_mainWindowViewModel != null)
                    _mainWindowViewModel.HasUnsavedChanges = false;
                if (_appShellViewModel != null)
                    _appShellViewModel.HeaderViewModel.HasUnsavedChanges = false;
            }
            await CurrencyService.TryWarmTodayRateAsync();

            // Offline the figures would be wrong rather than pending, so the page keeps its teaser.
            if (data.PendingConversions.Count > 0)
                return;

            await InsightsPageViewModel.CaptureSampleSnapshotAsync(data);
            _insightsPageViewModel?.OnSampleSnapshotChanged();
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogWarning($"Could not work out the sample company's insights: {ex.Message}", "Insights");
        }
        finally
        {
            _mainWindowViewModel?.HideLoading();
        }
    }

    private static async Task OpenSampleCompanyAsync()
    {
        if (CompanyManager == null || _mainWindowViewModel == null || _appShellViewModel == null || _fileService == null)
            return;

        try
        {
            var sampleFilePath = SampleCompanyService.GetSampleCompanyPath();
            var needsCreation = true;

            if (File.Exists(sampleFilePath))
            {
                var footer = await CompanyManager.GetFileInfoAsync(sampleFilePath);
                if (footer != null && IsVersionUpToDate(footer.Version))
                    needsCreation = false;
                else
                    SampleCompanyService.CleanupSampleCompanyFiles();
            }

            _mainWindowViewModel.ShowLoading("Opening sample company...".Translate());

            if (needsCreation)
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "ArgoBooks.Resources.SampleCompanyData.xlsx";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _mainWindowViewModel.HideLoading();
                    await ShowErrorDialogAsync("Error".Translate(), "Sample company data not found.".Translate());
                    return;
                }

                var importService = new SpreadsheetImportService(ErrorLogger, TelemetryManager);
                var sampleService = new SampleCompanyService(_fileService, importService);

                // Validate first (same as regular imports)
                var validationContext = await sampleService.ValidateSampleCompanyAsync(stream);
                var validationResult = validationContext.ValidationResult;

                _mainWindowViewModel.HideLoading();

                // Only interrupt with the issues dialog when there's something the user must act
                // on (errors or issues that can't be auto-fixed); when everything is auto-fixable,
                // proceed straight to import. Mirrors the regular import path so the dialog never
                // opens in an empty "no issues found" state.
                if (validationResult.Errors.Count > 0 || validationResult.HasNonAutoFixableIssues)
                {
                    var validationDialog = _appShellViewModel.ImportValidationDialogViewModel;
                    var dialogResult = await validationDialog.ShowAsync(validationResult);

                    if (dialogResult == ImportValidationDialogResult.Cancel)
                    {
                        SampleCompanyService.CleanupValidationContext(validationContext);
                        return;
                    }
                }

                _mainWindowViewModel.ShowLoading("Opening sample company...".Translate());

                // Finish import
                sampleFilePath = await sampleService.FinishSampleCompanyCreationAsync(validationContext);
            }

            var success = await CompanyManager.OpenCompanyAsync(sampleFilePath);

            if (success)
            {
                if (CompanyManager.CompanyData != null)
                {
                    if (SampleCompanyService.TimeShiftSampleData(CompanyManager.CompanyData))
                    {
                        CompanyManager.NotifyDataChanged();

                        // Suppress the "Saved" indicator for this internal save
                        _suppressSavedFeedback = true;
                        await CompanyManager.SaveCompanyAsync();
                    }
                    CompanyManager.CompanyData.MarkAsSaved();

                    // Reset unsaved changes since time-shift is automatic
                    _mainWindowViewModel.HasUnsavedChanges = false;
                    _appShellViewModel.HeaderViewModel.HasUnsavedChanges = false;
                    SyncSampleCompanyState();

                    // Set date range to show full year of sample data
                    ChartSettingsService.Instance.SelectedDateRange = "Last 365 Days";

                    // The time shift moved every date, so their rates may not be cached yet.
                    await CurrencyService.WarmCompanyRatesAsync(CompanyManager.CompanyData);

                    await CaptureSampleInsightsAsync();
                }

                // Exploring in the sample company looks identical to real use on the
                // dashboard otherwise: the same CustomerCreated and ProductCreated events
                // arrive with no CompanyCreated before them, which reads as lost telemetry
                // rather than as someone evaluating with the demo data.
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.SampleCompanyOpened);

                await LoadRecentCompaniesAsync();
            }
            else
            {
                _mainWindowViewModel.HideLoading();
                await ShowErrorDialogAsync("Error".Translate(), "Failed to open sample company.".Translate());
            }
        }
        catch (CompanyAlreadyOpenException)
        {
            _mainWindowViewModel.HideLoading();
            await ShowCompanyAlreadyOpenAsync();
        }
        catch (Exception ex)
        {
            _mainWindowViewModel.HideLoading();
            ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to open sample company");
            await ShowErrorDialogAsync("Error".Translate(), "Failed to open sample company: {0}".TranslateFormat(ex.Message));
        }
    }

    /// <summary>
    /// Checks if the sample company version is up to date with the current app version.
    /// </summary>
    private static bool IsVersionUpToDate(string sampleVersion)
    {
        if (!Version.TryParse(sampleVersion, out var sampleVer))
            return false;

        var appVersion = AppInfo.AssemblyVersion;
        if (appVersion == null)
            return false;

        // Sample is up to date if major.minor.build matches
        return sampleVer.Major == appVersion.Major &&
               sampleVer.Minor == appVersion.Minor &&
               sampleVer.Build == appVersion.Build;
    }

    /// <summary>
    /// Opens the edit company modal with the current company information.
    /// </summary>
    public static void OpenEditCompanyModal(string? contextMessage = null)
    {
        if (CompanyManager?.IsCompanyOpen != true || _appShellViewModel == null) return;

        var settings = CompanyManager.CurrentCompanySettings;
        var logoPath = CompanyManager.CurrentCompanyLogoPath;
        var logo = LoadBitmapFromPath(logoPath);
        _appShellViewModel.EditCompanyModalViewModel.Open(
            settings?.Company.Name ?? "",
            settings?.Company.BusinessType,
            settings?.Company.Industry,
            logo,
            settings?.Company.Phone,
            settings?.Company.Country,
            settings?.Company.City,
            settings?.Company.Address,
            settings?.Company.ProvinceState,
            settings?.Company.Email,
            CompanyManager.CompanyData!.Settings.Localization.Currency,
            contextMessage);
    }

    /// <summary>
    /// Restores a company logo file and updates the LogoFileName setting.
    /// Used by undo/redo to restore logo state.
    /// </summary>
    private static void RestoreCompanyLogo(CompanySettings settings, string? logoFileName, byte[]? logoBytes, string? tempDir)
    {
        settings.Company.LogoFileName = logoFileName;
        if (tempDir != null && logoFileName != null && logoBytes != null)
        {
            var path = Path.Combine(tempDir, logoFileName);
            File.WriteAllBytes(path, logoBytes);
        }
    }

    /// <summary>
    /// Refreshes company-related UI elements (sidebar, company switcher, main window title).
    /// Used by undo/redo to update the UI after restoring company data.
    /// </summary>
    private static void RefreshCompanyUi(string companyName)
    {
        if (_appShellViewModel == null) return;
        _mainWindowViewModel?.OpenCompany(companyName);
        var logo = LoadBitmapFromPath(CompanyManager?.CurrentCompanyLogoPath);
        _appShellViewModel.SetCompanyInfo(companyName, logo);
        _appShellViewModel.ApplyFeatureVisibility(CompanyManager?.CompanyData?.Settings);
        _appShellViewModel.CompanySwitcherPanelViewModel.SetCurrentCompany(
            companyName,
            CompanyManager?.CurrentFilePath,
            logo);
        _reportsPageViewModel?.RefreshCanvas();
    }

    private static async Task<bool> ConfirmCancelAsync()
    {
        var dialog = ConfirmationDialog;
        if (dialog == null) return true;
        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "Cancel Operation?".Translate(),
            Message = "Are you sure you want to cancel?".Translate(),
            PrimaryButtonText = "Yes".Translate(),
            CancelButtonText = "No".Translate()
        });
        return result == ConfirmationResult.Primary;
    }

    /// <summary>
    /// Performs the AI-powered import flow: analyze → review → validate → import.
    /// </summary>
    private static async Task PerformAiImportAsync(string filePath, CompanyData companyData, bool isCsv)
    {
        if (_appShellViewModel == null) return;

        // The name the user actually picked. filePath may later be swapped for a temp file
        // (legacy .xls conversion, or experimental layout normalization), so capture the
        // display name up front and use it in all user-facing UI instead of the temp path.
        var originalFileName = Path.GetFileName(filePath);

        var analysisCts = new CancellationTokenSource();
        _mainWindowViewModel?.ShowLoading("Analyzing spreadsheet structure...".Translate(), "Reading file...", 0, analysisCts, ConfirmCancelAsync);
        await Task.Yield(); // Allow UI to render the loading overlay before heavy work begins

        // Legacy .xls (BIFF) is not read by the pipeline directly. Convert it to a temp
        // .xlsx up front so the rest of the flow only ever sees .xlsx/.csv (unchanged).
        // Note: a true .xlsx ends in ".xls" only via the longer ".xlsx" suffix, so we
        // explicitly exclude .xlsx here. isCsv is left untouched (stays false for .xls).
        if (!isCsv
            && filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
            && !filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                filePath = await Task.Run(() => LegacyXlsConverter.ConvertXlsToTempXlsx(filePath));
            }
            catch (Exception ex)
            {
                _mainWindowViewModel?.HideLoading();
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportFailed, "xls-convert");
                ErrorLogger?.LogError(ex, ErrorCategory.Import, "Failed to convert legacy .xls file for import");
                await ShowErrorDialogAsync(
                    "Import Failed".Translate(),
                    "This .xls file could not be read. Try re-saving it as .xlsx and importing that instead.".Translate());
                return;
            }
        }

        // Check rate limit via server-side API
        using var usageService = new UsageLimitService(UsageLimit.AiImports(), LicenseService, ErrorLogger);
        var usageCheck = await usageService.CheckUsageAsync();

        if (!usageCheck.Allowed)
        {
            _mainWindowViewModel?.HideLoading();

            // A populated ErrorMessage means the usage check couldn't complete (offline or
            // server unreachable) rather than a real limit being hit, so show the connection
            // error instead of a misleading "0/0" import-limit prompt.
            // "quota" is the monthly limit; "usage-check" is the server being unreachable.
            _ = TelemetryManager?.TrackFeatureAsync(
                FeatureName.ImportFailed,
                string.IsNullOrEmpty(usageCheck.ErrorMessage) ? "quota" : "usage-check");

            if (!string.IsNullOrEmpty(usageCheck.ErrorMessage))
            {
                await UpgradePromptHelper.ShowUsageCheckFailedAsync(usageCheck.ErrorMessage);
            }
            else
            {
                await UpgradePromptHelper.ShowAiImportLimitPromptAsync(
                    usageCheck.Used,
                    usageCheck.MonthlyLimit,
                    usageCheck.ResetsAt);
            }
            return;
        }

        var geminiService = new GeminiService(ErrorLogger, TelemetryManager);
        if (!geminiService.IsConfigured)
        {
            _mainWindowViewModel?.HideLoading();
            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportFailed, "not-configured");
            await ShowErrorDialogAsync(
                "AI Not Configured".Translate(),
                "AI-powered import requires portal access. Please register your company first.".Translate());
            return;
        }

        // AI layout interpretation for messy spreadsheets (long preambles, merged/multi-row
        // headers, cross-tabs, stacked tables): rewrite messy sheets into clean single-header
        // tables before analysis. The cheap, local LayoutGate inside NormalizeAsync skips clean
        // sheets and returns the ORIGINAL path when nothing needs interpreting, so normal imports
        // pay no extra cost. Any failure falls back to the original file, so it can never break
        // an import. CSV files are single-table and never need layout interpretation.
        if (!isCsv)
        {
            try
            {
                // Run off the UI thread: NormalizeAsync opens the workbook with ClosedXML
                // synchronously (several seconds for a real file), which would otherwise block
                // the loading overlay from even painting until it finished.
                var normalizer = new LayoutNormalizationService(geminiService, ErrorLogger);
                filePath = await Task.Run(() => normalizer.NormalizeAsync(filePath, analysisCts.Token), analysisCts.Token);
            }
            catch (Exception ex)
            {
                ErrorLogger?.LogError(ex, ErrorCategory.Import,
                    "AI layout interpretation failed; importing the original file unchanged");
                // filePath is left as the original path: the import proceeds normally.
            }
        }

        var analysisService = new SpreadsheetAnalysisService(geminiService, ErrorLogger, CompanyManager!.CurrentCompanySettings?.Company.Country);
        var importService = new SpreadsheetImportService(ErrorLogger, TelemetryManager, geminiService);

        // Drive the analysis bar from the learned duration estimate (smooth easing toward the pooled
        // p50/p90, asymptoting near the ceiling and completing when the call returns).
        // The service reports only the status detail.
        var analysisDetail = "Reading file...".Translate();
        using var analysisTicker = new EstimatedProgressTicker(
            OperationKind.SpreadsheetAnalysis,
            pct => _mainWindowViewModel?.ShowLoading("Analyzing spreadsheet structure...".Translate(), analysisDetail, pct, analysisCts, ConfirmCancelAsync));
        var analysisProgress = new Progress<(string detail, double percent)>(p => analysisDetail = p.detail);

        try
        {
            // Step 1: AI Analysis. Run off the UI thread: the analyzer opens the workbook
            // synchronously before its network calls, which would otherwise freeze the loading
            // overlay. The Progress callback was created on the UI thread, so it still marshals back.
            analysisTicker.Start();
            var analysis = isCsv
                ? await Task.Run(() => analysisService.AnalyzeCsvAsync(filePath, analysisCts.Token, analysisProgress), analysisCts.Token)
                : await Task.Run(() => analysisService.AnalyzeAsync(filePath, analysisCts.Token, analysisProgress), analysisCts.Token);
            analysisTicker.Stop();

            await Task.Yield();
            _mainWindowViewModel?.HideLoading();

            // Fall back to the whole-file AI rescue when normal analysis produced nothing importable:
            // either it returned no result/sheets at all, or it returned sheets but classified every one
            // as Unknown/unsupported (e.g. a Profit and Loss report). Both are dead-ends on the normal
            // path, so hand them to the rescue, which either extracts records or explains, in vetted copy,
            // why the file cannot be imported.
            if (analysis == null || analysis.Sheets.Count == 0 || analysis.Sheets.All(s => !s.IsIncluded))
            {
                await TryRescueImportAsync(
                    filePath, isCsv, companyData, analysisService, importService, originalFileName, usageService);
                return;
            }

            // Show the file the user actually selected, not the temp file we may have analyzed.
            analysis.FileName = originalFileName;

            // Step 2: Show mapping review dialog
            var mappingDialog = _appShellViewModel.ImportMappingDialogViewModel;

            _ = TelemetryManager?.TrackFeatureAsync(
                FeatureName.ImportPreviewShown, $"sheets:{analysis.Sheets.Count(sh => sh.IsIncluded)}");

            var dialogResult = await mappingDialog.ShowAsync(analysis, usageCheck.Remaining, usageCheck.MonthlyLimit);

            if (dialogResult == ImportMappingDialogResult.Cancel)
            {
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "mapping");
                return;
            }

            // Get user-updated analysis (they may have changed entity types or excluded sheets)
            var updatedAnalysis = mappingDialog.GetUpdatedAnalysis();
            if (updatedAnalysis == null) return;

            // Filter to only included sheets
            var includedSheets = updatedAnalysis.Sheets.Where(s => s.IsIncluded).ToList();
            if (includedSheets.Count == 0)
            {
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "no-sheets");
                await ShowInfoDialogAsync("Info".Translate(), "No sheets were selected for import.".Translate());
                return;
            }

            // Start timing after user approval, excludes UI wait time
            var importStopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Create snapshot for undo
            var snapshot = CreateCompanyDataSnapshot(companyData);

            // Step 3: Split sheets by processing tier
            // Respect the AI's tier recommendation for both Excel and CSV files.
            // Mixed-type CSVs (e.g., expenses + payments in one file) need Tier 2 LLM processing.
            var tier1Sheets = includedSheets.Where(s => s.Tier == ProcessingTier.Tier1_Mapping).ToList();
            var tier2Sheets = includedSheets.Where(s => s.Tier == ProcessingTier.Tier2_LlmProcessing).ToList();

            var importOptions = new ImportOptions
            {
                SkipExistingRecords = mappingDialog.SkipExistingRecords
            };

            // Detect currency written into the amount cells (symbols/codes). Unambiguous cases
            // (an explicit code, or a symbol used by one currency like £/€) resolve silently; an
            // ambiguous symbol like "$" prompts the user once and is applied to every matching row.
            try
            {
                var currencyScan = CurrencyImportPreparer.ScanWorkbook(filePath, updatedAnalysis);
                if (currencyScan.Ambiguities.Count > 0)
                {
                    var companyCurrency = companyData.Settings.Localization.Currency;
                    var currencyDialog = _appShellViewModel.CurrencyAmbiguityDialogViewModel;
                    var currencyResult = await currencyDialog.ShowAsync(currencyScan.Ambiguities, companyCurrency);
                    if (currencyResult == CurrencyAmbiguityDialogResult.Cancel)
                    {
                        _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "currency-prompt");
                        return;
                    }

                    CurrencyImportPreparer.ApplyResolution(currencyScan, currencyDialog.Resolution);
                    importOptions.SymbolResolution = currencyDialog.Resolution
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                }
                importOptions.RowCurrencyBySheet = currencyScan.Resolved;

                // If any non-USD currency was detected, fetch the EXACT-date rate for every
                // transaction date before importing, so money never converts at a wrong-date rate
                // (see docs/Calculations.md). If rates can't be fetched (offline or server down),
                // pause with a connect-and-retry prompt. Best-effort: any row the scan misses still
                // self-heals via IsPendingConversion. A row that names no currency is in the
                // company's, so a non-USD company always needs its rates.
                var hasNonUsd =
                    currencyScan.Resolved.Values.SelectMany(m => m.Values)
                        .Concat(importOptions.SymbolResolution.Values)
                        .Append(companyData.Settings.Localization.Currency)
                        .Any(c => !string.IsNullOrEmpty(c) && !string.Equals(c, "USD", StringComparison.OrdinalIgnoreCase));
                if (hasNonUsd && ExchangeRateService.Instance is { } exchangeRates)
                {
                    var importDates = importService.CollectTransactionDates(filePath, updatedAnalysis);
                    var readinessSvc = new RateReadinessService(exchangeRates, new ConnectivityService(), ErrorLogger);

                    // Fetching the exact-date rate for every transaction date can take seconds to
                    // minutes, and it runs before any rows are inserted. Show it as an explicit,
                    // cancelable phase with a determinate progress bar so the import doesn't look frozen.
                    using var rateCts = new CancellationTokenSource();
                    _mainWindowViewModel?.ShowLoading("Fetching exchange rates...".Translate(), null, 0, rateCts, ConfirmCancelAsync);
                    var rateProgress = new Progress<int>(pct => _mainWindowViewModel?.UpdateLoadingProgress(pct));

                    while (true)
                    {
                        RateReadiness readiness;
                        try
                        {
                            readiness = await readinessSvc.EnsureRatesAsync(importDates, rateProgress, rateCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            _mainWindowViewModel?.HideLoading();
                            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "rate-fetch");
                            return; // user canceled while fetching rates
                        }
                        catch (Exception ex)
                        {
                            ErrorLogger?.LogError(ex, ErrorCategory.Import, "Rate readiness check failed");
                            readiness = new RateReadiness(RateReadinessStatus.Unavailable, RateUnavailableReason.Unknown, []);
                        }

                        if (readiness.Status == RateReadinessStatus.Ready)
                        {
                            if (readiness.FutureDatesDeferred.Count > 0)
                                ErrorLogger?.LogWarning(
                                    $"{readiness.FutureDatesDeferred.Count} future-dated rows will import as pending (no rate yet).",
                                    "Import");
                            break;
                        }

                        // Pause the progress overlay while the connect-and-retry prompt is up, then restore it.
                        _mainWindowViewModel?.HideLoading();
                        var choice = await _appShellViewModel.RateUnavailableDialogViewModel.ShowAsync(readiness.Reason);
                        if (choice == RateRetryResult.Cancel)
                        {
                            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "rates-unavailable");
                            return; // abort: nothing imported
                        }
                        _mainWindowViewModel?.ShowLoading("Fetching exchange rates...".Translate(), null, 0, rateCts, ConfirmCancelAsync);
                    }

                    _mainWindowViewModel?.HideLoading();
                }
            }
            catch (Exception ex)
            {
                ErrorLogger?.LogError(ex, ErrorCategory.Import,
                    "In-cell currency detection failed; importing without per-row currency");
            }

            // Tier 1: Validate with mappings
            SpreadsheetImportResult? tier1Result = null;
            if (tier1Sheets.Count > 0)
            {
                using var validationCts = new CancellationTokenSource();
                _mainWindowViewModel?.ShowLoading("Validating mapped data...".Translate(), cts: validationCts, cancelConfirmation: ConfirmCancelAsync);

                var validationResult = isCsv
                    ? new ImportValidationResult() // CSV validation is simpler
                    : await importService.ValidateWithMappingsAsync(filePath, companyData, updatedAnalysis);

                _mainWindowViewModel?.HideLoading();

                if (validationResult.HasIssues)
                {
                    // Auto-create any missing references (suppliers, customers, categories,
                    // etc.) silently. The user shouldn't have to approve creating placeholder
                    // records, it should "just work".
                    if (validationResult.HasMissingReferences)
                        importOptions.AutoCreateMissingReferences = true;

                    // Only interrupt with the issues dialog when there's something the user
                    // must act on: critical errors or issues that can't be auto-fixed. When
                    // everything is auto-fixable, proceed straight to import.
                    if (validationResult.Errors.Count > 0 || validationResult.HasNonAutoFixableIssues)
                    {
                        var validationDialog = _appShellViewModel.ImportValidationDialogViewModel;
                        var valResult = await validationDialog.ShowAsync(validationResult);

                        if (valResult == ImportValidationDialogResult.Cancel)
                        {
                            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "validation-prompt");
                            return;
                        }

                        if (valResult == ImportValidationDialogResult.CreateMissingAndImport)
                            importOptions.AutoCreateMissingReferences = true;

                        if (validationResult.Errors.Count > 0)
                        {
                            _ = TelemetryManager?.TrackFeatureAsync(
                                FeatureName.ImportFailed, $"validation-errors:{validationResult.Errors.Count}");
                            return;
                        }
                    }
                }

                // Import Tier 1 data
                var importCts = new CancellationTokenSource();
                _mainWindowViewModel?.ShowLoading("Importing data...".Translate(), cts: importCts, cancelConfirmation: ConfirmCancelAsync);

                var importProgress = new Progress<(string detail, double percent)>(p =>
                {
                    _mainWindowViewModel?.ShowLoading("Importing data...".Translate(), p.detail, p.percent, importCts, ConfirmCancelAsync);
                });

                // It runs off the UI thread, where the conversion pass changes the same records and queue.
                using (PendingConversionService is { } conversions ? await conversions.SuspendAsync() : null)
                {
                    tier1Result = isCsv
                        ? await importService.ImportCsvWithMappingsAsync(filePath, companyData, updatedAnalysis, importOptions, importCts.Token, importProgress)
                        : await importService.ImportWithMappingsAsync(filePath, companyData, updatedAnalysis, importOptions, importCts.Token, importProgress);
                }

                // AI-categorize any products that ended up without a category
                // (skip if Tier 2 sheets will handle it after their processing)
                if (tier2Sheets.Count == 0)
                {
                    _mainWindowViewModel?.ShowLoading("Categorizing products...".Translate(), progress: 95, cts: importCts, cancelConfirmation: ConfirmCancelAsync);
                    await importService.AiCategorizeMissingProductsAsync(companyData, importCts.Token);
                }

                // Yield to let any pending Progress<T> callbacks (dispatched via
                // SynchronizationContext.Post) execute before hiding the loading
                // overlay, otherwise the last callback can re-show it after HideLoading.
                await Task.Yield();
                _mainWindowViewModel?.HideLoading();
            }

            // Tier 2: LLM row processing
            var totalImported = 0;
            var totalSkipped = 0;
            var allSheetResults = new List<SheetImportResult>();
            if (tier2Sheets.Count > 0)
            {
                var tier2Cts = new CancellationTokenSource();

                _mainWindowViewModel?.ShowLoading(
                    "AI processing...".Translate(),
                    $"Processing {tier2Sheets.Count} sheet(s)...",
                    progress: 0,
                    cts: tier2Cts,
                    cancelConfirmation: ConfirmCancelAsync);

                // Pre-read all Tier 2 sheet data from the file once to avoid
                // re-parsing the workbook for each sheet.
                var sheetDataMap = await SpreadsheetAnalysisService.ReadSheetDataAsync(
                    filePath, tier2Sheets, tier2Cts.Token);

                // Compute total rows across all Tier 2 sheets for aggregate progress
                var totalRowsAllSheets = sheetDataMap.Values.Sum(d => d.Rows.Count);
                var processedRowCounts = new int[tier2Sheets.Count];

                // Use a timer to show estimated progress while waiting for
                // the LLM to finish. Chunk-level progress only fires after
                // each chunk completes, so with few rows (< chunk size) the
                // bar would otherwise stay at 0% the entire time.
                var estimatedProgress = 0.0;
                var chunkProgressReceived = false;
                var estimateTimerCts = new CancellationTokenSource();

                var timerTask = Task.Run(async () =>
                {
                    using var estimateTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
                    while (await estimateTimer.WaitForNextTickAsync(estimateTimerCts.Token))
                    {
                        if (chunkProgressReceived) break;
                        estimatedProgress = Math.Min(estimatedProgress + 1, 90);
                        _mainWindowViewModel?.ShowLoading(
                            "AI processing...".Translate(),
                            "Processing rows...",
                            estimatedProgress,
                            tier2Cts,
                            ConfirmCancelAsync);
                    }
                }, tier2Cts.Token);

                // Phase A: Process all Tier 2 sheets in parallel (LLM calls are stateless)
                var sheetTasks = tier2Sheets.Select((sheet, idx) =>
                {
                    if (!sheetDataMap.TryGetValue(sheet.SourceSheetName, out var data))
                        return Task.FromResult(new List<LlmProcessedData>());

                    return analysisService.ProcessAllChunksAsync(
                        data.Headers, data.Rows, sheet,
                        new Progress<(int processed, int total)>(p =>
                        {
                            chunkProgressReceived = true;
                            Interlocked.Exchange(ref processedRowCounts[idx], p.processed);
                            var totalProcessed = processedRowCounts.Sum();
                            var pct = totalRowsAllSheets > 0
                                ? (double)totalProcessed / totalRowsAllSheets * 100
                                : -1;
                            _mainWindowViewModel?.ShowLoading(
                                "AI processing...".Translate(),
                                $"{totalProcessed}/{totalRowsAllSheets} rows",
                                pct,
                                tier2Cts,
                                ConfirmCancelAsync);
                        }),
                        tier2Cts.Token);
                }).ToArray();

                var allProcessedChunks = await Task.WhenAll(sheetTasks);

                // Stop the estimate timer and wait for it to exit cleanly
                estimateTimerCts.Cancel();
                try { await timerTask; } catch (OperationCanceledException) { }

                // Phase B: Import sequentially (CompanyData mutation is not thread-safe)
                _mainWindowViewModel?.ShowLoading(
                    "Importing data...".Translate(),
                    progress: 90,
                    cts: tier2Cts,
                    cancelConfirmation: ConfirmCancelAsync);
                for (int i = 0; i < tier2Sheets.Count; i++)
                {
                    var tier2Result = importService.ImportProcessedEntities(
                        companyData, allProcessedChunks[i], tier2Sheets[i].SourceSheetName, importOptions);
                    totalImported += tier2Result.Inserted;
                    totalSkipped += tier2Result.Skipped;
                    allSheetResults.Add(tier2Result);
                }

                // AI-categorize any products that ended up without a category (single call for all sheets)
                _mainWindowViewModel?.ShowLoading(
                    "AI processing...".Translate(),
                    "Categorizing products...",
                    95,
                    tier2Cts,
                    ConfirmCancelAsync);
                await importService.AiCategorizeMissingProductsAsync(companyData, tier2Cts.Token);

                // Yield to let any pending Progress<T> callbacks (dispatched via
                // SynchronizationContext.Post) execute before hiding the loading
                // overlay, otherwise the last callback can re-show it after HideLoading.
                await Task.Yield();
                _mainWindowViewModel?.HideLoading();
            }

            // Track import duration telemetry (covers analysis, validation, all tiers, and categorization)
            importStopwatch.Stop();
            var importContext = isCsv ? "ai-csv" : "ai-xlsx";
            if (tier2Sheets.Count > 0 && tier1Sheets.Count == 0)
                importContext = isCsv ? "ai-csv-tier2" : "ai-xlsx-tier2";
            else if (tier2Sheets.Count > 0)
                importContext = isCsv ? "ai-csv-mixed" : "ai-xlsx-mixed";
            _ = TelemetryManager?.TrackFeatureAsync(
                FeatureName.DataImported, importContext, importStopwatch.ElapsedMilliseconds);

            // Create snapshot for redo
            var importedSnapshot = CreateCompanyDataSnapshot(companyData);

            // Record undo action. Besides restoring the data, each direction must refresh the UI
            // the same way the import itself does: reload the Bank Matching page (a workbook may
            // have routed bank rows into a BankImportSession) and rebuild the current page so its
            // charts/widgets repaint with the restored data. Without the page rebuild the charts
            // stay stale after undo/redo until the user navigates away and back.
            void RestoreImportSnapshotAndRefresh(string snapshotJson)
            {
                RestoreCompanyDataFromSnapshot(companyData, snapshotJson);
                CompanyManager?.MarkAsChanged();
                _bankMatchingPageViewModel?.Reload();
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => NavigationService?.RefreshCurrentPage(),
                    Avalonia.Threading.DispatcherPriority.Background);
            }

            UndoRedoManager.RecordAction(new DelegateAction(
                "AI import spreadsheet data".Translate(),
                () => RestoreImportSnapshotAndRefresh(snapshot),
                () => RestoreImportSnapshotAndRefresh(importedSnapshot)
            ));

            CompanyManager.MarkAsChanged();

            // Auto-switch date range to "All Time" so imported data is visible on dashboard/analytics
            // (imported data may be from any time period, not necessarily the current month)
            ChartSettingsService.Instance.SelectedDateRange = "All Time";

            // Combine Tier 1 and Tier 2 counts
            var totalUpdated = 0;
            if (tier1Result != null)
            {
                totalImported += tier1Result.TotalImported;
                totalUpdated += tier1Result.TotalUpdated;
                totalSkipped += tier1Result.TotalSkipped;
                allSheetResults.AddRange(tier1Result.SheetResults);
            }

            var totalProcessed = totalImported + totalUpdated;

            // Collect all warnings: the top-level tier-1 warnings plus every per-sheet warning
            // (reference-resolution "created a new customer/supplier" and re-import notices added on
            // the Tier 2 path), which would otherwise never reach the user. Distinct() guards against
            // a per-sheet warning that was also surfaced at the top level.
            var allWarnings = (tier1Result?.Warnings ?? [])
                .Concat(allSheetResults.SelectMany(sr => sr.Warnings))
                .Distinct()
                .ToList();

            // Report any sheets that were out of scope (detected type Unknown or confidence too low)
            var unsupportedSheets = updatedAnalysis.Sheets.Where(s => s.UnsupportedReason != null).ToList();
            foreach (var unsupported in unsupportedSheets)
                allWarnings.Add($"Sheet \"{unsupported.SourceSheetName}\" was skipped: {unsupported.UnsupportedReason}");

            // If part of the AI analysis failed, tell the user their import is partial.
            if (!string.IsNullOrEmpty(updatedAnalysis.PartialAnalysisWarning))
                allWarnings.Add(updatedAnalysis.PartialAnalysisWarning);

            // Collect skip reasons from all sheets
            var allSkipReasons = allSheetResults
                .SelectMany(sr => sr.SkipReasons)
                .GroupBy(r => r)
                .Select(g => g.Count() > 1 ? $"{g.Key} (\u00d7{g.Count()})" : g.Key)
                .ToList();

            // Collect unimported rows from every sheet
            var allUnimported = allSheetResults.SelectMany(r => r.UnimportedRows).ToList();

            // Bank statement rows are routed to the Bank Matching page (their own session), not
            // counted as new/updated book records, so factor them into "needs save" separately.
            var totalBankRouted = allSheetResults.Sum(sr => sr.BankMatchingImported);

            // Only a run that brought something in uses up an import.
            if (totalProcessed > 0 || totalBankRouted > 0)
            {
                await usageService.IncrementUsageAsync();
                TutorialService.Instance.CompleteChecklistItem(TutorialService.ChecklistItems.ImportData);
            }

            // Show import result dialog
            var resultDialog = _appShellViewModel.ImportResultDialogViewModel;
            await resultDialog.ShowAsync(
                originalFileName,
                allSheetResults,
                totalImported, totalUpdated, totalSkipped,
                allSkipReasons, allWarnings, totalProcessed > 0 || totalBankRouted > 0,
                allUnimported);

            // Rebuild the current page so its charts/widgets show the freshly imported data.
            // This mirrors navigating away and back, which is otherwise needed because chart
            // widgets don't reliably repaint while hidden behind the import dialogs. Posted at
            // Background priority so it runs after the result dialog has finished closing.
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => NavigationService?.RefreshCurrentPage(),
                Avalonia.Threading.DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            _mainWindowViewModel?.HideLoading();
            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "cancelled");
        }
        catch (Exception ex)
        {
            _mainWindowViewModel?.HideLoading();
            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportFailed, "exception");
            ErrorLogger?.LogError(ex, ErrorCategory.Import, "Failed to perform AI import");
            await ShowErrorDialogAsync(
                "Import Failed".Translate(),
                "Failed to import data:\n\n{0}".TranslateFormat(ex.Message));
        }
    }

    /// <summary>
    /// Handles restoring from a .argobk backup file.
    /// Copies the backup to a new .argo file chosen by the user, then opens it as a new company.
    /// The original company file is left untouched.
    /// </summary>
    private static async Task RestoreFromBackupAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (CompanyManager == null || _appShellViewModel == null) return;

        // Show open file dialog for backup files
        var files = await desktop.MainWindow!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Restore from Backup".Translate(),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Argo Books Backup")
                {
                    Patterns = ["*.argobk"]
                }
            ]
        });

        if (files.Count == 0) return;

        var backupPath = files[0].Path.LocalPath;

        // Suggest a name based on the backup filename (strip .argobk, add .argo)
        var suggestedName = Path.GetFileNameWithoutExtension(backupPath);

        // Show save dialog to choose where to restore the company file
        var saveFile = await desktop.MainWindow!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Restored Company As".Translate(),
            SuggestedFileName = $"{suggestedName}.argo",
            DefaultExtension = "argo",
            FileTypeChoices =
            [
                new FilePickerFileType("Argo Books Files")
                {
                    Patterns = ["*.argo"]
                }
            ]
        });

        if (saveFile == null) return;

        var destPath = saveFile.Path.LocalPath;

        // Both come before the copy. After it, saving the open company's changes, or another
        // window's save, would write over the file just restored.
        if (CompanyManager.IsOpenInAnotherInstance(destPath))
        {
            await ShowCompanyAlreadyOpenAsync();
            return;
        }
        if (!await ConfirmLeavingCompanyAsync()) return;

        try
        {
            // Copy the backup file to the new .argo path
            File.Copy(backupPath, destPath, overwrite: true);

            // Open it as a new company (this closes the current one)
            await OpenCompanyWithRetryAsync(destPath, leavingConfirmed: true);
            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.BackupRestored);
        }
        catch (Exception ex)
        {
            _mainWindowViewModel?.HideLoading();
            ErrorLogger?.LogError(ex, ErrorCategory.Import, "Failed to restore from backup");
            await ShowErrorDialogAsync("Restore Failed".Translate(), "Failed to restore from backup: {0}".TranslateFormat(ex.Message));
        }
    }

    /// <summary>
    /// Opens a file picker for a bank statement and passes the chosen file directly to
    /// the BankStatementImportModal without navigating to the Bank Matching page first.
    /// Called from the Expenses/Revenue "Import bank statement" menu item.
    /// </summary>
    public static async Task OpenBankStatementImportAsync()
    {
        if (BankStatementImportModalViewModel == null) return;
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportOpened, "bank");

        var file = await desktop.MainWindow!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Bank Statement".Translate(),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Bank statements") { Patterns = ["*.csv", "*.xlsx", "*.xls", "*.pdf"] }
            ]
        });
        if (file.Count == 0)
        {
            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "bank:file-picker");
            return;
        }

        await BankStatementImportModalViewModel.OpenAsync(file[0].Path.LocalPath);
    }

    /// <summary>
    /// Imports a bank statement via the smart importer (parse only, no commit) and shows it on the
    /// Bank Matching page. Triggered by the page's Import button.
    /// </summary>
    private static async Task PerformBankImportAsync()
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        if (CompanyManager?.CompanyData is not { } companyData)
        {
            await ShowErrorDialogAsync("Error".Translate(), "No company is currently open.".Translate());
            return;
        }

        _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportOpened, "bank-matching");

        var file = await desktop.MainWindow!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Bank Statement".Translate(),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Bank statements") { Patterns = ["*.csv", "*.xlsx", "*.xls", "*.pdf"] }
            ]
        });
        if (file.Count == 0)
        {
            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "bank-matching:file-picker");
            return;
        }

        var filePath = file[0].Path.LocalPath;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        _mainWindowViewModel?.ShowLoading("Scanning bank statement...".Translate());
        await Task.Yield();

        try
        {
            List<Core.Models.BankMatching.BankStatementLine> lines;
            if (ext == ".pdf")
            {
                _mainWindowViewModel?.HideLoading();
                lines = await ImportPdfStatementAsync(filePath);
                if (lines.Count == 0) return; // gated out, cancelled, or extraction failed
            }
            else
            {
                var parser = new BankStatementImportService(ErrorLogger);
                var isCsv = ext == ".csv";

                // Try local column detection first (instant, no AI). Fall back to AI column mapping
                // only when the headers aren't recognized locally.
                lines = isCsv
                    ? await parser.ParseCsvAsync(filePath)
                    : await parser.ParseExcelAsync(filePath);

                if (lines.Count == 0)
                {
                    // Null: the user is out of bank imports and has just been told so.
                    if (await TryAiParseBankStatementAsync(filePath, isCsv, parser) is not { } aiLines) return;
                    lines = aiLines;
                }
            }

            _mainWindowViewModel?.HideLoading();

            if (lines.Count == 0)
            {
                _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportFailed, $"bank-matching:no-rows:{ext.TrimStart('.')}");
                await ShowInfoDialogAsync("Info".Translate(),
                    "No transactions were found. Make sure the file has Date, Description and Amount (or Debit/Credit) columns.".Translate());
                return;
            }

            // Snapshot before mutation so the import can be undone in one step.
            var snapshot = CreateCompanyDataSnapshot(companyData);

            var session = new Core.Models.BankMatching.BankImportSession
            {
                Id = Guid.NewGuid().ToString("N"),
                ImportedAt = DateTime.UtcNow,
                SourceFileName = Path.GetFileName(filePath),
                Lines = lines
            };
            companyData.BankImportSessions.Add(session);
            companyData.MarkAsModified();

            var importedSnapshot = CreateCompanyDataSnapshot(companyData);
            UndoRedoManager.RecordAction(new DelegateAction(
                "Import bank statement".Translate(),
                () => { RestoreCompanyDataFromSnapshot(companyData, snapshot); CompanyManager.MarkAsChanged(); _bankMatchingPageViewModel?.Reload(); },
                () => { RestoreCompanyDataFromSnapshot(companyData, importedSnapshot); CompanyManager.MarkAsChanged(); _bankMatchingPageViewModel?.Reload(); }
            ));

            CompanyManager.MarkAsChanged();

            _bankMatchingPageViewModel?.Reload();
            NavigationService?.NavigateTo(PageNames.BankMatching);

            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.DataImported, $"bank-matching:{lines.Count}");
            TutorialService.Instance.CompleteChecklistItem(TutorialService.ChecklistItems.ImportData);

            await ShowInfoDialogAsync(
                "Bank Matching".Translate(),
                "Imported {0} transactions from {1}.".TranslateFormat(lines.Count, Path.GetFileName(filePath)));
        }
        catch (OperationCanceledException)
        {
            _mainWindowViewModel?.HideLoading();
            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportAbandoned, "bank-matching:cancelled");
        }
        catch (UnreadableStatementFileException)
        {
            _mainWindowViewModel?.HideLoading();
            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportFailed, $"bank-matching:unreadable:{ext.TrimStart('.')}");
            await ShowInfoDialogAsync("Import Bank Statement".Translate(), ImportRescueMessages.UnreadableFile);
        }
        catch (Exception ex)
        {
            _mainWindowViewModel?.HideLoading();
            _ = TelemetryManager?.TrackFeatureAsync(FeatureName.ImportFailed, "bank-matching:exception");
            ErrorLogger?.LogError(ex, ErrorCategory.Import, "Bank statement import failed");
            await ShowErrorDialogAsync("Import Failed".Translate(), "Failed to import bank statement:\n\n{0}".TranslateFormat(ex.Message));
        }
    }

    /// <summary>
    /// Backup parser: uses the smart importer's AI column mapping when local header detection
    /// couldn't recognize the statement's columns. Consumes one bank AI import on success. Returns
    /// null when the user is out of bank imports (the limit prompt has been shown), or an empty list
    /// if AI isn't available or finds nothing.
    /// </summary>
    internal static async Task<List<Core.Models.BankMatching.BankStatementLine>?> TryAiParseBankStatementAsync(
        string filePath, bool isCsv, BankStatementImportService parser)
    {
        var gemini = new GeminiService(ErrorLogger, TelemetryManager);
        if (!gemini.IsConfigured) return [];

        // The gate may show the limit prompt, which the loading overlay would cover.
        _mainWindowViewModel?.HideLoading();
        using var usage = await TryBeginBankPdfImportAsync();
        if (usage == null) return null;
        _mainWindowViewModel?.ShowLoading("Scanning bank statement...".Translate());

        var analysisService = new SpreadsheetAnalysisService(gemini, ErrorLogger, CompanyManager?.CurrentCompanySettings?.Company.Country);
        var analysis = isCsv
            ? await analysisService.AnalyzeCsvAsync(filePath)
            : await analysisService.AnalyzeAsync(filePath);

        var sheet = analysis?.Sheets.FirstOrDefault();
        if (sheet == null) return [];

        var lines = isCsv
            ? await parser.ParseCsvWithAnalysisAsync(filePath, sheet)
            : await parser.ParseExcelWithAnalysisAsync(filePath, sheet);

        if (lines.Count > 0)
            await usage.IncrementUsageAsync();

        return lines;
    }

    /// <summary>
    /// Shared usage gate for a bank statement AI import (one "bank" import): a PDF statement, or the AI
    /// column fallback for a CSV/Excel statement whose columns weren't recognized. Available on the
    /// free tier within the monthly limit, like the other AI imports. Shows the usage-limit prompt and
    /// returns null when the user is out of imports. On success returns a live bank-import usage
    /// service the CALLER owns: dispose it (with <c>using</c>) and call IncrementUsageAsync once
    /// extraction succeeds. Shared by the Bank Matching page import and the review-modal import so the
    /// gate lives in one place.
    /// </summary>
    internal static async Task<UsageLimitService?> TryBeginBankPdfImportAsync()
    {
        var usage = new UsageLimitService(UsageLimit.AiImports("bank"), LicenseService, ErrorLogger);
        var check = await usage.CheckUsageAsync();
        if (check.Allowed) return usage;

        usage.Dispose();
        if (check.ErrorMessage != null)
            await UpgradePromptHelper.ShowUsageCheckFailedAsync(check.ErrorMessage);
        else
            await UpgradePromptHelper.ShowAiImportLimitPromptAsync(check.Used, check.MonthlyLimit, check.ResetsAt);
        return null;
    }

    /// <summary>How a screen shows a PDF statement being read. See <see cref="ReadBankPdfStatementAsync"/>.</summary>
    /// <param name="Begin">Reading starts; given the file's size, for a progress estimate.</param>
    /// <param name="End">Reading is over; true when rows came back.</param>
    /// <param name="StillWanted">False once the user has closed the screen, so no message is shown.</param>
    internal sealed record BankPdfReadProgress(Action<long> Begin, Action<bool> End, Func<bool> StillWanted);

    /// <summary>
    /// The one way a PDF bank statement is read, for the Bank Matching page and the import modal:
    /// checks the bank-import limit, sends the PDF to the AI extractor, and charges the single
    /// bank-import credit once rows come back. Returns the rows, or an empty list when the user is
    /// out of imports, the server is busy, or nothing could be read (each told to the user).
    /// </summary>
    internal static async Task<List<Core.Models.BankMatching.BankStatementLine>> ReadBankPdfStatementAsync(
        string filePath, BankPdfReadProgress progress)
    {
        using var usage = await TryBeginBankPdfImportAsync();
        if (usage == null || PdfStatementExtractor == null) return [];

        progress.Begin(new FileInfo(filePath).Length);
        List<Core.Models.BankMatching.BankStatementLine> extracted;
        try
        {
            var bytes = await SharedFileReader.ReadAllBytesAsync(filePath);
            extracted = await PdfStatementExtractor.ExtractAsync(bytes, Path.GetFileName(filePath));
        }
        catch (ServerRateLimitedException ex)
        {
            // Nothing was read, so nothing is charged; the file itself may be fine.
            if (EndRead(progress, false))
                await ShowInfoDialogAsync("Import Bank Statement".Translate(), ex.Message);
            return [];
        }
        catch
        {
            progress.End(false);
            throw;
        }

        var stillWanted = EndRead(progress, extracted.Count > 0);
        if (extracted.Count == 0)
        {
            // Don't fail silently: the extractor returns nothing both when the PDF has no
            // recognizable transactions and when the server couldn't process it. Nothing is charged.
            if (stillWanted)
                await ShowInfoDialogAsync(
                    "Import Bank Statement".Translate(),
                    "We couldn't read any transactions from that PDF. It may not be a recognizable bank statement, or the server couldn't process it. Try again, or import a CSV or Excel export instead.".Translate());
            return [];
        }

        // Charged even if the user has since closed the screen, or a start-and-cancel loop could
        // read PDFs without ever using up the limit.
        await usage.IncrementUsageAsync();
        return extracted;
    }

    /// <summary>Ends the read and says whether the screen was still open beforehand.</summary>
    /// <remarks>Asked before End, because ending a failed read closes the import screen.</remarks>
    internal static bool EndRead(BankPdfReadProgress progress, bool succeeded)
    {
        var stillWanted = progress.StillWanted();
        progress.End(succeeded);
        return stillWanted;
    }

    /// <summary>
    /// PDF bank statement import for the Bank Matching page, behind the loading overlay. The rows go
    /// straight to the page with no follow-up categorization, so the read is the single charge.
    /// </summary>
    private static Task<List<Core.Models.BankMatching.BankStatementLine>> ImportPdfStatementAsync(string filePath) =>
        ReadBankPdfStatementAsync(filePath, new BankPdfReadProgress(
            _ => ShowBusyOverlay("Reading PDF statement...".Translate()),
            _ => HideBusyOverlay(),
            () => true));

    internal static string CreateCompanyDataSnapshot(CompanyData data)
    {
        var snapshot = new
        {
            data.IdCounters,
            data.Customers,
            data.Products,
            data.Suppliers,
            data.Categories,
            data.Locations,
            data.Revenues,
            data.Expenses,
            data.Invoices,
            data.Quotes,
            data.Payments,
            data.RecurringInvoices,
            data.RecurringTransactions,
            data.Inventory,
            data.StockAdjustments,
            data.StockTransfers,
            data.PurchaseOrders,
            data.RentalInventory,
            data.Rentals,
            data.Returns,
            data.LostDamaged,
            data.Receipts,
            data.EventLog,
            data.BankImportSessions,
            data.Employees,
            data.PendingConversions
        };
        return System.Text.Json.JsonSerializer.Serialize(snapshot);
    }

    /// <summary>
    /// Restores company data collections from a JSON snapshot.
    /// </summary>
    internal static void RestoreCompanyDataFromSnapshot(CompanyData data, string snapshotJson)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        using var doc = System.Text.Json.JsonDocument.Parse(snapshotJson);
        var root = doc.RootElement;
        var queuedBefore = data.PendingConversions.Select(p => p.Key).ToList();

        List<T> Read<T>(string propertyName) =>
            root.TryGetProperty(propertyName, out var prop)
                ? System.Text.Json.JsonSerializer.Deserialize<List<T>>(prop.GetRawText(), options) ?? []
                : [];

        // Undo steps hold records and change them later, so each record stays the object it was.
        void RestoreList<T>(List<T> list, string propertyName) where T : class, Core.Models.Common.IRecord =>
            list.RestoreInPlace(Read<T>(propertyName));

        if (root.TryGetProperty("IdCounters", out var counters)
            && System.Text.Json.JsonSerializer.Deserialize<IdCounters>(counters.GetRawText(), options) is { } restoredCounters)
            data.IdCounters.CopyFrom(restoredCounters);

        // Restore all collections
        RestoreList(data.Customers, "Customers");
        RestoreList(data.Products, "Products");
        RestoreList(data.Suppliers, "Suppliers");
        RestoreList(data.Categories, "Categories");
        RestoreList(data.Locations, "Locations");
        RestoreList(data.Revenues, "Revenues");
        RestoreList(data.Expenses, "Expenses");
        RestoreList(data.Invoices, "Invoices");
        RestoreList(data.Quotes, "Quotes");
        RestoreList(data.Payments, "Payments");
        RestoreList(data.RecurringInvoices, "RecurringInvoices");
        RestoreList(data.RecurringTransactions, "RecurringTransactions");
        RestoreList(data.Inventory, "Inventory");
        RestoreList(data.StockAdjustments, "StockAdjustments");
        RestoreList(data.StockTransfers, "StockTransfers");
        RestoreList(data.PurchaseOrders, "PurchaseOrders");
        RestoreList(data.RentalInventory, "RentalInventory");
        RestoreList(data.Rentals, "Rentals");
        RestoreList(data.Returns, "Returns");
        RestoreList(data.LostDamaged, "LostDamaged");
        RestoreList(data.Receipts, "Receipts");
        RestoreList(data.EventLog, "EventLog");
        RestoreList(data.BankImportSessions, "BankImportSessions");
        RestoreList(data.Employees, "Employees");
        // An entry the restore leaves as it was stays the same object too: a stock undo reads the
        // rate the queue converted its entry at off the entry it holds (StockChange.OldPendingCost).
        var liveEntries = data.PendingConversions
            .GroupBy(p => p.Key)
            .ToDictionary(g => g.Key, g => g.First());
        var restoredEntries = Read<Core.Models.Common.PendingConversion>("PendingConversions")
            .Select(e => liveEntries.TryGetValue(e.Key, out var live)
                         && System.Text.Json.JsonSerializer.Serialize(live) == System.Text.Json.JsonSerializer.Serialize(e)
                ? live
                : e)
            .ToList();
        data.PendingConversions.Clear();
        data.PendingConversions.AddRange(restoredEntries);
        data.InvalidateLookupCaches();

        // An undo takes away rows whose conversions were queued, and a redo brings them back pending.
        Core.Services.UsdConversion.Mirror(data, queuedBefore.Concat(data.PendingConversions.Select(p => p.Key)));
    }

    /// <summary>
    /// Opens the file dialog to select a company file.
    /// </summary>
    private static async Task OpenCompanyFileDialogAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var files = await desktop.MainWindow!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Company".Translate(),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Argo Books Files")
                {
                    Patterns = ["*.argo"]
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = ["*.*"]
                }
            ]
        });

        if (files.Count > 0)
        {
            var filePath = files[0].Path.LocalPath;
            await OpenCompanyWithRetryAsync(filePath);
        }
    }

    /// <summary>
    /// Handles a "New Company" request from the file menu or company switcher. If a company is
    /// open with unsaved changes, prompts to save (mirroring Close Company) before opening the
    /// create-company wizard, so making a new company can't silently discard pending edits.
    /// The welcome screen's "Create New Company" path skips this (no company is open there).
    /// </summary>
    /// <summary>
    /// Points the sample-company banner at whichever company is now open.
    ///
    /// Every open of the demo starts a fresh visit: the scan count restarts and an earlier
    /// dismissal does not carry over. Dismiss means "not now", not "never again", and
    /// someone opening the demo for a third time is likelier to be ready to make something
    /// real, not less. Keying this on the open rather than on leaving also avoids the trap
    /// the first version fell into, where closing and reopening the demo was not a
    /// transition away from it and so reset nothing.
    /// </summary>
    private static void SyncSampleCompanyState()
    {
        if (_mainWindowViewModel == null || CompanyManager == null)
        {
            return;
        }

        var isSample = CompanyManager.IsSampleCompany;

        if (isSample)
        {
            _mainWindowViewModel.SampleReceiptScans = 0;
            _mainWindowViewModel.SampleBannerDismissed = false;
        }

        _mainWindowViewModel.IsSampleCompany = isSample;

        // The settings modal keeps its own copy, set only on company open and close. Save As
        // fires neither, so without this its sample-company notice and the controls it
        // disables stayed that way over the user's own company.
        if (_appShellViewModel?.SettingsModalViewModel != null)
        {
            _appShellViewModel.SettingsModalViewModel.IsSampleCompany = isSample;
        }

        // These two read the flag live but raise nothing on their own, and a page control is
        // rebuilt only when navigated to. Save As neither navigates nor clears the page cache,
        // so the dashboard's sample warning and the greyed out resend buttons stayed as they
        // were on whichever page was already open.
        _dashboardPageViewModel?.RefreshSampleCompanyState();
        _invoicesPageViewModel?.RefreshSampleCompanyState();
        _quotesPageViewModel?.RefreshSampleCompanyState();
    }

    internal static async Task RequestCreateNewCompanyAsync()
    {
        if (_appShellViewModel == null) return;
        if (!await ConfirmLeavingCompanyAsync()) return;

        _appShellViewModel.CreateCompanyViewModel.OpenCommand.Execute(null);
    }

    /// <summary>
    /// Offers to save unsaved changes before the open company is closed or replaced. Shared by
    /// Close Company, New Company and every way of opening a company: opening one closes the
    /// current company without saving it, so any path that skips this loses the changes.
    /// </summary>
    /// <returns>False when the user chose to stay where they are.</returns>
    private static async Task<bool> ConfirmLeavingCompanyAsync()
    {
        // UndoRedoManager's saved state, which correctly accounts for undoing back to the
        // last-saved point.
        if (CompanyManager?.IsCompanyOpen != true || UndoRedoManager.IsAtSavedState)
            return true;

        switch (await ShowUnsavedChangesDialogAsync())
        {
            case UnsavedChangesResult.Save:
                // Sample company cannot be saved directly - redirect to Save As.
                if (CompanyManager.IsSampleCompany)
                {
                    return Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                           && await SaveCompanyAsDialogAsync(desktop);
                }

                try
                {
                    return await SaveCompanyWithSecurityGuidanceAsync();
                }
                catch (Exception ex)
                {
                    // What the security guidance does not cover, such as a drive that has gone.
                    // Staying on the open company keeps the changes; carrying on would discard them.
                    ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Save before leaving the company failed");
                    await ShowWarningDialogAsync(
                        "Could Not Save".Translate(),
                        "Your changes could not be saved, so the company is still open with them. {0}".TranslateFormat(ex.Message));
                    return false;
                }
            case UnsavedChangesResult.DontSave:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Opens a company file with password retry support.
    /// Shows password modal on encrypted files and retries on wrong password.
    /// </summary>
    /// <param name="filePath">The company file to open.</param>
    /// <param name="leavingConfirmed">True when the caller has already asked about unsaved changes.</param>
    private static async Task OpenCompanyWithRetryAsync(string filePath, bool leavingConfirmed = false)
    {
        if (CompanyManager == null || _mainWindowViewModel == null || _appShellViewModel == null) return;
        if (!leavingConfirmed && !await ConfirmLeavingCompanyAsync()) return;

        var passwordModal = _appShellViewModel.PasswordPromptModalViewModel;

        _isOpeningCompany = true;
        _mainWindowViewModel.ShowLoading("Opening company...".Translate());

        try
        {
            var success = await CompanyManager.OpenCompanyAsync(filePath);
            _isOpeningCompany = false;
            if (success)
            {
                // Close the password modal if it was open
                passwordModal.Close();

                // Time-shift sample company data if needed
                if (CompanyManager.IsSampleCompany && CompanyManager.CompanyData != null)
                {
                    if (SampleCompanyService.TimeShiftSampleData(CompanyManager.CompanyData))
                    {
                        CompanyManager.NotifyDataChanged();
                        _suppressSavedFeedback = true;
                        await CompanyManager.SaveCompanyAsync();
                    }
                    CompanyManager.CompanyData.MarkAsSaved();
                    _mainWindowViewModel.HasUnsavedChanges = false;
                    _appShellViewModel.HeaderViewModel.HasUnsavedChanges = false;
                    SyncSampleCompanyState();
                    ChartSettingsService.Instance.SelectedDateRange = "Last 365 Days";
                    await CaptureSampleInsightsAsync();
                }

                await LoadRecentCompaniesAsync();
            }
            else
            {
                // User cancelled password prompt
                _isOpeningCompany = false;
                _mainWindowViewModel.HideLoading();
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Wrong password - show error and retry
            _isOpeningCompany = false;
            _mainWindowViewModel.HideLoading();

            passwordModal.ShowError("Invalid password. Please try again.".Translate());

            // Wait for the user to retry
            var newPassword = await passwordModal.WaitForPasswordAsync();

            if (string.IsNullOrEmpty(newPassword))
            {
                // User cancelled
                passwordModal.Close();
                return;
            }

            // Close the modal and retry with the new password
            passwordModal.Close();
            await OpenCompanyWithPasswordRetryAsync(filePath, newPassword);
        }
        catch (FileNotFoundException)
        {
            _isOpeningCompany = false;
            _mainWindowViewModel.HideLoading();
            passwordModal.Close();
            await ShowWarningDialogAsync(
                "Company File Not Found".Translate(),
                "The company file no longer exists.".Translate());
            SettingsService?.RemoveRecentCompany(filePath);
            await LoadRecentCompaniesAsync();
        }
        catch (CompanyFileTooNewException ex)
        {
            // File was saved by a newer Argo Books build. Use ConfirmationDialog (same path as
            // the FileNotFoundException case) rather than the message-box service, because the
            // latter races with the loading overlay and the dialog ends up queued behind the
            // next user action.
            _isOpeningCompany = false;
            _mainWindowViewModel.HideLoading();
            passwordModal.Close();
            // Not logged as an error: this is an expected, handled condition (the file was saved by a
            // newer build) that already shows the user the "Update Argo Books" dialog below.
            if (ConfirmationDialog != null)
            {
                var result = await ConfirmationDialog.ShowAsync(new ConfirmationDialogOptions
                {
                    Title = "Update Argo Books".Translate(),
                    Message = "This company file was created by Argo Books {0}. You are running Argo Books {1}. Please update to Argo Books {0} or later to open it.".TranslateFormat(ex.FileVersion, ex.AppVersion),
                    PrimaryButtonText = "Update Now".Translate(),
                    SecondaryButtonText = null,
                    CancelButtonText = "Not Now".Translate()
                });

                if (result == ConfirmationResult.Primary && _appShellViewModel != null)
                {
                    // Reopened by TryOpenStartupCompanyAsync once the updated app restarts.
                    _fileToOpenAfterUpdate = filePath;
                    _appShellViewModel.CheckForUpdateModalViewModel.OpenAndUpdateCommand.Execute(null);
                }
            }
        }
        catch (CompanyAlreadyOpenException)
        {
            // Expected, recoverable: the company is open in another running instance. Show a
            // friendly notice (neutral dialog, not the red error box) and leave it in recents.
            _isOpeningCompany = false;
            _mainWindowViewModel.HideLoading();
            passwordModal.Close();
            await ShowCompanyAlreadyOpenAsync();
        }
        catch (Exception ex)
        {
            _isOpeningCompany = false;
            _mainWindowViewModel.HideLoading();
            passwordModal.Close();
            ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to open company file");
            await ShowErrorDialogAsync("Error".Translate(), "Failed to open file: {0}".TranslateFormat(ex.Message));
        }
    }

    /// <summary>
    /// Shows the friendly "this company is already open in another window" notice, as information
    /// rather than an error since it's an expected, recoverable situation.
    /// </summary>
    private static Task ShowCompanyAlreadyOpenAsync() =>
        ShowInfoDialogAsync(
            "Already Open".Translate(),
            "This company is already open in another window. Close it there first, then try again.".Translate());

    /// <summary>
    /// Retries opening a company file with a specific password.
    /// </summary>
    private static async Task<bool> OpenCompanyWithPasswordRetryAsync(string filePath, string password)
    {
        if (CompanyManager == null || _mainWindowViewModel == null || _appShellViewModel == null)
            return false;

        var passwordModal = _appShellViewModel.PasswordPromptModalViewModel;

        _mainWindowViewModel.ShowLoading("Opening company...".Translate());

        try
        {
            var success = await CompanyManager.OpenCompanyAsync(filePath, password);
            if (success)
            {
                passwordModal.Close();
                await LoadRecentCompaniesAsync();
                return true;
            }
            else
            {
                _mainWindowViewModel.HideLoading();
                return false;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Wrong password again - show error and retry
            _mainWindowViewModel.HideLoading();

            passwordModal.ShowError("Invalid password. Please try again.".Translate());

            // Wait for the user to retry
            var newPassword = await passwordModal.WaitForPasswordAsync();

            if (string.IsNullOrEmpty(newPassword))
            {
                // User cancelled
                passwordModal.Close();
                return false;
            }

            // Close the modal and retry recursively
            passwordModal.Close();
            return await OpenCompanyWithPasswordRetryAsync(filePath, newPassword);
        }
        catch (CompanyAlreadyOpenException)
        {
            _mainWindowViewModel.HideLoading();
            passwordModal.Close();
            await ShowCompanyAlreadyOpenAsync();
            return false;
        }
        catch (Exception ex)
        {
            _mainWindowViewModel.HideLoading();
            passwordModal.Close();
            ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to open company file with password");
            await ShowErrorDialogAsync("Error".Translate(), "Failed to open file: {0}".TranslateFormat(ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Opens the save dialog for Save As.
    /// </summary>
    /// <returns>True if the file was saved successfully, false if cancelled or failed.</returns>
    private static async Task<bool> SaveCompanyAsDialogAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var suggestedName = CompanyManager?.CurrentCompanyName ?? "Company";

        // For sample company, suggest a copy name but don't show warning
        // (user already chose Save As, so they intend to create a copy)
        if (CompanyManager?.IsSampleCompany == true)
        {
            suggestedName += " (copy)";
        }

        var file = await ShowSaveFileDialogAsync(desktop, suggestedName);
        if (file == null) return false;

        var filePath = file.Path.LocalPath;

        while (true)
        {
            try
            {
                // Suppress the default CompanySaved feedback, we show our own with forceSaved
                _suppressSavedFeedback = true;
                await CompanyManager!.SaveCompanyAsAsync(filePath);

                // Refresh UI with the (possibly updated) company name
                var newName = CompanyManager.CurrentCompanyName ?? "Company";
                RefreshCompanyUi(newName);

                // Not in RefreshCompanyUi: its other callers are renames, where this would
                // bring a dismissed banner back.
                SyncSampleCompanyState();

                _appShellViewModel!.HeaderViewModel.ShowSavedFeedback(forceSaved: true);

                await LoadRecentCompaniesAsync();
                return true;
            }
            catch (Exception ex) when (FileAccessHelper.IsLikelySecurityBlock(ex))
            {
                _suppressSavedFeedback = false;
                ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Save As blocked by security software");
                switch (await ShowSaveBlockedDialogAsync(filePath))
                {
                    case SaveBlockedChoice.Retry:
                        continue;
                    case SaveBlockedChoice.SaveElsewhere:
                        var newFile = await ShowSaveFileDialogAsync(desktop, suggestedName);
                        if (newFile == null) return false;
                        filePath = newFile.Path.LocalPath;
                        continue;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                _suppressSavedFeedback = false;
                ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to save company as new file");
                await ShowErrorDialogAsync("Error".Translate(), GetFriendlySaveErrorMessage(ex));
                return false;
            }
        }
    }

    /// <summary>
    /// Turns a save/file exception into a message a non-technical user can act on.
    /// Windows surfaces cryptic text like "A device which does not exist was specified"
    /// when the company file's drive is unplugged (USB/external) or a mapped/network
    /// drive disconnects; this explains what happened and how to recover instead of
    /// showing the raw OS error. Unknown failures fall back to the original message.
    /// </summary>
    private static string GetFriendlySaveErrorMessage(Exception ex)
    {
        // Win32 errors HRESULT-wrapped by .NET file APIs:
        //   ERROR_NOT_READY     (21) -> 0x80070015: drive present but not ready (no media / spun down)
        //   ERROR_DEV_NOT_EXIST (55) -> 0x80070037: the volume is gone (ejected, disconnected, letter changed)
        const int errorNotReady = unchecked((int)0x80070015);
        const int errorDevNotExist = unchecked((int)0x80070037);

        var driveUnavailable =
            ex is DriveNotFoundException ||
            ex is DirectoryNotFoundException ||
            (ex is IOException && (ex.HResult == errorNotReady || ex.HResult == errorDevNotExist));

        if (driveUnavailable)
        {
            return ("The drive where this company file is stored is no longer available. "
                + "This usually happens when a USB or external drive is unplugged, or a network drive disconnects. "
                + "Reconnect the drive and try again, or use \"Save As\" to save the file somewhere else "
                + "(such as your main drive). Your work is still open, so nothing has been lost yet.").Translate();
        }

        if (ex is UnauthorizedAccessException)
        {
            return ("Argo Books does not have permission to save to this location. "
                + "Use \"Save As\" to save the file to a folder you own, such as your Documents folder.").Translate();
        }

        // Unknown failure: keep the raw error text so detail isn't hidden.
        return "Failed to save: {0}".TranslateFormat(ex.Message);
    }

    /// <summary>
    /// Public entry point for SaveAs dialog, callable from MainWindow.
    /// </summary>
    /// <returns>True if the file was saved successfully, false if cancelled or failed.</returns>
    public static async Task<bool> SaveCompanyAsFromWindowAsync()
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        return await SaveCompanyAsDialogAsync(desktop);
    }

    private enum SaveBlockedChoice { Retry, SaveElsewhere, Cancel }

    /// <summary>
    /// Saves the open company, and if the save is blocked by antivirus / Windows
    /// ransomware protection (or a file lock), shows a clear message offering Retry,
    /// Save to a different folder, or Cancel, instead of a raw "access denied".
    /// Non-security failures propagate to the caller's existing handling.
    /// </summary>
    /// <returns>True if the company was saved, false if the user cancelled.</returns>
    public static async Task<bool> SaveCompanyWithSecurityGuidanceAsync()
    {
        if (CompanyManager == null)
            return false;

        while (true)
        {
            try
            {
                await CompanyManager.SaveCompanyAsync();
                return true;
            }
            catch (Exception ex) when (FileAccessHelper.IsLikelySecurityBlock(ex))
            {
                ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Company save blocked by security software");
                switch (await ShowSaveBlockedDialogAsync(CompanyManager.CurrentFilePath))
                {
                    case SaveBlockedChoice.Retry:
                        continue;
                    case SaveBlockedChoice.SaveElsewhere:
                        return await SaveCompanyAsFromWindowAsync();
                    default:
                        return false;
                }
            }
        }
    }

    /// <summary>
    /// Shows the "save blocked by security software" dialog and maps the button choice.
    /// </summary>
    private static async Task<SaveBlockedChoice> ShowSaveBlockedDialogAsync(string? targetPath)
    {
        if (ConfirmationDialog is { } dialog)
        {
            var folder = string.IsNullOrEmpty(targetPath)
                ? "the selected folder".Translate()
                : (Path.GetDirectoryName(targetPath) ?? targetPath);

            var message = ("Your antivirus or Windows ransomware protection may have blocked saving to:\n\n{0}\n\n" +
                           "You can retry, save to a different folder, or allow Argo Books in your security software " +
                           "(for example: Windows Security → Virus & threat protection → Ransomware protection → Allow an app).")
                .TranslateFormat(folder);

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Icon = DialogIcon.Warning,
                Title = "Couldn't save your company file".Translate(),
                Message = message,
                PrimaryButtonText = "Retry".Translate(),
                SecondaryButtonText = "Save to a different folder…".Translate(),
                CancelButtonText = "Cancel".Translate(),
                CloseOnBackdropClick = false
            });

            return result switch
            {
                ConfirmationResult.Primary => SaveBlockedChoice.Retry,
                ConfirmationResult.Secondary => SaveBlockedChoice.SaveElsewhere,
                _ => SaveBlockedChoice.Cancel
            };
        }

        return SaveBlockedChoice.Cancel;
    }

    /// <summary>
    /// Shows a save file dialog.
    /// </summary>
    private static async Task<IStorageFile?> ShowSaveFileDialogAsync(IClassicDesktopStyleApplicationLifetime desktop, string suggestedFileName)
    {
        return await desktop.MainWindow!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Company".Translate(),
            SuggestedFileName = $"{CompanyManager.ToCompanyFileName(suggestedFileName)}.argo",
            DefaultExtension = "argo",
            FileTypeChoices =
            [
                new FilePickerFileType("Argo Books Files")
                {
                    Patterns = ["*.argo"]
                }
            ]
        });
    }

    /// <summary>
    /// Loads recent companies into the UI.
    /// </summary>
    private static async Task LoadRecentCompaniesAsync()
    {
        if (CompanyManager == null || _appShellViewModel == null || SettingsService == null)
            return;

        try
        {
            var recentCompanies = await CompanyManager.GetRecentCompaniesAsync();

            // Update file menu
            _appShellViewModel.FileMenuPanelViewModel.RecentCompanies.Clear();
            foreach (var company in recentCompanies.Take(10))
            {
                _appShellViewModel.FileMenuPanelViewModel.RecentCompanies.Add(new RecentCompanyItem
                {
                    Name = company.CompanyName,
                    FilePath = company.FilePath,
                    LastOpened = company.ModifiedAt,
                    Icon = company.IsEncrypted ? "Lock" : "Building"
                });
            }

            // Update company switcher
            _appShellViewModel.CompanySwitcherPanelViewModel.RecentCompanies.Clear();
            foreach (var company in recentCompanies.Take(5))
            {
                _appShellViewModel.CompanySwitcherPanelViewModel.AddRecentCompany(
                    company.CompanyName,
                    company.FilePath);
            }

            // Update welcome screen
            if (_welcomeScreenViewModel != null)
            {
                _welcomeScreenViewModel.RecentCompanies.Clear();

                var companiesForWelcome = recentCompanies.Take(10).ToList();

                foreach (var company in companiesForWelcome)
                {
                    // Use logo thumbnail from footer (instant, no decompression needed)
                    byte[]? logoBytes = null;
                    if (company.LogoThumbnail != null)
                    {
                        try { logoBytes = Convert.FromBase64String(company.LogoThumbnail); }
                        catch { /* corrupted thumbnail, skip */ }
                    }

                    _welcomeScreenViewModel.RecentCompanies.Add(new RecentCompanyItem
                    {
                        Name = company.CompanyName,
                        FilePath = company.FilePath,
                        LastOpened = company.ModifiedAt,
                        Icon = company.IsEncrypted ? "Lock" : "Building",
                        Logo = LoadBitmapFromBytes(logoBytes)
                    });
                }
                _welcomeScreenViewModel.HasRecentCompanies = _welcomeScreenViewModel.RecentCompanies.Count > 0;
                _welcomeScreenViewModel.IsRecentCompaniesLoaded = true;
            }

            // Set up file watchers to detect when recent company files are deleted
            SetupRecentCompanyFileWatchers(recentCompanies.Select(c => c.FilePath).ToList());
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogWarning($"Failed to load recent companies: {ex.Message}", "RecentCompanies");
            // Mark as loaded even on error to prevent indefinite hidden state
            if (_welcomeScreenViewModel != null)
                _welcomeScreenViewModel.IsRecentCompaniesLoaded = true;
        }
    }

    /// <summary>
    /// Sets up file system watchers to detect when recent company files are deleted.
    /// </summary>
    private static void SetupRecentCompanyFileWatchers(List<string> filePaths)
    {
        // Dispose existing watchers
        foreach (var watcher in RecentCompanyWatchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        RecentCompanyWatchers.Clear();

        // Group files by directory to minimize number of watchers
        var directories = filePaths
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var directory in directories)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                continue;

            try
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    Filter = "*.argo",
                    NotifyFilter = NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };

                watcher.Deleted += OnRecentCompanyFileDeleted;
                watcher.Renamed += OnRecentCompanyFileRenamed;

                RecentCompanyWatchers[directory] = watcher;
            }
            catch (Exception ex)
            {
                ErrorLogger?.LogWarning($"Failed to create file watcher for {directory}: {ex.Message}", "FileWatcher");
            }
        }
    }

    /// <summary>
    /// Handles when a recent company file is deleted from the file system.
    /// </summary>
    private static void OnRecentCompanyFileDeleted(object sender, FileSystemEventArgs e)
    {
        // FileService.SaveCompanyAsync writes to <path>.tmp then File.Move(tmp, path,
        // overwrite: true). On Windows that overwrite-move emits a Deleted event for
        // the destination even though the rename itself is atomic, by the time we
        // observe it, the file is already back. Treating it as a real deletion would
        // strip the entry from settings.json on every save. Skip if the file exists.
        if (File.Exists(e.FullPath))
            return;

        // Run on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Re-check on the UI thread in case the file came back during the post hop.
            if (File.Exists(e.FullPath))
                return;
            RemoveRecentCompanyFromUi(e.FullPath);
        });
    }

    /// <summary>
    /// Handles when a recent company file is renamed (which also removes it from our tracked list).
    /// </summary>
    private static void OnRecentCompanyFileRenamed(object sender, RenamedEventArgs e)
    {
        // Run on UI thread - remove the old path
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RemoveRecentCompanyFromUi(e.OldFullPath);
        });
    }

    /// <summary>
    /// Removes a company from all recent company UI lists and persists the change.
    /// </summary>
    private static void RemoveRecentCompanyFromUi(string filePath)
    {
        if (_appShellViewModel == null || SettingsService == null)
            return;

        // Remove from file menu
        var fileMenuItem = _appShellViewModel.FileMenuPanelViewModel.RecentCompanies
            .FirstOrDefault(c => string.Equals(c.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (fileMenuItem != null)
            _appShellViewModel.FileMenuPanelViewModel.RecentCompanies.Remove(fileMenuItem);

        // Remove from company switcher
        var switcherItem = _appShellViewModel.CompanySwitcherPanelViewModel.RecentCompanies
            .FirstOrDefault(c => string.Equals(c.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (switcherItem != null)
            _appShellViewModel.CompanySwitcherPanelViewModel.RecentCompanies.Remove(switcherItem);

        // Remove from welcome screen
        if (_welcomeScreenViewModel != null)
        {
            var welcomeItem = _welcomeScreenViewModel.RecentCompanies
                .FirstOrDefault(c => string.Equals(c.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (welcomeItem != null)
            {
                _welcomeScreenViewModel.RecentCompanies.Remove(welcomeItem);
                _welcomeScreenViewModel.HasRecentCompanies = _welcomeScreenViewModel.RecentCompanies.Count > 0;
            }
        }

        // Persist the removal to settings
        SettingsService.RemoveRecentCompany(filePath);
        _ = SettingsService.SaveGlobalSettingsAsync();
    }

    /// <summary>
    /// Asks whether to save unsaved changes.
    /// </summary>
    /// <returns>The user's choice.</returns>
    private static Task<UnsavedChangesResult> ShowUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog?.ShowAsync(
            "Unsaved Changes".Translate(),
            "You have unsaved changes. Would you like to save them before closing?".Translate())
        ?? Task.FromResult(UnsavedChangesResult.Cancel);

    /// <summary>
    /// Registers all available pages with the navigation service.
    /// </summary>
    private static void RegisterPages(NavigationService navigationService)
    {
        _welcomeScreenViewModel = new WelcomeScreenViewModel();
        navigationService.RegisterPage("Welcome", _ => new WelcomeScreen { DataContext = _welcomeScreenViewModel });

        // Main Section
        navigationService.RegisterPage("Dashboard", _ =>
        {
            if (_dashboardPageViewModel == null)
            {
                _dashboardPageViewModel = new DashboardPageViewModel();
                _dashboardPageViewModel.GoogleSheetsExportStatusChanged += async (_, args) =>
                {
                    if (args.IsExporting)
                    {
                        _mainWindowViewModel?.ShowLoading(
                            "Exporting to Google Sheets...".Translate(),
                            cts: args.CancellationTokenSource);
                    }
                    else if (args.IsSuccess)
                    {
                        _mainWindowViewModel?.HideLoading();
                        // No notification - the browser opens automatically
                    }
                    else if (!string.IsNullOrEmpty(args.ErrorMessage))
                    {
                        _mainWindowViewModel?.HideLoading();
                        if (ConnectivityMessage.IsConnectivityMessage(args.ErrorMessage))
                            await ShowConnectivityErrorAsync(args.ErrorMessage);
                        else
                            await ShowErrorDialogAsync("Export Failed".Translate(), args.ErrorMessage);
                    }
                    else
                    {
                        // Cancelled or no error message
                        _mainWindowViewModel?.HideLoading();
                    }
                };
            }
            _dashboardPageViewModel.HasPremium = _appShellViewModel?.SidebarViewModel.HasPremium ?? false;
            if (CompanyManager?.IsCompanyOpen == true)
            {
                _dashboardPageViewModel.Initialize(CompanyManager);
            }
            return new DashboardPage { DataContext = _dashboardPageViewModel };
        });
        navigationService.RegisterPage("Analytics", _ =>
        {
            _analyticsPageViewModel ??= new AnalyticsPageViewModel();
            if (CompanyManager?.IsCompanyOpen == true)
            {
                _analyticsPageViewModel.Initialize(CompanyManager);
            }
            return new AnalyticsPage { DataContext = _analyticsPageViewModel };
        });
        navigationService.RegisterPage("Insights", _ =>
        {
            _insightsPageViewModel ??= new InsightsPageViewModel();
            _insightsPageViewModel.HasPremium = _appShellViewModel?.SidebarViewModel.HasPremium ?? false;
            return new InsightsPage { DataContext = _insightsPageViewModel };
        });
        navigationService.RegisterPage("Reports", _ => new ReportsPage { DataContext = _reportsPageViewModel ??= new ReportsPageViewModel() });

        // Transactions Section
        navigationService.RegisterPage("Revenue", param =>
        {
            _revenuePageViewModel ??= new RevenuePageViewModel();
            _revenuePageViewModel.HasPremium = _appShellViewModel?.SidebarViewModel.HasPremium ?? false;
            _revenuePageViewModel.HighlightTransactionId = null;
            if (param is TransactionNavigationParameter navParam)
            {
                _revenuePageViewModel.HighlightTransactionId = navParam.TransactionId;
                _revenuePageViewModel.ApplyHighlight();
            }
            return new RevenuePage { DataContext = _revenuePageViewModel };
        });
        navigationService.RegisterPage("Expenses", param =>
        {
            _expensesPageViewModel ??= new ExpensesPageViewModel();
            _expensesPageViewModel.HasPremium = _appShellViewModel?.SidebarViewModel.HasPremium ?? false;
            _expensesPageViewModel.HighlightTransactionId = null;
            if (param is TransactionNavigationParameter navParam)
            {
                _expensesPageViewModel.HighlightTransactionId = navParam.TransactionId;
                _expensesPageViewModel.ApplyHighlight();
            }
            return new ExpensesPage { DataContext = _expensesPageViewModel };
        });
        navigationService.RegisterPage("Quotes", param =>
        {
            _quotesPageViewModel ??= new QuotesPageViewModel();
            _quotesPageViewModel.HighlightTransactionId = null;
            // Cached across navigations, so re-read CompanyData on arrival.
            _quotesPageViewModel.RefreshQuotesCommand.Execute(null);
            if (param is TransactionNavigationParameter navParam)
            {
                _quotesPageViewModel.HighlightTransactionId = navParam.TransactionId;
                _quotesPageViewModel.ApplyHighlight();
            }

            // Pull the answers customers gave while the app was elsewhere.
            _ = AutoSyncPortalPaymentsAsync();
            return new QuotesPage { DataContext = _quotesPageViewModel };
        });
        navigationService.RegisterPage("Invoices", param =>
        {
            if (_invoicesPageViewModel == null)
            {
                _invoicesPageViewModel = new InvoicesPageViewModel();
                _invoicesPageViewModel.UpgradeRequested += (_, _) => _appShellViewModel!.UpgradeModalViewModel.OpenCommand.Execute(null);
            }
            _invoicesPageViewModel.HasPremium = _appShellViewModel!.SidebarViewModel.HasPremium;
            _invoicesPageViewModel.HighlightTransactionId = null;
            if (param is Dictionary<string, object?> dict
                && dict.TryGetValue("selectedTabIndex", out var tabIndex) && tabIndex is int index)
            {
                _invoicesPageViewModel.SelectedTabIndex = index;
            }

            // Cached across navigations, so re-read CompanyData on arrival.
            _invoicesPageViewModel.RefreshInvoicesCommand.Execute(null);

            // After the refresh: ApplyHighlight clears the id as it re-filters, so refreshing
            // afterwards would drop the highlight.
            if (param is TransactionNavigationParameter navParam)
            {
                _invoicesPageViewModel.HighlightTransactionId = navParam.TransactionId;
                _invoicesPageViewModel.ApplyHighlight();
            }

            _ = AutoSyncPortalPaymentsAsync();
            return new InvoicesPage { DataContext = _invoicesPageViewModel };
        });
        // Inventory Section
        navigationService.RegisterPage("Products", param =>
        {
            if (_productsPageViewModel == null)
            {
                _productsPageViewModel = new ProductsPageViewModel();
            }
            _productsPageViewModel.HighlightTransactionId = null;
            if (param is TransactionNavigationParameter navParam)
            {
                _productsPageViewModel.HighlightTransactionId = navParam.TransactionId;
                _productsPageViewModel.ApplyHighlight();
            }
            else if (param is Dictionary<string, object?> dict)
            {
                if (dict.TryGetValue("selectedTabIndex", out var tabIndex) && tabIndex is int index)
                {
                    _productsPageViewModel.SelectedTabIndex = index;
                }
            }
            return new ProductsPage { DataContext = _productsPageViewModel };
        });
        navigationService.RegisterPage("StockLevels", param =>
        {
            _stockLevelsPageViewModel ??= new StockLevelsPageViewModel();
            _stockLevelsPageViewModel.HighlightTransactionId = null;
            if (param is TransactionNavigationParameter navParam)
            {
                _stockLevelsPageViewModel.HighlightTransactionId = navParam.TransactionId;
                _stockLevelsPageViewModel.ApplyHighlight();
            }
            return new StockLevelsPage { DataContext = _stockLevelsPageViewModel };
        });
        navigationService.RegisterPage("Locations", param =>
        {
            _locationsPageViewModel ??= new LocationsPageViewModel();
            if (param is Dictionary<string, object?> dict && dict.TryGetValue("openAddModal", out var openAdd) && openAdd is true)
            {
                LocationsModalsViewModel?.OpenAddModal();
            }
            return new LocationsPage { DataContext = _locationsPageViewModel };
        });
        navigationService.RegisterPage("StockAdjustments", _ => new StockAdjustmentsPage { DataContext = _stockAdjustmentsPageViewModel ??= new StockAdjustmentsPageViewModel() });
        navigationService.RegisterPage("BankMatching", _ =>
        {
            if (_bankMatchingPageViewModel == null)
            {
                _bankMatchingPageViewModel = new BankMatchingPageViewModel();
                _bankMatchingPageViewModel.ImportRequested += async (_, _) => await PerformBankImportAsync();
            }
            return new BankMatchingPage { DataContext = _bankMatchingPageViewModel };
        });
        navigationService.RegisterPage("PurchaseOrders", param =>
        {
            _purchaseOrdersPageViewModel ??= new PurchaseOrdersPageViewModel();
            _purchaseOrdersPageViewModel.HighlightTransactionId = null;
            if (param is TransactionNavigationParameter navParam)
            {
                _purchaseOrdersPageViewModel.HighlightTransactionId = navParam.TransactionId;
                _purchaseOrdersPageViewModel.ApplyHighlight();
            }
            return new PurchaseOrdersPage { DataContext = _purchaseOrdersPageViewModel };
        });
        navigationService.RegisterPage("Categories", param =>
        {
            _categoriesPageViewModel ??= new CategoriesPageViewModel();
            if (param is Dictionary<string, object?> dict)
            {
                if (dict.TryGetValue("selectedTabIndex", out var tabIndex) && tabIndex is int index)
                {
                    _categoriesPageViewModel.SelectedTabIndex = index;
                }
            }
            return new CategoriesPage { DataContext = _categoriesPageViewModel };
        });

        // The sidebar lists each side separately, but both are one page and one view model,
        // so these registrations only preset the tab.
        navigationService.RegisterPage("ExpenseCategories", _ => CategoriesPageForTab(0));
        navigationService.RegisterPage("RevenueCategories", _ => CategoriesPageForTab(1));
        navigationService.RegisterPage("ExpenseProducts", _ => ProductsPageForTab(0));
        navigationService.RegisterPage("RevenueProducts", _ => ProductsPageForTab(1));

        CategoriesPage CategoriesPageForTab(int tabIndex)
        {
            _categoriesPageViewModel ??= new CategoriesPageViewModel();
            _categoriesPageViewModel.SelectedTabIndex = tabIndex;
            return new CategoriesPage { DataContext = _categoriesPageViewModel };
        }

        ProductsPage ProductsPageForTab(int tabIndex)
        {
            _productsPageViewModel ??= new ProductsPageViewModel();
            _productsPageViewModel.HighlightTransactionId = null;
            _productsPageViewModel.SelectedTabIndex = tabIndex;
            return new ProductsPage { DataContext = _productsPageViewModel };
        }

        // Payroll Section
        navigationService.RegisterPage("Employees", _ =>
        {
            _employeesPageViewModel ??= new EmployeesPageViewModel();
            _employeesPageViewModel.Load();
            return new EmployeesPage { DataContext = _employeesPageViewModel };
        });

        navigationService.RegisterPage("PayRuns", _ =>
        {
            if (_payRunsPageViewModel == null)
            {
                _payRunsPageViewModel = new PayRunsPageViewModel();
                _payRunsPageViewModel.UpgradeRequested += (_, _) => _appShellViewModel!.UpgradeModalViewModel.OpenCommand.Execute(null);
            }
            _payRunsPageViewModel.HasPremium = _appShellViewModel!.SidebarViewModel.HasPremium;
            _payRunsPageViewModel.Load();
            return new PayRunsPage { DataContext = _payRunsPageViewModel };
        });

        // Contacts Section
        navigationService.RegisterPage("Customers", param =>
        {
            _customersPageViewModel ??= new CustomersPageViewModel();
            _customersPageViewModel.HighlightTransactionId = null;
            if (param is TransactionNavigationParameter navParam)
            {
                _customersPageViewModel.HighlightTransactionId = navParam.TransactionId;
                _customersPageViewModel.ApplyHighlight();
            }
            return new CustomersPage { DataContext = _customersPageViewModel };
        });
        navigationService.RegisterPage("Suppliers", param =>
        {
            _suppliersPageViewModel ??= new SuppliersPageViewModel();
            _suppliersPageViewModel.HighlightTransactionId = null;
            if (param is TransactionNavigationParameter navParam)
            {
                _suppliersPageViewModel.HighlightTransactionId = navParam.TransactionId;
                _suppliersPageViewModel.ApplyHighlight();
            }
            return new SuppliersPage { DataContext = _suppliersPageViewModel };
        });

        // Rentals Section
        navigationService.RegisterPage("RentalInventory", _ => new RentalInventoryPage { DataContext = _rentalInventoryPageViewModel ??= new RentalInventoryPageViewModel() });
        navigationService.RegisterPage("RentalRecords", param =>
        {
            _rentalRecordsPageViewModel ??= new RentalRecordsPageViewModel();

            // An invoice paid elsewhere raises no rental-side event, so the Paid column
            // would otherwise keep the answer it had when the page was built.
            _rentalRecordsPageViewModel.RefreshRecordsCommand.Execute(null);

            _rentalRecordsPageViewModel.HighlightTransactionId = null;
            if (param is TransactionNavigationParameter navParam)
            {
                _rentalRecordsPageViewModel.HighlightTransactionId = navParam.TransactionId;
                _rentalRecordsPageViewModel.ApplyHighlight();
            }
            return new RentalRecordsPage { DataContext = _rentalRecordsPageViewModel };
        });

        // Tracking Section
        navigationService.RegisterPage("Returns", _ => new ReturnsPage { DataContext = _returnsPageViewModel ??= new ReturnsPageViewModel() });
        navigationService.RegisterPage("LostDamaged", _ => new LostDamagedPage { DataContext = _lostDamagedPageViewModel ??= new LostDamagedPageViewModel() });
        navigationService.RegisterPage("Receipts", param =>
        {
            _receiptsPageViewModel ??= new ReceiptsPageViewModel();
            _receiptsPageViewModel.HasPremium = _appShellViewModel?.SidebarViewModel.HasPremium ?? false;
            _ = AutoMobileSyncAsync();
            return new ReceiptsPage { DataContext = _receiptsPageViewModel };
        });

    }

    /// <summary>
    /// Loads a Bitmap from a file path.
    /// </summary>
    private static Bitmap? LoadBitmapFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogWarning($"Failed to load bitmap from path: {ex.Message}", "BitmapLoader");
            return null;
        }
    }

    private static Bitmap? LoadBitmapFromBytes(byte[]? data)
    {
        if (data == null || data.Length == 0)
            return null;

        try
        {
            using var ms = new MemoryStream(data);
            return new Bitmap(ms);
        }
        catch (Exception ex)
        {
            ErrorLogger?.LogWarning($"Failed to load bitmap from bytes: {ex.Message}", "BitmapLoader");
            return null;
        }
    }

}

/// <summary>
/// Event arguments for plan status changes.
/// </summary>
public class PlanStatusChangedEventArgs(bool hasPremium) : EventArgs
{
    public bool HasPremium { get; } = hasPremium;
}
