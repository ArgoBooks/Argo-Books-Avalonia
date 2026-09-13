using System.Net;
using System.Text;
using ArgoBooks.Core.Models.Integrations;
using ArgoBooks.Core.Services.Integrations;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class SettingsStripeIntegrationTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    [Fact]
    public async Task TryConnectStripe_ValidKey_StoresKeyAndMarksConnected()
    {
        var vm = new SettingsModalViewModel
        {
            StripeKeyInput = "rk_test_abc"
        };
        var target = new StripeIntegrationSettings();
        // A scoped restricted key validates against balance_transactions (returns a list);
        // the label is derived from the key's test/live mode.
        var client = new StripeApiClient(new HttpClient(
            new StubHandler(HttpStatusCode.OK, "{\"object\":\"list\",\"data\":[]}")));

        var connected = await vm.TryConnectStripeAsync(client, target);

        Assert.True(connected);
        Assert.True(target.Connected);
        Assert.Equal("rk_test_abc", target.ApiKey);
        Assert.Equal("Test mode", target.AccountLabel);
        Assert.True(vm.StripeIntegrationConnected);
        Assert.Equal("Test mode", vm.StripeIntegrationAccountLabel);
        Assert.Null(vm.StripeIntegrationError);
    }

    [Fact]
    public async Task TryConnectStripe_RejectedKey_SetsErrorAndStaysDisconnected()
    {
        var vm = new SettingsModalViewModel
        {
            StripeKeyInput = "bad"
        };
        var target = new StripeIntegrationSettings();
        var client = new StripeApiClient(new HttpClient(
            new StubHandler(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"no\"}}")));

        var connected = await vm.TryConnectStripeAsync(client, target);

        Assert.False(connected);
        Assert.False(target.Connected);
        Assert.Null(target.ApiKey);
        Assert.False(vm.StripeIntegrationConnected);
        Assert.NotNull(vm.StripeIntegrationError);
    }
}
