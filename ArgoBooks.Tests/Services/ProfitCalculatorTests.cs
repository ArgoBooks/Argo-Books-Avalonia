using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for ProfitCalculator: the cross-cutting profit formula from
/// docs/Calculations.md §2 Rule 1 (pre-tax revenue, gross expenses) and
/// §8 (refund pre-tax portion).
/// </summary>
public class ProfitCalculatorTests
{
    private static readonly DateTime Start = new(2026, 5, 1);
    private static readonly DateTime End = new(2026, 5, 31);

    [Fact]
    public void CalculateNetProfitUSD_RevenueOnly_StripsTax()
    {
        // $119 invoice = $86.91 subtotal + $32.09 tax. Profit excludes tax.
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Date = new DateTime(2026, 5, 11),
            PaymentStatus = RevenuePaymentStatus.Paid,
            Total = 119m,
            TaxAmount = 32.09m,
            OriginalCurrency = "USD"
        });

        var profit = ProfitCalculator.CalculateNetProfitUSD(data, Start, End);

        Assert.Equal(86.91m, profit);
    }

    [Fact]
    public void CalculateNetProfitUSD_UnpaidRevenueIgnored()
    {
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Date = new DateTime(2026, 5, 11),
            PaymentStatus = RevenuePaymentStatus.Pending,
            Total = 119m,
            TaxAmount = 32.09m,
            OriginalCurrency = "USD"
        });

        var profit = ProfitCalculator.CalculateNetProfitUSD(data, Start, End);

        Assert.Equal(0m, profit);
    }

    [Fact]
    public void CalculateNetProfitUSD_FullRefundOfTaxedInvoice_NetsToZero()
    {
        // Full refund of a tax-bearing
        // invoice should net to $0, not negative tax.
        // $86.91 pre-tax revenue − $86.91 pre-tax refund = $0.
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1",
            Subtotal = 86.91m,
            TaxAmount = 32.09m,
            Total = 119m,
            OriginalCurrency = "USD"
        });
        data.Revenues.Add(new Revenue
        {
            Date = new DateTime(2026, 5, 11),
            InvoiceId = "INV-1",
            PaymentStatus = RevenuePaymentStatus.Paid,
            Total = 119m,
            TaxAmount = 32.09m,
            OriginalCurrency = "USD"
        });
        data.Payments.Add(new Payment
        {
            Date = new DateTime(2026, 5, 11),
            InvoiceId = "INV-1",
            Amount = -119m,
            IsRefund = true,
            OriginalCurrency = "USD"
        });

        var profit = ProfitCalculator.CalculateNetProfitUSD(data, Start, End);

        Assert.True(Math.Abs(profit) < 0.01m,
            $"Expected profit ≈ 0 for fully refunded invoice; got {profit}");
    }

    [Fact]
    public void CalculateNetProfitUSD_SubtractsGrossExpenses()
    {
        // Expenses use Total (gross). Tax paid to suppliers is real cash out.
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Date = new DateTime(2026, 5, 11),
            PaymentStatus = RevenuePaymentStatus.Paid,
            Total = 100m,
            TaxAmount = 0m,
            OriginalCurrency = "USD"
        });
        data.Expenses.Add(new Expense
        {
            Date = new DateTime(2026, 5, 12),
            Total = 30m,
            TaxAmount = 5m,
            OriginalCurrency = "USD"
        });

        var profit = ProfitCalculator.CalculateNetProfitUSD(data, Start, End);

        // 100 - 30 = 70 (gross expense, not 100 - 25)
        Assert.Equal(70m, profit);
    }

    [Fact]
    public void CalculateNetProfitUSD_EmptyData_ReturnsZero()
    {
        var data = new CompanyData();
        var profit = ProfitCalculator.CalculateNetProfitUSD(data, Start, End);
        Assert.Equal(0m, profit);
    }

    [Fact]
    public void CalculateNetProfitByDayUSD_SumMatchesTotalProfit()
    {
        // The chart title is computed by summing per-day values, so the
        // sum of per-day must equal the total.
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Date = new DateTime(2026, 5, 11),
            PaymentStatus = RevenuePaymentStatus.Paid,
            Total = 119m,
            TaxAmount = 32.09m,
            OriginalCurrency = "USD"
        });

        var byDay = ProfitCalculator.CalculateNetProfitByDayUSD(data, Start, End);
        var total = ProfitCalculator.CalculateNetProfitUSD(data, Start, End);

        Assert.True(Math.Abs(byDay.Values.Sum() - total) < 0.01m,
            $"Per-day sum {byDay.Values.Sum()} should match total profit {total}");
    }
}
