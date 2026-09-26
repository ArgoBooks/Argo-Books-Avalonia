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
/// Every company file numbers its records the same way, so two companies each have a
/// PUR-2026-00005. The conversion queue was one list for every company, matched on that id alone:
/// one company's entry replaced the other's, opening a company took in the other's entries, and a
/// conversion wrote one company's amount onto the other's record.
/// </summary>
public class PendingConversionCompanyScopeTests
{
    private const string Id = "PUR-2026-00005";
    private static readonly DateTime Date = new(2026, 3, 2);

    public PendingConversionCompanyScopeTests()
    {
        // The services here know which company is open. Keep them from becoming the shared
        // instance the view model tests queue with, which knows nothing of these companies.
        _ = PendingConversionService.Instance ?? new PendingConversionService(new TestPlatform(null));
    }

    [Fact]
    public async Task SameIdInTwoCompanies_EachConvertsItsOwnAmount()
    {
        var rates = new ExchangeRateService(new TestPlatform(null), new HttpClient(new EurHandler(0.9m)));
        var a = CompanyWithPendingExpense();
        var b = CompanyWithPendingExpense();
        a.PendingConversions.Add(Entry(100m));
        b.PendingConversions.Add(Entry(300m));
        var open = a;
        var service = new PendingConversionService(new TestPlatform(null), exchangeRateService: rates)
        {
            CurrentCompany = () => (open, open == a ? "A.argo" : "B.argo")
        };

        // A is open while offline, then B, then A again. Each open reconciles and then converts.
        await service.ReconcileWithCompanyDataAsync(a);
        open = b;
        await service.ReconcileWithCompanyDataAsync(b);
        await service.ProcessPendingConversionsAsync(b);
        open = a;
        await service.ReconcileWithCompanyDataAsync(a);
        await service.ProcessPendingConversionsAsync(a);

        var rate = await rates.GetExchangeRateAsync("EUR", "USD", Date);
        Assert.Equal(300m * rate, b.Expenses[0].TotalUSD);
        Assert.Equal(100m * rate, a.Expenses[0].TotalUSD);
    }

    [Fact]
    public async Task OpeningACompany_TakesInOnlyItsOwnEntries()
    {
        var a = CompanyWithPendingExpense();
        var b = CompanyWithPendingExpense();
        b.PendingConversions.Add(Entry(300m));
        var open = a;
        var service = new PendingConversionService(new TestPlatform(null))
        {
            CurrentCompany = () => (open, open == a ? "A.argo" : "B.argo")
        };
        await service.AddPendingConversionAsync(Entry(100m));

        open = b;
        await service.ReconcileWithCompanyDataAsync(b);

        Assert.Equal(300m, Assert.Single(b.PendingConversions).Total);
    }

