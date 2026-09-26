using System.Net;
using System.Text;
using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// A monthly limit the server counts per license key or device: the endpoint that counts it and
/// the names its answer gives the "may go ahead" flag and the count.
/// </summary>
public sealed record UsageLimit(string Name, string Endpoint, string AllowedField, string CountField, string? Type = null)
{
    public static UsageLimit ReceiptScans { get; } = new("Receipt scan", "/api/receipt/usage.php", "can_scan", "scan_count");

    public static UsageLimit InvoiceSends { get; } = new("Invoice send", "/api/invoice/usage.php", "can_send", "send_count");

    /// <param name="type">"spreadsheet" or "bank": which monthly counter the server uses.</param>
    public static UsageLimit AiImports(string type = "spreadsheet") =>
        new("AI import", "/api/ai-import/usage.php", "can_import", "import_count", type);
}

/// <summary>
/// Checks and counts one server-side monthly limit (receipt scans, AI imports, invoice sends).
/// Every limit follows the same policy; see docs/LicenseKey.md "Usage limits".
/// </summary>
public class UsageLimitService : IUsageLimitService
{
    public const string NoIdentityMessage = "No license key or device ID found.";
    public const string CancelledMessage = "Request was cancelled.";
    public const string UnverifiedLicenseMessage = "Your license key couldn't be verified. If your subscription has ended, restart Argo Books to continue on the free plan.";
    public const string RateLimitedMessage = "Too many requests. Please try again in a few minutes.";
    public const string RefusedMessage = "The server refused the monthly limit check. Please try again, or contact support if this keeps happening.";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly UsageLimit _limit;
    private readonly Func<(string? LicenseKey, string? DeviceId)> _identity;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IConnectivityService _connectivityService;
    private readonly IErrorLogger? _errorLogger;
    private bool _disposed;

    private UsageCheckResult? _cached;
    private DateTime _cacheExpiry = DateTime.MinValue;

    public UsageLimitService(UsageLimit limit, LicenseService? licenseService = null, IErrorLogger? errorLogger = null)
        : this(limit, () => (licenseService?.GetLicenseKey(), licenseService?.GetDeviceId()),
            new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, new ConnectivityService(), errorLogger)
    {
        _ownsHttpClient = true;
    }

    /// <summary>Creates an instance with its dependencies given (for testing).</summary>
    public UsageLimitService(UsageLimit limit, Func<(string? LicenseKey, string? DeviceId)> identity,
        HttpClient httpClient, IConnectivityService connectivityService, IErrorLogger? errorLogger = null)
    {
        _limit = limit;
        _identity = identity;
        _httpClient = httpClient;
        _connectivityService = connectivityService;
        _errorLogger = errorLogger;
    }

    /// <summary>
    /// Whether one more may go ahead. A blocked result with no <see cref="UsageCheckResult.ErrorMessage"/>
    /// means the limit is reached; with one, the check couldn't be made or the server refused it.
    /// </summary>
    public async Task<UsageCheckResult> CheckUsageAsync(CancellationToken cancellationToken = default)
    {
        var (licenseKey, deviceId) = _identity();
        if (string.IsNullOrEmpty(licenseKey) && string.IsNullOrEmpty(deviceId))
            return new UsageCheckResult { ErrorMessage = NoIdentityMessage, IsOffline = true };

        if (_cached != null && DateTime.UtcNow < _cacheExpiry)
            return _cached;

        HttpStatusCode status;
        UsageAnswer? answer;
        try
        {
            (status, answer) = await CallApiAsync("check", licenseKey, deviceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new UsageCheckResult { ErrorMessage = CancelledMessage, IsOffline = true };
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            // Without internet the feature's own call can't run either, so say so now. With
            // internet only the usage server hiccuped, and the feature's own call will fail
            // anyway if the server is truly down.
            var message = await NetworkFailure.ResolveAndReportAsync(
                _errorLogger, ex, $"{_limit.Name} usage check failed", _connectivityService);
            return message == ConnectivityMessage.NoInternet
                ? new UsageCheckResult { ErrorMessage = message, IsOffline = true }
                : AllowedUncounted();
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Api, $"{_limit.Name} usage check got an unreadable answer, allowing");
            return AllowedUncounted();
        }

        // A reached limit can come back as a failure carrying the counts; it is still an answer.
        if (answer != null && (answer.Success || (!answer.Allowed && answer.MonthlyLimit > 0)))
        {
            var result = new UsageCheckResult
            {
                Allowed = answer.Allowed,
                Used = answer.Count,
                MonthlyLimit = answer.MonthlyLimit,
                Remaining = answer.Remaining,
                Tier = answer.Tier,
                ResetsAt = answer.ResetsAt
            };
            if (answer.Success)
                Cache(result);
            return result;
        }

        // Only our endpoint saying no blocks: a 4xx whose JSON says why. Anything else (a 5xx, a
        // body that isn't our JSON such as a hosting-layer error page, or a failure without counts)
        // is the usage server being unavailable, and the feature's own call decides.
        var code = (int)status;
        if (code is >= 400 and < 500 && answer is { Refuses: true })
        {
            _errorLogger?.LogWarning($"{_limit.Name} usage check refused (HTTP {code}): {answer.Error ?? answer.ErrorCode}", category: ErrorCategory.Api);
            return new UsageCheckResult { ErrorMessage = RefusalMessage(status), IsOffline = true };
        }

        _errorLogger?.LogError(new Exception(answer?.Error ?? $"HTTP {code}, not an answer from the usage endpoint"), ErrorCategory.Api,
            $"{_limit.Name} usage server unavailable, allowing");
        return AllowedUncounted();
    }

