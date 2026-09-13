using System.Text.Json.Nodes;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the TelemetryStorageService class.
/// </summary>
public class TelemetryStorageServiceTests
{
    #region GetPendingEventsAsync Tests

    [Fact]
    public async Task GetPendingEventsAsync_NewService_ReturnsEmptyList()
    {
        var platformService = new MockPlatformService();
        var service = new TelemetryStorageService(platformService);

        var events = await service.GetPendingEventsAsync();

        Assert.NotNull(events);
        Assert.Empty(events);
    }

    private static string EventsPath(MockPlatformService platform) =>
        Path.Combine(platform.GetAppDataPath(), "telemetry", "events.json");

    private static JsonArray ReadEventsFile(MockPlatformService platform) =>
        JsonNode.Parse(File.ReadAllText(EventsPath(platform)))!.AsArray();

    private static void WriteEventsFile(MockPlatformService platform, JsonArray events) =>
        File.WriteAllText(EventsPath(platform), events.ToJsonString());

    /// <summary>
    /// A file written by a newer build can hold a value this one does not know. Dropping that
    /// one event is fine. Keeping it as an empty entry was not: every later read of the list
    /// failed on it, so nothing pending was ever uploaded again.
    /// </summary>
    [Fact]
    public async Task GetPendingEventsAsync_AnUnreadableEvent_CostsOnlyThatEvent()
    {
        var platformService = new MockPlatformService();
        var service = new TelemetryStorageService(platformService);
        var kept = new SessionEvent { Action = SessionAction.SessionStart };
        await service.RecordEventAsync(new SessionEvent { Action = SessionAction.SessionStart });
        await service.RecordEventAsync(kept);

        JsonArray events = ReadEventsFile(platformService);
        events[0]!["event"]!["action"] = "AnActionFromANewerBuild";
        WriteEventsFile(platformService, events);

        var pending = await service.GetPendingEventsAsync();
        Assert.Equal(kept.DataId, Assert.Single(pending).DataId);

        await service.MarkEventsUploadedAsync([kept.DataId]);
        Assert.Empty(await service.GetPendingEventsAsync());
    }

    [Fact]
    public async Task GetPendingEventsAsync_AnEventOfAnUnknownType_CostsOnlyThatEvent()
    {
        var platformService = new MockPlatformService();
        var service = new TelemetryStorageService(platformService);
        var kept = new SessionEvent { Action = SessionAction.SessionStart };
        await service.RecordEventAsync(new SessionEvent { Action = SessionAction.SessionStart });
        await service.RecordEventAsync(kept);

        JsonArray events = ReadEventsFile(platformService);
        events[0]!["event"]!["dataType"] = "ATypeFromANewerBuild";
        WriteEventsFile(platformService, events);

        var pending = await service.GetPendingEventsAsync();

        Assert.Equal(kept.DataId, Assert.Single(pending).DataId);
    }

    #endregion

    #region GetStatisticsAsync Tests

    [Fact]
    public async Task GetStatisticsAsync_NewService_ReturnsStatistics()
    {
        var platformService = new MockPlatformService();
        var service = new TelemetryStorageService(platformService);

        var stats = await service.GetStatisticsAsync();

        Assert.NotNull(stats);
    }

    #endregion

    #region ClearAllDataAsync Tests

    [Fact]
    public async Task ClearAllDataAsync_EmptyService_DoesNotThrow()
    {
        var platformService = new MockPlatformService();
        var service = new TelemetryStorageService(platformService);

        await service.ClearAllDataAsync();

        var events = await service.GetPendingEventsAsync();
        Assert.Empty(events);
    }

    #endregion

    #region ExportToJsonAsync Tests

    [Fact]
    public async Task ExportToJsonAsync_EmptyService_ReturnsValidJson()
    {
        var platformService = new MockPlatformService();
        var service = new TelemetryStorageService(platformService);

        var json = await service.ExportToJsonAsync();

        Assert.NotNull(json);
        Assert.False(string.IsNullOrEmpty(json));
    }

    #endregion

    #region Mock Classes

    private class MockPlatformService : IPlatformService
    {
        // One directory per mock instance, not one per call. As an expression-bodied member
        // this handed out a fresh GUID every time it was read, so the events file was written
        // to one directory and looked for in another: nothing could ever be read back, and
        // every test passed by finding an empty store no matter what had been recorded.
        private readonly string _appDataPath =
            Path.Combine(Path.GetTempPath(), "ArgoBooks_Test_" + Guid.NewGuid().ToString("N")[..8]);

        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => _appDataPath;
        public string GetTempPath() => Path.GetTempPath();
        public string GetDefaultDocumentsPath() => Path.GetTempPath();
        public string GetLogsPath() => Path.GetTempPath();
        public string GetCachePath() => Path.GetTempPath();
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
        public void RegisterFileTypeAssociations(string iconPath) { }
        public StringComparer PathComparer => StringComparer.Ordinal;
    }

    #endregion
}
