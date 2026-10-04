using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace ArgoBooks.Core.Services.Integrations;

/// <summary>
/// Client for the Argo Books public API on argorobots.com.
///
/// Two surfaces, deliberately kept in one class because they are always used
/// together:
///
///   /api/developer/*  the control plane. Authenticated by this desktop's own
///                     licence identity, and the only place API keys are made.
///   /v1/*             the public API, authenticated by one of those keys. The
///                     desktop uses a key it minted for itself, so revoking a
///                     developer's key never locks the app out of its own queue.
/// </summary>
public class ArgoApiClient
{
    private static string ControlBase => $"{ApiConfig.BaseUrl}/api/developer";
    private static string V1Base => $"{ApiConfig.BaseUrl}/v1";

    /// <summary>Server caps a page at 100. Ask for the maximum: this is a bulk drain, not a UI page.</summary>
    private const int PageSize = 100;

    /// <summary>Stops a runaway loop if the server ever reports has_more forever.</summary>
    private const int MaxPages = 200;

    private readonly HttpClient _http;

    public ArgoApiClient(HttpClient http) => _http = http;

    // -----------------------------------------------------------------------
    // Control plane
    // -----------------------------------------------------------------------

    /// <summary>The label this app gives its own key, and how it recognises one later.</summary>
    public const string DesktopKeyLabel = "Argo Books desktop";

    /// <summary>
    /// Register the company and return its account id.
    ///
    /// Idempotent: calling it again returns the same account rather than making
    /// a second one. Deliberately does NOT mint a key, because minting is the
    /// one part that is not idempotent and callers were previously getting a
    /// spare key every time somebody pressed the button twice.
    /// </summary>
    public async Task<string> EnsureAccountAsync(
        string companyUid,
        string displayName,
        string licenseKey,
        string deviceId,
        CancellationToken ct = default)
    {
        var account = await SendControlAsync(
            HttpMethod.Post,
            "/account",
            new { company_uid = companyUid, display_name = displayName },
            licenseKey, deviceId, ct);

        return account.RootElement.GetProperty("account_id").GetString()
               ?? throw new ArgoApiException("no_account", "The server did not return an account id.");
    }

    /// <summary>
    /// Mint the key this app uses for itself, separate from any key the merchant
    /// hands to a developer, so revoking a developer never locks the app out of
    /// its own review queue.
    ///
    /// Call only when no desktop key is stored. Every call creates another one.
    /// </summary>
    public async Task<string> CreateDesktopKeyAsync(
        string companyUid,
        string licenseKey,
        string deviceId,
        CancellationToken ct = default)
    {
        var key = await SendControlAsync(
            HttpMethod.Post,
            "/keys",
            new { company_uid = companyUid, label = DesktopKeyLabel, scopes = new[] { "read", "write" } },
            licenseKey, deviceId, ct);

        return key.RootElement.GetProperty("secret").GetString()
               ?? throw new ArgoApiException("no_secret", "The server did not return a key.");
    }

    /// <summary>Mint a key for a developer. The secret is returned once and never again.</summary>
    public async Task<(string Secret, string Hint)> CreateDeveloperKeyAsync(
        string companyUid,
        string label,
        bool allowWrite,
        string licenseKey,
        string deviceId,
        CancellationToken ct = default)
    {
        var scopes = allowWrite ? new[] { "read", "write" } : new[] { "read" };
        var doc = await SendControlAsync(
            HttpMethod.Post, "/keys",
            new { company_uid = companyUid, label, scopes },
            licenseKey, deviceId, ct);

        return (
            doc.RootElement.GetProperty("secret").GetString() ?? string.Empty,
            doc.RootElement.GetProperty("hint").GetString() ?? string.Empty);
    }

    /// <summary>List the company's keys. Hints only: the secrets are not recoverable.</summary>
    public async Task<JsonDocument> ListKeysAsync(
        string companyUid, string licenseKey, string deviceId, CancellationToken ct = default)
        => await SendControlAsync(HttpMethod.Get, $"/keys?company_uid={Uri.EscapeDataString(companyUid)}", null, licenseKey, deviceId, ct);

