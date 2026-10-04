using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// What a Stripe sync has to pick up after the sale itself went in: a refund made later, a charge
/// that was still pending, and charges it is shown a second time.
/// </summary>
public class StripeLaterActivityTests
{
    private sealed class StripeStub : HttpMessageHandler
    {
        public string Charges { get; set; } = Charge("ch_1", "succeeded", 5000, 0);
        public string Refunds { get; set; } = "";
        public bool RefundsForbidden { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("/v1/refunds") && RefundsForbidden)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                { Content = new StringContent("{}", Encoding.UTF8, "application/json") });

            var items = url.Contains("/v1/refunds") ? Refunds
                : url.Contains("/v1/charges") ? Charges
                : "";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent($"{{\"has_more\":false,\"data\":[{items}]}}", Encoding.UTF8, "application/json") });
        }
    }

    private static string Charge(string id, string status, long amount, long refunded) =>
        $"{{\"id\":\"{id}\",\"status\":\"{status}\",\"paid\":{(status == "succeeded" ? "true" : "false")}," +
        $"\"amount\":{amount},\"amount_refunded\":{refunded},\"currency\":\"usd\",\"created\":1700000000,\"description\":\"Plan\"}}";

    private static string Refund(string id, string charge, long amount, long created) =>
        $"{{\"id\":\"{id}\",\"charge\":\"{charge}\",\"amount\":{amount},\"currency\":\"usd\",\"created\":{created},\"status\":\"succeeded\"}}";

    private static CompanyData ConnectedData()
    {
        var data = new CompanyData();
        data.Settings.Integrations.Stripe.ApiKey = "rk_test";
        data.Settings.Integrations.Stripe.Connected = true;
        return data;
    }

    private static async Task SyncAsync(StripeSyncService svc, CompanyData data)
    {
        var preview = await svc.PreviewAsync(data);
        if (preview.HasActivity) svc.ImportPreview(data, preview);
    }

    [Fact]
    public async Task ARefundMadeAfterTheSaleWasImported_IsRecordedOnce()
    {
        var stub = new StripeStub();
        var svc = new StripeSyncService(new StripeApiClient(new HttpClient(stub)));
        var data = ConnectedData();
        await SyncAsync(svc, data);
        var sale = Assert.Single(data.Revenues);

        stub.Refunds = Refund("re_1", "ch_1", 2000, 1700090000);
        await SyncAsync(svc, data);

        var recorded = Assert.Single(data.Returns);
        Assert.Equal(sale.Id, recorded.OriginalTransactionId);
        Assert.Equal(20.00m, recorded.RefundAmount);

        // Shown the same refund again, as after a disconnect and reconnect.
        data.Settings.Integrations.Stripe.LastRefundCursor = null;
        await SyncAsync(svc, data);

        Assert.Single(data.Returns);
    }

    [Fact]
    public async Task ASecondRefundOnTheSameSale_IsRecordedAsWell()
    {
        var stub = new StripeStub();
        var svc = new StripeSyncService(new StripeApiClient(new HttpClient(stub)));
        var data = ConnectedData();
        await SyncAsync(svc, data);

        stub.Refunds = Refund("re_1", "ch_1", 2000, 1700090000);
        await SyncAsync(svc, data);
        stub.Refunds = Refund("re_2", "ch_1", 1000, 1700090500) + "," + Refund("re_1", "ch_1", 2000, 1700090000);
        await SyncAsync(svc, data);

        Assert.Equal([20.00m, 10.00m], data.Returns.Select(r => r.RefundAmount));
    }

    // The sale arrives already refunded, and the refund list shows the same refund.
    [Fact]
    public async Task ARefundTheSaleArrivedWith_IsNotRecordedTwice()
    {
        var stub = new StripeStub
        {
            Charges = Charge("ch_1", "succeeded", 5000, 5000),
            Refunds = Refund("re_1", "ch_1", 5000, 1700000500)
        };
        var svc = new StripeSyncService(new StripeApiClient(new HttpClient(stub)));
        var data = ConnectedData();

        await SyncAsync(svc, data);
        await SyncAsync(svc, data);

        Assert.Equal(50.00m, Assert.Single(data.Returns).RefundAmount);
    }

    [Fact]
    public async Task AKeyThatCannotReadRefunds_StillImportsTheSales()
    {
        var stub = new StripeStub { RefundsForbidden = true };
        var data = ConnectedData();

        await SyncAsync(new StripeSyncService(new StripeApiClient(new HttpClient(stub))), data);

        Assert.Single(data.Revenues);
    }

    [Fact]
    public async Task AChargeStillPending_IsImportedOnceItSucceeds()
    {
        var stub = new StripeStub
        {
            Charges = Charge("ch_3", "succeeded", 3000, 0) + "," + Charge("ch_2", "pending", 2000, 0) + "," + Charge("ch_1", "succeeded", 1000, 0)
        };
        var svc = new StripeSyncService(new StripeApiClient(new HttpClient(stub)));
        var data = ConnectedData();

        await SyncAsync(svc, data);
        Assert.Equal(2, data.Revenues.Count);

        stub.Charges = Charge("ch_3", "succeeded", 3000, 0) + "," + Charge("ch_2", "succeeded", 2000, 0) + "," + Charge("ch_1", "succeeded", 1000, 0);
        await SyncAsync(svc, data);

        Assert.Equal(["ch_1", "ch_2", "ch_3"], data.Revenues.Select(r => r.ReferenceNumber).Order());
    }

    // Disconnecting clears the cursor, so reconnecting reads every charge again.
    [Fact]
    public async Task ChargesAlreadyInTheBooks_AreNotImportedAgain()
    {
        var svc = new StripeSyncService(new StripeApiClient(new HttpClient(new StripeStub())));
        var data = ConnectedData();
        await SyncAsync(svc, data);

        data.Settings.Integrations.Stripe.LastSyncCursor = null;
        var preview = await svc.PreviewAsync(data);

        Assert.False(preview.HasActivity);
        Assert.Single(data.Revenues);
    }
}
