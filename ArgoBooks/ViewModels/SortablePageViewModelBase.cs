using System.ComponentModel;
using System.Windows.Input;
using ArgoBooks.Controls;
using ArgoBooks.Core.Services;
using ArgoBooks.Helpers;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Base class for page ViewModels that support sorting and pagination.
/// Provides common sorting, pagination properties and navigation commands.
/// </summary>
public abstract partial class SortablePageViewModelBase : ViewModelBase, ICleanupViewModel
{
    protected SortablePageViewModelBase()
    {
        // Field initializers (including loaded column visibility) have already run by the time
        // this base constructor executes, so push that visibility into the column-width manager.
        Helpers.ColumnVisibilityHelper.SyncToManager(this);

        // Subscribe to language changes to refresh translated content
        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Called when the language changes. Refreshes the page to update translations.
    /// Override in derived classes to refresh ComboBox options or other translated content.
    /// </summary>
    protected virtual void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        // Refresh the page data to update translated badge text
        OnSortOrPageChanged();
    }

    /// <summary>
    /// Unsubscribes from language change events. Call this when the ViewModel is disposed.
    /// </summary>
    public virtual void Cleanup()
    {
        LanguageService.Instance.LanguageChanged -= OnLanguageChanged;
        CancelPendingSearch();

        if (_deferredReload != null)
        {
            App.UndoRedoManager.StateChanged -= OnUndoRedoStateChangedDeferred;
            if (App.NavigationService != null)
                App.NavigationService.Navigated -= OnNavigatedDeferred;
        }
    }

    #region Deferred Undo/Redo Refresh

    private Func<string?, bool>? _isThisPage;
    private Action? _deferredReload;
    private Action? _beforeDeferredCheck;
    private Action? _onNavigatedHere;
    private bool _needsRefresh;

    /// <summary>
    /// Reloads the page after an undo or redo. While the page is not showing, the reload waits
    /// until the user navigates back to it. Unsubscribed by <see cref="Cleanup"/>.
    /// </summary>
    /// <param name="isThisPage">Whether a navigation page name refers to this page.</param>
    /// <param name="reload">Reloads the page's data.</param>
    /// <param name="beforeCheck">Runs on every undo/redo, before deciding whether to reload now.</param>
    /// <param name="onNavigatedHere">Runs on every navigation to this page, after any pending reload.</param>
    protected void EnableDeferredUndoRefresh(
        Func<string?, bool> isThisPage,
        Action reload,
        Action? beforeCheck = null,
        Action? onNavigatedHere = null)
    {
        _isThisPage = isThisPage;
        _deferredReload = reload;
        _beforeDeferredCheck = beforeCheck;
        _onNavigatedHere = onNavigatedHere;

        App.UndoRedoManager.StateChanged += OnUndoRedoStateChangedDeferred;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigatedDeferred;
    }

    private void OnUndoRedoStateChangedDeferred(object? sender, EventArgs e)
    {
        _beforeDeferredCheck?.Invoke();

        if (!_isThisPage!(App.NavigationService?.CurrentPageName))
        {
            _needsRefresh = true;
            return;
        }
        _deferredReload!();
    }

    private void OnNavigatedDeferred(object? sender, NavigationEventArgs e)
    {
        if (!_isThisPage!(e.PageName))
            return;

        if (_needsRefresh)
        {
            _needsRefresh = false;
            _deferredReload!();
        }
        _onNavigatedHere?.Invoke();
    }

    #endregion

    #region Sorting

    [ObservableProperty]
    private string _sortColumn = "Name";

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.None;

    private RelayCommand<string>? _sortByCommand;

    /// <summary>
    /// Command to sort by a column. Pass the column name as parameter.
    /// </summary>
    public ICommand SortByCommand => _sortByCommand ??= new RelayCommand<string>(SortBy);

    /// <summary>
    /// Sorts by the specified column. Toggles direction if same column, otherwise starts ascending.
    /// </summary>
    private void SortBy(string? column)
    {
        if (string.IsNullOrEmpty(column))
            return;

        if (SortColumn == column)
        {
            SortDirection = SortDirection switch
            {
                SortDirection.None => SortDirection.Ascending,
                SortDirection.Ascending => SortDirection.Descending,
                SortDirection.Descending => SortDirection.None,
                _ => SortDirection.Ascending
            };
        }
        else
        {
            SortColumn = column;
            SortDirection = SortDirection.Ascending;
        }

        OnSortOrPageChanged();
    }

    #endregion

    #region Pagination

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private int _pageSize = 10;

    /// <summary>
    /// Gets whether we can navigate to the previous page.
    /// </summary>
    public bool CanGoToPreviousPage => CurrentPage > 1;

    /// <summary>
    /// Gets whether we can navigate to the next page.
    /// </summary>
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    partial void OnCurrentPageChanged(int value)
    {
        NotifyPaginationChanged();
        OnSortOrPageChanged();
    }

