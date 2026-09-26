using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The one way a transaction's USD amount is split across its lines (docs/Calculations.md §13): by
/// line subtotal, at full precision, the last line taking the remainder so the shares add up exactly.
/// </summary>
public class LineAllocationTests
{
    private static LineItem Line(decimal qty, decimal unitPrice, string? productId = null) =>
        new() { ProductId = productId, Quantity = qty, UnitPrice = unitPrice };

    private static Revenue Sale(decimal total, decimal tax, params LineItem[] lines) => new()
    {
        Date = new DateTime(2026, 5, 11),
        PaymentStatus = RevenuePaymentStatus.Paid,
        Total = total,
        TaxAmount = tax,
        OriginalCurrency = "USD",
        LineItems = [.. lines]
    };

    [Fact]
    public void ThreeEqualLines_AddUpToTheTotalExactly()
    {
        var result = LineAllocation.Allocate(Sale(10m, 0m, Line(1, 5), Line(1, 5), Line(1, 5)), LineAllocationBasis.Gross);

        Assert.True(result.IsSplit);
        Assert.Equal(10m, result.Shares.Sum(s => s.AmountUSD));
        // Not rounded to cents per line: 3.33 × 3 would lose a cent.
        Assert.NotEqual(3.33m, result.Shares[0].AmountUSD);
        Assert.Equal(3.33m, Math.Round(result.Shares[0].AmountUSD, 2));
    }

    [Fact]
    public void Gross_SharesTheTotalWithTax_PreTax_SharesTheTotalWithout()
    {
        var sale = Sale(110m, 10m, Line(1, 60), Line(1, 40));

        var gross = LineAllocation.Allocate(sale, LineAllocationBasis.Gross);
        var preTax = LineAllocation.Allocate(sale, LineAllocationBasis.PreTax);

        Assert.Equal([66m, 44m], gross.Shares.Select(s => s.AmountUSD));
        Assert.Equal([60m, 40m], preTax.Shares.Select(s => s.AmountUSD));
    }

    [Fact]
    public void LinesThatAddUpToZero_AreNotSplit_AndTheWholeAmountIsUnallocated()
    {
        // Both lines discounted to nothing, but shipping was still charged.
        var sale = Sale(15m, 0m,
            new LineItem { Quantity = 1, UnitPrice = 50, Discount = 50 },
            new LineItem { Quantity = 2, UnitPrice = 10, Discount = 30 });

        var result = LineAllocation.Allocate(sale, LineAllocationBasis.Gross);

        Assert.False(result.IsSplit);
        Assert.Equal(15m, result.UnallocatedUSD);
        Assert.All(result.Shares, s => Assert.Equal(0m, s.AmountUSD));
        Assert.Equal(2, result.Shares.Count);
    }

    [Fact]
    public void NoLines_IsNotSplit()
    {
        var result = LineAllocation.Allocate(Sale(25m, 5m), LineAllocationBasis.PreTax);

        Assert.False(result.IsSplit);
        Assert.Empty(result.Shares);
        Assert.Equal(20m, result.UnallocatedUSD);
    }

    [Fact]
    public void AFreeLineAmongPaidOnes_GetsNothing()
    {
        var result = LineAllocation.Allocate([Line(1, 0), Line(1, 30), Line(1, 70)], 200m);

        Assert.Equal([0m, 60m, 140m], result.Shares.Select(s => s.AmountUSD));
    }

    [Fact]
    public void AFreeLastLine_GetsExactlyNothing_AndThePaidLinesTakeTheWholeAmount()
    {
        // Three thirds of 10 leave a remainder of 1e-27, which must not land on the free line.
        var result = LineAllocation.Allocate([Line(1, 1), Line(1, 1), Line(1, 1), Line(1, 0)], 10m);

        Assert.Equal(0m, result.Shares[3].AmountUSD);
        Assert.Equal(10m, result.Shares.Sum(s => s.AmountUSD));
    }

    [Fact]
    public void AWaitingSale_SharesZero()
    {
        var sale = Sale(100m, 0m, Line(1, 60), Line(1, 40));
        sale.OriginalCurrency = "EUR";
        sale.IsPendingConversion = true;

        var result = LineAllocation.Allocate(sale, LineAllocationBasis.Gross);

        Assert.True(result.IsSplit);
        Assert.All(result.Shares, s => Assert.Equal(0m, s.AmountUSD));
    }

    [Fact]
    public void SalesByProduct_AddUpToTheSaleTotal()
    {
        var data = new CompanyData();
        data.Products.Add(new Product { Id = "A", Name = "A" });
        data.Products.Add(new Product { Id = "B", Name = "B" });
        data.Products.Add(new Product { Id = "C", Name = "C" });
        data.Revenues.Add(Sale(100m, 13m, Line(1, 29, "A"), Line(1, 29, "B"), Line(1, 29, "C")));

        var sales = ProductSalesService.GetProductSales(data, new DateTime(2026, 5, 1), new DateTime(2026, 5, 31), cashBasis: true);

        Assert.Equal(100m, sales.Sum(p => p.RevenueUSD));
    }

    [Fact]
    public void RevenueByCategoryChart_PutsAnUnsplittableSaleUnderOther()
    {
        var data = new CompanyData();
        data.Revenues.Add(Sale(15m, 0m, new LineItem { Quantity = 1, UnitPrice = 50, Discount = 50 }));
        var service = new ReportChartDataService(data, new ReportFilters
        {
            StartDate = new DateTime(2026, 5, 1),
            EndDate = new DateTime(2026, 5, 31)
        });

        var point = Assert.Single(service.GetRevenueDistribution());

        Assert.Equal("Other", point.Label);
        Assert.Equal(15d, point.Value);
    }
}
