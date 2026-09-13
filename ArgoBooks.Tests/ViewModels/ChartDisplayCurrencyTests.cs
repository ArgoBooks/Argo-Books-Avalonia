using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.ViewModels;
using ArgoBooks.ViewModels.Dashboard;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Charts, dashboard widgets and Analytics stat cards for a euro company, where 1 USD is 0.90 EUR on
/// every seeded date. Counts stay counts, returns and losses convert from the currency of the sale or
/// purchase they came from, and an invoice converts at its issue date.
/// </summary>
public class ChartDisplayCurrencyTests : ModalViewModelTestBase
{
    private static readonly DateTime Today = DateTime.Today;
    private static readonly DateTime MonthStart = new(Today.Year, Today.Month, 1);
    private static readonly DateTime EndOfToday = Today.AddDays(1).AddTicks(-1);

    public ChartDisplayCurrencyTests()
    {
        Company.Settings.Localization.Currency = "EUR";
        UseNoExchangeRates();
        ChartSettingsService.Instance.StartDate = MonthStart;
        ChartSettingsService.Instance.EndDate = EndOfToday;
    }

    [Theory]
    [InlineData(ChartDataType.TotalTransactions)]
    [InlineData(ChartDataType.CustomerGrowth)]
    [InlineData(ChartDataType.ReturnsOverTime)]
    [InlineData(ChartDataType.LossesOverTime)]
    [InlineData(ChartDataType.ExpenseVsRevenueReturns)]
    [InlineData(ChartDataType.ExpenseVsRevenueLosses)]
    public async Task CountWidgets_ShowCountsNotConvertedAmounts(ChartDataType chartType)
    {
        await SeedEuroRatesAsync(Today, MonthStart);
        AddOneOfEach();

        Assert.Equal(1d, Ys(LoadWidget(chartType).Series).Max());
    }

    [Fact]
    public async Task AnalyticsCountCharts_ShowCountsNotConvertedAmounts()
    {
        await SeedEuroRatesAsync(Today, MonthStart);
        AddOneOfEach();
        var loader = new ChartLoaderService();

        Assert.Equal(1d, Ys(loader.LoadTotalTransactionsChart(Company, MonthStart, EndOfToday).Series).Max());
        Assert.Equal(1d, Ys(loader.LoadExpenseVsRevenueReturnsChart(Company, MonthStart, EndOfToday).Series).Max());
        Assert.Equal(1d, Ys(loader.LoadExpenseVsRevenueLossesChart(Company, MonthStart, EndOfToday).Series).Max());
    }

    // €50 refunded on a euro sale stays €50; $100 refunded on a dollar sale is €90. Losses likewise.
    [Fact]
    public async Task ReturnAndLossImpactCharts_ConvertFromTheirSaleOrPurchaseCurrency()
    {
        await SeedEuroRatesAsync(Today, MonthStart);
        AddForeignReturnsAndLosses();
        var loader = new ChartLoaderService();

        Assert.Equal(140d, Ys(loader.LoadReturnFinancialImpactChart(Company, MonthStart, EndOfToday).Series).Sum());
        Assert.Equal(110d, Ys(loader.LoadLossFinancialImpactChart(Company, MonthStart, EndOfToday).Series).Sum());
        Assert.Equal(140d, Ys(LoadWidget(ChartDataType.ReturnFinancialImpact).Series).Sum());
        Assert.Equal(110d, Ys(LoadWidget(ChartDataType.LossFinancialImpact).Series).Sum());
    }

    [Fact]
    public async Task ReturnAndLossCards_ConvertFromTheirSaleOrPurchaseCurrency()
    {
        await SeedEuroRatesAsync(Today, MonthStart);
        AddForeignReturnsAndLosses();

        var vm = LoadAnalyticsStatistics("LoadReturnsStatistics", "LoadLossesStatistics");

        Assert.Equal(CurrencyService.Format(140m), vm.ReturnsFinancialImpact);
        Assert.Equal(CurrencyService.Format(110m), vm.LossesFinancialImpact);
    }

    // $100 this period less a $40 refund, against $100 last period, is a 40% fall, as the Revenue card counts it.
    // This Month compares with the same days of last month, which always include its first day.
    [Fact]
    public void RevenueGrowth_SubtractsRefundsAsTheRevenueCardDoes()
    {
        Company.Revenues.Add(new Revenue { Id = "REV-NOW", Date = Today, OriginalCurrency = "USD", Total = 100m });
        Company.Revenues.Add(new Revenue { Id = "REV-BEFORE", Date = MonthStart.AddMonths(-1).AddHours(12), OriginalCurrency = "USD", Total = 100m });
        Company.Payments.Add(new Payment
        {
            Id = "PAY-REFUND", IsRefund = true, Date = Today, OriginalCurrency = "USD", Amount = -40m, AmountUSD = -40m
        });

        var vm = LoadAnalyticsStatistics("LoadPerformanceStatistics");

        Assert.NotNull(vm.RevenueGrowthChangeValue);
        Assert.Equal(-40d, vm.RevenueGrowthChangeValue.Value, 6);
    }

