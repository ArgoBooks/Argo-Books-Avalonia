using System.Net;
using System.Text;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class StripeApiClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task ValidateKeyAsync_ValidTestKey_ReturnsOk_ValidatesAgainstBalanceTransactions_TestModeLabel()
    {
        // A scoped restricted key returns a balance-transactions list, not an account.
        var handler = new StubHandler(HttpStatusCode.OK, "{\"object\":\"list\",\"data\":[]}");
        var client = new StripeApiClient(new HttpClient(handler));

        var result = await client.ValidateKeyAsync("rk_test_abc");

        Assert.True(result.Ok);
        Assert.Equal("Test mode", result.AccountLabel);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("rk_test_abc", handler.LastRequest!.Headers.Authorization!.Parameter);
        // Validates against balance_transactions (which a scoped key can read), NOT /v1/account.
        Assert.Contains("balance_transactions", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("limit=1", handler.LastRequest!.RequestUri!.ToString());
        Assert.DoesNotContain("/v1/account", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ValidateKeyAsync_LiveKey_ReturnsLiveAccountLabel()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"object\":\"list\",\"data\":[]}");
        var client = new StripeApiClient(new HttpClient(handler));

        var result = await client.ValidateKeyAsync("rk_live_abc");

        Assert.True(result.Ok);
        Assert.Equal("Live account", result.AccountLabel);
    }

    [Fact]
    public async Task ValidateKeyAsync_Unauthorized_ReturnsRejectedMessage()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Invalid API Key\"}}");
        var client = new StripeApiClient(new HttpClient(handler));

        var result = await client.ValidateKeyAsync("bad");

        Assert.False(result.Ok);
        Assert.Contains("rejected by Stripe", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateKeyAsync_Forbidden_TellsUserToGrantReadAccess()
    {
        // A valid key that lacks the required read permissions returns 403.
        var handler = new StubHandler(HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"insufficient permissions\"}}");
        var client = new StripeApiClient(new HttpClient(handler));

        var result = await client.ValidateKeyAsync("rk_test_abc");

        Assert.False(result.Ok);
        Assert.Contains("Read access", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchPayoutsAsync_ReadsEachPayoutsCurrency()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            "{\"has_more\":false,\"data\":[" +
            "{\"id\":\"po_1\",\"amount\":10000,\"currency\":\"jpy\",\"arrival_date\":1700000900,\"status\":\"paid\"}" +
            "]}");
        var client = new StripeApiClient(new HttpClient(handler));

        var payout = Assert.Single(await client.FetchPayoutsAsync("rk_test_abc"));

        Assert.Equal(10000, payout.AmountCents);
        Assert.Equal("jpy", payout.Currency);
    }
}
