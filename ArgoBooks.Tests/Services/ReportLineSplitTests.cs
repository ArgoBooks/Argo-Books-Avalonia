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
/// Every screen that shares a transaction's amount across its lines does it through
/// <see cref="LineAllocation"/> (docs/Calculations.md §13). The tax charts put all of a
/// transaction's tax on its first line, and the Tax Summary and refunds by product each split
/// amounts their own way.
/// </summary>
public class ReportLineSplitTests
{
    private static readonly ReportFilters Year2024 = new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31)
    };

    private static LineItem Line(string productId, decimal unitPrice, decimal taxRate = 0m) =>
        new() { ProductId = productId, Quantity = 1, UnitPrice = unitPrice, TaxRate = taxRate };

    private static CompanyData TwoCategories()
    {
        var data = new CompanyData();
        data.Categories.Add(new Category { Id = "CAT-A", Name = "Apparel" });
        data.Categories.Add(new Category { Id = "CAT-B", Name = "Books" });
        data.Products.Add(new Product { Id = "PRD-A", Name = "Shirt", CategoryId = "CAT-A" });
        data.Products.Add(new Product { Id = "PRD-B", Name = "Novel", CategoryId = "CAT-B" });
        return data;
    }

    private static Revenue TaxedSale(params LineItem[] lines) => new()
    {
        Id = "REV-1", Date = new DateTime(2024, 6, 1), OriginalCurrency = "USD", PaymentStatus = RevenuePaymentStatus.Paid,
        TaxRate = 10m, TaxAmount = 10m, TaxAmountUSD = 10m, Total = 110m, TotalUSD = 110m, LineItems = [.. lines]
    };

    [Fact]
    public void AllocateTax_ByEachLinesOwnTax_WhenTheLinesCarryTax()
    {
        var lines = new List<LineItem> { Line("A", 100m, 0.05m), Line("B", 100m, 0.15m), Line("C", 100m) };

        var result = LineAllocation.AllocateTax(lines, 10m);

        Assert.Equal([2.5m, 7.5m, 0m], result.Shares.Select(s => s.AmountUSD));
    }

    [Fact]
    public void AllocateTax_BySubtotal_WhenNoLineCarriesTax()
    {
        var lines = new List<LineItem> { Line("A", 60m), Line("B", 40m) };

        var result = LineAllocation.AllocateTax(lines, 10m);

        Assert.Equal([6m, 4m], result.Shares.Select(s => s.AmountUSD));
    }

    [Fact]
    public void TaxByCategory_SharesEachTransactionsTaxAcrossItsLines()
    {
        var data = TwoCategories();
        data.Revenues.Add(TaxedSale(Line("PRD-A", 60m), Line("PRD-B", 40m)));

        var points = new ReportChartDataService(data, Year2024).GetTaxByCategory();

        Assert.Equal(6d, points.Single(p => p.Label == "Apparel").Value, 6);
        Assert.Equal(4d, points.Single(p => p.Label == "Books").Value, 6);
    }

    [Fact]
    public void TaxByProduct_SharesEachTransactionsTaxByItsLinesOwnTax()
    {
        var data = TwoCategories();
        data.Revenues.Add(TaxedSale(Line("PRD-A", 100m, 0.05m), Line("PRD-B", 100m, 0.15m)));

        var points = new ReportChartDataService(data, Year2024).GetTaxByProduct();

        Assert.Equal(2.5d, points.Single(p => p.Label == "Shirt").Value, 6);
        Assert.Equal(7.5d, points.Single(p => p.Label == "Novel").Value, 6);
    }

    [Fact]
    public void TaxByCategory_TransactionWithNoLines_GoesUnderOther()
    {
        var data = TwoCategories();
        data.Revenues.Add(TaxedSale());

        var point = Assert.Single(new ReportChartDataService(data, Year2024).GetTaxByCategory());

        Assert.Equal(("Other", 10d), (point.Label, point.Value));
    }

    // An expense whose lines all bought stock has only its shipping and fees left as an expense.
    // Its lines can't take that, so it goes under Uncategorized like any transaction whose lines
    // can't, rather than under the first stock line's category.
    [Fact]
    public void IncomeStatement_ExpenseWhoseLinesAllBoughtStock_PutsTheRestUnderUncategorized()
    {
        var data = TwoCategories();
        data.Expenses.Add(new Expense
        {
            Id = "PUR-1", Date = new DateTime(2024, 6, 1), OriginalCurrency = "USD",
            Total = 120m, TotalUSD = 120m, Amount = 100m, ShippingCost = 20m,
            LineItems = [new LineItem { ProductId = "PRD-A", Quantity = 10, UnitPrice = 10m, IsStockPurchase = true }]
        });

        var result = new AccountingReportDataService(data, Year2024).GetReportData(AccountingReportType.IncomeStatement);

        Assert.Contains(result.Rows, r => r.Label == "Uncategorized" && r.Values[0].Contains("20.00"));
        Assert.DoesNotContain(result.Rows, r => r.Label == "Apparel");
    }

    // The report passed the end date as it was, midnight, so a sale made later on the last day of
    // the range was left out, unlike every other report.
    [Fact]
    public void SalesByProductReport_CountsASaleLaterOnTheLastDay()
    {
        var data = TwoCategories();
        data.Revenues.Add(new Revenue
        {
            Id = "REV-1", Date = new DateTime(2024, 12, 31, 15, 30, 0), OriginalCurrency = "USD",
            Total = 50m, TotalUSD = 50m, LineItems = [Line("PRD-A", 50m)]
        });

        var result = new AccountingReportDataService(data, Year2024).GetReportData(AccountingReportType.ProductSales);

        Assert.Contains(result.Rows, r => r.Label == "Shirt");
    }

    [Fact]
    public void TopRefundedProducts_SharesARefundByLineSubtotal()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1", Total = 110m, TotalUSD = 110m, TaxAmount = 10m,
            LineItems =
            [
                new LineItem { Description = "Shirt", Quantity = 1, UnitPrice = 60m },
                new LineItem { Description = "Novel", Quantity = 1, UnitPrice = 40m, TaxRate = 0.25m }
            ]
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-R", InvoiceId = "INV-1", IsRefund = true, Amount = -110m, AmountUSD = -110m,
            OriginalCurrency = "USD", Date = new DateTime(2024, 6, 2)
        });

        var top = RefundAnalyticsService.TopRefundedProducts(data, new DateTime(2024, 1, 1), 5);

        Assert.Equal(66m, top.Single(t => t.ProductLabel == "Shirt").AmountUSD);
        Assert.Equal(44m, top.Single(t => t.ProductLabel == "Novel").AmountUSD);
    }
}
