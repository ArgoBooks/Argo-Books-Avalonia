using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Single source of truth for "how much of X has been refunded" calculations.
/// All UI surfaces (revenue page, payments page, dashboard stat cards, charts)
/// route through here so refund deductions stay consistent across the app.
///
/// Cash-basis dating: a refund reduces revenue on the date the refund was
/// issued, not the date of the original payment. Same-day refund nets to zero
/// on that day; multi-day refund leaves the original day's revenue intact and
/// produces a negative on the refund's day.
///
/// Only the part of a refund that was revenue comes off: a security deposit it
/// gave back was never counted (<see cref="Payment.RevenueShare"/>, §8).
/// </summary>
public static class RefundAggregator
{
    /// <summary>
    /// USD-normalized variant for dashboard aggregations. Uses Payment.EffectiveAmountUSD
    /// so multi-currency portals roll up consistently.
    /// </summary>
    public static decimal GetRefundedInDateRangeUSD(IEnumerable<Payment> allPayments, DateTime start, DateTime end)
    {
        return allPayments
            .Where(p => p.IsRefund && p.Date >= start && p.Date <= end)
            .Sum(p => Math.Abs(p.EffectiveAmountUSD) * p.RevenueShare);
    }

    /// <summary>
    /// Display-currency variant of <see cref="GetRefundedInDateRangeUSD"/>: converts each refund at
    /// its OWN date via <paramref name="toDisplay"/> before summing (docs/Calculations.md §3a Phase 2).
    /// Pass <c>CurrencyService.GetDisplayAmount</c>. Equals the USD sum for a USD display currency.
    /// </summary>
    public static decimal GetRefundedInDateRangeDisplay(
        IEnumerable<Payment> allPayments, DateTime start, DateTime end, Func<decimal, DateTime, decimal> toDisplay)
    {
        return allPayments
            .Where(p => p.IsRefund && p.Date >= start && p.Date <= end)
            .Sum(p => toDisplay(Math.Abs(p.EffectiveAmountUSD) * p.RevenueShare, p.Date));
    }

    /// <summary>
    /// Group refund amounts (absolute USD) by the day the refund was issued.
    /// Used by per-day charts that subtract refunds from revenue/profit so
    /// the deduction lands on the refund's own day, not the original payment's.
    /// </summary>
    public static Dictionary<DateTime, decimal> GroupRefundsByDayUSD(
        IEnumerable<Payment> allPayments, DateTime start, DateTime end)
    {
        return allPayments
            .Where(p => p.IsRefund && p.Date >= start && p.Date <= end)
            .GroupBy(p => p.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(p => Math.Abs(p.EffectiveAmountUSD) * p.RevenueShare));
    }

    /// <summary>
    /// The share of an invoice's revenue that was before tax: total less deposit and tax, over total
    /// less deposit, which is what the invoice's revenue counted. It scales the revenue part of a
    /// refund; the deposit part came off nothing. Not Subtotal/Total: Subtotal is the line sum
    /// before an invoice discount and without shipping or fees, so it took the wrong amount off
    /// profit whenever an invoice had any of those. An invoice that is all deposit has no tax.
    /// </summary>
    public static decimal PreTaxShare(Invoice invoice)
    {
        var revenue = invoice.Total - invoice.SecurityDeposit;
        return revenue > 0 ? (revenue - invoice.TaxAmount) / revenue : 1m;
    }

    /// <summary>
    /// The pre-tax USD part of one refund: its revenue part scaled by its invoice's
    /// <see cref="PreTaxShare"/>, or the whole revenue part when the invoice link is missing.
    /// </summary>
    public static decimal PreTaxPortionUSD(Payment refund, IReadOnlyDictionary<string, Invoice> invoicesById)
    {
        var refundUSD = Math.Abs(refund.EffectiveAmountUSD) * refund.RevenueShare;
        return !string.IsNullOrEmpty(refund.InvoiceId)
               && invoicesById.TryGetValue(refund.InvoiceId, out var invoice)
               && invoice.Total > 0
            ? refundUSD * PreTaxShare(invoice)
            : refundUSD;
    }

    /// <summary>
    /// The sales tax one refund handed back (USD): its revenue part less the pre-tax part. The
    /// deposit part carried no tax.
    /// </summary>
    public static decimal TaxPortionUSD(Payment refund, IReadOnlyDictionary<string, Invoice> invoicesById) =>
        Math.Abs(refund.EffectiveAmountUSD) * refund.RevenueShare - PreTaxPortionUSD(refund, invoicesById);

    /// <summary>
    /// Pre-tax USD portion of refunds inside [start, end], for profit math.
    /// Each refund's revenue part is scaled by its invoice's <see cref="PreTaxShare"/>
    /// so the tax part of the refund, which was never profit on the revenue side,
    /// isn't subtracted again. Falls back to the full revenue part when
    /// the invoice link is missing.
    /// See docs/Calculations.md §8 for the rationale.
    /// </summary>
    public static decimal GetRefundedPreTaxInDateRangeUSD(
        IEnumerable<Payment> allPayments,
        IReadOnlyDictionary<string, Invoice> invoicesById,
        DateTime start, DateTime end)
    {
        return allPayments
            .Where(x => x.IsRefund && x.Date >= start && x.Date <= end)
            .Sum(p => PreTaxPortionUSD(p, invoicesById));
    }

    /// <summary>
    /// Display-currency variant of <see cref="GetRefundedPreTaxInDateRangeUSD"/>: converts each
    /// refund's pre-tax USD portion at its OWN date via <paramref name="toDisplay"/> before summing
    /// (docs/Calculations.md §3a Phase 2). Equals the USD sum for a USD display currency.
    /// </summary>
    public static decimal GetRefundedPreTaxInDateRangeDisplay(
        IEnumerable<Payment> allPayments,
        IReadOnlyDictionary<string, Invoice> invoicesById,
        DateTime start, DateTime end, Func<decimal, DateTime, decimal> toDisplay)
    {
        return allPayments
            .Where(x => x.IsRefund && x.Date >= start && x.Date <= end)
            .Sum(p => toDisplay(PreTaxPortionUSD(p, invoicesById), p.Date));
    }
}
