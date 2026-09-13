using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A pending row converts ONLY at its exact transaction-date rate. When that date's rate cannot be
/// fetched (but today's can), it must stay pending rather than fall back to today's rate.
/// </summary>
public class PendingConversionServiceTests
{
    [Fact]
    public async Task Process_PastRow_ExactRateUnavailable_StaysPending_NotTodaysRate()
    {
        var today = DateTime.Today;
        var past = today.AddMonths(-3);

        // Handler serves today's "latest" rate but fails any historical (date=...) request.
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new TodayOnlyEurHandler(0.9m)));
        await ex.GetExchangeRateAsync("USD", "EUR", today); // cache today only

        var data = new CompanyData();
        var expense = new Expense
        {
            Id = "E1",
            Total = 100m,
            OriginalCurrency = "EUR",
            Date = past,
            IsPendingConversion = true,
            TotalUSD = 0m
        };
        data.Expenses.Add(expense);

        var svc = new PendingConversionService(new MockPlatform(), exchangeRateService: ex);
        await svc.AddPendingConversionAsync(new PendingConversion
        {
            TransactionId = "E1",
            TransactionType = "Expense",
            Total = 100m,
            OriginalCurrency = "EUR",
            TransactionDate = past
        });

        await svc.ProcessPendingConversionsAsync(data);

        Assert.True(expense.IsPendingConversion); // exact-date rate missing -> not converted at today's rate
        Assert.Equal(0m, expense.TotalUSD);
    }

    [Fact]
    public async Task Process_PendingInvoice_RecomputesBalanceUsdFromPayments_NotImportSnapshot()
    {
        // A foreign-currency invoice imported without a rate stores a snapshot of its balance. If a
        // payment is recorded before the rate heals, ApplyConversion converts the STALE snapshot
        // balance instead of recomputing from the live payments, overstating USD outstanding.
        var date = new DateTime(2024, 6, 1);
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new AlwaysEurHandler(1.0m)));

        var data = new CompanyData();
        var invoice = new Invoice
        {
            Id = "INV-1",
            OriginalCurrency = "EUR",
            Total = 100m,
            AmountPaid = 100m,
            Balance = 0m,            // fully paid in its own currency
            IsPendingConversion = true,
            TotalUSD = 0m,
            BalanceUSD = 0m
        };
        data.Invoices.Add(invoice);
        // A USD payment that fully covers the invoice in USD terms.
        data.Payments.Add(new Payment { Id = "P1", InvoiceId = "INV-1", Amount = 100m, OriginalCurrency = "USD" });

        var svc = new PendingConversionService(new MockPlatform(), exchangeRateService: ex);
        await svc.AddPendingConversionAsync(new PendingConversion
        {
            TransactionId = "INV-1",
            TransactionType = "Invoice",
            Total = 100m,
            Balance = 100m,          // snapshot captured at import, BEFORE the payment
            OriginalCurrency = "EUR",
            TransactionDate = date
        });

        await svc.ProcessPendingConversionsAsync(data);

        Assert.False(invoice.IsPendingConversion);
        Assert.Equal(100m, invoice.TotalUSD);
        // Buggy: BalanceUSD = snapshot(100) * rate(1.0) = 100. Correct: fully paid -> 0.
        Assert.Equal(0m, invoice.BalanceUSD);
    }

    /// <summary>
    /// The service keeps its own queue, and processing converts whatever amount that queue holds.
    /// A row corrected in the company file has to replace the service's copy, or the old amount
    /// is what gets converted.
    /// </summary>
    [Fact]
    public async Task MirrorAsync_StaleQueuedAmount_IsReplacedBeforeItConverts()
    {
        var date = DateTime.Today.AddMonths(-2);
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new AlwaysEurHandler(0.9m)));
        var data = new CompanyData();
        var expense = new Expense { Id = "E1", Total = 2200m, OriginalCurrency = "EUR", Date = date, IsPendingConversion = true };
        data.Expenses.Add(expense);

        var svc = new PendingConversionService(new MockPlatform(), exchangeRateService: ex);
        await svc.AddPendingConversionAsync(Row("E1", 2000m, date));
        data.PendingConversions.Add(Row("E1", 2200m, date));

        await svc.MirrorAsync(data, ["E1"]);
        await svc.ProcessPendingConversionsAsync(data);

        var rate = await ex.GetExchangeRateAsync("EUR", "USD", date);
        Assert.Equal(2200m * rate, expense.TotalUSD);
    }

    /// <summary>Processing does not check the row is still wanted, so one left behind overwrites.</summary>
    [Fact]
    public async Task MirrorAsync_RowTheCompanyNoLongerQueues_IsForgotten()
    {
        var date = DateTime.Today.AddMonths(-2);
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new AlwaysEurHandler(0.9m)));
        var data = new CompanyData();
        var expense = new Expense { Id = "E1", Total = 2200m, OriginalCurrency = "EUR", Date = date, TotalUSD = 1980m };
        data.Expenses.Add(expense);

        var svc = new PendingConversionService(new MockPlatform(), exchangeRateService: ex);
        await svc.AddPendingConversionAsync(Row("E1", 2000m, date));

        await svc.MirrorAsync(data, ["E1"]);
        await svc.ProcessPendingConversionsAsync(data);

        Assert.Equal(1980m, expense.TotalUSD);
    }

    private static PendingConversion Row(string id, decimal total, DateTime date) => new()
    {
        TransactionId = id,
        TransactionType = "Expense",
        OriginalCurrency = "EUR",
        TransactionDate = date,
        Total = total
    };

    private sealed class AlwaysEurHandler(decimal usdToEur) : HttpMessageHandler
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

    private sealed class TodayOnlyEurHandler(decimal usdToEur) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Historical requests carry a ?date=... query; fail those, succeed for "latest" (today).
            if (request.RequestUri?.Query.Contains("date=", StringComparison.OrdinalIgnoreCase) == true)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var payload = $$"""{ "success": true, "base": "USD", "rates": { "EUR": {{usdToEur}} } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MockPlatform : IPlatformService
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
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("");
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
