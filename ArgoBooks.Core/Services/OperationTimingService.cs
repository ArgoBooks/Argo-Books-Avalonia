using System.Net.Http.Json;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// App-wide singleton that owns the <see cref="OperationEstimator"/> and keeps it fed:
/// it fetches pooled duration priors from <c>/api/ai/timing-priors.php</c>, caches them
/// (plus the locally-learned calibration) to disk, and records each completed AI call's
/// server-measured timing so estimates self-calibrate.
///
/// Mirrors the <see cref="ExchangeRateService"/> singleton style (<c>Instance</c> set in
/// the constructor). All disk and network work is best-effort: a failure leaves the
/// estimator on its seed priors and never throws to callers.
/// </summary>
public sealed class OperationTimingService
{
    private const string EndpointPath = "/api/ai/timing-priors.php";
    private const string CacheFileName = "ai-timings.json";
    private static readonly string PriorsUrl = $"{ApiConfig.BaseUrl}{EndpointPath}";

    private readonly IPlatformService _platform;
    private readonly HttpClient _httpClient;
    private readonly IErrorLogger? _errorLogger;
    private readonly OperationEstimator _estimator = new();
    private readonly object _diskLock = new();
    private PriorsPayload? _cachedPriors;

    /// <summary>Singleton instance, set on first construction.</summary>
    public static OperationTimingService? Instance { get; private set; }

    public OperationTimingService(IErrorLogger? errorLogger = null)
        : this(PlatformServiceFactory.GetPlatformService(), new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, errorLogger)
    {
    }

    public OperationTimingService(IPlatformService platform, HttpClient httpClient, IErrorLogger? errorLogger = null)
    {
        _platform = platform;
        _httpClient = httpClient;
        _errorLogger = errorLogger;
        Instance ??= this;
    }

    /// <summary>
    /// Loads cached priors + learned calibration from disk (fast), then kicks off a
    /// background priors refresh. Does not block startup on the network call.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        LoadFromDisk();
        _ = RefreshPriorsAsync(cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>Estimate one operation (see <see cref="OperationEstimator.Estimate"/>).</summary>
    public OperationEstimate Estimate(OperationKind op, double? sizeFeature = null, long uploadBytes = 0, int? pageCount = null)
        => _estimator.Estimate(op, sizeFeature, uploadBytes, pageCount);

    /// <summary>
    /// Records a completed call so the estimator self-calibrates, then persists the learned
    /// state. <paramref name="serverComputeMs"/> is the server-measured Gemini time (from the
    /// response timing block); <paramref name="totalWallClockMs"/> is the client's full stopwatch.
    /// </summary>
    public void RecordResult(
        OperationKind op,
        double serverComputeMs,
        double totalWallClockMs,
        long uploadBytes = 0,
        double? loadFactor = null)
    {
        if (loadFactor is > 0)
            _estimator.UpdateLoadFactor(loadFactor.Value);
        _estimator.RecordResult(op, serverComputeMs, totalWallClockMs, uploadBytes);
        SaveToDisk();
    }

    /// <summary>
    /// GETs the latest pooled priors for the configured model and applies them. Best-effort:
    /// any failure leaves the current priors in place. Only re-throws caller cancellation.
    /// </summary>
    public async Task RefreshPriorsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PriorsUrl);
            // Optional auth: the endpoint reads priors without auth but uses the device/license
            // for rate-limit bucketing, matching the other desktop GET endpoints.
            LicenseAuthHelper.AddAuthHeaders(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return;

            var payload = await response.Content.ReadFromJsonAsync<PriorsPayload>(cancellationToken);
            ApplyPayload(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // The HttpClient's own request timeout (not a caller cancellation). This is best-effort
            // startup work, so leave the current priors in place and don't log it as a network error
            // (otherwise a slow/unreachable endpoint spams the error dashboard on every launch).
        }
        catch (Exception ex)
        {
            NetworkFailure.Report(_errorLogger, ex, "OperationTimingService.RefreshPriorsAsync");
        }
    }

