using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.Tests.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Totals shown in a euro company. Every amount converts from USD at its own date, and a total that
/// needs a rate the app does not have yet shows Pending rather than a figure with dollars mixed in.
/// </summary>
public class CurrencyTotalOrPendingTests : ModalViewModelTestBase
{
    private static readonly DateTime First = new(2026, 5, 4);
    private static readonly DateTime Second = new(2026, 5, 11);
    private static readonly DateTime NoRate = new(2026, 5, 18);
    private static readonly DateTime RangeStart = new(2026, 5, 1);
    private static readonly DateTime RangeEnd = new DateTime(2026, 6, 1).AddTicks(-1);

    private static readonly PropertyInfo RatesInstance =
        typeof(ExchangeRateService).GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;

    public CurrencyTotalOrPendingTests()
    {
        Company.Settings.Localization.Currency = "EUR";
        // Records the shared rate service so the base class puts it back after the test.
        UseNoExchangeRates();
    }

    #region TryComputeDisplay and FormatTotalOrPending

    /// <summary>
    /// With every rate known, each amount converts at the rate of its own date, so $100 at 0.90 and
    /// $50 at 0.80 make €130, not $150 converted at either single rate.
    /// </summary>
    [Fact]
    public async Task TryComputeDisplay_EveryRateKnown_ConvertsEachAmountAtItsOwnDate()
    {
        await UseEuroRatesAsync((First, 0.90m), (Second, 0.80m));

        var complete = CurrencyService.TryComputeDisplay(
            convert => convert(100m, First) + convert(50m, Second), out decimal total);

        Assert.True(complete);
        Assert.Equal(130m, total);
    }

    /// <summary>
    /// One amount whose date has no rate makes the whole computation incomplete, so the caller
    /// shows Pending instead of a total that quietly counts that amount in dollars.
    /// </summary>
    [Fact]
    public async Task TryComputeDisplay_OneRateMissing_ReturnsFalse()
    {
        await UseEuroRatesAsync((First, 0.90m));

        var complete = CurrencyService.TryComputeDisplay(
            convert => convert(100m, First) + convert(50m, NoRate), out decimal _);

        Assert.False(complete);
    }

    /// <summary>
    /// A formatted total with every rate known shows the converted figure in the display currency.
    /// </summary>
    [Fact]
    public async Task FormatTotalOrPending_EveryRateKnown_ShowsTheConvertedTotal()
    {
        await UseEuroRatesAsync((First, 0.90m), (Second, 0.80m));

        var shown = CurrencyService.FormatTotalOrPending(convert => convert(100m, First) + convert(50m, Second));

        Assert.Equal(CurrencyService.Format(130m), shown);
    }

    /// <summary>
    /// A formatted total with any amount missing its rate shows Pending, never a partial number.
    /// </summary>
    [Fact]
    public async Task FormatTotalOrPending_OneRateMissing_ShowsPending()
    {
        await UseEuroRatesAsync((First, 0.90m));

        var shown = CurrencyService.FormatTotalOrPending(convert => convert(100m, First) + convert(50m, NoRate));

        Assert.Equal(CurrencyService.PendingMarker, shown);
    }

    #endregion

    #region FormatNetProfitOrPending

    /// <summary>
    /// Net profit is paid revenue before tax, less expenses, less the pre-tax part of refunds, each
    /// converted at its own date: €90 of sales, less €40 of expenses, less a €16 refund, is €34.
    /// </summary>
    [Fact]
    public async Task FormatNetProfitOrPending_EveryRateKnown_ShowsPreTaxProfitConvertedAtEachDate()
    {
        await UseEuroRatesAsync((First, 0.90m), (Second, 0.80m));
        AddSaleExpenseAndRefund(refundDate: Second);

        var shown = CurrencyService.FormatNetProfitOrPending(Company, RangeStart, RangeEnd);

        Assert.Equal(CurrencyService.Format(34m), shown);
    }

    /// <summary>
    /// A refund dated on a day with no rate leaves the profit unknown, so it shows Pending rather
    /// than a profit that has not had the refund taken off in euros.
    /// </summary>
    [Fact]
    public async Task FormatNetProfitOrPending_RefundDatedWithoutARate_ShowsPending()
    {
        await UseEuroRatesAsync((First, 0.90m), (Second, 0.80m));
        AddSaleExpenseAndRefund(refundDate: NoRate);

        var shown = CurrencyService.FormatNetProfitOrPending(Company, RangeStart, RangeEnd);

        Assert.Equal(CurrencyService.PendingMarker, shown);
    }

    /// <summary>
    /// A sale that has not been paid is not part of profit, so a missing rate on its date must not
    /// hold the profit at Pending.
    /// </summary>
    [Fact]
    public async Task FormatNetProfitOrPending_UnpaidSaleDatedWithoutARate_DoesNotHoldTheProfit()
    {
        await UseEuroRatesAsync((First, 0.90m), (Second, 0.80m));
        AddSaleExpenseAndRefund(refundDate: Second);
        Company.Revenues.Add(new Revenue
        {
            Id = "REV-UNPAID", Date = NoRate, OriginalCurrency = "USD", Total = 500m,
            PaymentStatus = ArgoBooks.Core.Enums.RevenuePaymentStatus.Unpaid
        });

        var shown = CurrencyService.FormatNetProfitOrPending(Company, RangeStart, RangeEnd);

        Assert.Equal(CurrencyService.Format(34m), shown);
    }

