using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Inventory;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Reconstructs historical inventory stock levels and values from the
/// StockAdjustment ledger. Quantities are reconstructed exactly as of a
/// given date; value uses each item's current UnitCost (the app keeps no
/// cost history) treated as USD-equivalent. See docs/Calculations.md §10.
/// </summary>
public static class InventoryValuationService
{
    /// <summary>
    /// The signed change in stock this adjustment represents:
    /// Add is positive, Remove is negative, Set is (new - previous).
    /// </summary>
    public static int SignedDelta(StockAdjustment adjustment) => adjustment.AdjustmentType switch
    {
        AdjustmentType.Add => adjustment.Quantity,
        AdjustmentType.Remove => -adjustment.Quantity,
        AdjustmentType.Set => adjustment.NewStock - adjustment.PreviousStock,
        _ => 0
    };

    /// <summary>
    /// Stock on hand for one item as of <paramref name="asOfDate"/>, computed
    /// by rolling back from the item's current InStock every adjustment whose
    /// effective date is on a later DAY than the as-of date. Order-independent, so
    /// back-dated adjustments reconstruct correctly. The comparison is day-granular:
    /// an adjustment stamped with a time-of-day (every manual/rental adjustment uses
    /// DateTime.UtcNow) made on the as-of day itself is INCLUDED, matching the
    /// inclusive end-date semantics of every other report filter. See docs/Calculations.md §10.
    /// </summary>
    public static int StockOnHandAsOf(
        InventoryItem item,
        IEnumerable<StockAdjustment> itemAdjustments,
        Func<StockAdjustment, DateTime> effectiveDate,
        DateTime asOfDate)
    {
        var rollback = itemAdjustments
            .Where(a => effectiveDate(a).Date > asOfDate.Date)
            .Sum(SignedDelta);
        return item.InStock - rollback;
    }

    /// <summary>
    /// Total inventory value as of <paramref name="asOfDate"/>: each item's
    /// reconstructed stock-on-hand times its current UnitCost, summed.
    /// </summary>
    public static decimal TotalValueAsOf(CompanyData data, DateTime asOfDate)
    {
        var transactionDates = BuildTransactionDateLookup(data);

        // Timestamps are stamped in UTC, but the as-of date is a local day.
        DateTime EffectiveDate(StockAdjustment a) =>
            !string.IsNullOrEmpty(a.ReferenceNumber)
            && transactionDates.TryGetValue(a.ReferenceNumber, out var date)
                ? date
                : a.Timestamp.Kind == DateTimeKind.Utc ? a.Timestamp.ToLocalTime() : a.Timestamp;

        var adjustmentsByItem = data.StockAdjustments
            .GroupBy(a => a.InventoryItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StockAdjustment>)g.ToList());

        var total = 0m;
        foreach (var item in data.Inventory)
        {
            var adjustments = adjustmentsByItem.GetValueOrDefault(item.Id) ?? [];
            var quantity = StockOnHandAsOf(item, adjustments, EffectiveDate, asOfDate);
            total += quantity * item.UnitCost;
        }
        return total;
    }

    /// <summary>
    /// Maps Revenue/Expense Ids to their transaction Date so an auto-generated
    /// adjustment's ReferenceNumber can resolve to the real (possibly
    /// back-dated) transaction date rather than its record Timestamp.
    /// </summary>
    private static Dictionary<string, DateTime> BuildTransactionDateLookup(CompanyData data)
    {
        var dict = new Dictionary<string, DateTime>();
        foreach (var revenue in data.Revenues)
            if (!string.IsNullOrEmpty(revenue.Id))
                dict[revenue.Id] = revenue.Date;
        foreach (var expense in data.Expenses)
            if (!string.IsNullOrEmpty(expense.Id))
                dict[expense.Id] = expense.Date;
        return dict;
    }
}
