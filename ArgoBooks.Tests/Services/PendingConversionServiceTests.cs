using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Inventory;
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
    public PendingConversionServiceTests()
    {
        // The queues these tests build must not become the shared one other tests mirror into.
        _ = PendingConversionService.Instance ?? new PendingConversionService();
    }

    /// <summary>
    /// Imported sheets keep their own ids, so a stock record and a revenue can both be "1". The
    /// queue told entries apart by id alone: opening the company kept only one of the two, and the
    /// revenue stayed pending at 0 USD for good.
    /// </summary>
    [Fact]
    public async Task Process_StockRecordAndRevenueSharingAnId_BothConvert()
    {
        var date = DateTime.Today.AddMonths(-2);
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new AlwaysEurHandler(0.8m)));
        var data = new CompanyData();
        var item = new InventoryItem { Id = "1", ProductId = "PRD-1", LocationId = "LOC-1", IsPendingConversion = true };
        var revenue = new Revenue { Id = "1", Total = 100m, OriginalCurrency = "EUR", Date = date, IsPendingConversion = true };
        data.Inventory.Add(item);
        data.Revenues.Add(revenue);
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = "1", TransactionType = PendingConversionType.InventoryItem,
            OriginalCurrency = "EUR", TransactionDate = date, Total = 10m
        });
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = "1", TransactionType = PendingConversionType.Revenue,
            OriginalCurrency = "EUR", TransactionDate = date, Total = 100m
        });

        var svc = new PendingConversionService(exchangeRateService: ex);
        svc.ReconcileWithCompanyData(data);
        Assert.Equal(2, svc.PendingCount);

        await svc.ProcessPendingConversionsAsync(data);

        var rate = await ex.GetExchangeRateAsync("EUR", "USD", date);
        Assert.False(revenue.IsPendingConversion);
        Assert.Equal(100m * rate, revenue.TotalUSD);
        Assert.False(item.IsPendingConversion);
        Assert.Equal(10m * rate, item.UnitCost);
        Assert.Empty(data.PendingConversions);
    }

    /// <summary>
    /// A record saved again, still waiting, while the queue fetched the rate for its earlier entry.
    /// The pass converted the earlier amounts over the record and then removed the record's newer
    /// entry too, so the record kept the stale figure for good.
    /// </summary>
    [Fact]
    public async Task Process_RecordSavedAgainWhileItsRateIsFetched_KeepsTheNewerAmounts()
    {
        var date = DateTime.Today.AddMonths(-2);
        var data = new CompanyData();
        var expense = new Expense { Id = "E1", Total = 2000m, OriginalCurrency = "EUR", Date = date, IsPendingConversion = true };
        data.Expenses.Add(expense);
        data.PendingConversions.Add(Row("E1", 2000m, date));

        PendingConversionService? svc = null;
        var savedAgain = false;
        var handler = new AlwaysEurHandler(0.8m, onRequest: () =>
        {
            if (savedAgain) return;
            savedAgain = true;
            expense.Total = 2200m;
            data.PendingConversions.Clear();
            data.PendingConversions.Add(Row("E1", 2200m, date));
            svc!.Mirror(data, [new PendingConversionKey("E1", "Expense")]);
        });
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(handler));
        svc = new PendingConversionService(exchangeRateService: ex);
        svc.ReconcileWithCompanyData(data);

        await svc.ProcessPendingConversionsAsync(data);

        Assert.True(expense.IsPendingConversion);
        Assert.Equal(0m, expense.TotalUSD);
        Assert.Equal(1, svc.PendingCount);

        await svc.ProcessPendingConversionsAsync(data);

        var rate = await ex.GetExchangeRateAsync("EUR", "USD", date);
        Assert.False(expense.IsPendingConversion);
        Assert.Equal(2200m * rate, expense.TotalUSD);
        Assert.Empty(data.PendingConversions);
    }

    /// <summary>
    /// An undo and a redo while a pass was still fetching rates put the record back waiting, with the
    /// very entry the pass had already converted. The pass then removed that entry as done, and the
    /// record waited for good.
    /// </summary>
    [Fact]
    public async Task Process_EntryPutBackAfterItConverted_StaysQueued()
    {
        var date = DateTime.Today.AddMonths(-2);
        var data = new CompanyData();
        var expense = new Expense { Id = "E1", Total = 2000m, OriginalCurrency = "EUR", Date = date, IsPendingConversion = true };
        data.Expenses.Add(expense);
        var entry = Row("E1", 2000m, date);
        data.PendingConversions.Add(entry);
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = "E2", TransactionType = "Expense", OriginalCurrency = "GBP", TransactionDate = date, Total = 5m
        });

        PendingConversionService? svc = null;
        var requests = 0;
        var handler = new AlwaysEurHandler(0.8m, onRequest: () =>
        {
            // The second request is for the GBP row, after the EUR one has converted.
            if (++requests != 2) return;
            expense.IsPendingConversion = true;
            expense.TotalUSD = 0m;
            svc!.Mirror(data, [entry.Key]);
        });
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(handler));
        svc = new PendingConversionService(exchangeRateService: ex);
        svc.ReconcileWithCompanyData(data);

        await svc.ProcessPendingConversionsAsync(data);

        Assert.True(expense.IsPendingConversion);
        Assert.Contains(entry, data.PendingConversions);

        await svc.ProcessPendingConversionsAsync(data);

        var rate = await ex.GetExchangeRateAsync("EUR", "USD", date);
        Assert.False(expense.IsPendingConversion);
        Assert.Equal(2000m * rate, expense.TotalUSD);
    }

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

        UsdConversion.Apply(data, expense, rate: null);
        var svc = new PendingConversionService(exchangeRateService: ex);
        svc.ReconcileWithCompanyData(data);

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
            IssueDate = date,
            Total = 100m,
            Balance = 100m
        };
        data.Invoices.Add(invoice);
        // Queued with its balance as imported, BEFORE the payment.
        UsdConversion.Apply(data, invoice, rate: null);

        // Then fully paid in its own currency, by a USD payment that covers it in USD terms too.
        invoice.AmountPaid = 100m;
        invoice.Balance = 0m;
        data.Payments.Add(new Payment { Id = "P1", InvoiceId = "INV-1", Amount = 100m, OriginalCurrency = "USD" });

        var svc = new PendingConversionService(exchangeRateService: ex);
        svc.ReconcileWithCompanyData(data);

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
    public async Task Mirror_StaleQueuedAmount_IsReplacedBeforeItConverts()
    {
        var date = DateTime.Today.AddMonths(-2);
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new AlwaysEurHandler(0.9m)));
        var data = new CompanyData();
        var expense = new Expense { Id = "E1", Total = 2200m, OriginalCurrency = "EUR", Date = date, IsPendingConversion = true };
        data.Expenses.Add(expense);

        data.PendingConversions.Add(Row("E1", 2000m, date));
        var svc = new PendingConversionService(exchangeRateService: ex);
        svc.ReconcileWithCompanyData(data);
        data.PendingConversions.Clear();
        data.PendingConversions.Add(Row("E1", 2200m, date));

        svc.Mirror(data, [new PendingConversionKey("E1", "Expense")]);
        await svc.ProcessPendingConversionsAsync(data);

        var rate = await ex.GetExchangeRateAsync("EUR", "USD", date);
        Assert.Equal(2200m * rate, expense.TotalUSD);
    }

    /// <summary>Processing does not check the row is still wanted, so one left behind overwrites.</summary>
    [Fact]
    public async Task Mirror_RowTheCompanyNoLongerQueues_IsForgotten()
    {
        var date = DateTime.Today.AddMonths(-2);
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new AlwaysEurHandler(0.9m)));
        var data = new CompanyData();
        var expense = new Expense { Id = "E1", Total = 2200m, OriginalCurrency = "EUR", Date = date, IsPendingConversion = true };
        data.Expenses.Add(expense);
        data.PendingConversions.Add(Row("E1", 2000m, date));
        var svc = new PendingConversionService(exchangeRateService: ex);
        svc.ReconcileWithCompanyData(data);

        // Converted and taken off the company's queue some other way, such as an edit made online.
        expense.IsPendingConversion = false;
        expense.TotalUSD = 1980m;
        data.PendingConversions.Clear();
        svc.Mirror(data, [new PendingConversionKey("E1", "Expense")]);
        await svc.ProcessPendingConversionsAsync(data);

        Assert.Equal(1980m, expense.TotalUSD);
    }

    // A spreadsheet import changes records and the queue off the UI thread, while the timer's pass
    // changed the same list on the UI thread. The import now suspends the passes: it waits for one
    // under way, which stops before its next rate, and none runs until the import is done.
    [Fact]
    public async Task Suspend_WaitsForThePassUnderWay_AndHoldsOffPassesUntilDisposed()
    {
        var date = DateTime.Today.AddMonths(-2);
        var data = new CompanyData();
        var first = new Expense { Id = "E1", Total = 100m, OriginalCurrency = "EUR", Date = date, IsPendingConversion = true };
        var second = new Expense { Id = "E2", Total = 200m, OriginalCurrency = "EUR", Date = date, IsPendingConversion = true };
        data.Expenses.AddRange([first, second]);
        data.PendingConversions.AddRange([Row("E1", 100m, date), Row("E2", 200m, date)]);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new GatedEurHandler(0.8m, entered, release)));
        var svc = new PendingConversionService(exchangeRateService: ex);
        svc.ReconcileWithCompanyData(data);

        var pass = svc.ProcessPendingConversionsAsync(data);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var suspending = svc.SuspendAsync();
        Assert.False(suspending.IsCompleted);

        release.SetResult();
        using (await suspending.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            Assert.True(pass.IsCompleted);
            Assert.False(first.IsPendingConversion);
            Assert.True(second.IsPendingConversion);

            await svc.ProcessPendingConversionsAsync(data);
            Assert.True(second.IsPendingConversion);
        }

        await svc.ProcessPendingConversionsAsync(data);
        Assert.False(second.IsPendingConversion);
        Assert.Empty(data.PendingConversions);
    }

    private sealed class GatedEurHandler(decimal usdToEur, TaskCompletionSource entered, TaskCompletionSource release) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task;
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "EUR": {{usdToEur}} } }""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }

    private static PendingConversion Row(string id, decimal total, DateTime date) => new()
    {
        TransactionId = id,
        TransactionType = "Expense",
        OriginalCurrency = "EUR",
        TransactionDate = date,
        Total = total
    };

    private sealed class AlwaysEurHandler(decimal usdToEur, Action? onRequest = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest?.Invoke();
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
        public StringComparer PathComparer => StringComparer.Ordinal;
    }
}
