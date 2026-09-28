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
        _ = PendingConversionService.Instance ?? new PendingConversionService();
    }

    [Fact]
    public async Task SameIdInTwoCompanies_EachConvertsItsOwnAmount()
    {
        var rates = Rates();
        var a = CompanyWithPendingExpense();
        var b = CompanyWithPendingExpense();
        a.PendingConversions.Add(Entry(100m));
        b.PendingConversions.Add(Entry(300m));
        var open = a;
        var service = new PendingConversionService(exchangeRateService: rates) { CurrentCompany = () => open };

        // A is open while offline, then B, then A again. Each open reconciles and then converts.
        service.ReconcileWithCompanyData(a);
        open = b;
        service.ReconcileWithCompanyData(b);
        await service.ProcessPendingConversionsAsync(b);
        open = a;
        service.ReconcileWithCompanyData(a);
        await service.ProcessPendingConversionsAsync(a);

        var rate = await rates.GetExchangeRateAsync("EUR", "USD", Date);
        Assert.Equal(300m * rate, b.Expenses[0].TotalUSD);
        Assert.Equal(100m * rate, a.Expenses[0].TotalUSD);
    }

    [Fact]
    public void OpeningACompany_TakesInOnlyItsOwnEntries()
    {
        var a = CompanyWithPendingExpense();
        var b = CompanyWithPendingExpense();
        b.PendingConversions.Add(Entry(300m));
        var open = a;
        var service = new PendingConversionService { CurrentCompany = () => open };
        a.PendingConversions.Add(Entry(100m));
        service.Mirror(a, [a.PendingConversions[0].Key]);

        open = b;
        service.ReconcileWithCompanyData(b);

        Assert.Equal(300m, Assert.Single(b.PendingConversions).Total);
        Assert.Equal(1, service.PendingCount);
    }

    // The queue used to be kept in a file of its own as well, written on every change, and opening
    // a company preferred that file's entries. An edit made and then thrown away unsaved still had
    // its entry there, so reopening the company converted the saved record with the discarded amounts.
    [Fact]
    public async Task ReopeningACompany_ConvertsTheAmountsItsFileSaved_NotAnUnsavedEdits()
    {
        var rates = Rates();
        var edited = CompanyWithPendingExpense();
        edited.PendingConversions.Add(Entry(100m));
        var open = edited;
        var service = new PendingConversionService(exchangeRateService: rates) { CurrentCompany = () => open };
        service.ReconcileWithCompanyData(edited);

        // Edited offline, then closed without saving and opened again from its file.
        edited.Expenses[0].Total = 300m;
        UsdConversion.Set(edited, UsdConversion.KeyOf(edited.Expenses[0]), UsdConversion.EntryFor(edited.Expenses[0]));
        service.Mirror(edited, [UsdConversion.KeyOf(edited.Expenses[0])]);
        var reopened = CompanyWithPendingExpense();
        reopened.PendingConversions.Add(Entry(100m));
        open = reopened;
        service.ReconcileWithCompanyData(reopened);
        await service.ProcessPendingConversionsAsync(reopened);

        var rate = await rates.GetExchangeRateAsync("EUR", "USD", Date);
        Assert.Equal(100m * rate, reopened.Expenses[0].TotalUSD);
        Assert.Empty(reopened.PendingConversions);
    }

    private static ExchangeRateService Rates() =>
        new(new TestPlatform(), new HttpClient(new EurHandler(0.9m)));

    private static CompanyData CompanyWithPendingExpense()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = Id, OriginalCurrency = "EUR", Date = Date, Total = 100m, IsPendingConversion = true });
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

    private sealed class TestPlatform : IPlatformService
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
