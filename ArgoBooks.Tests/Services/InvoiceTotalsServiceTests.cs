using System.Globalization;
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
    // IsPaidInFull rounds at the currency's own decimals, not a fixed two. The yen has none, so a total carrying a fraction is settled by the whole-yen figure the customer is shown and pays.
    [Theory]
    [InlineData("JPY", "1000.4", "1000", true)]
    [InlineData("JPY", "1000.5", "1001", true)]
    [InlineData("JPY", "1000", "999", false)]
    [InlineData("USD", "37.6629", "37.66", true)]
    [InlineData("USD", "37.66", "37.65", false)]
    public void IsPaidInFull_RoundsAtTheCurrencysOwnDecimals(string currency, string total, string paid, bool expected)
    {
        var invoice = new Invoice
        {
            Id = "INV-1",
            Total = decimal.Parse(total, CultureInfo.InvariantCulture),
            AmountPaid = decimal.Parse(paid, CultureInfo.InvariantCulture),
            Status = InvoiceStatus.Sent,
            OriginalCurrency = currency
        };

        Assert.Equal(expected, invoice.IsPaidInFull);
    }

    // Nothing is owed on an invoice with no total, so it is not "paid in full" either.
    [Fact]
    public void IsPaidInFull_IsFalseWhenThereIsNoTotal()
    {
        var invoice = new Invoice { Id = "INV-1", Total = 0m, AmountPaid = 0m, OriginalCurrency = "JPY" };

        Assert.False(invoice.IsPaidInFull);
    }

    [Fact]
    public void FractionalCentTotal_PaidAtTheDisplayedAmount_IsPaidAndNotOverdue()
    {
        // 13% tax on 33.33 stores 37.6629 on an invoice reading $37.66, and the customer pays what it reads, leaving a third of a cent.
        var invoice = new Invoice
        {
            Id = "INV-1",
            Total = 37.6629m,
            Status = InvoiceStatus.Sent,
            DueDate = DateTime.Today.AddDays(-30),
            OriginalCurrency = "USD"
        };
        var payments = new[]
        {
            new Payment { InvoiceId = "INV-1", Amount = 37.66m, OriginalCurrency = "USD" }
        };

        InvoiceTotalsService.RecalculateFromPayments(invoice, payments);
        InvoiceTotalsService.RecalculateStatus(invoice);

        Assert.True(invoice.IsPaidInFull);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.False(invoice.IsOverdue);
    }

    [Fact]
    public void FractionalCentTotal_PaidAtTheDisplayedAmount_MarksTheLinkedRevenuePaid()
    {
        var invoice = new Invoice { Id = "INV-1", Total = 37.6629m, OriginalCurrency = "USD" };
        var payments = new[]
        {
            new Payment { InvoiceId = "INV-1", Amount = 37.66m, OriginalCurrency = "USD" }
        };
        var revenues = new[] { new Revenue { Id = "REV-1", InvoiceId = "INV-1" } };

        InvoiceTotalsService.RecalculateFromPayments(invoice, payments);
        InvoiceTotalsService.RecalculateStatus(invoice);
        InvoiceTotalsService.SyncLinkedRevenueStatus(invoice, revenues);

        // The two used to disagree: the revenue said collected, the invoice said Partial.
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(RevenuePaymentStatus.Paid, revenues[0].PaymentStatus);
    }

    [Fact]
    public void ShortByOneCent_IsStillPartial()
    {
        var invoice = new Invoice { Id = "INV-1", Total = 100m, Status = InvoiceStatus.Sent, OriginalCurrency = "USD" };
        var payments = new[]
        {
            new Payment { InvoiceId = "INV-1", Amount = 99.99m, OriginalCurrency = "USD" }
        };

        InvoiceTotalsService.RecalculateFromPayments(invoice, payments);
        InvoiceTotalsService.RecalculateStatus(invoice);

        Assert.False(invoice.IsPaidInFull);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
    }

    [Fact]
    public void ShortByMoreThanACent_IsStillPartial()
    {
        var invoice = new Invoice { Id = "INV-1", Total = 100m, Status = InvoiceStatus.Sent, OriginalCurrency = "USD" };
        var payments = new[]
        {
            new Payment { InvoiceId = "INV-1", Amount = 99.97m, OriginalCurrency = "USD" }
        };

        InvoiceTotalsService.RecalculateFromPayments(invoice, payments);
        InvoiceTotalsService.RecalculateStatus(invoice);

        Assert.False(invoice.IsPaidInFull);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
    }

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
        // Customer paid $103 ($100 invoice + $3 processing fee they absorbed). A full refund returns $100 (the invoice value, not the fee).
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
        // Customer paid $100, was refunded $100, then paid $100 again. AmountPaid=$200, AmountRefunded=$100. Net paid is $100, a full invoice value of fresh money, not fee residue.
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