    /// <summary>Revoke a key immediately.</summary>
    public async Task RevokeKeyAsync(
        string companyUid, string keyId, string licenseKey, string deviceId, CancellationToken ct = default)
        => await SendControlAsync(HttpMethod.Post, "/keys/revoke", new { company_uid = companyUid, key_id = keyId }, licenseKey, deviceId, ct);

    /// <summary>
    /// Rename a key. Only the label changes: the secret already handed to an
    /// integration keeps working, so this is safe to do at any time.
    /// </summary>
    public async Task RenameKeyAsync(
        string companyUid, string keyId, string label, string licenseKey, string deviceId, CancellationToken ct = default)
        => await SendControlAsync(
            HttpMethod.Post, "/keys/rename",
            new { company_uid = companyUid, key_id = keyId, label },
            licenseKey, deviceId, ct);

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    public async Task<ArgoAccount?> GetAccountAsync(string key, CancellationToken ct = default)
        => await SendV1Async<ArgoAccount>(HttpMethod.Get, "/account", key, null, null, ct);

    /// <summary>
    /// Every pending object of one type, following the cursor to the end.
    ///
    /// Pages by starting_after rather than an offset because the queue is live:
    /// a developer pushing while the desktop drains would make an offset skip
    /// rows, and a skipped expense is one that silently never reaches the books.
    /// </summary>
    public async Task<List<T>> ListPendingAsync<T>(
        string key, string resource, bool expandLineItems = false, CancellationToken ct = default)
    {
        var all = new List<T>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var path = $"/{resource}?import_status=pending&limit={PageSize}";
            if (expandLineItems)
                path += "&expand%5B%5D=line_items";
            if (cursor != null)
                path += $"&starting_after={Uri.EscapeDataString(cursor)}";

            var list = await SendV1Async<ArgoList<T>>(HttpMethod.Get, path, key, null, null, ct);
            if (list == null || list.Data.Count == 0)
                break;

            all.AddRange(list.Data);
            if (!list.HasMore)
                break;

            cursor = IdOf(list.Data[^1]);
            if (cursor == null)
                break;
        }

