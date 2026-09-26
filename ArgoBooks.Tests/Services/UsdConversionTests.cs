using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Storing a record's USD amounts, or leaving it pending in the conversion queue, is done one way
/// for every save, import and sync (docs/Calculations.md Rule 3a).
/// </summary>
public class UsdConversionTests
{
    public UsdConversionTests()
    {
        // Queuing mirrors into the shared queue; it must not be one another test converts with.
        _ = PendingConversionService.Instance ?? new PendingConversionService();
    }

    private static Expense Expense(string id = "PUR-1") => new()
    {
        Id = id,
        Date = new DateTime(2026, 3, 10),
        OriginalCurrency = "EUR",
        Quantity = 1,
        UnitPrice = 80m,
        Amount = 80m,
        TaxAmount = 20m,
        ShippingCost = 5m,
        Fee = 2m,
        Discount = 7m,
        Total = 100m
    };

    [Fact]
    public void Apply_WithARate_StoresEveryAmountAtThatRate_AndQueuesNothing()
    {
        var data = new CompanyData();
        var expense = Expense();

        Assert.True(UsdConversion.Apply(data, expense, 1.25m));

        Assert.Equal(125m, expense.TotalUSD);
        Assert.Equal(25m, expense.TaxAmountUSD);
        Assert.Equal(6.25m, expense.ShippingCostUSD);
        Assert.Equal(2.5m, expense.FeeUSD);
        Assert.Equal(8.75m, expense.DiscountUSD);
        Assert.Equal(100m, expense.UnitPriceUSD);
        Assert.False(expense.IsPendingConversion);
        Assert.Empty(data.PendingConversions);
    }

    [Fact]
    public void Apply_WithoutARate_ZeroesTheUsdAmounts_AndQueuesTheNativeOnes()
    {
        var data = new CompanyData();
        var expense = Expense();
        expense.TotalUSD = 100m; // a figure a nearby rate would have given must not survive

        Assert.False(UsdConversion.Apply(data, expense, null));

        Assert.True(expense.IsPendingConversion);
        Assert.Equal(0m, expense.TotalUSD);
        Assert.Equal(0m, expense.UnitPriceUSD);
        var entry = Assert.Single(data.PendingConversions);
        Assert.Equal(new PendingConversionKey("PUR-1", "Expense"), entry.Key);
        Assert.Equal((100m, 20m, 5m, 7m, 2m, 80m), (entry.Total, entry.TaxAmount, entry.ShippingCost, entry.Discount, entry.Fee, entry.UnitPrice));
        Assert.Equal(("EUR", expense.Date), (entry.OriginalCurrency, entry.TransactionDate));
    }

    // One path kept the first entry when a record was queued again, so the queue converted the
    // amounts from its first save rather than the ones it has now.
    [Fact]
    public void QueuingARecordAgain_ReplacesItsEntry_WithTheLatestAmounts()
    {
        var data = new CompanyData();
        var expense = Expense();
        UsdConversion.Apply(data, expense, null);

        expense.Total = 150m;
        UsdConversion.Apply(data, expense, null);

        Assert.Equal(150m, Assert.Single(data.PendingConversions).Total);
    }

    [Fact]
    public void ARecordThatConverts_LeavesTheQueue()
    {
        var data = new CompanyData();
        var expense = Expense();
        UsdConversion.Apply(data, expense, null);

        UsdConversion.Apply(data, expense, 1.25m);

        Assert.Empty(data.PendingConversions);
        Assert.False(expense.IsPendingConversion);
    }

    [Fact]
    public void QueuingARevenue_LeavesAnExpenseWithTheSameIdQueued()
    {
        var data = new CompanyData();
        var expense = Expense("1");
        var revenue = new Revenue { Id = "1", Date = expense.Date, OriginalCurrency = "EUR", Total = 40m };
        UsdConversion.Apply(data, expense, null);

        UsdConversion.Apply(data, revenue, null);
        UsdConversion.Apply(data, revenue, 1.25m);

        Assert.Equal(new PendingConversionKey("1", "Expense"), Assert.Single(data.PendingConversions).Key);
    }

    [Fact]
    public void AnAmountConvertingAtAnotherRecordsRate_WaitsForThatRecordsDate()
    {
        var data = new CompanyData();
        var payment = new Payment { Id = "PAY-1", Date = new DateTime(2026, 4, 2), Amount = 50m, OriginalCurrency = "EUR" };
        var invoiceDate = new DateTime(2026, 3, 1);

        UsdConversion.Apply(data, payment, null, invoiceDate);

        Assert.Equal(invoiceDate, Assert.Single(data.PendingConversions).TransactionDate);
    }

    [Fact]
    public void AnInvoicesRate_IsItsOwnRatio_OnceItHasOne()
    {
        var invoice = new Invoice { OriginalCurrency = "EUR", Total = 200m, TotalUSD = 250m, IssueDate = new DateTime(2026, 3, 1) };

        Assert.Equal(1.25m, UsdConversion.InvoiceRate(invoice));
    }

    [Fact]
    public void CachedRate_ForUsd_IsOne_WithoutAskingTheSource()
    {
        UsdRateSource neverAsked = (_, _) => throw new InvalidOperationException("USD needs no rate.");

        Assert.Equal(1m, UsdConversion.CachedRate("USD", DateTime.Today, neverAsked));
        Assert.Equal(1m, UsdConversion.CachedRate("", DateTime.Today, neverAsked));
    }
}
