using System.Runtime.InteropServices;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Central telemetry management service that coordinates all telemetry operations.
/// </summary>
public class TelemetryManager : ITelemetryManager
{
    /// <summary>
    /// How often the session sentinel is stamped. Also the tick that drives the periodic
    /// upload below, so it doubles as the granularity of a recovered session's duration.
    /// </summary>
    private const int HeartbeatIntervalSeconds = 60;

    /// <summary>
    /// Heartbeats between periodic uploads, i.e. one flush every 20 minutes.
    /// <para>
    /// Sized against the server's free-tier ceiling of 6 uploads per hour per device: at
    /// most 3 periodic flushes, plus the startup flush and the one on close, leaves
    /// headroom. Quiet ticks are free because UploadPendingDataAsync returns without a
    /// request when nothing is pending.
    /// </para>
    /// </summary>
    private const int HeartbeatsPerUpload = 20;

    private readonly ITelemetryStorageService _storageService;
    private readonly ITelemetryUploadService _uploadService;
    private readonly IGeoLocationService _geoLocationService;
    private readonly IErrorLogger _errorLogger;
    private readonly IPlatformService _platformService;

    private readonly string _appVersion;
    private readonly string _platform;
    private readonly string _userAgent;

    /// <inheritdoc />
    public void NoteCurrentPage(string? pageName)
    {
        var now = DateTime.UtcNow;
        string? left;
        long activeOnPage;
        long wallOnPage;

        // Shares the activity lock: both are written from the UI thread and read at
        // session end, so a second lock would only add an ordering to get wrong.
        lock (_activityLock)
        {
            left = _currentPage;
            // Active time is already idle-aware, so the difference across the visit is too.
            // Deriving it rather than running a second timer means one idle rule, not two
            // that can disagree. Subtracted as ticks and rounded once, so a visit is not
            // charged the rounding of every input inside it.
            activeOnPage = ToSeconds(_activeTicks - _pageEnteredActiveTicks);
            wallOnPage = (long)(now - _pageEnteredUtc).TotalSeconds;

            _currentPage = string.IsNullOrWhiteSpace(pageName) ? null : pageName;
            _pageEnteredActiveTicks = _activeTicks;
            _pageEnteredUtc = now;
        }

        if (left != null)
        {
            _ = TrackPageViewAsync(left, activeOnPage, wallOnPage);
        }
    }

    /// <summary>
    /// Closes the page still open, so the last screen of a session is recorded like the rest.
    /// Without this the page someone quit from is the one page never measured, which is the
    /// one most worth seeing.
    /// </summary>
    private void FlushCurrentPage()
    {
        NoteCurrentPage(null);
    }

    /// <inheritdoc />
    public void MarkActivity()
    {
        var now = DateTime.UtcNow;

        lock (_activityLock)
        {
            if (!_isInitialized)
            {
                return;
            }

            // Credit the gap since the previous input, but only if it is short enough to
            // have been someone reading the screen rather than someone who walked away.
            // A longer gap contributes nothing at all, which is the whole point: it is why
            // an app left open overnight cannot inflate the figure.
            //
            // Accumulated in ticks rather than whole seconds. Clicks and keystrokes are
            // usually a fraction of a second apart, and truncating each gap on its own
            // threw all of that away: three keys a second measured as no activity at all,
            // and one input every 1.5s as half the time it took. The faster someone
            // worked, the less of their time was counted.
            var gap = now - _lastActivityUtc;
            if (gap > TimeSpan.Zero && gap <= IdleThreshold)
            {
                _activeTicks += gap.Ticks;
            }

            _lastActivityUtc = now;
        }
    }

    /// <summary>Ticks to whole seconds, rounded once at the point of reporting.</summary>
    private static long ToSeconds(long ticks) => ticks / TimeSpan.TicksPerSecond;

    /// <summary>
    /// How long a gap between inputs before the user is treated as away. Long enough that
    /// reading a report on screen still counts, short enough that a window left open over
    /// lunch does not.
    /// </summary>
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly object _activityLock = new();
    private DateTime _lastActivityUtc;
    private string? _currentPage;
    private long _pageEnteredActiveTicks;
    private DateTime _pageEnteredUtc;
    private long _activeTicks;
    private DateTime _sessionStartTime;
    private GeoLocationData? _cachedGeoLocation;
    private bool _isInitialized;

    private SessionSentinel? _sentinel;
    private Timer? _heartbeatTimer;
    private int _heartbeatTicks;
    private int _uploadInFlight;

    // Company profiles already recorded this session, so reopening or re-saving a company
    // does not record it again. Session-scoped on purpose: a profile per launch is a
    // reasonable refresh rate for details the user can edit at any time.
    private readonly HashSet<string> _reportedCompanyProfiles = [];
    private readonly Lock _profileGate = new();
    private readonly HashSet<string> _reportedCompanyScales = [];

