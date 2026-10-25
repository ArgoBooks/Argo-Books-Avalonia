using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The money that moves when goods come back (docs/Calculations.md §10, Returns and Losses). A return
/// records what came back; its refund is what reaches the books. For a sale it comes off what the customer
/// still owes first, as a credit note, and only the rest is paid back, as a refund payment; both take the
/// sale's revenue and tax back off on the return date. For a purchase it is a supplier credit, an expense
/// below zero, which takes the cost and the tax claimed back off the same way.
/// </summary>
public static class ReturnRefunds
{
    /// <summary>
    /// The refund recorded for a return: a supplier credit for a purchase, or for a sale a refund payment,
    /// a credit note or both.
    /// </summary>
    public sealed record Refund(Payment? Payment, Expense? Expense, Payment? CreditNote = null);

    /// <summary>
    /// Records the refund for <paramref name="returnRecord"/> and links it, or returns null when nothing is
    /// owed or paid back: a sale paid in full online, whose refund is made through the payment provider and
    /// arrives with the payment sync. A purchase always gets a supplier credit, which on a bill not paid yet
    /// comes off what is owed (<see cref="FollowBill"/>).
    /// </summary>
    public static Refund? Record(CompanyData data, Return returnRecord)
    {
        var refund = returnRecord.ReturnType == "Expense"
            ? data.Expenses.FirstOrDefault(e => e.Id == returnRecord.OriginalTransactionId) is { } purchase
              && returnRecord.NetRefund > 0
                ? new Refund(null, SupplierCredit(data, returnRecord, purchase))
                : null
            : data.Revenues.FirstOrDefault(r => r.Id == returnRecord.OriginalTransactionId) is { } sale
                ? SaleRefund(data, returnRecord, sale)
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
        if (returnRecord.ReturnType == "Expense")
            return !string.IsNullOrEmpty(returnRecord.RefundRecordId)
                   && data.Expenses.FirstOrDefault(e => e.Id == returnRecord.RefundRecordId) is { } credit
                ? new Refund(null, credit)
                : null;

        var payment = string.IsNullOrEmpty(returnRecord.RefundRecordId)
            ? null
            : data.Payments.FirstOrDefault(p => p.Id == returnRecord.RefundRecordId);
        var creditNote = string.IsNullOrEmpty(returnRecord.CreditNoteId)
            ? null
            : data.Payments.FirstOrDefault(p => p.Id == returnRecord.CreditNoteId);
        return payment == null && creditNote == null ? null : new Refund(payment, null, creditNote);
    }

