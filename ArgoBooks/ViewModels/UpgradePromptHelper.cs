using ArgoBooks.Core.Enums;
using ArgoBooks.Localization;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Helper for showing compelling upgrade prompt dialogs when free-tier limits are reached.
/// Instead of plain error messages, these prompts highlight Premium benefits and offer
/// a direct path to upgrade.
/// </summary>
public static class UpgradePromptHelper
{
    /// <summary>
    /// Shows a compelling upgrade prompt when the invoice send limit is reached.
    /// </summary>
    /// <param name="limit">The monthly invoice limit on the free plan.</param>
    public static async Task ShowInvoiceLimitPromptAsync(int limit)
    {
        var dialog = App.ConfirmationDialog;
        if (dialog == null) return;

        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.UpgradePromptShown, "invoice-limit");

        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "Invoice Limit Reached".Translate(),
            Message = string.Format(
                "You've sent all {0} invoices included in your free plan this month.\n\nUpgrade to Premium for unlimited invoices, online payment collection, and priority support, all for just $10 CAD/month.".Translate(),
                limit),
            PrimaryButtonText = "Upgrade Now".Translate(),
            CancelButtonText = "Maybe Later".Translate(),
            SecondaryButtonText = null
        });

        if (result == ConfirmationResult.Primary)
        {
            App.OpenUpgradeModal("invoice-limit");
        }
    }

    /// <summary>
    /// Shows a plain error dialog when a usage check could not be completed, e.g. the
    /// device is offline or the server was unreachable. This is shown instead of a
    /// limit/upgrade prompt, since the user hasn't actually hit a limit (the count would
    /// otherwise read a misleading "0/0"). The message is produced by the usage service,
    /// which checks connectivity to tailor the wording.
    /// </summary>
    public static Task ShowUsageCheckFailedAsync(string? message)
        => App.ShowConnectivityErrorAsync(message);

    /// <summary>
    /// Shows a compelling upgrade prompt when the AI import limit is reached.
    /// </summary>
    /// <param name="importCount">Number of imports used this month.</param>
    /// <param name="monthlyLimit">The monthly import limit.</param>
    /// <param name="resetsAt">When the limit resets.</param>
    public static async Task ShowAiImportLimitPromptAsync(int importCount, int monthlyLimit, string? resetsAt)
    {
        var dialog = App.ConfirmationDialog;
        if (dialog == null) return;

        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.UpgradePromptShown, "import-limit");

        var resetDate = resetsAt ?? "the 1st of next month".Translate();
        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "AI Import Limit Reached".Translate(),
            Message = string.Format(
                "You've used all {0} of your {1} AI imports this month. Your limit resets on {2}.\n\nUpgrade to Premium for a higher import allowance and unlock AI receipt scanning, predictive analytics, and more.".Translate(),
                importCount, monthlyLimit, resetDate),
            PrimaryButtonText = "Upgrade Now".Translate(),
            CancelButtonText = "Maybe Later".Translate(),
            SecondaryButtonText = null
        });

        if (result == ConfirmationResult.Primary)
        {
            App.OpenUpgradeModal("import-limit");
        }
    }

    /// <summary>
    /// Shows a compelling upgrade prompt when the receipt scan limit is reached.
    /// </summary>
    /// <param name="scanCount">Number of scans used this month.</param>
    /// <param name="monthlyLimit">The monthly scan limit.</param>
    /// <param name="resetsAt">When the limit resets.</param>
    public static async Task ShowReceiptScanLimitPromptAsync(int scanCount, int monthlyLimit, string? resetsAt)
    {
        var dialog = App.ConfirmationDialog;
        if (dialog == null) return;

        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.UpgradePromptShown, "scan-limit");

        var resetDate = resetsAt ?? "the 1st of next month".Translate();
        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "Scan Limit Reached".Translate(),
            Message = string.Format(
                "You've used all {0} of your {1} receipt scans this month. Your limit resets on {2}.\n\nUpgrade to Premium for 500 scans per month, unlimited invoices, predictive analytics, and more.".Translate(),
                scanCount, monthlyLimit, resetDate),
            PrimaryButtonText = "Upgrade Now".Translate(),
            CancelButtonText = "Maybe Later".Translate(),
            SecondaryButtonText = null
        });

        if (result == ConfirmationResult.Primary)
        {
            App.OpenUpgradeModal("scan-limit");
        }
    }

    /// <summary>
    /// Asks what to do when a bulk selection is larger than the scans left this month.
    /// </summary>
    /// <remarks>
    /// This is the not-yet-out-of-scans case, so it is a choice rather than a wall:
    /// ShowReceiptScanLimitPromptAsync covers the user who has none left at all.
    /// </remarks>
    /// <param name="selected">How many receipts the user picked.</param>
    /// <param name="remaining">Scans left this month, always fewer than <paramref name="selected"/>.</param>
    /// <param name="monthlyLimit">The plan's monthly allowance, for context in the message.</param>
    /// <param name="resetsAt">When the allowance resets, if the server told us.</param>
    /// <returns>True to scan the first <paramref name="remaining"/> receipts, false to scan none.</returns>
    public static async Task<bool> ConfirmPartialReceiptScanAsync(int selected, int remaining, int monthlyLimit, string? resetsAt)
    {
        var dialog = App.ConfirmationDialog;
        // No dialog service means no way to ask, but the quota still has to hold. Go ahead
        // with the number the allowance covers rather than silently scanning the whole batch.
        if (dialog == null) return true;

        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.UpgradePromptShown, "partial-scan");

        var resetDate = resetsAt ?? "the 1st of next month".Translate();
        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "More Receipts Than Scans Left".Translate(),
            Message = string.Format(
                "You've selected {0} receipts, but you have {1} of your {2} scans left this month. Your limit resets on {3}.\n\nScan the first {1} now, or upgrade to Premium for 500 scans a month.".Translate(),
                selected, remaining, monthlyLimit, resetDate),
            PrimaryButtonText = string.Format("Scan {0} Now".Translate(), remaining),
            SecondaryButtonText = "Upgrade".Translate(),
            CancelButtonText = "Cancel".Translate()
        });

        if (result == ConfirmationResult.Secondary)
        {
            App.OpenUpgradeModal("partial-scan");
            return false;
        }

        return result == ConfirmationResult.Primary;
    }
}
