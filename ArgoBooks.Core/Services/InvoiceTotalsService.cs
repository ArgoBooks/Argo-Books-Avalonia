using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Single point of truth for recomputing the stored totals on an
/// <see cref="Invoice"/> from its <see cref="Payment"/> rows.
///
/// Payments are added from several places (portal sync, spreadsheet import,
/// manual entry), and a path that updates the fields itself can miss one and
/// let the stored totals drift. Anything that mutates an invoice's payments
/// should call <see cref="RecalculateFromPayments"/> followed by
/// <see cref="RecalculateStatus"/>.
///
/// See docs/Calculations.md §5 for the field semantics.
/// </summary>
public static class InvoiceTotalsService
{
    /// <summary>
    /// Derive AmountPaid, AmountRefunded, Balance, and BalanceUSD from the
    /// Payment rows tied to this invoice. Idempotent.
    /// </summary>
    public static void RecalculateFromPayments(
        Invoice invoice, IEnumerable<Payment> allPayments)
    {
        // Normalize currency comparison: treat null/empty as "USD" and
        // compare case-insensitively. This matches the historical inline
        // logic in PaymentPortalService.
        var invoiceCurrency = string.IsNullOrEmpty(invoice.OriginalCurrency)
            ? "USD" : invoice.OriginalCurrency;

        bool MatchesInvoiceCurrency(Payment p) =>
            string.Equals(
                string.IsNullOrEmpty(p.OriginalCurrency) ? "USD" : p.OriginalCurrency,
                invoiceCurrency,
                StringComparison.OrdinalIgnoreCase);

        var invoicePayments = allPayments.Where(p => p.InvoiceId == invoice.Id).ToList();

        // AmountPaid: gross positive payments in the invoice's currency.
        invoice.AmountPaid = invoicePayments
            .Where(p => !p.IsRefund && p.Amount > 0 && MatchesInvoiceCurrency(p))
            .Sum(p => p.Amount);

        // AmountRefunded: absolute sum of refund rows in the invoice's currency.
        invoice.AmountRefunded = invoicePayments
            .Where(p => p.IsRefund && MatchesInvoiceCurrency(p))
            .Sum(p => Math.Abs(p.Amount));

        // Balance: remaining owed. Refunds don't raise the balance, once
        // the customer paid, returning their money doesn't make them owe
        // again.
        invoice.Balance = Math.Max(0, invoice.Total - invoice.AmountPaid);

        // Keep USD fields in sync when we have a conversion ratio.
        if (invoice.TotalUSD > 0)
        {
            var totalPaidUSD = invoicePayments
                .Where(p => !p.IsRefund && p.Amount > 0)
                .Sum(p => p.EffectiveAmountUSD);
            invoice.BalanceUSD = Math.Max(0, invoice.TotalUSD - totalPaidUSD);
        }
    }

    /// <summary>
    /// Whether the customer has paid the whole invoice. Refunds don't undo it: a refund is
    /// subtracted from revenue on its own date (docs/Calculations.md §8), so the revenue it
    /// refunds has to stay counted.
    /// </summary>
    public static bool IsPaidInFull(Invoice invoice) =>
        invoice.Total > 0 && invoice.AmountPaid + 0.01m >= invoice.Total;

    /// <summary>
    /// Counts an invoice's revenue as collected once the invoice is paid in full, and not before
    /// (docs/Calculations.md §7). Called after <see cref="Recalculate"/> by every path that
    /// records or removes a payment on an invoice, so one paid by hand counts the same as one
    /// paid online.
    /// </summary>
    public static void SyncLinkedRevenueStatus(Invoice invoice, IEnumerable<Revenue> revenues)
    {
        var status = IsPaidInFull(invoice) ? RevenuePaymentStatus.Paid : RevenuePaymentStatus.Unpaid;
        foreach (var revenue in revenues.Where(r => r.InvoiceId == invoice.Id))
            revenue.PaymentStatus = status;
    }

