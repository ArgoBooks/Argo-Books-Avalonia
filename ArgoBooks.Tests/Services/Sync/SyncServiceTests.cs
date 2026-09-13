using System.Net;
using System.Text;
using ArgoBooks.Core.Services.Sync;
using Xunit;
namespace ArgoBooks.Tests.Services.Sync;

public class SyncServiceTests
{
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string _json; public HttpRequestMessage? Last; public string? LastBody;
        public CannedHandler(string json) => _json = json;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(_json, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task CreatePairing_returns_token_and_short_code_from_response()
    {
        var handler = new CannedHandler("{\"success\":true,\"pairing_token\":\"abc123\",\"short_code\":\"483920\",\"expires_in_seconds\":600}");
        var svc = new SyncService(new HttpClient(handler));
        var pairing = await svc.CreatePairingAsync("uid-1", "Acme", CancellationToken.None);
        Assert.NotNull(pairing);
        Assert.Equal("abc123", pairing.Token);
        Assert.Equal("483920", pairing.ShortCode);
        Assert.EndsWith("/pair/create", handler.Last!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CreatePairing_returns_null_when_token_missing()
    {
        var handler = new CannedHandler("{\"success\":true}");
        var svc = new SyncService(new HttpClient(handler));
        var pairing = await svc.CreatePairingAsync("uid-1", "Acme", CancellationToken.None);
        Assert.Null(pairing);
    }

    [Fact]
    public async Task GetPairingStatus_parses_status_and_optional_key()
    {
        var handler = new CannedHandler("{\"success\":true,\"status\":\"delivered\",\"phone_public_key\":\"PUBKEY\"}");
        var svc = new SyncService(new HttpClient(handler));
        var status = await svc.GetPairingStatusAsync("abc123", CancellationToken.None);
        Assert.NotNull(status);
        Assert.Equal("delivered", status.Status);
        Assert.Equal("PUBKEY", status.PhonePublicKey);
        Assert.EndsWith("/pair/status", handler.Last!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetPairingStatus_key_is_null_when_absent()
    {
        var handler = new CannedHandler("{\"success\":true,\"status\":\"pending\"}");
        var svc = new SyncService(new HttpClient(handler));
        var status = await svc.GetPairingStatusAsync("abc123", CancellationToken.None);
        Assert.NotNull(status);
        Assert.Equal("pending", status.Status);
        Assert.Null(status.PhonePublicKey);
    }

    [Fact]
    public async Task DeliverKey_posts_ciphertext_to_pair_deliver_with_owner_auth_headers()
    {
        var handler = new CannedHandler("{\"success\":true}");
        var svc = new SyncService(new HttpClient(handler));
        await svc.DeliverKeyAsync("abc123", "ENCRYPTED_BLOB", CancellationToken.None);

        Assert.EndsWith("/pair/deliver", handler.Last!.RequestUri!.AbsolutePath);
        Assert.Contains("\"pairing_token\":\"abc123\"", handler.LastBody);
        Assert.Contains("\"encrypted_sync_key\":\"ENCRYPTED_BLOB\"", handler.LastBody);
    }

    [Fact]
    public async Task PullQueue_maps_items()
    {
        var handler = new CannedHandler("{\"success\":true,\"items\":[{\"id\":7,\"ciphertext\":\"BLOB\"}]}");
        var svc = new SyncService(new HttpClient(handler));
        var items = await svc.PullQueueAsync("uid-1", CancellationToken.None);
        Assert.Single(items);
        Assert.Equal(7, items[0].Id);
        Assert.Equal("BLOB", items[0].Ciphertext);
    }
}
