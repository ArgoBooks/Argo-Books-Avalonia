namespace ArgoBooks.Core.Services;

/// <summary>
/// Implementation of INavigationService providing page navigation.
/// </summary>
public class NavigationService : INavigationService
{
    private NavigationEntry? _currentEntry;

    private readonly Dictionary<string, Func<object?, object>> _pageFactories = new();
    private readonly List<NavigationGuardCallback> _navigationGuards = [];
    private Action<object>? _navigationCallback;

    /// <inheritdoc />
    public string CurrentPageName => _currentEntry?.PageName ?? string.Empty;

    /// <inheritdoc />
    public event EventHandler<NavigationEventArgs>? Navigated;

    /// <summary>
    /// Registers a page factory for creating views/view models by page name.
    /// </summary>
    /// <param name="pageName">The page name identifier.</param>
    /// <param name="factory">Factory function that creates the page (receives optional parameter).</param>
    public void RegisterPage(string pageName, Func<object?, object> factory)
    {
        _pageFactories[pageName] = factory;
    }

    /// <summary>
    /// Registers a simple page factory without parameter support.
    /// </summary>
    /// <param name="pageName">The page name identifier.</param>
    /// <param name="factory">Factory function that creates the page.</param>
    public void RegisterPage(string pageName, Func<object> factory)
    {
        _pageFactories[pageName] = _ => factory();
    }

    /// <summary>
    /// Sets the callback to invoke when navigation occurs (to update the current view).
    /// </summary>
    /// <param name="callback">Callback that receives the new page view/viewmodel.</param>
    public void SetNavigationCallback(Action<object> callback)
    {
        _navigationCallback = callback;
    }

    /// <inheritdoc />
    public void NavigateTo(string pageName, object? parameter = null)
    {
        if (string.IsNullOrEmpty(pageName))
            return;

        var previousPageName = _currentEntry?.PageName;
        _currentEntry = new NavigationEntry(pageName, parameter);

        // Navigate and notify
        PerformNavigation(previousPageName);
    }

    /// <inheritdoc />
    public async Task<bool> NavigateToAsync(string pageName, object? parameter = null)
    {
        if (string.IsNullOrEmpty(pageName))
            return false;

        var currentPage = _currentEntry?.PageName ?? string.Empty;

        // Check all navigation guards
        foreach (var guard in _navigationGuards.ToList())
        {
            var canNavigate = await guard(currentPage, pageName);
            if (!canNavigate)
            {
                return false; // Navigation cancelled
            }
        }

        // Guards passed, proceed with navigation
        NavigateTo(pageName, parameter);
        return true;
    }

    /// <inheritdoc />
    public void RegisterNavigationGuard(NavigationGuardCallback guard)
    {
        if (!_navigationGuards.Contains(guard))
        {
            _navigationGuards.Add(guard);
        }
    }

    /// <summary>
    /// Re-creates the current page to refresh its content from CompanyData.
    /// </summary>
    public void RefreshCurrentPage()
    {
        if (_currentEntry == null)
            return;

        if (_pageFactories.TryGetValue(_currentEntry.PageName, out var factory))
        {
            var page = factory(_currentEntry.Parameter);
            _navigationCallback?.Invoke(page);
        }
    }

    /// <summary>
    /// Performs the actual navigation by invoking the factory and callback.
    /// </summary>
    private void PerformNavigation(string? previousPageName)
    {
        if (_currentEntry == null)
            return;

        // Create the page using the registered factory
        if (_pageFactories.TryGetValue(_currentEntry.PageName, out var factory))
        {
            var page = factory(_currentEntry.Parameter);
            _navigationCallback?.Invoke(page);
        }

        // Raise navigation event
        Navigated?.Invoke(this, new NavigationEventArgs(
            _currentEntry.PageName,
            previousPageName,
            _currentEntry.Parameter));
    }
}

/// <summary>
/// Represents the current navigation entry.
/// </summary>
internal class NavigationEntry(string pageName, object? parameter = null)
{
    public string PageName { get; } = pageName;
    public object? Parameter { get; } = parameter;
}
