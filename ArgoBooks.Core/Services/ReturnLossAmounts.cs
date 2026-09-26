using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Tracking;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Returns and losses store their amount in the currency of the sale or purchase they came from and
/// carry no currency of their own, so every surface that shows those amounts converts each one from
/// that currency at the record's own date. See docs/Calculations.md §10, Returns and Losses.
/// </summary>
public static class ReturnLossAmounts
{
    /// <summary>The currency a return's <see cref="Return.RefundAmount"/> is recorded in.</summary>
    public static string CurrencyOf(CompanyData data, Return returnRecord) =>
        RecordedCurrency(data, returnRecord.OriginalTransactionId);

    /// <summary>The currency a loss's <see cref="LostDamaged.ValueLost"/> is recorded in.</summary>
    public static string CurrencyOf(CompanyData data, LostDamaged loss) =>
        RecordedCurrency(data, loss.InventoryItemId);

    // A record with no sale or purchase behind it (an imported row) is taken to be in the company
    // currency, as the importer takes an amount with no currency column to be.
    private static string RecordedCurrency(CompanyData data, string? transactionId)
    {
        if (!string.IsNullOrEmpty(transactionId))
        {
            var revenue = data.Revenues.FirstOrDefault(r => r.Id == transactionId);
            if (revenue != null)
                return revenue.OriginalCurrency;

            var expense = data.Expenses.FirstOrDefault(e => e.Id == transactionId);
            if (expense != null)
                return expense.OriginalCurrency;
        }

        var companyCurrency = data.Settings.Localization.Currency;
        return string.IsNullOrEmpty(companyCurrency) ? "USD" : companyCurrency;
    }
}
