using System.Text;
using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for tracking and enforcing AI import usage limits via server-side API.
/// Communicates with the server API to track usage per license key.
/// </summary>
public class AiImportUsageService : IDisposable
{
    private static readonly string UsageApiUrl = $"{ApiConfig.BaseUrl}/api/ai-import/usage.php";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly LicenseService? _licenseService;
    private readonly IConnectivityService _connectivityService;
    private readonly IErrorLogger? _errorLogger;
    private readonly string _importType;
    private bool _disposed;

    // Cache the last known usage to reduce API calls
    private AiImportUsageStatus? _cachedUsage;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates a new instance of the AiImportUsageService.
    /// </summary>
    /// <param name="importType">"spreadsheet" (default) or "bank" - selects which monthly counter to use.</param>
    public AiImportUsageService(LicenseService? licenseService = null, IErrorLogger? errorLogger = null, string importType = "spreadsheet")
        : this(licenseService, new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, new ConnectivityService(), errorLogger)
    {
        _ownsHttpClient = true;
        _importType = importType;
    }

    /// <summary>
    /// Creates a new instance with custom dependencies (for testing).
    /// </summary>
    public AiImportUsageService(LicenseService? licenseService, HttpClient httpClient, IConnectivityService connectivityService, IErrorLogger? errorLogger = null, string importType = "spreadsheet")
    {
        _licenseService = licenseService;
        _httpClient = httpClient;
        _connectivityService = connectivityService;
        _errorLogger = errorLogger;
        _importType = importType;
    }

