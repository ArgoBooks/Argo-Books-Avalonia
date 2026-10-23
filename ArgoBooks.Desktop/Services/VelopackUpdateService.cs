using System.Diagnostics;
using ArgoBooks.Core.Services;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace ArgoBooks.Desktop.Services;

/// <summary>
/// Updates the desktop app through Velopack: downloads the new version, checks its signature, and restarts into it.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    /// <summary>
    /// Velopack's id for the app. On Windows it is also the install folder under %LocalAppData%, which an uninstall deletes, so it must differ from the ArgoBooks folder that holds the app's own data.
    /// </summary>
    public const string PackId = "ArgoBooksApp";

    /// <summary>
    /// The folder holding the update lists, packages and signatures. The sandbox has its own, so an update under test can never reach real users even when both hosts serve the same directory.
    /// </summary>
    private static readonly string FeedUrl = $"{ApiConfig.BaseUrl}/resources/" + (ApiConfig.IsSandbox ? "updates-sandbox" : "updates");

    /// <summary>
    /// The public half of the Ed25519 key every update file is signed with. A Debug build checks the sandbox folder, so it trusts the sandbox key instead.
    /// </summary>
#if DEBUG
    private const string UpdatePublicKey = "rWJz6TYW5NWY3G8DACjXC1rMLUT5tVA67DMXv8ZCmj4=";
#else
    private const string UpdatePublicKey = "Of6Zmn6HdF02vdJCJB6nutS4ceAPmIpC7fOAXZrb4no=";
