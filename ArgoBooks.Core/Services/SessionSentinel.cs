using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Marks a telemetry session as "in progress" on disk so a run that never closes
/// cleanly can still be reported on the next launch.
///
/// <para>
/// A session that ends normally records its own SessionEnd. A session killed from
/// Task Manager, ended by an OS restart, or lost to a power cut records nothing, so
/// the server sees a SessionStart with no matching end and no duration at all. This
/// class leaves a small file behind for the lifetime of the session and deletes it on
/// a clean exit, so a leftover file on the next launch means the previous run died.
/// </para>
///
/// <para>
/// Liveness is decided by an <em>open handle</em>, not by the file existing: the file
/// is held with <see cref="FileShare.Read"/> for the whole session, so another process
/// can read it but cannot take it exclusively. The sweep in
/// <see cref="CollectOrphans"/> tries for an exclusive handle, which succeeds only when
/// no process holds the file. That distinction matters because multiple instances are
/// supported deliberately (see <see cref="AtomicFile.TempPathFor"/>), so a live sibling
/// instance's file must never be mistaken for a dead run's. Deliberately NOT
/// <see cref="FileOptions.DeleteOnClose"/>, which is what
/// <see cref="CompanyInstanceLock"/> uses: that would erase the evidence at the exact
/// moment we need it to survive.
/// </para>
///
/// <para>
/// The heartbeat is what makes the recovered event useful. Without it we would know a
/// session died but not when, so it could carry no duration. Rewriting the timestamp
/// every minute puts the recovered duration within one heartbeat of the truth.
/// </para>
/// </summary>
public sealed class SessionSentinel : IDisposable
{
    private const string TelemetryDirectory = "telemetry";
    private const string SessionsDirectory = "sessions";

    /// <summary>
    /// Ceiling on how many dead sessions one launch reports. A machine that somehow
    /// accumulated hundreds must not produce hundreds of events in a single upload.
    /// Every swept file is deleted regardless; only the reporting is capped, and the
    /// drop is logged rather than silent.
    /// </summary>
    private const int MaxOrphansPerSweep = 20;

    /// <summary>Sentinels older than this are cleaned up but not reported: too stale to be worth attributing.</summary>
    private static readonly TimeSpan MaxOrphanAge = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly SessionRecord _record;
    private FileStream? _stream;
    private string? _path;

    private SessionSentinel(FileStream stream, string path, SessionRecord record)
    {
        _stream = stream;
        _path = path;
        _record = record;
    }

