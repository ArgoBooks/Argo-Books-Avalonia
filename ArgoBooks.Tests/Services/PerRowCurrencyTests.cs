using System.Net;
using System.Text;
using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A spreadsheet with a per-row currency column converts each row at its
/// transaction-date rate (the same mechanism manual entry uses), instead of assuming every
/// amount is already in the company currency. When no currency column is mapped, behavior is
/// unchanged: OriginalCurrency = company currency and the USD-equivalent = the raw amount.
/// </summary>
public class PerRowCurrencyTests
{
    private static readonly DateTime TxnDate = new(2026, 3, 15);

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static LlmProcessedData Chunk(SpreadsheetSheetType type, params JsonElement[] entities)
    {
        var chunk = new LlmProcessedData { EntityType = type };
        foreach (var e in entities)
            chunk.Entities.Add(e);
        return chunk;
    }

    /// <summary>
    /// Builds an ExchangeRateService whose cache holds a known USD->EUR rate for <see cref="TxnDate"/>,
    /// seeded through the real fetch path with a stub HTTP handler. The inverse (EUR->USD) is stored
    /// automatically, so conversion from EUR to USD is deterministic.
    /// </summary>
    private static async Task<ExchangeRateService> SeededServiceAsync(decimal usdToEur)
    {
        var handler = new StubRatesHandler(usdToEur);
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(handler));
        // Populate the cache for TxnDate via the normal async fetch path.
        await service.GetExchangeRateAsync("USD", "EUR", TxnDate);
        return service;
    }

    [Fact]
    public async Task ExpenseRows_WithCurrencyColumn_ConvertPerRow_CompanyCurrencyUnchanged()
    {
        // USD->EUR = 0.90, so EUR->USD = 1/0.90 ≈ 1.111111.
        var service = await SeededServiceAsync(0.90m);

        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD"; // company currency
        var svc = new SpreadsheetImportService(exchangeRateService: service);

        var usdRow = Json($$"""
            { "id": "EXP-USD", "date": "{{TxnDate:yyyy-MM-dd}}", "total": 100.00, "taxAmount": 0,
              "description": "USD expense", "originalCurrency": "USD" }
            """);
        var eurRow = Json($$"""
            { "id": "EXP-EUR", "date": "{{TxnDate:yyyy-MM-dd}}", "total": 100.00, "taxAmount": 0,
              "description": "EUR expense", "originalCurrency": "EUR" }
            """);

        svc.ImportProcessedEntities(data, [Chunk(SpreadsheetSheetType.Expenses, usdRow, eurRow)], "Expenses");

        var usd = data.Expenses.Single(e => e.Id == "EXP-USD");
        var eur = data.Expenses.Single(e => e.Id == "EXP-EUR");

        // OriginalCurrency is taken from the cell.
        Assert.Equal("USD", usd.OriginalCurrency);
        Assert.Equal("EUR", eur.OriginalCurrency);

        // USD row: USD-equivalent equals the raw total (no conversion).
        Assert.Equal(usd.Total, usd.TotalUSD);

        // EUR row: converted, so TotalUSD differs from the raw total and equals total * (1/0.90).
        // The USD base is stored full-precision (no 2dp round); display rounds. See Calculations.md Rule 3.
        Assert.NotEqual(eur.Total, eur.TotalUSD);
        Assert.Equal(100.00m * (1m / 0.90m), eur.TotalUSD);

        // The company currency setting is untouched.
        Assert.Equal("USD", data.Settings.Localization.Currency);
    }

    [Fact]
    public async Task ExpenseRow_FutureDatedForeign_NoRate_IsPendingConversion()
    {
        var service = await SeededServiceAsync(0.90m); // caches USD->EUR for TxnDate only
        var future = new DateTime(2999, 1, 1);

        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        var svc = new SpreadsheetImportService(exchangeRateService: service);

        var row = Json($$"""
            { "id": "EXP-FUT", "date": "{{future:yyyy-MM-dd}}", "total": 100.00, "taxAmount": 0,
              "description": "Future foreign expense", "originalCurrency": "EUR" }
            """);
        svc.ImportProcessedEntities(data, [Chunk(SpreadsheetSheetType.Expenses, row)], "Expenses");

        var exp = data.Expenses.Single(e => e.Id == "EXP-FUT");
        Assert.True(exp.IsPendingConversion);
        Assert.Equal(0m, exp.TotalUSD);
        Assert.Equal(100.00m, exp.Total); // native amount preserved
        Assert.Contains(data.PendingConversions, p => p.TransactionId == "EXP-FUT"); // enqueued to self-heal
    }

    [Fact]
    public async Task ExpenseRow_PastForeign_ExactRateCached_ConvertsNotPending()
    {
        var service = await SeededServiceAsync(0.90m); // USD->EUR = 0.90 at TxnDate

        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        var svc = new SpreadsheetImportService(exchangeRateService: service);

        var row = Json($$"""
            { "id": "EXP-PAST", "date": "{{TxnDate:yyyy-MM-dd}}", "total": 100.00, "taxAmount": 0,
              "description": "Past foreign expense", "originalCurrency": "EUR" }
            """);
        svc.ImportProcessedEntities(data, [Chunk(SpreadsheetSheetType.Expenses, row)], "Expenses");

        var exp = data.Expenses.Single(e => e.Id == "EXP-PAST");
        Assert.False(exp.IsPendingConversion);
        Assert.Equal(100.00m * (1m / 0.90m), exp.TotalUSD);
    }

    [Fact]
    public async Task ExpenseRow_NoCurrencyColumn_ConvertsFromCompanyCurrencyAtTheRowDate()
    {
        // Amounts with no currency of their own are in the company currency, which here is not
        // USD, so they convert at the row's date like any other foreign amount.
        var service = await SeededServiceAsync(0.90m);
        var data = new CompanyData();
        data.Settings.Localization.Currency = "EUR";
        var svc = new SpreadsheetImportService(exchangeRateService: service);

        var row = Json($$"""
            { "id": "EXP-1", "date": "{{TxnDate:yyyy-MM-dd}}", "total": 250.00, "taxAmount": 10.00,
              "shippingCost": 5.00, "description": "Plain expense" }
            """);

        svc.ImportProcessedEntities(data, [Chunk(SpreadsheetSheetType.Expenses, row)], "Expenses");

        var exp = data.Expenses.Single(e => e.Id == "EXP-1");

        Assert.Equal("EUR", exp.OriginalCurrency);
        Assert.False(exp.IsPendingConversion);
        Assert.Equal(250.00m * (1m / 0.90m), exp.TotalUSD);
        Assert.Equal(10.00m * (1m / 0.90m), exp.TaxAmountUSD);
        Assert.Equal(5.00m * (1m / 0.90m), exp.ShippingCostUSD);
    }

    [Fact]
    public async Task RevenueRow_NoCurrencyColumn_NoRateForTheDate_IsPendingInsteadOfCountedAsUsd()
    {
        var service = await SeededServiceAsync(0.90m); // caches TxnDate only
        var data = new CompanyData();
        data.Settings.Localization.Currency = "EUR";
        var svc = new SpreadsheetImportService(exchangeRateService: service);

        var row = Json("""
            { "id": "REV-1", "date": "2025-02-03", "total": 80.00, "description": "Plain sale" }
            """);

        svc.ImportProcessedEntities(data, [Chunk(SpreadsheetSheetType.Revenue, row)], "Revenue");

        var rev = data.Revenues.Single(r => r.Id == "REV-1");
        Assert.True(rev.IsPendingConversion);
        Assert.Equal(0m, rev.TotalUSD);
        Assert.Contains(data.PendingConversions, p => p.TransactionId == "REV-1" && p.OriginalCurrency == "EUR");
    }

    [Fact]
    public void ExpenseRow_EmptyCurrencyCell_FallsBackToCompanyCurrency()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        var svc = new SpreadsheetImportService();

        // An empty/blank currency cell must be treated as "not mapped" (no conversion).
        var row = Json($$"""
            { "id": "EXP-BLANK", "date": "{{TxnDate:yyyy-MM-dd}}", "total": 75.00, "taxAmount": 0,
              "description": "Blank currency", "originalCurrency": "  " }
            """);

        svc.ImportProcessedEntities(data, [Chunk(SpreadsheetSheetType.Expenses, row)], "Expenses");

        var exp = data.Expenses.Single(e => e.Id == "EXP-BLANK");
        Assert.Equal("USD", exp.OriginalCurrency);
        Assert.Equal(exp.Total, exp.TotalUSD);
    }

    #region Stubs

    private sealed class StubRatesHandler(decimal usdToEur) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "EUR": {{usdToEur}} } }""";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class MockPlatformService : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetTempPath() => Path.GetTempPath();
        public string GetDefaultDocumentsPath() => Path.GetTempPath();
        public string GetLogsPath() => Path.GetTempPath();
        public string GetCachePath() => Path.GetTempPath();
        public void EnsureDirectoryExists(string path) { }
        public bool SupportsFileSystem => false; // skip disk persistence in tests
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
        public void RegisterFileTypeAssociations(string iconPath) { }
        public StringComparer PathComparer => StringComparer.Ordinal;
    }

    #endregion
}