    #endregion

    #region FormatStockValue

    /// <summary>
    /// Stock on hand is valued now, so its USD value converts at today's rate: $60 at 0.90 is €54.
    /// </summary>
    [Fact]
    public async Task FormatStockValue_TodaysRateKnown_ConvertsTheStockAtTodaysRate()
    {
        await UseEuroRatesAsync((DateTime.Today, 0.90m));
        Company.Inventory.Add(new InventoryItem { Id = "INV-1", InStock = 10, UnitCost = 5m });
        Company.Inventory.Add(new InventoryItem { Id = "INV-2", InStock = 4, UnitCost = 2.5m });

        var shown = CurrencyService.FormatStockValue(Company.Inventory);

        Assert.Equal(CurrencyService.Format(54m), shown);
    }

    /// <summary>
    /// Without today's rate the stock value cannot be shown in euros, so it shows Pending rather
    /// than the dollar figure.
    /// </summary>
    [Fact]
    public async Task FormatStockValue_TodaysRateMissing_ShowsPending()
    {
        await UseEuroRatesAsync((First, 0.90m));
        Company.Inventory.Add(new InventoryItem { Id = "INV-1", InStock = 10, UnitCost = 5m });

        var shown = CurrencyService.FormatStockValue(Company.Inventory);

        Assert.Equal(CurrencyService.PendingMarker, shown);
    }

    /// <summary>
    /// A record holding stock whose cost is still waiting for its rate counts 0 in the sum, so the
    /// value would read low. It shows Pending instead, even with today's rate known.
    /// </summary>
    [Fact]
    public async Task FormatStockValue_RecordHoldingStockWithAPendingCost_ShowsPending()
    {
        await UseEuroRatesAsync((DateTime.Today, 0.90m));
        Company.Inventory.Add(new InventoryItem { Id = "INV-1", InStock = 10, UnitCost = 5m });
        Company.Inventory.Add(new InventoryItem { Id = "INV-2", InStock = 3, UnitCost = 0m, IsPendingConversion = true });

        var shown = CurrencyService.FormatStockValue(Company.Inventory);

        Assert.Equal(CurrencyService.PendingMarker, shown);
    }

    /// <summary>
    /// A pending cost on a record with no stock adds nothing to the value, so it must not hold the
    /// whole value at Pending.
    /// </summary>
    [Fact]
    public async Task FormatStockValue_PendingCostOnARecordWithNoStock_StillShowsTheValue()
    {
        await UseEuroRatesAsync((DateTime.Today, 0.90m));
        Company.Inventory.Add(new InventoryItem { Id = "INV-1", InStock = 10, UnitCost = 5m });
        Company.Inventory.Add(new InventoryItem { Id = "INV-2", InStock = 0, UnitCost = 0m, IsPendingConversion = true });

        var shown = CurrencyService.FormatStockValue(Company.Inventory);

        Assert.Equal(CurrencyService.Format(45m), shown);
    }

    #endregion

    #region Helpers

    // $110 paid with $10 tax on the first date, a $50 expense on the second, and a $20 refund.
    private void AddSaleExpenseAndRefund(DateTime refundDate)
    {
        Company.Revenues.Add(new Revenue { Id = "REV-1", Date = First, OriginalCurrency = "USD", Total = 110m, TaxAmount = 10m });
        Company.Expenses.Add(new Expense { Id = "EXP-1", Date = Second, OriginalCurrency = "USD", Total = 50m });
        Company.Payments.Add(new Payment
        {
            Id = "PAY-REFUND", IsRefund = true, Date = refundDate, OriginalCurrency = "USD", Amount = -20m, AmountUSD = -20m
        });
    }

    // Caches each USD->EUR rate through the real fetch path against a stub, so nothing reaches the network.
    private static async Task UseEuroRatesAsync(params (DateTime Date, decimal UsdToEur)[] rates)
    {
        var handler = new EuroRateHandler();
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(handler));
        foreach (var (date, rate) in rates)
        {
            handler.UsdToEur = rate;
            await service.GetExchangeRateAsync("USD", "EUR", date);
        }
        RatesInstance.SetValue(null, service);
    }

    private sealed class EuroRateHandler : HttpMessageHandler
    {
        public decimal UsdToEur { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var rate = UsdToEur.ToString(CultureInfo.InvariantCulture);
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "EUR": {{rate}} } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MockPlatformService : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetCachePath() => Path.GetTempPath();
        public void EnsureDirectoryExists(string path) { }
        public bool SupportsFileSystem => false;
        public bool SupportsNativeDialogs => false;
        public bool SupportsBiometrics => false;
        public Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("Not supported");
        public Task<bool> AuthenticateWithBiometricAsync(string reason) => Task.FromResult(false);
        public void StorePasswordForBiometric(string fileId, string password) { }
        public string? GetPasswordForBiometric(string fileId) => null;
        public void ClearPasswordForBiometric(string fileId) { }
        public bool SupportsAutoUpdate => false;
        public int MaxRecentCompanies => 10;
        public string NormalizePath(string path) => path;
        public string CombinePaths(params string[] paths) => Path.Combine(paths);
        public string GetMachineId() => "test-machine-id";
        public StringComparer PathComparer => StringComparer.Ordinal;
    }

    #endregion
}