    /// <summary>Applies a fetched/cached payload to the estimator. No-op when empty.</summary>
    private void ApplyPayload(PriorsPayload? payload)
    {
        var priors = payload?.ToTimingPriors();
        if (priors == null || priors.Priors.Count == 0)
            return;
        _cachedPriors = payload;
        _estimator.SetPriors(priors);
        SaveToDisk();
    }

    private string CacheFilePath() => _platform.CombinePaths(_platform.GetAppDataPath(), CacheFileName);

    private void LoadFromDisk()
    {
        try
        {
            var path = CacheFilePath();
            if (!File.Exists(path))
                return;
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<PersistState>(json);
            if (state == null)
                return;

            if (state.UserCalibration is > 0)
                _estimator.UserCalibration = state.UserCalibration.Value;
            if (state.UploadBytesPerMs is > 0)
                _estimator.UploadBytesPerMs = state.UploadBytesPerMs.Value;

            if (state.Priors != null)
            {
                var priors = state.Priors.ToTimingPriors();
                if (priors.Priors.Count > 0)
                {
                    _cachedPriors = state.Priors;
                    _estimator.SetPriors(priors);
                }
            }
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "OperationTimingService.LoadFromDisk");
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var state = new PersistState
            {
                UserCalibration = _estimator.UserCalibration,
                UploadBytesPerMs = _estimator.UploadBytesPerMs,
                Priors = _cachedPriors,
            };
            var json = JsonSerializer.Serialize(state);
            lock (_diskLock)
            {
                var dir = _platform.GetAppDataPath();
                _platform.EnsureDirectoryExists(dir);
                File.WriteAllText(CacheFilePath(), json);
            }
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "OperationTimingService.SaveToDisk");
        }
    }

    // ----- Disk + wire DTOs -----

    private sealed class PersistState
    {
        [JsonPropertyName("userCalibration")] public double? UserCalibration { get; set; }
        [JsonPropertyName("uploadBytesPerMs")] public double? UploadBytesPerMs { get; set; }
        [JsonPropertyName("priors")] public PriorsPayload? Priors { get; set; }
    }

    /// <summary>Matches the JSON returned by <c>/api/ai/timing-priors.php</c>.</summary>
    internal sealed class PriorsPayload
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("load_factor")] public double LoadFactor { get; set; } = 1.0;
        [JsonPropertyName("priors")] public List<PriorDto>? Priors { get; set; }

        public TimingPriors ToTimingPriors()
        {
            var list = new List<OperationPrior>();
            foreach (var p in Priors ?? [])
            {
                if (p.P50Ms <= 0)
                    continue;
                list.Add(new OperationPrior
                {
                    Operation = OperationKindExtensions.FromServerTag(p.Operation),
                    P50Ms = p.P50Ms,
                    P90Ms = p.P90Ms > p.P50Ms ? p.P90Ms : p.P50Ms * 2,
                    SampleCount = p.SampleCount,
                    AvgSizeFeature = p.AvgSizeFeature,
                    AvgOutputTokens = p.AvgOutputTokens,
                    PerPageMs = p.PerPageMs,
                });
            }
            return new TimingPriors
            {
                Model = Model ?? "",
                LoadFactor = LoadFactor > 0 ? LoadFactor : 1.0,
                Priors = list,
            };
        }
    }

    internal sealed class PriorDto
    {
        [JsonPropertyName("operation")] public string? Operation { get; set; }
        [JsonPropertyName("p50_ms")] public double P50Ms { get; set; }
        [JsonPropertyName("p90_ms")] public double P90Ms { get; set; }
        [JsonPropertyName("sample_count")] public int SampleCount { get; set; }
        [JsonPropertyName("avg_size_feature")] public double? AvgSizeFeature { get; set; }
        [JsonPropertyName("avg_output_tokens")] public double? AvgOutputTokens { get; set; }
        [JsonPropertyName("per_page_ms")] public double? PerPageMs { get; set; }
    }
}
