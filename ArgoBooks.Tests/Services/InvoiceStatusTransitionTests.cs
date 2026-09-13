using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Invoice status is recomputed from the Payment rows by InvoiceTotalsService (docs/Calculations.md
/// §5, §6). The refund-status discriminator (net paid vs Total) is subtle: a fully-refunded invoice
/// with a processing-fee residue is "Refunded", while pay -> refund -> pay-again is
/// "PartiallyRefunded". These tests pin every branch.
/// </summary>
public class InvoiceStatusTransitionTests
{
    private static Invoice Invoice(decimal total, InvoiceStatus status = InvoiceStatus.Sent) => new()
    {
        Id = "INV-1",
        OriginalCurrency = "USD",
        Total = total,
        Balance = total,
        Status = status
    };

    private static Payment Pay(decimal amount, bool refund = false) => new()
    {
        InvoiceId = "INV-1",
        OriginalCurrency = "USD",
        Amount = amount,
        IsRefund = refund
    };

    private static InvoiceStatus StatusAfter(Invoice invoice, params Payment[] payments)
    {
        InvoiceTotalsService.Recalculate(invoice, new List<Payment>(payments));
        return invoice.Status;
    }

    [Fact]
    public void FullPayment_MarksPaid()
    {
        var invoice = Invoice(100m);
        Assert.Equal(InvoiceStatus.Paid, StatusAfter(invoice, Pay(100m)));
        Assert.Equal(100m, invoice.AmountPaid);
        Assert.Equal(0m, invoice.Balance);
    }

    [Fact]
    public void PartialPayment_MarksPartial()
    {
        var invoice = Invoice(200m);
        Assert.Equal(InvoiceStatus.Partial, StatusAfter(invoice, Pay(50m)));
        Assert.Equal(150m, invoice.Balance);
    }

    [Fact]
    public void PaidThenFullRefund_NoFee_MarksRefunded()
    {
        var invoice = Invoice(100m);
        Assert.Equal(InvoiceStatus.Refunded, StatusAfter(invoice, Pay(100m), Pay(-100m, refund: true)));
    }

    [Fact]
    public void PaidWithProcessingFee_ThenFullRefund_MarksRefunded()
    {
        // Customer absorbed a $3 fee: AmountPaid 103, AmountRefunded 100 (refund excludes the fee).
        // Net paid $3 < Total, so this is fee residue, not a second payment: Refunded.
        var invoice = Invoice(100m);
        Assert.Equal(InvoiceStatus.Refunded, StatusAfter(invoice, Pay(103m), Pay(-100m, refund: true)));
    }

    [Fact]
    public void PayRefundPayAgain_MarksPartiallyRefunded()
    {
        // Net paid = 200 - 100 = 100 >= Total, so the customer paid the invoice over again on top of
        // the refund: keep the refund history visible with PartiallyRefunded.
        var invoice = Invoice(100m);
        Assert.Equal(InvoiceStatus.PartiallyRefunded,
            StatusAfter(invoice, Pay(100m), Pay(-100m, refund: true), Pay(100m)));
    }

    [Fact]
    public void PartialRefund_MarksPartiallyRefunded()
    {
        var invoice = Invoice(100m);
        Assert.Equal(InvoiceStatus.PartiallyRefunded, StatusAfter(invoice, Pay(100m), Pay(-30m, refund: true)));
    }

    [Fact]
    public void NoPaymentsOrRefunds_LeavesLifecycleStatusUntouched()
    {
        var invoice = Invoice(100m, InvoiceStatus.Draft);
        Assert.Equal(InvoiceStatus.Draft, StatusAfter(invoice));
    }

    /// <summary>
    /// Deleting, undoing or moving an invoice's only payment leaves nothing paid, so it is owed again.
    /// Left as Paid it dropped out of Outstanding and could never go overdue.
    /// </summary>
    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Pending)]
    [InlineData(InvoiceStatus.Viewed)]
    [InlineData(InvoiceStatus.Draft)]
    public void RemovingEveryPayment_ReturnsToTheStatusBeforeTheFirst(InvoiceStatus before)
    {
        var invoice = Invoice(100m, before);
        StatusAfter(invoice, Pay(40m));
        StatusAfter(invoice, Pay(100m));

        Assert.Equal(before, StatusAfter(invoice));
        Assert.Equal(100m, invoice.Balance);
    }

    [Fact]
    public void RemovingEveryPaymentAfterARefund_ReturnsToTheStatusBeforeTheFirst()
    {
        var invoice = Invoice(100m);
        StatusAfter(invoice, Pay(100m), Pay(-100m, refund: true));

        Assert.Equal(InvoiceStatus.Sent, StatusAfter(invoice));
    }

    /// <summary>
    /// An imported invoice can arrive Paid with no payment rows. No payment moved it there, so there
    /// is nothing to go back to and its status stays as imported.
    /// </summary>
    [Fact]
    public void PaidWithoutEverHavingAPayment_KeepsItsStatus()
    {
        var invoice = Invoice(100m, InvoiceStatus.Paid);

        Assert.Equal(InvoiceStatus.Paid, StatusAfter(invoice));
    }

    [Fact]
    public void PaymentInDifferentCurrency_IsNotCounted()
    {
        // A payment tagged with a currency other than the invoice's must not move the invoice totals
        // (docs/Calculations.md §5); this is why the payment modal stamps the invoice's currency.
        var invoice = Invoice(100m);
        var foreignPayment = new Payment { InvoiceId = "INV-1", OriginalCurrency = "EUR", Amount = 100m };

        var status = StatusAfter(invoice, foreignPayment);

        Assert.Equal(0m, invoice.AmountPaid);
        Assert.Equal(100m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Sent, status);
    }
}
