using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Insights;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the InsightsService class. The display-currency tests install a freshly seeded
/// <see cref="ExchangeRateService.Instance"/> (and restore the prior one), sharing the
/// ExchangeRateSingleton collection so they don't race other singleton-mutating tests.
/// </summary>
[Collection("ExchangeRateSingleton")]
public class InsightsServiceTests
{
    private static readonly DateTime Date1 = new(2024, 3, 10);
    private static readonly DateTime Date2 = new(2024, 9, 20);

    #region GenerateInsightsAsync Tests

    [Fact]
    public async Task GenerateInsightsAsync_EmptyCompanyData_ReturnsInsufficientData()
    {
        var service = new InsightsService();
        var companyData = new CompanyData();
        var dateRange = new AnalysisDateRange
        {
            StartDate = DateTime.Now.AddMonths(-12),
            EndDate = DateTime.Now
        };

        var result = await service.GenerateInsightsAsync(companyData, dateRange);

        Assert.NotNull(result);
        Assert.False(result.HasSufficientData);
        Assert.NotNull(result.InsufficientDataMessage);
    }

    #endregion

    #region Forecast period

    // The Insights page passes the period being forecast ("Next Month" on Sep 11 is Oct 1 - Oct 31).
    // Saving it under the period after that made Past Predictions score it against the wrong month.
    [Fact]
    public async Task GenerateForecastAsync_FutureRange_SavesTheForecastUnderThatRange()
    {
        var service = new InsightsService();
        var data = new CompanyData();
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1);
        var end = start.AddMonths(1).AddSeconds(-1);

        await service.GenerateForecastAsync(data, AnalysisDateRange.Custom(start, end));

