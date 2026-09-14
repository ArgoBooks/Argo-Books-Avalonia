using System.Collections.ObjectModel;
using System.Windows.Input;
using ArgoBooks.Controls;
using ArgoBooks.Services;
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
    }

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

    #endregion

    #region Column Menu

    [ObservableProperty]
    private bool _isColumnMenuOpen;

    [RelayCommand]
    private void ToggleColumnMenu() => IsColumnMenuOpen = !IsColumnMenuOpen;

    [RelayCommand]
    private void CloseColumnMenu() => IsColumnMenuOpen = false;

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
