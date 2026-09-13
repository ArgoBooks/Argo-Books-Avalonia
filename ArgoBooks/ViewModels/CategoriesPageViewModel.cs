using System.Collections.ObjectModel;
using ArgoBooks.Core;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Helpers;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Categories page.
/// </summary>
public partial class CategoriesPageViewModel : SortablePageViewModelBase
{
    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

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

    [ObservableProperty]
    private string _paginationText = "0 categories";

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterCategories();

    /// <inheritdoc />
    protected override void UpdatePageNumbers()
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

    private void UpdatePaginationText(int totalCount)
    {
        PaginationText = PaginationTextHelper.FormatPaginationText(
            totalCount, CurrentPage, PageSize, TotalPages, "category", "categories");
    }

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

    #region Modal State

    [ObservableProperty]
    private bool _isAddModalOpen;

    [ObservableProperty]
    private bool _isEditModalOpen;

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private bool _isMoveModalOpen;

    #endregion

    #region Column Visibility and Widths

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    /// <summary>
    /// Column widths manager for the table (shared across page navigations).
    /// </summary>
    public CategoriesTableColumnWidths ColumnWidths => App.CategoriesColumnWidths;

    [ObservableProperty]
    private bool _showNameColumn = ColumnVisibilityHelper.Load("Categories", "Name", true);

    [ObservableProperty]
    private bool _showDescriptionColumn = ColumnVisibilityHelper.Load("Categories", "Description", true);

    [ObservableProperty]
    private bool _showProductCountColumn = ColumnVisibilityHelper.Load("Categories", "ProductCount", true);

