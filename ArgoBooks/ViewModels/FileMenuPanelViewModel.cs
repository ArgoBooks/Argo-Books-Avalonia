using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the file menu panel.
/// </summary>
public partial class FileMenuPanelViewModel : ViewModelBase
{
    private SidebarViewModel? _sidebarViewModel;

    // Header button offset from sidebar edge (matches header's left padding)
    private const double HeaderButtonOffset = 16;
    // Width of the main file menu panel
    private const double PanelWidth = 240;
    // Vertical offset of submenu from main panel top
    private const double SubmenuVerticalOffset = 69;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isRecentSubmenuOpen;

    [ObservableProperty]
    private string? _currentCompanyPath;

    #region Dynamic Positioning

    /// <summary>
    /// Gets the left offset for the main file menu panel based on sidebar width.
    /// </summary>
    public double PanelLeftOffset => (_sidebarViewModel?.Width ?? 250) + HeaderButtonOffset;

    /// <summary>
    /// Gets the left offset for the submenu (main panel left + panel width).
    /// </summary>
    public double SubmenuLeftOffset => PanelLeftOffset + PanelWidth;

    /// <summary>
    /// Gets the margin for the main file menu panel.
    /// </summary>
    public Thickness PanelMargin => new(PanelLeftOffset, 60, 0, 0);

    public Thickness SubmenuMargin => new(SubmenuLeftOffset, 60 + SubmenuVerticalOffset, 0, 0);

    #endregion

    /// <summary>
    /// Recent companies for quick access.
    /// </summary>
    public ObservableCollection<RecentCompanyItem> RecentCompanies { get; } = [];

    /// <summary>
    /// Recent companies excluding the currently open company.
    /// </summary>
    public IEnumerable<RecentCompanyItem> FilteredRecentCompanies =>
        RecentCompanies.Where(c => c.FilePath != CurrentCompanyPath);

    /// <summary>
    /// Whether there are any filtered recent companies to display.
    /// </summary>
    public bool HasFilteredRecentCompanies =>
        FilteredRecentCompanies.Any();

    /// <summary>
    /// Default constructor for design-time.
    /// </summary>
    public FileMenuPanelViewModel()
    {
        // Refresh filtered properties whenever the collection changes
        RecentCompanies.CollectionChanged += (_, _) => RefreshFilteredRecent();

        // Design-time defaults
        RecentCompanies.Add(new RecentCompanyItem { Name = "My Company Inc.", Icon = "Building" });
        RecentCompanies.Add(new RecentCompanyItem { Name = "Side Business LLC", Icon = "Store" });
        RecentCompanies.Add(new RecentCompanyItem { Name = "Consulting Services", Icon = "Briefcase" });
    }

    /// <summary>
    /// Sets the sidebar view model to track sidebar width for dynamic positioning.
    /// </summary>
    public void SetSidebarViewModel(SidebarViewModel sidebarViewModel)
    {
        _sidebarViewModel = sidebarViewModel;
        _sidebarViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidebarViewModel.Width))
            {
                OnPropertyChanged(nameof(PanelLeftOffset));
                OnPropertyChanged(nameof(SubmenuLeftOffset));
                OnPropertyChanged(nameof(PanelMargin));
                OnPropertyChanged(nameof(SubmenuMargin));
            }
        };
    }

    #region Commands

    /// <summary>
    /// Opens the file menu panel.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        IsOpen = true;
    }

    /// <summary>
    /// Closes the file menu panel.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Toggles the file menu panel.
    /// </summary>
    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;
    }

    /// <summary>
    /// Creates a new company.
    /// </summary>
    [RelayCommand]
    private void NewCompany()
    {
        Close();
        CreateNewCompanyRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens a company file.
    /// </summary>
    [RelayCommand]
    private void OpenCompany()
    {
        Close();
        OpenCompanyRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens a recent company.
    /// </summary>
    [RelayCommand]
    private void OpenRecentCompany(RecentCompanyItem? company)
    {
        if (company == null) return;
        Close();
        OpenRecentCompanyRequested?.Invoke(this, company);
    }

    /// <summary>
    /// Clears the recent companies list.
    /// </summary>
    [RelayCommand]
    private void ClearRecent()
    {
        RecentCompanies.Clear();
        RefreshFilteredRecent();
        // Don't close the panel - let user see that the list is empty
    }

    /// <summary>
    /// Saves the current company.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        Close();
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saves the current company to a new file.
    /// </summary>
    [RelayCommand]
    private void SaveAs()
    {
        Close();
        SaveAsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Closes the current company.
    /// </summary>
    [RelayCommand]
    private void CloseCompany()
    {
        Close();
        CloseCompanyRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the import dialog.
    /// </summary>
    [RelayCommand]
    private void Import()
    {
        Close();
        ImportRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the export dialog.
    /// </summary>
    [RelayCommand]
    private void ExportAs()
    {
        Close();
        ExportAsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens Send to Accountant.
    /// </summary>
    [RelayCommand]
    private void SendToAccountant()
    {
        Close();
        SendToAccountantRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows the company file in the file explorer.
    /// </summary>
    [RelayCommand]
    private void ShowInFolder()
    {
        Close();
        ShowInFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Events

    public event EventHandler? CreateNewCompanyRequested;
    public event EventHandler? OpenCompanyRequested;
    public event EventHandler<RecentCompanyItem>? OpenRecentCompanyRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? SaveAsRequested;
    public event EventHandler? CloseCompanyRequested;
    public event EventHandler? ImportRequested;
    public event EventHandler? ExportAsRequested;
    public event EventHandler? SendToAccountantRequested;
    public event EventHandler? ShowInFolderRequested;

    #endregion

    /// <summary>
    /// Sets the current company path for filtering recent companies.
    /// </summary>
    public void SetCurrentCompany(string? filePath)
    {
        CurrentCompanyPath = filePath;
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
/// Represents a recent company item.
/// </summary>
public class RecentCompanyItem
{
    public string? Name { get; set; }
    public string? FilePath { get; set; }
    public string? Icon { get; set; }
    public Bitmap? Logo { get; set; }
    public bool HasLogo => Logo != null;

    /// <summary>Stands in for a missing logo, the same as the company tile inside the app.</summary>
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();
    public DateTime LastOpened { get; set; } = DateTime.Now;
}
