using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Assets;
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
        // Full refund of a tax-bearing invoice should net to $0, not negative tax. $86.91 pre-tax revenue − $86.91 pre-tax refund = $0.
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

    [Theory]
    [InlineData("United States", null, 70)]
    [InlineData("Canada", "ON", 75)]
    [InlineData("Canada", "BC", 71.25)]
    public void CalculateNetProfitUSD_CountsPurchaseTaxOnlyWhereItIsNotClaimedBack(string country, string? province, decimal expected)
    {
        var data = new CompanyData();
        data.Settings.Company.Country = country;
        data.Settings.Company.ProvinceState = province;
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

        // US sales tax is a cost, so 100 - 30. Ontario's HST is claimed back, so 100 - 25. In BC only the 5% GST on the 25 is, so 100 - 28.75.
        Assert.Equal(expected, profit);
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

    [Fact]
    public void CalculateNetProfitUSD_AddsAssetSalePrice_OnTheDayItSold()
    {
        // The $1,000 purchase counts as an expense in full, so the $400 it sold for comes back as profit.
        var data = new CompanyData();
        data.Expenses.Add(new Expense
        {
            Date = new DateTime(2026, 5, 2),
            Total = 1000m,
            OriginalCurrency = "USD"
        });
        data.Assets.Add(new FixedAsset
        {
            PurchaseDate = new DateTime(2026, 5, 2),
            Cost = 1000m,
            Currency = "USD",
            DisposedDate = new DateTime(2026, 5, 20),
            DisposalKind = AssetDisposalKind.Sold,
            SalePrice = 400m
        });
        data.Assets.Add(new FixedAsset
        {
            PurchaseDate = new DateTime(2026, 1, 5),
            Cost = 200m,
            Currency = "USD",
            DisposedDate = new DateTime(2026, 5, 21),
            DisposalKind = AssetDisposalKind.Scrapped,
            SalePrice = 50m
        });

        var byDay = ProfitCalculator.CalculateNetProfitByDayUSD(data, Start, End);

        Assert.Equal(-600m, ProfitCalculator.CalculateNetProfitUSD(data, Start, End));
        Assert.Equal(400m, byDay[new DateTime(2026, 5, 20)]);
        Assert.False(byDay.ContainsKey(new DateTime(2026, 5, 21)));
    }

    [Fact]
    public void CalculateNetProfitUSD_AssetSoldOutsideRange_IsNotCounted()
    {
        var data = new CompanyData();
        data.Assets.Add(new FixedAsset
        {
            PurchaseDate = new DateTime(2026, 1, 5),
            Cost = 1000m,
            Currency = "USD",
            DisposedDate = new DateTime(2026, 6, 3),
            DisposalKind = AssetDisposalKind.Sold,
            SalePrice = 400m
        });

        Assert.Equal(0m, ProfitCalculator.CalculateNetProfitUSD(data, Start, End));
    }
}
