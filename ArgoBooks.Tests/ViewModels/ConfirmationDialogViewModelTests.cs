using ArgoBooks.Core.Enums;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the ConfirmationDialogViewModel.
/// </summary>
public class ConfirmationDialogViewModelTests
{
    private readonly ConfirmationDialogViewModel _viewModel = new();

    #region ShowAsync with Options Tests

    [Fact]
    public void ShowAsync_WithOptions_SetsTitleAndMessage()
    {
        var options = new ConfirmationDialogOptions
        {
            Title = "Delete Item",
            Message = "Are you sure you want to delete this item?"
        };

        _viewModel.ShowAsync(options);

        Assert.Equal("Delete Item", _viewModel.Title);
        Assert.Equal("Are you sure you want to delete this item?", _viewModel.Message);
    }

    [Fact]
    public void ShowAsync_WithOptions_SetsButtonTexts()
    {
        var options = new ConfirmationDialogOptions
        {
            PrimaryButtonText = "Delete",
            SecondaryButtonText = "Archive",
            CancelButtonText = "Go Back"
        };

        _viewModel.ShowAsync(options);

        Assert.Equal("Delete", _viewModel.PrimaryButtonText);
        Assert.Equal("Archive", _viewModel.SecondaryButtonText);
        Assert.Equal("Go Back", _viewModel.CancelButtonText);
    }

    [Fact]
    public void ShowAsync_WithOptions_SetsIsOpenToTrue()
    {
        var options = new ConfirmationDialogOptions
        {
            Title = "Test",
            Message = "Test message"
        };

        _viewModel.ShowAsync(options);

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void ShowAsync_WithNullPrimaryButton_ShowPrimaryButtonIsFalse()
    {
        var options = new ConfirmationDialogOptions
        {
            PrimaryButtonText = null
        };

        _viewModel.ShowAsync(options);

        Assert.False(_viewModel.ShowPrimaryButton);
    }

    [Fact]
    public void ShowAsync_WithSecondaryButton_ShowSecondaryButtonIsTrue()
    {
        var options = new ConfirmationDialogOptions
        {
            SecondaryButtonText = "Don't Save"
        };

        _viewModel.ShowAsync(options);

        Assert.True(_viewModel.ShowSecondaryButton);
    }

    [Fact]
    public void ShowAsync_WithNullSecondaryButton_ShowSecondaryButtonIsFalse()
    {
        var options = new ConfirmationDialogOptions
        {
            SecondaryButtonText = null
        };

        _viewModel.ShowAsync(options);

        Assert.False(_viewModel.ShowSecondaryButton);
    }

    [Fact]
    public void ShowAsync_WithNullCancelButton_ShowCancelButtonIsFalse()
    {
        var options = new ConfirmationDialogOptions
        {
            CancelButtonText = null
        };

        _viewModel.ShowAsync(options);

        Assert.False(_viewModel.ShowCancelButton);
    }

    [Fact]
    public void ShowAsync_WithDestructiveFlags_SetsDestructiveProperties()
    {
        var options = new ConfirmationDialogOptions
        {
            IsPrimaryDestructive = true,
            IsSecondaryDestructive = true
        };

        _viewModel.ShowAsync(options);

        Assert.True(_viewModel.IsPrimaryDestructive);
        Assert.True(_viewModel.IsSecondaryDestructive);
    }

    #endregion

    #region ShowAsync Simple Overload Tests

    [Fact]
    public void ShowAsync_SimpleOverload_SetsTitleAndMessage()
    {
        _viewModel.ShowAsync("Confirm Delete", "Are you sure?");

        Assert.Equal("Confirm Delete", _viewModel.Title);
        Assert.Equal("Are you sure?", _viewModel.Message);
    }

    [Fact]
    public void ShowAsync_SimpleOverload_SetsIsOpenToTrue()
    {
        _viewModel.ShowAsync("Title", "Message");

        Assert.True(_viewModel.IsOpen);
    }

    #endregion

    #region Close Tests

    [Fact]
    public void Close_WhenOpen_SetsIsOpenToFalse()
    {
        _viewModel.ShowAsync("Title", "Message");
        Assert.True(_viewModel.IsOpen);

        _viewModel.Close();

        Assert.False(_viewModel.IsOpen);
    }

    #endregion

    #region Command Action Tests

    [Fact]
    public async Task PrimaryActionCommand_WhenExecuted_SetsIsOpenToFalseAndReturnsPrimary()
    {
        var task = _viewModel.ShowAsync("Title", "Message");

        _viewModel.PrimaryActionCommand.Execute(null);

        var result = await task;
        Assert.False(_viewModel.IsOpen);
        Assert.Equal(ConfirmationResult.Primary, result);
    }

    [Fact]
    public async Task SecondaryActionCommand_WhenExecuted_SetsIsOpenToFalseAndReturnsSecondary()
    {
        var options = new ConfirmationDialogOptions
        {
            SecondaryButtonText = "Don't Save"
        };
        var task = _viewModel.ShowAsync(options);

        _viewModel.SecondaryActionCommand.Execute(null);

        var result = await task;
        Assert.False(_viewModel.IsOpen);
        Assert.Equal(ConfirmationResult.Secondary, result);
    }

    [Fact]
    public async Task CancelActionCommand_WhenExecuted_SetsIsOpenToFalseAndReturnsCancel()
    {
        var task = _viewModel.ShowAsync("Title", "Message");

        _viewModel.CancelActionCommand.Execute(null);

        var result = await task;
        Assert.False(_viewModel.IsOpen);
        Assert.Equal(ConfirmationResult.Cancel, result);
    }

    [Fact]
    public async Task Close_WhenCalled_ReturnsNone()
    {
        var task = _viewModel.ShowAsync("Title", "Message");

        _viewModel.Close();

        var result = await task;
        Assert.Equal(ConfirmationResult.None, result);
    }

    #endregion
}
