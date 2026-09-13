using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

// Uses App.CompanyManager, App.PaymentPortalService and the portal API key, all process-wide,
// so run in the serialized modal-VM collection.
[Collection("ModalViewModels")]
public class SettingsCompanySwitchTests : IDisposable
{
    private readonly string _priorPortalKey = DotEnv.Get(PortalSettings.ApiKeyEnvVar);

    // Holds the portal's reply until the test has switched companies.
    private sealed class GatedHandler(string body) : HttpMessageHandler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Release.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    // The modal stays open while its company is closed (auto-lock, or a file opened from Finder)
    // and another one opens. Its fields still hold the first company's values.
    [Fact]
    public async Task Save_AfterAnotherCompanyOpened_LeavesTheNewCompanysSettingsAlone()
    {
        var companyA = new CompanyData();
        companyA.Categories.Add(new Category { Id = "CAT-PUR-001", Name = "Office", Type = CategoryType.Expense });
        companyA.BankCategoryRules.Add(new BankCategoryRule { Id = "R1", Pattern = "staples", CategoryId = "CAT-PUR-001" });
        companyA.Settings.Notifications.LowStockAlert = false;
        companyA.Settings.Localization.DateFormat = "DD/MM/YYYY";
        companyA.Settings.PaymentPortal.AutoSyncIntervalMinutes = 30;
        App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(companyA));

        var vm = new SettingsModalViewModel();
        vm.OpenWithTab(SettingsTab.General);

        var companyB = new CompanyData();
        App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(companyB));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(companyB.BankCategoryRules);
        Assert.True(companyB.Settings.Notifications.LowStockAlert);
        Assert.Equal("MM/DD/YYYY", companyB.Settings.Localization.DateFormat);
        Assert.Equal(5, companyB.Settings.PaymentPortal.AutoSyncIntervalMinutes);
    }

    // A status check asked with company A's key can take up to the 30s client timeout. If the
    // user switches to company B meanwhile, the reply describes A and must not land in B.
    [Fact]
    public async Task PortalStatus_ArrivingAfterACompanySwitch_IsNotWrittenIntoTheNewCompany()
    {
        var companyA = new CompanyData();
        companyA.Settings.PaymentPortal.PersistedApiKey = "test-key-a";
        App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(companyA));
        PortalSettings.ActivateApiKey(companyA.Settings.PaymentPortal);

        var handler = new GatedHandler("""
            {
              "success": true,
              "connected": true,
              "connectedProviders": { "stripeConnected": true, "stripeEmail": "a@stripe.test" },
              "preferences": { "sendPaymentReminders": true, "emailOwnerOnPayment": true },
              "company": { "name": "Company A", "owner_email": "owner-a@example.com" }
            }
            """);
        App.SetPaymentPortalServiceForTesting(new PaymentPortalService(new HttpClient(handler)));

        var vm = new SettingsModalViewModel();
        var refresh = vm.RefreshProviderStatusAsync();

        var companyB = new CompanyData();
        companyB.Settings.Company.Email = "owner-b@example.com";
        var portalB = companyB.Settings.PaymentPortal;
        portalB.CompanyName = "Company B";
        portalB.SendPaymentReminders = false;
        portalB.EmailOwnerOnPayment = false;
        portalB.AutoSyncIntervalMinutes = 30;
        App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(companyB));

        handler.Release.SetResult();
        await refresh;

        Assert.False(portalB.ConnectedAccounts.StripeConnected);
        Assert.Null(portalB.ConnectedAccounts.StripeEmail);
        Assert.False(portalB.SendPaymentReminders);
        Assert.False(portalB.EmailOwnerOnPayment);
        Assert.Equal("Company B", portalB.CompanyName);
        Assert.Equal(30, portalB.AutoSyncIntervalMinutes);
        Assert.Equal("owner-b@example.com", companyB.Settings.Company.Email);
    }

    public void Dispose()
    {
        App.SetCompanyManagerForTesting(null);
        App.SetPaymentPortalServiceForTesting(null);
        if (string.IsNullOrEmpty(_priorPortalKey))
            PortalSettings.DeactivateApiKey();
        else
            DotEnv.SetInMemory(PortalSettings.ApiKeyEnvVar, _priorPortalKey);
        GC.SuppressFinalize(this);
    }
}