    partial void OnPageSizeChanged(int value)
    {
        CurrentPage = 1;
        NotifyPaginationChanged();
        OnSortOrPageChanged();
    }

    /// <summary>
    /// Navigates to the previous page.
    /// </summary>
    [RelayCommand]
    private void GoToPreviousPage()
    {
        if (CanGoToPreviousPage)
            CurrentPage--;
    }

    /// <summary>
    /// Navigates to the next page.
    /// </summary>
    [RelayCommand]
    private void GoToNextPage()
    {
        if (CanGoToNextPage)
            CurrentPage++;
    }

    /// <summary>
    /// Navigates to a specific page.
    /// </summary>
    [RelayCommand]
    private void GoToPage(int page)
    {
        if (page >= 1 && page <= TotalPages && page != CurrentPage)
            CurrentPage = page;
    }

    /// <summary>
    /// Notifies that pagination-related properties have changed.
    /// </summary>
    protected void NotifyPaginationChanged()
    {
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
    }

    [ObservableProperty]
    private string _paginationText = string.Empty;

    /// <summary>
    /// Updates <see cref="TotalPages"/>, keeps <see cref="CurrentPage"/> in range, sets
    /// <see cref="PaginationText"/> and returns the current page's slice of the items.
    /// </summary>
    protected List<T> Paginate<T>(IReadOnlyCollection<T> items, string singular, string? plural = null)
    {
        TotalPages = PaginationMath.TotalPages(items.Count, PageSize);
        CurrentPage = PaginationMath.ClampPage(CurrentPage, TotalPages);

        PaginationText = PaginationTextHelper.FormatPaginationText(
            items.Count, CurrentPage, PageSize, TotalPages, singular, plural);
        NotifyPaginationChanged();

        return PaginationMath.Slice(items, CurrentPage, PageSize).ToList();
    }

    #endregion

    #region Column Menu and Visibility

    [ObservableProperty]
    private bool _isColumnMenuOpen;

    [RelayCommand]
    private void ToggleColumnMenu() => IsColumnMenuOpen = !IsColumnMenuOpen;

    [RelayCommand]
    private void CloseColumnMenu() => IsColumnMenuOpen = false;

    /// <summary>
    /// The page's column visibility settings key and defaults. Pages with a column menu override
    /// this; their <c>Show{Column}Column</c> changes are then applied and saved automatically.
    /// </summary>
    protected virtual ColumnVisibilityDefaults? ColumnVisibility => null;

    private bool _isResettingColumns;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (ColumnVisibilityHelper.GetColumnKey(e.PropertyName) is { } column &&
            ColumnVisibility is { } columns &&
            columns.Defaults.ContainsKey(column) &&
            GetType().GetProperty(e.PropertyName!)?.GetValue(this) is bool isVisible)
        {
            ColumnVisibilityHelper.GetManager(this)?.SetColumnVisibility(column, isVisible);

            // A reset clears the page's saved overrides, so writing the defaults back is pointless.
            if (!_isResettingColumns)
                ColumnVisibilityHelper.Save(columns.PageName, column, isVisible);
        }

        base.OnPropertyChanged(e);
    }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        if (ColumnVisibility is not { } columns)
            return;

        ColumnVisibilityHelper.GetManager(this)?.ResetWidths();
        ColumnVisibilityHelper.ResetPage(columns.PageName);

        _isResettingColumns = true;
        try
        {
            ColumnVisibilityHelper.ApplyDefaults(this, columns);
        }
        finally
        {
            _isResettingColumns = false;
        }
    }

    #endregion

    #region Transaction Highlighting

    /// <summary>
    /// Transaction ID to highlight when navigating from dashboard.
    /// Set this before calling ApplyHighlight().
    /// </summary>
    public string? HighlightTransactionId { get; set; }

    /// <summary>
    /// Refreshes the display to apply highlighting after HighlightTransactionId is set.
    /// </summary>
    public void ApplyHighlight() => OnSortOrPageChanged();

    /// <summary>
    /// Calculates the page number for a highlighted item and navigates to it.
    /// Call this after sorting but before pagination in your filter method.
    /// </summary>
    /// <typeparam name="T">The type of display item.</typeparam>
    /// <param name="items">The sorted list of items.</param>
    /// <param name="idSelector">Function to get the ID from an item.</param>
    protected void NavigateToHighlightedItem<T>(List<T> items, Func<T, string?> idSelector)
    {
        if (string.IsNullOrEmpty(HighlightTransactionId))
            return;

        var highlightIndex = items.FindIndex(x => idSelector(x) == HighlightTransactionId);
        if (highlightIndex >= 0)
        {
            CurrentPage = (highlightIndex / PageSize) + 1;
        }

        // Clear highlight ID after first use
        HighlightTransactionId = null;
    }

    #endregion

    /// <summary>
    /// Called when sorting or pagination changes. Override to refresh data.
    /// </summary>
    protected abstract void OnSortOrPageChanged();
}
