using ArgoBooks.Core;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Helpers;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Categories page.
/// </summary>
public partial class CategoriesPageViewModel : SortablePageViewModelBase
{
    #region Tab Selection

    [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsExpensesTabSelected => SelectedTabIndex == 0;

    public bool IsRevenueTabSelected => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsExpensesTabSelected));
        OnPropertyChanged(nameof(IsRevenueTabSelected));
        FilterCategories();
    }

    #endregion

    #region Search

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterCategories();
        });

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterCategories();

    #endregion

    #region Categories Collections

    /// <summary>
    /// All categories (unfiltered).
    /// </summary>
    private readonly List<Category> _allCategories = [];

    /// <summary>
    /// Expense categories (Purchase type) for display.
    /// </summary>
    public BatchObservableCollection<CategoryDisplayItem> ExpenseCategories { get; } = [];

    /// <summary>
    /// Revenue categories (Sales type) for display.
    /// </summary>
    public BatchObservableCollection<CategoryDisplayItem> RevenueCategories { get; } = [];

    /// <summary>
    /// Gets the current tab's categories for display.
    /// </summary>
    public BatchObservableCollection<CategoryDisplayItem> CurrentCategories =>
        IsExpensesTabSelected ? ExpenseCategories : RevenueCategories;

    #endregion

    #region Column Visibility and Widths

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public CategoriesTableColumnWidths ColumnWidths => App.CategoriesColumnWidths;

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Categories", new Dictionary<string, bool>
    {
        ["Name"] = true,
        ["Description"] = true,
        ["ProductCount"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showNameColumn = ColumnDefaults.Load("Name");

    [ObservableProperty]
    private bool _showDescriptionColumn = ColumnDefaults.Load("Description");

    [ObservableProperty]
    private bool _showProductCountColumn = ColumnDefaults.Load("ProductCount");

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public CategoriesPageViewModel()
    {
        LoadCategories();

        EnableDeferredUndoRefresh(IsThisPage, LoadCategories);

        // Subscribe to shared modal events to refresh data
        if (App.CategoryModalsViewModel != null)
        {
            App.CategoryModalsViewModel.CategorySaved += OnCategoryModalClosed;
            App.CategoryModalsViewModel.CategoryDeleted += OnCategoryModalClosed;
        }
    }

    /// <summary>
    /// Unsubscribes from the events wired up in the constructor so the VM isn't kept alive (and
    /// reacting) after a company switch.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.CategoryModalsViewModel != null)
        {
            App.CategoryModalsViewModel.CategorySaved -= OnCategoryModalClosed;
            App.CategoryModalsViewModel.CategoryDeleted -= OnCategoryModalClosed;
        }
    }

    /// <summary>
    /// Handles category modal closed events by refreshing the categories.
    /// </summary>
    private void OnCategoryModalClosed(object? sender, EventArgs e)
    {
        LoadCategories();
    }

    /// <summary>
    /// The sidebar opens this page on a tab, under its own page name.
    /// </summary>
    private static bool IsThisPage(string? pageName) =>
        pageName is PageNames.Categories or PageNames.ExpenseCategories or PageNames.RevenueCategories;

    #endregion

    #region Data Loading

    /// <summary>
    /// Loads categories from the company data.
    /// </summary>
    private void LoadCategories()
    {
        _allCategories.Clear();
        ExpenseCategories.Clear();
        RevenueCategories.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Categories == null)
            return;

        _allCategories.AddRange(companyData.Categories);
        FilterCategories();
    }

    /// <summary>
    /// Filters and organizes categories based on current tab, search, sorting, and pagination.
    /// </summary>
    private void FilterCategories()
    {
        var targetType = IsExpensesTabSelected ? CategoryType.Expense : CategoryType.Revenue;
        var targetCollection = IsExpensesTabSelected ? ExpenseCategories : RevenueCategories;

        // Get all categories of the current type
        var categories = _allCategories
            .Where(c => c.Type == targetType)
            .ToList();

        // Apply search filter using Levenshtein distance
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            categories = categories
                .RankBySearch(SearchQuery, c => [c.Name, c.Description])
                .ToList();
        }

        // Pre-build product count lookup for O(1) access per category
        var productCountByCategory = App.CompanyManager?.CompanyData?.Products
            .Where(p => !string.IsNullOrEmpty(p.CategoryId))
            .GroupBy(p => p.CategoryId!)
            .ToDictionary(g => g.Key, g => g.Count());

        // Build display items list (flat, with parent-child info)
        var displayItems = new List<CategoryDisplayItem>();

        // Build hierarchy - parent categories first, then children
        var parentCategories = categories.Where(c => string.IsNullOrEmpty(c.ParentId)).ToList();

        foreach (var parent in parentCategories)
        {
            var childCount = CountChildren(parent.Id);
            displayItems.Add(CreateDisplayItem(parent, null, childCount, productCountByCategory));

            // Add children
            var children = categories.Where(c => c.ParentId == parent.Id);
            foreach (var child in children)
            {
                var grandchildCount = CountChildren(child.Id);
                displayItems.Add(CreateDisplayItem(child, parent.Name, grandchildCount, productCountByCategory, isChild: true));
            }
        }

        // Add orphaned categories (have a parent ID that doesn't exist)
        var orphans = categories.Where(c =>
            !string.IsNullOrEmpty(c.ParentId) &&
            !categories.Any(p => p.Id == c.ParentId));

        foreach (var orphan in orphans)
        {
            var childCount = CountChildren(orphan.Id);
            displayItems.Add(CreateDisplayItem(orphan, "Unknown", childCount, productCountByCategory));
        }

        // Apply sorting
        if (SortDirection != SortDirection.None)
        {
            displayItems = displayItems.ApplySort(
                SortColumn,
                SortDirection,
                new Dictionary<string, Func<CategoryDisplayItem, object?>>
                {
                    ["Name"] = x => x.Name,
                    ["Parent"] = x => x.ParentName,
                    ["Description"] = x => x.Description,
                    ["ProductCount"] = x => x.ProductCount
                });
        }

        var pagedItems = Paginate(displayItems, "category", "categories");

        targetCollection.ReplaceAll(pagedItems);

        OnPropertyChanged(nameof(CurrentCategories));
    }

    private CategoryDisplayItem CreateDisplayItem(Category category, string? parentName, int childCount, Dictionary<string, int>? productCountByCategory, bool isChild = false)
    {
        // Look up product count from pre-built dictionary
        var productCount = productCountByCategory?.GetValueOrDefault(category.Id) ?? 0;

        return new CategoryDisplayItem
        {
            Id = category.Id,
            Name = category.Name,
            ParentId = category.ParentId,
            ParentName = parentName ?? string.Empty,
            Description = category.Description ?? string.Empty,
            Color = category.Color,
            Icon = category.Icon,
            ProductCount = productCount,
            ChildCount = childCount,
            IsChild = isChild,
            Type = category.Type
        };
    }

    private int CountChildren(string parentId)
    {
        return _allCategories.Count(c => c.ParentId == parentId);
    }

    #endregion

    #region Add Category

    /// <summary>
    /// Opens the Add Category modal (for top-level category).
    /// </summary>
    [RelayCommand]
    private void OpenAddModal()
    {
        App.CategoryModalsViewModel?.OpenAddModal(IsExpensesTabSelected);
    }

    /// <summary>
    /// Opens the Add Category modal for adding a sub-category under a parent.
    /// </summary>
    [RelayCommand]
    private void OpenAddSubCategoryModal(CategoryDisplayItem? parent)
    {
        App.CategoryModalsViewModel?.OpenAddSubCategoryModal(parent, IsExpensesTabSelected);
    }

    #endregion

    #region Edit Category

    /// <summary>
    /// Opens the Edit Category modal.
    /// </summary>
    [RelayCommand]
    private void OpenEditModal(CategoryDisplayItem? item)
    {
        App.CategoryModalsViewModel?.OpenEditModal(item, IsExpensesTabSelected);
    }

    #endregion

    #region Delete Category

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void OpenDeleteConfirm(CategoryDisplayItem? item)
    {
        App.CategoryModalsViewModel?.OpenDeleteConfirm(item);
    }

    #endregion

    #region Move Category

    /// <summary>
    /// Opens the Move Category modal.
    /// </summary>
    [RelayCommand]
    private void OpenMoveModal(CategoryDisplayItem? item)
    {
        App.CategoryModalsViewModel?.OpenMoveModal(item, IsExpensesTabSelected);
    }

    #endregion
}

/// <summary>
/// Display model for categories in the UI.
/// </summary>
public partial class CategoryDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _parentId;

    [ObservableProperty]
    private string _parentName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _color = AppColors.CategoryDefault;

    [ObservableProperty]
    private string _icon = "📦";

    [ObservableProperty]
    private int _productCount;

    [ObservableProperty]
    private int _childCount;

    [ObservableProperty]
    private bool _isChild;

    [ObservableProperty]
    private CategoryType _type;

}
