using System.Collections.ObjectModel;
using ArgoBooks.Localization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Stepping, progress dots and the highlight box shared by the app tour and the page tutorials.
/// </summary>
public abstract partial class TutorialStepperViewModelBase : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private int _currentStepIndex;

    [ObservableProperty]
    private string _currentTitle = "";

    [ObservableProperty]
    private string _currentDescription = "";

    [ObservableProperty]
    private int _totalSteps;

    [ObservableProperty]
    private bool _isFirstStep = true;

    [ObservableProperty]
    private bool _isLastStep;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private Thickness _highlightMargin;

    [ObservableProperty]
    private double _highlightWidth = double.NaN;

    [ObservableProperty]
    private double _highlightHeight = double.NaN;

    [ObservableProperty]
    private bool _showHighlight;

    [ObservableProperty]
    private CornerRadius _highlightCornerRadius = new(8);

    /// <summary>Step indicators for the progress dots.</summary>
    public ObservableCollection<StepIndicator> StepIndicators { get; } = [];

    protected abstract int StepCount { get; }

    /// <summary>Shows the content of the step at <paramref name="index"/>.</summary>
    protected abstract void ApplyStep(int index);

    /// <summary>Runs when Next is pressed on the last step.</summary>
    protected abstract void Finish();

    /// <summary>Runs after a step is shown, once the progress state is up to date.</summary>
    protected virtual void OnStepShown()
    {
    }

    protected void InitializeStepIndicators()
    {
        TotalSteps = StepCount;
        StepIndicators.Clear();
        for (int i = 0; i < TotalSteps; i++)
            StepIndicators.Add(new StepIndicator { Index = i, IsActive = false });
    }

    protected void OpenAtFirstStep()
    {
        CurrentStepIndex = 0;
        UpdateCurrentStep();
        IsOpen = true;
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStepIndex < StepCount - 1)
        {
            CurrentStepIndex++;
            UpdateCurrentStep();
        }
        else
        {
            Finish();
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStepIndex > 0)
        {
            CurrentStepIndex--;
            UpdateCurrentStep();
        }
    }

    protected void UpdateCurrentStep()
    {
        if (CurrentStepIndex < 0 || CurrentStepIndex >= StepCount)
            return;

        ApplyStep(CurrentStepIndex);
        IsFirstStep = CurrentStepIndex == 0;
        IsLastStep = CurrentStepIndex == StepCount - 1;
        ProgressText = "{0} of {1}".TranslateFormat(CurrentStepIndex + 1, TotalSteps);

        for (int i = 0; i < StepIndicators.Count; i++)
            StepIndicators[i].IsActive = i == CurrentStepIndex;

        OnStepShown();
    }

    /// <summary>
    /// Updates the highlight bounds. Called from code-behind after measuring elements.
    /// </summary>
    public void SetHighlightBounds(Rect bounds, CornerRadius cornerRadius)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            ShowHighlight = false;
            return;
        }

        HighlightMargin = new Thickness(bounds.Left, bounds.Top, 0, 0);
        HighlightWidth = bounds.Width;
        HighlightHeight = bounds.Height;
        HighlightCornerRadius = cornerRadius;
        ShowHighlight = true;
    }

    public void HideHighlight()
    {
        ShowHighlight = false;
    }
}
