using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArgoBooks.Core.Models.Portal;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Client for the Argo Books portal refund API. Wraps the server's
/// /api/portal/refunds/ and /api/portal/account/ endpoints.
///
/// All POSTs send an Idempotency-Key header so retries are safe.
/// Authentication uses the per-company API key from <see cref="PortalSettings"/>.
/// Reads the API key at call time so a key rotation propagates without
/// re-instantiation.
/// </summary>
public class RefundService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public RefundService(HttpClient http)
    {
        _http = http;
    }

    private static string BaseRoot => ApiConfig.BaseUrl.TrimEnd('/');
    private static string? CurrentApiKey => PortalSettings.IsConfigured ? PortalSettings.ApiKey : null;

    /// <summary>
    /// Begin a refund. Server creates a refund_request, emails a 6-digit code
    /// to the company's locked owner email, and returns the request ID.
    /// </summary>
    public async Task<RefundRequestResult> RequestRefundAsync(RefundDraft draft, CancellationToken ct = default)
    {
        var req = BuildRequest(HttpMethod.Post, "/api/portal/refunds/request.php", new
        {
            invoice_id = draft.InvoiceId,
            invoice_number = draft.InvoiceNumber,
            customer_name = draft.CustomerName,
            provider = draft.Provider,
            provider_payment_id = draft.ProviderPaymentId,
            amount_cents = draft.AmountCents,
            currency = draft.Currency,
            line_items = draft.LineItems,
            reason = draft.Reason,
        });

        return await SendAsync<RefundRequestResult>(req, ct);
    }

    /// <summary>
    /// Submit the 6-digit code. Server validates, runs velocity check, transitions
    /// state to processing (or cooling_off / failed) and returns the result.
    /// </summary>
    public async Task<RefundConfirmResult> ConfirmCodeAsync(long requestId, string code, CancellationToken ct = default)
    {
        var req = BuildRequest(HttpMethod.Post, "/api/portal/refunds/confirm.php", new
        {
            request_id = requestId,
            code = code,
        });
        return await SendAsync<RefundConfirmResult>(req, ct);
    }

    /// <summary>
    /// Poll the current state of a refund_request. Used during cooling_off /
    /// processing to keep the UI in sync.
    /// </summary>
    public async Task<RefundStatusResult> GetStatusAsync(long requestId, CancellationToken ct = default)
    {
        var req = BuildRequest(HttpMethod.Get, $"/api/portal/refunds/status.php?id={requestId}", body: null);
        return await SendAsync<RefundStatusResult>(req, ct);
    }

    /// <summary>
    /// Cancel a refund_request that hasn't yet entered processing.
    /// </summary>
    public async Task<RefundConfirmResult> CancelAsync(long requestId, CancellationToken ct = default)
    {
        var req = BuildRequest(HttpMethod.Post, "/api/portal/refunds/cancel.php", new { request_id = requestId });
        return await SendAsync<RefundConfirmResult>(req, ct);
    }

    /// <summary>
    /// Re-send the 6-digit verification code (max 3 per request, 1 per 60s).
    /// </summary>
    public async Task<ApiResult> ResendCodeAsync(long requestId, CancellationToken ct = default)
    {
        var req = BuildRequest(HttpMethod.Post, "/api/portal/refunds/resend-code.php", new { request_id = requestId });
        return await SendAsync<ApiResult>(req, ct);
    }

    // ----- Account email verification + change -----

    public async Task<ApiResult> RequestRegistrationCodeAsync(CancellationToken ct = default)
        => await SendAsync<ApiResult>(BuildRequest(HttpMethod.Post, "/api/portal/account/verify-email/request.php", body: null), ct);

    public async Task<ApiResult> ConfirmRegistrationCodeAsync(string code, CancellationToken ct = default)
        => await SendAsync<ApiResult>(BuildRequest(HttpMethod.Post, "/api/portal/account/verify-email/confirm.php", new { code }), ct);

    /// <summary>
    /// First-time setup of owner_email when the portal company row has none.
    /// Server returns 409 OWNER_EMAIL_ALREADY_SET if there's already an
    /// email on file (user must use the Change flow in that case).
    /// On success the server stores owner_email AND emails a verification
    /// code to it; email_verified_at is NOT set until the caller confirms
    /// that code via /verify-email/confirm.php.
    /// </summary>
    public async Task<SetInitialOwnerEmailResult> SetInitialOwnerEmailAsync(string email, CancellationToken ct = default)
        => await SendAsync<SetInitialOwnerEmailResult>(BuildRequest(HttpMethod.Post, "/api/portal/account/set-initial-email.php", new { email }), ct);

    public async Task<EmailChangeRequestResult> RequestEmailChangeAsync(string newEmail, bool passwordVerified, CancellationToken ct = default)
        => await SendAsync<EmailChangeRequestResult>(
            BuildRequest(HttpMethod.Post, "/api/portal/account/email-change/request.php", new { new_email = newEmail, password_verified = passwordVerified }), ct);

    public async Task<EmailChangeStateResult> ConfirmEmailChangeOldAsync(long changeId, string code, CancellationToken ct = default)
        => await SendAsync<EmailChangeStateResult>(
            BuildRequest(HttpMethod.Post, "/api/portal/account/email-change/confirm-old.php", new { change_id = changeId, code }), ct);

    public async Task<EmailChangeStateResult> ConfirmEmailChangeNewAsync(long changeId, string code, CancellationToken ct = default)
        => await SendAsync<EmailChangeStateResult>(
            BuildRequest(HttpMethod.Post, "/api/portal/account/email-change/confirm-new.php", new { change_id = changeId, code }), ct);

    public async Task<ApiResult> CancelEmailChangeAsync(long changeId, CancellationToken ct = default)
        => await SendAsync<ApiResult>(BuildRequest(HttpMethod.Post, "/api/portal/account/email-change/cancel.php", new { change_id = changeId }), ct);

    public async Task<ApiResult> ResendEmailChangeCodeAsync(long changeId, string target, CancellationToken ct = default)
        => await SendAsync<ApiResult>(BuildRequest(HttpMethod.Post, "/api/portal/account/email-change/resend-code.php", new { change_id = changeId, target }), ct);

    // ----- helpers -----

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body)
    {
        var url = BaseRoot + path;
        var req = new HttpRequestMessage(method, url);
        var apiKey = CurrentApiKey;
        if (!string.IsNullOrEmpty(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        if (method == HttpMethod.Post)
        {
            req.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
            // Send "{}" (empty JSON object) rather than "" for body-less POSTs.
            // The Content-Type is application/json, and an empty string is not
            // valid JSON: endpoints that unconditionally json_decode the body
            // can choke on it. An empty object decodes cleanly to {} / null.
            req.Content = body is null
                ? new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                : JsonContent.Create(body, options: s_json);
        }
        return req;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct) where T : ApiResult, new()
    {
        try
        {
            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
            {
                return new T { Ok = res.IsSuccessStatusCode, HttpStatus = (int)res.StatusCode };
            }
            T parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<T>(body, s_json) ?? new T();
            }
            catch (JsonException)
            {
                parsed = new T { ErrorCode = "RESPONSE_PARSE_ERROR", Message = body };
            }
            parsed.HttpStatus = (int)res.StatusCode;
            parsed.Ok = res.IsSuccessStatusCode && parsed.Success;
            return parsed;
        }
        catch (HttpRequestException)
        {
            return new T { ErrorCode = "NETWORK_ERROR", Message = await ConnectivityMessage.ResolveAsync(), HttpStatus = (int)HttpStatusCode.ServiceUnavailable };
        }
        catch (TaskCanceledException)
        {
            return new T { ErrorCode = "TIMEOUT", Message = "Request timed out.", HttpStatus = (int)HttpStatusCode.RequestTimeout };
        }
    }
}

