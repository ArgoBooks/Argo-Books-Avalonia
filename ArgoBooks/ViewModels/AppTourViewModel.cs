using System.Runtime.InteropServices;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Represents a single step in the app tour.
/// </summary>
public class TourStep
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string TargetArea { get; init; }
    public string? Icon { get; init; }
}

/// <summary>
/// Represents a step indicator dot for the tour progress.
/// </summary>
public partial class StepIndicator : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public int Index { get; init; }
}

/// <summary>
/// ViewModel for the interactive app tour overlay.
/// </summary>
public partial class AppTourViewModel : TutorialStepperViewModelBase
{
    private static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private static string QuickActionsShortcut => IsMacOS ? "Cmd+K" : "Ctrl+K";

    private static List<TourStep> GetTourSteps() =>
    [
        new TourStep
        {
            Title = "Navigation Sidebar".Translate(),
            Description = "Use the sidebar to navigate between different sections of the app. You can collapse it by clicking the menu icon at the top.".Translate(),
            TargetArea = "sidebar",
            Icon = Icons.Menu
        },
        new TourStep
        {
            Title = "Dashboard Overview".Translate(),
            Description = "The Dashboard shows your key business metrics at a glance. Revenue, expenses, and recent transactions are all here.".Translate(),
            TargetArea = "content",
            Icon = Icons.Dashboard
        },
        new TourStep
        {
            Title = "Quick Actions".Translate(),
            Description = "Press {0} anytime to open Quick Actions. It's the fastest way to create expenses, revenue, and more.".TranslateFormat(QuickActionsShortcut),
            TargetArea = "searchbar",
            Icon = Icons.Lightning
        },
        new TourStep
        {
            Title = "One last step".Translate(),
            Description = "Head back to the Dashboard and follow the Setup Checklist to finish getting your books ready. You can restart this tour anytime from the Help menu.".Translate(),
            TargetArea = "center",
            Icon = Icons.Check
        }
    ];

    private List<TourStep> _tourSteps;

    [ObservableProperty]
    private string _currentTargetArea = "center";

    [ObservableProperty]
    private string? _currentIcon;

    /// <summary>
    /// Event raised when the target area changes and bounds need to be recalculated.
    /// </summary>
    public event EventHandler? TargetAreaChanged;

    /// <summary>
    /// Event raised when the tour is completed.
    /// </summary>
    public event EventHandler? TourCompleted;

    /// <summary>
    /// Event raised when the tour is skipped/exited early.
    /// </summary>
    public event EventHandler? TourSkipped;

    public AppTourViewModel()
    {
        _tourSteps = GetTourSteps();
        InitializeStepIndicators();

        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
    }

    protected override int StepCount => _tourSteps.Count;

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        _tourSteps = GetTourSteps();
        UpdateCurrentStep();
    }

    /// <summary>
    /// Starts the app tour from the beginning.
    /// </summary>
    public void StartTour() => OpenAtFirstStep();

    /// <summary>
    /// Shows the tour if the user hasn't completed it yet
    /// and we're on the company where the tutorial was started.
    /// </summary>
    public void ShowIfNeeded()
    {
        TutorialService.Instance.SetCurrentCompanyPath(App.CompanyManager?.CurrentFilePath);

        if (!TutorialService.Instance.HasCompletedAppTour &&
            TutorialService.Instance.ShouldShowTutorialOnCurrentCompany())
        {
            StartTour();
        }
    }

    [RelayCommand]
    private void SkipTour()
    {
        IsOpen = false;
        TutorialService.Instance.CompleteAppTour();
        TourSkipped?.Invoke(this, EventArgs.Empty);
    }

    protected override void Finish()
    {
        IsOpen = false;
        TutorialService.Instance.CompleteAppTour();
        TourCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void ApplyStep(int index)
    {
        var step = _tourSteps[index];
        CurrentTitle = step.Title;
        CurrentDescription = step.Description;
        CurrentTargetArea = step.TargetArea;
        CurrentIcon = step.Icon;
    }

    protected override void OnStepShown() => TargetAreaChanged?.Invoke(this, EventArgs.Empty);
}
