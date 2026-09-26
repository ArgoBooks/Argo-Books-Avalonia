using ArgoBooks.Core.Data;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Aggregations for the Refunds tab on the Analytics page. All metrics are
/// computed locally from the company's Payment list (no server roundtrip).
/// Refund Payments are identified by IsRefund==true and have negative Amount;
/// the absolute value of EffectiveAmountUSD is the refund amount.
///
/// Sums are USD-normalized (Payment.EffectiveAmountUSD) by default so multi-currency
/// portals roll up consistently. Money methods accept an optional
/// <c>Func&lt;decimal,DateTime,decimal&gt; toDisplay</c>; when supplied, each refund is
/// converted to the display currency at its OWN date (Calculations.md Rule 3a) and callers
/// format the result directly with <c>CurrencyService.Format</c> (no second conversion).
/// When omitted the result stays in USD. See docs/Calculations.md §3.
/// </summary>
public static class RefundAnalyticsService
{
    /// <summary>
    /// Identity USD "conversion" used when no per-date display converter is supplied,
    /// so existing USD callers and tests keep their exact behavior.
    /// </summary>
    private static decimal IdentityUSD(decimal usd, DateTime _) => usd;

    /// <summary>Total refunded over a window, in USD.</summary>
    public static decimal TotalRefundedUSD(CompanyData company, DateTime since)
        => company.Payments
            .Where(p => p.IsRefund && p.Date >= since)
            .Sum(p => Math.Abs(p.EffectiveAmountUSD));

    /// <summary>
    /// Total refunded over a window, converted to display currency per
    /// Calculations.md Rule 3a: each refund is converted at its OWN date before summing.
    /// </summary>
    public static decimal TotalRefundedDisplay(CompanyData company, DateTime since, Func<decimal, DateTime, decimal> toDisplay)
        => company.Payments
            .Where(p => p.IsRefund && p.Date >= since)
            .Sum(p => toDisplay(Math.Abs(p.EffectiveAmountUSD), p.Date));

    /// <summary>Refund rate = sum(refunds) / sum(positive payments) over the window. 0 if no payments.</summary>
    public static decimal RefundRate(CompanyData company, DateTime since)
    {
        var refunds = TotalRefundedUSD(company, since);
        var positive = company.Payments
            .Where(p => !p.IsRefund && p.Date >= since)
            .Sum(p => p.EffectiveAmountUSD);
        return positive > 0 ? refunds / positive : 0m;
    }

