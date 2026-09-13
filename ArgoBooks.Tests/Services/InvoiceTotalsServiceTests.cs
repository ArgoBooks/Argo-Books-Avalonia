using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for InvoiceTotalsService: deriving AmountPaid, AmountRefunded,
/// Balance, BalanceUSD, and Status from the Payment list.
/// </summary>
public class InvoiceTotalsServiceTests
{
    [Fact]
    public void RecalculateFromPayments_PartialPayment_LeavesBalance()
    {
        var invoice = new Invoice
        {
            Id = "INV-1",
            Total = 100m,
            OriginalCurrency = "USD"
        };
        var payments = new[]
        {
            new Payment { InvoiceId = "INV-1", Amount = 40m, OriginalCurrency = "USD" }
        };

        InvoiceTotalsService.RecalculateFromPayments(invoice, payments);

        Assert.Equal(40m, invoice.AmountPaid);
        Assert.Equal(60m, invoice.Balance);
        Assert.Equal(0m, invoice.AmountRefunded);
    }

    [Fact]
    public void RecalculateFromPayments_FullPayment_ZeroBalance()
    {
        var invoice = new Invoice { Id = "INV-1", Total = 100m, OriginalCurrency = "USD" };
        var payments = new[]
        {
            new Payment { InvoiceId = "INV-1", Amount = 100m, OriginalCurrency = "USD" }
        };

        InvoiceTotalsService.RecalculateFromPayments(invoice, payments);

        Assert.Equal(100m, invoice.AmountPaid);
        Assert.Equal(0m, invoice.Balance);
    }

    [Fact]
    public void RecalculateFromPayments_RefundDoesNotRaiseBalance()
    {
        // Per docs/Calculations.md §5: refunds reduce AmountRefunded /
        // bump the status, but don't make the customer owe again.
        var invoice = new Invoice { Id = "INV-1", Total = 100m, OriginalCurrency = "USD" };
        var payments = new[]
        {
            new Payment { InvoiceId = "INV-1", Amount = 100m, OriginalCurrency = "USD" },
            new Payment { InvoiceId = "INV-1", Amount = -30m, IsRefund = true, OriginalCurrency = "USD" }
        };

        InvoiceTotalsService.RecalculateFromPayments(invoice, payments);

        Assert.Equal(100m, invoice.AmountPaid);
        Assert.Equal(30m, invoice.AmountRefunded);
        Assert.Equal(0m, invoice.Balance);
    }

    [Fact]
    public void RecalculateFromPayments_OnlySumsMatchingInvoice()
    {
        var invoice = new Invoice { Id = "INV-1", Total = 100m, OriginalCurrency = "USD" };
        var payments = new[]
        {
            new Payment { InvoiceId = "INV-1", Amount = 40m, OriginalCurrency = "USD" },
            new Payment { InvoiceId = "INV-2", Amount = 999m, OriginalCurrency = "USD" }
        };

        InvoiceTotalsService.RecalculateFromPayments(invoice, payments);

        Assert.Equal(40m, invoice.AmountPaid);
    }

    [Fact]
    public void RecalculateStatus_FullRefund_FlipsToRefunded()
    {
        var invoice = new Invoice
        {
            Id = "INV-1",
            Total = 100m,
            AmountPaid = 100m,
            AmountRefunded = 100m,
            Status = InvoiceStatus.Paid
        };

        InvoiceTotalsService.RecalculateStatus(invoice);

        Assert.Equal(InvoiceStatus.Refunded, invoice.Status);
    }

    [Fact]
    public void RecalculateStatus_PartialRefund_FlipsToPartiallyRefunded()
    {
        var invoice = new Invoice
        {
            Id = "INV-1",
            Total = 100m,
            AmountPaid = 100m,
            AmountRefunded = 25m,
            Status = InvoiceStatus.Paid
        };

        InvoiceTotalsService.RecalculateStatus(invoice);

        Assert.Equal(InvoiceStatus.PartiallyRefunded, invoice.Status);
    }

    [Fact]
    public void RecalculateStatus_FullRefundWithProcessingFee_StillRefunded()
    {
        // Customer paid $103 ($100 invoice + $3 processing fee they absorbed).
        // A full refund returns $100 (the invoice value, not the fee).
        // Net paid afterward is $3, fee residue, not a re-payment. Status
        // should be Refunded.
        var invoice = new Invoice
        {
            Id = "INV-1",
            Total = 100m,
            AmountPaid = 103m,
            AmountRefunded = 100m,
            Status = InvoiceStatus.Paid
        };

        InvoiceTotalsService.RecalculateStatus(invoice);

        Assert.Equal(InvoiceStatus.Refunded, invoice.Status);
    }

    [Fact]
    public void RecalculateStatus_PayRefundPay_StaysPartiallyRefunded()
    {
        // Customer paid $100, was refunded $100, then paid $100 again.
        // AmountPaid=$200, AmountRefunded=$100. Net paid is $100, a full
        // invoice value of fresh money, not fee residue. Refund history
        // must remain visible, so status is PartiallyRefunded.
        var invoice = new Invoice
        {
            Id = "INV-1",
            Total = 100m,
            AmountPaid = 200m,
            AmountRefunded = 100m,
            Status = InvoiceStatus.Refunded
        };

        InvoiceTotalsService.RecalculateStatus(invoice);

        Assert.Equal(InvoiceStatus.PartiallyRefunded, invoice.Status);
    }

    [Fact]
    public void RecalculateStatus_DraftWithoutPayments_StaysAsDraft()
    {
        var invoice = new Invoice
        {
            Id = "INV-1",
            Total = 100m,
            AmountPaid = 0m,
            AmountRefunded = 0m,
            Status = InvoiceStatus.Draft
        };

        InvoiceTotalsService.RecalculateStatus(invoice);

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
    }

    [Fact]
    public void RecalculateStatus_FullPaymentAfterSent_FlipsToPaid()
    {
        var invoice = new Invoice
        {
            Id = "INV-1",
            Total = 100m,
            AmountPaid = 100m,
            Balance = 0m,
            Status = InvoiceStatus.Sent
        };

        InvoiceTotalsService.RecalculateStatus(invoice);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }
}
