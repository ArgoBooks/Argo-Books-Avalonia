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
}