        return all;
    }

    /// <summary>
    /// Retrieve one object by id, whatever its type, as raw JSON.
    ///
    /// Used to resolve a reference to something imported in an earlier batch.
    /// Such an object is no longer pending, so it is absent from the preview,
    /// and without this the link from the new record to it would be lost.
    /// Returns null when the object cannot be read.
    /// </summary>
    public async Task<JsonElement?> GetRawObjectAsync(
        string key, string resource, string id, CancellationToken ct = default)
    {
        try
        {
            var doc = await SendV1Async<JsonElement>(HttpMethod.Get, $"/{resource}/{id}", key, null, null, ct);
            return doc;
        }
        catch (ArgoApiException)
        {
            // A reference we cannot read is not worth failing the whole import
            // over. The caller falls back to matching on the natural key.
            return null;
        }
    }

    /// <summary>The URL segment an id belongs to, or null if the prefix is unknown.</summary>
    public static string? ResourceForId(string id)
    {
        // Ordered longest-first: "rev_" has to be tested before "re_", or every
        // revenue id would be mistaken for a refund.
        foreach (var (prefix, resource) in new[]
                 {
                     ("cus_", "customers"), ("sup_", "suppliers"), ("cat_", "categories"),
                     ("prd_", "products"), ("exp_", "expenses"), ("rev_", "revenue"),
                     ("re_", "refunds"),
                 })
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal))
                return resource;
        }
        return null;
    }

    /// <summary>
    /// Claim the listed objects as imported, in one server-side transaction.
    /// <paramref name="localRefs"/> maps an API id to the id this company gave the
    /// object locally, so a developer can see where their data ended up.
    /// </summary>
    /// <param name="idempotencyKey">
    /// The server requires one and answers a repeated key with what it answered the first time,
    /// for a day. So a claim that is meant to happen again must not reuse a key.
    /// </param>
    public async Task<ArgoBatch?> CreateImportBatchAsync(
        string key,
        IReadOnlyList<string> objectIds,
        IReadOnlyDictionary<string, string> localRefs,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var body = new { objects = objectIds, local_refs = localRefs };
        return await SendV1Async<ArgoBatch>(HttpMethod.Post, "/import_batches", key, body, idempotencyKey, ct);
    }

    /// <summary>
    /// Release a batch's objects back to pending. Called when the merchant undoes
    /// the import, so the queue and the books do not disagree about what was taken.
    /// </summary>
    public async Task<ArgoBatch?> RevertImportBatchAsync(string key, string batchId, CancellationToken ct = default)
        => await SendV1Async<ArgoBatch>(HttpMethod.Post, $"/import_batches/{batchId}/revert", key, new { }, Guid.NewGuid().ToString("N"), ct);

    /// <summary>Mark one object as declined, so the developer learns it was seen and refused.</summary>
    public async Task RejectAsync(string key, string resource, string objectId, CancellationToken ct = default)
        => await SendV1Async<JsonElement?>(HttpMethod.Post, $"/{resource}/{objectId}/reject", key, new { }, null, ct);

    // -----------------------------------------------------------------------

    /// <summary>Read the `id` off a deserialized wire record without reflection per call site.</summary>
    private static string? IdOf<T>(T item) => item switch
    {
        ArgoCustomer c => c.Id,
        ArgoSupplier s => s.Id,
        ArgoCategory c => c.Id,
        ArgoProduct p => p.Id,
        ArgoExpense e => e.Id,
        ArgoRevenue r => r.Id,
        ArgoRefund r => r.Id,
        _ => null
    };

    private async Task<JsonDocument> SendControlAsync(
        HttpMethod method, string path, object? body, string licenseKey, string deviceId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, ControlBase + path);

        // Premium sends the licence key, free sends the device id. The server
        // accepts either, matching how api/sync identifies a desktop.
        if (!string.IsNullOrWhiteSpace(licenseKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", licenseKey);
        if (!string.IsNullOrWhiteSpace(deviceId))
            req.Headers.TryAddWithoutValidation("X-Device-Id", deviceId);

        if (body != null)
            req.Content = JsonContent.Create(body);

        using var resp = await SendWithFriendlyErrorsAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new ArgoApiException("control_plane_error", ControlErrorMessage(json, resp.StatusCode));

        return JsonDocument.Parse(json);
    }

    private async Task<T?> SendV1Async<T>(
        HttpMethod method, string path, string key, object? body, string? idempotencyKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, V1Base + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        if (idempotencyKey != null)
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        if (body != null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await SendWithFriendlyErrorsAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var error = TryParseError(json);
            throw new ArgoApiException(
                error?.Code ?? "http_" + (int)resp.StatusCode,
                error?.Message ?? $"The Argo Books API returned HTTP {(int)resp.StatusCode}.");
        }

        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json);
    }

    private async Task<HttpResponseMessage> SendWithFriendlyErrorsAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(req, ct);
        }
        catch (HttpRequestException)
        {
            throw new ArgoApiException("network", "Could not reach argorobots.com. Check your internet connection and try again.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ArgoApiException("timeout", "The request to argorobots.com timed out. Try again in a moment.");
        }
    }

    private static ArgoApiError? TryParseError(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ArgoErrorEnvelope>(json)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The control plane uses the portal's older envelope, not the /v1 one.</summary>
    private static string ControlErrorMessage(string json, HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var message))
                return message.GetString() ?? $"HTTP {(int)status}.";
        }
        catch (JsonException)
        {
            // Fall through to the status code.
        }
        return $"The server returned HTTP {(int)status}.";
    }
}
