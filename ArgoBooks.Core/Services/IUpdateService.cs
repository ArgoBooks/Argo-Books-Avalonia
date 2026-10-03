namespace ArgoBooks.Core.Services;

/// <summary>
/// Represents information about an available update.
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>
    /// The version string of the available update (e.g., "2.1.0").
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The download URL for the update package.
    /// </summary>
    public required string DownloadUrl { get; init; }

    /// <summary>
    /// Optional URL to release notes.
    /// </summary>
    public string? ReleaseNotesUrl { get; init; }

    /// <summary>
    /// Optional file size in bytes, for progress display.
    /// </summary>
    public long? FileSizeBytes { get; init; }
}

/// <summary>
/// Represents the current state of the update system.
/// </summary>
public enum UpdateState
{
    /// <summary>No update activity.</summary>
    Idle,

    /// <summary>Currently checking for updates.</summary>
    Checking,

    /// <summary>The application is up to date.</summary>
    UpToDate,

    /// <summary>An update is available for download.</summary>
    UpdateAvailable,

    /// <summary>Currently downloading the update.</summary>
    Downloading,

    /// <summary>Download complete, ready to install and restart.</summary>
    ReadyToInstall,

    /// <summary>An error occurred during the update process.</summary>
    Error
}

/// <summary>
/// Service for checking, downloading, and applying application updates.
/// Implementations are platform-specific (e.g., NetSparkle for desktop).
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Gets the current state of the update system.
    /// </summary>
    UpdateState State { get; }

    /// <summary>
    /// Gets information about the available update, if any.
    /// Only valid when <see cref="State"/> is <see cref="UpdateState.UpdateAvailable"/>,
    /// <see cref="UpdateState.Downloading"/>, or <see cref="UpdateState.ReadyToInstall"/>.
    /// </summary>
    UpdateInfo? AvailableUpdate { get; }

    /// <summary>
    /// Gets the last error message, if <see cref="State"/> is <see cref="UpdateState.Error"/>.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Gets the path to the downloaded installer, if available.
    /// Used to inform the user for manual installation on failure.
    /// </summary>
    string? InstallerPath { get; }

    /// <summary>
    /// Checks the update server for a newer version.
    /// Sets <see cref="State"/> to <see cref="UpdateState.UpToDate"/> or <see cref="UpdateState.UpdateAvailable"/>.
    /// </summary>
    /// <returns>The update info if an update is available, null if up to date.</returns>
    Task<UpdateInfo?> CheckForUpdateAsync();

    /// <summary>
    /// Downloads the update package to a temporary location.
    /// Must be called after <see cref="CheckForUpdateAsync"/> returns a non-null result.
    /// Reports progress via <see cref="DownloadProgressChanged"/>.
    /// </summary>
    Task DownloadUpdateAsync();

    /// <summary>
    /// Launches the installer/applies the downloaded update and exits the application.
    /// Must be called after download completes (<see cref="State"/> is <see cref="UpdateState.ReadyToInstall"/>).
    /// </summary>
    void ApplyUpdateAndRestart();

    /// <summary>
    /// Raised when the update state changes.
    /// </summary>
    event EventHandler<UpdateState>? StateChanged;

    /// <summary>
    /// Raised during download to report progress (0-100).
    /// </summary>
    event EventHandler<int>? DownloadProgressChanged;

    /// <summary>
    /// Raised before the update is applied and the application exits.
    /// Subscribers should save any unsaved data, and set
    /// <see cref="ApplyingUpdateEventArgs.Cancel"/> if they could not.
    /// </summary>
    event EventHandler<ApplyingUpdateEventArgs>? ApplyingUpdate;
}

/// <summary>
/// Carries a subscriber's refusal back to the updater.
/// </summary>
public sealed class ApplyingUpdateEventArgs : EventArgs
{
    /// <summary>
    /// Set by a subscriber that could not get the user's work onto disk. Restarting over unsaved
    /// changes would discard them with nothing said, so the update is abandoned instead and the
    /// user can retry once they have saved.
    /// </summary>
    public bool Cancel { get; set; }
}
