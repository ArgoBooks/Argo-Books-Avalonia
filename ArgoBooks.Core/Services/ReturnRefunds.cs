using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The money that moves when goods come back (docs/Calculations.md §10, Returns and Losses). A return
/// records what came back; its refund is what reaches the books. For a sale that is a refund payment, which
/// takes the sale's revenue and tax back off on the return date, and for a purchase it is a supplier credit,
/// an expense below zero, which takes the cost and the tax claimed back off the same way.
/// </summary>
public static class ReturnRefunds
{
    /// <summary>The refund recorded for a return: one of the two is set.</summary>
    public sealed record Refund(Payment? Payment, Expense? Expense);

    /// <summary>
    /// Records the refund for <paramref name="returnRecord"/> and links it, or returns null when nothing was
    /// paid back: a sale that was never paid, or one paid online, whose refund is made through the payment
    /// provider and arrives with the payment sync, or a bill not paid yet.
    /// </summary>
    public static Refund? Record(CompanyData data, Return returnRecord)
    {
        var refund = returnRecord.ReturnType == "Expense"
            ? data.Expenses.FirstOrDefault(e => e.Id == returnRecord.OriginalTransactionId) is { } purchase
              && purchase.IsPaid
              && returnRecord.NetRefund > 0
                ? new Refund(null, SupplierCredit(data, returnRecord, purchase))
                : null
            : data.Revenues.FirstOrDefault(r => r.Id == returnRecord.OriginalTransactionId) is { } sale
              && SaleRefund(data, returnRecord, sale) is { } payment
                ? new Refund(payment, null)
                : null;

        if (refund == null)
            return null;
        Restore(data, returnRecord, refund);
        return refund;
    }

    /// <summary>
    /// The return a supplier credit was recorded for, or null for any other expense. The credit's line is
    /// the purchase's, so the expense form would rebuild it as the whole purchase: it is changed through its return.
    /// </summary>
    public static Return? ReturnForCredit(CompanyData data, string expenseId) =>
        data.Returns.FirstOrDefault(r => r.ReturnType == "Expense" && r.RefundRecordId == expenseId);

    /// <summary>The refund recorded for <paramref name="returnRecord"/>, or null.</summary>
    public static Refund? Find(CompanyData data, Return returnRecord)
    {
        if (string.IsNullOrEmpty(returnRecord.RefundRecordId))
            return null;
        return returnRecord.ReturnType == "Expense"
            ? data.Expenses.FirstOrDefault(e => e.Id == returnRecord.RefundRecordId) is { } credit ? new Refund(null, credit) : null
            : data.Payments.FirstOrDefault(p => p.Id == returnRecord.RefundRecordId) is { } payment ? new Refund(payment, null) : null;
    }

    /// <summary>Takes a refund out of the books, for undoing its return.</summary>
    public static void Remove(CompanyData data, Refund refund)
    {
        if (refund.Payment is { } payment)
        {
            data.Payments.RemoveRecord(payment);
            UsdConversion.Set(data, UsdConversion.KeyOf(payment), null);
            RecalculateInvoice(data, payment);
        }
        if (refund.Expense is { } credit)
        {
            data.Expenses.RemoveRecord(credit);
            UsdConversion.Set(data, UsdConversion.KeyOf(credit), null);
        }
    }

    /// <summary>Puts a refund into the books and links it to its return.</summary>
    public static void Restore(CompanyData data, Return returnRecord, Refund refund)
    {
        if (refund.Payment is { } payment)
        {
            data.Payments.RestoreRecord(payment);
            UsdConversion.Requeue(data, payment);
            RecalculateInvoice(data, payment);
            returnRecord.RefundRecordId = payment.Id;
        }
        if (refund.Expense is { } credit)
        {
            data.Expenses.RestoreRecord(credit);
            UsdConversion.Requeue(data, credit);
            returnRecord.RefundRecordId = credit.Id;
        }
    }

    /// <summary>The items a sale's whole refund is worked out from, as marking it returned works it out.</summary>
    internal static decimal SaleItems(Revenue sale) => sale.Subtotal > 0 ? sale.Subtotal : sale.Amount;

    /// <summary>
    /// The tax a refund of <paramref name="amount"/> gives back: the transaction's tax in the share its whole
    /// refund (<see cref="ReturnLossAmounts.RefundFor"/>) that this refund is, so a partial return gives back part of it.
    /// </summary>
    private static decimal TaxShare(Transaction transaction, decimal itemsAmount, decimal amount)
    {
        var whole = ReturnLossAmounts.RefundFor(transaction, itemsAmount);
        return whole > 0 ? transaction.TaxAmount * Math.Min(1m, amount / whole) : 0m;
    }

    /// <summary>
    /// The tax a refund of <paramref name="amount"/> for <paramref name="returnRecord"/> gives back: the return's
    /// own tax in the share of its refund this is, or, for a return recorded before it kept its tax, the
    /// transaction's tax as <see cref="TaxShare"/> works it out.
    /// </summary>
    private static decimal TaxFor(Return returnRecord, Transaction transaction, decimal itemsAmount, decimal amount) =>
        returnRecord.RefundTax is { } tax
            ? returnRecord.RefundAmount > 0 ? tax * Math.Min(1m, amount / returnRecord.RefundAmount) : 0m
            : TaxShare(transaction, itemsAmount, amount);

