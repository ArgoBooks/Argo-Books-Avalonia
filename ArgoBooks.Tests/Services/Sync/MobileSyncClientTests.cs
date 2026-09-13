using System.Net;
using System.Text;
using ArgoBooks.Shared.Sync;
using Xunit;

namespace ArgoBooks.Tests.Services.Sync;

public class MobileSyncClientTests
{
    private sealed class CannedHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        public string? LastRequestBody;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastRequestBody = request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task RedeemPairing_parses_device_token_and_company_uid()
    {
        var handler = new CannedHandler(
            "{\"success\":true,\"device_token\":\"token-abc123\",\"company_uid\":\"company-456\",\"company_label\":\"Acme Corp\"}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.RedeemPairingAsync("pairing-xyz", "My Phone", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("token-abc123", result.DeviceToken);
        Assert.Equal("company-456", result.CompanyUid);
        Assert.Equal("Acme Corp", result.CompanyLabel);
    }

    [Fact]
    public async Task RedeemPairing_posts_to_correct_endpoint()
    {
        var handler = new CannedHandler(
            "{\"success\":true,\"device_token\":\"token-abc123\",\"company_uid\":\"company-456\",\"company_label\":\"Acme Corp\"}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        await client.RedeemPairingAsync("pairing-xyz", "My Phone", CancellationToken.None);

        Assert.EndsWith("/pair/redeem", handler.Last!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetSnapshot_sends_device_token_header()
    {
        var handler = new CannedHandler("{\"ciphertext\":\"encrypted-data\",\"updated_at\":\"2026-07-13T10:00:00Z\"}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        await client.GetSnapshotAsync("device-token-123", CancellationToken.None);

        Assert.True(handler.Last!.Headers.Contains("X-Sync-Device-Token"));
        var headerValues = handler.Last.Headers.GetValues("X-Sync-Device-Token");
        Assert.Contains("device-token-123", headerValues);
    }

    [Fact]
    public async Task GetSnapshot_parses_ciphertext_and_updated_at()
    {
        var handler = new CannedHandler("{\"ciphertext\":\"blob-data\",\"updated_at\":\"2026-07-13T10:00:00Z\"}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.GetSnapshotAsync("device-token-123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("blob-data", result.Ciphertext);
        Assert.Equal("2026-07-13T10:00:00Z", result.UpdatedAt);
    }

    [Fact]
    public async Task GetSnapshot_returns_null_on_404()
    {
        var handler = new CannedHandler("{}", HttpStatusCode.NotFound);
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.GetSnapshotAsync("device-token-123", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSnapshot_posts_to_correct_endpoint()
    {
        var handler = new CannedHandler("{\"ciphertext\":\"blob-data\",\"updated_at\":\"2026-07-13T10:00:00Z\"}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        await client.GetSnapshotAsync("device-token-123", CancellationToken.None);

        Assert.EndsWith("/snapshot/get", handler.Last!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PushCapture_posts_ciphertext_with_device_token_header()
    {
        var handler = new CannedHandler("{\"success\":true}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        await client.PushCaptureAsync("device-token-123", "capture-blob-456", CancellationToken.None);

        Assert.True(handler.Last!.Headers.Contains("X-Sync-Device-Token"));
        var headerValues = handler.Last.Headers.GetValues("X-Sync-Device-Token");
        Assert.Contains("device-token-123", headerValues);
        Assert.EndsWith("/queue/push", handler.Last.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PushCapture_posts_to_correct_endpoint()
    {
        var handler = new CannedHandler("{\"success\":true}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        await client.PushCaptureAsync("device-token-123", "capture-blob-456", CancellationToken.None);

        Assert.EndsWith("/queue/push", handler.Last!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ClaimPairing_parses_device_token_and_company_fields()
    {
        var handler = new CannedHandler(
            "{\"success\":true,\"company_uid\":\"company-456\",\"company_label\":\"Acme Corp\",\"device_token\":\"token-abc123\"}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.ClaimPairingAsync("123456", "pubkey-base64", "My Phone", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("token-abc123", result.DeviceToken);
        Assert.Equal("company-456", result.CompanyUid);
        Assert.Equal("Acme Corp", result.CompanyLabel);
    }

    [Fact]
    public async Task ClaimPairing_posts_code_public_key_and_label_to_correct_endpoint()
    {
        var handler = new CannedHandler(
            "{\"success\":true,\"company_uid\":\"company-456\",\"company_label\":\"Acme Corp\",\"device_token\":\"token-abc123\"}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        await client.ClaimPairingAsync("123456", "pubkey-base64", "My Phone", CancellationToken.None);

        Assert.EndsWith("/pair/claim", handler.Last!.RequestUri!.AbsolutePath);
        var body = handler.LastRequestBody!;
        Assert.Contains("\"code\":\"123456\"", body);
        Assert.Contains("\"phone_public_key\":\"pubkey-base64\"", body);
        Assert.Contains("\"device_label\":\"My Phone\"", body);
    }

    [Fact]
    public async Task ClaimPairing_returns_null_on_400()
    {
        var handler = new CannedHandler("{\"error\":\"invalid_code\"}", HttpStatusCode.BadRequest);
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.ClaimPairingAsync("bad-code", "pubkey-base64", "My Phone", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ClaimPairing_returns_null_on_429()
    {
        var handler = new CannedHandler("{\"error\":\"rate_limited\"}", HttpStatusCode.TooManyRequests);
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.ClaimPairingAsync("123456", "pubkey-base64", "My Phone", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchPairingKey_returns_ciphertext_when_present()
    {
        var handler = new CannedHandler("{\"encrypted_sync_key\":\"encrypted-key-blob\"}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.FetchPairingKeyAsync("device-token-123", CancellationToken.None);

        Assert.Equal("encrypted-key-blob", result);
    }

    [Fact]
    public async Task FetchPairingKey_returns_null_when_pending()
    {
        var handler = new CannedHandler("{\"pending\":true}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.FetchPairingKeyAsync("device-token-123", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchPairingKey_returns_null_on_404()
    {
        var handler = new CannedHandler("{}", HttpStatusCode.NotFound);
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.FetchPairingKeyAsync("device-token-123", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchPairingKey_sends_device_token_header()
    {
        var handler = new CannedHandler("{\"encrypted_sync_key\":\"encrypted-key-blob\"}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        await client.FetchPairingKeyAsync("device-token-123", CancellationToken.None);

        Assert.True(handler.Last!.Headers.Contains("X-Sync-Device-Token"));
        var headerValues = handler.Last.Headers.GetValues("X-Sync-Device-Token");
        Assert.Contains("device-token-123", headerValues);
    }

    [Fact]
    public async Task FetchPairingKey_posts_to_correct_endpoint()
    {
        var handler = new CannedHandler("{\"encrypted_sync_key\":\"encrypted-key-blob\"}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        await client.FetchPairingKeyAsync("device-token-123", CancellationToken.None);

        Assert.EndsWith("/pair/key", handler.Last!.RequestUri!.AbsolutePath);
    }

    // Regression tests: a non-2xx response with a non-JSON body (e.g. an Apache/htaccess HTML
    // error page, which is realistic for these routes) must never reach JsonDocument.Parse.
    // Existing callers must still surface HttpRequestException (not JsonException), and the
    // newer ClaimPairing/FetchPairingKey callers must still return null (not throw).

    [Fact]
    public async Task GetSnapshot_throws_HttpRequestException_not_JsonException_on_non_json_error_body()
    {
        var handler = new CannedHandler("<html><body>500 Internal Server Error</body></html>", HttpStatusCode.InternalServerError);
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetSnapshotAsync("device-token-123", CancellationToken.None));
    }

    [Fact]
    public async Task RedeemPairing_throws_HttpRequestException_not_JsonException_on_non_json_error_body()
    {
        var handler = new CannedHandler("<html><body>502 Bad Gateway</body></html>", HttpStatusCode.BadGateway);
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.RedeemPairingAsync("pairing-xyz", "My Phone", CancellationToken.None));
    }

    [Fact]
    public async Task ClaimPairing_returns_null_on_non_json_error_body()
    {
        var handler = new CannedHandler("<html><body>500 Internal Server Error</body></html>", HttpStatusCode.InternalServerError);
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.ClaimPairingAsync("123456", "pubkey-base64", "My Phone", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchPairingKey_returns_null_on_non_json_error_body()
    {
        var handler = new CannedHandler("<html><body>500 Internal Server Error</body></html>", HttpStatusCode.InternalServerError);
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");

        var result = await client.FetchPairingKeyAsync("device-token-123", CancellationToken.None);

        Assert.Null(result);
    }
}