    private static string RefusalMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => UnverifiedLicenseMessage,
        HttpStatusCode.TooManyRequests => RateLimitedMessage,
        _ => RefusedMessage
    };

    /// <summary>
    /// Counts one after it has gone through. The work is done by then, so a network failure is
    /// reported as success-offline rather than holding the user up.
    /// </summary>
    public async Task<UsageIncrementResult> IncrementUsageAsync(CancellationToken cancellationToken = default)
    {
        var (licenseKey, deviceId) = _identity();
        if (string.IsNullOrEmpty(licenseKey) && string.IsNullOrEmpty(deviceId))
            return new UsageIncrementResult { ErrorMessage = NoIdentityMessage };

        UsageAnswer? answer;
        try
        {
            (_, answer) = await CallApiAsync("increment", licenseKey, deviceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new UsageIncrementResult { ErrorMessage = CancelledMessage };
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            NetworkFailure.Report(_errorLogger, ex, $"{_limit.Name} usage increment failed", ErrorCategory.Api);
            return new UsageIncrementResult { Success = true, IsOffline = true };
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Api, $"{_limit.Name} usage increment got an unreadable answer");
            return new UsageIncrementResult { ErrorMessage = "Unable to record usage." };
        }

        if (answer == null)
        {
            _errorLogger?.LogError($"{_limit.Name} usage increment got an unreadable answer", ErrorCategory.Api);
            return new UsageIncrementResult { ErrorMessage = "Unable to record usage." };
        }

        if (!answer.Success)
            return new UsageIncrementResult { ErrorMessage = answer.Error ?? "Failed to record usage." };

        Cache(new UsageCheckResult
        {
            Allowed = answer.HasAllowed ? answer.Allowed : answer.MonthlyLimit < 0 || answer.Remaining > 0,
            Used = answer.Count,
            MonthlyLimit = answer.MonthlyLimit,
            Remaining = answer.Remaining,
            Tier = answer.Tier,
            ResetsAt = answer.ResetsAt
        });
        return new UsageIncrementResult
        {
            Success = true,
            Used = answer.Count,
            MonthlyLimit = answer.MonthlyLimit,
            Remaining = answer.Remaining
        };
    }

    public void InvalidateCache()
    {
        _cached = null;
        _cacheExpiry = DateTime.MinValue;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing && _ownsHttpClient)
            _httpClient.Dispose();
        _disposed = true;
    }

    private static UsageCheckResult AllowedUncounted() => new() { Allowed = true, IsOffline = true };

    private void Cache(UsageCheckResult result)
    {
        _cached = result;
        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
    }

    /// <summary>The request never got an answer: no connection, the server unreachable, or a timeout.</summary>
    private static bool IsTransportFailure(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    /// <summary>The status and the parsed answer, or a null answer when the body isn't a JSON object.</summary>
    private async Task<(HttpStatusCode Status, UsageAnswer? Answer)> CallApiAsync(string action, string? licenseKey, string? deviceId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string>
        {
            ["license_key"] = licenseKey ?? "",
            ["device_id"] = deviceId ?? "",
            ["action"] = action
        };
        if (_limit.Type != null)
            body["type"] = _limit.Type;

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync($"{ApiConfig.BaseUrl}{_limit.Endpoint}", content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response.StatusCode, UsageAnswer.TryParse(json, _limit));
    }

    private sealed record UsageAnswer(
        bool Success, bool Allowed, bool HasAllowed, int Count, int MonthlyLimit, int Remaining,
        string? Tier, string? ResetsAt, string? Error, string? ErrorCode)
    {
        /// <summary>The body says why it said no, which only our endpoint's own refusals do.</summary>
        public bool Refuses => Error != null || ErrorCode != null;

        public static UsageAnswer? TryParse(string json, UsageLimit limit)
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(json); }
            catch (JsonException) { return null; }

            using var owned = document;
            var root = owned.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            bool Bool(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
            int Int(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
            string? Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            return new UsageAnswer(
                Bool("success"),
                Bool(limit.AllowedField),
                root.TryGetProperty(limit.AllowedField, out _),
                Int(limit.CountField),
                Int("monthly_limit"),
                Int("remaining"),
                Text("tier"),
                Text("resets_at"),
                Text("error") ?? Text("message"),
                Text("errorCode"));
        }
    }
}

/// <summary>A server-counted monthly limit, as the screens that enforce one use it.</summary>
public interface IUsageLimitService : IDisposable
{
    Task<UsageCheckResult> CheckUsageAsync(CancellationToken cancellationToken = default);

    Task<UsageIncrementResult> IncrementUsageAsync(CancellationToken cancellationToken = default);

    void InvalidateCache();
}

/// <summary>The answer to whether one more scan, import or send may go ahead.</summary>
public class UsageCheckResult
{
    public bool Allowed { get; init; }

    /// <summary>Set when the check couldn't be made; unset on a block means the limit is reached.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Used this month.</summary>
    public int Used { get; init; }

    /// <summary>The plan's monthly limit; -1 for unlimited.</summary>
    public int MonthlyLimit { get; init; }

    public int Remaining { get; init; }

    public string? Tier { get; init; }

    /// <summary>When the count resets (the first of next month).</summary>
    public string? ResetsAt { get; init; }

    /// <summary>The server's count wasn't had, so the counts above are not real.</summary>
    public bool IsOffline { get; init; }
}

/// <summary>The result of counting one scan, import or send.</summary>
public class UsageIncrementResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int Used { get; init; }
    public int MonthlyLimit { get; init; }
    public int Remaining { get; init; }

    /// <summary>The server couldn't be reached, so this one went uncounted.</summary>
    public bool IsOffline { get; init; }
}
