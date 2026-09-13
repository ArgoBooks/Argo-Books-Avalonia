using System.Collections.ObjectModel;
using ArgoBooks.Core.Platform;
using ArgoBooks.Services;
using ApiConfig = ArgoBooks.Core.Services.ApiConfig;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the welcome/startup screen.
/// </summary>
public partial class WelcomeScreenViewModel : ViewModelBase
{
    /// <summary>
    /// Recent companies for quick access.
    /// </summary>
    public ObservableCollection<RecentCompanyItem> RecentCompanies { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecentCompanies))]
    private bool _hasRecentCompanies;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecentCompanies))]
    private bool _isRecentCompaniesLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecentCompanies))]
    private bool _isTutorialMode;

    /// <summary>
    /// Only show recent companies section after initial load completes and there are companies.
    /// This prevents layout shift/flicker on startup.
    /// </summary>
    public bool ShowRecentCompanies => IsRecentCompaniesLoaded && HasRecentCompanies && !IsTutorialMode;

    [ObservableProperty]
    private string _appVersion = AppInfo.Version;

    public bool IsSandboxMode => ApiConfig.IsSandbox;

    /// <summary>
    /// Default constructor for design-time.
    /// </summary>
    public WelcomeScreenViewModel()
    {
        // Only populate sample data in design mode to avoid flicker at runtime
        if (!Design.IsDesignMode)
            return;

        // Design-time defaults
        RecentCompanies.Add(new RecentCompanyItem
        {
            Name = "My Company Inc.",
            FilePath = "C:\\ArgoBooks\\MyCompany.argobk",
            Icon = "Building",
            LastOpened = DateTime.Now.AddDays(-1)
        });
        RecentCompanies.Add(new RecentCompanyItem
        {
            Name = "Side Business LLC",
            FilePath = "C:\\ArgoBooks\\SideBusiness.argobk",
            Icon = "Store",
            LastOpened = DateTime.Now.AddDays(-3)
        });
        RecentCompanies.Add(new RecentCompanyItem
        {
            Name = "Consulting Services",
            FilePath = "C:\\ArgoBooks\\Consulting.argobk",
            Icon = "Briefcase",
            LastOpened = DateTime.Now.AddDays(-7)
        });
        HasRecentCompanies = RecentCompanies.Count > 0;
        IsRecentCompaniesLoaded = true;
    }

    #region Commands

    /// <summary>
    /// Creates a new company.
    /// </summary>
    [RelayCommand]
    private void CreateNewCompany()
    {
        CreateNewCompanyRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens an existing company file.
    /// </summary>
    [RelayCommand]
    private void OpenCompany()
    {
        OpenCompanyRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens a recent company.
    /// </summary>
    [RelayCommand]
    private void OpenRecentCompany(RecentCompanyItem? company)
    {
        if (company == null) return;
        OpenRecentCompanyRequested?.Invoke(this, company);
    }

    /// <summary>
    /// Opens the sample company for exploration.
    /// </summary>
    [RelayCommand]
    private void OpenSampleCompany()
    {
        OpenSampleCompanyRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes a company from recent list.
    /// </summary>
    [RelayCommand]
    private void RemoveFromRecent(RecentCompanyItem? company)
    {
        if (company == null) return;
        RecentCompanies.Remove(company);
        HasRecentCompanies = RecentCompanies.Count > 0;
        RemoveFromRecentRequested?.Invoke(this, company);
    }

    /// <summary>
    /// Clears all recent companies.
    /// </summary>
    [RelayCommand]
    private void ClearRecent()
    {
        RecentCompanies.Clear();
        HasRecentCompanies = false;
        ClearRecentRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens help documentation.
    /// </summary>
    [RelayCommand]
    private void OpenHelp()
    {
        UrlHelper.SafeOpenUrl($"{ApiConfig.BaseUrl}/contact-us/");
        OpenHelpRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens what's new / release notes.
    /// </summary>
    [RelayCommand]
    private void OpenWhatsNew()
    {
        UrlHelper.SafeOpenUrl($"{ApiConfig.BaseUrl}/whats-new/");
        OpenWhatsNewRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Events

    public event EventHandler? CreateNewCompanyRequested;
    public event EventHandler? OpenCompanyRequested;
    public event EventHandler<RecentCompanyItem>? OpenRecentCompanyRequested;
    public event EventHandler<RecentCompanyItem>? RemoveFromRecentRequested;
    public event EventHandler? ClearRecentRequested;
    public event EventHandler? OpenSampleCompanyRequested;
    public event EventHandler? OpenHelpRequested;
    public event EventHandler? OpenWhatsNewRequested;

    #endregion

    #region Public Methods

    /// <summary>
    /// Re-evaluated each time the welcome screen is shown, so the first-run layout gives way to
    /// the normal one once the user has created or opened a company of their own, and comes back
    /// after Restart Tutorial, which clears both of these.
    /// </summary>
    public void InitializeTutorialMode()
    {
        var tutorialService = TutorialService.Instance;
        IsTutorialMode = !tutorialService.HasCompletedWelcomeTutorial && !tutorialService.IsTutorialInProgressOnCompany;
    }

    /// <summary>
    /// Adds a company to the recent list.
    /// </summary>
    public void AddRecentCompany(string name, string filePath, string? icon = null)
    {
        // Remove if already exists
        var existing = RecentCompanies.FirstOrDefault(c => c.FilePath == filePath);
        if (existing != null)
        {
            RecentCompanies.Remove(existing);
        }

        // Add to top
        RecentCompanies.Insert(0, new RecentCompanyItem
        {
            Name = name,
            FilePath = filePath,
            Icon = icon ?? "Building",
            LastOpened = DateTime.Now
        });

        // Keep max 10 recent
        while (RecentCompanies.Count > 10)
        {
            RecentCompanies.RemoveAt(RecentCompanies.Count - 1);
        }

        HasRecentCompanies = RecentCompanies.Count > 0;
    }

    #endregion
}
