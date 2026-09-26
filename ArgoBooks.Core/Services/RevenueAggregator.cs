using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Helpers for filtering and aggregating Revenue rows, so dashboard /
/// analytics charts and stat cards only count revenue the company has
/// actually received (cash-basis), not invoices it has merely issued.
///
/// Paid and Complete are the only statuses that mean the customer has
/// settled the full amount; the others are not yet (fully) collected.
/// See docs/Calculations.md §7.
/// </summary>
public static class RevenueAggregator
{
    /// <summary>
    /// True when a Revenue row is considered fully collected. Accepts both
    /// Paid (the modern default) and Complete (legacy alias from older imports).
    /// </summary>
    public static bool IsCollected(Revenue revenue) =>
        revenue.PaymentStatus == RevenuePaymentStatus.Paid ||
        revenue.PaymentStatus == RevenuePaymentStatus.Complete;

    /// <summary>
    /// Filters a Revenue sequence to only fully-collected rows.
    /// </summary>
    public static IEnumerable<Revenue> OnlyCollected(IEnumerable<Revenue> revenues) =>
        revenues.Where(IsCollected);

    /// <summary>
    /// Sum gross-of-tax USD revenue (EffectiveTotalUSD) inside a date range,
    /// counting only collected rows. This is the figure the Revenue stat
    /// card and revenue charts display, what the customer was actually
    /// billed and has actually paid.
    /// </summary>
    public static decimal SumCollectedRevenueUSD(
        IEnumerable<Revenue> revenues, DateTime start, DateTime end)
    {
        return revenues
            .Where(s => s.Date >= start && s.Date <= end)
            .Where(IsCollected)
            .Sum(s => s.EffectiveTotalUSD);
    }

    /// <summary>
    /// Sum pre-tax USD revenue (EffectiveSubtotalUSD) inside a date range,
    /// counting only collected rows. This is the figure profit calculations
    /// use: sales tax collected is owed to the government, not kept.
    /// </summary>
    public static decimal SumCollectedRevenuePreTaxUSD(
        IEnumerable<Revenue> revenues, DateTime start, DateTime end)
    {
        return revenues
            .Where(s => s.Date >= start && s.Date <= end)
            .Where(IsCollected)
            .Sum(s => s.EffectiveSubtotalUSD);
    }

    /// <summary>
    /// Display-currency variant of <see cref="SumCollectedRevenueUSD"/>: converts each row at its
    /// OWN date via <paramref name="toDisplay"/> before summing (docs/Calculations.md Rule 3a).
    /// Pass <c>CurrencyService.GetDisplayAmount</c>. Equals the USD sum for a USD display currency.
    /// </summary>
    public static decimal SumCollectedRevenueDisplay(
        IEnumerable<Revenue> revenues, DateTime start, DateTime end, Func<decimal, DateTime, decimal> toDisplay)
    {
        return revenues
            .Where(s => s.Date >= start && s.Date <= end)
            .Where(IsCollected)
            .Sum(s => toDisplay(s.EffectiveTotalUSD, s.Date));
    }

    /// <summary>
    /// Display-currency variant of <see cref="SumCollectedRevenuePreTaxUSD"/> (per-date conversion).
    /// </summary>
    public static decimal SumCollectedRevenuePreTaxDisplay(
        IEnumerable<Revenue> revenues, DateTime start, DateTime end, Func<decimal, DateTime, decimal> toDisplay)
    {
        return revenues
            .Where(s => s.Date >= start && s.Date <= end)
            .Where(IsCollected)
            .Sum(s => toDisplay(s.EffectiveSubtotalUSD, s.Date));
    }

    /// <summary>Collected revenue, tax included (USD), by day, for time-series charts.</summary>
    public static Dictionary<DateTime, decimal> GroupCollectedRevenueByDayUSD(
        IEnumerable<Revenue> revenues, DateTime start, DateTime end) =>
        GroupCollectedByDay(revenues, start, end, s => s.EffectiveTotalUSD);

    /// <summary>Collected revenue without tax (USD), by day, for per-day profit.</summary>
    public static Dictionary<DateTime, decimal> GroupCollectedRevenuePreTaxByDayUSD(
        IEnumerable<Revenue> revenues, DateTime start, DateTime end) =>
        GroupCollectedByDay(revenues, start, end, s => s.EffectiveSubtotalUSD);

    private static Dictionary<DateTime, decimal> GroupCollectedByDay(
        IEnumerable<Revenue> revenues, DateTime start, DateTime end, Func<Revenue, decimal> amountUSD)
    {
        return revenues
            .Where(s => s.Date >= start && s.Date <= end)
            .Where(IsCollected)
            .GroupBy(s => s.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(amountUSD));
    }
}
