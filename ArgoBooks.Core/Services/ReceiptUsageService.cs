using System.Text;
using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for tracking and enforcing receipt scan usage limits.
/// Communicates with the server API to track usage per license key.
/// </summary>
public class ReceiptUsageService : IReceiptUsageService
{
    private static readonly string UsageApiUrl = $"{ApiConfig.BaseUrl}/api/receipt/usage.php";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly LicenseService? _licenseService;
    private readonly IConnectivityService _connectivityService;
    private readonly IErrorLogger? _errorLogger;
    private bool _disposed;

    // Cache the last known usage to reduce API calls
    private UsageStatus? _cachedUsage;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates a new instance of the ReceiptUsageService.
    /// </summary>
    public ReceiptUsageService(LicenseService? licenseService = null, IErrorLogger? errorLogger = null)
        : this(licenseService, new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, new ConnectivityService(), errorLogger)
    {
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a new instance with custom dependencies (for testing).
    /// </summary>
    public ReceiptUsageService(LicenseService? licenseService, HttpClient httpClient, IConnectivityService connectivityService, IErrorLogger? errorLogger = null)
    {
        _licenseService = licenseService;
        _httpClient = httpClient;
        _connectivityService = connectivityService;
        _errorLogger = errorLogger;
    }

    /// <inheritdoc />
    public async Task<UsageCheckResult> CheckUsageAsync(CancellationToken cancellationToken = default)
    {
        var licenseKey = _licenseService?.GetLicenseKey() ?? "";
        var deviceId = _licenseService?.GetDeviceId() ?? "";
        if (string.IsNullOrEmpty(licenseKey) && string.IsNullOrEmpty(deviceId))
        {
            return new UsageCheckResult
            {
                CanScan = false,
                ErrorMessage = "No license key or device ID found.",
                ScanCount = 0,
                MonthlyLimit = 0,
                Remaining = 0
            };
        }

        // Check cache first
        if (_cachedUsage != null && DateTime.UtcNow < _cacheExpiry)
        {
            return new UsageCheckResult
            {
                CanScan = _cachedUsage.CanScan,
                ScanCount = _cachedUsage.ScanCount,
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
                _cachedUsage = new UsageStatus
                {
                    CanScan = response.CanScan,
                    ScanCount = response.ScanCount,
                    MonthlyLimit = response.MonthlyLimit,
                    Remaining = response.Remaining,
                    Tier = response.Tier,
                    ResetsAt = response.ResetsAt
                };
                _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);

                return new UsageCheckResult
                {
                    CanScan = response.CanScan,
                    ScanCount = response.ScanCount,
                    MonthlyLimit = response.MonthlyLimit,
                    Remaining = response.Remaining,
                    Tier = response.Tier,
                    ResetsAt = response.ResetsAt
                };
            }

            return new UsageCheckResult
            {
                CanScan = false,
                ErrorMessage = response.Error ?? "Failed to check usage"
            };
        }
        catch (HttpRequestException)
        {
            // Allow the scan only if the usage server hiccuped but we still have internet
            // (a fresh cache shows capacity). When fully offline the scan API can't run, so
            // report the connectivity problem now instead of letting it fail mid-scan.
            if (_cachedUsage != null && _cachedUsage.CanScan && DateTime.UtcNow < _cacheExpiry
                && await _connectivityService.IsInternetAvailableAsync(cancellationToken))
            {
                return new UsageCheckResult
                {
                    CanScan = true,
                    ScanCount = _cachedUsage.ScanCount,
                    MonthlyLimit = _cachedUsage.MonthlyLimit,
                    Remaining = _cachedUsage.Remaining,
                    Tier = _cachedUsage.Tier,
                    ResetsAt = _cachedUsage.ResetsAt,
                    IsOffline = true
                };
            }

            // Check if it's a connectivity issue or server issue
            var errorMessage = await ConnectivityMessage.ResolveAsync(_connectivityService, cancellationToken);
            return new UsageCheckResult
            {
                CanScan = false,
                ErrorMessage = errorMessage
            };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !cancellationToken.IsCancellationRequested)
        {
            // Timeout - check connectivity to give better error message
            var errorMessage = await ConnectivityMessage.ResolveAsync(_connectivityService, cancellationToken);
            return new UsageCheckResult
            {
                CanScan = false,
                ErrorMessage = errorMessage
            };
        }
        catch (TaskCanceledException)
        {
            // User cancelled
            return new UsageCheckResult
            {
                CanScan = false,
                ErrorMessage = "Request was cancelled."
            };
        }
        catch (Exception ex)
        {
            NetworkFailure.Report(_errorLogger, ex, "Receipt usage check failed", ErrorCategory.Api);
            return new UsageCheckResult
            {
                CanScan = false,
                ErrorMessage = $"Error checking usage: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<UsageIncrementResult> IncrementUsageAsync(CancellationToken cancellationToken = default)
    {
        var licenseKey = _licenseService?.GetLicenseKey() ?? "";
        var deviceId = _licenseService?.GetDeviceId() ?? "";
        if (string.IsNullOrEmpty(licenseKey) && string.IsNullOrEmpty(deviceId))
        {
            return new UsageIncrementResult
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
                _cachedUsage = new UsageStatus
                {
                    CanScan = response.Remaining > 0,
                    ScanCount = response.ScanCount,
                    MonthlyLimit = response.MonthlyLimit,
                    Remaining = response.Remaining,
                    Tier = response.Tier,
                    ResetsAt = response.ResetsAt
                };
                _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);

                return new UsageIncrementResult
                {
                    Success = true,
                    ScanCount = response.ScanCount,
                    MonthlyLimit = response.MonthlyLimit,
                    Remaining = response.Remaining
                };
            }

            return new UsageIncrementResult
            {
                Success = false,
                ErrorMessage = response.Error ?? "Failed to record usage"
            };
        }
        catch (HttpRequestException)
        {
            // Network error - we'll track locally and sync later
            // For now, just return success to not block the user
            return new UsageIncrementResult
            {
                Success = true,
                IsOffline = true
            };
        }
        catch (Exception ex)
        {
            NetworkFailure.Report(_errorLogger, ex, "Receipt usage increment failed", ErrorCategory.Api);
            return new UsageIncrementResult
            {
                Success = false,
                ErrorMessage = $"Error recording usage: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public void InvalidateCache()
    {
        _cachedUsage = null;
        _cacheExpiry = DateTime.MinValue;
    }

    /// <inheritdoc />
    public UsageStatus? GetCachedUsage() => _cachedUsage;

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

    private async Task<UsageApiResponse> CallApiAsync(string action, string licenseKey, CancellationToken cancellationToken)
    {
        var deviceId = _licenseService?.GetDeviceId() ?? "";
        var requestBody = new
        {
            license_key = licenseKey,
            device_id = deviceId,
            action
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(UsageApiUrl, content, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        return JsonSerializer.Deserialize<UsageApiResponse>(responseJson) ?? new UsageApiResponse();
    }

    private class UsageApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("can_scan")]
        public bool CanScan { get; init; }

        [JsonPropertyName("scan_count")]
        public int ScanCount { get; init; }

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
/// Interface for receipt usage tracking service.
/// </summary>
public interface IReceiptUsageService : IDisposable
{
    /// <summary>
    /// Checks current usage and whether the user can scan more receipts.
    /// </summary>
    Task<UsageCheckResult> CheckUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the usage count after a successful scan.
    /// </summary>
    Task<UsageIncrementResult> IncrementUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached usage data.
    /// </summary>
    void InvalidateCache();

    /// <summary>
    /// Gets the cached usage status (may be stale).
    /// </summary>
    UsageStatus? GetCachedUsage();
}

/// <summary>
/// Result of checking usage status.
/// </summary>
public class UsageCheckResult
{
    /// <summary>
    /// Whether the user can scan another receipt.
    /// </summary>
    public bool CanScan { get; init; }

    /// <summary>
    /// Error message if check failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Current number of scans this month.
    /// </summary>
    public int ScanCount { get; init; }

    /// <summary>
    /// Maximum scans allowed this month.
    /// </summary>
    public int MonthlyLimit { get; init; }

    /// <summary>
    /// Remaining scans this month.
    /// </summary>
    public int Remaining { get; init; }

    /// <summary>
    /// User's subscription tier.
    /// </summary>
    public string? Tier { get; init; }

    /// <summary>
    /// When the usage counter resets (first of next month).
    /// </summary>
    public string? ResetsAt { get; init; }

    /// <summary>
    /// Whether the result is from offline cache.
    /// </summary>
    public bool IsOffline { get; init; }
}

/// <summary>
/// Result of incrementing usage.
/// </summary>
public class UsageIncrementResult
{
    /// <summary>
    /// Whether the increment was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if increment failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// New scan count after increment.
    /// </summary>
    public int ScanCount { get; init; }

    /// <summary>
    /// Monthly limit.
    /// </summary>
    public int MonthlyLimit { get; init; }

    /// <summary>
    /// Remaining scans after increment.
    /// </summary>
    public int Remaining { get; init; }

    /// <summary>
    /// Whether the increment was done offline (will sync later).
    /// </summary>
    public bool IsOffline { get; init; }
}

/// <summary>
/// Cached usage status.
/// </summary>
public class UsageStatus
{
    public bool CanScan { get; init; }
    public int ScanCount { get; init; }
    public int MonthlyLimit { get; init; }
    public int Remaining { get; init; }
    public string? Tier { get; init; }
    public string? ResetsAt { get; init; }
}
