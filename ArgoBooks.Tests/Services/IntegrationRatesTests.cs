using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Storing the USD base for a row an integration built.
///
/// Both integration importers used to set the *USD fields equal to the native amounts
/// whatever the currency, which is right only for a USD row. For anything else it filed
/// the native figure as though it were dollars: a EUR 100 sale counted as $100 in every
/// report, chart and COGS figure that reads the base. That is a plausible wrong number
/// rather than a visible error, which is what makes it worth pinning down here.
///
/// <see cref="IntegrationRates.ApplyUsdAmounts"/> reads
/// <see cref="ExchangeRateService.Instance"/>, so each test installs a freshly seeded
/// service as the singleton and restores the prior one.
/// </summary>
[Collection("ExchangeRateSingleton")]
public class IntegrationRatesTests
{
    private const decimal UsdToEur = 0.80m; // so EUR -> USD is 1.25
    private static readonly DateTime RateDate = new(2024, 5, 15);
    private static readonly DateTime UnpricedDate = new(2024, 6, 20);

    private static async Task<ExchangeRateService> SeededServiceAsync()
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new StubEurHandler(UsdToEur)));
        await service.GetExchangeRateAsync("USD", "EUR", RateDate); // seeds USD->EUR and its inverse
        return service;
    }

    /// <summary>Installs <paramref name="service"/> as the singleton, returning the prior instance.</summary>
    private static ExchangeRateService? SetInstance(ExchangeRateService? service)
    {
        var prop = typeof(ExchangeRateService)
            .GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;
        var prior = (ExchangeRateService?)prop.GetValue(null);
        prop.SetValue(null, service);
        return prior;
    }

    private static Expense Row(DateTime date) => new()
    {
        Id = "PUR-2024-00001",
        Date = date,
        Description = "Hosting",
        Quantity = 1,
        UnitPrice = 80m,
        Amount = 80m,
        TaxAmount = 20m,
        Total = 100m
    };

    [Fact]
    public void UsdRow_MirrorsNativeAmounts_WithoutConsultingTheRateService()
    {
        // No singleton installed on purpose: a USD row must convert even headless.
        var prior = SetInstance(null);
        try
        {
            var data = new CompanyData();
            var expense = Row(UnpricedDate);

            IntegrationRates.ApplyUsdAmounts(expense, "USD", data);

            Assert.Equal(100m, expense.TotalUSD);
            Assert.Equal(20m, expense.TaxAmountUSD);
            Assert.Equal(80m, expense.UnitPriceUSD);
            Assert.False(expense.IsPendingConversion);
            Assert.Empty(data.PendingConversions);
        }
        finally { SetInstance(prior); }
    }

    /// <summary>The bug this exists for: EUR 100 must not be filed as $100.</summary>
    [Fact]
    public async Task NonUsdRow_StoresTheConvertedBase_NotTheNativeFigure()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = new CompanyData();
            var expense = Row(RateDate);

            IntegrationRates.ApplyUsdAmounts(expense, "EUR", data);

            Assert.NotEqual(expense.Total, expense.TotalUSD);
            Assert.Equal(100m * (1m / UsdToEur), expense.TotalUSD);
            Assert.Equal(20m * (1m / UsdToEur), expense.TaxAmountUSD);
            Assert.Equal(80m * (1m / UsdToEur), expense.UnitPriceUSD);
            Assert.False(expense.IsPendingConversion);
            Assert.Empty(data.PendingConversions);
        }
        finally { SetInstance(prior); }
    }

    /// <summary>
    /// The base is stored unrounded. Rounding it to cents makes a native to base to native
    /// round-trip drift, so a chart re-deriving from the base reads a cent low.
    /// </summary>
    [Fact]
    public async Task NonUsdRow_StoresTheBaseAtFullPrecision()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = new CompanyData();
            var expense = Row(RateDate);
            expense.Total = 10m;

            IntegrationRates.ApplyUsdAmounts(expense, "EUR", data);

            Assert.Equal(12.5m, expense.TotalUSD);
            Assert.Equal(10m * (1m / UsdToEur), expense.TotalUSD);
        }
        finally { SetInstance(prior); }
    }

    [Fact]
    public async Task NonUsdRow_WithNoRateForItsDate_DefersInsteadOfGuessing()
    {
        var prior = SetInstance(await SeededServiceAsync()); // seeds RateDate only
        try
        {
            var data = new CompanyData();
            var expense = Row(UnpricedDate);

            IntegrationRates.ApplyUsdAmounts(expense, "EUR", data);

            Assert.True(expense.IsPendingConversion);
            Assert.Equal(0m, expense.TotalUSD);
            Assert.Equal(0m, expense.TaxAmountUSD);
            Assert.Equal(0m, expense.UnitPriceUSD);

            // The native amounts are untouched, so the row still reads correctly in its
            // own currency while the USD base waits.
            Assert.Equal(100m, expense.Total);
        }
        finally { SetInstance(prior); }
    }

    /// <summary>
    /// A deferred row is no use unless something finishes the job, so it must reach the
    /// queue PendingConversionService drains, carrying every amount the heal needs.
    /// </summary>
    [Fact]
    public async Task DeferredRow_JoinsTheConversionQueueWithItsNativeAmounts()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = new CompanyData();
            var expense = Row(UnpricedDate);

            IntegrationRates.ApplyUsdAmounts(expense, "EUR", data);

            var entry = Assert.Single(data.PendingConversions);
            Assert.Equal(expense.Id, entry.TransactionId);
            Assert.Equal("Expense", entry.TransactionType);
            Assert.Equal("EUR", entry.OriginalCurrency);
            Assert.Equal(UnpricedDate, entry.TransactionDate);
            Assert.Equal(100m, entry.Total);
            Assert.Equal(20m, entry.TaxAmount);
            Assert.Equal(80m, entry.UnitPrice);
        }
        finally { SetInstance(prior); }
    }

    [Fact]
    public async Task DeferredRevenue_IsQueuedAsRevenueNotExpense()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = new CompanyData();
            var revenue = new Revenue
            {
                Id = "REV-2024-00001",
                Date = UnpricedDate,
                Description = "Order",
                Total = 50m
            };

            IntegrationRates.ApplyUsdAmounts(revenue, "EUR", data);

            Assert.Equal("Revenue", Assert.Single(data.PendingConversions).TransactionType);
        }
        finally { SetInstance(prior); }
    }

    [Fact]
    public async Task DeferringTheSameRowTwice_DoesNotQueueItTwice()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = new CompanyData();
            var expense = Row(UnpricedDate);

            IntegrationRates.ApplyUsdAmounts(expense, "EUR", data);
            IntegrationRates.ApplyUsdAmounts(expense, "EUR", data);

            Assert.Single(data.PendingConversions);
        }
        finally { SetInstance(prior); }
    }

    private sealed class StubEurHandler(decimal usdToEur) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "EUR": {{usdToEur}} } }""";
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
        public string GetTempPath() => Path.GetTempPath();
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
}
