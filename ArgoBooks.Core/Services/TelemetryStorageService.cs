using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for storing telemetry events locally.
/// </summary>
public class TelemetryStorageService : ITelemetryStorageService
{
    private const string TelemetryDirectory = "telemetry";
    private const string EventsFileName = "events.json";
    private const string UploadStateFileName = "upload_state.json";
    private const int MaxEventsInMemory = 10000;

    private readonly IPlatformService _platformService;
    private readonly IErrorLogger? _errorLogger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private List<TelemetryEventWrapper> _events = [];
    private UploadState _uploadState = new();

    // 0 until this store has reported a failure of its own. See ReportStorageFailure.
    private int _storageFailureReported;

    /// <summary>
    /// Initializes a new instance of the TelemetryStorageService.
    /// </summary>
    public TelemetryStorageService(IPlatformService? platformService = null, IErrorLogger? errorLogger = null)
    {
        _platformService = platformService ?? PlatformServiceFactory.GetPlatformService();
        _errorLogger = errorLogger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <inheritdoc />
    public async Task RecordEventAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        await WithFreshStateAsync(async () =>
        {
            var wrapper = new TelemetryEventWrapper
            {
                DataType = telemetryEvent.DataType,
                Event = telemetryEvent
            };

            _events.Add(wrapper);

            // Trim if over capacity
            if (_events.Count > MaxEventsInMemory)
            {
                // Remove oldest non-uploaded events first, then oldest uploaded
                var toRemove = _events
                    .OrderBy(e => e.Event.IsUploaded)
                    .ThenBy(e => e.Event.Timestamp)
                    .Take(_events.Count - (int)(MaxEventsInMemory * 0.9))
                    .ToList();

                foreach (var item in toRemove)
                {
                    _events.Remove(item);
                }
            }

            await SaveEventsAsync(cancellationToken);
            return true;
        }, fallback: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TelemetryEvent>> GetPendingEventsAsync(CancellationToken cancellationToken = default)
    {
        return await WithFreshStateAsync(() =>
        {
            IReadOnlyList<TelemetryEvent> pending = _events
                .Where(e => !e.Event.IsUploaded)
                .Select(e => e.Event)
                .OrderBy(e => e.Timestamp)
                .ToList();

            return Task.FromResult(pending);
        }, fallback: [], cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkEventsUploadedAsync(IEnumerable<string> dataIds, CancellationToken cancellationToken = default)
    {
        var idSet = dataIds.ToHashSet();

        await WithFreshStateAsync(async () =>
        {
            // Counted from what this call actually flipped, not from the id set. A sibling
            // instance may have uploaded some of these already, and adding the whole set
            // regardless is how the running total drifted above the real one.
            var newlyMarked = 0;
            foreach (var wrapper in _events.Where(e => idSet.Contains(e.Event.DataId)))
            {
                if (wrapper.Event.IsUploaded)
                {
                    continue;
                }

                wrapper.Event.IsUploaded = true;
                newlyMarked++;
            }

            _uploadState.LastUploadTime = DateTime.UtcNow;
            _uploadState.TotalEventsUploaded += newlyMarked;

            await SaveEventsAsync(cancellationToken);
            await SaveUploadStateAsync(cancellationToken);
            return true;
        }, fallback: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> ExportToJsonAsync(CancellationToken cancellationToken = default)
    {
        return await WithFreshStateAsync(() =>
        {
            var exportData = new TelemetryExport
            {
                ExportTime = DateTime.UtcNow,
                TotalEvents = _events.Count,
                Events = _events.Select(e => e.Event).OrderByDescending(e => e.Timestamp).ToList()
            };

            return Task.FromResult(JsonSerializer.Serialize(exportData, _jsonOptions));
        }, fallback: string.Empty, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Held across the delete so a sibling instance cannot be mid-write and put the
            // file straight back. This is the user asking us to erase their data from
            // Settings, so it has to actually stick.
            using var fileLock = await TelemetryFileLock.AcquireAsync(
                GetTelemetryDirectory(), _errorLogger, cancellationToken);

            _events.Clear();
            _uploadState = new UploadState();

            var eventsPath = GetEventsFilePath();
            var statePath = GetUploadStatePath();

            if (File.Exists(eventsPath))
                File.Delete(eventsPath);

            if (File.Exists(statePath))
                File.Delete(statePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<TelemetryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return await WithFreshStateAsync(() =>
        {
            return Task.FromResult(new TelemetryStatistics
            {
                TotalEvents = _events.Count,
                PendingEvents = _events.Count(e => !e.Event.IsUploaded),
                UploadedEvents = _events.Count(e => e.Event.IsUploaded),
                EventsByType = _events
                    .GroupBy(e => e.DataType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                OldestEventTime = _events.MinBy(e => e.Event.Timestamp)?.Event.Timestamp,
                NewestEventTime = _events.MaxBy(e => e.Event.Timestamp)?.Event.Timestamp,
                LastUploadTime = _uploadState.LastUploadTime,
                TotalEventsEverUploaded = _uploadState.TotalEventsUploaded
            });
        }, fallback: new TelemetryStatistics(), cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> with the in-process lock and the cross-instance
    /// lock both held, and with <see cref="_events"/> and <see cref="_uploadState"/> read
    /// fresh from disk first.
    ///
    /// <para>
    /// The re-read is not belt-and-braces, it is the point. These files are device-global
    /// and every running instance writes them, so a copy cached at startup goes stale the
    /// moment a sibling instance saves. Writing that stale copy back is what used to
    /// resurrect events whose "uploaded" flag another instance had just recorded, and the
    /// server stores whatever arrives, so those events were uploaded and counted a second
    /// time. Reading inside the lock means the version we modify is the version on disk.
    /// </para>
    ///
    /// <para>
    /// If the cross-instance lock cannot be taken we continue anyway: telemetry must never
    /// be the reason an action fails. That degrades to the old racy behaviour for one
    /// operation rather than losing the event.
    /// </para>
    /// </summary>
    /// <param name="fallback">
    /// Returned when the events file exists but cannot be read, so callers get a harmless
    /// value rather than null. Never a success value: <see cref="GetPendingEventsAsync"/>
    /// passes an empty list, which correctly reads as "nothing to upload right now".
    /// </param>
    private async Task<T> WithFreshStateAsync<T>(
        Func<Task<T>> operation,
        T fallback,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            using var fileLock = await TelemetryFileLock.AcquireAsync(
                GetTelemetryDirectory(), _errorLogger, cancellationToken);

            if (!await LoadEventsAsync(cancellationToken))
            {
                // Unreadable rather than absent. Skipping costs at most this one event;
                // continuing would write an empty list over everything still pending.
                // LoadEventsAsync has already logged the reason.
                return fallback;
            }

            await LoadUploadStateAsync(cancellationToken);

            return await operation();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Reads the events file into <see cref="_events"/>. Returns false if the file is
    /// there but could not be read, which the caller must treat as "do not write".
    ///
    /// <para>
    /// Every operation re-reads now, so falling back to an empty list on a read failure
    /// would no longer just lose this instance's view: the very next save would write
    /// that empty list over a file still holding everyone's pending events. A missing
    /// file is a genuine empty list and is not a failure.
    /// </para>
    /// </summary>
    private async Task<bool> LoadEventsAsync(CancellationToken cancellationToken)
    {
        var path = GetEventsFilePath();
        if (!File.Exists(path))
        {
            _events = [];
            return true;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<List<StoredEventWrapper>>(stream, _jsonOptions, cancellationToken);

            // The converter answers an event it cannot read with null. Kept, that entry broke
            // every query over the list, all of which read Event, so pending events stopped
            // uploading for good. Dropping it costs that one event and nothing else.
            var events = new List<TelemetryEventWrapper>();
            foreach (var stored in loaded ?? [])
            {
                if (stored.Event is { } telemetryEvent)
                    events.Add(new TelemetryEventWrapper { DataType = stored.DataType, Event = telemetryEvent });
            }

            _events = events;
            return true;
        }
        catch (Exception ex)
        {
            ReportStorageFailure(ex, "Failed to load telemetry events");
            _events = [];
            return false;
        }
    }

    /// <summary>
    /// Reports a failure of the telemetry store itself, exactly once per run.
    ///
    /// <para>
    /// This layer cannot report its own failures the usual way, because the usual way runs
    /// through it. <see cref="IErrorLogger.LogError(Exception, ErrorCategory, string?)"/>
    /// hands the entry to the telemetry manager, which records it as an event, which comes
    /// straight back here and fails again for the same reason it failed the first time. The
    /// result is a loop that queues a fresh task per iteration and only ends when the disk
    /// problem does. Reporting once gives us the diagnosis and then falls back to
    /// <see cref="IErrorLogger.LogDebug"/>, which stays local and cannot re-enter.
    /// </para>
    /// </summary>
    private void ReportStorageFailure(Exception exception, string context)
    {
        if (Interlocked.Exchange(ref _storageFailureReported, 1) == 0)
        {
            _errorLogger?.LogError(exception, ErrorCategory.FileSystem, context);
            return;
        }

        _errorLogger?.LogDebug($"{context}: {exception.Message}");
    }

    private async Task SaveEventsAsync(CancellationToken cancellationToken)
    {
        var path = GetEventsFilePath();
        EnsureDirectoryExists(path);

        // Callers reach here holding the cross-instance lock, so no sibling is writing at
        // the same time. The per-process scratch name stays regardless: it keeps the swap
        // atomic against antivirus and indexers, which do not honour our lock.
        var tempPath = AtomicFile.TempPathFor(path);
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, _events, _jsonOptions, cancellationToken);
            }

            await AtomicFile.ReplaceAsync(tempPath, path, overwrite: true, cancellationToken);
        }
        catch (Exception ex)
        {
            AtomicFile.TryDeleteTemp(tempPath);
            // Same re-entry trap as the load path, and worse: a save that keeps failing
            // used to log an error, which recorded an event, which saved, which failed.
            ReportStorageFailure(ex, "Failed to save telemetry events");
        }
    }

    private async Task LoadUploadStateAsync(CancellationToken cancellationToken)
    {
        var path = GetUploadStatePath();
        if (!File.Exists(path))
        {
            _uploadState = new UploadState();
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<UploadState>(stream, _jsonOptions, cancellationToken);
            _uploadState = loaded ?? new UploadState();
        }
        catch (Exception ex)
        {
            ReportStorageFailure(ex, "Failed to load upload state");
            _uploadState = new UploadState();
        }
    }

    private async Task SaveUploadStateAsync(CancellationToken cancellationToken)
    {
        var path = GetUploadStatePath();
        EnsureDirectoryExists(path);

        // Device-global like the events file above; same per-process scratch treatment.
        var tempPath = AtomicFile.TempPathFor(path);
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, _uploadState, _jsonOptions, cancellationToken);
            }

            await AtomicFile.ReplaceAsync(tempPath, path, overwrite: true, cancellationToken);
        }
        catch (Exception ex)
        {
            AtomicFile.TryDeleteTemp(tempPath);
            ReportStorageFailure(ex, "Failed to save upload state");
        }
    }

    private string GetEventsFilePath()
    {
        return _platformService.CombinePaths(
            _platformService.GetAppDataPath(),
            TelemetryDirectory,
            EventsFileName);
    }

    private string GetUploadStatePath()
    {
        return _platformService.CombinePaths(
            _platformService.GetAppDataPath(),
            TelemetryDirectory,
            UploadStateFileName);
    }

    /// <summary>
    /// The folder holding every file this service owns. Also where the cross-instance
    /// lock lives, so the lock always sits on the same volume as the data it guards.
    /// </summary>
    private string GetTelemetryDirectory()
    {
        return _platformService.CombinePaths(
            _platformService.GetAppDataPath(),
            TelemetryDirectory);
    }

    private void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _platformService.EnsureDirectoryExists(directory);
        }
    }

    /// <inheritdoc />
    public async Task<string?> SaveBackupFileAsync(CancellationToken cancellationToken = default)
    {
        return await WithFreshStateAsync(async () =>
        {
            var pendingEvents = _events
                .Where(e => !e.Event.IsUploaded)
                .Select(e => e.Event)
                .OrderBy(e => e.Timestamp)
                .ToList();

            if (pendingEvents.Count == 0)
            {
                return null;
            }

            var backupData = new TelemetryBackup
            {
                BackupTime = DateTime.UtcNow,
                TotalEvents = pendingEvents.Count,
                Events = pendingEvents
            };

            var backupPath = GetBackupFilePath();
            EnsureDirectoryExists(backupPath);

            try
            {
                await using var stream = File.Create(backupPath);
                await JsonSerializer.SerializeAsync(stream, backupData, _jsonOptions, cancellationToken);
                _errorLogger?.LogInfo($"Telemetry backup saved to: {backupPath}");
                return backupPath;
            }
            catch (Exception ex)
            {
                ReportStorageFailure(ex, "Failed to save telemetry backup file");
                return null;
            }
        }, fallback: null, cancellationToken);
    }

    private string GetBackupFilePath()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return _platformService.CombinePaths(
            _platformService.GetAppDataPath(),
            TelemetryDirectory,
            "backups",
            $"telemetry_backup_{timestamp}.json");
    }

    #region Internal Types

    private class TelemetryEventWrapper
    {
        public TelemetryDataType DataType { get; set; }

        [JsonConverter(typeof(TelemetryEventConverter))]
        public TelemetryEvent Event { get; set; } = null!;
    }

    /// <summary>
    /// A wrapper as read from disk, where Event is null if the converter could not read it.
    /// </summary>
    private class StoredEventWrapper
    {
        public TelemetryDataType DataType { get; set; }

        [JsonConverter(typeof(TelemetryEventConverter))]
        public TelemetryEvent? Event { get; set; }
    }

    private class UploadState
    {
        public DateTime? LastUploadTime { get; set; }
        public int TotalEventsUploaded { get; set; }
    }

    private class TelemetryExport
    {
        public DateTime ExportTime { get; set; }
        public int TotalEvents { get; set; }
        public List<TelemetryEvent> Events { get; set; } = [];
    }

    private class TelemetryBackup
    {
        public DateTime BackupTime { get; set; }
        public int TotalEvents { get; set; }
        public List<TelemetryEvent> Events { get; set; } = [];
    }

    /// <summary>
    /// Custom JSON converter for polymorphic TelemetryEvent serialization.
    /// </summary>
    private class TelemetryEventConverter : JsonConverter<TelemetryEvent>
    {
        public override TelemetryEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("dataType", out var dataTypeElement))
            {
                return null;
            }

            // A data type added since this build is as unreadable as any other unknown value, and
            // costs the same: that event, not the file.
            if (dataTypeElement.ValueKind != JsonValueKind.String ||
                !Enum.TryParse<TelemetryDataType>(dataTypeElement.GetString(), ignoreCase: true, out var dataType))
            {
                return null;
            }

            var json = root.GetRawText();

            // One unreadable event used to fail the whole List<TelemetryEvent>, taking every
            // other event in the file with it. That is reachable whenever a build reads a file
            // written by a newer one: an enum value it does not have, such as a FeatureName
            // added since, throws here. Losing one event is acceptable, losing the batch is not.
            try
            {
                return Deserialize(dataType, json, options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static TelemetryEvent? Deserialize(TelemetryDataType dataType, string json, JsonSerializerOptions options)
        {
            return dataType switch
            {
                TelemetryDataType.Session => JsonSerializer.Deserialize<SessionEvent>(json, options),
                TelemetryDataType.Export => JsonSerializer.Deserialize<ExportEvent>(json, options),
                TelemetryDataType.ApiUsage => JsonSerializer.Deserialize<ApiUsageEvent>(json, options),
                TelemetryDataType.Error => JsonSerializer.Deserialize<ErrorEvent>(json, options),
                TelemetryDataType.FeatureUsage => JsonSerializer.Deserialize<FeatureUsageEvent>(json, options),
                TelemetryDataType.CompanyProfile => JsonSerializer.Deserialize<CompanyProfileEvent>(json, options),
                TelemetryDataType.Startup => JsonSerializer.Deserialize<StartupEvent>(json, options),
                TelemetryDataType.PageView => JsonSerializer.Deserialize<PageViewEvent>(json, options),
                TelemetryDataType.CompanyScale => JsonSerializer.Deserialize<CompanyScaleEvent>(json, options),
                // Writing uses the runtime type, so a type missing here still reaches disk and
                // is then dropped on the next launch: pending events survive a restart only if
                // they can be read back. Add every new event type in both places.
                _ => null
            };
        }

        public override void Write(Utf8JsonWriter writer, TelemetryEvent value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }

    #endregion
}

/// <summary>
/// Interface for telemetry storage operations.
/// </summary>
public interface ITelemetryStorageService
{
    /// <summary>
    /// Records a telemetry event to local storage.
    /// </summary>
    Task RecordEventAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all events that haven't been uploaded yet.
    /// </summary>
    Task<IReadOnlyList<TelemetryEvent>> GetPendingEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the specified events as uploaded.
    /// </summary>
    Task MarkEventsUploadedAsync(IEnumerable<string> dataIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports all telemetry data as a JSON string for user review.
    /// </summary>
    Task<string> ExportToJsonAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all stored telemetry data.
    /// </summary>
    Task ClearAllDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about stored telemetry data.
    /// </summary>
    Task<TelemetryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves pending events to a local backup file. This is used when upload fails
    /// to ensure data is preserved locally for later upload attempts.
    /// </summary>
    /// <returns>The path to the saved backup file, or null if there was nothing to save.</returns>
    Task<string?> SaveBackupFileAsync(CancellationToken cancellationToken = default);
}
