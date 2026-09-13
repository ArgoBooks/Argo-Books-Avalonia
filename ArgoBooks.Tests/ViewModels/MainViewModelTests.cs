using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the MainViewModel class.
/// </summary>
public class MainViewModelTests
{
    #region Default State Tests

    [Fact]
    public void Constructor_DefaultTitle_IsArgoBooks()
    {
        var vm = new MainViewModel();

        Assert.Equal("Argo Books", vm.Title);
    }

    #endregion

    #region Property Change Tests

    [Fact]
    public void IsCompanyOpen_SetTrue_RaisesPropertyChanged()
    {
        var vm = new MainViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsCompanyOpen))
                raised = true;
        };

        vm.IsCompanyOpen = true;

        Assert.True(raised);
        Assert.True(vm.IsCompanyOpen);
    }

    [Fact]
    public void CurrentCompanyName_Set_UpdatesTitle()
    {
        var vm = new MainViewModel
        {
            CurrentCompanyName = "Test Company"
        };

        Assert.Equal("Test Company - Argo Books", vm.Title);
    }

    [Fact]
    public void CurrentCompanyName_SetNull_ResetsTitle()
    {
        var vm = new MainViewModel
        {
            CurrentCompanyName = "Test Company"
        };

        vm.CurrentCompanyName = null;

        Assert.Equal("Argo Books", vm.Title);
    }

    [Fact]
    public void CurrentCompanyName_SetEmpty_ResetsTitle()
    {
        var vm = new MainViewModel
        {
            CurrentCompanyName = "Test Company"
        };

        vm.CurrentCompanyName = "";

        Assert.Equal("Argo Books", vm.Title);
    }

    #endregion

    #region ToggleSidebar Tests

    [Fact]
    public void ToggleSidebarCommand_WhenNotCollapsed_CollapseSidebar()
    {
        var vm = new MainViewModel();
        Assert.False(vm.IsSidebarCollapsed);

        vm.ToggleSidebarCommand.Execute(null);

        Assert.True(vm.IsSidebarCollapsed);
    }

    [Fact]
    public void ToggleSidebarCommand_WhenCollapsed_ExpandSidebar()
    {
        var vm = new MainViewModel
        {
            IsSidebarCollapsed = true
        };

        vm.ToggleSidebarCommand.Execute(null);

        Assert.False(vm.IsSidebarCollapsed);
    }

    [Fact]
    public void ToggleSidebarCommand_DoubleToggle_ReturnToOriginal()
    {
        var vm = new MainViewModel();

        vm.ToggleSidebarCommand.Execute(null);
        vm.ToggleSidebarCommand.Execute(null);

        Assert.False(vm.IsSidebarCollapsed);
    }

    #endregion
}
