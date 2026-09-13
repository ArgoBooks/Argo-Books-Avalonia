using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

// Uses App.CompanyManager, App.PaymentPortalService and the portal API key, all process-wide,
// so run in the serialized modal-VM collection.
[Collection("ModalViewModels")]
public class SettingsPortalRefreshTests : IDisposable
{
    private readonly string _priorPortalKey = DotEnv.Get(PortalSettings.ApiKeyEnvVar);

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

    // The status refresh starts as Settings opens. Portal fields the user changes before it
    // answers only apply on Save, so the refresh must write back what the server told it and
    // nothing else, or Close without saving keeps the edits.
    [Fact]
    public async Task StatusRefresh_WritesServerStateButNotUnsavedPortalEdits()
    {
        var company = new CompanyData();
        var portal = company.Settings.PaymentPortal;
        portal.PersistedApiKey = "test-key";
        portal.NotifyOnPayment = true;
        portal.AutoSyncIntervalMinutes = 30;
        App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(company));
        PortalSettings.ActivateApiKey(portal);

        // No portal service yet, so opening loads the fields without starting a refresh of its own.
        var vm = new SettingsModalViewModel();
        vm.OpenWithTab(SettingsTab.General);

        var handler = new GatedHandler("""
            {
              "success": true,
              "connected": true,
              "connectedProviders": { "stripeConnected": true, "stripeEmail": "a@stripe.test" },
              "preferences": { "sendPaymentReminders": true, "emailOwnerOnPayment": true }
            }
            """);
        App.SetPaymentPortalServiceForTesting(new PaymentPortalService(new HttpClient(handler)));

        var refresh = vm.RefreshProviderStatusAsync();

        vm.PortalNotifyOnPayment = false;
        vm.PortalSyncInterval = "60";

        handler.Release.SetResult();
        await refresh;

        Assert.True(portal.NotifyOnPayment);
        Assert.Equal(30, portal.AutoSyncIntervalMinutes);

        Assert.True(portal.ConnectedAccounts.StripeConnected);
        Assert.Equal("a@stripe.test", portal.ConnectedAccounts.StripeEmail);
        Assert.True(portal.SendPaymentReminders);
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
