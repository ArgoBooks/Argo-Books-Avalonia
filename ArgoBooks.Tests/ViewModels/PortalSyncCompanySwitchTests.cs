using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

// Uses App.CompanyManager, App.PaymentPortalService and the portal API key, all process-wide,
// so run in the serialized modal-VM collection.
[Collection("ModalViewModels")]
public class PortalSyncCompanySwitchTests : IDisposable
{
    private readonly string _priorPortalKey = DotEnv.Get(PortalSettings.ApiKeyEnvVar);

    private const string SyncBody = """
        {
          "success": true,
          "payments": [
            {
              "id": 7,
              "invoiceId": "INV-001",
              "customerName": "Acme Corp",
              "amount": 100,
              "currency": "USD",
              "paymentMethod": "stripe",
              "referenceNumber": "REF-1",
              "createdAt": "2026-09-01T10:00:00Z"
            }
          ]
        }
        """;

    // Holds the payment sync reply until the test has switched companies; answers anything else at once.
    private sealed class PortalHandler : HttpMessageHandler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Paths) Paths.Add(path);

            var body = """{ "success": true }""";
            if (path.EndsWith("/payments/sync", StringComparison.Ordinal))
            {
                await Release.Task.WaitAsync(ct);
                body = SyncBody;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    // The sync asks with company A's key and can take up to the 30s client timeout. If company B
    // is opened meanwhile, A's payments must not be confirmed under B's key, saved into B, or
    // announced in B. Force sync brings them back when A is next opened.
    [Fact]
    public async Task PaymentsArrivingAfterACompanySwitch_AreNotConfirmedOrApplied()
    {
        var customer = new Customer { Id = "CUST-001", Name = "Acme Corp" };
        var companyA = new CompanyData();
        companyA.Customers.Add(customer);
        companyA.Invoices.Add(new Invoice
        {
            Id = "INV-001",
            InvoiceNumber = "INV-2026-001",
            CustomerId = customer.Id,
            Total = 100m,
            OriginalCurrency = "USD",
            Status = InvoiceStatus.Sent,
        });
        companyA.Settings.PaymentPortal.PersistedApiKey = "test-key-a";
        App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(companyA));
        PortalSettings.ActivateApiKey(companyA.Settings.PaymentPortal);

        var handler = new PortalHandler();
        App.SetPaymentPortalServiceForTesting(new PaymentPortalService(new HttpClient(handler)));

        var sync = App.AutoSyncPortalPaymentsAsync();

        var companyB = new CompanyData();
        companyB.Settings.PaymentPortal.PersistedApiKey = "test-key-b";
        App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(companyB));
        PortalSettings.ActivateApiKey(companyB.Settings.PaymentPortal);

        handler.Release.SetResult();
        await sync;

        Assert.DoesNotContain(handler.Paths, p => p.EndsWith("/payments/sync/confirm", StringComparison.Ordinal));
        Assert.Empty(companyA.Payments);
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
