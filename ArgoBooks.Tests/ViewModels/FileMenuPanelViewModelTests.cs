using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the FileMenuPanelViewModel.
/// </summary>
public class FileMenuPanelViewModelTests
{
    private readonly FileMenuPanelViewModel _viewModel = new();

    #region Default State Tests

    [Fact]
    public void Constructor_Default_HasDesignTimeRecentCompanies()
    {
        Assert.Equal(3, _viewModel.RecentCompanies.Count);
    }

    [Fact]
    public void Constructor_Default_HasFilteredRecentCompaniesIsFalse()
    {
        // Design-time items have null FilePath, which matches the default null CurrentCompanyPath,
        // so all items are filtered out and HasFilteredRecentCompanies is false.
        Assert.False(_viewModel.HasFilteredRecentCompanies);
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

    #region Menu Action Commands Tests

    [Fact]
    public void NewCompanyCommand_WhenExecuted_ClosesPanel()
    {
        _viewModel.IsOpen = true;

        _viewModel.NewCompanyCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void OpenCompanyCommand_WhenExecuted_ClosesPanel()
    {
        _viewModel.IsOpen = true;

        _viewModel.OpenCompanyCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void SaveCommand_WhenExecuted_ClosesPanel()
    {
        _viewModel.IsOpen = true;

        _viewModel.SaveCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void SaveAsCommand_WhenExecuted_ClosesPanel()
    {
        _viewModel.IsOpen = true;

        _viewModel.SaveAsCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void CloseCompanyCommand_WhenExecuted_ClosesPanel()
    {
        _viewModel.IsOpen = true;

        _viewModel.CloseCompanyCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void ImportCommand_WhenExecuted_ClosesPanel()
    {
        _viewModel.IsOpen = true;

        _viewModel.ImportCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void ExportAsCommand_WhenExecuted_ClosesPanel()
    {
        _viewModel.IsOpen = true;

        _viewModel.ExportAsCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    [Fact]
    public void ShowInFolderCommand_WhenExecuted_ClosesPanel()
    {
        _viewModel.IsOpen = true;

        _viewModel.ShowInFolderCommand.Execute(null);

        Assert.False(_viewModel.IsOpen);
    }

    #endregion

    #region Event Raising Tests

    [Fact]
    public void NewCompanyCommand_RaisesCreateNewCompanyRequestedEvent()
    {
        var eventRaised = false;
        _viewModel.CreateNewCompanyRequested += (_, _) => eventRaised = true;

        _viewModel.NewCompanyCommand.Execute(null);

        Assert.True(eventRaised);
    }

    [Fact]
    public void OpenCompanyCommand_RaisesOpenCompanyRequestedEvent()
    {
        var eventRaised = false;
        _viewModel.OpenCompanyRequested += (_, _) => eventRaised = true;

        _viewModel.OpenCompanyCommand.Execute(null);

        Assert.True(eventRaised);
    }

    [Fact]
    public void SaveCommand_RaisesSaveRequestedEvent()
    {
        var eventRaised = false;
        _viewModel.SaveRequested += (_, _) => eventRaised = true;

        _viewModel.SaveCommand.Execute(null);

        Assert.True(eventRaised);
    }

    [Fact]
    public void SaveAsCommand_RaisesSaveAsRequestedEvent()
    {
        var eventRaised = false;
        _viewModel.SaveAsRequested += (_, _) => eventRaised = true;

        _viewModel.SaveAsCommand.Execute(null);

        Assert.True(eventRaised);
    }

    #endregion

    #region SetCurrentCompany Tests

    [Fact]
    public void SetCurrentCompany_WithPath_SetsCurrentCompanyPath()
    {
        _viewModel.SetCurrentCompany("/path/to/company.argo");

        Assert.Equal("/path/to/company.argo", _viewModel.CurrentCompanyPath);
    }

    [Fact]
    public void SetCurrentCompany_WithNull_SetsCurrentCompanyPathToNull()
    {
        _viewModel.SetCurrentCompany("/path/to/company.argo");

        _viewModel.SetCurrentCompany(null);

        Assert.Null(_viewModel.CurrentCompanyPath);
    }

    [Fact]
    public void SetCurrentCompany_FiltersRecentCompanies()
    {
        _viewModel.RecentCompanies.Clear();
        _viewModel.RecentCompanies.Add(new RecentCompanyItem { Name = "Company A", FilePath = "/a.argo" });
        _viewModel.RecentCompanies.Add(new RecentCompanyItem { Name = "Company B", FilePath = "/b.argo" });

        _viewModel.SetCurrentCompany("/a.argo");

        var filtered = _viewModel.FilteredRecentCompanies.ToList();
        Assert.Single(filtered);
        Assert.Equal("Company B", filtered[0].Name);
    }

    #endregion

    #region ClearRecent Tests

    [Fact]
    public void ClearRecentCommand_ClearsRecentCompanies()
    {
        Assert.True(_viewModel.RecentCompanies.Count > 0);

        _viewModel.ClearRecentCommand.Execute(null);

        Assert.Empty(_viewModel.RecentCompanies);
    }

    [Fact]
    public void ClearRecentCommand_UpdatesHasFilteredRecentCompanies()
    {
        _viewModel.ClearRecentCommand.Execute(null);

        Assert.False(_viewModel.HasFilteredRecentCompanies);
    }

    #endregion

    #region Dynamic Positioning Tests

    [Fact]
    public void PanelLeftOffset_WithoutSidebar_DefaultsToExpandedWidth()
    {
        // Without a sidebar ViewModel, defaults to 250 (expanded) + 16 (offset)
        Assert.Equal(266, _viewModel.PanelLeftOffset);
    }

    [Fact]
    public void SubmenuLeftOffset_WithoutSidebar_IncludesPanelWidth()
    {
        // PanelLeftOffset (266) + PanelWidth (240)
        Assert.Equal(506, _viewModel.SubmenuLeftOffset);
    }

    [Fact]
    public void SetSidebarViewModel_UpdatesPositioning()
    {
        var sidebarVm = new SidebarViewModel();
        _viewModel.SetSidebarViewModel(sidebarVm);

        // Default expanded (250) + offset (16) = 266
        Assert.Equal(266, _viewModel.PanelLeftOffset);

        // Collapse sidebar to 70
        sidebarVm.IsCollapsed = true;

        // Collapsed (70) + offset (16) = 86
        Assert.Equal(86, _viewModel.PanelLeftOffset);
    }

    #endregion
}
