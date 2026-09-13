using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Interface for the central telemetry management service.
/// </summary>
public interface ITelemetryManager
{
    /// <summary>
    /// Initializes the telemetry manager and starts a new session.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the current session (called on app shutdown).
    /// </summary>
    Task EndSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Note that the user just did something. Called from the window's input handlers, so
    /// it has to stay cheap and synchronous: it runs on the UI thread ahead of every key
    /// press and click.
    /// </summary>
    void MarkActivity();

    /// <summary>
    /// Remember which page is on screen, so a session can report where it ended.
    /// Cheap and synchronous: it runs on every navigation.
    /// </summary>
    void NoteCurrentPage(string? pageName);

    /// <summary>
    /// Tracks a feature usage event.
    /// </summary>
    Task TrackFeatureAsync(FeatureName featureName, string? context = null, long? durationMs = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a completed visit to one screen. Raised by <see cref="NoteCurrentPage"/> when
    /// the page changes and at session end, so callers never invoke this directly.
    /// </summary>
    Task TrackPageViewAsync(string pageName, long activeSeconds, long durationSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks an export operation.
    /// </summary>
    Task TrackExportAsync(ExportType exportType, long durationMs, long fileSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks an API call.
    /// </summary>
    Task TrackApiCallAsync(ApiName apiName, long durationMs, bool success, string? model = null, int? tokensUsed = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks an error event from the error logger.
    /// </summary>
    Task TrackErrorAsync(ErrorLogEntry errorEntry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the business behind the company that is open. Personal data, unlike every
    /// other call here: see <see cref="CompanyProfileEvent"/>. Ignores repeat calls for the
    /// same company within a session, so callers can fire it on every open without
    /// producing duplicates. Not called for the sample company: its details are the demo
    /// file's, not the user's.
    /// </summary>
    Task TrackCompanyProfileAsync(
        string? companyName,
        string? businessType,
        string? industry,
        string? country,
        string? currency,
        string? language,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records how much is in the open company file. Ignores repeat calls for the same company
    /// within a session, so callers can fire it from the company-opened path, which also runs
    /// on save and on returning from settings. Not called for the sample company: its contents
    /// ship with the demo file and are identical on every install.
    /// </summary>
    Task TrackCompanyScaleAsync(CompanyScaleCounts counts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records how long this launch took. Called once, when the main window opens.
    /// </summary>
    Task TrackStartupAsync(
        long? toFirstPaintMs,
        long? toServicesReadyMs,
        long? toViewModelsReadyMs,
        long? toReadyMs,
        bool coldStart,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads all pending telemetry data to the server.
    /// </summary>
    Task<TelemetryUploadResult> UploadPendingDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all collected telemetry data.
    /// </summary>
    Task ClearAllDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about collected telemetry data.
    /// </summary>
    Task<TelemetryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