    public async Task<AiImportCheckResult> CheckUsageAsync(CancellationToken cancellationToken = default)
    {
        var licenseKey = _licenseService?.GetLicenseKey() ?? "";
        var deviceId = _licenseService?.GetDeviceId() ?? "";
        if (string.IsNullOrEmpty(licenseKey) && string.IsNullOrEmpty(deviceId))
        {
            return new AiImportCheckResult
            {
                CanImport = false,
                ErrorMessage = "No license key or device ID found.",
                ImportCount = 0,
                MonthlyLimit = 0,
                Remaining = 0
            };
        }

        // Check cache first
        if (_cachedUsage != null && DateTime.UtcNow < _cacheExpiry)
        {
            return new AiImportCheckResult
            {
                CanImport = _cachedUsage.CanImport,
                ImportCount = _cachedUsage.ImportCount,
                MonthlyLimit = _cachedUsage.MonthlyLimit,
                Remaining = _cachedUsage.Remaining,
                Tier = _cachedUsage.Tier,
                ResetsAt = _cachedUsage.ResetsAt
            };
        }

        try
        {
            var response = await CallApiAsync("check", licenseKey, cancellationToken);

            if (response.Success)
            {
                // Update cache
                _cachedUsage = new AiImportUsageStatus
                {
                    CanImport = response.CanImport,
                    ImportCount = response.ImportCount,
                    MonthlyLimit = response.MonthlyLimit,
                    Remaining = response.Remaining,
                    Tier = response.Tier,
                    ResetsAt = response.ResetsAt
                };
                _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);

                return new AiImportCheckResult
                {
                    CanImport = response.CanImport,
                    ImportCount = response.ImportCount,
                    MonthlyLimit = response.MonthlyLimit,
                    Remaining = response.Remaining,
                    Tier = response.Tier,
                    ResetsAt = response.ResetsAt
                };
            }

            // Server error (e.g. database outage), don't block the user.
            // Only block if the server explicitly says the user has exceeded their quota.
            var isQuotaExceeded = response.CanImport == false && response.MonthlyLimit > 0;
            if (isQuotaExceeded)
            {
                return new AiImportCheckResult
                {
                    CanImport = false,
                    ImportCount = response.ImportCount,
                    MonthlyLimit = response.MonthlyLimit,
                    Remaining = response.Remaining,
                    Tier = response.Tier,
                    ResetsAt = response.ResetsAt
                };
            }

            // Server-side error, allow import to proceed gracefully
            _errorLogger?.LogError(new Exception(response.Error ?? "Unknown API error"), ErrorCategory.Api, "AI import usage check returned server error, allowing import");
            return new AiImportCheckResult
            {
                CanImport = true,
                ErrorMessage = null
            };
        }
        catch (HttpRequestException)
        {
            // Allow the import only if the usage server hiccuped but we still have internet
            // (a fresh cache shows capacity). When fully offline the AI call can't run, so
            // report the connectivity problem now instead of letting it fail mid-import.
            if (_cachedUsage != null && _cachedUsage.CanImport && DateTime.UtcNow < _cacheExpiry
                && await _connectivityService.IsInternetAvailableAsync(cancellationToken))
            {
                return new AiImportCheckResult
                {
                    CanImport = true,
                    ImportCount = _cachedUsage.ImportCount,
                    MonthlyLimit = _cachedUsage.MonthlyLimit,
                    Remaining = _cachedUsage.Remaining,
                    Tier = _cachedUsage.Tier,
                    ResetsAt = _cachedUsage.ResetsAt,
                    IsOffline = true
                };
            }

            var errorMessage = await ConnectivityMessage.ResolveAsync(_connectivityService, cancellationToken);
            return new AiImportCheckResult
            {
                CanImport = false,
                ErrorMessage = errorMessage
            };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !cancellationToken.IsCancellationRequested)
        {
            var errorMessage = await ConnectivityMessage.ResolveAsync(_connectivityService, cancellationToken);
            return new AiImportCheckResult
            {
                CanImport = false,
                ErrorMessage = errorMessage
            };
        }
        catch (TaskCanceledException)
        {
            return new AiImportCheckResult
            {
                CanImport = false,
                ErrorMessage = "Request was cancelled."
            };
        }
        catch (Exception ex)
        {
            NetworkFailure.Report(_errorLogger, ex, "AI import usage check failed", ErrorCategory.Api);
            return new AiImportCheckResult
            {
                CanImport = false,
                ErrorMessage = $"Error checking usage: {ex.Message}"
            };
        }
    }

    public async Task<AiImportIncrementResult> IncrementUsageAsync(CancellationToken cancellationToken = default)
    {
        var licenseKey = _licenseService?.GetLicenseKey() ?? "";
        var deviceId = _licenseService?.GetDeviceId() ?? "";
        if (string.IsNullOrEmpty(licenseKey) && string.IsNullOrEmpty(deviceId))
        {
            return new AiImportIncrementResult
            {
                Success = false,
                ErrorMessage = "No license key or device ID found"
            };
        }

        try
        {
            var response = await CallApiAsync("increment", licenseKey, cancellationToken);

            if (response.Success)
            {
                // Update cache with new values
                _cachedUsage = new AiImportUsageStatus
                {
                    CanImport = response.Remaining > 0,
                    ImportCount = response.ImportCount,
                    MonthlyLimit = response.MonthlyLimit,
                    Remaining = response.Remaining,
                    Tier = response.Tier,
                    ResetsAt = response.ResetsAt
                };
                _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);

                return new AiImportIncrementResult
                {
                    Success = true,
                    ImportCount = response.ImportCount,
                    MonthlyLimit = response.MonthlyLimit,
                    Remaining = response.Remaining
                };
            }

            return new AiImportIncrementResult
            {
                Success = false,
                ErrorMessage = response.Error ?? "Failed to record usage"
            };
        }
        catch (HttpRequestException)
        {
            // Network error - don't block the user
            return new AiImportIncrementResult
            {
                Success = true,
                IsOffline = true
            };
        }
        catch (Exception ex)
        {
            NetworkFailure.Report(_errorLogger, ex, "AI import usage increment failed", ErrorCategory.Api);
            return new AiImportIncrementResult
            {
                Success = false,
                ErrorMessage = $"Error recording usage: {ex.Message}"
            };
        }
    }

    public void InvalidateCache()
    {
        _cachedUsage = null;
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
        {
            _httpClient.Dispose();
        }
        _disposed = true;
    }

    private async Task<AiImportApiResponse> CallApiAsync(string action, string licenseKey, CancellationToken cancellationToken)
    {
        var deviceId = _licenseService?.GetDeviceId() ?? "";
        var requestBody = new
        {
            license_key = licenseKey,
            device_id = deviceId,
            action,
            type = _importType
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(UsageApiUrl, content, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        return JsonSerializer.Deserialize<AiImportApiResponse>(responseJson) ?? new AiImportApiResponse();
    }

    private class AiImportApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("can_import")]
        public bool CanImport { get; init; }

        [JsonPropertyName("import_count")]
        public int ImportCount { get; init; }

        [JsonPropertyName("monthly_limit")]
        public int MonthlyLimit { get; init; }

        [JsonPropertyName("remaining")]
        public int Remaining { get; init; }

        [JsonPropertyName("tier")]
        public string? Tier { get; init; }

        [JsonPropertyName("resets_at")]
        public string? ResetsAt { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }
}

/// <summary>
/// Result of checking AI import usage status.
/// </summary>
public class AiImportCheckResult
{
    public bool CanImport { get; init; }
    public string? ErrorMessage { get; init; }
    public int ImportCount { get; init; }
    public int MonthlyLimit { get; init; }
    public int Remaining { get; init; }
    public string? Tier { get; init; }
    public string? ResetsAt { get; init; }
    public bool IsOffline { get; init; }
}

/// <summary>
/// Result of incrementing AI import usage.
/// </summary>
public class AiImportIncrementResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int ImportCount { get; init; }
    public int MonthlyLimit { get; init; }
    public int Remaining { get; init; }
    public bool IsOffline { get; init; }
}

/// <summary>
/// Cached AI import usage status.
/// </summary>
public class AiImportUsageStatus
{
    public bool CanImport { get; init; }
    public int ImportCount { get; init; }
    public int MonthlyLimit { get; init; }
    public int Remaining { get; init; }
    public string? Tier { get; init; }
    public string? ResetsAt { get; init; }
}
