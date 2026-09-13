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

    private static CompanyData CompanyWithPendingExpense()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = Id, OriginalCurrency = "EUR", Date = Date, Total = 1m, IsPendingConversion = true });
        return data;
    }

    private static PendingConversion Entry(decimal total) => new()
    {
        TransactionId = Id,
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

    /// <summary>Writes to <paramref name="appData"/> when given one, and nowhere otherwise.</summary>
    private sealed class TestPlatform(string? appData) : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => appData ?? Path.GetTempPath();
        public string GetTempPath() => Path.GetTempPath();
        public string GetDefaultDocumentsPath() => Path.GetTempPath();
        public string GetLogsPath() => Path.GetTempPath();
        public string GetCachePath() => Path.GetTempPath();
        public void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);
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
        public void RegisterFileTypeAssociations(string iconPath) { }
        public StringComparer PathComparer => StringComparer.Ordinal;
    }
}
