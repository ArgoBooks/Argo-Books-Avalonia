using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Shared.Telemetry;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// One run of the app has to produce one SessionEnd.
///
/// Two paths close a session. Closing the window ends it, and applying an update ends it
/// before handing over to the installer, which kills the process outright. When an install
/// is started and then fails, the app stays open and the window close runs as well, so both
/// paths can reach the same session.
/// </summary>
public class TelemetrySessionEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "argo-session-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class TempPlatform(string root) : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => root;
        public string GetTempPath() => Path.Combine(root, "temp");
        public string GetCachePath() => Path.Combine(root, "cache");
        public void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);
        public bool SupportsFileSystem => true;
        public bool SupportsNativeDialogs => false;
        public bool SupportsBiometrics => false;
        public Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("Not supported");
        public Task<bool> AuthenticateWithBiometricAsync(string reason) => Task.FromResult(false);
        public void StorePasswordForBiometric(string fileId, string password) { }
        public string? GetPasswordForBiometric(string fileId) => null;
        public void ClearPasswordForBiometric(string fileId) { }
        public bool SupportsAutoUpdate => false;
        public int MaxRecentCompanies => 10;
        public string NormalizePath(string path) => path;
        public string CombinePaths(params string[] paths) => Path.Combine(paths);
        public string GetMachineId() => "test-machine-id";
        public StringComparer PathComparer => StringComparer.Ordinal;
    }

    private sealed class RecordingStorage : ITelemetryStorageService
    {
        public List<TelemetryEvent> Events { get; } = [];
        public Task RecordEventAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(telemetryEvent);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<TelemetryEvent>> GetPendingEventsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TelemetryEvent>>([]);
        public Task MarkEventsUploadedAsync(IEnumerable<string> dataIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task ClearAllDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TelemetryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new TelemetryStatistics());
        public Task<string?> SaveBackupFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class NoUpload : ITelemetryUploadService
    {
        public Task<TelemetryUploadResult> UploadPendingDataAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new TelemetryUploadResult());
    }

    private sealed class SilentLogger : IErrorLogger
    {
        public void LogError(Exception exception, ErrorCategory category, string? context = null, string callerFile = "", int callerLine = 0, string callerMember = "") { }
        public void LogError(string message, ErrorCategory category, string? context = null, string callerFile = "", int callerLine = 0, string callerMember = "") { }
        public void LogWarning(string message, string? context = null, ErrorCategory category = ErrorCategory.Unknown, string? code = null, string callerFile = "", int callerLine = 0, string callerMember = "") { }
        public void LogInfo(string message) { }
        public void LogDebug(string message) { }
        public IReadOnlyList<ErrorLogEntry> GetRecentErrors(int count = 50) => [];
        public event EventHandler<ErrorLogEntry>? ErrorLogged { add { } remove { } }
    }

    private sealed class NoGeo : IGeoLocationService
    {
        public Task<GeoLocationData> GetLocationAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new GeoLocationData());
    }

    private (TelemetryManager Manager, RecordingStorage Storage) Build()
    {
        var storage = new RecordingStorage();
        var manager = new TelemetryManager(
            storage, new NoUpload(), new NoGeo(), new SilentLogger(),
            appVersion: "9.9.9", platformService: new TempPlatform(_root));
        return (manager, storage);
    }

    private static int CountEnds(RecordingStorage storage) =>
        storage.Events.OfType<SessionEvent>().Count(e => e.Action == SessionAction.SessionEnd);

    [Fact]
    public async Task EndingASessionTwiceStillFilesOneEnd()
    {
        var (manager, storage) = Build();
        await manager.InitializeAsync();

        await manager.EndSessionAsync();
        await manager.EndSessionAsync();

        Assert.Equal(1, CountEnds(storage));
    }

    /// <summary>
    /// The sentinel is what the next launch reads to decide a run died. Ending the session
    /// has to remove it, or an update, which closes the session and then exits the process,
    /// is reported as a crash.
    /// </summary>
    [Fact]
    public async Task EndingASessionLeavesNoSentinelBehind()
    {
        var (manager, _) = Build();
        await manager.InitializeAsync();

        var sessions = Path.Combine(_root, "telemetry", "sessions");
        Assert.True(Directory.Exists(sessions) && Directory.GetFiles(sessions).Length > 0,
            "the session should be marked in progress on disk while it runs");

        await manager.EndSessionAsync();

        Assert.Empty(Directory.GetFiles(sessions));
    }
}
