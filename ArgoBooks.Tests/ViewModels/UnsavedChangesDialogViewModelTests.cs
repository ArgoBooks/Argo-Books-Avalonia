using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the UnsavedChangesDialogViewModel.
/// </summary>
public class UnsavedChangesDialogViewModelTests
{
    private readonly UnsavedChangesDialogViewModel _viewModel = new();

    #region Default State Tests

    [Fact]
    public void Constructor_DefaultState_HasChangesIsFalse()
    {
        Assert.False(_viewModel.HasChanges);
    }

    [Fact]
    public void Constructor_DefaultState_TotalChangeCountIsZero()
    {
        Assert.Equal(0, _viewModel.TotalChangeCount);
    }

    #endregion

    #region ShowAsync Tests

    [Fact]
    public void ShowAsync_WithCategories_PopulatesCategoriesCollection()
    {
        var category = new ChangeCategory { Name = "Customers" };
        category.Changes.Add(new ChangeItem { Description = "Added customer A", ChangeType = ChangeType.Added });
        category.Changes.Add(new ChangeItem { Description = "Modified customer B", ChangeType = ChangeType.Modified });

        _viewModel.ShowAsync([category]);

        Assert.Single(_viewModel.Categories);
        Assert.Equal("Customers", _viewModel.Categories[0].Name);
        Assert.Equal(2, _viewModel.Categories[0].Changes.Count);
    }