    partial void OnShowNameColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Name", value); ColumnVisibilityHelper.Save("Categories", "Name", value); }
    partial void OnShowDescriptionColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("Description", value); ColumnVisibilityHelper.Save("Categories", "Description", value); }
    partial void OnShowProductCountColumnChanged(bool value) { ColumnWidths.SetColumnVisibility("ProductCount", value); ColumnVisibilityHelper.Save("Categories", "ProductCount", value); }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("Categories");
        ShowNameColumn = true;
        ShowDescriptionColumn = true;
        ShowProductCountColumn = true;
    }

    #endregion

    #region Modal Form Fields

    [ObservableProperty]
    private string _modalCategoryName = string.Empty;

    [ObservableProperty]
    private string _modalDescription = string.Empty;

    [ObservableProperty]
    private IconOption? _modalSelectedIconOption;

    [ObservableProperty]
    private string? _modalError;

    [ObservableProperty]
    private string? _modalCategoryNameError;

    /// <summary>
    /// The category being edited (null for add).
    /// </summary>
    private Category? _editingCategory;

    /// <summary>
    /// The category being deleted.
    /// </summary>
    private CategoryDisplayItem? _deletingCategory;

    /// <summary>
    /// Whether to also delete subcategories when deleting a parent category.
    /// </summary>
    [ObservableProperty]
    private bool _deleteSubcategories;

    /// <summary>
    /// The parent category when adding a sub-category.
    /// </summary>
    private CategoryDisplayItem? _addingSubCategoryParent;

    /// <summary>
    /// The category being moved.
    /// </summary>
    private CategoryDisplayItem? _movingCategory;

    #endregion

    #region Sub-Category Add Properties

    /// <summary>
    /// Gets whether we're adding a sub-category (vs top-level category).
    /// </summary>
    public bool IsAddingSubCategory => _addingSubCategoryParent != null;

    /// <summary>
    /// Gets the name of the parent category when adding a sub-category.
    /// </summary>
    public string AddingSubCategoryParentName => _addingSubCategoryParent?.Name ?? string.Empty;

    #endregion

    #region Move Modal Properties

    [ObservableProperty]
    private CategoryDisplayItem? _moveTargetCategory;

    [ObservableProperty]
    private string? _moveError;

    /// <summary>
    /// Available target categories for moving.
    /// </summary>
    public ObservableCollection<CategoryDisplayItem> MoveTargetCategories { get; } = [];

    /// <summary>
    /// Gets the name of the category being moved.
    /// </summary>
    public string MovingCategoryName => _movingCategory?.Name ?? string.Empty;

    #endregion

    #region Dropdown Options

    /// <summary>
    /// Available icons for dropdown.
    /// </summary>
    public ObservableCollection<IconOption> AvailableIcons { get; } =
    [
        new("📦", "Box"),
        new("🏷️", "Tag"),
        new("📁", "Folder"),
        new("🛒", "Shopping Cart"),
        new("🚚", "Truck"),
        new("🔧", "Tools"),
        new("🏠", "Home"),
        new("💻", "Computer"),
        new("📱", "Phone"),
        new("💡", "Light Bulb"),
        new("⚙️", "Settings"),
        new("⭐", "Star"),
        new("❤️", "Heart"),
        new("💵", "Dollar")
    ];

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public CategoriesPageViewModel()
    {
        // Set default selections
        _modalSelectedIconOption = AvailableIcons.FirstOrDefault();
        LoadCategories();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

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
        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
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
    /// Handles undo/redo state changes by refreshing the categories.
    /// </summary>
    private bool _needsRefresh;

    /// <summary>
    /// The sidebar opens this page on a tab, under its own page name.
    /// </summary>
    private static bool IsThisPage(string? pageName) =>
        pageName is PageNames.Categories or PageNames.ExpenseCategories or PageNames.RevenueCategories;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (!IsThisPage(App.NavigationService?.CurrentPageName))
        {
            _needsRefresh = true;
            return;
        }
        LoadCategories();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (IsThisPage(e.PageName) && _needsRefresh)
        {
            _needsRefresh = false;
            LoadCategories();
        }
    }

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
    /// Refreshes the categories from the data source.
    /// </summary>
    [RelayCommand]
    private void RefreshCategories()
    {
        LoadCategories();
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
                .Select(c => new
                {
                    Category = c,
                    NameScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, c.Name),
                    DescScore = LevenshteinDistance.ComputeSearchScore(SearchQuery, c.Description ?? string.Empty)
                })
                .Where(x => x.NameScore >= 0 || x.DescScore >= 0)
                .OrderByDescending(x => Math.Max(x.NameScore, x.DescScore))
                .Select(x => x.Category)
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

        // Calculate pagination
        var totalCount = displayItems.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Apply pagination
        var pagedItems = displayItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

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

    /// <summary>
    /// Closes the Add modal.
    /// </summary>
    [RelayCommand]
    private void CloseAddModal()
    {
        IsAddModalOpen = false;
        ClearModalFields();
    }

    /// <summary>
    /// Saves a new category.
    /// </summary>
    [RelayCommand]
    private void SaveNewCategory()
    {
        if (!ValidateModal())
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Generate new ID
        companyData.IdCounters.Category++;
        var typePrefix = IsExpensesTabSelected ? "PUR" : "SAL";
        var newId = $"CAT-{typePrefix}-{companyData.IdCounters.Category:D3}";

        var parentId = IsAddingSubCategory ? _addingSubCategoryParent?.Id : null;

        var newCategory = new Category
        {
            Id = newId,
            Name = ModalCategoryName.Trim(),
            Type = IsExpensesTabSelected ? CategoryType.Expense : CategoryType.Revenue,
            ParentId = parentId,
            Description = string.IsNullOrWhiteSpace(ModalDescription) ? null : ModalDescription.Trim(),
            Color = AppColors.CategoryDefault,
            Icon = ModalSelectedIconOption?.Icon ?? "📦"
        };

        companyData.Categories.Add(newCategory);
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add category '{newCategory.Name}'",
            () =>
            {
                companyData.Categories.Remove(newCategory);
                companyData.MarkAsModified();
                LoadCategories();
            },
            () =>
            {
                companyData.Categories.Add(newCategory);
                companyData.MarkAsModified();
                LoadCategories();
            }));

        // Reload and close
        LoadCategories();
        CloseAddModal();
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

    /// <summary>
    /// Closes the Edit modal.
    /// </summary>
    [RelayCommand]
    private void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingCategory = null;
        ClearModalFields();
    }

    /// <summary>
    /// Saves changes to an existing category.
    /// </summary>
    [RelayCommand]
    private void SaveEditedCategory()
    {
        if (!ValidateModal() || _editingCategory == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        // Store old values for undo
        var oldName = _editingCategory.Name;
        var oldDescription = _editingCategory.Description;
        var oldIcon = _editingCategory.Icon;

        // Store new values (parent is changed via Move, not Edit)
        var newName = ModalCategoryName.Trim();
        var newDescription = string.IsNullOrWhiteSpace(ModalDescription) ? null : ModalDescription.Trim();
        var newIcon = ModalSelectedIconOption?.Icon ?? "📦";

        // Update the category (keep parent unchanged)
        var categoryToEdit = _editingCategory;
        categoryToEdit.Name = newName;
        categoryToEdit.Description = newDescription;
        categoryToEdit.Icon = newIcon;

        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit category '{newName}'",
            () =>
            {
                categoryToEdit.Name = oldName;
                categoryToEdit.Description = oldDescription;
                categoryToEdit.Icon = oldIcon;
                companyData.MarkAsModified();
                LoadCategories();
            },
            () =>
            {
                categoryToEdit.Name = newName;
                categoryToEdit.Description = newDescription;
                categoryToEdit.Icon = newIcon;
                companyData.MarkAsModified();
                LoadCategories();
            }));

        // Reload and close
        LoadCategories();
        CloseEditModal();
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

    /// <summary>
    /// Closes the delete confirmation dialog.
    /// </summary>
    [RelayCommand]
    private void CloseDeleteConfirm()
    {
        IsDeleteConfirmOpen = false;
        _deletingCategory = null;
    }

    /// <summary>
    /// Confirms and deletes the category.
    /// </summary>
    [RelayCommand]
    private void ConfirmDelete()
    {
        if (_deletingCategory == null)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return;

        var category = companyData.Categories.FirstOrDefault(c => c.Id == _deletingCategory.Id);
        if (category != null)
        {
            // Store child categories for undo
            var children = companyData.Categories.Where(c => c.ParentId == category.Id).ToList();
            var childOriginalParents = children.ToDictionary(c => c.Id, c => c.ParentId);
            var deletedChildren = new List<Category>();
            var shouldDeleteSubcategories = DeleteSubcategories;

            if (shouldDeleteSubcategories)
            {
                // Delete subcategories
                deletedChildren.AddRange(children);
                foreach (var child in children)
                {
                    companyData.Categories.Remove(child);
                }
            }
            else
            {
                // Clear parent reference - subcategories become top-level
                foreach (var child in children)
                {
                    child.ParentId = null;
                }
            }

            var deletedCategory = category;
            companyData.Categories.Remove(category);
            companyData.MarkAsModified();

            App.UndoRedoManager.RecordAction(new DelegateAction(
                $"Delete category '{deletedCategory.Name}'",
                () =>
                {
                    // Undo: restore category
                    companyData.Categories.Add(deletedCategory);

                    if (shouldDeleteSubcategories)
                    {
                        foreach (var child in deletedChildren)
                        {
                            companyData.Categories.Add(child);
                        }
                    }
                    else
                    {
                        // Restore child parent references
                        foreach (var kvp in childOriginalParents)
                        {
                            var child = companyData.Categories.FirstOrDefault(c => c.Id == kvp.Key);
                            child?.ParentId = kvp.Value;
                        }
                    }
                    companyData.MarkAsModified();
                    LoadCategories();
                },
                () =>
                {
                    // Redo: delete again
                    if (shouldDeleteSubcategories)
                    {
                        foreach (var child in deletedChildren)
                        {
                            companyData.Categories.Remove(child);
                        }
                    }
                    else
                    {
                        foreach (var kvp in childOriginalParents)
                        {
                            var child = companyData.Categories.FirstOrDefault(c => c.Id == kvp.Key);
                            child?.ParentId = null;
                        }
                    }
                    companyData.Categories.Remove(deletedCategory);
                    companyData.MarkAsModified();
                    LoadCategories();
                }));
        }

        LoadCategories();
        CloseDeleteConfirm();
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

    /// <summary>
    /// Closes the Move modal.
    /// </summary>
    [RelayCommand]
    private void CloseMoveModal()
    {
        IsMoveModalOpen = false;
        _movingCategory = null;
        MoveTargetCategory = null;
        MoveError = null;
    }

    /// <summary>
    /// Confirms and moves the category to the new parent.
    /// </summary>
    [RelayCommand]
    private void ConfirmMove()
    {
        if (_movingCategory == null || MoveTargetCategory == null)
        {
            MoveError = "Please select a target category.".Translate().Translate();
            return;
        }

        var companyData = App.CompanyManager?.CompanyData;

        var category = companyData?.Categories.FirstOrDefault(c => c.Id == _movingCategory.Id);
        if (category == null)
            return;

        // Store old parent for undo
        var oldParentId = category.ParentId;
        var newParentId = string.IsNullOrEmpty(MoveTargetCategory.Id) ? null : MoveTargetCategory.Id;

        // Don't move if same parent
        if (oldParentId == newParentId)
        {
            MoveError = "Category is already under this parent.".Translate().Translate();
            return;
        }

        // Update parent
        category.ParentId = newParentId;
        companyData?.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Move category '{category.Name}'",
            () =>
            {
                category.ParentId = oldParentId;
                companyData?.MarkAsModified();
                LoadCategories();
            },
            () =>
            {
                category.ParentId = newParentId;
                companyData?.MarkAsModified();
                LoadCategories();
            }));

        LoadCategories();
        CloseMoveModal();
    }

    #endregion

    #region Modal Helpers

    private void ClearModalFields()
    {
        ModalCategoryName = string.Empty;
        ModalDescription = string.Empty;
        ModalSelectedIconOption = AvailableIcons.FirstOrDefault();
        ModalError = null;
        ModalCategoryNameError = null;
        _addingSubCategoryParent = null;
    }

    private bool ValidateModal()
    {
        // Clear all errors first
        ModalError = null;
        ModalCategoryNameError = null;

        var isValid = true;

        // Validate category name (required)
        if (string.IsNullOrWhiteSpace(ModalCategoryName))
        {
            ModalCategoryNameError = "Category name is required.".Translate();
            isValid = false;
        }
        else
        {
            // Check for duplicate names within the same type
            var targetType = IsExpensesTabSelected ? CategoryType.Expense : CategoryType.Revenue;
            var existingWithSameName = _allCategories.Any(c =>
                c.Type == targetType &&
                c.Name.Equals(ModalCategoryName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (_editingCategory == null || c.Id != _editingCategory.Id));

            if (existingWithSameName)
            {
                ModalCategoryNameError = "A category with this name already exists.".Translate();
                isValid = false;
            }
        }

        return isValid;
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

/// <summary>
/// Represents an icon option for dropdown.
/// </summary>
public class IconOption(string icon, string name)
{
    public string Icon { get; } = icon;
    public string Name { get; } = name;
    public string DisplayName => $"{Icon} {Name}";
}
