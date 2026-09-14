using ArgoBooks.Core.Platform;
using ArgoBooks.Services;
using ApiConfig = ArgoBooks.Core.Services.ApiConfig;
using AppInfo = ArgoBooks.Core.Services.AppInfo;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the help panel.
/// </summary>
public partial class HelpPanelViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _appVersion = AppInfo.Version;

    /// <summary>
    /// Default constructor for design-time.
    /// </summary>
    public HelpPanelViewModel()
    {
    }

    #region Commands

    [RelayCommand]
    private void Open()
    {
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;
    }

    [RelayCommand]
    private void OpenWhatsNew()
    {
        Close();
        OpenUrl($"{ApiConfig.BaseUrl}/whats-new/");
    }

    [RelayCommand]
    private void OpenDocumentation()
    {
        Close();
        OpenUrl($"{ApiConfig.BaseUrl}/documentation/");
    }

    [RelayCommand]
    private void OpenCommunity()
    {
        Close();
        OpenUrl($"{ApiConfig.BaseUrl}/community/");
    }

    [RelayCommand]
    private void OpenSupport()
    {
        Close();
        OpenUrl($"{ApiConfig.BaseUrl}/contact-us/");
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        Close();
        CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RestartTutorial()
    {
        Close();
        TutorialService.Instance.ResetAllTutorials();
        RestartTutorialRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowSetupChecklist()
    {
        Close();
        TutorialService.Instance.ShowSetupChecklist();
    }

    /// <summary>
    /// Opens a URL in the default browser.
    /// </summary>
    private static void OpenUrl(string url)
    {
        UrlHelper.SafeOpenUrl(url);
    }

    #endregion

    #region Events

    public event EventHandler? CheckForUpdatesRequested;
    public event EventHandler? RestartTutorialRequested;

    #endregion
}
