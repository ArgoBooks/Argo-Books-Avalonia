using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the UnsavedChangesDialogViewModel. Which button was pressed decides whether the
/// user's work is saved or thrown away, so each one has to report the right result.
/// </summary>
public class UnsavedChangesDialogViewModelTests
{
    private readonly UnsavedChangesDialogViewModel _viewModel = new();

    [Fact]
    public void ShowAsync_WithCustomText_SetsTitleAndMessage()
    {
        _viewModel.ShowAsync("Custom Title", "Custom message text");

        Assert.True(_viewModel.IsOpen);
        Assert.Equal("Custom Title", _viewModel.Title);
        Assert.Equal("Custom message text", _viewModel.Message);
    }

    [Fact]
    public async Task Close_WhenCalled_ReturnsNone()
    {
        var task = _viewModel.ShowAsync();

        _viewModel.Close();

        Assert.False(_viewModel.IsOpen);
        Assert.Equal(UnsavedChangesResult.None, await task);
    }

    [Fact]
    public async Task SaveCommand_WhenExecuted_ReturnsSave()
    {
        var task = _viewModel.ShowAsync();

        _viewModel.SaveCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
        Assert.Equal(UnsavedChangesResult.Save, await task);
    }

    [Fact]
    public async Task DontSaveCommand_WhenExecuted_ReturnsDontSave()
    {
        var task = _viewModel.ShowAsync();

        _viewModel.DontSaveCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
        Assert.Equal(UnsavedChangesResult.DontSave, await task);
    }

    [Fact]
    public async Task CancelCommand_WhenExecuted_ReturnsCancel()
    {
        var task = _viewModel.ShowAsync();

        _viewModel.CancelCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
        Assert.Equal(UnsavedChangesResult.Cancel, await task);
    }
}
