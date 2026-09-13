using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ArgoBooks.Utilities;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.Controls;

/// <summary>
/// Represents a category item for selection.
/// </summary>
public class CategoryItem
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A searchable category input control.
/// </summary>
public partial class CategoryInput : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;
    private TextBox? _categorySearchBox;
    private ListBox? _categoryListBox;
    private bool _isUpdatingText;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Styled Properties

    public static readonly StyledProperty<string?> SelectedCategoryIdProperty =
        AvaloniaProperty.Register<CategoryInput, string?>(nameof(SelectedCategoryId), defaultBindingMode: BindingMode.TwoWay);

    public string? SelectedCategoryId
    {
        get => GetValue(SelectedCategoryIdProperty);
        set => SetValue(SelectedCategoryIdProperty, value);
    }

    public static readonly StyledProperty<string?> SelectedCategoryNameProperty =
        AvaloniaProperty.Register<CategoryInput, string?>(nameof(SelectedCategoryName), defaultBindingMode: BindingMode.TwoWay);

    public string? SelectedCategoryName
    {
        get => GetValue(SelectedCategoryNameProperty);
        set => SetValue(SelectedCategoryNameProperty, value);
    }

    public static readonly StyledProperty<ObservableCollection<CategoryItem>> CategoriesProperty =
        AvaloniaProperty.Register<CategoryInput, ObservableCollection<CategoryItem>>(nameof(Categories), []);

    /// <summary>
    /// Gets or sets the available categories.
    /// </summary>
    public ObservableCollection<CategoryItem> Categories
    {
        get => GetValue(CategoriesProperty);
        set => SetValue(CategoriesProperty, value);
    }

    public static readonly StyledProperty<ICommand?> OpenCategoriesPageCommandProperty =
        AvaloniaProperty.Register<CategoryInput, ICommand?>(nameof(OpenCategoriesPageCommand));

    public ICommand? OpenCategoriesPageCommand
    {
        get => GetValue(OpenCategoriesPageCommandProperty);
        set => SetValue(OpenCategoriesPageCommandProperty, value);
    }

    #endregion

    #region Properties

    public CategoryItem? SelectedCategory
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
    /// Gets whether there are any categories available.
    /// </summary>
    public bool HasCategories => Categories.Count > 0;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                RaisePropertyChanged();
                if (!_isUpdatingText)
                {
                    UpdateFilteredCategories();
                    if (!string.IsNullOrEmpty(value) && value != SelectedCategory?.Name)
                    {
                        IsDropdownOpen = true;
                    }
                }
            }
        }
    }

    public bool IsDropdownOpen
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                RaisePropertyChanged();

                if (value)
                {
                    SelectedIndex = 0;
                    UpdateFilteredCategories();
                }
            }
        }
    }

    /// <summary>
    /// Gets whether there are filtered categories.
    /// </summary>
    public bool HasFilteredCategories
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
    /// Gets or sets the currently highlighted index in the dropdown.
    /// </summary>
    public int SelectedIndex
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                RaisePropertyChanged();
                ScrollToSelectedItem();
            }
        }
    } = -1;

    /// <summary>
    /// Gets the filtered categories based on search.
    /// </summary>
    public ObservableCollection<CategoryItem> FilteredCategories { get; } = [];

    #endregion

    #region Commands

    public ICommand ToggleDropdownCommand { get; }

    #endregion

    public CategoryInput()
    {
        ToggleDropdownCommand = new RelayCommand(ToggleDropdown);

        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CategoriesProperty)
        {
            RaisePropertyChanged(nameof(HasCategories));
            UpdateFilteredCategories();

            // Subscribe to collection changes
            if (change.OldValue is ObservableCollection<CategoryItem> oldCollection)
            {
                oldCollection.CollectionChanged -= OnCategoriesCollectionChanged;
            }
            if (change.NewValue is ObservableCollection<CategoryItem> newCollection)
            {
                newCollection.CollectionChanged += OnCategoriesCollectionChanged;
            }
        }
        else if (change.Property == SelectedCategoryIdProperty && !_isUpdatingText)
        {
            var id = change.NewValue as string;
            var category = Categories.FirstOrDefault(c => c.Id == id);
            SelectedCategory = category;
            UpdateSearchText();
        }
        else if (change.Property == SelectedCategoryNameProperty && !_isUpdatingText)
        {
            var name = change.NewValue as string;
            var category = Categories.FirstOrDefault(c =>
                c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            SelectedCategory = category;
            UpdateSearchText();
        }
    }

    private void OnCategoriesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(HasCategories));
        UpdateFilteredCategories();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _categorySearchBox = this.FindControl<TextBox>("CategorySearchBox");

        if (_categorySearchBox != null)
        {
            _categorySearchBox.GotFocus += OnCategorySearchBoxGotFocus;
            _categorySearchBox.KeyDown += OnCategorySearchBoxKeyDown;
        }

        _categoryListBox = this.FindControl<ListBox>("CategoryListBox");
        if (_categoryListBox != null)
        {
            _categoryListBox.DoubleTapped += OnCategoryListBoxDoubleTapped;
            _categoryListBox.PointerWheelChanged += OnCategoryListBoxPointerWheelChanged;
            _categoryListBox.PointerReleased += OnCategoryListBoxPointerReleased;
        }

        // Initialize with current selection
        UpdateFilteredCategories();
        UpdateSearchText();
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_categorySearchBox != null)
        {
            _categorySearchBox.GotFocus -= OnCategorySearchBoxGotFocus;
            _categorySearchBox.KeyDown -= OnCategorySearchBoxKeyDown;
        }

        if (_categoryListBox != null)
        {
            _categoryListBox.DoubleTapped -= OnCategoryListBoxDoubleTapped;
            _categoryListBox.PointerWheelChanged -= OnCategoryListBoxPointerWheelChanged;
            _categoryListBox.PointerReleased -= OnCategoryListBoxPointerReleased;
        }
    }

    private void OnCategoryListBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
    }

    private void OnCategoryListBoxDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_categoryListBox?.SelectedItem is CategoryItem category)
        {
            SelectCategory(category);
        }
    }

    private void OnCategoryListBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_categoryListBox?.SelectedItem is CategoryItem category)
        {
            SelectCategory(category);
        }
    }

    private void OnCategorySearchBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        IsDropdownOpen = true;
        _categorySearchBox?.SelectAll();
    }

    private void OnCategorySearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (!IsDropdownOpen)
                {
                    IsDropdownOpen = true;
                    SelectedIndex = 0;
                }
                else if (SelectedIndex < FilteredCategories.Count - 1)
                {
                    SelectedIndex++;
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (IsDropdownOpen && SelectedIndex > 0)
                {
                    SelectedIndex--;
                }
                e.Handled = true;
                break;
            case Key.Escape:
                IsDropdownOpen = false;
                UpdateSearchText();
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Tab:
                if (IsDropdownOpen && SelectedIndex >= 0 && SelectedIndex < FilteredCategories.Count)
                {
                    SelectCategory(FilteredCategories[SelectedIndex]);
                }
                else if (FilteredCategories.Count > 0)
                {
                    SelectCategory(FilteredCategories[0]);
                }
                if (e.Key == Key.Enter)
                    e.Handled = true;
                break;
        }
    }

    private void ScrollToSelectedItem()
    {
        if (_categoryListBox == null || SelectedIndex < 0 || SelectedIndex >= FilteredCategories.Count)
            return;

        _categoryListBox.ScrollIntoView(FilteredCategories[SelectedIndex]);
    }

    private void ToggleDropdown()
    {
        IsDropdownOpen = !IsDropdownOpen;
        if (IsDropdownOpen)
        {
            _categorySearchBox?.Focus();
            _categorySearchBox?.SelectAll();
        }
    }

    private void SelectCategory(CategoryItem? category)
    {
        if (category == null)
            return;

        _isUpdatingText = true;
        SelectedCategory = category;
        SelectedCategoryId = category.Id;
        SelectedCategoryName = category.Name;
        UpdateSearchText();
        IsDropdownOpen = false;
        _isUpdatingText = false;
    }

    private void UpdateSearchText()
    {
        _isUpdatingText = true;
        _searchText = SelectedCategory?.Name ?? string.Empty;
        RaisePropertyChanged(nameof(SearchText));
        _isUpdatingText = false;
    }

    private void UpdateFilteredCategories()
    {
        FilteredCategories.Clear();

        var searchText = _searchText.Trim().ToLowerInvariant();

        IEnumerable<CategoryItem> filtered;

        if (string.IsNullOrEmpty(searchText) || searchText == SelectedCategory?.Name.ToLowerInvariant())
        {
            filtered = Categories;
        }
        else
        {
            filtered = Categories.RankBySearch(searchText, c => [c.Name]);
        }

        foreach (var item in filtered.Take(50))
        {
            FilteredCategories.Add(item);
        }

        HasFilteredCategories = FilteredCategories.Count > 0;
    }

    /// <summary>
    /// Clears the selection.
    /// </summary>
    public void ClearSelection()
    {
        _isUpdatingText = true;
        SelectedCategory = null;
        SelectedCategoryId = null;
        SelectedCategoryName = null;
        _searchText = string.Empty;
        RaisePropertyChanged(nameof(SearchText));
        _isUpdatingText = false;
    }

    /// <summary>
    /// Handles the click event for the "Create one" button.
    /// </summary>
    private void OnCreateCategoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IsDropdownOpen = false;
        OpenCategoriesPageCommand?.Execute(null);
    }
}
