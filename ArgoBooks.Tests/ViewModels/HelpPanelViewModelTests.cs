using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the HelpPanelViewModel.
/// </summary>
public class HelpPanelViewModelTests
{
    private readonly HelpPanelViewModel _viewModel = new();

    #region Default State Tests

    [Fact]
    public void Constructor_Default_AppVersionIsNotNull()
    {
        Assert.NotNull(_viewModel.AppVersion);
    }

    [Fact]
    public void Constructor_Default_AppVersionIsNotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(_viewModel.AppVersion));
    }

    #endregion

    #region Open/Close/Toggle Commands Tests

    [Fact]
    public void OpenCommand_WhenExecuted_SetsIsOpenToTrue()
    {
        _viewModel.OpenCommand.Execute(null);

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void CloseCommand_WhenOpen_SetsIsOpenToFalse()
    {
        _viewModel.IsOpen = true;

        _viewModel.CloseCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void ToggleCommand_WhenClosed_SetsIsOpenToTrue()
    {
        _viewModel.ToggleCommand.Execute(null);

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void ToggleCommand_WhenOpen_SetsIsOpenToFalse()
    {
        _viewModel.IsOpen = true;

        _viewModel.ToggleCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void ToggleCommand_DoubleTap_RestoresOriginalState()
    {
        _viewModel.ToggleCommand.Execute(null);
        _viewModel.ToggleCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    #endregion

    #region Event Raising Tests

    [Fact]
    public void CheckForUpdatesCommand_RaisesCheckForUpdatesRequestedEvent()
    {
        var eventRaised = false;
        _viewModel.CheckForUpdatesRequested += (_, _) => eventRaised = true;

        _viewModel.CheckForUpdatesCommand.Execute(null);

        Assert.True(eventRaised);
    }

    #endregion

    #region IsOpen Property Change Tests

    [Fact]
    public void IsOpen_WhenSetToTrue_RaisesPropertyChanged()
    {
        var propertyChanged = false;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HelpPanelViewModel.IsOpen))
                propertyChanged = true;
        };

        _viewModel.IsOpen = true;

        Assert.True(propertyChanged);
    }

    [Fact]
    public void IsOpen_WhenSetDirectly_UpdatesValue()
    {
        _viewModel.IsOpen = true;
        Assert.True(_viewModel.IsOpen);

        _viewModel.IsOpen = false;
        Assert.False(_viewModel.IsOpen);
    }

    #endregion
}
