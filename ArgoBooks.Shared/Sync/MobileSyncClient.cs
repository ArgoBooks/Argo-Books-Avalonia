using System.Net;
using ArgoBooks.Core.Services;
using System.Text;
using System.Text.Json;

namespace ArgoBooks.Shared.Sync;

/// <summary>
/// HTTP client for the mobile app's sync endpoints. Authenticates as a paired device
/// (via X-Sync-Device-Token header) rather than as the desktop owner.
/// Lives in Shared so the Android app can reference it without pulling in ArgoBooks.Core.
/// </summary>
public class MobileSyncClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates a new mobile sync client.
    /// </summary>
    /// <param name="http">HttpClient to use for requests (or null for a default instance).</param>
    /// <param name="baseUrl">Base URL of the sync server. In the app this is always
    /// MobileApiConfig.BaseUrl, i.e. https://argorobots.com or https://dev.argorobots.com;
    /// tests pass a local stub such as http://localhost:5000.</param>
    public MobileSyncClient(HttpClient? http, string baseUrl)
    {
        _http = http ?? new HttpClient();
        _baseUrl = baseUrl;
    }

    /// <summary>
    /// Sends the request and returns the raw status code + body text, without attempting any
    /// JSON parsing. A non-2xx body (e.g. an Apache/htaccess HTML error page) is not guaranteed
    /// to be JSON, so parsing must happen only after the caller has decided the response is one
    /// it expects to be JSON.
    /// </summary>
    private async Task<(HttpStatusCode StatusCode, string Text)> SendRawAsync(string path, object body, string? deviceToken, CancellationToken ct)
    {
        var fullUrl = _baseUrl + "/api/sync" + path;
        using var req = new HttpRequestMessage(HttpMethod.Post, fullUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        if (deviceToken != null)
        {
            req.Headers.Add("X-Sync-Device-Token", deviceToken);
        }

        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        return (resp.StatusCode, text);
    }

    /// <summary>
    /// Sends the request and parses the body as JSON only on success. Non-2xx bodies are never
    /// passed to <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/>: a non-success
    /// response returns <c>default</c> for the JSON element alongside its status code, letting
    /// the caller decide how to react (e.g. return null vs. throw) without risking a
    /// <see cref="JsonException"/> from a non-JSON error page.
    /// </summary>
    private async Task<(HttpStatusCode StatusCode, JsonElement Json)> PostRawAsync(string path, object body, string? deviceToken, CancellationToken ct)
    {
        var (statusCode, text) = await SendRawAsync(path, body, deviceToken, ct);

        // The body is still in hand here, and it holds the server's wording for how long to wait.
        // FromBody reads it without throwing on a non-JSON error page.
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            throw ServerRateLimitedException.FromBody(text);
        }

        if (statusCode < HttpStatusCode.OK || statusCode >= HttpStatusCode.MultipleChoices)
        {
            return (statusCode, default);
        }

        var json = string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (statusCode, json);
    }

    /// <summary>
    /// Original contract used by the pre-existing callers (redeem/snapshot/queue push): resolve
    /// the status code first, short-circuit 404, and throw <see cref="HttpRequestException"/> on
    /// any other non-2xx status, all BEFORE ever attempting to parse the body as JSON. This keeps
    /// those callers unaffected by non-JSON error pages.
    /// </summary>
    private async Task<JsonElement> PostAsync(string path, object body, string? deviceToken = null, CancellationToken ct = default)
    {
        var (statusCode, text) = await SendRawAsync(path, body, deviceToken, ct);

        // Return null on 404 for snapshot get (no snapshot yet)
        if (statusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        // A 401/403 on an authenticated call means this device's token was revoked desktop-side.
        // Surface it as a distinct type (not HttpRequestException) so callers disconnect instead of
        // falling back to cached data like they would for a transient network error.
        if (deviceToken != null && (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden))
        {
            throw new SyncUnauthorizedException($"Request to {path} was rejected with status code {(int)statusCode} (device revoked).");
        }

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            throw ServerRateLimitedException.FromBody(text);
        }

        if (statusCode < HttpStatusCode.OK || statusCode >= HttpStatusCode.MultipleChoices)
        {
            throw new HttpRequestException($"Request to {path} failed with status code {(int)statusCode}.");
        }

        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>
    /// Redeems a pairing token (from the QR code) to get a device token and company info.
    /// This call is unauthenticated (no device token yet).
    /// </summary>
    public async Task<PairResult?> RedeemPairingAsync(string pairingToken, string deviceLabel, CancellationToken ct)
    {
        var json = await PostAsync("/pair/redeem", new { pairing_token = pairingToken, device_label = deviceLabel }, ct: ct);

        if (json.ValueKind == default)
        {
            return null;
        }

        var result = new PairResult
        {
            DeviceToken = json.TryGetProperty("device_token", out var dt) ? (dt.GetString() ?? string.Empty) : string.Empty,
            CompanyUid = json.TryGetProperty("company_uid", out var cu) ? (cu.GetString() ?? string.Empty) : string.Empty,
            CompanyLabel = json.TryGetProperty("company_label", out var cl) ? (cl.GetString() ?? string.Empty) : string.Empty
        };

        return result;
    }

    /// <summary>
    /// Fetches the encrypted snapshot from the server using the device token.
    /// Returns null if the server returns 404 (no snapshot yet).
    /// </summary>
    public async Task<SnapshotResult?> GetSnapshotAsync(string deviceToken, CancellationToken ct)
    {
        var json = await PostAsync("/snapshot/get", new { }, deviceToken, ct);

        if (json.ValueKind == default)
        {
            return null;
        }

        var result = new SnapshotResult
        {
            Ciphertext = json.TryGetProperty("ciphertext", out var c) ? (c.GetString() ?? string.Empty) : string.Empty,
            UpdatedAt = json.TryGetProperty("updated_at", out var u) ? (u.GetString() ?? string.Empty) : string.Empty
        };

        return result;
    }

    /// <summary>
    /// Pushes a captured transaction to the server queue (from a receipt scan or manual entry).
    /// </summary>
    public async Task PushCaptureAsync(string deviceToken, string ciphertext, CancellationToken ct)
    {
        await PostAsync("/queue/push", new { ciphertext }, deviceToken, ct);
    }

    /// <summary>
    /// Claims a short pairing code (typed in on the phone) to get a device token and company info.
    /// This call is unauthenticated (no device token yet). Returns null on a non-success status
    /// (e.g. 400 for an invalid/expired code), and throws <see cref="ServerRateLimitedException"/>
    /// on a 429, which is not a verdict on the code.
    /// </summary>
    public async Task<ClaimResult?> ClaimPairingAsync(string code, string phonePublicKeyBase64, string deviceLabel, CancellationToken ct)
    {
        var (statusCode, json) = await PostRawAsync(
            "/pair/claim",
            new { code, phone_public_key = phonePublicKeyBase64, device_label = deviceLabel },
            deviceToken: null,
            ct);

        if (statusCode < HttpStatusCode.OK || statusCode >= HttpStatusCode.MultipleChoices)
        {
            return null;
        }

        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ClaimResult(
            DeviceToken: json.TryGetProperty("device_token", out var dt) ? (dt.GetString() ?? string.Empty) : string.Empty,
            CompanyUid: json.TryGetProperty("company_uid", out var cu) ? (cu.GetString() ?? string.Empty) : string.Empty,
            CompanyLabel: json.TryGetProperty("company_label", out var cl) ? (cl.GetString() ?? string.Empty) : string.Empty);
    }

    /// <summary>
    /// Fetches the encrypted sync key once the desktop has approved a pending pairing claim.
    /// Returns null while the pairing is still pending (server returns <c>{pending: true}</c>)
    /// or if the claim was never made / has expired (404).
    /// </summary>
    public async Task<string?> FetchPairingKeyAsync(string deviceToken, CancellationToken ct)
    {
        var (statusCode, json) = await PostRawAsync("/pair/key", new { }, deviceToken, ct);

        if (statusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (statusCode < HttpStatusCode.OK || statusCode >= HttpStatusCode.MultipleChoices)
        {
            return null;
        }

        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (json.TryGetProperty("pending", out var pending) && pending.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        return json.TryGetProperty("encrypted_sync_key", out var key) ? key.GetString() : null;
    }
}
