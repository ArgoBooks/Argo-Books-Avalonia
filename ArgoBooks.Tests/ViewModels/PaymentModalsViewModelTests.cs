using System.Reflection;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Drives the real PaymentModalsViewModel save/undo/redo flows against an in-memory company. Guards
/// the currency-tagging fix (a payment must settle its invoice in the invoice's currency) and the
/// undo/redo round-trip for recorded payments.
/// </summary>
public class PaymentModalsViewModelTests : ModalViewModelTestBase
{
    private Invoice AddInvoice(string currency, decimal total)
    {
        var invoice = new Invoice
        {
            Id = "INV-001",
            InvoiceNumber = "INV-001",
            CustomerId = "CUST-1",
            OriginalCurrency = currency,
            Subtotal = total,
            Total = total,
            TotalUSD = total,
            Balance = total,
            Status = InvoiceStatus.Sent,
            IssueDate = new DateTime(2026, 1, 5),
            DueDate = new DateTime(2026, 2, 5)
        };
        Company.Invoices.Add(invoice);
        return invoice;
    }

    private static PaymentModalsViewModel NewPaymentFor(string invoiceId, string amount)
    {
        var vm = new PaymentModalsViewModel
        {
            ModalInvoiceId = invoiceId,
            ModalAmount = amount,
            ModalPaymentMethod = "Cash",
            ModalDate = new DateTimeOffset(new DateTime(2026, 1, 10), TimeSpan.Zero)
        };
        return vm;
    }

