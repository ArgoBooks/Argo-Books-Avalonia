using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Insights;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Per-product revenue is GROSS (tax-inclusive) everywhere per docs/Calculations.md §13, and the
/// Analytics/Report surfaces (ProductSalesService) follow that. The Insights "Top Performing Product"
/// card must agree; it currently distributes the pre-tax subtotal, so it under-reports by the tax.
/// </summary>
public class InsightsTopProductRevenueTests
{
    [Fact]
    public void TopProductRevenue_IsGrossNotPreTax()
    {
        var data = new CompanyData();
        data.Products.Add(new Product { Id = "P1", Name = "Widget", CostPrice = 20m });
        data.Revenues.Add(new Revenue
        {
            Id = "R1",
            Date = DateTime.Today.AddDays(-10),
            OriginalCurrency = "USD",
            Total = 110m,      // $100 subtotal + $10 tax
            TotalUSD = 110m,
            TaxAmount = 10m,
            TaxAmountUSD = 10m,
            PaymentStatus = RevenuePaymentStatus.Paid,
            LineItems = [new() { ProductId = "P1", Quantity = 1, UnitPrice = 100m }]
        });

        var service = new InsightsService();
        var range = new AnalysisDateRange { StartDate = DateTime.Now.AddMonths(-12), EndDate = DateTime.Now };

        var recs = service.GenerateRecommendations(data, range);
        var top = recs.FirstOrDefault(r => r.Title == "Top Performing Product");

        Assert.NotNull(top);
        // Gross revenue for the single product is the full $110, not the $100 pre-tax subtotal.
        Assert.Equal(110m, top.MetricValue);
    }

    // Three thirds of $10 leave a remainder of 1e-27. Given to the free line, it made that product's
    // margin (revenue - cost) / revenue overflow.
    [Fact]
    public void AFreeLineWithACostPrice_DoesNotBreakTheTopProduct()
    {
        var data = new CompanyData();
        data.Products.Add(new Product { Id = "P1", Name = "Widget", CostPrice = 1m });
        data.Products.Add(new Product { Id = "P2", Name = "Free sample", CostPrice = 2m });
        data.Revenues.Add(Sale(10m,
            new LineItem { ProductId = "P1", Quantity = 1, UnitPrice = 1m },
            new LineItem { ProductId = "P1", Quantity = 1, UnitPrice = 1m },
            new LineItem { ProductId = "P1", Quantity = 1, UnitPrice = 1m },
            new LineItem { ProductId = "P2", Quantity = 1, UnitPrice = 0m }));

        var top = TopProduct(data);

        Assert.NotNull(top);
        Assert.Equal(10m, top.MetricValue);
    }

    [Fact]
    public void AProductWithUnderACentOfRevenue_HasNoMargin()
    {
        var data = new CompanyData();
        // A cent shared by two products leaves each half a cent.
        data.Products.Add(new Product { Id = "P1", Name = "Widget", CostPrice = 5m });
        data.Products.Add(new Product { Id = "P2", Name = "Gadget", CostPrice = 5m });
        data.Revenues.Add(Sale(0.01m,
            new LineItem { ProductId = "P1", Quantity = 1, UnitPrice = 1m },
            new LineItem { ProductId = "P2", Quantity = 1, UnitPrice = 1m }));

        Assert.Null(TopProduct(data));
    }

    private static Revenue Sale(decimal total, params LineItem[] lines) => new()
    {
        Id = "R1",
        Date = DateTime.Today.AddDays(-10),
        OriginalCurrency = "USD",
        Total = total,
        TotalUSD = total,
        PaymentStatus = RevenuePaymentStatus.Paid,
        LineItems = [.. lines]
    };

    private static InsightItem? TopProduct(CompanyData data) =>
        new InsightsService()
            .GenerateRecommendations(data, new AnalysisDateRange { StartDate = DateTime.Now.AddMonths(-12), EndDate = DateTime.Now })
            .FirstOrDefault(r => r.Title == "Top Performing Product");
}