    /// <summary>The supplier credits recorded for returns, which show on their purchase's row rather than as rows of their own.</summary>
    public static HashSet<string> SupplierCreditIds(CompanyData data) =>
        data.Returns
            .Where(r => r.ReturnType == "Expense" && r.RefundRecordId != null)
            .Select(r => r.RefundRecordId!)
            .ToHashSet();

    /// <summary>What the sale's other returns have refunded already, so a later partial return can't refund more than was paid.</summary>
    private static decimal RefundedBefore(CompanyData data, Return returnRecord, Revenue sale)
    {
        var refundIds = data.Returns
            .Where(r => r != returnRecord && r.OriginalTransactionId == sale.Id && r.RefundRecordId != null)
            .Select(r => r.RefundRecordId!)
            .ToHashSet();
        return data.Payments.Where(p => refundIds.Contains(p.Id)).Sum(p => Math.Abs(p.Amount));
    }

    private static void RecalculateInvoice(CompanyData data, Payment payment)
    {
        if (data.Invoices.FirstOrDefault(i => i.Id == payment.InvoiceId) is { } invoice)
            InvoiceTotalsService.Recalculate(invoice, data.Payments);
    }

    /// <summary>
    /// A refund of what the customer paid toward the returned sale, at most the return's refund, giving back
    /// its share of the sale's tax. Null when nothing was paid, or the payment was made online.
    /// </summary>
    private static Payment? SaleRefund(CompanyData data, Return returnRecord, Revenue sale)
    {
        var invoice = string.IsNullOrEmpty(sale.InvoiceId) ? null : data.Invoices.FirstOrDefault(i => i.Id == sale.InvoiceId);
        if (invoice != null && data.Payments.Any(p => p.InvoiceId == invoice.Id && p.Source == PaymentSource.Online))
            return null;

        // An invoice's net paid already has earlier refunds taken off.
        var paid = invoice != null
            ? invoice.NetPaid
            : RevenueAggregator.IsCollected(sale) ? sale.Total - RefundedBefore(data, returnRecord, sale) : 0m;
        var amount = Math.Min(returnRecord.NetRefund, paid);
        if (amount <= 0)
            return null;

        var payment = new Payment
        {
            Id = new IdGenerator(data).NextPaymentId(),
            InvoiceId = invoice?.Id ?? string.Empty,
            CustomerId = sale.CustomerId ?? string.Empty,
            Date = returnRecord.ReturnDate,
            Amount = -amount,
            PaymentMethod = sale.PaymentMethod,
            Notes = $"Refund for return {returnRecord.Id} of {sale.Id}.",
            IsRefund = true,
            OriginalCurrency = sale.OriginalCurrency,
            RefundTaxAmount = TaxFor(returnRecord, sale, SaleItems(sale), amount),
            CreatedAt = DateTime.UtcNow
        };
        UsdConversion.Write(payment, UsdConversion.EntryFor(payment), UsdConversion.CachedRate(payment.OriginalCurrency, payment.Date));
        return payment;
    }

    /// <summary>
    /// A supplier credit for the returned purchase: an expense below zero on the return date, for the return's
    /// refund, giving back its share of the purchase's tax, with the returned lines so it comes off the same categories.
    /// </summary>
    private static Expense SupplierCredit(CompanyData data, Return returnRecord, Expense purchase)
    {
        var amount = returnRecord.NetRefund;
        var tax = TaxFor(returnRecord, purchase, purchase.Amount, amount);

        var credit = new Expense
        {
            Id = new IdGenerator(data).NextExpenseId(returnRecord.ReturnDate),
            Date = returnRecord.ReturnDate,
            SupplierId = purchase.SupplierId,
            Description = $"Refund for {purchase.Id}",
            LineItems = ReturnedLines(returnRecord, purchase),
            Quantity = 1,
            UnitPrice = -(amount - tax),
            Amount = -(amount - tax),
            TaxRate = purchase.TaxRate,
            TaxAmount = -tax,
            Total = -amount,
            PaymentMethod = purchase.PaymentMethod,
            Notes = $"Supplier credit for return {returnRecord.Id}.",
            OriginalCurrency = purchase.OriginalCurrency,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        UsdConversion.SetAmounts(credit, UsdConversion.CachedRate(credit.OriginalCurrency, credit.Date));
        return credit;
    }

    /// <summary>
    /// Copies of the purchase lines that came back, at the quantity that came back. A return from before
    /// returns could be partial took the whole purchase, so it copies every line.
    /// </summary>
    private static List<LineItem> ReturnedLines(Return returnRecord, Expense purchase)
    {
        if (returnRecord.Items.Count == 0 || returnRecord.Items.Any(i => i.LineIndex == null))
            return purchase.LineItems.Select(li => li.Clone()).ToList();

        return returnRecord.Items
            .Where(i => i.LineIndex is { } index && index < purchase.LineItems.Count)
            .Select(i =>
            {
                var line = purchase.LineItems[i.LineIndex!.Value].Clone();
                line.Quantity = i.Quantity;
                return line;
            })
            .ToList();
    }
}