    [Fact]
    public void ShowAsync_WithCategories_SetsIsOpenToTrue()
    {
        var category = new ChangeCategory { Name = "Products" };
        category.Changes.Add(new ChangeItem { Description = "New product" });

        _viewModel.ShowAsync([category]);

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void ShowAsync_WithEmptyCategory_FiltersOutEmptyCategories()
    {
        var emptyCategory = new ChangeCategory { Name = "Empty" };
        var populatedCategory = new ChangeCategory { Name = "Customers" };
        populatedCategory.Changes.Add(new ChangeItem { Description = "Change 1" });

        _viewModel.ShowAsync([emptyCategory, populatedCategory]);

        Assert.Single(_viewModel.Categories);
        Assert.Equal("Customers", _viewModel.Categories[0].Name);
    }

    [Fact]
    public void ShowAsync_WithCustomTitle_SetsTitle()
    {
        _viewModel.ShowAsync(title: "Custom Title");

        Assert.Equal("Custom Title", _viewModel.Title);
    }

    [Fact]
    public void ShowAsync_WithCustomMessage_SetsMessage()
    {
        _viewModel.ShowAsync(message: "Custom message text");

        Assert.Equal("Custom message text", _viewModel.Message);
    }

    #endregion

    #region TotalChangeCount Tests

    [Fact]
    public void TotalChangeCount_MultipleCategories_SumsAcrossAllCategories()
    {
        var category1 = new ChangeCategory { Name = "Customers" };
        category1.Changes.Add(new ChangeItem { Description = "Change 1" });
        category1.Changes.Add(new ChangeItem { Description = "Change 2" });

        var category2 = new ChangeCategory { Name = "Products" };
        category2.Changes.Add(new ChangeItem { Description = "Change 3" });
        category2.Changes.Add(new ChangeItem { Description = "Change 4" });
        category2.Changes.Add(new ChangeItem { Description = "Change 5" });

        _viewModel.ShowAsync([category1, category2]);

        Assert.Equal(5, _viewModel.TotalChangeCount);
    }

    [Fact]
    public void TotalChangeCount_SingleCategory_ReturnsCorrectCount()
    {
        var category = new ChangeCategory { Name = "Invoices" };
        category.Changes.Add(new ChangeItem { Description = "Invoice 1" });

        _viewModel.ShowAsync([category]);

        Assert.Equal(1, _viewModel.TotalChangeCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void TotalChangeCount_VariousCounts_ReturnsExpectedValue(int changeCount)
    {
        var category = new ChangeCategory { Name = "Items" };
        for (int i = 0; i < changeCount; i++)
        {
            category.Changes.Add(new ChangeItem { Description = $"Change {i}" });
        }

        if (changeCount > 0)
        {
            _viewModel.ShowAsync([category]);
        }
        else
        {
            _viewModel.ShowAsync([]);
        }

        Assert.Equal(changeCount, _viewModel.TotalChangeCount);
    }

    #endregion

    #region HasChanges Tests

    [Fact]
    public void HasChanges_WithPopulatedCategories_ReturnsTrue()
    {
        var category = new ChangeCategory { Name = "Customers" };
        category.Changes.Add(new ChangeItem { Description = "Added customer" });

        _viewModel.ShowAsync([category]);

        Assert.True(_viewModel.HasChanges);
    }

    [Fact]
    public void HasChanges_WithNoCategories_ReturnsFalse()
    {
        _viewModel.ShowAsync([]);

        Assert.False(_viewModel.HasChanges);
    }

    [Fact]
    public void HasChanges_WithNullCategories_ReturnsFalse()
    {
        _viewModel.ShowAsync(categories: null);

        Assert.False(_viewModel.HasChanges);
    }

    #endregion

    #region Close Tests

    [Fact]
    public void Close_WhenOpen_SetsIsOpenToFalse()
    {
        var category = new ChangeCategory { Name = "Test" };
        category.Changes.Add(new ChangeItem { Description = "Change" });
        _viewModel.ShowAsync([category]);
        Assert.True(_viewModel.IsOpen);

        _viewModel.Close();

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public async Task Close_WhenCalled_ReturnsNone()
    {
        var category = new ChangeCategory { Name = "Test" };
        category.Changes.Add(new ChangeItem { Description = "Change" });
        var task = _viewModel.ShowAsync([category]);

        _viewModel.Close();

        var result = await task;
        Assert.Equal(UnsavedChangesResult.None, result);
    }

    #endregion

    #region Command Action Tests

    [Fact]
    public async Task SaveCommand_WhenExecuted_SetsIsOpenToFalseAndReturnsSave()
    {
        var category = new ChangeCategory { Name = "Test" };
        category.Changes.Add(new ChangeItem { Description = "Change" });
        var task = _viewModel.ShowAsync([category]);

        _viewModel.SaveCommand.Execute(null);

        var result = await task;
        Assert.False(_viewModel.IsOpen);
        Assert.Equal(UnsavedChangesResult.Save, result);
    }

    [Fact]
    public async Task DontSaveCommand_WhenExecuted_SetsIsOpenToFalseAndReturnsDontSave()
    {
        var category = new ChangeCategory { Name = "Test" };
        category.Changes.Add(new ChangeItem { Description = "Change" });
        var task = _viewModel.ShowAsync([category]);

        _viewModel.DontSaveCommand.Execute(null);

        var result = await task;
        Assert.False(_viewModel.IsOpen);
        Assert.Equal(UnsavedChangesResult.DontSave, result);
    }

    [Fact]
    public async Task CancelCommand_WhenExecuted_SetsIsOpenToFalseAndReturnsCancel()
    {
        var category = new ChangeCategory { Name = "Test" };
        category.Changes.Add(new ChangeItem { Description = "Change" });
        var task = _viewModel.ShowAsync([category]);

        _viewModel.CancelCommand.Execute(null);

        var result = await task;
        Assert.False(_viewModel.IsOpen);
        Assert.Equal(UnsavedChangesResult.Cancel, result);
    }

    #endregion

    #region ChangeItem Tests

    [Theory]
    [InlineData(ChangeType.Added, "Plus")]
    [InlineData(ChangeType.Deleted, "Trash")]
    [InlineData(ChangeType.Modified, "Pencil")]
    public void ChangeItem_IconName_ReturnsCorrectIconForChangeType(ChangeType changeType, string expectedIcon)
    {
        var item = new ChangeItem { ChangeType = changeType };

        Assert.Equal(expectedIcon, item.IconName);
    }

    [Theory]
    [InlineData(ChangeType.Added, "success")]
    [InlineData(ChangeType.Deleted, "danger")]
    [InlineData(ChangeType.Modified, "warning")]
    public void ChangeItem_ColorClass_ReturnsCorrectColorForChangeType(ChangeType changeType, string expectedColor)
    {
        var item = new ChangeItem { ChangeType = changeType };

        Assert.Equal(expectedColor, item.ColorClass);
    }

    #endregion

    #region ToggleCategory Tests

    [Fact]
    public void ToggleCategoryCommand_WhenExpanded_CollapsesCategory()
    {
        var category = new ChangeCategory { Name = "Test", IsExpanded = true };
        category.Changes.Add(new ChangeItem { Description = "Change" });
        _viewModel.ShowAsync([category]);

        _viewModel.ToggleCategoryCommand.Execute(category);

        Assert.False(category.IsExpanded);
    }

    [Fact]
    public void ToggleCategoryCommand_WhenCollapsed_ExpandsCategory()
    {
        var category = new ChangeCategory { Name = "Test", IsExpanded = false };
        category.Changes.Add(new ChangeItem { Description = "Change" });
        _viewModel.ShowAsync([category]);

        _viewModel.ToggleCategoryCommand.Execute(category);

        Assert.True(category.IsExpanded);
    }

    #endregion
}
