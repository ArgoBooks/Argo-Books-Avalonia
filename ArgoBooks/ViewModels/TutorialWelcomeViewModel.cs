using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the tutorial welcome overlay shown to first-time users.
/// </summary>
public partial class TutorialWelcomeViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>
    /// Event raised when the user chooses to start the app tour.
    /// </summary>
    public event EventHandler? StartTourRequested;

    /// <summary>
    /// Event raised when the user skips the tutorial.
    /// </summary>
    public event EventHandler? TutorialSkipped;

    /// <summary>
    /// Shows the welcome overlay if the user hasn't completed the welcome tutorial
    /// and we're on the company where the tutorial was started.
    /// </summary>
    public void ShowIfNeeded()
    {
        TutorialService.Instance.SetCurrentCompanyPath(App.CompanyManager?.CurrentFilePath);

        if (!TutorialService.Instance.HasCompletedWelcomeTutorial &&
            TutorialService.Instance.ShouldShowTutorialOnCurrentCompany())
        {
            TutorialService.Instance.InitializeForNewUser();
            IsOpen = true;
        }
    }

    /// <summary>
    /// Forces the welcome overlay to show (for restart functionality).
    /// </summary>
    public void Show()
    {
        IsOpen = true;
    }

    [RelayCommand]
    private void StartTour()
    {
        IsOpen = false;
        TutorialService.Instance.CompleteWelcomeTutorial();
        StartTourRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SkipTutorial()
    {
        IsOpen = false;
        TutorialService.Instance.SkipTour();
        TutorialSkipped?.Invoke(this, EventArgs.Empty);
    }
}
