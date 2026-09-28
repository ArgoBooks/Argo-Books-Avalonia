namespace ArgoBooks.Core.Services;

/// <summary>
/// Delegate for navigation guard callbacks that can prevent or allow navigation.
/// </summary>
/// <param name="fromPage">The page being navigated from.</param>
/// <param name="toPage">The page being navigated to.</param>
/// <returns>True to allow navigation, false to cancel it.</returns>
public delegate Task<bool> NavigationGuardCallback(string fromPage, string toPage);

/// <summary>
/// Service for navigating between pages in the application.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Gets the name of the current page.
    /// </summary>
    string CurrentPageName { get; }

    /// <summary>
    /// Navigates to the specified page with an optional parameter.
    /// </summary>
    /// <param name="pageName">Name of the page to navigate to.</param>
    /// <param name="parameter">Optional parameter to pass to the page.</param>
    void NavigateTo(string pageName, object? parameter = null);

    /// <summary>
    /// Navigates to the specified page asynchronously, checking navigation guards.
    /// </summary>
    /// <param name="pageName">Name of the page to navigate to.</param>
    /// <param name="parameter">Optional parameter to pass to the page.</param>
    /// <returns>True if navigation occurred, false if cancelled by a guard.</returns>
    Task<bool> NavigateToAsync(string pageName, object? parameter = null);

    /// <summary>
    /// Re-creates the current page to refresh its content.
    /// </summary>
    void RefreshCurrentPage();

    /// <summary>
    /// Registers a navigation guard that can prevent navigation.
    /// </summary>
    /// <param name="guard">The guard callback to register.</param>
    void RegisterNavigationGuard(NavigationGuardCallback guard);

    /// <summary>
    /// Event raised when navigation occurs.
    /// </summary>
    event EventHandler<NavigationEventArgs>? Navigated;
}

/// <summary>
/// Event arguments for navigation events.
/// </summary>
public class NavigationEventArgs : EventArgs
{
    /// <summary>
    /// The page being navigated to.
    /// </summary>
    public string PageName { get; }

    /// <summary>
    /// The page being navigated from.
    /// </summary>
    public string? PreviousPageName { get; }

    /// <summary>
    /// Optional parameter passed during navigation.
    /// </summary>
    public object? Parameter { get; }

    /// <summary>
    /// Creates a new NavigationEventArgs instance.
    /// </summary>
    public NavigationEventArgs(string pageName, string? previousPageName = null, object? parameter = null)
    {
        PageName = pageName;
        PreviousPageName = previousPageName;
        Parameter = parameter;
    }
}