    /// <summary>Takes a refund out of the books, for undoing its return.</summary>
    public static void Remove(CompanyData data, Refund refund)
    {
        foreach (var payment in new[] { refund.Payment, refund.CreditNote }.OfType<Payment>())
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
        if (refund.CreditNote is { } creditNote)
        {
            data.Payments.RestoreRecord(creditNote);
            UsdConversion.Requeue(data, creditNote);
            RecalculateInvoice(data, creditNote);
            returnRecord.CreditNoteId = creditNote.Id;
        }
        if (refund.Expense is { } credit)
        {
            data.Expenses.RestoreRecord(credit);
            UsdConversion.Requeue(data, credit);
            returnRecord.RefundRecordId = credit.Id;
            if (data.Expenses.FirstOrDefault(e => e.Id == returnRecord.OriginalTransactionId) is { } purchase)
                Follow(credit, purchase);
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

    /// <summary>
    /// Keeps the supplier credits for a purchase's returns owed and paid along with it. On a bill the credit is
    /// owed until the bill is paid, so it comes off what is owed, and it counts as paid on the later of the day
    /// the bill was paid and the day of the return, which is when the money stopped being owed.
    /// Called whenever the bill's paid date changes.
    /// </summary>
    public static void FollowBill(CompanyData data, Expense purchase)
    {
        var creditIds = data.Returns
            .Where(r => r.ReturnType == "Expense" && r.OriginalTransactionId == purchase.Id && r.RefundRecordId != null)
            .Select(r => r.RefundRecordId!)
            .ToHashSet();
        foreach (var credit in data.Expenses.Where(e => creditIds.Contains(e.Id)))
            Follow(credit, purchase);
    }

    private static void Follow(Expense credit, Expense purchase)
    {
        credit.DueDate = purchase.DueDate;
        credit.PaidDate = purchase.DueDate == null || purchase.PaidDate is not { } paid
            ? null
            : paid.Date > credit.Date.Date ? paid.Date : credit.Date.Date;
    }

    /// <summary>The supplier credits recorded for returns, which show on their purchase's row rather than as rows of their own.</summary>
    public static HashSet<string> SupplierCreditIds(CompanyData data) =>
        data.Returns
            .Where(r => r.ReturnType == "Expense" && r.RefundRecordId != null)
            .Select(r => r.RefundRecordId!)
            .ToHashSet();

    /// <summary>What the sale's other returns have refunded and credited already, so a later return can't take off more than is left.</summary>
    private static (decimal Refunded, decimal Credited) BeforeThis(CompanyData data, Return returnRecord, Revenue sale)
    {
        var others = data.Returns.Where(r => r != returnRecord && r.OriginalTransactionId == sale.Id).ToList();
        decimal Sum(IEnumerable<string?> ids)
        {
            var set = ids.OfType<string>().ToHashSet();
            return data.Payments.Where(p => set.Contains(p.Id)).Sum(p => Math.Abs(p.Amount));
        }
        return (Sum(others.Select(r => r.RefundRecordId)), Sum(others.Select(r => r.CreditNoteId)));
    }

    private static void RecalculateInvoice(CompanyData data, Payment payment)
    {
        if (data.Invoices.FirstOrDefault(i => i.Id == payment.InvoiceId) is not { } invoice)
            return;
        InvoiceTotalsService.Recalculate(invoice, data.Payments);
        InvoiceTotalsService.SyncLinkedRevenueStatus(invoice, data.Revenues);
    }

    /// <summary>
    /// What a sale's return takes off, split the way accounting software applies a credit note. The part the
    /// customer still owed comes off what they owe, as a credit note, and only the rest is paid back, at most
    /// what they paid. Each gives back its share of the return's tax. A cash refund on an invoice paid online is
    /// left to the payment provider, and arrives with the payment sync.
    /// </summary>
    private static Refund? SaleRefund(CompanyData data, Return returnRecord, Revenue sale)
    {
        var invoice = string.IsNullOrEmpty(sale.InvoiceId) ? null : data.Invoices.FirstOrDefault(i => i.Id == sale.InvoiceId);
        var (refundedBefore, creditedBefore) = BeforeThis(data, returnRecord, sale);

        // A sale with no invoice is owed in full until it is marked paid, and paid in full after.
        decimal owed, paid;
        if (invoice != null)
        {
            owed = Math.Max(0m, invoice.Total - invoice.AmountPaid - invoice.AmountCredited);
            paid = invoice.NetPaid;
        }
        else if (RevenueAggregator.IsCollected(sale))
        {
            owed = 0m;
            paid = sale.Total - creditedBefore - refundedBefore;
        }
        else
        {
            owed = sale.Total - creditedBefore;
            paid = 0m;
        }

        var credit = Math.Max(0m, Math.Min(returnRecord.NetRefund, owed));
        var cash = Math.Max(0m, Math.Min(returnRecord.NetRefund - credit, paid));
        if (invoice != null && data.Payments.Any(p => p.InvoiceId == invoice.Id && p.Source == PaymentSource.Online))
            cash = 0m;
        if (credit <= 0 && cash <= 0)
            return null;

        var ids = new IdGenerator(data);
        Payment Make(decimal amount, bool isCreditNote)
        {
            var payment = new Payment
            {
                Id = ids.NextPaymentId(),
                InvoiceId = invoice?.Id ?? string.Empty,
                CustomerId = sale.CustomerId ?? string.Empty,
                Date = returnRecord.ReturnDate,
                Amount = -amount,
                PaymentMethod = sale.PaymentMethod,
                Notes = isCreditNote
                    ? $"Credit note for return {returnRecord.Id} of {sale.Id}."
                    : $"Refund for return {returnRecord.Id} of {sale.Id}.",
                IsRefund = true,
                IsCreditNote = isCreditNote,
                OriginalCurrency = sale.OriginalCurrency,
                RefundTaxAmount = TaxFor(returnRecord, sale, SaleItems(sale), amount),
                CreatedAt = DateTime.UtcNow
            };
            UsdConversion.Write(payment, UsdConversion.EntryFor(payment), UsdConversion.CachedRate(payment.OriginalCurrency, payment.Date));
            return payment;
        }

        var creditNote = credit > 0 ? Make(credit, isCreditNote: true) : null;
        var refund = cash > 0 ? Make(cash, isCreditNote: false) : null;
        return new Refund(refund, null, creditNote);
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
        Follow(credit, purchase);
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
