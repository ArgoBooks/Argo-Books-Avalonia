using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.ViewModels.Dashboard;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The Total Revenue and Total Expenses figures on the dashboard and on the Revenue and Expenses
/// pages, for a euro company. Each row converts at its own date, and a figure that needs a rate the
/// app does not have yet shows Pending.
/// </summary>
public class DashboardRevenueExpenseTotalsTests : ModalViewModelTestBase
{
    private static readonly DateTime First = new(2026, 5, 4);
    private static readonly DateTime Second = new(2026, 5, 11);
    private static readonly DateTime NoRate = new(2026, 5, 18);
    private static readonly DateTime BeforeRange = new(2026, 4, 20);
    private static readonly DateTime RangeStart = new(2026, 5, 1);
    private static readonly DateTime RangeEnd = new DateTime(2026, 6, 1).AddTicks(-1);

    private static readonly PropertyInfo RatesInstance =
        typeof(ExchangeRateService).GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;

    public DashboardRevenueExpenseTotalsTests()
    {
        Company.Settings.Localization.Currency = "EUR";
        // Records the shared rate service so the base class puts it back after the test.
        UseNoExchangeRates();
    }

    #region FormatRevenue

    /// <summary>
    /// Total Revenue is paid revenue with tax included, less refunds, each at its own date: a $110
    /// sale at 0.90 is €99, a €45 sale stays €45, and a $20 refund at 0.80 takes off €16, for €128.
    /// An unpaid sale is left out because its money has not come in.
    /// </summary>
    [Fact]
    public async Task FormatRevenue_EveryRateKnown_ShowsPaidRevenueLessRefundsAtEachDate()
    {
        await UseEuroRatesAsync((First, 0.90m), (Second, 0.80m));
        AddSalesAndRefund(refundDate: Second);
        Company.Revenues.Add(new Revenue
        {
            Id = "REV-UNPAID", Date = First, OriginalCurrency = "USD", Total = 1000m, PaymentStatus = RevenuePaymentStatus.Unpaid
        });

        var shown = DashboardCalculations.FormatRevenue(Company, RangeStart, RangeEnd);

        Assert.Equal(CurrencyService.Format(128m), shown);
    }

    /// <summary>
    /// A refund dated on a day with no rate leaves Total Revenue unknown, so it shows Pending rather
    /// than revenue with the refund taken off in dollars.
    /// </summary>
    [Fact]
    public async Task FormatRevenue_RefundDatedWithoutARate_ShowsPending()
    {
        await UseEuroRatesAsync((First, 0.90m), (Second, 0.80m));
        AddSalesAndRefund(refundDate: NoRate);

        var shown = DashboardCalculations.FormatRevenue(Company, RangeStart, RangeEnd);

        Assert.Equal(CurrencyService.PendingMarker, shown);
    }

    /// <summary>
    /// An unpaid sale is not counted, so a missing rate on its date must not hold Total Revenue at
    /// Pending.
    /// </summary>
    [Fact]
    public async Task FormatRevenue_UnpaidSaleDatedWithoutARate_DoesNotHoldTheTotal()
    {
        await UseEuroRatesAsync((First, 0.90m), (Second, 0.80m));
        AddSalesAndRefund(refundDate: Second);
        Company.Revenues.Add(new Revenue
        {
            Id = "REV-UNPAID", Date = NoRate, OriginalCurrency = "USD", Total = 1000m, PaymentStatus = RevenuePaymentStatus.Unpaid
        });

        var shown = DashboardCalculations.FormatRevenue(Company, RangeStart, RangeEnd);

        Assert.Equal(CurrencyService.Format(128m), shown);
    }

    #endregion

    #region FormatExpenses

    /// <summary>
    /// Total Expenses counts every expense with its tax, each at its own date: $110 at 0.90 is €99
    /// and a €40 expense stays €40, for €139. An expense from before the range is not counted and
    /// its missing rate does not matter.
    /// </summary>
    [Fact]
    public async Task FormatExpenses_EveryRateKnown_ShowsEveryExpenseWithTaxAtItsOwnDate()
    {
        await UseEuroRatesAsync((First, 0.90m), (Second, 0.80m));
        AddExpenses();
        Company.Expenses.Add(new Expense { Id = "EXP-EARLIER", Date = BeforeRange, OriginalCurrency = "USD", Total = 500m });

        var shown = DashboardCalculations.FormatExpenses(Company, RangeStart, RangeEnd);

        Assert.Equal(CurrencyService.Format(139m), shown);
    }

    /// <summary>
    /// One expense dated on a day with no rate makes Total Expenses Pending, rather than a total
    /// that counts that expense in dollars.
    /// </summary>
    [Fact]
    public async Task FormatExpenses_OneExpenseDatedWithoutARate_ShowsPending()
    {
        await UseEuroRatesAsync((First, 0.90m), (Second, 0.80m));
        AddExpenses();
        Company.Expenses.Add(new Expense { Id = "EXP-3", Date = NoRate, OriginalCurrency = "USD", Total = 30m });

        var shown = DashboardCalculations.FormatExpenses(Company, RangeStart, RangeEnd);

        Assert.Equal(CurrencyService.PendingMarker, shown);
    }

    /// <summary>
    /// In a dollar company, a euro expense still waiting for its rate has no dollar amount yet. The
    /// total would leave it out and read low, so Total Expenses has to show Pending until it converts.
    /// </summary>
    [Fact]
    public async Task FormatExpenses_UsdCompany_ForeignExpenseStillWaitingForItsRate_ShowsPending()
    {
        Company.Settings.Localization.Currency = "USD";
        await UseEuroRatesAsync();
        Company.Expenses.Add(new Expense { Id = "EXP-1", Date = First, OriginalCurrency = "USD", Total = 100m });
        Company.Expenses.Add(new Expense
        {
            Id = "EXP-2", Date = NoRate, OriginalCurrency = "EUR", Total = 45m, TotalUSD = 0m, IsPendingConversion = true
        });

        var shown = DashboardCalculations.FormatExpenses(Company, RangeStart, RangeEnd);

        Assert.Equal(CurrencyService.PendingMarker, shown);
    }

    #endregion

    #region Helpers

    // A $110 sale with $10 tax, a €45 sale stored as $56.25, and a $20 refund.
    private void AddSalesAndRefund(DateTime refundDate)
    {
        Company.Revenues.Add(new Revenue { Id = "REV-1", Date = First, OriginalCurrency = "USD", Total = 110m, TaxAmount = 10m });
        Company.Revenues.Add(new Revenue { Id = "REV-2", Date = Second, OriginalCurrency = "EUR", Total = 45m, TotalUSD = 56.25m });
        Company.Payments.Add(new Payment
        {
            Id = "PAY-REFUND", IsRefund = true, Date = refundDate, OriginalCurrency = "USD", Amount = -20m, AmountUSD = -20m
        });
    }

    // A $110 expense with $10 tax and a €40 expense stored as $50.
    private void AddExpenses()
    {
        Company.Expenses.Add(new Expense { Id = "EXP-1", Date = First, OriginalCurrency = "USD", Total = 110m, TaxAmount = 10m });
        Company.Expenses.Add(new Expense { Id = "EXP-2", Date = Second, OriginalCurrency = "EUR", Total = 40m, TotalUSD = 50m });
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

    #endregion
}