        var record = Assert.Single(data.ForecastRecords);
        Assert.Equal(start, record.PeriodStartDate);
        Assert.Equal(end, record.PeriodEndDate);
    }

    #endregion

    #region Trends for a future range

    // "Next Month" on Sep 11 analyses this month so far against the same days of last month.
    private static readonly DateTime TrendsToday = new(2026, 9, 11);

    [Fact]
    public async Task AnalyzeTrendsAsync_NextMonth_SteadyDailyRevenue_ReportsNoDecline()
    {
        var data = DailyRevenue(new DateTime(2026, 7, 1), TrendsToday, 100m);

        var trends = await ServiceOn(TrendsToday).AnalyzeTrendsAsync(data, NextMonth(TrendsToday));

        Assert.DoesNotContain(trends, t => t.Title == "Revenue Decline Detected");
    }

    [Fact]
    public async Task AnalyzeTrendsAsync_NextMonth_CountsTodaysEntries()
    {
        var data = DailyRevenue(new DateTime(2026, 7, 1), TrendsToday, 100m);
        data.Revenues.Add(UsdRevenue("Today", TrendsToday.AddHours(15), 2000m));

        var trends = await ServiceOn(TrendsToday).AnalyzeTrendsAsync(data, NextMonth(TrendsToday));

        // Sep 1-11 is 11 x 100 + 2000; Aug 1-11 is 11 x 100.
        var growth = Assert.Single(trends, t => t.Title == "Revenue Growth Detected");
        Assert.Equal(3100m, growth.MetricValue);
        Assert.Equal(181.8m, Math.Round(growth.PercentageChange!.Value, 1));
    }

    private static InsightsService ServiceOn(DateTime today) =>
        new(new LocalMLForecastingService(), new ForecastAccuracyService(), () => today);

    private static AnalysisDateRange NextMonth(DateTime today)
    {
        var start = new DateTime(today.Year, today.Month, 1).AddMonths(1);
        return AnalysisDateRange.Custom(start, start.AddMonths(1).AddSeconds(-1));
    }

    private static CompanyData DailyRevenue(DateTime from, DateTime through, decimal amount) =>
        AddDailyRevenue(new CompanyData(), from, through, amount);

    private static CompanyData AddDailyRevenue(CompanyData data, DateTime from, DateTime through, decimal amount)
    {
        for (var day = from; day <= through; day = day.AddDays(1))
            data.Revenues.Add(UsdRevenue($"R{day:yyyyMMdd}", day.AddHours(12), amount));
        return data;
    }

    #endregion

    #region Trends for a past range

    // Insights has to compare a period with the same one the dashboard does for that preset.

    [Fact]
    public async Task AnalyzeTrendsAsync_LastMonth_ComparesAgainstTheWholeMonthBefore()
    {
        var today = new DateTime(2026, 4, 15);
        var data = DailyRevenue(new DateTime(2026, 1, 29), new DateTime(2026, 1, 31), 100m);
        AddDailyRevenue(data, new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), 50m);
        AddDailyRevenue(data, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), 100m);
        var lastMonth = Preset(DateRangePreset.LastMonth, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31, 23, 59, 59));

        var trends = await ServiceOn(today).AnalyzeTrendsAsync(data, lastMonth);

        // March is 31 x 100; February is 28 x 50. The 31 days before March would reach back to Jan 29.
        var growth = Assert.Single(trends, t => t.Title == "Revenue Growth Detected");
        Assert.Equal(3100m, growth.MetricValue);
        Assert.Equal(121.4m, Math.Round(growth.PercentageChange!.Value, 1));
    }

    [Fact]
    public async Task AnalyzeTrendsAsync_ThisMonth_ComparesAgainstTheSameDaysOfLastMonth()
    {
        var today = new DateTime(2026, 9, 11);
        var data = DailyRevenue(new DateTime(2026, 8, 1), new DateTime(2026, 8, 11), 100m);
        AddDailyRevenue(data, new DateTime(2026, 8, 12), new DateTime(2026, 8, 31), 50m);
        AddDailyRevenue(data, new DateTime(2026, 9, 1), today, 200m);
        var thisMonth = Preset(DateRangePreset.ThisMonth, new DateTime(2026, 9, 1), today.AddDays(1).AddTicks(-1));

        var trends = await ServiceOn(today).AnalyzeTrendsAsync(data, thisMonth);

        // Sep 1-11 is 11 x 200; Aug 1-11 is 11 x 100. The 11 days just before are Aug 21-31 at 50.
        var growth = Assert.Single(trends, t => t.Title == "Revenue Growth Detected");
        Assert.Equal(2200m, growth.MetricValue);
        Assert.Equal(100m, Math.Round(growth.PercentageChange!.Value, 1));
    }

    [Fact]
    public async Task AnalyzeTrendsAsync_Last3Months_ComparesAgainstTheSameNumberOfDaysBefore()
    {
        var today = new DateTime(2026, 9, 11);
        var data = DailyRevenue(new DateTime(2026, 3, 1), new DateTime(2026, 6, 10), 100m);
        AddDailyRevenue(data, new DateTime(2026, 6, 11), today, 200m);
        var last3Months = new AnalysisDateRange
        {
            StartDate = new DateTime(2026, 6, 11),
            EndDate = today.AddDays(1).AddSeconds(-1),
            PresetName = "Last 3 Months"
        };

        var trends = await ServiceOn(today).AnalyzeTrendsAsync(data, last3Months);

        // Jun 11 - Sep 11 is 93 x 200; the 93 days before, Mar 10 - Jun 10, are 93 x 100.
        var growth = Assert.Single(trends, t => t.Title == "Revenue Growth Detected");
        Assert.Equal(18600m, growth.MetricValue);
        Assert.Equal(100m, Math.Round(growth.PercentageChange!.Value, 1));
    }

    private static AnalysisDateRange Preset(DateRangePreset preset, DateTime start, DateTime end) =>
        new() { StartDate = start, EndDate = end, PresetName = preset.GetDisplayName() };

    #endregion

    #region FormatCurrency / display currency

    [Fact]
    public void FormatCurrency_UsdRun_IsCultureIndependent()
    {
        // A fresh service defaults to a USD run, so amounts stay in USD. The format (symbol + N0,
        // InvariantCulture) must not depend on the machine locale.
        var service = new InsightsService();
        var method = FormatCurrencyMethod();

        string Run(string culture)
        {
            string result = null!;
            var thread = new Thread(() =>
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                result = (string)method.Invoke(service, [1234m])!;
            });
            thread.Start();
            thread.Join();
            return result;
        }

        Assert.Equal("$1,234", Run("en-US"));
        Assert.Equal(Run("en-US"), Run("de-DE"));
    }

    [Fact]
    public async Task DisplayCurrency_NonUsdCompanyWithRates_ConvertsAtTransactionDateAndUsesSymbol()
    {
        // A EUR company with the exact-date USD->EUR rate cached for every conversion date resolves to
        // EUR: amounts convert at each transaction's own date (100 USD * 0.90 = 90 EUR on Date1) and
        // format with the euro symbol, not a hardcoded "$". Today is one of the dates reports check,
        // and opening a company always fetches it.
        var service = await SeededServiceAsync(Date1, DateTime.Today);
        var prior = SetInstance(service);
        try
        {
            var data = new CompanyData();
            data.Settings.Localization.Currency = "EUR";
            data.Revenues.Add(UsdRevenue("R1", Date1, 100m));

            var insights = new InsightsService();
            ResolveDisplayCode(insights, data);

            var converted = ToDisplay(insights, 100m, Date1);
            Assert.Equal(90m, converted);

            var formatted = (string)FormatCurrencyMethod().Invoke(insights, [converted])!;
            Assert.Equal("€90", formatted);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public async Task DisplayCurrency_MissingRate_FallsBackToUsd()
    {
        // Same EUR company, but one transaction date (Date2) has no cached exact-date rate (only Date1
        // is seeded). Because a needed date can't be converted, the whole run falls back to USD:
        // amounts stay unconverted and format with "$" (the all-or-nothing rule reports use).
        var service = await SeededServiceAsync(Date1);
        var prior = SetInstance(service);
        try
        {
            var data = new CompanyData();
            data.Settings.Localization.Currency = "EUR";
            data.Revenues.Add(UsdRevenue("R1", Date1, 100m));
            data.Revenues.Add(UsdRevenue("R2", Date2, 200m)); // Date2 rate not cached

            var insights = new InsightsService();
            ResolveDisplayCode(insights, data);

            Assert.Equal(100m, ToDisplay(insights, 100m, Date1));
            Assert.Equal("$100", (string)FormatCurrencyMethod().Invoke(insights, [100m])!);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    #endregion

    #region Helpers

    private static MethodInfo FormatCurrencyMethod() =>
        typeof(InsightsService).GetMethod("FormatCurrency", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static void ResolveDisplayCode(InsightsService service, CompanyData data) =>
        typeof(InsightsService).GetMethod("ResolveDisplayCode", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(service, [data]);

    private static decimal ToDisplay(InsightsService service, decimal amountUSD, DateTime date) =>
        (decimal)typeof(InsightsService).GetMethod("ToDisplay", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(service, [amountUSD, date])!;

    private static Revenue UsdRevenue(string id, DateTime date, decimal total) => new()
    {
        Id = id,
        Date = date,
        Description = id,
        OriginalCurrency = "USD",
        Total = total,
        TotalUSD = total,
        TaxAmount = 0,
        TaxAmountUSD = 0
    };

    /// <summary>
    /// Builds an ExchangeRateService whose cache holds a USD->EUR rate (0.90) for each seeded date,
    /// via the real async fetch path against a stub handler. A date omitted here is never cached,
    /// exercising the missing-rate fallback.
    /// </summary>
    private static async Task<ExchangeRateService> SeededServiceAsync(params DateTime[] seedDates)
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new FixedEurHandler()));
        foreach (var d in seedDates)
            await service.GetExchangeRateAsync("USD", "EUR", d);
        return service;
    }

    private static ExchangeRateService? SetInstance(ExchangeRateService? service)
    {
        var prop = typeof(ExchangeRateService)
            .GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;
        var prior = (ExchangeRateService?)prop.GetValue(null);
        prop.SetValue(null, service);
        return prior;
    }

    #endregion

    #region Stubs

    /// <summary>Returns a fixed USD->EUR rate of 0.90 for any date.</summary>
    private sealed class FixedEurHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string payload = """{ "success": true, "base": "USD", "rates": { "EUR": 0.9 } }""";
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
        public string GetDefaultDocumentsPath() => Path.GetTempPath();
        public string GetLogsPath() => Path.GetTempPath();
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
        public void RegisterFileTypeAssociations(string iconPath) { }
        public StringComparer PathComparer => StringComparer.Ordinal;
    }

    #endregion
}
