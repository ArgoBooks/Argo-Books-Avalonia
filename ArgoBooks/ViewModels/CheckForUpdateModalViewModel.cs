using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Check for Update modal.
/// Drives the full update lifecycle: check → download → install.
/// When no <see cref="IUpdateService"/> is provided (design-time or browser),
/// falls back to a no-op stub so the UI still renders.
/// </summary>
public partial class CheckForUpdateModalViewModel : ViewModelBase
{
    private readonly IUpdateService? _updateService;

    #region Observable Properties

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _currentVersion = AppInfo.Version;

    [ObservableProperty]
    private string _newVersion = "";

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isUpToDate;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private bool _isReadyToInstall;

    #endregion

    /// <summary>
    /// Design-time / fallback constructor (no update service).
    /// </summary>
    public CheckForUpdateModalViewModel()
    {
        _isUpToDate = true;
    }

    /// <summary>
    /// Production constructor with a real update service.
    /// </summary>
    /// <param name="updateService">The platform update service.</param>
    public CheckForUpdateModalViewModel(IUpdateService updateService) : this()
    {
        _updateService = updateService;
        _isUpToDate = false; // Don't assume up-to-date before checking

        _updateService.DownloadProgressChanged += (_, progress) =>
        {
            DownloadProgress = progress;
        };
    }

    /// <summary>
    /// Opens the modal and starts checking for updates.
    /// If an update is already known (from background check), skips the check
    /// and starts downloading immediately.
    /// If an operation is already in progress (e.g. download running), just shows the modal.
    /// </summary>
    [RelayCommand]
    private async Task Open()
    {
        IsOpen = true;

        // If something is already in progress, just show the current state
        if (IsDownloading || IsChecking || IsReadyToInstall)
            return;

        if (HasUpdate)
        {
            // Update already known from background check, start downloading
            await DownloadUpdate();
        }
        else
        {
            await CheckForUpdates();
        }
    }

    /// <summary>
    /// Opens the modal and immediately starts downloading the known update.
    /// Used when the user clicks "Download now" from the update banner.
    /// If a download is already in progress, just shows the modal.
    /// </summary>
    [RelayCommand]
    private async Task OpenAndDownload()
    {
        IsOpen = true;

        // If already downloading or done, just show the current state
        if (IsDownloading || IsReadyToInstall)
            return;

        await DownloadUpdate();
    }

    /// <summary>
    /// Opens the modal and takes the update as far as it can without another click: checks if
    /// the background check has not found one yet, then downloads. Used when a company file
    /// needs a newer version, so the user has already said they want the update.
    /// </summary>
    [RelayCommand]
    private async Task OpenAndUpdate()
    {
        IsOpen = true;

        if (IsDownloading || IsChecking || IsReadyToInstall)
            return;

        if (!HasUpdate)
            await CheckForUpdates();

        if (HasUpdate)
            await DownloadUpdate();
    }

    /// <summary>
    /// Closes the modal. Any in-progress download continues in the background.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Checks for updates using the update service.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdates()
    {
        ResetStates();
        IsChecking = true;

        try
        {
            if (_updateService == null)
            {
                // No update service, always report up-to-date
                await Task.Delay(1000);
                IsUpToDate = true;
                return;
            }

            // Run the check and a minimum delay in parallel so
            // "Checking for updates..." is visible long enough to read
            var checkTask = _updateService.CheckForUpdateAsync();
            var delayTask = Task.Delay(1000);
            var update = await checkTask;
            await delayTask;

            if (update != null)
            {
                NewVersion = $"V.{update.Version}";
                HasUpdate = true;
            }
            else if (_updateService.State == UpdateState.Error)
            {
                ErrorMessage = _updateService.LastError ?? "Unknown error";
                HasError = true;
            }
            else
            {
                IsUpToDate = true;
            }

        }
        catch
        {
            HasError = true;
            ErrorMessage = _updateService?.LastError ?? "Unable to check for updates";
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Downloads the available update.
    /// </summary>
    [RelayCommand]
    private async Task DownloadUpdate()
    {
        if (_updateService == null) return;

        ResetStates();
        IsDownloading = true;
        DownloadProgress = 0;

        try
        {
            await _updateService.DownloadUpdateAsync();
            IsReadyToInstall = true;
            IsDownloading = false;
        }
        catch
        {
            IsDownloading = false;
            HasError = true;
            ErrorMessage = _updateService.LastError ?? "Download failed";
        }
    }

    /// <summary>
    /// Applies the downloaded update and restarts the application.
    /// </summary>
    [RelayCommand]
    private void ApplyUpdate()
    {
        if (_updateService == null) return;

        try
        {
            _updateService.ApplyUpdateAndRestart();
        }
        catch
        {
            ResetStates();
            HasError = true;

            // Show the installer path so the user can install manually
            var path = _updateService.InstallerPath;
            ErrorMessage = !string.IsNullOrEmpty(path)
                ? $"Automatic install failed. Please run the installer manually from:\n{path}"
                : _updateService.LastError ?? "Failed to apply update";
        }
    }

    /// <summary>
    /// Views the release notes in the default browser.
    /// </summary>
    [RelayCommand]
    private void ViewReleaseNotes()
    {
        var url = _updateService?.AvailableUpdate?.ReleaseNotesUrl ?? $"{ApiConfig.BaseUrl}/whats-new/";

        UrlHelper.SafeOpenUrl(url);
    }

    /// <summary>
    /// Sets HasUpdate from an external background check (e.g., on startup).
    /// </summary>
    internal void NotifyUpdateAvailable(UpdateInfo update)
    {
        NewVersion = $"V.{update.Version}";
        ResetStates();
        HasUpdate = true;
    }

    /// <summary>
    /// Resets all mutual-exclusive state flags.
    /// </summary>
    private void ResetStates()
    {
        IsChecking = false;
        IsUpToDate = false;
        HasUpdate = false;
        HasError = false;
        IsDownloading = false;
        IsReadyToInstall = false;
        ErrorMessage = "";
    }
}
