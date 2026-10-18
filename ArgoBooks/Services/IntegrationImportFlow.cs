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

        /// <summary>Shows or hides the busy spinner, which is hidden while the user is being asked rather than waited on.</summary>
        public Action<bool>? SetBusy { get; init; }
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
            var confirmed = await ConfirmAsync(host, new ConfirmationDialogOptions
            {
                Title = "Import from Stripe".Translate(),
                Message = (Amounts(preview.Sales, preview.Fees, costsAreFees: true) is { } amounts
                        ? "Import your Stripe activity: {0}?".TranslateFormat(amounts)
                        : "Import your Stripe activity?".Translate())
                    + (preview.LaterRefunds.Count > 0
                        ? "\n\n" + "Refunds on earlier sales to record: {0}".TranslateFormat(preview.LaterRefunds.Count)
                        : string.Empty),
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

            await host.Inform(title.Translate(), Entries(creation.RevenuesCreated, creation.ExpensesCreated) is { } stripeEntries
                ? "Imported {0} from Stripe.".TranslateFormat(stripeEntries)
                : "Import finished.".Translate());
            if (host.AfterQueueChange != null) await host.AfterQueueChange();
            return new IntegrationImportResult(IntegrationImportOutcome.Imported);
        }
        catch (Exception ex)
        {
            return await FailedAsync(title, "Sync failed: {0}".TranslateFormat(ex.Message), ex);
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

            // Three answers, not two: without Discard an unwanted object is offered again on every sync, where Cancel only means not now.
            var choice = await ConfirmAsync(host, new ConfirmationDialogOptions
            {
                Title = "Import from the Argo Books API".Translate(),
                Message = ArgoApiQuestion(preview.TotalObjects, Amounts(preview.Sales, preview.ExpenseAmounts, costsAreFees: false)),
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
            await SaveToMatchServerAsync();

            await host.Inform(title.Translate(), Entries(creation.RevenuesCreated, creation.ExpensesCreated) is { } apiEntries
                ? "Imported {0}.".TranslateFormat(apiEntries)
                : "Import finished.".Translate());
            if (host.AfterQueueChange != null) await host.AfterQueueChange();
            return new IntegrationImportResult(IntegrationImportOutcome.Imported);
        }
        catch (Exception ex)
        {
            return await FailedAsync(title, "Import failed: {0}".TranslateFormat(ex.Message), ex);
        }
    }

    /// <summary>
    /// "$10.00 in revenue and $4.00 in expenses", leaving out a side with nothing on it, or null when
    /// neither has anything, as when only customers or products were sent.
    /// </summary>
    internal static string? Amounts(IReadOnlyCollection<IncomingAmount> revenue, IReadOnlyCollection<IncomingAmount> costs, bool costsAreFees)
    {
        if (revenue.Count > 0 && costs.Count > 0)
            return (costsAreFees ? "{0} in revenue and {1} in fees" : "{0} in revenue and {1} in expenses")
                .TranslateFormat(Total(revenue), Total(costs));
        if (revenue.Count > 0)
            return "{0} in revenue".TranslateFormat(Total(revenue));
        if (costs.Count > 0)
            return (costsAreFees ? "{0} in fees" : "{0} in expenses").TranslateFormat(Total(costs));
        return null;
    }

    internal static string ArgoApiQuestion(int items, string? amounts)
    {
        var question = (items == 1, amounts) switch
        {
            (true, null) => "Import 1 item sent by your connected apps?".Translate(),
            (true, _) => "Import 1 item sent by your connected apps: {0}?".TranslateFormat(amounts),
            (false, null) => "Import {0} items sent by your connected apps?".TranslateFormat(items),
            (false, _) => "Import {0} items sent by your connected apps: {1}?".TranslateFormat(items, amounts)
        };
        var discard = items == 1
            ? "Discard removes it for good and tells the app that sent it. Cancel leaves it waiting.".Translate()
            : "Discard removes them for good and tells the apps that sent them. Cancel leaves them waiting.".Translate();
        return question + "\n\n" + discard;
    }

    /// <summary>"2 revenue entries and 1 expense entry", leaving out a kind with none, or null when there are neither.</summary>
    internal static string? Entries(int revenues, int expenses)
    {
        var revenuePart = revenues == 1 ? "1 revenue entry".Translate() : "{0} revenue entries".TranslateFormat(revenues);
        var expensePart = expenses == 1 ? "1 expense entry".Translate() : "{0} expense entries".TranslateFormat(expenses);
        return (revenues > 0, expenses > 0) switch
        {
            (true, true) => "{0} and {1}".TranslateFormat(revenuePart, expensePart),
            (true, false) => revenuePart,
            (false, true) => expensePart,
            _ => null
        };
    }

    /// <summary>
    /// The preview's amounts in the display currency, each converted at its own date (Rule 4), or
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

    private static async Task<ConfirmationResult> ConfirmAsync(Host host, ConfirmationDialogOptions options)
    {
        // Never import without a review step.
        if (App.ConfirmationDialog == null)
            return ConfirmationResult.None;

        // Nothing is running while the question is open, so the status and spinner from fetching the rates go.
        host.SetStatus(string.Empty);
        host.SetBusy?.Invoke(false);
        var answer = await App.ConfirmationDialog.ShowAsync(options);
        host.SetBusy?.Invoke(true);
        return answer;
    }

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

    private static async Task<IntegrationImportResult> FailedAsync(string title, string message, Exception ex)
    {
        App.ErrorLogger?.LogError(ex, ErrorCategory.Api, $"{title} sync failed");
        await App.ShowWarningDialogAsync(title.Translate(), message);
        return new IntegrationImportResult(IntegrationImportOutcome.Failed, Error: ex.Message);
    }

    private static async Task ReleaseAsync(ArgoApiSyncService svc, CompanyData data, string? batchId, Host host)
    {
        if (batchId != null && await svc.TryReleaseBatchAsync(data, batchId))
            await SaveToMatchServerAsync();
        if (host.AfterQueueChange != null) await host.AfterQueueChange();
    }

    /// <summary>
    /// Saves the company once the server has been told items were taken, or handed back. The
    /// server's answer is permanent and the books were only changed in memory, so closing without
    /// saving left the two disagreeing: items the server would never offer again that were in no
    /// file, or, after an undo, items in the file that the next sync brought in a second time.
    /// </summary>
    private static async Task SaveToMatchServerAsync()
    {
        if (App.CompanyManager is not { IsCompanyOpen: true } manager) return;

        App.SuppressNextSavedFeedback();
        try
        {
            await manager.SaveCompanyAsync();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Save after an Argo Books API import failed");
            App.AddNotification(
                "Not saved yet".Translate(),
                "The import is in your books but could not be written to the company file. Use Save to try again.".Translate(),
                NotificationType.Warning);
        }
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

            await App.ShowWarningDialogAsync(
                "Argo Books API".Translate(),
                ("The restored items are back in your books, but the server could not be told they were taken. " +
                 "They may still show as waiting on your next sync. Importing them again would create duplicates, " +
                 "so check before you do.").Translate());
        }
        else
        {
            await SaveToMatchServerAsync();
        }

        if (host.AfterQueueChange != null) await host.AfterQueueChange();
    }
}
