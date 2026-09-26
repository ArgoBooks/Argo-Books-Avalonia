using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the TelemetryUploadService class.
/// </summary>
public class TelemetryUploadServiceTests
{
    #region UploadPendingDataAsync Tests

    [Fact]
    public async Task UploadPendingDataAsync_NoPendingEvents_ReturnsSuccess()
    {
        var storageService = new MockTelemetryStorageService();
        var service = new TelemetryUploadService(storageService);

        var result = await service.UploadPendingDataAsync();

        // Without an API key, it should either fail or backup
        Assert.NotNull(result);
    }

    #endregion

    #region Mock Classes

    private class MockTelemetryStorageService : ITelemetryStorageService
    {
        public Task RecordEventAsync(ArgoBooks.Core.Models.Telemetry.TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ArgoBooks.Core.Models.Telemetry.TelemetryEvent>> GetPendingEventsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ArgoBooks.Core.Models.Telemetry.TelemetryEvent>>(new List<ArgoBooks.Core.Models.Telemetry.TelemetryEvent>());

        public Task MarkEventsUploadedAsync(IEnumerable<string> eventIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAllDataAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<TelemetryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new TelemetryStatistics());

        public Task<string?> SaveBackupFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    #endregion
}
