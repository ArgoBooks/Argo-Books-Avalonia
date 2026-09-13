using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the NavigationService class.
/// </summary>
public class NavigationServiceTests
{
    private readonly NavigationService _navigationService = new();

    #region NavigateTo Tests

    [Fact]
    public void NavigateTo_WithRegisteredPage_InvokesFactoryCallback()
    {
        var factoryInvoked = false;
        _navigationService.RegisterPage("TestPage", _ =>
        {
            factoryInvoked = true;
            return new object();
        });

        _navigationService.NavigateTo("TestPage");

        Assert.True(factoryInvoked);
    }

    [Fact]
    public void NavigateTo_WithParameter_PassesParameterToFactory()
    {
        object? receivedParameter = null;
        _navigationService.RegisterPage("TestPage", param =>
        {
            receivedParameter = param;
            return new object();
        });

        var expectedParam = "test-param";
        _navigationService.NavigateTo("TestPage", expectedParam);

        Assert.Equal(expectedParam, receivedParameter);
    }

    [Fact]
    public void NavigateTo_PushesToBackStack_CanGoBackBecomesTrue()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());

        _navigationService.NavigateTo("Page1");
        Assert.False(_navigationService.CanGoBack);

        _navigationService.NavigateTo("Page2");
        Assert.True(_navigationService.CanGoBack);
    }

    [Fact]
    public void NavigateTo_ClearsForwardStack()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());
        _navigationService.RegisterPage("Page3", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");
        _navigationService.GoBack();

        Assert.True(_navigationService.CanGoForward);

        _navigationService.NavigateTo("Page3");

        Assert.False(_navigationService.CanGoForward);
    }

    [Fact]
    public void NavigateTo_SetsCurrentPageName()
    {
        _navigationService.RegisterPage("Dashboard", _ => new object());

        _navigationService.NavigateTo("Dashboard");

        Assert.Equal("Dashboard", _navigationService.CurrentPageName);
    }

    [Fact]
    public void NavigateTo_WithEmptyPageName_DoesNothing()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.NavigateTo("Page1");

        _navigationService.NavigateTo(string.Empty);

        Assert.Equal("Page1", _navigationService.CurrentPageName);
        Assert.False(_navigationService.CanGoBack);
    }

    [Fact]
    public void NavigateTo_InvokesNavigationCallback()
    {
        object? callbackPage = null;
        _navigationService.SetNavigationCallback(page => callbackPage = page);

        var expectedPage = new object();
        _navigationService.RegisterPage("TestPage", _ => expectedPage);

        _navigationService.NavigateTo("TestPage");

        Assert.Same(expectedPage, callbackPage);
    }

    #endregion

    #region GoBack Tests

    [Fact]
    public void GoBack_ReturnsPreviousPage()
    {
        object? currentPage = null;
        _navigationService.SetNavigationCallback(page => currentPage = page);

        var page1 = new object();
        var page2 = new object();
        _navigationService.RegisterPage("Page1", _ => page1);
        _navigationService.RegisterPage("Page2", _ => page2);

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");

        var result = _navigationService.GoBack();

        Assert.True(result);
        Assert.Equal("Page1", _navigationService.CurrentPageName);
        Assert.Same(page1, currentPage);
    }

    [Fact]
    public void GoBack_WhenEmpty_ReturnsFalseAndDoesNothing()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.NavigateTo("Page1");

        var result = _navigationService.GoBack();

        Assert.False(result);
        Assert.Equal("Page1", _navigationService.CurrentPageName);
    }

    [Fact]
    public void GoBack_WhenNoCurrentEntry_ReturnsFalse()
    {
        var result = _navigationService.GoBack();

        Assert.False(result);
    }

    [Fact]
    public void GoBack_PushesCurrentToForwardStack()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");

        _navigationService.GoBack();

        Assert.True(_navigationService.CanGoForward);
    }

    #endregion

    #region GoForward Tests

    [Fact]
    public void GoForward_ReturnsForwardPage()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");
        _navigationService.GoBack();

        var result = _navigationService.GoForward();

        Assert.True(result);
        Assert.Equal("Page2", _navigationService.CurrentPageName);
    }

    [Fact]
    public void GoForward_WhenNoForwardHistory_ReturnsFalse()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.NavigateTo("Page1");

        var result = _navigationService.GoForward();

        Assert.False(result);
    }

    [Fact]
    public void GoForward_PushesCurrentToBackStack()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");
        _navigationService.GoBack();
        _navigationService.GoForward();

        Assert.True(_navigationService.CanGoBack);
    }

    #endregion

    #region CanGoBack / CanGoForward State Tracking Tests

    [Fact]
    public void CanGoBack_AfterMultipleNavigations_TracksCorrectly()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());
        _navigationService.RegisterPage("Page3", _ => new object());

        _navigationService.NavigateTo("Page1");
        Assert.False(_navigationService.CanGoBack);

        _navigationService.NavigateTo("Page2");
        Assert.True(_navigationService.CanGoBack);

        _navigationService.NavigateTo("Page3");
        Assert.True(_navigationService.CanGoBack);

        _navigationService.GoBack();
        Assert.True(_navigationService.CanGoBack);

        _navigationService.GoBack();
        Assert.False(_navigationService.CanGoBack);
    }

    [Fact]
    public void CanGoForward_AfterGoBackAndNavigate_TracksCorrectly()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());
        _navigationService.RegisterPage("Page3", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");
        _navigationService.GoBack();
        Assert.True(_navigationService.CanGoForward);

        _navigationService.NavigateTo("Page3");
        Assert.False(_navigationService.CanGoForward);
    }

    #endregion

    #region ClearHistory Tests

    [Fact]
    public void ClearHistory_EmptiesBackAndForwardStacks()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());
        _navigationService.RegisterPage("Page3", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");
        _navigationService.NavigateTo("Page3");
        _navigationService.GoBack();

        Assert.True(_navigationService.CanGoBack);
        Assert.True(_navigationService.CanGoForward);

        _navigationService.ClearHistory();

        Assert.False(_navigationService.CanGoBack);
        Assert.False(_navigationService.CanGoForward);
    }

    [Fact]
    public void ClearHistory_PreservesCurrentPage()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");

        _navigationService.ClearHistory();

        Assert.Equal("Page2", _navigationService.CurrentPageName);
    }

    #endregion

    #region Navigated Event Tests

    [Fact]
    public void NavigateTo_RaisesNavigatedEvent()
    {
        NavigationEventArgs? raisedArgs = null;
        _navigationService.Navigated += (_, args) => raisedArgs = args;
        _navigationService.RegisterPage("TestPage", _ => new object());

        _navigationService.NavigateTo("TestPage");

        Assert.NotNull(raisedArgs);
        Assert.Equal("TestPage", raisedArgs.PageName);
    }

    [Fact]
    public void NavigateTo_RaisesNavigatedEvent_WithPreviousPageName()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());

        _navigationService.NavigateTo("Page1");

        NavigationEventArgs? raisedArgs = null;
        _navigationService.Navigated += (_, args) => raisedArgs = args;

        _navigationService.NavigateTo("Page2");

        Assert.NotNull(raisedArgs);
        Assert.Equal("Page2", raisedArgs.PageName);
        Assert.Equal("Page1", raisedArgs.PreviousPageName);
    }

    [Fact]
    public void NavigateTo_RaisesNavigatedEvent_WithParameter()
    {
        NavigationEventArgs? raisedArgs = null;
        _navigationService.Navigated += (_, args) => raisedArgs = args;
        _navigationService.RegisterPage("TestPage", _ => new object());

        var parameter = new { Id = 42 };
        _navigationService.NavigateTo("TestPage", parameter);

        Assert.NotNull(raisedArgs);
        Assert.Same(parameter, raisedArgs.Parameter);
    }

    [Fact]
    public void GoBack_RaisesNavigatedEvent()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");

        NavigationEventArgs? raisedArgs = null;
        _navigationService.Navigated += (_, args) => raisedArgs = args;

        _navigationService.GoBack();

        Assert.NotNull(raisedArgs);
        Assert.Equal("Page1", raisedArgs.PageName);
        Assert.Equal("Page2", raisedArgs.PreviousPageName);
    }

    [Fact]
    public void GoForward_RaisesNavigatedEvent()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");
        _navigationService.GoBack();

        NavigationEventArgs? raisedArgs = null;
        _navigationService.Navigated += (_, args) => raisedArgs = args;

        _navigationService.GoForward();

        Assert.NotNull(raisedArgs);
        Assert.Equal("Page2", raisedArgs.PageName);
        Assert.Equal("Page1", raisedArgs.PreviousPageName);
    }

    #endregion

    #region NavigateToAsync Tests

    [Fact]
    public async Task NavigateToAsync_WithNoGuards_NavigatesSuccessfully()
    {
        _navigationService.RegisterPage("TestPage", _ => new object());

        var result = await _navigationService.NavigateToAsync("TestPage");

        Assert.True(result);
        Assert.Equal("TestPage", _navigationService.CurrentPageName);
    }

    [Fact]
    public async Task NavigateToAsync_WithGuardThatAllows_NavigatesSuccessfully()
    {
        _navigationService.RegisterPage("TestPage", _ => new object());
        _navigationService.RegisterNavigationGuard((_, _) => Task.FromResult(true));

        var result = await _navigationService.NavigateToAsync("TestPage");

        Assert.True(result);
        Assert.Equal("TestPage", _navigationService.CurrentPageName);
    }

    [Fact]
    public async Task NavigateToAsync_WithGuardThatBlocks_DoesNotNavigate()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.RegisterNavigationGuard((_, _) => Task.FromResult(false));

        var result = await _navigationService.NavigateToAsync("Page2");

        Assert.False(result);
        Assert.Equal("Page1", _navigationService.CurrentPageName);
    }

    [Fact]
    public async Task NavigateToAsync_WithEmptyPageName_ReturnsFalse()
    {
        var result = await _navigationService.NavigateToAsync(string.Empty);

        Assert.False(result);
    }

    #endregion

    #region GetHistory Tests

    [Fact]
    public void GetHistory_ReturnsNavigationHistory()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());
        _navigationService.RegisterPage("Page3", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");
        _navigationService.NavigateTo("Page3");

        var history = _navigationService.GetHistory();

        Assert.Equal(3, history.Count);
        Assert.Equal("Page1", history[0]);
        Assert.Equal("Page2", history[1]);
        Assert.Equal("Page3", history[2]);
    }

    #endregion

    #region GetCurrentParameter Tests

    [Fact]
    public void GetCurrentParameter_ReturnsParameterForCurrentPage()
    {
        _navigationService.RegisterPage("TestPage", _ => new object());
        var parameter = "my-parameter";

        _navigationService.NavigateTo("TestPage", parameter);

        Assert.Equal(parameter, _navigationService.GetCurrentParameter());
    }

    [Fact]
    public void GetCurrentParameter_WhenNoCurrentPage_ReturnsNull()
    {
        Assert.Null(_navigationService.GetCurrentParameter());
    }

    #endregion

    #region RefreshCurrentPage Tests

    [Fact]
    public void RefreshCurrentPage_ReInvokesFactory()
    {
        var invokeCount = 0;
        _navigationService.RegisterPage("TestPage", _ =>
        {
            invokeCount++;
            return new object();
        });
        _navigationService.NavigateTo("TestPage");
        Assert.Equal(1, invokeCount);

        _navigationService.RefreshCurrentPage();

        Assert.Equal(2, invokeCount);
    }

    [Fact]
    public void RefreshCurrentPage_DoesNotAffectBackStack()
    {
        _navigationService.RegisterPage("Page1", _ => new object());
        _navigationService.RegisterPage("Page2", _ => new object());

        _navigationService.NavigateTo("Page1");
        _navigationService.NavigateTo("Page2");

        Assert.True(_navigationService.CanGoBack);

        _navigationService.RefreshCurrentPage();

        Assert.True(_navigationService.CanGoBack);
        Assert.Equal("Page2", _navigationService.CurrentPageName);
    }

    #endregion

    #region RegisterPage Overload Tests

    [Fact]
    public void RegisterPage_SimpleFactory_InvokesCorrectly()
    {
        var factoryInvoked = false;
        _navigationService.RegisterPage("TestPage", () =>
        {
            factoryInvoked = true;
            return new object();
        });

        _navigationService.NavigateTo("TestPage");

        Assert.True(factoryInvoked);
    }

    #endregion

    #region Navigation Guard Tests

    [Fact]
    public async Task UnregisterNavigationGuard_RemovesGuard()
    {
        NavigationGuardCallback guard = (_, _) => Task.FromResult(false);
        _navigationService.RegisterNavigationGuard(guard);
        _navigationService.UnregisterNavigationGuard(guard);

        _navigationService.RegisterPage("TestPage", _ => new object());
        var result = await _navigationService.NavigateToAsync("TestPage");

        Assert.True(result);
    }

    #endregion
}
