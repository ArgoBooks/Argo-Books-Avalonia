using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Services;

/// <summary>
/// Drives the confirm-convert-undo flow for switching a receipt between expense and revenue.
/// Shared so the receipts page and the receipt viewer offer identical behaviour.
/// </summary>
public static class ReceiptTypeSwitchService
{
    /// <summary>Runs the switch end to end. Returns true when the books were changed.</summary>
    public static async Task<bool> SwitchAsync(string receiptId)
    {
        try
        {
            var companyData = App.CompanyManager?.CompanyData;
            var dialog = App.ConfirmationDialog;
            if (companyData == null || dialog == null) return false;

            var receipt = companyData.Receipts.FirstOrDefault(r => r.Id == receiptId);
            if (receipt == null) return false;

            var block = ReceiptTypeConverter.GetBlockReason(companyData, receipt);
            if (block != ReceiptSwitchBlock.None)
            {
                await dialog.ShowAsync(new ConfirmationDialogOptions
                {
                    Title = "Cannot Change Type".Translate(),
                    Message = BlockMessage(block, receipt),
                    PrimaryButtonText = "OK".Translate(),
                    SecondaryButtonText = null,
                    CancelButtonText = null
                });
                return false;
            }

            var target = receipt.TransactionType == ReceiptTypeConverter.Revenue
                ? ReceiptTypeConverter.Expense
                : ReceiptTypeConverter.Revenue;

            var message = "This will replace {0} with a new {1} transaction carrying the same amounts. The old transaction number will not be reused."
                .TranslateFormat(receipt.TransactionId, target.ToLowerInvariant());

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Change to {0}".TranslateFormat(target),
                Message = message,
                PrimaryButtonText = "Change".Translate(),
                CancelButtonText = "Cancel".Translate()
            });

            if (result != ConfirmationResult.Primary) return false;

            var oldTransactionId = receipt.TransactionId;
            var switched = ReceiptTypeConverter.Switch(companyData, receipt);
            ResyncPendingQueue(switched.MovedConversion, oldTransactionId);

            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Change receipt {receipt.Id} to {target.ToLowerInvariant()}",
                () =>
                {
                    ReceiptTypeConverter.Revert(companyData, receipt, switched);
                    ResyncPendingQueue(switched.MovedConversion, switched.Created.Id);
                },
                () =>
                {
                    ReceiptTypeConverter.Reapply(companyData, receipt, switched);
                    ResyncPendingQueue(switched.MovedConversion, switched.Removed.Id);
                }));

            App.CompanyManager?.MarkAsChanged();
            return true;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Receipt.SwitchType");
            return false;
        }
    }

    /// <summary>
    /// Moves a queued currency conversion in the self-heal service to match the move the
    /// converter just made in the company file.
    ///
    /// The service works from its own copy of the queue, keyed on transaction id and shared
    /// across companies, so re-pointing the row in CompanyData alone would leave it chasing
    /// the id the switch deleted until the company was next opened.
    /// </summary>
    private static void ResyncPendingQueue(PendingConversion? moved, string staleId)
    {
        if (moved == null) return;

        var service = PendingConversionService.Instance;
        if (service == null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await service.ForgetAsync([staleId]);
                await service.AddPendingConversionAsync(moved);
            }
            catch (Exception ex)
            {
                App.ErrorLogger?.LogWarning(
                    $"Failed to move pending conversion {staleId}: {ex.Message}", "Receipt.SwitchType");
            }
        });
    }

    private static string BlockMessage(ReceiptSwitchBlock block, Receipt receipt) => block switch
    {
        ReceiptSwitchBlock.NoTransaction =>
            "This receipt is not linked to a transaction, so there is nothing to move.".Translate(),
        ReceiptSwitchBlock.HasPayments =>
            "Payments have been recorded against {0}. Remove them first.".TranslateFormat(receipt.TransactionId),
        ReceiptSwitchBlock.FromInvoice =>
            "{0} came from an invoice and has to stay revenue.".TranslateFormat(receipt.TransactionId),
        ReceiptSwitchBlock.HasReturns =>
            "A return refers to {0}. Remove it first.".TranslateFormat(receipt.TransactionId),
        ReceiptSwitchBlock.UsedByPayRun =>
            "{0} is a payroll expense and is referenced by a pay run.".TranslateFormat(receipt.TransactionId),
        _ => string.Empty
    };
}
