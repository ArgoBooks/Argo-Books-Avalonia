using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// The ways a new user can add their first transactions from the welcome overlay.
/// </summary>
public enum FirstStep
{
    ScanReceipt,
    BankStatement,
    Import,
    Manual
}

/// <summary>
/// ViewModel for the welcome overlay on a new user's first company, which asks how they want to add their first transactions.
/// </summary>
// People who keep using Argo Books mostly start by scanning a receipt or importing a statement, and a tour of the menus rarely got anyone there.
public partial class TutorialWelcomeViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>
    /// Event raised when the user chooses to start the app tour.
    /// </summary>
    public event EventHandler? StartTourRequested;

    /// <summary>
    /// Event raised when the user picks a way to add their first transactions.
    /// </summary>
    public event EventHandler<FirstStep>? FirstStepRequested;

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
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.FirstStepChosen, "tour");
        StartTourRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SkipTutorial()
    {
        IsOpen = false;
        TutorialService.Instance.SkipTour();
    }

    [RelayCommand]
    private void ChooseScanReceipt() => Choose(FirstStep.ScanReceipt, "scan");

    [RelayCommand]
    private void ChooseBankStatement() => Choose(FirstStep.BankStatement, "bank");

    [RelayCommand]
    private void ChooseImport() => Choose(FirstStep.Import, "import");

    [RelayCommand]
    private void ChooseManual() => Choose(FirstStep.Manual, "manual");

    private void Choose(FirstStep step, string telemetryContext)
    {
        IsOpen = false;
        TutorialService.Instance.FinishWelcome();
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.FirstStepChosen, telemetryContext);
        FirstStepRequested?.Invoke(this, step);
    }
}
