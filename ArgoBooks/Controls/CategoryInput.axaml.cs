using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

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
/// A searchable category picker built on <see cref="SearchableDropdown"/>.
/// </summary>
public partial class CategoryInput : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public static readonly StyledProperty<string?> SelectedCategoryIdProperty =
        AvaloniaProperty.Register<CategoryInput, string?>(nameof(SelectedCategoryId), defaultBindingMode: BindingMode.TwoWay);

    public string? SelectedCategoryId
    {
        get => GetValue(SelectedCategoryIdProperty);
        set => SetValue(SelectedCategoryIdProperty, value);
    }

    public static readonly StyledProperty<ObservableCollection<CategoryItem>> CategoriesProperty =
        AvaloniaProperty.Register<CategoryInput, ObservableCollection<CategoryItem>>(nameof(Categories), []);

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

    public static readonly StyledProperty<string?> TypedTextProperty =
        AvaloniaProperty.Register<CategoryInput, string?>(nameof(TypedText), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// What is actually typed in the box, whether or not it names an existing category, so a save can
    /// create the category the user typed instead of rejecting the form.
    /// </summary>
    public string? TypedText
    {
        get => GetValue(TypedTextProperty);
        set => SetValue(TypedTextProperty, value);
    }

    private bool _syncing;

    public CategoryItem? SelectedCategory
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            RaisePropertyChanged();
            if (_syncing) return;

            _syncing = true;
            try
            {
                SelectedCategoryId = value?.Id;
            }
            finally
            {
                _syncing = false;
            }
        }
    }

    /// <summary>
    /// The text in the box. Typing a category's full name picks it and anything else clears the pick, so the
    /// required-category check treats half-typed text as no category.
    /// </summary>
    public string? SearchText
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            RaisePropertyChanged();
            TypedText = value;
            if (!_syncing)
                SelectedCategory = FindByName(value);
        }
    }

    public CategoryInput()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CategoriesProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= OnCategoriesCollectionChanged;
            if (change.NewValue is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += OnCategoriesCollectionChanged;
            SyncFromId();
        }
        else if (change.Property == SelectedCategoryIdProperty && !_syncing)
        {
            SyncFromId();
        }
    }

    // A reloaded list holds new items, so the pick is found again by its id.
    private void OnCategoriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncFromId();

    private void SyncFromId()
    {
        var id = SelectedCategoryId;
        _syncing = true;
        try
        {
            SelectedCategory = id == null ? null : Categories.FirstOrDefault(c => c.Id == id);
        }
        finally
        {
            _syncing = false;
        }
    }

    private CategoryItem? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();
        if (SelectedCategory != null && SelectedCategory.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            return SelectedCategory;

        return Categories.FirstOrDefault(c => c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
