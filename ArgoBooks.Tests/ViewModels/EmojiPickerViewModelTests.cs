using ArgoBooks.Data;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the EmojiPickerViewModel.
/// </summary>
public class EmojiPickerViewModelTests
{
    private readonly EmojiPickerViewModel _viewModel = new();

    #region Tabs Initialization Tests

    [Fact]
    public void Constructor_InitializesTabsWithSpecialTabs()
    {
        Assert.True(_viewModel.Tabs.Count >= 2);
        Assert.Equal("Recent", _viewModel.Tabs[0].Name);
        Assert.Equal("Favorites", _viewModel.Tabs[1].Name);
    }

    [Fact]
    public void Constructor_RecentTabIsSpecial()
    {
        var recentTab = _viewModel.Tabs.First(t => t.Name == "Recent");

        Assert.True(recentTab.IsSpecial);
    }

    [Fact]
    public void Constructor_FavoritesTabIsSpecial()
    {
        var favoritesTab = _viewModel.Tabs.First(t => t.Name == "Favorites");

        Assert.True(favoritesTab.IsSpecial);
    }

    [Fact]
    public void Constructor_InitializesCategoryTabs()
    {
        // Should have special tabs + category tabs
        var categoryTabCount = _viewModel.Tabs.Count(t => !t.IsSpecial);

        Assert.Equal(EmojiData.Categories.Length, categoryTabCount);
    }

    [Fact]
    public void Constructor_CategoryTabsMatchEmojiDataCategories()
    {
        var categoryTabs = _viewModel.Tabs.Where(t => !t.IsSpecial).ToList();

        for (int i = 0; i < EmojiData.Categories.Length; i++)
        {
            Assert.Equal(EmojiData.Categories[i].Name, categoryTabs[i].Name);
            Assert.Equal(EmojiData.Categories[i].Icon, categoryTabs[i].Icon);
        }
    }

    [Fact]
    public void Constructor_FirstCategoryIsOffice()
    {
        var firstCategory = _viewModel.Tabs.First(t => !t.IsSpecial);

        Assert.Equal("Office", firstCategory.Name);
    }

    #endregion

    #region Category Browsing Tests

    [Fact]
    public void SelectTab_SetsSelectedTab()
    {
        var tab = _viewModel.Tabs.First(t => !t.IsSpecial);

        _viewModel.SelectTabCommand.Execute(tab);

        Assert.Equal(tab, _viewModel.SelectedTab);
    }

    [Fact]
    public void SelectTab_MarksTabAsSelected()
    {
        var tab = _viewModel.Tabs.First(t => !t.IsSpecial);

        _viewModel.SelectTabCommand.Execute(tab);

        Assert.True(tab.IsSelected);
    }

    [Fact]
    public void SelectTab_DeselectsPreviousTab()
    {
        var firstTab = _viewModel.Tabs.First(t => !t.IsSpecial);
        var secondTab = _viewModel.Tabs.Where(t => !t.IsSpecial).Skip(1).First();

        _viewModel.SelectTabCommand.Execute(firstTab);
        _viewModel.SelectTabCommand.Execute(secondTab);

        Assert.False(firstTab.IsSelected);
        Assert.True(secondTab.IsSelected);
    }

    [Fact]
    public void SelectTab_ClearsSearchText()
    {
        _viewModel.SearchText = "some search";
        var tab = _viewModel.Tabs.First(t => !t.IsSpecial);

        _viewModel.SelectTabCommand.Execute(tab);

        Assert.Equal(string.Empty, _viewModel.SearchText);
    }

    [Fact]
    public void SelectTab_WithNull_DoesNothing()
    {
        var currentTab = _viewModel.SelectedTab;

        _viewModel.SelectTabCommand.Execute(null);

        Assert.Equal(currentTab, _viewModel.SelectedTab);
    }

    [Fact]
    public void SelectTab_OnlyOneTabIsSelectedAtATime()
    {
        var tab = _viewModel.Tabs.First(t => !t.IsSpecial);

        _viewModel.SelectTabCommand.Execute(tab);

        var selectedCount = _viewModel.Tabs.Count(t => t.IsSelected);
        Assert.Equal(1, selectedCount);
    }

    #endregion

    #region Search Tests

    [Fact]
    public void ClearSearchCommand_ClearsSearchText()
    {
        _viewModel.SearchText = "test";

        _viewModel.ClearSearchCommand.Execute(null);

        Assert.Equal(string.Empty, _viewModel.SearchText);
    }

    #endregion

    #region Close Tests

    [Fact]
    public void CloseCommand_SetsIsOpenToFalse()
    {
        _viewModel.IsOpen = true;

        _viewModel.CloseCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    #endregion

    #region EmptyState and ShowClearRecent Tests

    [Fact]
    public void EmptyStateMessage_ForRecentTab_ReturnsCorrectMessage()
    {
        // Directly set SelectedTab for testing the property
        var recentTab = _viewModel.Tabs.First(t => t.Name == "Recent");
        _viewModel.SelectTabCommand.Execute(recentTab);

        Assert.Equal("No recent emojis yet", _viewModel.EmptyStateMessage);
    }

    [Fact]
    public void EmptyStateMessage_ForFavoritesTab_ReturnsCorrectMessage()
    {
        var favoritesTab = _viewModel.Tabs.First(t => t.Name == "Favorites");
        _viewModel.SelectTabCommand.Execute(favoritesTab);

        Assert.Equal("No favorite emojis yet", _viewModel.EmptyStateMessage);
    }

    [Fact]
    public void EmptyStateHint_ForRecentTab_ReturnsCorrectHint()
    {
        var recentTab = _viewModel.Tabs.First(t => t.Name == "Recent");
        _viewModel.SelectTabCommand.Execute(recentTab);

        Assert.Equal("Emojis you select will appear here", _viewModel.EmptyStateHint);
    }

    [Fact]
    public void EmptyStateHint_ForFavoritesTab_ReturnsCorrectHint()
    {
        var favoritesTab = _viewModel.Tabs.First(t => t.Name == "Favorites");
        _viewModel.SelectTabCommand.Execute(favoritesTab);

        Assert.Equal("Right-click an emoji to add it to favorites", _viewModel.EmptyStateHint);
    }

    [Fact]
    public void EmptyStateMessage_ForCategoryTab_ReturnsEmptyString()
    {
        var categoryTab = _viewModel.Tabs.First(t => !t.IsSpecial);
        _viewModel.SelectTabCommand.Execute(categoryTab);

        Assert.Equal("", _viewModel.EmptyStateMessage);
    }

    #endregion

}
