using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for RefundAggregator: date attribution, gross / pre-tax sums,
/// and per-day grouping. These enforce the refund accounting rules from
/// docs/Calculations.md §8.
/// </summary>
public class RefundAggregatorTests
{
    [Fact]
    public void GetRefundedInDateRangeUSD_AttributesByRefundDate()
    {
        // Original payment on May 1, refund on May 20. The refund counts
        // toward the May 15–25 window even though the original payment
        // doesn't (cash-basis dating per §8).
        var payments = new[]
        {
            new Payment { Date = new DateTime(2026, 5, 1), Amount = 100m, OriginalCurrency = "USD" },
            new Payment { Date = new DateTime(2026, 5, 20), Amount = -100m, IsRefund = true, OriginalCurrency = "USD" }
        };

        var refunded = RefundAggregator.GetRefundedInDateRangeUSD(
            payments, new DateTime(2026, 5, 15), new DateTime(2026, 5, 25));

        Assert.Equal(100m, refunded);
    }

    [Fact]
    public void GroupRefundsByDayUSD_AggregatesByRefundDay()
    {
        var payments = new[]
        {
            new Payment { Date = new DateTime(2026, 5, 10), Amount = -30m, IsRefund = true, OriginalCurrency = "USD" },
            new Payment { Date = new DateTime(2026, 5, 10), Amount = -20m, IsRefund = true, OriginalCurrency = "USD" },
            new Payment { Date = new DateTime(2026, 5, 11), Amount = -15m, IsRefund = true, OriginalCurrency = "USD" }
        };

        var byDay = RefundAggregator.GroupRefundsByDayUSD(
            payments, new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));

        Assert.Equal(50m, byDay[new DateTime(2026, 5, 10)]);
        Assert.Equal(15m, byDay[new DateTime(2026, 5, 11)]);
    }

    [Fact]
    public void GetRefundedPreTaxInDateRangeUSD_ScalesByInvoiceRatio()
    {
        // Invoice: $86.91 subtotal + $32.09 tax = $119 total.
        // Full $119 refund → pre-tax portion = $86.91.
        var invoice = new Invoice
        {
            Id = "INV-1",
            Subtotal = 86.91m,
            TaxAmount = 32.09m,
            Total = 119m
        };
        var refund = new Payment
        {
            Date = new DateTime(2026, 5, 11),
            InvoiceId = "INV-1",
            Amount = -119m,
            IsRefund = true,
            OriginalCurrency = "USD"
        };

        var preTax = RefundAggregator.GetRefundedPreTaxInDateRangeUSD(
            new[] { refund },
            new Dictionary<string, Invoice> { ["INV-1"] = invoice },
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));

        // Allow tiny rounding tolerance (decimal arithmetic).
        Assert.True(Math.Abs(preTax - 86.91m) < 0.01m);
    }

    [Fact]
    public void GetRefundedPreTaxInDateRangeUSD_FallsBackWhenInvoiceMissing()
    {
        // Refund with no matching invoice → fall back to full amount.
        var refund = new Payment
        {
            Date = new DateTime(2026, 5, 11),
            InvoiceId = "INV-MISSING",
            Amount = -50m,
            IsRefund = true,
            OriginalCurrency = "USD"
        };

        var preTax = RefundAggregator.GetRefundedPreTaxInDateRangeUSD(
            new[] { refund },
            new Dictionary<string, Invoice>(),
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));

        Assert.Equal(50m, preTax);
    }
}