    [Fact]
    public async Task EachCompanysQueue_OutlivesTheSession_WithoutTheOthers()
    {
        var appData = Directory.CreateTempSubdirectory("argo-queue-").FullName;
        try
        {
            var pathA = Path.Combine(appData, "A.argo");
            var pathB = Path.Combine(appData, "B.argo");
            var a = CompanyWithPendingExpense();
            var b = CompanyWithPendingExpense();
            var open = a;
            var first = new PendingConversionService(new TestPlatform(appData))
            {
                CurrentCompany = () => (open, open == a ? pathA : pathB)
            };
            await first.AddPendingConversionAsync(Entry(100m));
            open = b;
            await first.AddPendingConversionAsync(Entry(300m));

            // The next session opens A again, from a file saved before its entry was queued.
            var reopened = CompanyWithPendingExpense();
            var second = new PendingConversionService(new TestPlatform(appData))
            {
                CurrentCompany = () => (reopened, pathA)
            };
            await second.LoadAsync();
            await second.ReconcileWithCompanyDataAsync(reopened);

            Assert.Equal(100m, Assert.Single(reopened.PendingConversions).Total);
        }
        finally
        {
            Directory.Delete(appData, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAs_TakesTheCompanysQueueToTheNewFile()
    {
        var appData = Directory.CreateTempSubdirectory("argo-queue-").FullName;
        try
        {
            var company = CompanyWithPendingExpense();
            var path = Path.Combine(appData, "A.argo");
            var first = new PendingConversionService(new TestPlatform(appData))
            {
                CurrentCompany = () => (company, path)
            };
            await first.AddPendingConversionAsync(Entry(100m));

            path = Path.Combine(appData, "A copy.argo");
            Assert.True(first.HasPendingConversions);

            var reopened = CompanyWithPendingExpense();
            var second = new PendingConversionService(new TestPlatform(appData))
            {
                CurrentCompany = () => (reopened, path)
            };
            await second.ReconcileWithCompanyDataAsync(reopened);

            Assert.Equal(100m, Assert.Single(reopened.PendingConversions).Total);
        }
        finally
        {
            Directory.Delete(appData, recursive: true);
        }
    }

    // A save waiting behind another took the queue as it was when its turn came. By then another
    // company could be open, so its entries went to that company's file and this one's never did.
    [Fact]
    public async Task SaveAskedForBeforeAnotherCompanyOpens_WritesThatCompanysEntries()
    {
        var appData = Directory.CreateTempSubdirectory("argo-queue-").FullName;
        try
        {
            var (platform, entered, release) = FirstWriteHeld(appData, failOnWrite: 0);
            var pathA = Path.Combine(appData, "A.argo");
            var pathB = Path.Combine(appData, "B.argo");
            var a = CompanyWithPendingExpense();
            var b = CompanyWithPendingExpense();
            var open = a;
            var service = new PendingConversionService(platform)
            {
                CurrentCompany = () => (open, open == a ? pathA : pathB)
            };

            var first = Task.Run(() => service.AddPendingConversionAsync(Entry("A-1", 100m)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var second = service.AddPendingConversionAsync(Entry("A-2", 200m));
            open = b;
            var third = service.AddPendingConversionAsync(Entry("B-1", 300m));
            release.Set();
            await Task.WhenAll(first, second, third);

            Assert.Equal(new[] { 100m, 200m }, (await SavedAsync(appData, pathA)).Select(e => e.Total).Order());
            Assert.Equal(new[] { 300m }, (await SavedAsync(appData, pathB)).Select(e => e.Total));
        }
        finally
        {
            Directory.Delete(appData, recursive: true);
        }
    }

    // A write marked everything asked for so far as saved before it wrote, so when it failed, the
    // saves waiting behind it skipped, and the file kept the queue from before them.
    [Fact]
    public async Task FailedWrite_LeavesTheSavesBehindItToWrite()
    {
        var appData = Directory.CreateTempSubdirectory("argo-queue-").FullName;
        try
        {
            var (platform, entered, release) = FirstWriteHeld(appData, failOnWrite: 2);
            var path = Path.Combine(appData, "A.argo");
            var company = CompanyWithPendingExpense();
            var service = new PendingConversionService(platform)
            {
                CurrentCompany = () => (company, path)
            };

            var first = Task.Run(() => service.AddPendingConversionAsync(Entry("A-1", 100m)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var second = service.AddPendingConversionAsync(Entry("A-2", 200m));
            var third = service.AddPendingConversionAsync(Entry("A-3", 300m));
            release.Set();
            await Task.WhenAll(first, second, third);
            await service.FlushForCloseAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(new[] { 100m, 200m, 300m }, (await SavedAsync(appData, path)).Select(e => e.Total).Order());
        }
        finally
        {
            Directory.Delete(appData, recursive: true);
        }
    }

    // Closing waited on the queue file with no limit, so a stalled disk kept the window from ever
    // closing, and a pass that finished its rate fetch after the wait started a write that raced the
    // exit. The wait is bounded, and nothing is written for the company after it.
    [Fact]
    public async Task FlushForClose_StopsWaitingOnAStalledWrite_AndStartsNoWriteAfterIt()
    {
        var appData = Directory.CreateTempSubdirectory("argo-queue-").FullName;
        try
        {
            var (platform, entered, release) = FirstWriteHeld(appData, failOnWrite: 0);
            var path = Path.Combine(appData, "A.argo");
            var company = CompanyWithPendingExpense();
            var service = new PendingConversionService(platform)
            {
                CurrentCompany = () => (company, path)
            };

            var first = Task.Run(() => service.AddPendingConversionAsync(Entry("A-1", 100m)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

            Assert.False(await service.FlushForCloseAsync(TimeSpan.FromMilliseconds(50)));
            await service.AddPendingConversionAsync(Entry("A-2", 200m));
            release.Set();
            await first;

            Assert.Equal(new[] { 100m }, (await SavedAsync(appData, path)).Select(e => e.Total));
            Assert.Empty(Directory.GetFiles(appData, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(appData, recursive: true);
        }
    }

    /// <summary>
    /// A platform whose first queue file write waits until released, so later saves queue up behind
    /// it, and whose write number <paramref name="failOnWrite"/> fails.
    /// </summary>
    private static (TestPlatform Platform, ManualResetEventSlim Entered, ManualResetEventSlim Release) FirstWriteHeld(
        string appData, int failOnWrite)
    {
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var writes = 0;
        var platform = new TestPlatform(appData, _ =>
        {
            var write = Interlocked.Increment(ref writes);
            if (write == 1)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
            if (write == failOnWrite)
                throw new IOException("The disk is full.");
        });
        return (platform, entered, release);
    }

    /// <summary>What the company's queue file holds, read the way the next session reads it.</summary>
    private static async Task<List<PendingConversion>> SavedAsync(string appData, string companyPath)
    {
        var reopened = new CompanyData();
        var service = new PendingConversionService(new TestPlatform(appData))
        {
            CurrentCompany = () => (reopened, companyPath)
        };
        await service.ReconcileWithCompanyDataAsync(reopened);
        return reopened.PendingConversions;
    }

    private static CompanyData CompanyWithPendingExpense()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = Id, OriginalCurrency = "EUR", Date = Date, Total = 1m, IsPendingConversion = true });
        return data;
    }

    private static PendingConversion Entry(decimal total) => Entry(Id, total);

    private static PendingConversion Entry(string id, decimal total) => new()
    {
        TransactionId = id,
        TransactionType = "Expense",
        OriginalCurrency = "EUR",
        TransactionDate = Date,
        Total = total
    };

    private sealed class EurHandler(decimal usdToEur) : HttpMessageHandler
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

    /// <summary>
    /// Writes to <paramref name="appData"/> when given one, and nowhere otherwise.
    /// <paramref name="beforeWrite"/> runs as each queue file is about to be written.
    /// </summary>
    private sealed class TestPlatform(string? appData, Action<string>? beforeWrite = null) : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => appData ?? Path.GetTempPath();
        public string GetTempPath() => Path.GetTempPath();
        public string GetCachePath() => Path.GetTempPath();

        public void EnsureDirectoryExists(string path)
        {
            beforeWrite?.Invoke(path);
            Directory.CreateDirectory(path);
        }

        public bool SupportsFileSystem => appData != null;
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
