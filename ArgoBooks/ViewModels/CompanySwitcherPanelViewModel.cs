using System.Collections.ObjectModel;
using ArgoBooks.Localization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the company switcher panel.
/// </summary>
public partial class CompanySwitcherPanelViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    #region Current Company

    [ObservableProperty]
    private string _currentCompanyName = "Argo Books";

    [ObservableProperty]
    private string _currentCompanyInitial = "AB";

    [ObservableProperty]
    private Bitmap? _currentCompanyLogo;

    [ObservableProperty]
    private bool _hasCurrentCompanyLogo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentCompanyPathDisplay))]
    private string? _currentCompanyPath;

    /// <summary>The open company's file, shortened to the folder and file name.</summary>
    public string CurrentCompanyPathDisplay => CompanyItem.ShortenPath(CurrentCompanyPath);

    #endregion

    /// <summary>
    /// Recent companies for quick switching (includes all).
    /// </summary>
    public ObservableCollection<CompanyItem> RecentCompanies { get; } = [];

    /// <summary>
    /// Recent companies excluding the currently open company.
    /// </summary>
    public IEnumerable<CompanyItem> FilteredRecentCompanies =>
        RecentCompanies.Where(c => c.FilePath != CurrentCompanyPath);

    /// <summary>
    /// Whether there are any filtered recent companies to display.
    /// </summary>
    public bool HasFilteredRecentCompanies =>
        FilteredRecentCompanies.Any();

    /// <summary>
    /// Default constructor for design-time.
    /// </summary>
    public CompanySwitcherPanelViewModel()
    {
        // Design-time defaults
        RecentCompanies.Add(new CompanyItem
        {
            Name = "My Company Inc.",
            Initial = "MC",
            FilePath = "/path/to/my-company.argo"
        });
        RecentCompanies.Add(new CompanyItem
        {
            Name = "Side Business LLC",
            Initial = "SB",
            FilePath = "/path/to/side-business.argo"
        });
        RecentCompanies.Add(new CompanyItem
        {
            Name = "Consulting Services",
            Initial = "CO",
            FilePath = "/path/to/consulting.argo"
        });
    }

    #region Commands

    /// <summary>
    /// Opens the company switcher panel.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        IsOpen = true;
    }

    /// <summary>
    /// Closes the company switcher panel.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Toggles the company switcher panel.
    /// </summary>
    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;
    }

    /// <summary>
    /// Switches to a different company.
    /// </summary>
    [RelayCommand]
    private void SwitchCompany(CompanyItem? company)
    {
        if (company == null) return;
        Close();
        SwitchCompanyRequested?.Invoke(this, company);
    }

    /// <summary>
    /// Opens an existing company file.
    /// </summary>
    [RelayCommand]
    private void OpenCompany()
    {
        Close();
        OpenCompanyRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the edit company modal.
    /// </summary>
    [RelayCommand]
    private void EditCompany()
    {
        Close();
        EditCompanyRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Events

    public event EventHandler<CompanyItem>? SwitchCompanyRequested;
    public event EventHandler? OpenCompanyRequested;
    public event EventHandler? EditCompanyRequested;

    #endregion

    /// <summary>
    /// Sets the current company information.
    /// </summary>
    public void SetCurrentCompany(string name, string? path = null, Bitmap? logo = null)
    {
        CurrentCompanyName = name;
        CurrentCompanyInitial = Helpers.InitialsHelper.From(name);
        CurrentCompanyPath = path;
        CurrentCompanyLogo = logo;
        HasCurrentCompanyLogo = logo != null;
    }

    /// <summary>
    /// Adds a recent company to the list.
    /// </summary>
    public void AddRecentCompany(string name, string filePath, Bitmap? logo = null)
    {
        // Don't add duplicates
        var existing = RecentCompanies.FirstOrDefault(c => c.FilePath == filePath);
        if (existing != null)
        {
            RecentCompanies.Remove(existing);
        }

        // Add to the beginning
        RecentCompanies.Insert(0, new CompanyItem
        {
            Name = name,
            Initial = Helpers.InitialsHelper.From(name),
            FilePath = filePath,
            Logo = logo
        });

        // Keep only the last 5 recent companies
        while (RecentCompanies.Count > 5)
        {
            RecentCompanies.RemoveAt(RecentCompanies.Count - 1);
        }

        RefreshFilteredRecent();
    }

    /// <summary>
    /// Clears the recent companies list.
    /// </summary>
    [RelayCommand]
    private void ClearRecent()
    {
        RecentCompanies.Clear();
        RefreshFilteredRecent();
    }

    /// <summary>
    /// Updates filtered list when current company path changes.
    /// </summary>
    partial void OnCurrentCompanyPathChanged(string? value)
    {
        OnPropertyChanged(nameof(FilteredRecentCompanies));
        OnPropertyChanged(nameof(HasFilteredRecentCompanies));
    }

    /// <summary>
    /// Refreshes the filtered recent companies properties.
    /// </summary>
    public void RefreshFilteredRecent()
    {
        OnPropertyChanged(nameof(FilteredRecentCompanies));
        OnPropertyChanged(nameof(HasFilteredRecentCompanies));
    }
}

/// <summary>
/// Represents a company item in the switcher.
/// </summary>
public class CompanyItem
{
    public string Name { get; set; } = "";
    public string Initial { get; set; } = "?";
    public string? FilePath { get; set; }
    public Bitmap? Logo { get; set; }
    public bool HasLogo => Logo != null;
    public DateTime LastOpened { get; set; } = DateTime.Now;

    /// <summary>When it was last opened, in words, so a row says why it is in the list.</summary>
    public string LastOpenedDisplay
    {
        get
        {
            var days = (int)(DateTime.Now.Date - LastOpened.Date).TotalDays;

            return days switch
            {
                <= 0 => "Opened today".Translate(),
                1 => "Opened yesterday".Translate(),
                < 7 => "Opened {0} days ago".TranslateFormat(days),
                < 14 => "Opened last week".Translate(),
                < 60 => "Opened {0} weeks ago".TranslateFormat(days / 7),
                _ => "Opened {0}".TranslateFormat(LastOpened.ToString("d MMM yyyy"))
            };
        }
    }

    /// <summary>A path short enough for a panel: the folder it sits in, then the file.</summary>
    public static string ShortenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var file = System.IO.Path.GetFileName(path);
        var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? string.Empty);

        return string.IsNullOrEmpty(folder) ? file : folder + System.IO.Path.DirectorySeparatorChar + file;
    }
}
