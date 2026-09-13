using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the SidebarViewModel.
/// </summary>
public class SidebarViewModelTests
{
    private readonly SidebarViewModel _viewModel = new();

    #region Constants Tests

    [Fact]
    public void ExpandedWidth_Value_Is250()
    {
        // The default Width should match ExpandedWidth (250)
        var vm = new SidebarViewModel();
        Assert.Equal(250, vm.Width);
    }

    [Fact]
    public void CollapsedWidth_WhenCollapsed_Is70()
    {
        _viewModel.IsCollapsed = true;
        Assert.Equal(70, _viewModel.Width);
    }

    #endregion

    #region Default State Tests

    [Fact]
    public void Constructor_DefaultState_CompanyNameIsArgoBooks()
    {
        Assert.Equal("Argo Books", _viewModel.CompanyName);
    }

    [Fact]
    public void Constructor_DefaultState_CompanyInitialIsA()
    {
        Assert.Equal("A", _viewModel.CompanyInitial);
    }

    [Fact]
    public void Constructor_DefaultState_CurrentPageIsDashboard()
    {
        Assert.Equal("Dashboard", _viewModel.CurrentPage);
    }

    [Fact]
    public void Constructor_DefaultState_CollapseTooltipIsCollapseSidebar()
    {
        Assert.Equal("Collapse sidebar", _viewModel.CollapseTooltip);
    }

    #endregion

    #region ToggleCollapse Tests

    [Fact]
    public void ToggleCollapseCommand_WhenExpanded_SetsIsCollapsedToTrue()
    {
        Assert.False(_viewModel.IsCollapsed);

        _viewModel.ToggleCollapseCommand.Execute(null);

        Assert.True(_viewModel.IsCollapsed);
    }

    [Fact]
    public void ToggleCollapseCommand_WhenExpanded_UpdatesWidthToCollapsedWidth()
    {
        _viewModel.ToggleCollapseCommand.Execute(null);

        Assert.Equal(70, _viewModel.Width);
    }

    [Fact]
    public void ToggleCollapseCommand_WhenCollapsed_SetsIsCollapsedToFalse()
    {
        _viewModel.IsCollapsed = true;

        _viewModel.ToggleCollapseCommand.Execute(null);

        Assert.False(_viewModel.IsCollapsed);
    }

    [Fact]
    public void ToggleCollapseCommand_WhenCollapsed_UpdatesWidthToExpandedWidth()
    {
        _viewModel.IsCollapsed = true;

        _viewModel.ToggleCollapseCommand.Execute(null);

        Assert.Equal(250, _viewModel.Width);
    }

    [Fact]
    public void ToggleCollapseCommand_WhenExpanded_UpdatesCollapseTooltipToExpand()
    {
        _viewModel.ToggleCollapseCommand.Execute(null);

        Assert.Equal("Expand sidebar", _viewModel.CollapseTooltip);
    }

    [Fact]
    public void ToggleCollapseCommand_WhenCollapsed_UpdatesCollapseTooltipToCollapse()
    {
        _viewModel.IsCollapsed = true;

        _viewModel.ToggleCollapseCommand.Execute(null);

        Assert.Equal("Collapse sidebar", _viewModel.CollapseTooltip);
    }

    [Fact]
    public void ToggleCollapseCommand_DoubleTap_RestoresOriginalState()
    {
        _viewModel.ToggleCollapseCommand.Execute(null);
        _viewModel.ToggleCollapseCommand.Execute(null);

        Assert.False(_viewModel.IsCollapsed);
        Assert.Equal(250, _viewModel.Width);
    }

    #endregion

    #region SetCompanyInfo Tests

    [Fact]
    public void SetCompanyInfo_WithName_SetsCompanyName()
    {
        _viewModel.SetCompanyInfo("Test Company");

        Assert.Equal("Test Company", _viewModel.CompanyName);
    }

    [Fact]
    public void SetCompanyInfo_WithName_SetsCompanyInitialToFirstChar()
    {
        _viewModel.SetCompanyInfo("Test Company");

        Assert.Equal("T", _viewModel.CompanyInitial);
    }

    [Fact]
    public void SetCompanyInfo_WithNull_SetsCompanyNameToDefault()
    {
        _viewModel.SetCompanyInfo(null);

        Assert.Equal("Argo Books", _viewModel.CompanyName);
    }

    [Fact]
    public void SetCompanyInfo_WithUserRole_SetsUserRole()
    {
        _viewModel.SetCompanyInfo("Company", userRole: "Administrator");

        Assert.Equal("Administrator", _viewModel.UserRole);
    }

    [Fact]
    public void SetCompanyInfo_WithNoLogo_HasCompanyLogoIsFalse()
    {
        _viewModel.SetCompanyInfo("Company");

        Assert.False(_viewModel.HasCompanyLogo);
        Assert.Null(_viewModel.CompanyLogo);
    }

    [Fact]
    public void SetCompanyInfo_WithLowercaseName_ConvertsInitialToUpperCase()
    {
        _viewModel.SetCompanyInfo("lowercase company");

        Assert.Equal("L", _viewModel.CompanyInitial);
    }

    #endregion

    #region CompanyName Change Tests

    [Fact]
    public void CompanyName_WhenSetToEmpty_CompanyInitialDefaultsToA()
    {
        _viewModel.CompanyName = "";

        Assert.Equal("A", _viewModel.CompanyInitial);
    }