    /// <summary>
    /// Starts a sentinel for the session beginning at <paramref name="startedUtc"/>.
    /// Returns null if the file could not be created, in which case unclean exits simply
    /// go undetected: telemetry itself is unaffected, so this never blocks a launch.
    /// </summary>
    public static SessionSentinel? Begin(
        IPlatformService platformService,
        DateTime startedUtc,
        string? appVersion,
        IErrorLogger? errorLogger = null)
    {
        try
        {
            var directory = GetSessionsDirectory(platformService);
            platformService.EnsureDirectoryExists(directory);

            var record = new SessionRecord
            {
                SessionId = Guid.NewGuid().ToString("N"),
                StartedUtc = startedUtc,
                LastHeartbeatUtc = startedUtc,
                AppVersion = appVersion,
            };

            var path = Path.Combine(directory, record.SessionId + ".json");

            // FileShare.Read: readable by the sweep's probe, but not claimable exclusively
            // while this process lives. CreateNew because the name is a fresh GUID.
            var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.WriteThrough);

            var sentinel = new SessionSentinel(stream, path, record);
            sentinel.Heartbeat(startedUtc);
            return sentinel;
        }
        catch (Exception ex)
        {
            // LogDebug compiles to nothing in release, so a missing sentinel leaves no trace and that machine reports session starts without ends.
            errorLogger?.LogWarning(
                $"Could not start session sentinel: {ex.Message}",
                nameof(SessionSentinel),
                ErrorCategory.FileSystem,
                "SentinelStartFailed");
            return null;
        }
    }

    /// <summary>
    /// Stamps the current time into the sentinel so a session that dies later can still
    /// report how long it ran. Best-effort and never throws.
    /// </summary>
    public void Heartbeat(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (_stream is null)
            {
                return;
            }

            try
            {
                _record.LastHeartbeatUtc = nowUtc;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(_record, JsonOptions);

                // Overwrite in place, then trim, rather than truncating first.
                _stream.Position = 0;
                _stream.Write(bytes, 0, bytes.Length);
                _stream.SetLength(bytes.Length);
                _stream.Flush();
            }
            catch (Exception)
            {
                // A sentinel that cannot be updated is not worth failing a session over. The stale timestamp only costs precision on a duration we would not otherwise have at all.
            }
        }
    }

    /// <summary>
    /// Marks the session as cleanly finished by removing the sentinel. Call only once the
    /// SessionEnd event is durably recorded: if this runs first and recording then fails,
    /// the session is lost entirely rather than recovered as unclean.
    /// </summary>
    public void Complete()
    {
        lock (_gate)
        {
            var path = _path;
            ReleaseHandle();

            if (path is null)
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Left behind, so the next launch reports this session as unclean when it was not.
            }
        }
    }

    /// <summary>
    /// Releases the handle WITHOUT deleting the file, so a teardown that never reached
    /// <see cref="Complete"/> is correctly reported as an unclean exit.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            ReleaseHandle();
        }
    }

    private void ReleaseHandle()
    {
        try
        {
            _stream?.Dispose();
        }
        catch (Exception)
        {
            // The OS releases the handle on process exit regardless.
        }

        _stream = null;
        _path = null;
    }

    /// <summary>
    /// Finds sessions from previous runs that ended without a clean shutdown, newest
    /// first, and removes every sentinel it sweeps. Files still held by a live instance
    /// are left untouched.
    /// </summary>
    public static IReadOnlyList<OrphanedSession> CollectOrphans(
        IPlatformService platformService,
        IErrorLogger? errorLogger = null)
    {
        var found = new List<OrphanedSession>();

        try
        {
            var directory = GetSessionsDirectory(platformService);
            if (!Directory.Exists(directory))
            {
                return found;
            }

            // GetFiles, not EnumerateFiles: the read below deletes each file as it closes,
            // and mutating the directory part-way through a lazy enumeration is not safe.
            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                var record = TryClaimAndRead(path);
                if (record is null || record.StartedUtc == default)
                {
                    continue;
                }

                if (DateTime.UtcNow - record.StartedUtc > MaxOrphanAge)
                {
                    continue;
                }

                found.Add(new OrphanedSession
                {
                    StartedUtc = record.StartedUtc,
                    // A sentinel killed before its first heartbeat landed would otherwise
                    // produce a negative duration.
                    LastHeartbeatUtc = record.LastHeartbeatUtc < record.StartedUtc
                        ? record.StartedUtc
                        : record.LastHeartbeatUtc,
                    AppVersion = record.AppVersion,
                });
            }
        }
        catch (Exception ex)
        {
            // Same reasoning as the start path: a sweep that cannot run means unclean
            // sessions are never reported, which is indistinguishable from there being none.
            errorLogger?.LogWarning(
                $"Could not sweep session sentinels: {ex.Message}",
                nameof(SessionSentinel),
                ErrorCategory.FileSystem,
                "SentinelSweepFailed");
        }

        if (found.Count <= MaxOrphansPerSweep)
        {
            return found;
        }

        errorLogger?.LogWarning(
            $"Found {found.Count} unclean sessions; reporting the {MaxOrphansPerSweep} most recent.",
            "Telemetry");

        return found
            .OrderByDescending(o => o.StartedUtc)
            .Take(MaxOrphansPerSweep)
            .ToList();
    }

    /// <summary>
    /// Takes the sentinel exclusively (proving no process holds it), reads it, and deletes
    /// it on close. Returns null when a live instance holds the file, when it is corrupt,
    /// or when it cannot be opened at all.
    /// </summary>
    private static SessionRecord? TryClaimAndRead(string path)
    {
        try
        {
            // FileShare.None is the liveness test: it fails while any process has the file open.
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<SessionRecord>(json, JsonOptions);
        }
        catch (IOException)
        {
            // Sharing violation: a live instance owns this session. Expected, not an error.
            return null;
        }
        catch (Exception)
        {
            // Corrupt JSON, permissions, a vanished file. Skip it; the next launch retries
            // unless DeleteOnClose already removed it.
            return null;
        }
    }

    private static string GetSessionsDirectory(IPlatformService platformService) =>
        platformService.CombinePaths(
            platformService.GetAppDataPath(),
            TelemetryDirectory,
            SessionsDirectory);

    /// <summary>On-disk shape of a sentinel.</summary>
    private sealed class SessionRecord
    {
        public string? SessionId { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime LastHeartbeatUtc { get; set; }
        public string? AppVersion { get; set; }
    }
}

/// <summary>
/// A previous session that ended without a clean shutdown, recovered from its sentinel.
/// </summary>
public sealed class OrphanedSession
{
    /// <summary>When the dead session started.</summary>
    public DateTime StartedUtc { get; init; }

    /// <summary>Last time the dead session was known to be alive, within one heartbeat.</summary>
    public DateTime LastHeartbeatUtc { get; init; }

    /// <summary>Version that ran the dead session, which may predate the current build.</summary>
    public string? AppVersion { get; init; }

    /// <summary>How long the dead session ran, to the nearest heartbeat.</summary>
    public long DurationSeconds
    {
        get
        {
            var seconds = (long)(LastHeartbeatUtc - StartedUtc).TotalSeconds;
            return seconds < 0 ? 0 : seconds;
        }
    }
}
