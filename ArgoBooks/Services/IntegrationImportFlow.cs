using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Integrations;
using ArgoBooks.Localization;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Services;

/// <summary>What happened to a Stripe or Argo Books API sync.</summary>
public enum IntegrationImportOutcome
{
    NothingWaiting,
    Cancelled,
    Discarded,
    Imported,
    Failed
}

/// <summary>The outcome, how many items the preview held, and the failure's message if it failed.</summary>
public sealed record IntegrationImportResult(IntegrationImportOutcome Outcome, int Waiting = 0, string? Error = null);

/// <summary>
/// The one preview-and-confirm flow for the Stripe and Argo Books API imports, run from the Revenue
/// page's banners and from Settings. Only how each entry point shows progress and messages, and
/// what it refreshes afterwards, differs (<see cref="Host"/>).
/// </summary>
public static class IntegrationImportFlow
{
    public sealed class Host
    {
        /// <summary>The status line beside the button or banner.</summary>
        public required Action<string> SetStatus { get; init; }

        /// <summary>Tells the user something (title, message): a notification, or a message box in a modal.</summary>
        public required Func<string, string, Task> Inform { get; init; }

        /// <summary>After the books change, including an undo or redo of the import.</summary>
        public Action? AfterChange { get; init; }

        /// <summary>After what is waiting to be imported may have changed.</summary>
        public Func<Task>? AfterQueueChange { get; init; }
    }

    public static async Task<IntegrationImportResult> RunStripeAsync(CompanyData data, HttpClient http, Host host)
    {
        const string title = "Stripe";
        host.SetStatus("Checking Stripe for new activity...".Translate());
        try
        {
            var svc = new StripeSyncService(new StripeApiClient(http));
            var preview = await svc.PreviewAsync(data);
            if (!preview.HasActivity)
                return await NothingWaitingAsync(title, host);

            await WarmRatesAsync(data, preview.Sales.Concat(preview.Fees), host);
            var confirmed = await ConfirmAsync(new ConfirmationDialogOptions
            {
                Title = "Import from Stripe".Translate(),
                Message = "Import your Stripe activity: {0} in sales and {1} in fees?"
                    .TranslateFormat(Total(preview.Sales), Total(preview.Fees)),
                PrimaryButtonText = "Import".Translate(),
                CancelButtonText = "Cancel".Translate()
            });
            if (confirmed != ConfirmationResult.Primary)
                return new IntegrationImportResult(IntegrationImportOutcome.Cancelled, preview.Charges.Count);

            host.SetStatus("Importing...".Translate());
            var creation = await svc.ImportPreviewAsync(data, preview, RateProgress(host));
            if (creation.AnyCreated)
                App.UndoRedoManager.RecordAction(new DelegateAction(
                    "Import from Stripe".Translate(),
                    () => { creation.Undo(data); Changed(host); },
                    () => { creation.Redo(data); Changed(host); }));
            Changed(host);

            await host.Inform(title.Translate(),
                "Imported {0} sales and {1} expense entries from Stripe.".TranslateFormat(creation.RevenuesCreated, creation.ExpensesCreated));
            if (host.AfterQueueChange != null) await host.AfterQueueChange();
            return new IntegrationImportResult(IntegrationImportOutcome.Imported);
        }
        catch (Exception ex)
        {
            return await FailedAsync(title, "Sync failed: {0}", ex);
        }
    }

    public static async Task<IntegrationImportResult> RunArgoApiAsync(CompanyData data, HttpClient http, Host host)
    {
        const string title = "Argo Books API";
        host.SetStatus("Checking for new data...".Translate());
        try
        {
            var svc = new ArgoApiSyncService(new ArgoApiClient(http));
            var preview = await svc.PreviewAsync(data);
            if (!preview.HasActivity)
                return await NothingWaitingAsync(title, host);

            await WarmRatesAsync(data, preview.RateAmounts, host);

            // Three answers, not two. Cancel leaves everything queued for next time, which is the
            // right response to "not now" but a poor one to "never": without Discard an unwanted
            // object is re-offered on every sync forever, and the app that sent it cannot tell
            // refusal from inattention.
            var choice = await ConfirmAsync(new ConfirmationDialogOptions
            {
                Title = "Import from the Argo Books API".Translate(),
                Message = "Import {0} items sent by your connected apps: {1} in revenue and {2} in expenses?\n\nDiscard removes them for good and tells the apps that sent them. Cancel leaves them waiting."
                    .TranslateFormat(preview.TotalObjects, Total(preview.Sales), Total(preview.ExpenseAmounts)),
                PrimaryButtonText = "Import".Translate(),
                SecondaryButtonText = "Discard".Translate(),
                IsSecondaryDestructive = true,
                CancelButtonText = "Cancel".Translate()
            });

            if (choice == ConfirmationResult.Secondary)
            {
                host.SetStatus("Discarding...".Translate());
                var discarded = await svc.RejectPreviewAsync(data, preview);
                await host.Inform(title.Translate(),
                    "Discarded {0} items. They will not be offered again.".TranslateFormat(discarded));
                if (host.AfterQueueChange != null) await host.AfterQueueChange();
                return new IntegrationImportResult(IntegrationImportOutcome.Discarded);
            }

            if (choice != ConfirmationResult.Primary)
                return new IntegrationImportResult(IntegrationImportOutcome.Cancelled, preview.TotalObjects);

            host.SetStatus("Importing...".Translate());
            var creation = await svc.ImportPreviewAsync(data, preview, RateProgress(host));
            if (creation.AnyCreated)
            {
                App.UndoRedoManager.RecordAction(new DelegateAction(
                    "Import from the Argo Books API".Translate(),
                    () =>
                    {
                        creation.Undo(data);
                        Changed(host);
                        // Hand the objects back on the server too, or the queue keeps reporting as
                        // imported what is no longer in the books.
                        _ = ReleaseAsync(svc, data, creation.BatchId, host);
                    },
                    () =>
                    {
                        creation.Redo(data);
                        Changed(host);
                        _ = ReclaimAsync(svc, data, creation, host);
                    }));
            }
            Changed(host);

            await host.Inform(title.Translate(),
                "Imported {0} sales and {1} expense entries.".TranslateFormat(creation.RevenuesCreated, creation.ExpensesCreated));
            if (host.AfterQueueChange != null) await host.AfterQueueChange();
            return new IntegrationImportResult(IntegrationImportOutcome.Imported);
        }
        catch (Exception ex)
        {
            return await FailedAsync(title, "Import failed: {0}", ex);
        }
    }

