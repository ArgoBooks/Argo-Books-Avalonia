using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// docs/Calculations.md Rule 3a: the accounting report converts every figure at the
/// transaction's OWN date, with a REPORT-WIDE consistency fallback to USD when any needed
/// exact-date rate is missing (so a printed document is never a mix of currencies).
///
/// Setup: the source transactions are recorded in USD (so their Effective*USD == native amount),
/// the company DISPLAY currency is CAD, and the report converts each USD figure to CAD at its own
/// date. With different USD->CAD rates on each date, a per-date total differs from a (wrong)
/// convert-everything-at-the-end-date total, which is exactly what these tests pin down.
///
/// The report reads <see cref="ExchangeRateService.Instance"/>, so each test installs a freshly
/// seeded service as the singleton (and restores the prior one) to keep the cache deterministic.
/// </summary>
[Collection("ExchangeRateSingleton")]
public class AccountingReportCurrencyTests
{
    private static readonly DateTime Date1 = new(2024, 3, 10);
    private static readonly DateTime Date2 = new(2024, 9, 20);
    private static readonly DateTime EndDate = new(2024, 12, 31);
    private const decimal RateDate1 = 1.40m; // USD->CAD on Date1
    private const decimal RateDate2 = 1.30m; // USD->CAD on Date2

    private static ReportFilters Filters() => new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = EndDate
    };

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
    /// Builds an ExchangeRateService whose cache holds the given per-date USD->CAD rates, seeded via
    /// the real async fetch path against a stub handler. Returns the service without touching the
    /// singleton (the caller installs it). When a date is omitted from <paramref name="seedDates"/>,
    /// its rate is never cached, which exercises the missing-rate fallback.
    /// </summary>
    private static async Task<ExchangeRateService> SeededServiceAsync(params DateTime[] seedDates)
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new DateRateHandler()));
        foreach (var d in seedDates)
            await service.GetExchangeRateAsync("USD", "CAD", d);
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

    private static decimal ParseAmount(string formatted) =>
        decimal.Parse(formatted.Replace("$", "").Replace(",", "").Replace("(", "-").Replace(")", ""),
            System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task IncomeStatement_NonUsdCompany_ConvertsEachRevenueAtItsOwnDate()
    {
        // Seed both transaction dates plus the report end date (the end date is always in the gate
        // set for point-in-time valuations, so its rate must be cached for CAD display).
        var service = await SeededServiceAsync(Date1, Date2, EndDate);
        var prior = SetInstance(service);
        try
        {
            var data = new CompanyData();
            data.Settings.Localization.Currency = "CAD";
            data.Revenues.Add(UsdRevenue("R1", Date1, 100m)); // 100 USD * 1.40 = 140 CAD
            data.Revenues.Add(UsdRevenue("R2", Date2, 200m)); // 200 USD * 1.30 = 260 CAD

            var report = new AccountingReportDataService(data, Filters())
                .GetReportData(AccountingReportType.IncomeStatement);

            // The report is rendered in CAD (every needed exact-date rate is cached).
            Assert.Equal("Amounts in CAD", report.Subtitle);

            // Per-date total = 140 + 260 = 400 CAD. Converting BOTH at the end date (Date2's 1.30)
            // would give 100*1.30 + 200*1.30 = 390 CAD, so this asserts the Rule 3a per-date behavior.
            var totalRow = report.Rows.Find(r => r.Label == "Total Revenue");
            Assert.NotNull(totalRow);
            Assert.Equal(400m, ParseAmount(totalRow.Values[0]));

            // The total equals the sum of each transaction converted at its OWN date.
            var perDateSum = Math.Round(100m * RateDate1, 2) + Math.Round(200m * RateDate2, 2);
            Assert.Equal(perDateSum, ParseAmount(totalRow.Values[0]));
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public async Task IncomeStatement_MissingExactDateRate_FallsBackToUsdForWholeReport()
    {
        // Seed only Date1; Date2's rate is never cached, so the report must fall back to USD.
        var service = await SeededServiceAsync(Date1);
        var prior = SetInstance(service);
        try
        {
            var data = new CompanyData();
            data.Settings.Localization.Currency = "CAD";
            data.Revenues.Add(UsdRevenue("R1", Date1, 100m));
            data.Revenues.Add(UsdRevenue("R2", Date2, 200m)); // Date2 has no rate

            var report = new AccountingReportDataService(data, Filters())
                .GetReportData(AccountingReportType.IncomeStatement);

            // Whole report falls back to USD so it is never a mix of currencies.
            Assert.Equal("Amounts in USD", report.Subtitle);

            // Totals are the raw USD figures (100 + 200 = 300), not converted to CAD.
            var totalRow = report.Rows.Find(r => r.Label == "Total Revenue");
            Assert.NotNull(totalRow);
            Assert.Equal(300m, ParseAmount(totalRow.Values[0]));
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public void IncomeStatement_UsdCompany_IsUnchanged_Identity()
    {
        // USD company: DisplayCode = USD, ToDisplay is identity, so no exchange service is needed and
        // every number is the raw USD figure exactly as before.
        var prior = SetInstance(null);
        try
        {
            var data = new CompanyData();
            data.Settings.Localization.Currency = "USD";
            data.Revenues.Add(UsdRevenue("R1", Date1, 100m));
            data.Revenues.Add(UsdRevenue("R2", Date2, 200m));

            var report = new AccountingReportDataService(data, Filters())
                .GetReportData(AccountingReportType.IncomeStatement);

            Assert.Equal("Amounts in USD", report.Subtitle);
            var totalRow = report.Rows.Find(r => r.Label == "Total Revenue");
            Assert.NotNull(totalRow);
            Assert.Equal(300m, ParseAmount(totalRow.Values[0]));
        }
        finally
        {
            SetInstance(prior);
        }
    }

    #region Stubs

    /// <summary>Returns a date-specific USD->CAD rate (1.40 on Date1, 1.30 on Date2).</summary>
    private sealed class DateRateHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri?.Query ?? "";
            var rate = query.Contains(Date1.ToString("yyyy-MM-dd")) ? RateDate1
                : query.Contains(Date2.ToString("yyyy-MM-dd")) ? RateDate2
                : 1.0m;

            var payload = $$"""{ "success": true, "base": "USD", "rates": { "CAD": {{rate}} } }""";
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