    [Fact]
    public async Task SaveNewPayment_ForeignCurrencyInvoice_TagsInvoiceCurrencyAndCounts()
    {
        var invoice = AddInvoice("EUR", 119m);

        await NewPaymentFor(invoice.Id, "119").SaveNewPayment();

        var payment = Assert.Single(Company.Payments);
        // The fix: the payment settles the EUR invoice in EUR, not the company display currency.
        Assert.Equal("EUR", payment.OriginalCurrency);
        // Because the currency matches, InvoiceTotalsService counts it toward the invoice.
        Assert.Equal(119m, invoice.AmountPaid);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public async Task SaveNewPayment_PartialPayment_SetsPartialStatus()
    {
        var invoice = AddInvoice("USD", 200m);

        await NewPaymentFor(invoice.Id, "50").SaveNewPayment();

        Assert.Equal(50m, invoice.AmountPaid);
        Assert.Equal(150m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
    }

    [Fact]
    public async Task SaveNewPayment_ThenUndo_RemovesPaymentAndRestoresBalance()
    {
        var invoice = AddInvoice("USD", 100m);
        await NewPaymentFor(invoice.Id, "100").SaveNewPayment();
        Assert.Single(Company.Payments);

        Undo();

        Assert.Empty(Company.Payments);
        Assert.Equal(0m, invoice.AmountPaid);
        Assert.Equal(100m, invoice.Balance);
    }

    [Fact]
    public async Task SaveNewPayment_UndoThenRedo_RestoresPayment()
    {
        var invoice = AddInvoice("USD", 100m);
        await NewPaymentFor(invoice.Id, "100").SaveNewPayment();
        Undo();

        Redo();

        var payment = Assert.Single(Company.Payments);
        Assert.Equal(100m, payment.Amount);
        Assert.Equal(100m, invoice.AmountPaid);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    private Revenue AddInvoiceRevenue(Invoice invoice)
    {
        var revenue = new Revenue
        {
            Id = "REV-001",
            InvoiceId = invoice.Id,
            Date = invoice.IssueDate,
            OriginalCurrency = invoice.OriginalCurrency,
            Total = invoice.Total,
            TotalUSD = invoice.TotalUSD,
            PaymentStatus = RevenuePaymentStatus.Unpaid
        };
        Company.Revenues.Add(revenue);
        return revenue;
    }

    /// <summary>
    /// Sending an invoice creates its revenue unpaid, and only the online payment path marked it
    /// collected, so an invoice paid by cash or cheque never reached revenue or profit.
    /// </summary>
    [Fact]
    public async Task SaveNewPayment_PayingTheInvoiceInFull_CountsItsRevenue()
    {
        var invoice = AddInvoice("USD", 100m);
        var revenue = AddInvoiceRevenue(invoice);

        await NewPaymentFor(invoice.Id, "100").SaveNewPayment();
        Assert.Equal(RevenuePaymentStatus.Paid, revenue.PaymentStatus);

        Undo();
        Assert.Equal(RevenuePaymentStatus.Unpaid, revenue.PaymentStatus);

        Redo();
        Assert.Equal(RevenuePaymentStatus.Paid, revenue.PaymentStatus);
    }

    [Fact]
    public async Task SaveNewPayment_PayingPartOfTheInvoice_LeavesItsRevenueUncollected()
    {
        var invoice = AddInvoice("USD", 100m);
        var revenue = AddInvoiceRevenue(invoice);

        await NewPaymentFor(invoice.Id, "40").SaveNewPayment();

        Assert.Equal(RevenuePaymentStatus.Unpaid, revenue.PaymentStatus);
    }

    /// <summary>
    /// Undoing the only payment left the invoice Paid with its whole balance owing, so it dropped out
    /// of Outstanding and could never go overdue. It goes back to Sent, and its revenue with it.
    /// </summary>
    [Fact]
    public async Task SaveNewPayment_ThenUndo_ReturnsTheInvoiceToSent()
    {
        var invoice = AddInvoice("USD", 100m);
        var revenue = AddInvoiceRevenue(invoice);
        await NewPaymentFor(invoice.Id, "100").SaveNewPayment();

        Undo();
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(RevenuePaymentStatus.Unpaid, revenue.PaymentStatus);

        Redo();
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public async Task SaveEditedPayment_MovingTheOnlyPaymentToAnotherInvoice_ReturnsTheFirstToSent()
    {
        var first = AddInvoice("USD", 100m);
        var second = new Invoice
        {
            Id = "INV-002",
            InvoiceNumber = "INV-002",
            CustomerId = "CUST-1",
            OriginalCurrency = "USD",
            Total = 100m,
            TotalUSD = 100m,
            Balance = 100m,
            Status = InvoiceStatus.Sent,
            IssueDate = new DateTime(2026, 1, 6),
            DueDate = new DateTime(2026, 2, 6)
        };
        Company.Invoices.Add(second);
        await NewPaymentFor(first.Id, "100").SaveNewPayment();
        var payment = Assert.Single(Company.Payments);

        var vm = new PaymentModalsViewModel();
        vm.OpenEditModal(new PaymentDisplayItem { Id = payment.Id });
        vm.SelectedInvoice = vm.InvoiceOptions.First(o => o.Id == second.Id);
        await vm.SaveEditedPayment();

        Assert.Equal(InvoiceStatus.Sent, first.Status);
        Assert.Equal(100m, first.Balance);
        Assert.Equal(InvoiceStatus.Paid, second.Status);

        Undo();
        Assert.Equal(InvoiceStatus.Paid, first.Status);
        Assert.Equal(InvoiceStatus.Sent, second.Status);
    }

    /// <summary>
    /// Payments are recorded on invoices that have been sent, which is when the invoice row offers it.
    /// A draft in this list could be paid, turning it Paid without its revenue ever being created.
    /// </summary>
    [Fact]
    public void OpenAddModal_DoesNotOfferDrafts()
    {
        var sent = AddInvoice("USD", 100m);
        Company.Invoices.Add(new Invoice
        {
            Id = "INV-002",
            InvoiceNumber = "INV-002",
            CustomerId = "CUST-1",
            Total = 50m,
            Balance = 50m,
            Status = InvoiceStatus.Draft,
            IssueDate = new DateTime(2026, 1, 6),
            DueDate = new DateTime(2026, 2, 6)
        });

        var vm = new PaymentModalsViewModel();
        vm.OpenAddModal();

        Assert.Contains(vm.InvoiceOptions, o => o.Id == sent.Id);
        Assert.DoesNotContain(vm.InvoiceOptions, o => o.Id == "INV-002");
    }

    /// <summary>
    /// A draft paid before drafts left the list keeps its payment linked when that payment is edited.
    /// </summary>
    [Fact]
    public void OpenEditModal_PaymentAlreadyOnADraft_KeepsThatInvoice()
    {
        var draft = AddInvoice("USD", 100m);
        draft.Status = InvoiceStatus.Draft;
        Company.Payments.Add(new Payment
        {
            Id = "PAY-001",
            InvoiceId = draft.Id,
            CustomerId = "CUST-1",
            Amount = 100m,
            AmountUSD = 100m,
            OriginalCurrency = "USD",
            Date = new DateTime(2026, 1, 10)
        });

        var vm = new PaymentModalsViewModel();
        vm.OpenEditModal(new PaymentDisplayItem { Id = "PAY-001" });

        Assert.Equal(draft.Id, vm.SelectedInvoice?.Id);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    /// <summary>No exchange rate service at all, so no rate for any date.</summary>
    private static IDisposable NoExchangeRates()
    {
        var instance = typeof(ExchangeRateService).GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;
        var prior = instance.GetValue(null);
        instance.SetValue(null, null);
        return new Restore(() => instance.SetValue(null, prior));
    }

    /// <summary>
    /// With no rate for the payment's date, the foreign amount was stored as the USD figure, so a
    /// ¥50,000 payment counted as $50,000. It now waits for its own date's rate (Rule 3a).
    /// </summary>
    [Fact]
    public async Task SaveNewPayment_NoRateForItsDate_WaitsForConversionInsteadOfCountingAsUsd()
    {
        using var noRates = NoExchangeRates();
        var invoice = AddInvoice("JPY", 100000m);

        await NewPaymentFor(invoice.Id, "50000").SaveNewPayment();

        var payment = Assert.Single(Company.Payments);
        Assert.True(payment.IsPendingConversion);
        Assert.Equal(0m, payment.EffectiveAmountUSD);
        var queued = Assert.Single(Company.PendingConversions);
        Assert.Equal((payment.Id, "Payment", "JPY", 50000m), (queued.TransactionId, queued.TransactionType, queued.OriginalCurrency, queued.Total));

        Undo();
        Assert.Empty(Company.PendingConversions);

        Redo();
        Assert.Equal(payment.Id, Assert.Single(Company.PendingConversions).TransactionId);
    }
}