    /// <summary>
    /// The preview's amounts in the display currency, each converted at its own date (Rule 3a), or
    /// Pending while a rate is missing. Never a sum of raw amounts, which may be in different currencies.
    /// </summary>
    private static string Total(IEnumerable<IncomingAmount> amounts) =>
        DisplayCurrency.TrySumFromNative(amounts, a => a.Amount, a => a.Currency, a => a.Date,
            CurrencyService.GetDisplayAmountFromNative, out var total)
            ? CurrencyService.Format(total)
            : CurrencyService.PendingMarker;

    /// <summary>
    /// Fetches the rates the preview's totals and the import need, before the totals are shown. The
    /// import would fetch the same rates, so this only moves the wait ahead of the question.
    /// </summary>
    private static Task WarmRatesAsync(CompanyData data, IEnumerable<IncomingAmount> amounts, Host host) =>
        IntegrationRates.EnsureAsync(amounts, data.Settings.Localization.Currency, RateProgress(host), App.ErrorLogger);

    private static IProgress<int> RateProgress(Host host) =>
        new Progress<int>(pct => host.SetStatus("Fetching exchange rates... {0}%".TranslateFormat(pct)));

    private static async Task<ConfirmationResult> ConfirmAsync(ConfirmationDialogOptions options) =>
        // Never import without a review step.
        App.ConfirmationDialog == null ? ConfirmationResult.None : await App.ConfirmationDialog.ShowAsync(options);

    private static void Changed(Host host)
    {
        App.CompanyManager?.MarkAsChanged();
        host.AfterChange?.Invoke();
    }

    private static async Task<IntegrationImportResult> NothingWaitingAsync(string title, Host host)
    {
        await host.Inform(title.Translate(), "You're already up to date.".Translate());
        if (host.AfterQueueChange != null) await host.AfterQueueChange();
        return new IntegrationImportResult(IntegrationImportOutcome.NothingWaiting);
    }

    private static async Task<IntegrationImportResult> FailedAsync(string title, string format, Exception ex)
    {
        App.ErrorLogger?.LogError(ex, ErrorCategory.Api, $"{title} sync failed");
        await App.ShowWarningMessageBoxAsync(title.Translate(), format.TranslateFormat(ex.Message));
        return new IntegrationImportResult(IntegrationImportOutcome.Failed, Error: ex.Message);
    }

    private static async Task ReleaseAsync(ArgoApiSyncService svc, CompanyData data, string? batchId, Host host)
    {
        if (batchId != null)
            await svc.TryReleaseBatchAsync(data, batchId);
        if (host.AfterQueueChange != null) await host.AfterQueueChange();
    }

    /// <summary>
    /// Claims a redone import's objects again. Undo handed them back to the queue, so without this
    /// the next sync would import every one of them a second time. A failed claim can't be fixed from
    /// here (usually something else already took them), so the user is told rather than left to find
    /// the duplicates later.
    /// </summary>
    private static async Task ReclaimAsync(ArgoApiSyncService svc, CompanyData data, ArgoApiImportCreation creation, Host host)
    {
        if (!await svc.TryReclaimBatchAsync(data, creation) && creation.BatchId != null)
        {
            // Redo re-recorded the old batch id, which names a batch the server has reverted.
            data.Settings.Integrations.ArgoApi.ImportedBatches.Remove(creation.BatchId);
            creation.BatchId = null;
            App.CompanyManager?.MarkAsChanged();

            await App.ShowWarningMessageBoxAsync(
                "Argo Books API".Translate(),
                ("The restored items are back in your books, but the server could not be told they were taken. " +
                 "They may still show as waiting on your next sync. Importing them again would create duplicates, " +
                 "so check before you do.").Translate());
        }

        if (host.AfterQueueChange != null) await host.AfterQueueChange();
    }
}
