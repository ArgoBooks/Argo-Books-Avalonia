using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A refund reverses revenue AND the tax collected on it, so its profit impact is only the pre-tax
/// portion, scaled by the invoice's (Total - Tax)/Total (docs/Calculations.md §8). This pins the
/// worked example from the doc and the profit-reduction it drives.
/// </summary>
public class RefundPreTaxMathTests
{
    private static readonly DateTime Start = new(2026, 1, 1);
    private static readonly DateTime End = new(2026, 12, 31);

    [Fact]
    public void FullRefund_ReducesByPreTaxPortionOnly()
    {
        // Doc §8: $86.91 subtotal + $32.09 tax = $119 total. A full $119 refund's pre-tax portion is
        // $86.91 (the $32.09 tax was never ours to keep).
        var invoice = new Invoice { Id = "INV-1", OriginalCurrency = "USD", Subtotal = 86.91m, TaxAmount = 32.09m, Total = 119m };
        var refund = new Payment
        {
            InvoiceId = "INV-1", OriginalCurrency = "USD", IsRefund = true,
            Amount = -119m, AmountUSD = -119m, Date = new DateTime(2026, 3, 1)
        };
        var byId = new Dictionary<string, Invoice> { ["INV-1"] = invoice };

        var preTax = RefundAggregator.GetRefundedPreTaxInDateRangeUSD([refund], byId, Start, End);
        var gross = RefundAggregator.GetRefundedInDateRangeUSD([refund], Start, End);

        Assert.Equal(86.91m, Math.Round(preTax, 2));
        Assert.Equal(119m, gross); // gross revenue subtraction is the full amount
    }

    [Fact]
    public void RefundWithoutInvoiceLink_FallsBackToFullAmount()
    {
        var refund = new Payment
        {
            OriginalCurrency = "USD", IsRefund = true,
            Amount = -50m, AmountUSD = -50m, Date = new DateTime(2026, 3, 1)
        };

        var preTax = RefundAggregator.GetRefundedPreTaxInDateRangeUSD(
            [refund], new Dictionary<string, Invoice>(), Start, End);

        Assert.Equal(50m, preTax);
    }

    [Fact]
    public void NetProfit_SubtractsRefundPreTaxPortion()
    {
        var data = new CompanyData();
        // Paid revenue: $100 gross, no tax -> $100 pre-tax.
        data.Revenues.Add(new Revenue
        {
            Id = "R1", Date = new DateTime(2026, 2, 1), OriginalCurrency = "USD",
            Total = 100m, TotalUSD = 100m, PaymentStatus = RevenuePaymentStatus.Paid
        });
        // A refund on an invoice with $86.91 pre-tax of a $119 total.
        data.Invoices.Add(new Invoice { Id = "INV-1", OriginalCurrency = "USD", Subtotal = 86.91m, TaxAmount = 32.09m, Total = 119m });
        data.Payments.Add(new Payment
        {
            InvoiceId = "INV-1", OriginalCurrency = "USD", IsRefund = true,
            Amount = -119m, AmountUSD = -119m, Date = new DateTime(2026, 3, 1)
        });

        var profit = ProfitCalculator.CalculateNetProfitUSD(data, Start, End);

        // 100 pre-tax revenue - 86.91 pre-tax refund = 13.09.
        Assert.Equal(13.09m, Math.Round(profit, 2));
    }

    /// <summary>
    /// Lines of 100, a 50% invoice discount and 10% tax: taxable 50, tax 5, total 55. The invoice's
    /// revenue counted 50 before tax. Subtotal is the line sum before the invoice discount, and
    /// before shipping, fees and any deposit, so scaling by Subtotal/Total took 100 off.
    /// </summary>
    private static Invoice DiscountedInvoice() => new()
    {
        Id = "INV-1", OriginalCurrency = "USD",
        Subtotal = 100m, DiscountAmount = 50m, DiscountIsPercent = true, TaxRate = 10m, TaxAmount = 5m, Total = 55m
    };

    private static Payment FullRefundOf(Invoice invoice) => new()
    {
        InvoiceId = invoice.Id, OriginalCurrency = "USD", IsRefund = true,
        Amount = -invoice.Total, AmountUSD = -invoice.Total, Date = new DateTime(2026, 3, 1)
    };

    [Fact]
    public void RefundOnADiscountedInvoice_TakesOffTheTotalLessTax()
    {
        var invoice = DiscountedInvoice();
        var byId = new Dictionary<string, Invoice> { [invoice.Id] = invoice };

        var preTax = RefundAggregator.GetRefundedPreTaxInDateRangeUSD([FullRefundOf(invoice)], byId, Start, End);

        Assert.Equal(50m, preTax);
    }

    [Fact]
    public void FullRefundOfADiscountedInvoice_LeavesNoProfit()
    {
        var data = new CompanyData();
        var invoice = DiscountedInvoice();
        data.Invoices.Add(invoice);
        data.Revenues.Add(new Revenue
        {
            Id = "R1", InvoiceId = invoice.Id, Date = new DateTime(2026, 2, 1), OriginalCurrency = "USD",
            Total = 55m, TotalUSD = 55m, TaxAmount = 5m, TaxAmountUSD = 5m, PaymentStatus = RevenuePaymentStatus.Paid
        });
        data.Payments.Add(FullRefundOf(invoice));

        Assert.Equal(0m, ProfitCalculator.CalculateNetProfitUSD(data, Start, End));
    }
}
