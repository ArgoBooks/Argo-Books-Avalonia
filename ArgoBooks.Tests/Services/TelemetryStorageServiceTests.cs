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

    /// <summary>
    /// Uploaded events are dropped from the file so it stays small; the pending ones must stay.
    /// </summary>
    [Fact]
    public async Task MarkEventsUploadedAsync_DropsUploadedEventsAndKeepsPending()
    {
        var platformService = new MockPlatformService();
        var service = new TelemetryStorageService(platformService);
        var uploaded = new SessionEvent { Action = SessionAction.SessionStart };
        var pending = new SessionEvent { Action = SessionAction.SessionStart };
        await service.RecordEventAsync(uploaded);
        await service.RecordEventAsync(pending);

        await service.MarkEventsUploadedAsync([uploaded.DataId]);

        Assert.Single(ReadEventsFile(platformService));
        Assert.Equal(pending.DataId, Assert.Single(await service.GetPendingEventsAsync()).DataId);
        Assert.Equal(1, (await service.GetStatisticsAsync()).TotalEventsEverUploaded);
    }

    /// <summary>
    /// Past 16KB the async reader hands the converter a partial buffer, where Utf8JsonReader.Skip
    /// throws. A converter relying on it failed every load of a real-sized file.
    /// </summary>
    [Fact]
    public async Task GetPendingEventsAsync_FileWellOver16KB_LoadsEveryEvent()
    {
        var platformService = new MockPlatformService();
        var service = new TelemetryStorageService(platformService);
        await service.RecordEventAsync(new SessionEvent { Action = SessionAction.SessionStart });
        await service.RecordEventAsync(new PageViewEvent { PageName = "Dashboard", ActiveSeconds = 12, DurationSeconds = 30 });
        await service.RecordEventAsync(new FeatureUsageEvent { Context = "Monthly revenue chart", DurationMs = 250 });
        await service.RecordEventAsync(new ErrorEvent { ErrorCode = "IOException", Message = "The file is in use" });

        var templates = ReadEventsFile(platformService);
        var events = new JsonArray();
        for (var i = 0; i < 3000; i++)
        {
            var entry = templates[i % templates.Count]!.DeepClone();
            entry["event"]!["dataId"] = $"event{i:D6}";
            events.Add(entry);
        }
        WriteEventsFile(platformService, events);
        Assert.True(new FileInfo(EventsPath(platformService)).Length > 16 * 1024 * 10);

        var pending = await new TelemetryStorageService(platformService).GetPendingEventsAsync();

        Assert.Equal(3000, pending.Count);
        Assert.Equal(750, pending.OfType<ErrorEvent>().Count());
        Assert.Equal(3000, pending.Select(e => e.DataId).Distinct().Count());
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

    #region Mock Classes

    private class MockPlatformService : IPlatformService
    {
        // One directory per mock instance, not one per call.
        private readonly string _appDataPath =
            Path.Combine(Path.GetTempPath(), "ArgoBooks_Test_" + Guid.NewGuid().ToString("N")[..8]);

        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => _appDataPath;
        public string GetTempPath() => Path.GetTempPath();
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
        public StringComparer PathComparer => StringComparer.Ordinal;
    }

    #endregion
}
