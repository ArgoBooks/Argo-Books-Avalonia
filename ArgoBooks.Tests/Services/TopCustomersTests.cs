using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Top customers (docs/Calculations.md §11), the one ranking behind the Analytics chart and the
/// dashboard widget: collected revenue in the range, tax included, less the revenue part of the
/// refunds issued in the range, leaving out anyone at or below 0.
/// </summary>
public class TopCustomersTests
{
    private static readonly DateTime Start = new(2026, 5, 1);
    private static readonly DateTime End = new(2026, 5, 31, 23, 59, 59);

    private static Revenue Sale(string customer, decimal total, int day = 10,
        RevenuePaymentStatus status = RevenuePaymentStatus.Paid) => new()
    {
        CustomerId = customer,
        Date = new DateTime(2026, 5, day),
        Total = total,
        OriginalCurrency = "USD",
        PaymentStatus = status
    };

    private static Payment Refund(string customer, decimal amount, int day = 20, decimal deposit = 0) => new()
    {
        CustomerId = customer,
        Date = new DateTime(2026, 5, day),
        Amount = -amount,
        OriginalCurrency = "USD",
        IsRefund = true,
        DepositAmount = deposit
    };

    private static List<(CustomerRevenue Customer, decimal Amount)> Rank(CompanyData data) =>
        TopCustomers.Rank(data.Revenues, data.Payments, Start, End, (usd, _) => usd);

    [Fact]
    public void RanksByCollectedRevenueLessRefunds()
    {
        var data = new CompanyData();
        data.Revenues.AddRange([Sale("A", 300), Sale("B", 500), Sale("B", 100, status: RevenuePaymentStatus.Unpaid)]);
        data.Payments.Add(Refund("B", 250));

        var ranked = Rank(data);

        Assert.Equal(["A", "B"], ranked.Select(c => c.Customer.CustomerId));
        Assert.Equal([300m, 250m], ranked.Select(c => c.Amount));
    }

    [Fact]
    public void OnlyTheRefundsIssuedInTheRange_AndOnlyTheirRevenuePart_ComeOff()
    {
        var data = new CompanyData();
        data.Revenues.Add(Sale("A", 400));
        data.Payments.AddRange([Refund("A", 100, deposit: 40), new Payment
        {
            CustomerId = "A", Date = new DateTime(2026, 6, 2), Amount = -50, OriginalCurrency = "USD", IsRefund = true
        }]);

        Assert.Equal(340m, Assert.Single(Rank(data)).Amount);
    }

    [Fact]
    public void ACustomerRefundedAllTheyPaid_IsLeftOut()
    {
        var data = new CompanyData();
        data.Revenues.AddRange([Sale("A", 100), Sale("B", 50)]);
        data.Payments.Add(Refund("A", 100));

        Assert.Equal("B", Assert.Single(Rank(data)).Customer.CustomerId);
    }

    [Fact]
    public void TheChart_ShowsTheSameRanking()
    {
        var data = new CompanyData();
        data.Customers.AddRange([new Customer { Id = "A", Name = "Ann" }, new Customer { Id = "B", Name = "Bob" }]);
        data.Revenues.AddRange([Sale("A", 300), Sale("B", 500)]);
        data.Payments.Add(Refund("B", 250));
        var chart = new ReportChartDataService(data, new ReportFilters { StartDate = Start, EndDate = End });

        var points = chart.GetTopCustomersByRevenue();

        Assert.Equal(["Ann", "Bob"], points.Select(p => p.Label));
        Assert.Equal([300d, 250d], points.Select(p => p.Value));
    }
}
