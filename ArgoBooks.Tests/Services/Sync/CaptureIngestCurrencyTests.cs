using System.Reflection;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Sync;
using Xunit;

namespace ArgoBooks.Tests.Services.Sync;

/// <summary>
/// A phone capture in a non-USD company needs its USD base like any other row, or it counts as
/// $0 in every total for good (Calculations.md Rule 3a). With no rate for its date it must be
/// marked pending and queued, so it converts once the rate exists.
/// </summary>
[Collection("ExchangeRateSingleton")]
public class CaptureIngestCurrencyTests
{
    private static readonly PropertyInfo RatesInstance =
        typeof(ExchangeRateService).GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;

    private static CompanyData EuroCompany()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "EUR";
        return data;
    }

    private static CapturedTransaction Capture(CapturedTransactionType type) => new()
    {
        Type = type,
        SupplierOrCustomer = "Office Depot",
        Date = new DateTime(2026, 6, 1),
        Total = 54.00m,
        Tax = 4.00m,
        LineItems = [new CapturedLineItem { Description = "Printer paper", Quantity = 2, UnitPrice = 25.00m, Total = 50.00m }]
    };

    private static void WithNoExchangeRates(Action test)
    {
        var prior = RatesInstance.GetValue(null);
        RatesInstance.SetValue(null, null);
        try { test(); }
        finally { RatesInstance.SetValue(null, prior); }
    }

    [Fact]
    public void NonUsdExpense_WithNoRate_IsPendingAndQueued()
    {
        WithNoExchangeRates(() =>
        {
            var data = EuroCompany();

            var id = CaptureIngestService.Ingest(data, Capture(CapturedTransactionType.Expense));

            var expense = data.Expenses.Single();
            Assert.Equal("EUR", expense.OriginalCurrency);
            Assert.True(expense.IsPendingConversion);
            var entry = Assert.Single(data.PendingConversions);
            Assert.Equal(id, entry.TransactionId);
            Assert.Equal("Expense", entry.TransactionType);
            Assert.Equal("EUR", entry.OriginalCurrency);
            Assert.Equal(54.00m, entry.Total);
        });
    }

    [Fact]
    public void NonUsdRevenue_WithNoRate_IsPendingAndQueued()
    {
        WithNoExchangeRates(() =>
        {
            var data = EuroCompany();

            var id = CaptureIngestService.Ingest(data, Capture(CapturedTransactionType.Revenue));

            Assert.True(data.Revenues.Single().IsPendingConversion);
            var entry = Assert.Single(data.PendingConversions);
            Assert.Equal(id, entry.TransactionId);
            Assert.Equal("Revenue", entry.TransactionType);
        });
    }

    [Fact]
    public void UsdCompany_StoresTheUsdBase()
    {
        WithNoExchangeRates(() =>
        {
            var data = new CompanyData();
            data.Settings.Localization.Currency = "USD";

            CaptureIngestService.Ingest(data, Capture(CapturedTransactionType.Expense));

            var expense = data.Expenses.Single();
            Assert.False(expense.IsPendingConversion);
            Assert.Equal(54.00m, expense.TotalUSD);
            Assert.Equal(4.00m, expense.TaxAmountUSD);
            Assert.Empty(data.PendingConversions);
        });
    }
}
