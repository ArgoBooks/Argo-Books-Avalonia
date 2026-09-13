using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Shared.Telemetry;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.Controls.ArgoTable;

/// <summary>
/// A reusable table control with title bar, search, filter, pagination, and content slots.
/// Provides the table "shell" while allowing pages to define custom header and row content.
/// </summary>
public partial class ArgoTable : UserControl, INotifyPropertyChanged
{
    #region Styled Properties

    // Content Slots
    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<ArgoTable, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<object?> RowsContentProperty =
        AvaloniaProperty.Register<ArgoTable, object?>(nameof(RowsContent));

    /// <summary>
    /// A slot in the header's middle column, right-aligned so it reads as belonging beside the
    /// search box rather than trailing the title.
    ///
    /// For a status a page wants visible without it becoming a control: a count, an allowance, a
    /// last-synced time.
    /// </summary>
    public static readonly StyledProperty<object?> HeaderInfoContentProperty =
        AvaloniaProperty.Register<ArgoTable, object?>(nameof(HeaderInfoContent));

    public static readonly StyledProperty<bool> ShowColumnHeadersProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(ShowColumnHeaders), true);

    public static readonly StyledProperty<object?> InfoBannerContentProperty =
        AvaloniaProperty.Register<ArgoTable, object?>(nameof(InfoBannerContent));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<ArgoTable, IEnumerable?>(nameof(ItemsSource));

    // Title Bar
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ArgoTable, string?>(nameof(Title));

    public static readonly StyledProperty<bool> ShowHeaderProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(ShowHeader), true);

    public static readonly StyledProperty<bool> ShowHeaderBorderProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(ShowHeaderBorder), true);

    public static readonly StyledProperty<Thickness> HeaderPaddingProperty =
        AvaloniaProperty.Register<ArgoTable, Thickness>(nameof(HeaderPadding), new Thickness(24, 16));

    public static readonly StyledProperty<double> HeaderSpacingProperty =
        AvaloniaProperty.Register<ArgoTable, double>(nameof(HeaderSpacing), 12);

    // Responsive Mode
    public static readonly StyledProperty<bool> IsCompactModeProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(IsCompactMode));

    public static readonly StyledProperty<bool> IsMediumModeProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(IsMediumMode));

    public static readonly StyledProperty<bool> ShowButtonTextProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(ShowButtonText), true);

    // Search
    public static readonly StyledProperty<bool> ShowSearchProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(ShowSearch), true);

    public static readonly StyledProperty<string?> SearchQueryProperty =
        AvaloniaProperty.Register<ArgoTable, string?>(nameof(SearchQuery));

    public static readonly StyledProperty<string> SearchPlaceholderProperty =
        AvaloniaProperty.Register<ArgoTable, string>(nameof(SearchPlaceholder), "Search...");

    public static readonly StyledProperty<double> SearchBoxWidthProperty =
        AvaloniaProperty.Register<ArgoTable, double>(nameof(SearchBoxWidth), 200);

    public static readonly StyledProperty<double> SearchBoxMinHeightProperty =
        AvaloniaProperty.Register<ArgoTable, double>(nameof(SearchBoxMinHeight), 36);

    public static readonly StyledProperty<Thickness> SearchIconMarginProperty =
        AvaloniaProperty.Register<ArgoTable, Thickness>(nameof(SearchIconMargin), new Thickness(12, 0, 8, 0));

    public static readonly StyledProperty<bool> ShowFilterButtonProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(ShowFilterButton), true);

    public static readonly StyledProperty<ICommand?> FilterCommandProperty =
        AvaloniaProperty.Register<ArgoTable, ICommand?>(nameof(FilterCommand));

    // Extra Buttons Content (slot for additional buttons between Filter and Add)
    public static readonly StyledProperty<object?> ExtraButtonsContentProperty =
        AvaloniaProperty.Register<ArgoTable, object?>(nameof(ExtraButtonsContent));

    public static readonly StyledProperty<bool> ShowExtraButtonsProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(ShowExtraButtons), true);

    public static readonly StyledProperty<bool> ShowAddButtonProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(ShowAddButton), true);

    public static readonly StyledProperty<bool> ShowEmptyAddButtonProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(ShowEmptyAddButton), true);

    public static readonly StyledProperty<bool> IsAddButtonEnabledProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(IsAddButtonEnabled), true);

    public static readonly StyledProperty<ICommand?> AddCommandProperty =
        AvaloniaProperty.Register<ArgoTable, ICommand?>(nameof(AddCommand));

    public static readonly StyledProperty<string> AddButtonTextProperty =
        AvaloniaProperty.Register<ArgoTable, string>(nameof(AddButtonText), "Add");

    public static readonly StyledProperty<string> AddButtonTooltipProperty =
        AvaloniaProperty.Register<ArgoTable, string>(nameof(AddButtonTooltip), "Add");

    // Pagination
    public static readonly StyledProperty<bool> ShowPaginationProperty =
        AvaloniaProperty.Register<ArgoTable, bool>(nameof(ShowPagination), true);

    public static readonly StyledProperty<int> CurrentPageProperty =
        AvaloniaProperty.Register<ArgoTable, int>(nameof(CurrentPage), 1);

    public static readonly StyledProperty<int> TotalPagesProperty =
        AvaloniaProperty.Register<ArgoTable, int>(nameof(TotalPages), 1);

    public static readonly StyledProperty<int> PageSizeProperty =
        AvaloniaProperty.Register<ArgoTable, int>(nameof(PageSize), 10);

    public static readonly StyledProperty<string> PaginationTextProperty =
        AvaloniaProperty.Register<ArgoTable, string>(nameof(PaginationText), "0 items");

    public static readonly StyledProperty<ICommand?> GoToPageCommandProperty =
        AvaloniaProperty.Register<ArgoTable, ICommand?>(nameof(GoToPageCommand));

    public static readonly StyledProperty<ICommand?> GoToPreviousPageCommandProperty =
        AvaloniaProperty.Register<ArgoTable, ICommand?>(nameof(GoToPreviousPageCommand));

    public static readonly StyledProperty<ICommand?> GoToNextPageCommandProperty =
        AvaloniaProperty.Register<ArgoTable, ICommand?>(nameof(GoToNextPageCommand));

    // Empty State
    public static readonly StyledProperty<string> EmptyIconProperty =
        AvaloniaProperty.Register<ArgoTable, string>(nameof(EmptyIcon), Icons.Customers);

    public static readonly StyledProperty<string> EmptyTitleProperty =
        AvaloniaProperty.Register<ArgoTable, string>(nameof(EmptyTitle), "No data found");

    public static readonly StyledProperty<string> EmptyMessageProperty =
        AvaloniaProperty.Register<ArgoTable, string>(nameof(EmptyMessage), "Add your first item to get started.");

    public static readonly StyledProperty<ITableColumnWidths?> ColumnWidthsManagerProperty =
        AvaloniaProperty.Register<ArgoTable, ITableColumnWidths?>(nameof(ColumnWidthsManager));

    public static readonly StyledProperty<ICommand?> ToggleColumnMenuCommandProperty =
        AvaloniaProperty.Register<ArgoTable, ICommand?>(nameof(ToggleColumnMenuCommand));

    #endregion

    #region Properties

    /// <summary>
    /// Content slot for the table column headers.
    /// </summary>
    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    /// <summary>Content slot beside the search box, for a status the page wants on show.</summary>
    public object? HeaderInfoContent
    {
        get => GetValue(HeaderInfoContentProperty);
        set => SetValue(HeaderInfoContentProperty, value);
    }

    /// <summary>
    /// Content slot for the table rows.
    /// </summary>
    public object? RowsContent
    {
        get => GetValue(RowsContentProperty);
        set => SetValue(RowsContentProperty, value);
    }

    /// <summary>
    /// Whether the column-header row is shown. Set false when the body is a non-tabular view
    /// (e.g. a calendar) so the empty gray header bar doesn't appear.
    /// </summary>
    public bool ShowColumnHeaders
    {
        get => GetValue(ShowColumnHeadersProperty);
        set => SetValue(ShowColumnHeadersProperty, value);
    }

    /// <summary>
    /// Optional info/warning banner displayed between the header and the table content.
    /// </summary>
    public object? InfoBannerContent
    {
        get => GetValue(InfoBannerContentProperty);
        set => SetValue(InfoBannerContentProperty, value);
    }

    /// <summary>
    /// Items source for empty state detection.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool ShowHeader
    {
        get => GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    public bool ShowHeaderBorder
    {
        get => GetValue(ShowHeaderBorderProperty);
        set => SetValue(ShowHeaderBorderProperty, value);
    }

    public Thickness HeaderPadding
    {
        get => GetValue(HeaderPaddingProperty);
        set => SetValue(HeaderPaddingProperty, value);
    }

    public double HeaderSpacing
    {
        get => GetValue(HeaderSpacingProperty);
        set => SetValue(HeaderSpacingProperty, value);
    }

    public bool IsCompactMode
    {
        get => GetValue(IsCompactModeProperty);
        set => SetValue(IsCompactModeProperty, value);
    }

    public bool IsMediumMode
    {
        get => GetValue(IsMediumModeProperty);
        set => SetValue(IsMediumModeProperty, value);
    }

    public bool ShowButtonText
    {
        get => GetValue(ShowButtonTextProperty);
        set => SetValue(ShowButtonTextProperty, value);
    }

    public bool ShowSearch
    {
        get => GetValue(ShowSearchProperty);
        set => SetValue(ShowSearchProperty, value);
    }

    public string? SearchQuery
    {
        get => GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    public string SearchPlaceholder
    {
        get => GetValue(SearchPlaceholderProperty);
        set => SetValue(SearchPlaceholderProperty, value);
    }

    public double SearchBoxWidth
    {
        get => GetValue(SearchBoxWidthProperty);
        set => SetValue(SearchBoxWidthProperty, value);
    }

    public double SearchBoxMinHeight
    {
        get => GetValue(SearchBoxMinHeightProperty);
        set => SetValue(SearchBoxMinHeightProperty, value);
    }

    public Thickness SearchIconMargin
    {
        get => GetValue(SearchIconMarginProperty);
        set => SetValue(SearchIconMarginProperty, value);
    }

    public bool ShowFilterButton
    {
        get => GetValue(ShowFilterButtonProperty);
        set => SetValue(ShowFilterButtonProperty, value);
    }

    public ICommand? FilterCommand
    {
        get => GetValue(FilterCommandProperty);
        set => SetValue(FilterCommandProperty, value);
    }

    /// <summary>
    /// Content slot for extra buttons between Filter and Add buttons.
    /// </summary>
    public object? ExtraButtonsContent
    {
        get => GetValue(ExtraButtonsContentProperty);
        set => SetValue(ExtraButtonsContentProperty, value);
    }

    public bool ShowExtraButtons
    {
        get => GetValue(ShowExtraButtonsProperty);
        set => SetValue(ShowExtraButtonsProperty, value);
    }

    public bool ShowAddButton
    {
        get => GetValue(ShowAddButtonProperty);
        set => SetValue(ShowAddButtonProperty, value);
    }

    /// <summary>
    /// Whether the empty state offers the add button as well. Turn it off where an empty table means a
    /// filter matched nothing rather than that nothing has been added yet.
    /// </summary>
    public bool ShowEmptyAddButton
    {
        get => GetValue(ShowEmptyAddButtonProperty);
        set => SetValue(ShowEmptyAddButtonProperty, value);
    }

    public bool IsAddButtonEnabled
    {
        get => GetValue(IsAddButtonEnabledProperty);
        set => SetValue(IsAddButtonEnabledProperty, value);
    }

    public ICommand? AddCommand
    {
        get => GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public string AddButtonText
    {
        get => GetValue(AddButtonTextProperty);
        set => SetValue(AddButtonTextProperty, value);
    }

    public string AddButtonTooltip
    {
        get => GetValue(AddButtonTooltipProperty);
        set => SetValue(AddButtonTooltipProperty, value);
    }

    public bool ShowPagination
    {
        get => GetValue(ShowPaginationProperty);
        set => SetValue(ShowPaginationProperty, value);
    }

    public int CurrentPage
    {
        get => GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public int TotalPages
    {
        get => GetValue(TotalPagesProperty);
        set => SetValue(TotalPagesProperty, value);
    }

    public int PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public string PaginationText
    {
        get => GetValue(PaginationTextProperty);
        set => SetValue(PaginationTextProperty, value);
    }

    public ICommand? GoToPageCommand
    {
        get => GetValue(GoToPageCommandProperty);
        set => SetValue(GoToPageCommandProperty, value);
    }

    public ICommand? GoToPreviousPageCommand
    {
        get => GetValue(GoToPreviousPageCommandProperty);
        set => SetValue(GoToPreviousPageCommandProperty, value);
    }

    public ICommand? GoToNextPageCommand
    {
        get => GetValue(GoToNextPageCommandProperty);
        set => SetValue(GoToNextPageCommandProperty, value);
    }

    public string EmptyIcon
    {
        get => GetValue(EmptyIconProperty);
        set => SetValue(EmptyIconProperty, value);
    }

    public string EmptyTitle
    {
        get => GetValue(EmptyTitleProperty);
        set => SetValue(EmptyTitleProperty, value);
    }

    public string EmptyMessage
    {
        get => GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    public ITableColumnWidths? ColumnWidthsManager
    {
        get => GetValue(ColumnWidthsManagerProperty);
        set => SetValue(ColumnWidthsManagerProperty, value);
    }

    public ICommand? ToggleColumnMenuCommand
    {
        get => GetValue(ToggleColumnMenuCommandProperty);
        set => SetValue(ToggleColumnMenuCommandProperty, value);
    }

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether there are no items to display.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            if (ItemsSource == null) return true;
            if (ItemsSource is ICollection collection) return collection.Count == 0;
            var enumerator = ItemsSource.GetEnumerator();
            try
            {
                return !enumerator.MoveNext();
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }
    }

    /// <summary>
    /// Gets whether to show page number buttons.
    /// </summary>
    public bool ShowPageNumbers => TotalPages > 1;

    /// <summary>
    /// Gets whether previous page navigation is enabled.
    /// </summary>
    public bool CanGoToPreviousPage => CurrentPage > 1;

    /// <summary>
    /// Gets whether next page navigation is enabled.
    /// </summary>
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    /// <summary>
    /// Gets the minimum total width for the table based on column widths.
    /// </summary>
    public double MinimumTotalWidth => ColumnWidthsManager?.MinimumTotalWidth ?? 600;

    /// <summary>
    /// Gets whether horizontal scrolling is needed.
    /// </summary>
    public bool NeedsHorizontalScroll => ColumnWidthsManager?.NeedsHorizontalScroll ?? false;

    #endregion

    #region Collections

    /// <summary>
    /// Page size options for the dropdown.
    /// </summary>
    public ObservableCollection<int> PageSizeOptions { get; } = [10, 25, 50, 100];

    /// <summary>
    /// Page numbers for pagination display.
    /// </summary>
    public ObservableCollection<int> PageNumbers { get; } = [];

    #endregion

    #region Commands

    public ICommand ClearSearchCommand { get; }

    #endregion

    #region Events

    /// <summary>
    /// Raised when the table header is right-clicked (for column menu).
    /// </summary>
    public event EventHandler<PointerPressedEventArgs>? HeaderRightClicked;

    /// <summary>
    /// Raised when the border size changes (for responsive header).
    /// </summary>
    public event EventHandler<SizeChangedEventArgs>? BorderSizeChanged;

    /// <summary>
    /// Raised when the table grid size changes (for column width calculation).
    /// </summary>
    public event EventHandler<SizeChangedEventArgs>? TableGridSizeChanged;

    /// <summary>
    /// Raised when a property value changes (for computed properties).
    /// </summary>
    public new event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region INotifyPropertyChanged

    /// <summary>
    /// Raises the PropertyChanged event for a computed property.
    /// </summary>
    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion

    public ArgoTable()
    {
        ClearSearchCommand = new RelayCommand(() => SearchQuery = null);
        InitializeComponent();
    }

    /// <summary>
    /// Pages already reported this session. A list empties and refills many times in one visit
    /// (loading, paging, deleting the last row); the signal is that it was seen, not how often.
    /// </summary>
    private static readonly HashSet<string> ReportedEmptyPages = new(StringComparer.Ordinal);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ReportEmptyStateIfNeeded();
    }

    private void ReportEmptyStateIfNeeded()
    {
        // No matches is not the same as no data.
        if (!IsEmpty || !string.IsNullOrWhiteSpace(SearchQuery))
            return;

        var page = App.NavigationService?.CurrentPageName;
        if (string.IsNullOrWhiteSpace(page) || !ReportedEmptyPages.Add(page))
            return;

        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.EmptyStateShown, page);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            RaisePropertyChanged(nameof(IsEmpty));
            ReportEmptyStateIfNeeded();

            if (change.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= OnItemsCollectionChanged;
            if (change.NewValue is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += OnItemsCollectionChanged;
        }
        else if (change.Property == TotalPagesProperty)
        {
            RaisePropertyChanged(nameof(ShowPageNumbers));
            RaisePropertyChanged(nameof(CanGoToPreviousPage));
            RaisePropertyChanged(nameof(CanGoToNextPage));
            UpdatePageNumbers();
        }
        else if (change.Property == CurrentPageProperty)
        {
            RaisePropertyChanged(nameof(CanGoToPreviousPage));
            RaisePropertyChanged(nameof(CanGoToNextPage));
        }
        else if (change.Property == ColumnWidthsManagerProperty)
        {
            RaisePropertyChanged(nameof(MinimumTotalWidth));
            RaisePropertyChanged(nameof(NeedsHorizontalScroll));

            if (change.OldValue is INotifyPropertyChanged oldNotify)
                oldNotify.PropertyChanged -= OnColumnWidthsPropertyChanged;
            if (change.NewValue is INotifyPropertyChanged newNotify)
                newNotify.PropertyChanged += OnColumnWidthsPropertyChanged;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(IsEmpty));
        ReportEmptyStateIfNeeded();
    }

    private void OnColumnWidthsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableColumnWidthsBase.MinimumTotalWidth))
            RaisePropertyChanged(nameof(MinimumTotalWidth));
        else if (e.PropertyName == nameof(TableColumnWidthsBase.NeedsHorizontalScroll))
            RaisePropertyChanged(nameof(NeedsHorizontalScroll));
    }

    private void UpdatePageNumbers()
    {
        PageNumbers.Clear();
        var startPage = Math.Max(1, CurrentPage - 2);
        var endPage = Math.Min(TotalPages, startPage + 4);
        startPage = Math.Max(1, endPage - 4);

        for (var i = startPage; i <= endPage; i++)
        {
            PageNumbers.Add(i);
        }
    }

    private void OnTableHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // Set the menu position on the ColumnWidthsManager
            if (ColumnWidthsManager != null)
            {
                // Find the containing Page (UserControl) - skip over ArgoTable itself
                // This ensures coordinates are relative to the page that contains both
                // the ArgoTable and the ColumnVisibilityMenu
                Control? ancestor = Parent as Control;
                while (ancestor != null)
                {
                    if (ancestor is UserControl && ancestor != this)
                    {
                        break;
                    }
                    ancestor = ancestor.Parent as Control;
                }

                // Fall back to Panel if no UserControl found
                if (ancestor == null)
                {
                    ancestor = Parent as Control;
                    while (ancestor != null && ancestor is not Panel)
                    {
                        ancestor = ancestor.Parent as Control;
                    }
                }

                if (ancestor != null)
                {
                    var position = e.GetPosition(ancestor);
                    ColumnWidthsManager.ColumnMenuX = position.X;
                    ColumnWidthsManager.ColumnMenuY = position.Y;
                }
            }

            HeaderRightClicked?.Invoke(this, e);
            ToggleColumnMenuCommand?.Execute(null);
        }
    }

    private void OnBorderSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        BorderSizeChanged?.Invoke(this, e);
    }

    private void OnTableGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            ColumnWidthsManager?.SetAvailableWidth(e.NewSize.Width);
        }
        TableGridSizeChanged?.Invoke(this, e);
    }
}