#endif

    /// <summary>The Windows installer every release publishes under the same name, which a copy from the old installer downloads to move over.</summary>
    private const string WindowsSetupFileName = PackId + "-win-Setup.exe";

    private readonly IErrorLogger? _errorLogger;
    private readonly SignedWebSource _source;
    private readonly CleaningUpdateManager _manager;
    private readonly Task _cleanup;
    private Velopack.UpdateInfo? _pending;
    private string? _setupPath;

    /// <inheritdoc />
    public UpdateState State { get; private set; } = UpdateState.Idle;

    /// <inheritdoc />
    public Core.Services.UpdateInfo? AvailableUpdate { get; private set; }

    /// <inheritdoc />
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public string? InstallerPath => _setupPath;

    /// <inheritdoc />
    public event EventHandler<UpdateState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<int>? DownloadProgressChanged;

    /// <inheritdoc />
    public event EventHandler<ApplyingUpdateEventArgs>? ApplyingUpdate;

    public VelopackUpdateService(IErrorLogger? errorLogger = null)
    {
        _errorLogger = errorLogger;
        _source = new SignedWebSource(FeedUrl, UpdatePublicKey);
        // Only whole packages are signed, so partial updates rebuilt from deltas would install without a signature check.
        _manager = new CleaningUpdateManager(_source, new UpdateOptions { MaximumDeltasBeforeFallback = -1 });
        _cleanup = Task.Run(RemoveInstalledPackages);
    }

    /// <summary>Deletes installed versions' update packages, which only delta updates need and this app installs whole packages only.</summary>
    private void RemoveInstalledPackages()
    {
        try
        {
            if (_manager.IsInstalled)
                _manager.RemovePackagesExceptPending();
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning($"Could not remove installed update packages: {ex.Message}", "AutoUpdate");
        }
    }

    /// <summary>Exposes Velopack's package cleanup, which it otherwise runs only when the next update downloads.</summary>
    private sealed class CleaningUpdateManager(IUpdateSource source, UpdateOptions options) : UpdateManager(source, options)
    {
        // A downloaded update that has not been installed yet is kept, so the user does not have to download it again.
        public void RemovePackagesExceptPending() => CleanPackagesExcept(UpdatePendingRestart?.FileName);
    }

    /// <summary>
    /// A Windows copy installed by the old Advanced Installer installer, which Velopack cannot update. Its update is the move to a Velopack install.
    /// </summary>
    private bool IsOldWindowsInstall => OperatingSystem.IsWindows() && !_manager.IsInstalled && RunsFromProgramFiles() && OldWindowsInstall.IsPresent();

    // The old installer put the app in Program Files, so a build run from the IDE on a PC that also has it installed is left alone.
    private static bool RunsFromProgramFiles()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;

        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath)
            .Any(folder => folder.Length > 0 && exe.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public bool IsMoveToNewInstall => IsOldWindowsInstall;

    /// <inheritdoc />
    public bool HasOldInstallToRemove => OperatingSystem.IsWindows() && _manager.IsInstalled && OldWindowsInstall.IsPresent();

    /// <inheritdoc />
    public Task<bool> RemoveOldInstallAsync() =>
        OperatingSystem.IsWindows() ? Task.Run(OldWindowsInstall.Remove) : Task.FromResult(false);

    /// <inheritdoc />
    public async Task<Core.Services.UpdateInfo?> CheckForUpdateAsync()
    {
        // The cleanup must not run while an update downloads, and every download starts from a check.
        await _cleanup;
        SetState(UpdateState.Checking);
        AvailableUpdate = null;
        LastError = null;
        _pending = null;

        try
        {
            if (IsOldWindowsInstall)
            {
                // The same version counts, because the bridge release ships both installers at one version.
                var latest = await LatestReleaseAsync("win");
                if (latest == null || (SemanticVersion.TryParse(AppInfo.VersionNumber, out var running) && latest.Version < running))
                    return UpToDate();

                AvailableUpdate = new Core.Services.UpdateInfo
                {
                    Version = latest.Version.ToString(),
                    DownloadUrl = _source.FileUrl(WindowsSetupFileName)
                };
                SetState(UpdateState.UpdateAvailable);
                return AvailableUpdate;
            }

            // A build run from the IDE, which Velopack did not install and cannot update.
            if (!_manager.IsInstalled)
                return UpToDate();

            var update = await _manager.CheckForUpdatesAsync();
            if (update == null)
                return UpToDate();

            _pending = update;
            AvailableUpdate = new Core.Services.UpdateInfo
            {
                Version = update.TargetFullRelease.Version.ToString(),
                DownloadUrl = _source.FileUrl(update.TargetFullRelease.FileName),
                FileSizeBytes = update.TargetFullRelease.Size > 0 ? update.TargetFullRelease.Size : null
            };
            SetState(UpdateState.UpdateAvailable);
            return AvailableUpdate;
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning($"Update check failed: {ex.Message}", "AutoUpdate");
            // An update check is an HTTP fetch of the update list, so a failure is almost always a connectivity problem.
            LastError = await ConnectivityMessage.ResolveAsync();
            SetState(UpdateState.Error);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DownloadUpdateAsync()
    {
        if (AvailableUpdate == null)
            throw new InvalidOperationException("No update available to download.");

        SetState(UpdateState.Downloading);
        LastError = null;

        try
        {
            if (_pending != null)
            {
                await _manager.DownloadUpdatesAsync(_pending, progress => DownloadProgressChanged?.Invoke(this, progress));
            }
            else
            {
                _setupPath = await DownloadWindowsSetupAsync();
            }

            DownloadProgressChanged?.Invoke(this, 100);
            SetState(UpdateState.ReadyToInstall);
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning($"Update download failed: {ex.Message}", "AutoUpdate");
            // A failed signature check has its own message, since telling the user to check their connection would be wrong.
            LastError = ex is UpdateSignatureException ? UpdateSignatureException.UserMessage : await ConnectivityMessage.ResolveAsync();
            SetState(UpdateState.Error);
            throw;
        }
    }

    /// <inheritdoc />
    public void ApplyUpdateAndRestart()
    {
        if (State != UpdateState.ReadyToInstall)
            throw new InvalidOperationException("No update is ready to install.");

        var applying = new ApplyingUpdateEventArgs();
        ApplyingUpdate?.Invoke(this, applying);
        if (applying.Cancel)
        {
            _errorLogger?.LogWarning("Update not applied: unsaved work could not be saved first.", "AutoUpdate");
            return;
        }

        if (_pending != null)
        {
            // Closes the app, installs the update, and opens the new version.
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
            return;
        }

        // Setup installs the Velopack copy for this user and opens it, and that copy then removes this one.
        using var setup = Process.Start(new ProcessStartInfo(_setupPath!) { UseShellExecute = true })
            ?? throw new InvalidOperationException("The Argo Books installer did not start.");
        Environment.Exit(0);
    }

    private Core.Services.UpdateInfo? UpToDate()
    {
        SetState(UpdateState.UpToDate);
        return null;
    }

    private async Task<VelopackAsset?> LatestReleaseAsync(string channel)
    {
        var feed = await _source.GetReleaseFeed(NullVelopackLogger.Instance, PackId, channel);
        return feed.Assets
            .Where(asset => asset.Type == VelopackAssetType.Full)
            .MaxBy(asset => asset.Version);
    }

    /// <summary>
    /// Downloads the Windows installer and checks its signature, for a copy moving over from the old installer.
    /// </summary>
    private async Task<string> DownloadWindowsSetupAsync()
    {
        var folder = Path.Combine(AppPaths.Temp, "Update");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, WindowsSetupFileName);
        var partial = path + ".partial";

        await _source.Downloader.DownloadFile(_source.FileUrl(WindowsSetupFileName), partial,
            progress => DownloadProgressChanged?.Invoke(this, progress), timeout: _source.Timeout);

        var signature = await _source.Downloader.DownloadString(_source.SignatureUrl(WindowsSetupFileName), timeout: _source.Timeout);
        if (!UpdateSignature.IsValid(partial, signature, UpdatePublicKey))
        {
            File.Delete(partial);
            throw new UpdateSignatureException($"{WindowsSetupFileName} does not match its signature, so it was not installed.");
        }

        File.Move(partial, path, overwrite: true);
        return path;
    }

    private void SetState(UpdateState newState)
    {
        State = newState;
        StateChanged?.Invoke(this, newState);
    }
}
