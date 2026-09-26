using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>One customer's collected sales and refunds in a date range.</summary>
public sealed record CustomerRevenue(string CustomerId, IReadOnlyList<Revenue> Sales, IReadOnlyList<Payment> Refunds)
{
    /// <summary>Collected revenue less refunds, each converted at its own date (Rule 3a).</summary>
    public decimal Total(Func<decimal, DateTime, decimal> convert) =>
        Sales.Sum(r => convert(r.EffectiveTotalUSD, r.Date))
        - Refunds.Sum(p => convert(RefundAggregator.RevenuePortionUSD(p), p.Date));
}

/// <summary>
/// Top customers by revenue, the one computation behind the Top Customers chart and the dashboard
/// widget (docs/Calculations.md §11): collected revenue in the range, tax included, less the
/// refunds issued in the range.
/// </summary>
public static class TopCustomers
{
    /// <summary>
    /// Customers ranked by <see cref="CustomerRevenue.Total"/>, largest first. A customer refunded as
    /// much as or more than they paid in the range isn't a top customer and is left out.
    /// </summary>
    public static List<(CustomerRevenue Customer, decimal Amount)> Rank(
        IEnumerable<Revenue> revenues, IEnumerable<Payment> payments, DateTime start, DateTime end,
        Func<decimal, DateTime, decimal> convert)
    {
        var refundsByCustomer = payments
            .Where(p => p.IsRefund && p.Date >= start && p.Date <= end && !string.IsNullOrEmpty(p.CustomerId))
            .GroupBy(p => p.CustomerId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Payment>)g.ToList());

        return revenues
            .Where(r => r.Date >= start && r.Date <= end && !string.IsNullOrEmpty(r.CustomerId))
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(r => r.CustomerId!)
            .Select(g => new CustomerRevenue(g.Key, g.ToList(), refundsByCustomer.GetValueOrDefault(g.Key, [])))
            .Select(c => (Customer: c, Amount: c.Total(convert)))
            .Where(c => c.Amount > 0)
            .OrderByDescending(c => c.Amount)
            .ToList();
    }
}
