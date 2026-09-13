using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the ExportAsModalViewModel.
/// </summary>
public class ExportAsModalViewModelTests
{
    private readonly ExportAsModalViewModel _viewModel = new();

    #region Tab Selection Tests

    [Fact]
    public void IsBackupSelected_WhenTabIndexIsZero_ReturnsTrue()
    {
        _viewModel.SelectedTabIndex = 0;

        Assert.True(_viewModel.IsBackupSelected);
    }

    [Fact]
    public void IsBackupSelected_WhenTabIndexIsOne_ReturnsFalse()
    {
        _viewModel.SelectedTabIndex = 1;

        Assert.False(_viewModel.IsBackupSelected);
    }

    [Fact]
    public void IsSpreadsheetSelected_WhenTabIndexIsOne_ReturnsTrue()
    {
        _viewModel.SelectedTabIndex = 1;

        Assert.True(_viewModel.IsSpreadsheetSelected);
    }

    [Fact]
    public void IsSpreadsheetSelected_WhenTabIndexIsZero_ReturnsFalse()
    {
        _viewModel.SelectedTabIndex = 0;

        Assert.False(_viewModel.IsSpreadsheetSelected);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, false, true)]
    public void TabSelection_CorrectComputedProperties(int tabIndex, bool expectedBackup, bool expectedSpreadsheet)
    {
        _viewModel.SelectedTabIndex = tabIndex;

        Assert.Equal(expectedBackup, _viewModel.IsBackupSelected);
        Assert.Equal(expectedSpreadsheet, _viewModel.IsSpreadsheetSelected);
    }

    #endregion

    #region FileFormats Tests

    [Fact]
    public void FileFormats_IsPopulated()
    {
        Assert.NotEmpty(_viewModel.FileFormats);
    }

    [Fact]
    public void FileFormats_ContainsXlsx()
    {
        Assert.Contains("xlsx", _viewModel.FileFormats);
    }

    #endregion

    #region DataItems Tests

    [Fact]
    public void DataItems_IsPopulated()
    {
        Assert.NotEmpty(_viewModel.DataItems);
    }

    [Fact]
    public void DataItems_AllInitiallySelected()
    {
        Assert.All(_viewModel.DataItems, item => Assert.True(item.IsSelected));
    }

    [Fact]
    public void DataItems_ContainsExpectedCategories()
    {
        var names = _viewModel.DataItems.Select(x => x.Name).ToList();

        Assert.Contains("Customers", names);
        Assert.Contains("Products", names);
        Assert.Contains("Revenue", names);
        Assert.Contains("Expenses", names);
        Assert.Contains("Invoices", names);
        Assert.Contains("Inventory", names);
    }

    [Fact]
    public void DataItems_LastItemHasIsLastTrue()
    {
        var lastItem = _viewModel.DataItems.Last();

        Assert.True(lastItem.IsLast);
    }

    #endregion

    #region SelectAllData Tests

    [Fact]
    public void SelectAllData_WhenSetToFalse_DeselectsAllDataItems()
    {
        _viewModel.SelectAllData = false;

        Assert.All(_viewModel.DataItems, item => Assert.False(item.IsSelected));
    }

    [Fact]
    public void SelectAllData_WhenSetToTrue_SelectsAllDataItems()
    {
        _viewModel.SelectAllData = false;
        _viewModel.SelectAllData = true;

        Assert.All(_viewModel.DataItems, item => Assert.True(item.IsSelected));
    }

    [Fact]
    public void SelectAllData_WhenOneItemDeselected_BecomesFalse()
    {
        _viewModel.DataItems[0].IsSelected = false;

        Assert.False(_viewModel.SelectAllData);
    }

    #endregion

    #region Open/Close Tests

    [Fact]
    public void OpenCommand_WhenExecuted_SetsIsOpenToTrue()
    {
        _viewModel.OpenCommand.Execute(null);

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void OpenCommand_WhenExecuted_ResetsTabIndexToZero()
    {
        _viewModel.SelectedTabIndex = 1;

        _viewModel.OpenCommand.Execute(null);

        Assert.Equal(0, _viewModel.SelectedTabIndex);
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

    #region Export Event Tests

    [Fact]
    public void ExportCommand_WhenBackupSelected_RaisesExportRequestedWithBackupFormat()
    {
        ExportEventArgs? eventArgs = null;
        _viewModel.ExportRequested += (_, args) => eventArgs = args;
        _viewModel.SelectedTabIndex = 0;

        _viewModel.ExportCommand.Execute(null);

        Assert.NotNull(eventArgs);
        Assert.Equal("backup", eventArgs.Format);
    }

    [Fact]
    public void ExportCommand_WhenSpreadsheetSelected_RaisesExportRequestedWithSelectedFormat()
    {
        ExportEventArgs? eventArgs = null;
        _viewModel.ExportRequested += (_, args) => eventArgs = args;
        _viewModel.SelectedTabIndex = 1;
        _viewModel.SelectedFileFormat = "xlsx";

        _viewModel.ExportCommand.Execute(null);

        Assert.NotNull(eventArgs);
        Assert.Equal("xlsx", eventArgs.Format);
    }

    [Fact]
    public void ExportCommand_WhenExecuted_ClosesModal()
    {
        _viewModel.OpenCommand.Execute(null);

        _viewModel.ExportCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    #endregion
}
