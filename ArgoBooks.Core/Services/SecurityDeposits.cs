using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// A security deposit is held for the customer, not earned, so it stays out of revenue unless the
/// business keeps it (docs/Calculations.md §4), and a refund that hands it back comes off nothing.
/// </summary>
public static class SecurityDeposits
{
    /// <summary>The invoice's deposit less what refunds have given back and what was kept as revenue.</summary>
    public static decimal StillHeld(Invoice invoice, IEnumerable<Payment> payments, IEnumerable<Revenue> revenues) =>
        Math.Max(0m, invoice.SecurityDeposit
            - payments.Where(p => p.IsRefund && p.InvoiceId == invoice.Id).Sum(p => p.DepositAmount)
            - revenues.Where(r => r.InvoiceId == invoice.Id && r.IsKeptDeposit).Sum(r => r.Total));

    /// <summary>
    /// How much of a refund gives the deposit back. A refund made in Argo Books names its deposit
    /// part. One made in the provider's dashboard doesn't, and is taken from the deposit first,
    /// which is what a refund on a deposit invoice usually is. Never more than is still held: a
    /// deposit already kept is revenue, so refunding it comes off revenue.
    /// </summary>
    public static decimal RefundPortion(decimal refundAmount, decimal? namedDepositPart, decimal stillHeld) =>
        Math.Max(0m, Math.Min(Math.Min(namedDepositPart ?? refundAmount, refundAmount), stillHeld));
}