    // $10 shipping on a sale and $20 on an expense average to $15, which is €13.50.
    [Fact]
    public async Task AvgShippingCost_AveragesSaleAndExpenseShippingInTheDisplayCurrency()
    {
        await SeedEuroRatesAsync(Today, MonthStart);
        Company.Revenues.Add(new Revenue { Id = "REV-1", Date = Today, OriginalCurrency = "USD", Total = 110m, ShippingCost = 10m });
        Company.Expenses.Add(new Expense { Id = "EXP-1", Date = Today, OriginalCurrency = "USD", Total = 220m, ShippingCost = 20m });

        var vm = LoadAnalyticsStatistics("LoadPerformanceStatistics");

        Assert.Equal(CurrencyService.Format(13.5m), vm.AvgShippingCost);
    }

    // A due date still to come has no rate; the issue date does.
    [Fact]
    public async Task UpcomingInvoices_ConvertAtTheIssueDate()
    {
        var issued = Today.AddDays(-10);
        await SeedEuroRatesAsync(issued);
        Company.Invoices.Add(new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", CustomerId = "CUS-1", Status = InvoiceStatus.Sent,
            IssueDate = issued, DueDate = Today.AddDays(5), OriginalCurrency = "USD", Total = 100m, Balance = 100m
        });
        var widget = new UpcomingInvoicesWidgetViewModel();
        widget.SetCompanyManager(App.CompanyManager);

        widget.LoadData();

        Assert.Equal(CurrencyService.Format(90m), Assert.Single(widget.Invoices).Amount);
    }

    #region Helpers

    private void AddOneOfEach()
    {
        Company.Revenues.Add(new Revenue { Id = "REV-1", Date = Today, CustomerId = "CUS-1", OriginalCurrency = "USD", Total = 100m });
        Company.Expenses.Add(new Expense { Id = "EXP-1", Date = Today, OriginalCurrency = "USD", Total = 100m });
        Company.Returns.Add(new Return { Id = "RET-1", OriginalTransactionId = "REV-1", ReturnDate = Today, RefundAmount = 100m });
        Company.LostDamaged.Add(new LostDamaged { Id = "LOST-1", InventoryItemId = "EXP-1", DateDiscovered = Today, ValueLost = 100m });
    }

    private void AddForeignReturnsAndLosses()
    {
        Company.Revenues.Add(new Revenue { Id = "REV-EUR", Date = Today, OriginalCurrency = "EUR", Total = 50m, TotalUSD = 55.56m });
        Company.Revenues.Add(new Revenue { Id = "REV-USD", Date = Today, OriginalCurrency = "USD", Total = 100m });
        Company.Expenses.Add(new Expense { Id = "EXP-EUR", Date = Today, OriginalCurrency = "EUR", Total = 20m, TotalUSD = 22.22m });
        Company.Expenses.Add(new Expense { Id = "EXP-USD", Date = Today, OriginalCurrency = "USD", Total = 100m });
        Company.Returns.Add(new Return { Id = "RET-EUR", OriginalTransactionId = "REV-EUR", ReturnDate = Today, RefundAmount = 50m });
        Company.Returns.Add(new Return { Id = "RET-USD", OriginalTransactionId = "REV-USD", ReturnDate = Today, RefundAmount = 100m });
        Company.LostDamaged.Add(new LostDamaged { Id = "LOST-EUR", InventoryItemId = "EXP-EUR", DateDiscovered = Today, ValueLost = 20m });
        Company.LostDamaged.Add(new LostDamaged { Id = "LOST-USD", InventoryItemId = "EXP-USD", DateDiscovered = Today, ValueLost = 100m });
    }

    private static UnifiedChartWidgetViewModel LoadWidget(ChartDataType chartType)
    {
        var widget = new UnifiedChartWidgetViewModel(chartType);
        widget.SetCompanyManager(App.CompanyManager);
        widget.LoadData();
        return widget;
    }

    private AnalyticsPageViewModel LoadAnalyticsStatistics(params string[] loaders)
    {
        var vm = new AnalyticsPageViewModel();
        try
        {
            foreach (var loader in loaders)
                typeof(AnalyticsPageViewModel).GetMethod(loader, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(vm, [Company]);
        }
        finally
        {
            vm.Cleanup();
        }
        return vm;
    }

    private static double[] Ys(IEnumerable<ISeries> series) =>
        series.SelectMany(s => s.Values!.Cast<ObservablePoint>())
            .Select(p => p.Y ?? 0)
            .ToArray();

    // USD->EUR 0.90 on each date, cached through the real fetch path against a stub so nothing reaches
    // the network. The base class puts the shared rate service back afterwards.
    private static async Task SeedEuroRatesAsync(params DateTime[] dates)
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new FixedEurHandler()));
        foreach (var date in dates)
            await service.GetExchangeRateAsync("USD", "EUR", date);
        typeof(ExchangeRateService)
            .GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, service);
    }

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