    /// <summary>
    /// Initializes a new instance of the TelemetryManager.
    /// </summary>
    public TelemetryManager(
        ITelemetryStorageService storageService,
        ITelemetryUploadService uploadService,
        IGeoLocationService geoLocationService,
        IErrorLogger errorLogger,
        string? appVersion = null,
        IPlatformService? platformService = null)
    {
        _storageService = storageService;
        _uploadService = uploadService;
        _geoLocationService = geoLocationService;
        _errorLogger = errorLogger;
        _platformService = platformService ?? PlatformServiceFactory.GetPlatformService();

        _appVersion = appVersion ?? AppInfo.VersionNumber;
        _platform = GetPlatform();
        _userAgent = GetUserAgent();

        // Wire up error logger to report errors to telemetry
        if (_errorLogger is ErrorLogger errorLoggerImpl)
        {
            errorLoggerImpl.TelemetryManager = this;
        }
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
                return;

            _sessionStartTime = DateTime.UtcNow;
            _lastActivityUtc = _sessionStartTime;
            _activeTicks = 0;
            _currentPage = null;
            _isInitialized = true;

            try
            {
                // Prefetch geolocation in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _cachedGeoLocation = await _geoLocationService.GetLocationAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _errorLogger.LogDebug($"Failed to get geolocation: {ex.Message}");
                    }
                }, cancellationToken);

                // Close out any previous run that died without recording its own end.
                // Done before this session's start so the recovered events, which carry
                // their original timestamps, read in order against it.
                await RecoverUncleanSessionsAsync(cancellationToken);

                _sentinel = SessionSentinel.Begin(_platformService, _sessionStartTime, _appVersion, _errorLogger);

                // Record session start
                var sessionEvent = await CreateEventAsync<SessionEvent>(cancellationToken);
                sessionEvent.Action = SessionAction.SessionStart;
                await _storageService.RecordEventAsync(sessionEvent, cancellationToken);

                StartHeartbeat();
            }
            catch (Exception ex)
            {
                _errorLogger.LogError(ex, ErrorCategory.Unknown, "Failed to initialize telemetry");
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task EndSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            return;

        // Stopped first so a heartbeat can't race the sentinel's removal below.
        StopHeartbeat();

        // Before lastPage is read below: this clears _currentPage, so the read has to happen
        // after, and the value it wants is captured here.
        string? finalPage;
        lock (_activityLock)
        {
            finalPage = _currentPage;
        }
        FlushCurrentPage();

        try
        {
            var duration = (long)(DateTime.UtcNow - _sessionStartTime).TotalSeconds;

            long activeSeconds;
            lock (_activityLock)
            {
                activeSeconds = ToSeconds(_activeTicks);
            }

            // finalPage, not _currentPage: flushing the last page view cleared the latter.
            string? lastPage = finalPage;

            var sessionEvent = await CreateEventAsync<SessionEvent>(cancellationToken);
            sessionEvent.Action = SessionAction.SessionEnd;
            sessionEvent.DurationSeconds = duration;
            sessionEvent.ActiveSeconds = activeSeconds;
            sessionEvent.LastPage = lastPage;
            sessionEvent.Clean = true;
            await _storageService.RecordEventAsync(sessionEvent, cancellationToken);

            // Only now is the session provably accounted for. Dropping the sentinel any
            // earlier would lose the session outright if the record above threw; dropping
            // it later would mean a failed upload got it reported as an unclean exit, even
            // though the event is safely stored for the next launch to deliver.
            _sentinel?.Complete();
            _sentinel = null;

            // Attempt to upload pending data on shutdown - must await to ensure upload completes before app closes
            await _uploadService.UploadPendingDataAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger.LogError(ex, ErrorCategory.Unknown, "Failed to end telemetry session");
        }
        finally
        {
            // Releases the handle without deleting, so a shutdown that never got as far as
            // Complete() is reported as the unclean exit it was.
            _sentinel?.Dispose();
            _sentinel = null;
        }
    }

    /// <summary>
    /// Turns sentinels left by runs that never shut down cleanly into the SessionEnd events
    /// those runs could not record for themselves.
    /// </summary>
    private async Task RecoverUncleanSessionsAsync(CancellationToken cancellationToken)
    {
        foreach (var orphan in SessionSentinel.CollectOrphans(_platformService, _errorLogger))
        {
            var sessionEvent = await CreateEventAsync<SessionEvent>(cancellationToken);
            sessionEvent.Timestamp = orphan.LastHeartbeatUtc;
            sessionEvent.Action = SessionAction.SessionEnd;
            sessionEvent.DurationSeconds = orphan.DurationSeconds;
            sessionEvent.Clean = false;

            // The dead run may have been an older build, so attribute it to the version
            // that actually ran rather than the one doing the recovering.
            if (!string.IsNullOrEmpty(orphan.AppVersion))
            {
                sessionEvent.AppVersion = orphan.AppVersion;
            }

            await _storageService.RecordEventAsync(sessionEvent, cancellationToken);
        }
    }

    private void StartHeartbeat()
    {
        var period = TimeSpan.FromSeconds(HeartbeatIntervalSeconds);
        _heartbeatTimer = new Timer(OnHeartbeat, null, period, period);
    }

    private void StopHeartbeat()
    {
        var timer = Interlocked.Exchange(ref _heartbeatTimer, null);
        timer?.Dispose();
    }

    /// <summary>
    /// Stamps the sentinel every tick, and every <see cref="HeartbeatsPerUpload"/> ticks
    /// also flushes pending events. The flush is what makes a session that never closes
    /// cleanly still worth something: without it, a user who force-quits and never reopens
    /// the app takes every event on their machine with them.
    /// </summary>
    private void OnHeartbeat(object? state)
    {
        try
        {
            _sentinel?.Heartbeat(DateTime.UtcNow);

            if (Interlocked.Increment(ref _heartbeatTicks) % HeartbeatsPerUpload != 0)
            {
                return;
            }

            // Skip rather than queue when a flush is still running. A slow or retrying
            // upload must not stack requests up behind it and burn the hourly allowance.
            if (Interlocked.CompareExchange(ref _uploadInFlight, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _uploadService.UploadPendingDataAsync();
                }
                catch (Exception ex)
                {
                    _errorLogger.LogDebug($"Periodic telemetry upload failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _uploadInFlight, 0);
                }
            });
        }
        catch (Exception ex)
        {
            _errorLogger.LogDebug($"Telemetry heartbeat failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task TrackFeatureAsync(FeatureName featureName, string? context = null, long? durationMs = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var featureEvent = await CreateEventAsync<FeatureUsageEvent>(cancellationToken);
            featureEvent.FeatureName = featureName;
            featureEvent.Context = context;
            featureEvent.DurationMs = durationMs;
            await _storageService.RecordEventAsync(featureEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger.LogDebug($"Failed to track feature: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task TrackPageViewAsync(string pageName, long activeSeconds, long durationSeconds, CancellationToken cancellationToken = default)
    {
        try
        {
            var pageEvent = await CreateEventAsync<PageViewEvent>(cancellationToken);
            pageEvent.PageName = pageName;
            pageEvent.ActiveSeconds = activeSeconds < 0 ? 0 : activeSeconds;
            pageEvent.DurationSeconds = durationSeconds < 0 ? 0 : durationSeconds;
            await _storageService.RecordEventAsync(pageEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger.LogDebug($"Failed to track page view: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task TrackCompanyScaleAsync(CompanyScaleCounts counts, CancellationToken cancellationToken = default)
    {
        try
        {
            // Keyed on the counts themselves rather than the company, so the row is written
            // again only when the file has actually changed size. Repeated saves that add
            // nothing record nothing.
            lock (_profileGate)
            {
                if (!_reportedCompanyScales.Add(counts.ToString()))
                {
                    return;
                }
            }

            var scaleEvent = await CreateEventAsync<CompanyScaleEvent>(cancellationToken);
            scaleEvent.Expenses = counts.Expenses;
            scaleEvent.Revenues = counts.Revenues;
            scaleEvent.Invoices = counts.Invoices;
            scaleEvent.Payments = counts.Payments;
            scaleEvent.Customers = counts.Customers;
            scaleEvent.Suppliers = counts.Suppliers;
            scaleEvent.Products = counts.Products;
            scaleEvent.Categories = counts.Categories;
            scaleEvent.Receipts = counts.Receipts;
            scaleEvent.Employees = counts.Employees;
            scaleEvent.BankLines = counts.BankLines;
            await _storageService.RecordEventAsync(scaleEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger.LogDebug($"Failed to track company scale: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task TrackCompanyProfileAsync(
        string? companyName,
        string? businessType,
        string? industry,
        string? country,
        string? currency,
        string? language,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // One row per company per session. Callers fire this from the company-opened
            // path, which also runs on every save and on returning from settings, so
            // without this a long session would record the same profile dozens of times.
            //
            // Language is in the key even though it makes a second row possible, because it
            // is the one field here a user actively changes mid-session, and they change it
            // in the settings screen whose exit re-fires this. Keyed without it we would
            // only ever record the language they opened with, which is the opposite of the
            // question the field exists to answer.
            var key = $"{companyName}|{country}|{currency}|{language}";
            lock (_profileGate)
            {
                if (!_reportedCompanyProfiles.Add(key))
                {
                    return;
                }
            }

            var profileEvent = await CreateEventAsync<CompanyProfileEvent>(cancellationToken);
            profileEvent.CompanyName = companyName;
            profileEvent.BusinessType = businessType;
            profileEvent.Industry = industry;
            profileEvent.Country = country;
            profileEvent.Currency = currency;
            profileEvent.Language = language;
            await _storageService.RecordEventAsync(profileEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger.LogDebug($"Failed to track company profile: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task TrackStartupAsync(
        long? toFirstPaintMs,
        long? toServicesReadyMs,
        long? toViewModelsReadyMs,
        long? toReadyMs,
        bool coldStart,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var startupEvent = await CreateEventAsync<StartupEvent>(cancellationToken);
            startupEvent.ToFirstPaintMs = toFirstPaintMs;
            startupEvent.ToReadyMs = toReadyMs;
            startupEvent.ToServicesReadyMs = toServicesReadyMs;
            startupEvent.ToViewModelsReadyMs = toViewModelsReadyMs;
            startupEvent.ColdStart = coldStart;
            await _storageService.RecordEventAsync(startupEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger.LogDebug($"Failed to track startup timing: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task TrackExportAsync(ExportType exportType, long durationMs, long fileSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var exportEvent = await CreateEventAsync<ExportEvent>(cancellationToken);
            exportEvent.ExportType = exportType;
            exportEvent.DurationMs = durationMs;
            exportEvent.FileSize = fileSize;
            await _storageService.RecordEventAsync(exportEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger.LogDebug($"Failed to track export: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task TrackApiCallAsync(ApiName apiName, long durationMs, bool success, string? model = null, int? tokensUsed = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiEvent = await CreateEventAsync<ApiUsageEvent>(cancellationToken);
            apiEvent.ApiName = apiName;
            apiEvent.DurationMs = durationMs;
            apiEvent.Success = success;
            apiEvent.Model = model;
            apiEvent.TokensUsed = tokensUsed;
            await _storageService.RecordEventAsync(apiEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger.LogDebug($"Failed to track API call: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task TrackErrorAsync(ErrorLogEntry errorEntry, CancellationToken cancellationToken = default)
    {
        try
        {
            var errorEvent = await CreateEventAsync<ErrorEvent>(cancellationToken);
            // Anything below Warning never reaches here (see ErrorLogger.AddEntry), so
            // the only values that travel are Warning and Error.
            errorEvent.Severity = errorEntry.Level == LogLevel.Warning ? LogLevel.Warning : LogLevel.Error;
            errorEvent.ErrorCode = errorEntry.ErrorCode ?? "Unknown";
            errorEvent.ErrorCategory = errorEntry.Category;
            errorEvent.Message = errorEntry.Message;
            errorEvent.SourceFile = errorEntry.SourceFile;
            errorEvent.LineNumber = errorEntry.LineNumber;
            errorEvent.MethodName = errorEntry.MethodName;
            errorEvent.Context = errorEntry.Context;
            await _storageService.RecordEventAsync(errorEvent, cancellationToken);
        }
        catch
        {
            // Don't log errors about error tracking to avoid infinite loops
        }
    }

    /// <inheritdoc />
    public async Task<TelemetryUploadResult> UploadPendingDataAsync(CancellationToken cancellationToken = default)
    {
        return await _uploadService.UploadPendingDataAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
        _errorLogger.LogInfo("User requested to clear all telemetry data");
        return _storageService.ClearAllDataAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<TelemetryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return _storageService.GetStatisticsAsync(cancellationToken);
    }

    private async Task<T> CreateEventAsync<T>(CancellationToken cancellationToken) where T : TelemetryEvent, new()
    {
        var telemetryEvent = new T
        {
            AppVersion = _appVersion,
            Platform = _platform,
            UserAgent = _userAgent
        };

        // Add geolocation if available
        if (_cachedGeoLocation != null)
        {
            telemetryEvent.GeoLocation = _cachedGeoLocation;
        }
        else
        {
            try
            {
                _cachedGeoLocation = await _geoLocationService.GetLocationAsync(cancellationToken);
                telemetryEvent.GeoLocation = _cachedGeoLocation;
            }
            catch
            {
                // Ignore geolocation failures
            }
        }

        return telemetryEvent;
    }

    private static string GetPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macOS";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "Linux";
        return "Unknown";
    }

    private static string GetUserAgent()
    {
        var os = RuntimeInformation.OSDescription;
        var arch = RuntimeInformation.OSArchitecture.ToString();
        return $"{os} ({arch})";
    }
}