    /// <summary>
    /// Recompute the invoice's stored Status from AmountPaid / AmountRefunded,
    /// as Paid / Partial / PartiallyRefunded / Refunded. Lifecycle states
    /// (Draft / Pending / Sent / Viewed / Cancelled / Overdue) are owned by the
    /// surfaces that drive them: one is left only once a payment or refund has
    /// happened, and is remembered in <see cref="Invoice.StatusBeforePayment"/>
    /// so the invoice goes back to it when every payment is removed again.
    /// </summary>
    public static void RecalculateStatus(Invoice invoice)
    {
        var hasPayments = invoice.AmountPaid > 0 || invoice.AmountRefunded > 0;
        if (invoice.Status == InvoiceStatus.Draft
            || invoice.Status == InvoiceStatus.Pending
            || invoice.Status == InvoiceStatus.Sent
            || invoice.Status == InvoiceStatus.Viewed
            || invoice.Status == InvoiceStatus.Cancelled
            || invoice.Status == InvoiceStatus.Overdue)
        {
            // Lifecycle states are external, only re-evaluate once a
            // payment or refund has actually happened.
            if (!hasPayments)
                return;
            invoice.StatusBeforePayment = invoice.Status;
        }
        else if (!hasPayments && invoice.StatusBeforePayment is { } before)
        {
            // Every payment was deleted, undone or moved to another invoice, so it is owed again.
            invoice.Status = before;
            invoice.StatusBeforePayment = null;
            return;
        }

        if (invoice.AmountRefunded > 0 && invoice.AmountPaid > 0)
        {
            invoice.Status = RefundedStatus(invoice);
            return;
        }

        if (invoice.Balance <= 0 && invoice.AmountPaid > 0)
        {
            invoice.Status = InvoiceStatus.Paid;
            return;
        }

        if (invoice.AmountPaid > 0)
        {
            invoice.Status = InvoiceStatus.Partial;
        }
    }

    /// <summary>
    /// Which refund status an invoice with refunds has (docs/Calculations.md §6).
    ///
    /// The refund-status rule has two competing inputs:
    ///   (a) Processing fees make AmountPaid &gt; Total even when a single
    ///       full refund returns the entire invoice value (refunds don't
    ///       include the fee). Status should be Refunded.
    ///   (b) A second payment after a refund also makes AmountPaid &gt;&gt; Total,
    ///       but the refund history should stay visible. Status should be
    ///       PartiallyRefunded.
    /// The discriminator: net paid (AmountPaid − AmountRefunded). If it's
    /// less than one full invoice value it's fee residue (case a); if it's
    /// at least one full invoice value the customer has paid the invoice
    /// over again on top of the refund (case b).
    /// Tiny epsilon for cent-level float drift.
    /// </summary>
    public static InvoiceStatus RefundedStatus(Invoice invoice)
    {
        if (invoice.AmountRefunded + 0.01m < invoice.Total)
            return InvoiceStatus.PartiallyRefunded;

        // Refund covers a full invoice value. Distinguish "single
        // pay + fee, fully refunded" from "pay → refund → pay again":
        // the latter leaves a net of at least one invoice value.
        var netPaid = invoice.AmountPaid - invoice.AmountRefunded;
        return netPaid + 0.01m < invoice.Total
            ? InvoiceStatus.Refunded
            : InvoiceStatus.PartiallyRefunded;
    }

    /// <summary>
    /// The status to show for an invoice (docs/Calculations.md §6), the same one the invoices list
    /// shows: Overdue is derived rather than stored, and a refunded invoice's status is worked out
    /// afresh by <see cref="RefundedStatus"/> in case the stored one is stale.
    /// </summary>
    public static InvoiceStatus DisplayStatus(Invoice invoice)
    {
        if (invoice.IsOverdue)
            return InvoiceStatus.Overdue;
        if (invoice.AmountRefunded > 0 && invoice.Total > 0)
            return RefundedStatus(invoice);
        return invoice.Status;
    }

    /// <summary>
    /// Convenience wrapper: recalc totals, then recalc status. Use this
    /// after any mutation to the invoice's payment list.
    /// </summary>
    public static void Recalculate(Invoice invoice, IEnumerable<Payment> allPayments)
    {
        RecalculateFromPayments(invoice, allPayments);
        RecalculateStatus(invoice);
    }
}