    [Fact]
    public void CompanyName_WhenSetToNull_CompanyInitialDefaultsToA()
    {
        _viewModel.CompanyName = null;

        Assert.Equal("A", _viewModel.CompanyInitial);
    }

    [Fact]
    public void CompanyName_WhenChanged_UpdatesCompanyInitial()
    {
        _viewModel.CompanyName = "Bravo Corp";

        Assert.Equal("B", _viewModel.CompanyInitial);
    }

    #endregion

    #region SetActivePage Tests

    [Fact]
    public void SetActivePage_WithValidPage_UpdatesCurrentPage()
    {
        _viewModel.SetActivePage("Analytics");

        Assert.Equal("Analytics", _viewModel.CurrentPage);
    }

    [Fact]
    public void SetActivePage_WithDashboard_ActivatesCorrectItem()
    {
        _viewModel.SetActivePage("Dashboard");

        Assert.Contains(_viewModel.MainItems, item => item.PageName == "Dashboard" && item.IsActive);
    }

    [Fact]
    public void SetActivePage_WithNewPage_DeactivatesPreviousPage()
    {
        _viewModel.SetActivePage("Dashboard");
        _viewModel.SetActivePage("Analytics");

        Assert.DoesNotContain(_viewModel.MainItems, item => item.PageName == "Dashboard" && item.IsActive);
        Assert.Contains(_viewModel.MainItems, item => item.PageName == "Analytics" && item.IsActive);
    }

    [Fact]
    public void SetActivePage_WithExpenses_ActivatesExpenseItem()
    {
        _viewModel.SetActivePage("Expenses");

        Assert.Contains(_viewModel.ExpenseItems, item => item.PageName == "Expenses" && item.IsActive);
    }

    #endregion

    #region Feature Visibility Tests

    [Fact]
    public void Constructor_DefaultState_ShowTransactionsIsTrue()
    {
        Assert.True(_viewModel.ShowTransactions);
    }

    [Fact]
    public void Constructor_DefaultState_ShowInventoryIsTrue()
    {
        Assert.True(_viewModel.ShowInventory);
    }

    [Fact]
    public void Constructor_DefaultState_ShowRentalsIsTrue()
    {
        Assert.True(_viewModel.ShowRentals);
    }

    [Fact]
    public void Constructor_DefaultState_HasPremiumIsFalse()
    {
        Assert.False(_viewModel.HasPremium);
    }

    [Fact]
    public void UpdateFeatureVisibility_SetsAllFlags()
    {
        _viewModel.UpdateFeatureVisibility(false, false, false, false);

        Assert.False(_viewModel.ShowTransactions);
        Assert.False(_viewModel.ShowInventory);
        Assert.False(_viewModel.ShowRentals);
        Assert.False(_viewModel.ShowPayroll);
    }

    [Fact]
    public void UpdateFeatureVisibility_IndividualFlags_SetCorrectly()
    {
        _viewModel.UpdateFeatureVisibility(true, false, true, false);

        Assert.True(_viewModel.ShowTransactions);
        Assert.False(_viewModel.ShowInventory);
        Assert.True(_viewModel.ShowRentals);
        Assert.False(_viewModel.ShowPayroll);
    }

    #endregion

    #region Premium/Enterprise Feature Tests

    [Fact]
    public void HasPremium_WhenSetToTrue_ShowsPremiumItems()
    {
        _viewModel.HasPremium = true;

        // Insights item in MainItems should be visible
        var insightsItem = _viewModel.MainItems.FirstOrDefault(i => i.PageName == "Insights");
        Assert.NotNull(insightsItem);
        Assert.True(insightsItem.IsVisible);
    }

    [Fact]
    public void HasPremium_WhenSetToFalse_DoesNotShowProBadgeOnInsights()
    {
        _viewModel.HasPremium = true;
        _viewModel.HasPremium = false;

        var insightsItem = _viewModel.MainItems.FirstOrDefault(i => i.PageName == "Insights");
        Assert.NotNull(insightsItem);
        Assert.Null(insightsItem.BadgeText);
    }

    #endregion

    #region Navigation Items Initialization Tests

    [Fact]
    public void Constructor_InitializesMainItems()
    {
        Assert.True(_viewModel.MainItems.Count > 0);
    }

    [Fact]
    public void Constructor_InitializesExpenseItems()
    {
        Assert.True(_viewModel.ExpenseItems.Count > 0);
    }

    [Fact]
    public void Constructor_InitializesRevenueItems()
    {
        Assert.True(_viewModel.RevenueItems.Count > 0);
    }

    [Fact]
    public void Constructor_InitializesImportItems()
    {
        Assert.True(_viewModel.ImportItems.Count > 0);
    }

    [Fact]
    public void Constructor_InitializesRentalItems()
    {
        Assert.True(_viewModel.RentalItems.Count > 0);
    }

    [Fact]
    public void Constructor_InitializesInventoryItems()
    {
        Assert.True(_viewModel.InventoryItems.Count > 0);
    }

    [Fact]
    public void Constructor_InitializesTrackingItems()
    {
        Assert.True(_viewModel.TrackingItems.Count > 0);
    }

    [Fact]
    public void Constructor_MainItems_ContainsDashboard()
    {
        Assert.Contains(_viewModel.MainItems, item => item.PageName == "Dashboard");
    }

    [Fact]
    public void Constructor_DashboardIsActiveByDefault()
    {
        var dashboard = _viewModel.MainItems.FirstOrDefault(i => i.PageName == "Dashboard");
        Assert.NotNull(dashboard);
        Assert.True(dashboard.IsActive);
    }

    #endregion
}
