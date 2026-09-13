using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the ImportModalViewModel.
/// </summary>
public class ImportModalViewModelTests
{
    private readonly ImportModalViewModel _viewModel = new();

    #region Open/Close Tests

    [Fact]
    public void OpenCommand_WhenExecuted_SetsIsOpenToTrue()
    {
        _viewModel.OpenCommand.Execute(null);

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void OpenCommand_WhenExecuted_ResetsSelectedFormatToNull()
    {
        _viewModel.SelectedFormat = "CSV";

        _viewModel.OpenCommand.Execute(null);

        Assert.Null(_viewModel.SelectedFormat);
    }

    [Fact]
    public void CloseCommand_WhenExecuted_SetsIsOpenToFalse()
    {
        _viewModel.OpenCommand.Execute(null);
        Assert.True(_viewModel.IsOpen);

        _viewModel.CloseCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    #endregion

    #region SelectFormat Tests

    [Fact]
    public void SelectFormatCommand_WithValidFormat_SetsSelectedFormat()
    {
        _viewModel.OpenCommand.Execute(null);

        _viewModel.SelectFormatCommand.Execute("CSV");

        Assert.Equal("CSV", _viewModel.SelectedFormat);
    }

    [Fact]
    public void SelectFormatCommand_WithValidFormat_ClosesModal()
    {
        _viewModel.OpenCommand.Execute(null);

        _viewModel.SelectFormatCommand.Execute("CSV");

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void SelectFormatCommand_WithNullFormat_DoesNotClose()
    {
        _viewModel.OpenCommand.Execute(null);

        _viewModel.SelectFormatCommand.Execute(null);

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void SelectFormatCommand_WithEmptyFormat_DoesNotClose()
    {
        _viewModel.OpenCommand.Execute(null);

        _viewModel.SelectFormatCommand.Execute("");

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void SelectFormatCommand_WithValidFormat_RaisesFormatSelectedEvent()
    {
        string? selectedFormat = null;
        _viewModel.FormatSelected += (_, format) => selectedFormat = format;
        _viewModel.OpenCommand.Execute(null);

        _viewModel.SelectFormatCommand.Execute("Excel");

        Assert.Equal("Excel", selectedFormat);
    }

    #endregion
}