    /// <summary>
    /// Monthly buckets of refund totals for the last <paramref name="months"/> months. With a
    /// <paramref name="toDisplay"/> converter, each refund is converted to the display currency at
    /// its OWN date before bucketing (Calculations.md Rule 3a); null keeps USD (tests/callers).
    /// </summary>
    public static IReadOnlyList<MonthlyRefundTotal> MonthlyTotals(
        CompanyData company, int months, Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        var firstMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-(months - 1));
        var buckets = new Dictionary<DateTime, decimal>();
        for (int i = 0; i < months; i++)
            buckets[firstMonth.AddMonths(i)] = 0m;
        foreach (var p in company.Payments.Where(p => p.IsRefund && p.Date >= firstMonth))
        {
            var key = new DateTime(p.Date.Year, p.Date.Month, 1);
            if (!buckets.ContainsKey(key)) continue;
            var amount = Math.Abs(p.EffectiveAmountUSD);
            buckets[key] += toDisplay != null ? toDisplay(amount, p.Date) : amount;
        }
        return buckets
            .OrderBy(kv => kv.Key)
            .Select(kv => new MonthlyRefundTotal(kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Top customers by absolute refund total since <paramref name="since"/>. Amounts are
    /// USD by default; pass <paramref name="toDisplay"/> to convert each refund at its OWN
    /// date (Calculations.md Rule 3a) and return display-currency totals.
    /// </summary>
    public static IReadOnlyList<CustomerRefundTotal> TopRefundedCustomers(
        CompanyData company, DateTime since, int top, Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        var convert = toDisplay ?? IdentityUSD;
        var byCustomer = company.Payments
            .Where(p => p.IsRefund && p.Date >= since)
            .GroupBy(p => p.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Total = g.Sum(p => convert(Math.Abs(p.EffectiveAmountUSD), p.Date)),
                Count = g.Count(),
            })
            .OrderByDescending(x => x.Total)
            .Take(top)
            .ToList();

        return byCustomer.Select(x => new CustomerRefundTotal(
            x.CustomerId,
            company.GetCustomer(x.CustomerId)?.Name ?? "Unknown",
            x.Total,
            x.Count)).ToList();
    }

    /// <summary>
    /// Top product/line items by refund total, derived from refunded invoices' line items.
    /// Amounts are USD by default; pass <paramref name="toDisplay"/> to convert each refund
    /// at its OWN date (Calculations.md Rule 3a) and return display-currency totals.
    /// </summary>
    public static IReadOnlyList<ProductRefundTotal> TopRefundedProducts(
        CompanyData company, DateTime since, int top, Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        // Sum refund amounts per invoice, then share each across the invoice's line items the way
        // any amount is (LineAllocation). This is an approximation: the true refunded line items are
        // stored in the server's line_items_json snapshot but not surfaced to the desktop.
        var convert = toDisplay ?? IdentityUSD;
        var byProduct = new Dictionary<string, decimal>();
        var refundsByInvoice = company.Payments
            .Where(p => p.IsRefund && p.Date >= since)
            .GroupBy(p => p.InvoiceId)
            // Convert each refund at its own date before summing per invoice.
            .ToDictionary(g => g.Key, g => g.Sum(p => convert(Math.Abs(p.EffectiveAmountUSD), p.Date)));

        foreach (var (invoiceId, refundAmt) in refundsByInvoice)
        {
            var invoice = company.GetInvoice(invoiceId);
            if (invoice?.LineItems == null || invoice.Total <= 0) continue;
            var allocation = LineAllocation.Allocate(invoice.LineItems, refundAmt);
            if (!allocation.IsSplit) continue;
            // refundAmt is already in the target currency, and the shares stay in it.
            foreach (var (li, share) in allocation.Shares)
            {
                var key = string.IsNullOrEmpty(li.Description) ? "(unnamed)" : li.Description;
                byProduct[key] = byProduct.GetValueOrDefault(key) + share;
            }
        }

        return byProduct
            .OrderByDescending(kv => kv.Value)
            .Take(top)
            .Select(kv => new ProductRefundTotal(kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Top reasons by occurrence count (filtered to non-empty reasons since the window).
    /// Totals are USD by default; pass <paramref name="toDisplay"/> to convert each refund
    /// at its OWN date (Calculations.md Rule 3a) and return display-currency totals.
    /// </summary>
    public static IReadOnlyList<RefundReasonCount> TopReasons(
        CompanyData company, DateTime since, int top, Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        var convert = toDisplay ?? IdentityUSD;
        return company.Payments
            .Where(p => p.IsRefund && p.Date >= since && !string.IsNullOrWhiteSpace(p.RefundReason))
            .GroupBy(p => p.RefundReason!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new RefundReasonCount(g.Key, g.Count(), g.Sum(p => convert(Math.Abs(p.EffectiveAmountUSD), p.Date))))
            .OrderByDescending(r => r.Count)
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// Channel breakdown by total refunded amount. Amounts are USD by default; pass
    /// <paramref name="toDisplay"/> to convert each refund at its OWN date
    /// (Calculations.md Rule 3a) and return display-currency totals.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> ChannelBreakdown(
        CompanyData company, DateTime since, Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        var convert = toDisplay ?? IdentityUSD;
        return company.Payments
            .Where(p => p.IsRefund && p.Date >= since)
            .GroupBy(p => p.PaymentMethod.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(p => convert(Math.Abs(p.EffectiveAmountUSD), p.Date)));
    }

    /// <summary>Average days between original payment and its refund (for refunds that link to a known source).</summary>
    public static double AverageRefundLatencyDays(CompanyData company, DateTime since)
    {
        var pairs = new List<double>();
        foreach (var refund in company.Payments.Where(p => p.IsRefund && p.Date >= since))
        {
            if (string.IsNullOrEmpty(refund.RefundedFromPaymentId)) continue;
            var source = company.Payments.FirstOrDefault(p => p.Id == refund.RefundedFromPaymentId);
            if (source == null) continue;
            var span = (refund.Date - source.Date).TotalDays;
            if (span >= 0) pairs.Add(span);
        }
        return pairs.Count > 0 ? pairs.Average() : 0;
    }
}

public record MonthlyRefundTotal(DateTime Month, decimal AmountUSD);
public record CustomerRefundTotal(string CustomerId, string CustomerName, decimal AmountUSD, int Count);
public record ProductRefundTotal(string ProductLabel, decimal AmountUSD);
public record RefundReasonCount(string Reason, int Count, decimal TotalAmountUSD);
