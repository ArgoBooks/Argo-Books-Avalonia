using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the AppTourViewModel.
/// </summary>
public class AppTourViewModelTests
{
    private readonly AppTourViewModel _viewModel = new();

    #region Default State Tests

    [Fact]
    public void Constructor_DefaultState_TotalStepsIsFour()
    {
        Assert.Equal(4, _viewModel.TotalSteps);
    }

    [Fact]
    public void Constructor_DefaultState_StepIndicatorsMatchTotalSteps()
    {
        Assert.Equal(_viewModel.TotalSteps, _viewModel.StepIndicators.Count);
    }

    #endregion

    #region StartTour Tests

    [Fact]
    public void StartTour_WhenCalled_SetsIsOpenToTrue()
    {
        _viewModel.StartTour();

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void StartTour_WhenCalled_SetsCurrentStepIndexToZero()
    {
        _viewModel.StartTour();

        Assert.Equal(0, _viewModel.CurrentStepIndex);
    }

    [Fact]
    public void StartTour_WhenCalled_SetsCurrentTitleToNonEmpty()
    {
        _viewModel.StartTour();

        Assert.False(string.IsNullOrEmpty(_viewModel.CurrentTitle));
    }

    [Fact]
    public void StartTour_WhenCalled_SetsCurrentDescriptionToNonEmpty()
    {
        _viewModel.StartTour();

        Assert.False(string.IsNullOrEmpty(_viewModel.CurrentDescription));
    }

    #endregion

    #region IsFirstStep Tests

    [Fact]
    public void IsFirstStep_AtStepZero_ReturnsTrue()
    {
        _viewModel.StartTour();

        Assert.True(_viewModel.IsFirstStep);
    }

    [Fact]
    public void IsFirstStep_AtStepOne_ReturnsFalse()
    {
        _viewModel.StartTour();

        _viewModel.NextStepCommand.Execute(null);

        Assert.False(_viewModel.IsFirstStep);
    }

    #endregion

    #region IsLastStep Tests

    [Fact]
    public void IsLastStep_AtFirstStep_ReturnsFalse()
    {
        _viewModel.StartTour();

        Assert.False(_viewModel.IsLastStep);
    }

    [Fact]
    public void IsLastStep_AtLastStep_ReturnsTrue()
    {
        _viewModel.StartTour();

        // Navigate to the last step (TotalSteps - 1 times)
        for (int i = 0; i < _viewModel.TotalSteps - 1; i++)
        {
            _viewModel.NextStepCommand.Execute(null);
        }

        Assert.True(_viewModel.IsLastStep);
    }

    #endregion

    #region NextStep Tests

    [Fact]
    public void NextStepCommand_WhenNotAtEnd_AdvancesIndex()
    {
        _viewModel.StartTour();
        Assert.Equal(0, _viewModel.CurrentStepIndex);

        _viewModel.NextStepCommand.Execute(null);

        Assert.Equal(1, _viewModel.CurrentStepIndex);
    }

    [Fact]
    public void NextStepCommand_WhenAdvancing_UpdatesTitle()
    {
        _viewModel.StartTour();
        var firstTitle = _viewModel.CurrentTitle;

        _viewModel.NextStepCommand.Execute(null);

        Assert.NotEqual(firstTitle, _viewModel.CurrentTitle);
    }

    [Fact]
    public void NextStepCommand_WhenAdvancing_UpdatesProgressText()
    {
        _viewModel.StartTour();

        _viewModel.NextStepCommand.Execute(null);

        Assert.Equal($"2 of {_viewModel.TotalSteps}", _viewModel.ProgressText);
    }

    #endregion

    #region PreviousStep Tests

    [Fact]
    public void PreviousStepCommand_WhenNotAtStart_GoesBack()
    {
        _viewModel.StartTour();
        _viewModel.NextStepCommand.Execute(null);
        Assert.Equal(1, _viewModel.CurrentStepIndex);

        _viewModel.PreviousStepCommand.Execute(null);

        Assert.Equal(0, _viewModel.CurrentStepIndex);
    }

    [Fact]
    public void PreviousStepCommand_WhenAtStart_StaysAtZero()
    {
        _viewModel.StartTour();
        Assert.Equal(0, _viewModel.CurrentStepIndex);

        _viewModel.PreviousStepCommand.Execute(null);

        Assert.Equal(0, _viewModel.CurrentStepIndex);
    }

    #endregion

    #region SkipTour Tests

    [Fact]
    public void SkipTourCommand_WhenExecuted_ClosesTour()
    {
        _viewModel.StartTour();
        Assert.True(_viewModel.IsOpen);

        _viewModel.SkipTourCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void SkipTourCommand_WhenExecuted_RaisesTourSkippedEvent()
    {
        var eventRaised = false;
        _viewModel.TourSkipped += (_, _) => eventRaised = true;
        _viewModel.StartTour();

        _viewModel.SkipTourCommand.Execute(null);

        Assert.True(eventRaised);
    }

    #endregion

    #region Step Titles Tests

    [Fact]
    public void StepTitles_AllStepsHaveNonEmptyTitles()
    {
        _viewModel.StartTour();

        for (int i = 0; i < _viewModel.TotalSteps; i++)
        {
            Assert.False(string.IsNullOrEmpty(_viewModel.CurrentTitle),
                $"Step {i} should have a non-empty title");
            Assert.False(string.IsNullOrEmpty(_viewModel.CurrentDescription),
                $"Step {i} should have a non-empty description");

            if (i < _viewModel.TotalSteps - 1)
            {
                _viewModel.NextStepCommand.Execute(null);
            }
        }
    }

    [Fact]
    public void StartTour_FirstStepTitle_IsNavigationSidebar()
    {
        _viewModel.StartTour();

        Assert.Equal("Navigation Sidebar", _viewModel.CurrentTitle);
    }

    #endregion

    #region StepIndicators Tests

    [Fact]
    public void StepIndicators_AfterStartTour_FirstIsActive()
    {
        _viewModel.StartTour();

        Assert.True(_viewModel.StepIndicators[0].IsActive);
        for (int i = 1; i < _viewModel.StepIndicators.Count; i++)
        {
            Assert.False(_viewModel.StepIndicators[i].IsActive);
        }
    }

    [Fact]
    public void StepIndicators_AfterNextStep_SecondIsActive()
    {
        _viewModel.StartTour();

        _viewModel.NextStepCommand.Execute(null);

        Assert.False(_viewModel.StepIndicators[0].IsActive);
        Assert.True(_viewModel.StepIndicators[1].IsActive);
    }

    #endregion
}
