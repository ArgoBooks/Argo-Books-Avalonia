using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A return's refund and a loss's value are recorded in the currency of the sale or purchase they
/// came from (docs/Calculations.md §10). The printed Returns and Lost &amp; Damaged tables put the
/// display symbol on that raw figure, so a 100 EUR refund read as "$100.00".
/// </summary>
[Collection("ExchangeRateSingleton")]
public class ReportReturnLossCurrencyTests
{
    private static readonly DateTime RecordDate = new(2024, 6, 1);
    private const decimal UsdToEur = 0.8m;
    private const decimal UsdToCad = 1.4m;

    [Fact]
    public async Task ReturnsTable_RefundOnAForeignSale_IsConvertedToTheDisplayCurrency()
    {
        var prior = SetInstance(await SeededServiceAsync(RecordDate));
        try
        {
            var data = new CompanyData();
            data.Settings.Localization.Currency = "USD";
            data.Revenues.Add(new Revenue { Id = "REV-EUR", Date = RecordDate, OriginalCurrency = "EUR", Total = 200m, TotalUSD = 250m });
            data.Returns.Add(new Return { Id = "RET-1", OriginalTransactionId = "REV-EUR", ReturnDate = RecordDate, RefundAmount = 100m });

            var (cell, _) = PrintTable(data, TransactionType.Returns);

            // 100 EUR at 1.25 USD per EUR.
            Assert.Equal(CurrencyInfo.FormatAmount(125m, "USD"), cell);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public async Task LossesTable_ValueFromAForeignPurchase_IsConvertedToTheDisplayCurrency()
    {
        var prior = SetInstance(await SeededServiceAsync(RecordDate));
        try
        {
            var data = new CompanyData();
            data.Settings.Localization.Currency = "CAD";
            data.Expenses.Add(new Expense { Id = "EXP-EUR", Date = RecordDate, OriginalCurrency = "EUR", Total = 200m, TotalUSD = 250m });
            data.LostDamaged.Add(new LostDamaged { Id = "LOS-1", InventoryItemId = "EXP-EUR", DateDiscovered = RecordDate, ValueLost = 100m });

            var (cell, _) = PrintTable(data, TransactionType.LostDamaged);

            // 100 EUR = 125 USD = 175 CAD.
            Assert.Equal(CurrencyInfo.FormatAmount(175m, "CAD"), cell);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public async Task ReturnsTable_RateMissing_ShowsPendingRatherThanTheUnconvertedAmount()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = new CompanyData();
            data.Settings.Localization.Currency = "USD";
            data.Revenues.Add(new Revenue { Id = "REV-EUR", Date = RecordDate, OriginalCurrency = "EUR", Total = 200m, TotalUSD = 250m });
            data.Returns.Add(new Return { Id = "RET-1", OriginalTransactionId = "REV-EUR", ReturnDate = RecordDate, RefundAmount = 100m });

            var (cell, total) = PrintTable(data, TransactionType.Returns);

            Assert.Equal("Pending", cell);
            Assert.Equal("Pending", total);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    /// <summary>The single row's Total cell and the table's Total footer, as the report prints them.</summary>
    private static (string Cell, string Total) PrintTable(CompanyData data, TransactionType type)
    {
        var table = new TableReportElement { TransactionType = type, MaxRows = 0 };
        var config = new ReportConfiguration
        {
            Filters = new ReportFilters { StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31) }
        };
        config.Elements.Add(table);

        using var renderer = new ReportRenderer(config, data);
        renderer.ComputeContinuationPlan();
        var plan = renderer.GetContinuationPlan()!;
        var rows = plan.CachedNormalTableData[table.Id];
        var columns = plan.CachedNormalTableColumns[table.Id];

        var totals = (Dictionary<string, string>)typeof(ReportRenderer)
            .GetMethod("CalculateTableTotals", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(renderer, [rows, columns, rows.Count])!;

        return (Assert.Single(rows)[columns.IndexOf("Total")], totals["Total"]);
    }

    /// <summary>A rate service whose cache holds the stub's rates for <paramref name="seedDates"/> only.</summary>
    private static async Task<ExchangeRateService> SeededServiceAsync(params DateTime[] seedDates)
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new FixedRatesHandler()));
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

    private sealed class FixedRatesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "EUR": {{UsdToEur}}, "CAD": {{UsdToCad}} } }""";
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
}