// --- Request / response shapes ---

public record RefundDraft(
    string InvoiceId,
    string InvoiceNumber,
    string? CustomerName,
    string Provider,           // "stripe" | "paypal" | "square"
    string ProviderPaymentId,
    long AmountCents,
    string Currency,
    IReadOnlyList<object>? LineItems,
    string? Reason
);

public class ApiResult
{
    [JsonPropertyName("success")] public bool Success { get; set; }

    // The portal API has two error-code field shapes in flight: refund-flow endpoints return `error`; the older `send_error_response()` helper returns `errorCode`.
    [JsonPropertyName("error")] public string? ErrorCode { get; set; }
    [JsonPropertyName("errorCode")]
    public string? ErrorCodeCamelCase
    {
        get => null;
        set { if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(ErrorCode)) ErrorCode = value; }
    }

    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonIgnore] public int HttpStatus { get; set; }
    [JsonIgnore] public bool Ok { get; set; }
}

public class RefundRequestResult : ApiResult
{
    [JsonPropertyName("requestId")] public long RequestId { get; set; }
    [JsonPropertyName("expiresInSeconds")] public int ExpiresInSeconds { get; set; }
    [JsonPropertyName("maskedEmail")] public string? MaskedEmail { get; set; }
}

public class RefundConfirmResult : ApiResult
{
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("velocityTier")] public string? VelocityTier { get; set; }
    [JsonPropertyName("coolingOffSeconds")] public int? CoolingOffSeconds { get; set; }
    [JsonPropertyName("providerRefundId")] public string? ProviderRefundId { get; set; }
    [JsonPropertyName("attemptsRemaining")] public int? AttemptsRemaining { get; set; }
}

public class RefundStatusResult : ApiResult
{
    [JsonPropertyName("requestId")] public long RequestId { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("stateReason")] public string? StateReason { get; set; }
    [JsonPropertyName("velocityTier")] public string? VelocityTier { get; set; }
    [JsonPropertyName("coolingOffUntil")] public string? CoolingOffUntil { get; set; }
    [JsonPropertyName("providerRefundId")] public string? ProviderRefundId { get; set; }
    [JsonPropertyName("completedAt")] public string? CompletedAt { get; set; }
}

public class EmailChangeRequestResult : ApiResult
{
    [JsonPropertyName("changeId")] public long ChangeId { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("maskedOldEmail")] public string? MaskedOldEmail { get; set; }

    /// <summary>
    /// False when the current address was never verified, so no code was sent to it and one
    /// has already gone to the new address. The old-code step is skipped entirely.
    /// </summary>
    [JsonPropertyName("oldEmailVerificationRequired")] public bool OldEmailVerificationRequired { get; set; } = true;

    /// <summary>Present only when the old step was skipped, since the new code is already out.</summary>
    [JsonPropertyName("maskedNewEmail")] public string? MaskedNewEmail { get; set; }
}

public class EmailChangeStateResult : ApiResult
{
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("maskedNewEmail")] public string? MaskedNewEmail { get; set; }
    [JsonPropertyName("newEmail")] public string? NewEmail { get; set; }
}

public class SetInitialOwnerEmailResult : ApiResult
{
    [JsonPropertyName("ownerEmail")] public string? OwnerEmail { get; set; }
    [JsonPropertyName("maskedEmail")] public string? MaskedEmail { get; set; }
}
