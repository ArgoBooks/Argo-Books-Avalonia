using System.Net;
using System.Text;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class StripeChargeFetchTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    // A succeeded subscription charge: gross 5000, fee 175, tax 400, discount 500,
    // customer Jane, invoice line "Premium Plan".
    private const string Body =
        "{\"has_more\":false,\"data\":[{" +
        "\"id\":\"ch_1\",\"status\":\"succeeded\",\"paid\":true,\"amount\":5000,\"amount_refunded\":0,\"currency\":\"usd\",\"created\":1700000000," +
        "\"customer\":{\"id\":\"cus_1\",\"name\":\"Jane Doe\",\"email\":\"jane@x.com\"}," +
        "\"balance_transaction\":{\"id\":\"txn_1\",\"fee\":175,\"net\":4825}," +
        "\"invoice\":{\"id\":\"in_1\",\"tax\":400,\"total_discount_amounts\":[{\"amount\":500}]," +
        "\"lines\":{\"data\":[{\"description\":\"Premium Plan\"}]}}" +
        "}]}";

    [Fact]
    public async Task Fetch_ParsesChargeDetail()
    {
        var handler = new StubHandler(Body);
        var client = new StripeApiClient(new HttpClient(handler));

        var charges = await client.FetchChargesUntilAsync("rk_test", null);

        var c = Assert.Single(charges);
        Assert.Equal("ch_1", c.ChargeId);
        Assert.Equal(5000, c.GrossCents);
        Assert.Equal(175, c.FeeCents);
        Assert.Equal(400, c.TaxCents);
        Assert.Equal(500, c.DiscountCents);
        Assert.Equal("Jane Doe", c.CustomerName);
        Assert.Equal("jane@x.com", c.CustomerEmail);
        Assert.Equal("Premium Plan", c.ProductName);
        Assert.Contains("expand", handler.LastUrl);
        Assert.Contains("/v1/charges", handler.LastUrl);
    }

    [Fact]
    public async Task Fetch_NoInvoice_FallsBackToChargeDescription()
    {
        var body = "{\"has_more\":false,\"data\":[{" +
            "\"id\":\"ch_2\",\"status\":\"succeeded\",\"paid\":true,\"amount\":1000,\"amount_refunded\":0,\"currency\":\"usd\",\"created\":1,\"description\":\"T-shirt\"," +
            "\"balance_transaction\":{\"fee\":30,\"net\":970}}]}";
        var client = new StripeApiClient(new HttpClient(new StubHandler(body)));

        var c = Assert.Single(await client.FetchChargesUntilAsync("rk_test", null));
        Assert.Equal("T-shirt", c.ProductName);
        Assert.Equal(0, c.TaxCents);
    }

    [Fact]
    public async Task Fetch_SkipsUnsucceededCharges()
    {
        var body = "{\"has_more\":false,\"data\":[{" +
            "\"id\":\"ch_3\",\"status\":\"failed\",\"paid\":false,\"amount\":1000,\"currency\":\"usd\",\"created\":1}]}";
        var client = new StripeApiClient(new HttpClient(new StubHandler(body)));

        Assert.Empty(await client.FetchChargesUntilAsync("rk_test", null));
    }
}
