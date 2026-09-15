using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using System.Collections.Concurrent;
using System.Reflection;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.Controls;

/// <summary>
/// A searchable dropdown control with filtering, keyboard navigation, and "Add new" support.
/// </summary>
public partial class SearchableDropdown : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    private TextBox? _searchTextBox;
    private ScrollViewer? _itemsScrollViewer;
    private int _highlightedIndex = -1;
    private bool _isSettingFromSelectedItem;

    // Opened on the picked item's own text: list everything until the user types.
    private bool _showAll;

    // Exact, prefix, word-start or contains, but not a fuzzy guess.
    private const double StrongMatchScore = 0.8;

    // Debounce typing so the fuzzy-scoring filter over the whole source list doesn't run per keystroke.
    private const int SearchDebounceMs = 120;
    private CancellationTokenSource? _searchDebounceCts;

    // Cache the reflected DisplayMemberPath property per (item type, path) so the filter doesn't call
    // GetProperty for every item on every keystroke.
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> DisplayPropertyCache = new();

    #region Styled Properties

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchableDropdown, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SearchableDropdown, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(SearchText), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> DisplayMemberPathProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(DisplayMemberPath));

    public static readonly StyledProperty<string?> SelectedDisplayMemberPathProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(SelectedDisplayMemberPath));

    public static readonly StyledProperty<string?> SearchMemberPathProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(SearchMemberPath));

    public static readonly StyledProperty<string?> ImageMemberPathProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(ImageMemberPath));

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(Label));

    public static readonly StyledProperty<string?> PlaceholderProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(Placeholder), "Search...");

    public static readonly StyledProperty<string?> HelperTextProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(HelperText));

    public static readonly StyledProperty<string?> ErrorMessageProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(ErrorMessage));

    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<SearchableDropdown, bool>(nameof(HasError));

    public static readonly StyledProperty<bool> IsRequiredProperty =
        AvaloniaProperty.Register<SearchableDropdown, bool>(nameof(IsRequired));

    public static readonly StyledProperty<bool> IsDropdownOpenProperty =
        AvaloniaProperty.Register<SearchableDropdown, bool>(nameof(IsDropdownOpen));

    public static readonly StyledProperty<bool> ShowAddNewProperty =
        AvaloniaProperty.Register<SearchableDropdown, bool>(nameof(ShowAddNew), defaultValue: false);

    public static readonly StyledProperty<string> AddNewTextProperty =
        AvaloniaProperty.Register<SearchableDropdown, string>(nameof(AddNewText), "Add new...");

    public static readonly StyledProperty<ICommand?> AddNewCommandProperty =
        AvaloniaProperty.Register<SearchableDropdown, ICommand?>(nameof(AddNewCommand));

    public static readonly StyledProperty<string?> EmptyMessageProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(EmptyMessage));

    public static readonly StyledProperty<string?> EmptyCreateLinkTextProperty =
        AvaloniaProperty.Register<SearchableDropdown, string?>(nameof(EmptyCreateLinkText), "Create one here");

    public static readonly StyledProperty<ICommand?> EmptyCreateCommandProperty =
        AvaloniaProperty.Register<SearchableDropdown, ICommand?>(nameof(EmptyCreateCommand));

    public static readonly StyledProperty<object?> AddNewCommandParameterProperty =
        AvaloniaProperty.Register<SearchableDropdown, object?>(nameof(AddNewCommandParameter));

    public static readonly StyledProperty<object?> EmptyCreateCommandParameterProperty =
        AvaloniaProperty.Register<SearchableDropdown, object?>(nameof(EmptyCreateCommandParameter));

    public static readonly StyledProperty<IEnumerable?> PriorityItemsProperty =
        AvaloniaProperty.Register<SearchableDropdown, IEnumerable?>(nameof(PriorityItems));

    public static readonly StyledProperty<bool> EnterSelectsBestMatchProperty =
        AvaloniaProperty.Register<SearchableDropdown, bool>(nameof(EnterSelectsBestMatch));

    #endregion

    #region Properties

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the search/filter text.
    /// </summary>
    public string? SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the property path to display.
    /// </summary>
    public string? DisplayMemberPath
    {
        get => GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>
    /// Gets or sets the property shown in the closed box once an item is picked, such as a short code where
    /// the list shows a longer name. Falls back to <see cref="DisplayMemberPath"/>.
    /// </summary>
    public string? SelectedDisplayMemberPath
    {
        get => GetValue(SelectedDisplayMemberPathProperty);
        set => SetValue(SelectedDisplayMemberPathProperty, value);
    }

    /// <summary>
    /// Gets or sets a second property that search also matches, such as an ID that is not displayed.
    /// </summary>
    public string? SearchMemberPath
    {
        get => GetValue(SearchMemberPathProperty);
        set => SetValue(SearchMemberPathProperty, value);
    }

    /// <summary>
    /// Gets or sets the property holding each item's image, such as a flag, shown before its text.
    /// </summary>
    public string? ImageMemberPath
    {
        get => GetValue(ImageMemberPathProperty);
        set => SetValue(ImageMemberPathProperty, value);
    }

    /// <summary>The selected item's image, shown in the closed box.</summary>
    public IImage? SelectedItemImage =>
        SelectedItem is { } item ? GetMemberValue(item, ImageMemberPath) as IImage : null;

    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text.
    /// </summary>
    public string? Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string? HelperText
    {
        get => GetValue(HelperTextProperty);
        set => SetValue(HelperTextProperty, value);
    }

    public string? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the control has a validation error.
    /// </summary>
    public bool HasError
    {
        get => GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the field is required.
    /// </summary>
    public bool IsRequired
    {
        get => GetValue(IsRequiredProperty);
        set => SetValue(IsRequiredProperty, value);
    }

    public bool IsDropdownOpen
    {
        get => GetValue(IsDropdownOpenProperty);
        set => SetValue(IsDropdownOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the "Add new" button.
    /// </summary>
    public bool ShowAddNew
    {
        get => GetValue(ShowAddNewProperty);
        set => SetValue(ShowAddNewProperty, value);
    }

    /// <summary>
    /// Gets or sets the "Add new" button text.
    /// </summary>
    public string AddNewText
    {
        get => GetValue(AddNewTextProperty);
        set => SetValue(AddNewTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the message shown when no items exist (e.g., "No customers exist.").
    /// </summary>
    public string? EmptyMessage
    {
        get => GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    /// <summary>
    /// Gets or sets the create link text shown when no items exist (e.g., "Create one here").
    /// </summary>
    public string? EmptyCreateLinkText
    {
        get => GetValue(EmptyCreateLinkTextProperty);
        set => SetValue(EmptyCreateLinkTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the command executed when the create link is clicked.
    /// </summary>
    public ICommand? EmptyCreateCommand
    {
        get => GetValue(EmptyCreateCommandProperty);
        set => SetValue(EmptyCreateCommandProperty, value);
    }

    /// <summary>
    /// Optional parameter passed to <see cref="AddNewCommand"/>. When set (e.g. the owning
    /// line-item object in a repeated row), it is forwarded instead of the typed search text so
    /// the consumer can auto-select the newly created entity into the row that launched the create.
    /// </summary>
    public object? AddNewCommandParameter
    {
        get => GetValue(AddNewCommandParameterProperty);
        set => SetValue(AddNewCommandParameterProperty, value);
    }

    /// <summary>
    /// Optional parameter passed to <see cref="EmptyCreateCommand"/> (the empty-state "create"
    /// link). Mirrors <see cref="AddNewCommandParameter"/> for the no-items case.
    /// </summary>
    public object? EmptyCreateCommandParameter
    {
        get => GetValue(EmptyCreateCommandParameterProperty);
        set => SetValue(EmptyCreateCommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets the priority items shown at the top of the dropdown.
    /// </summary>
    public IEnumerable? PriorityItems
    {
        get => GetValue(PriorityItemsProperty);
        set => SetValue(PriorityItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether Enter with nothing highlighted picks the top item when it clearly matches the typed
    /// text. For pure pickers only: a box that also takes new free-text names would swap them for an existing item.
    /// </summary>
    public bool EnterSelectsBestMatch
    {
        get => GetValue(EnterSelectsBestMatchProperty);
        set => SetValue(EnterSelectsBestMatchProperty, value);
    }

    /// <summary>
    /// Gets the filtered items based on search text.
    /// </summary>
    public ObservableCollection<object> FilteredItems { get; } = [];

    /// <summary>
    /// Gets the filtered priority items based on search text.
    /// </summary>
    public ObservableCollection<object> FilteredPriorityItems { get; } = [];

    /// <summary>
    /// Gets whether there are filtered priority items to display.
    /// </summary>
    public bool HasFilteredPriorityItems => FilteredPriorityItems.Count > 0;

    /// <summary>
    /// Gets whether there are filtered items to display.
    /// </summary>
    public bool HasFilteredItems => FilteredItems.Count > 0;

    /// <summary>
    /// Gets whether there are any filtered items (priority or regular) to display.
    /// </summary>
    public bool HasAnyFilteredItems => FilteredItems.Count > 0 || FilteredPriorityItems.Count > 0;

    /// <summary>
    /// Gets whether to show the separator between priority and regular items.
    /// </summary>
    public bool ShowPrioritySeparator => HasFilteredPriorityItems && HasFilteredItems;

    public bool HasItems => ItemsSource?.Cast<object>().Any() == true;

    /// <summary>
    /// Gets whether to show the empty create link (no items and command is set).
    /// </summary>
    public bool ShowEmptyCreate => !HasItems && EmptyCreateCommand != null && string.IsNullOrWhiteSpace(SearchText);

    /// <summary>
    /// Gets the currently highlighted item for keyboard navigation.
    /// </summary>
    public object? HighlightedItem
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                RaisePropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the total count of all filtered items (priority + regular).
    /// </summary>
    private int TotalFilteredCount => FilteredPriorityItems.Count + FilteredItems.Count;

    /// <summary>
    /// Gets an item by combined index (priority items first, then regular items).
    /// </summary>
    private object? GetItemByIndex(int index)
    {
        if (index < 0)
            return null;

        if (index < FilteredPriorityItems.Count)
            return FilteredPriorityItems[index];

        var regularIndex = index - FilteredPriorityItems.Count;
        if (regularIndex < FilteredItems.Count)
            return FilteredItems[regularIndex];

        return null;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Command to toggle the dropdown.
    /// </summary>
    public ICommand ToggleDropdownCommand { get; }

    /// <summary>
    /// Command to select an item.
    /// </summary>
    public ICommand SelectItemCommand { get; }

    /// <summary>
    /// Internal command bound to the "Add new" button. Closes the dropdown and
    /// forwards the current <see cref="SearchText"/> to the consumer's
    /// <see cref="AddNewCommand"/> as its parameter.
    /// </summary>
    public ICommand AddNewInternalCommand { get; }

    /// <summary>
    /// Command executed when "Add new" is clicked.
    /// </summary>
    public ICommand? AddNewCommand
    {
        get => GetValue(AddNewCommandProperty);
        set => SetValue(AddNewCommandProperty, value);
    }

    #endregion

    public SearchableDropdown()
    {
        ToggleDropdownCommand = new RelayCommand(ToggleDropdown);
        SelectItemCommand = new RelayCommand<object>(SelectItem);
        AddNewInternalCommand = new RelayCommand(() =>
        {
            // Forward the explicit parameter (e.g. the owning line item) when the consumer set one;
            // otherwise fall back to the typed search text, which some consumers use as the new name.
            var parameter = AddNewCommandParameter ?? SearchText;
            IsDropdownOpen = false;
            AddNewCommand?.Execute(parameter);
            // The consumer command may set SearchText (showing the pending new name),
            // which re-opens the dropdown via OnSearchTextChanged. Close it again.
            IsDropdownOpen = false;
        });

        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            // Unsubscribe from old collection
            if (change.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= OnItemsSourceCollectionChanged;
            }

            // Subscribe to new collection
            if (change.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += OnItemsSourceCollectionChanged;
            }

            UpdateFilteredItems();
        }
        else if (change.Property == SearchTextProperty)
        {
            OnSearchTextChanged();
            // The empty-create link should give way to whatever the field already holds (a typed
            // value or an AI-suggested name), so the value isn't hidden behind "Create one".
            RaisePropertyChanged(nameof(ShowEmptyCreate));
        }
        else if (change.Property == SelectedItemProperty)
        {
            // Sync SearchText when SelectedItem is set programmatically
            OnSelectedItemChanged(change.OldValue, change.NewValue);
        }
        else if (change.Property == SelectedDisplayMemberPathProperty && SelectedItem != null)
        {
            OnSelectedItemChanged(SelectedItem, SelectedItem);
        }
        else if (change.Property == IsDropdownOpenProperty)
        {
            if (change.NewValue is true)
            {
                _showAll = SelectedItem != null && SearchText == GetSelectedText(SelectedItem);
                // Refresh filtered items when dropdown opens to ensure latest data
                UpdateFilteredItems();
                // Don't highlight any item by default - wait for user to press a key
                _highlightedIndex = -1;
                HighlightedItem = null;
            }
            else
            {
                // Reset highlight when dropdown closes
                _highlightedIndex = -1;
                HighlightedItem = null;
            }
        }
    }

    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateFilteredItems();
    }

    private void OnSelectedItemChanged(object? oldValue, object? newValue)
    {
        _isSettingFromSelectedItem = true;
        try
        {
            if (newValue != null)
                SearchText = GetSelectedText(newValue);
            // A consumer dropping the pick while someone types over it leaves their text alone.
            else if (_searchTextBox?.IsFocused != true || oldValue == null || SearchText == GetSelectedText(oldValue))
                SearchText = string.Empty;
        }
        finally
        {
            _isSettingFromSelectedItem = false;
        }

        RaisePropertyChanged(nameof(SelectedItemImage));
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _searchTextBox = this.FindControl<TextBox>("SearchTextBox");
        if (_searchTextBox != null)
        {
            _searchTextBox.GotFocus += OnSearchTextBoxGotFocus;
            _searchTextBox.KeyDown += OnSearchTextBoxKeyDown;
            // Use tunneling to catch pointer events before TextBox handles them
            _searchTextBox.AddHandler(PointerPressedEvent, OnSearchTextBoxPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        _itemsScrollViewer = this.FindControl<ScrollViewer>("ItemsScrollViewer");
        if (_itemsScrollViewer != null)
        {
            _itemsScrollViewer.PointerWheelChanged += OnItemsScrollViewerPointerWheelChanged;
        }
    }

    private void OnItemsScrollViewerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Always handle the event to prevent propagation to parent scroll viewers
        e.Handled = true;
    }

    private void OnSearchTextBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Open dropdown when clicking the TextBox, even if it already has focus
        if (!IsDropdownOpen)
        {
            IsDropdownOpen = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        HandleKeyDown(e);
    }

    private void OnSearchTextBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        IsDropdownOpen = true;
    }

    private void OnSearchTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        HandleKeyDown(e);
    }

    private void HandleKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (!IsDropdownOpen)
                {
                    IsDropdownOpen = true;
                }
                else
                {
                    MoveHighlight(1);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (IsDropdownOpen)
                {
                    MoveHighlight(-1);
                }
                e.Handled = true;
                break;

            case Key.Tab:
                if (IsDropdownOpen && TotalFilteredCount > 0)
                {
                    // Tab navigates down through items (Shift+Tab navigates up)
                    if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                    {
                        MoveHighlight(-1);
                    }
                    else
                    {
                        MoveHighlight(1);
                    }
                    e.Handled = true;
                }
                else
                {
                    // If dropdown is closed, let Tab move focus naturally
                    IsDropdownOpen = false;
                }
                break;

            case Key.Enter:
                if (IsDropdownOpen)
                {
                    var itemToSelect = _highlightedIndex >= 0 ? GetItemByIndex(_highlightedIndex)
                        : EnterSelectsBestMatch ? FindEnterMatch()
                        : null;
                    if (itemToSelect != null)
                    {
                        SelectItem(itemToSelect);
                    }
                }
                e.Handled = true;
                break;

            case Key.Escape:
                IsDropdownOpen = false;
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// The item Enter picks with nothing highlighted: the top of the list, only when it clearly matches the
    /// typed text rather than a fuzzy guess.
    /// </summary>
    private object? FindEnterMatch()
    {
        var text = SearchText?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        if (_showAll)
            return SelectedItem;

        // A keystroke still inside the debounce window hasn't filtered the list yet.
        _searchDebounceCts?.Cancel();
        UpdateFilteredItems();

        var first = GetItemByIndex(0);
        return first != null
               && LevenshteinDistance.BestScore(text, GetDisplayText(first), GetMemberText(first, SearchMemberPath)) >= StrongMatchScore
            ? first
            : null;
    }

    private void MoveHighlight(int direction)
    {
        var totalCount = TotalFilteredCount;
        if (totalCount == 0)
            return;

        _highlightedIndex += direction;

        // Wrap around navigation
        if (_highlightedIndex < 0)
            _highlightedIndex = totalCount - 1;
        else if (_highlightedIndex >= totalCount)
            _highlightedIndex = 0;

        HighlightedItem = GetItemByIndex(_highlightedIndex);
        ScrollToHighlightedItem();
    }

    private void ScrollToHighlightedItem()
    {
        if (_itemsScrollViewer == null || HighlightedItem == null)
            return;

        // Estimate item height (approximately 40px per item based on padding)
        const double itemHeight = 40;
        var scrollOffset = _highlightedIndex * itemHeight;

        // Ensure the highlighted item is visible
        var viewportHeight = _itemsScrollViewer.Viewport.Height;
        var currentOffset = _itemsScrollViewer.Offset.Y;

        if (scrollOffset < currentOffset)
        {
            _itemsScrollViewer.Offset = new Vector(0, scrollOffset);
        }
        else if (scrollOffset + itemHeight > currentOffset + viewportHeight)
        {
            _itemsScrollViewer.Offset = new Vector(0, scrollOffset + itemHeight - viewportHeight);
        }
    }

    private void ToggleDropdown()
    {
        IsDropdownOpen = !IsDropdownOpen;
        if (IsDropdownOpen)
        {
            _searchTextBox?.Focus();
        }
    }

    private void SelectItem(object? item)
    {
        if (item == null)
            return;

        SelectedItem = item;
        SearchText = GetSelectedText(item);

        // Rebuild the filtered list synchronously before closing, cancelling any debounced pass.
        // This is what keeps the popup's teardown safe: UpdateFilteredItems' Clear() detaches the
        // item buttons (including the one currently being clicked) from the popup BEFORE it closes.
        // Closing the popup while the clicked button is still attached and mid-click crashes
        // Avalonia 12's visual-tree teardown (ArgumentOutOfRangeException in
        // OnDetachedFromVisualTreeCore). Only the typing path is debounced; selection must be sync.
        _searchDebounceCts?.Cancel();
        UpdateFilteredItems();

        IsDropdownOpen = false;
        _highlightedIndex = -1;
        HighlightedItem = null;
    }

    private void OnSearchTextChanged()
    {
        if (!_isSettingFromSelectedItem)
            _showAll = false;

        // Typing over the picked item's name, or deleting it, drops the pick. Without this the box can
        // read empty while the form still holds the item, and a save writes to the hidden one. Only
        // while the user types: a consumer setting the text (a pre-filled row, a pending new name)
        // must not clear what it just selected.
        if (!_isSettingFromSelectedItem && _searchTextBox?.IsFocused == true
            && SelectedItem is { } picked && SearchText != GetSelectedText(picked))
        {
            SelectedItem = null;
        }

        // Open dropdown only when the user is actually typing (search box focused), not when the
        // text is set programmatically / via binding (e.g. pre-filled rows in the bank importer).
        if (!_isSettingFromSelectedItem && !string.IsNullOrEmpty(SearchText) && !IsDropdownOpen
            && _searchTextBox?.IsFocused == true)
        {
            IsDropdownOpen = true;
        }

        // Reset highlight when search text changes - user must press key to navigate
        _highlightedIndex = -1;
        HighlightedItem = null;

        // Debounce the expensive filter (fuzzy scoring over the whole source list). Opening the
        // dropdown filters immediately via the IsDropdownOpen handler, so first-open feedback is
        // instant; only subsequent keystrokes are debounced.
        _searchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = DebouncedUpdateFilteredItemsAsync(cts.Token);
    }

    private async Task DebouncedUpdateFilteredItemsAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, token);
        }
        catch (TaskCanceledException)
        {
            return; // superseded by a newer keystroke
        }

        // Resumes on the UI thread's captured synchronization context, so it's safe to touch the
        // bound collections here.
        if (!token.IsCancellationRequested)
            UpdateFilteredItems();
    }

    private void UpdateFilteredItems()
    {
        FilteredItems.Clear();
        FilteredPriorityItems.Clear();

        if (ItemsSource == null)
            return;

        var searchText = _showAll ? string.Empty : SearchText ?? string.Empty;
        var prioritySet = new HashSet<object>();

        // Build a set of priority items for quick lookup
        if (PriorityItems != null)
        {
            foreach (var item in PriorityItems)
            {
                if (item != null)
                    prioritySet.Add(item);
            }
        }

        if (string.IsNullOrEmpty(searchText))
        {
            // No search - show priority items first, then all other items
            foreach (var item in PriorityItems ?? Enumerable.Empty<object>())
            {
                if (item != null)
                {
                    FilteredPriorityItems.Add(item);
                }
            }

            foreach (var item in ItemsSource)
            {
                if (item != null && !prioritySet.Contains(item))
                {
                    FilteredItems.Add(item);
                }
            }
        }
        else
        {
            var priorityMatches = (PriorityItems ?? Enumerable.Empty<object>())
                .OfType<object>()
                .RankBySearch(searchText, i => [GetDisplayText(i), GetMemberText(i, SearchMemberPath)]);
            foreach (var item in priorityMatches)
            {
                FilteredPriorityItems.Add(item);
            }

            var matches = ItemsSource
                .OfType<object>()
                .Where(i => !prioritySet.Contains(i))
                .RankBySearch(searchText, i => [GetDisplayText(i), GetMemberText(i, SearchMemberPath)]);
            foreach (var item in matches)
            {
                FilteredItems.Add(item);
            }
        }

        // Notify property changed for computed properties
        RaisePropertyChanged(nameof(HasFilteredItems));
        RaisePropertyChanged(nameof(HasFilteredPriorityItems));
        RaisePropertyChanged(nameof(HasAnyFilteredItems));
        RaisePropertyChanged(nameof(ShowPrioritySeparator));
        RaisePropertyChanged(nameof(HasItems));
        RaisePropertyChanged(nameof(ShowEmptyCreate));
    }

    private string GetDisplayText(object item)
    {
        if (string.IsNullOrEmpty(DisplayMemberPath))
            return item.ToString() ?? string.Empty;

        return GetMemberText(item, DisplayMemberPath) ?? item.ToString() ?? string.Empty;
    }

    private string GetSelectedText(object item) =>
        string.IsNullOrEmpty(SelectedDisplayMemberPath)
            ? GetDisplayText(item)
            : GetMemberText(item, SelectedDisplayMemberPath) ?? GetDisplayText(item);

    private static string? GetMemberText(object item, string? path) => GetMemberValue(item, path)?.ToString();

    private static object? GetMemberValue(object item, string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var property = DisplayPropertyCache.GetOrAdd(
            (item.GetType(), path),
            static key => key.Item1.GetProperty(key.Item2));
        return property?.GetValue(item);
    }
}
