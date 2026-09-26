using ArgoBooks.Core.Data;
using ArgoBooks.Core;
using ArgoBooks.Core.Models;
using ArgoBooks.Services;
using ArgoBooks.Localization;
using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for category modals, shared between CategoriesPage and AppShell.
/// </summary>
public partial class CategoryModalsViewModel : ViewModelBase
{
    #region Modal State

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private bool _isAddModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private bool _isEditModalOpen;

    /// <summary>Add and edit share one form, open while either flag is set.</summary>
    public bool IsFormOpen => IsAddModalOpen || IsEditModalOpen;

    // Set when the form opens and kept on close, so the title doesn't change while it closes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle), nameof(FormSaveText))]
    private bool _isEditMode;

    public string FormTitle => IsEditMode ? LanguageService.Instance.Translate("Edit Category") : AddModalTitle;
    public string FormSaveText => LanguageService.Instance.Translate(IsEditMode ? "Save Changes" : "Add Category");

    partial void OnIsAddModalOpenChanged(bool value)
    {
        if (value) IsEditMode = false;
    }

    partial void OnIsEditModalOpenChanged(bool value)
    {
        if (value) IsEditMode = true;
    }

    [RelayCommand]
    private Task RequestCloseFormAsync() => IsEditMode ? RequestCloseEditModalAsync() : RequestCloseAddModalAsync();

    [RelayCommand]
    private void SaveForm()
    {
        if (IsEditMode) SaveEditedCategory();
        else SaveNewCategory();
    }

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private bool _isMoveModalOpen;

    #endregion

    #region Modal Form Fields

    [ObservableProperty]
    private string _modalCategoryName = string.Empty;

    [ObservableProperty]
    private string _modalDescription = string.Empty;

    [ObservableProperty]
    private string _modalSelectedIcon = "📦";

    [ObservableProperty]
    private string? _modalError;

    [ObservableProperty]
    private string? _modalCategoryNameError;

    private Category? _editingCategory;
    private CategoryDisplayItem? _addingSubCategoryParent;
    private CategoryDisplayItem? _movingCategory;
    private bool _isExpensesTab = true;

    private sealed record EditState(string Name, string Description, string Icon);

    // The form as the edit modal opened, for change detection.
    private EditState? _original;

    private EditState Capture() => new(ModalCategoryName, ModalDescription, ModalSelectedIcon);

    /// <summary>
    /// Returns true if any data has been entered in the Add modal.
    /// </summary>
    public bool HasAddModalEnteredData =>
        !string.IsNullOrWhiteSpace(ModalCategoryName) ||
        !string.IsNullOrWhiteSpace(ModalDescription) ||
        ModalSelectedIcon != "📦";

    /// <summary>
    /// Returns true if any changes have been made in the Edit modal.
    /// </summary>
    public bool HasEditModalChanges => Capture() != _original;

    #endregion

    #region Move Modal Properties

    [ObservableProperty]
    private CategoryDisplayItem? _moveTargetCategory;

    [ObservableProperty]
    private string? _moveError;

    public ObservableCollection<CategoryDisplayItem> MoveTargetCategories { get; } = [];

    public string MovingCategoryName => _movingCategory?.Name ?? string.Empty;

    #endregion

    #region Sub-Category Properties

    public bool IsAddingSubCategory => _addingSubCategoryParent != null;
    public string AddingSubCategoryParentName => _addingSubCategoryParent?.Name ?? string.Empty;

    /// <summary>
    /// Gets the translated title for the Add Category modal.
    /// </summary>
    public string AddModalTitle => IsAddingSubCategory
        ? LanguageService.Instance.Translate("Add Sub-Category")
        : LanguageService.Instance.Translate("Add Category");

    #endregion

    #region Dropdown Options

    /// <summary>
    /// ViewModel for the emoji picker modal.
    /// </summary>
    public EmojiPickerViewModel EmojiPicker { get; } = new();

    #endregion

    #region Events

    public event EventHandler? CategorySaved;
    public event EventHandler? CategoryDeleted;

    /// <summary>
    /// The Id of the category most recently created via the Add modal. Lets a caller that
    /// opened "create category" from another modal auto-select the new category after save.
    /// </summary>
    public string? LastSavedCategoryId { get; private set; }

    #endregion

    /// <summary>
    /// Opens the emoji picker modal.
    /// </summary>
    [RelayCommand]
    public void OpenEmojiPicker()
    {
        EmojiPicker.Open(ModalSelectedIcon, emoji =>
        {
            ModalSelectedIcon = emoji;
        });
    }

    #region Add Category

    [RelayCommand]
    public void OpenAddModal()
    {
        _editingCategory = null;
        _addingSubCategoryParent = null;
        ClearModalFields();
        OnPropertyChanged(nameof(IsAddingSubCategory));
        OnPropertyChanged(nameof(AddingSubCategoryParentName));
        OnPropertyChanged(nameof(AddModalTitle));
        OnPropertyChanged(nameof(FormTitle));
        IsAddModalOpen = true;
    }

    public void OpenAddModal(bool isExpensesTab)
    {
        _isExpensesTab = isExpensesTab;
        OpenAddModal();
    }

    public void OpenAddSubCategoryModal(CategoryDisplayItem? parent, bool isExpensesTab)
    {
        if (parent == null) return;
        _isExpensesTab = isExpensesTab;
        _editingCategory = null;
        ClearModalFields();
        _addingSubCategoryParent = parent;
        OnPropertyChanged(nameof(IsAddingSubCategory));
        OnPropertyChanged(nameof(AddingSubCategoryParentName));
        OnPropertyChanged(nameof(AddModalTitle));
        OnPropertyChanged(nameof(FormTitle));
        IsAddModalOpen = true;
    }

    [RelayCommand]
    public void CloseAddModal()
    {
        IsAddModalOpen = false;
        ClearModalFields();
    }

    /// <summary>
    /// Requests to close the Add modal, showing confirmation if data was entered.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseAddModalAsync()
    {
        if (HasAddModalEnteredData)
        {
            if (!await ConfirmDiscardNewAsync())
                return;
        }

        CloseAddModal();
    }

    public void SaveNewCategory()
    {
        if (!ValidateModal()) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var categoryType = _isExpensesTab ? CategoryType.Expense : CategoryType.Revenue;
        var newId = new Core.Data.IdGenerator(companyData).NextCategoryId(categoryType);
        var parentId = IsAddingSubCategory ? _addingSubCategoryParent?.Id : null;

        var newCategory = new Category
        {
            Id = newId,
            Name = ModalCategoryName.Trim(),
            Type = categoryType,
            ParentId = parentId,
            Description = string.IsNullOrWhiteSpace(ModalDescription) ? null : ModalDescription.Trim(),
            Color = AppColors.CategoryDefault,
            Icon = ModalSelectedIcon
        };

        companyData.Categories.Add(newCategory);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.CategoryCreated);
        companyData.MarkAsModified();

        var categoryToUndo = newCategory;
        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Add category '{newCategory.Name}'",
            () => { companyData.Categories.RemoveRecord(categoryToUndo); companyData.MarkAsModified(); CategorySaved?.Invoke(this, EventArgs.Empty); },
            () => { companyData.Categories.RestoreRecord(categoryToUndo); companyData.MarkAsModified(); CategorySaved?.Invoke(this, EventArgs.Empty); }));

        LastSavedCategoryId = newCategory.Id;
        CategorySaved?.Invoke(this, EventArgs.Empty);

        CloseAddModal();
    }

    #endregion

    #region Edit Category

    public void OpenEditModal(CategoryDisplayItem? item, bool isExpensesTab)
    {
        if (item == null) return;
        _isExpensesTab = isExpensesTab;

        var companyData = App.CompanyManager?.CompanyData;
        var category = companyData?.GetCategory(item.Id);
        if (category == null) return;

        _editingCategory = category;
        ModalCategoryName = category.Name;
        ModalDescription = category.Description ?? string.Empty;
        ModalSelectedIcon = category.Icon;

        _original = Capture();

        ModalError = null;
        IsEditModalOpen = true;
    }

    public void CloseEditModal()
    {
        IsEditModalOpen = false;
        _editingCategory = null;
        ClearModalFields();
    }

    /// <summary>
    /// Requests to close the Edit modal, showing confirmation if changes were made.
    /// </summary>
    public async Task RequestCloseEditModalAsync()
    {
        if (HasEditModalChanges)
        {
            if (!await ConfirmDiscardEditsAsync())
                return;
        }

        CloseEditModal();
    }

    public void SaveEditedCategory()
    {
        if (!ValidateModal() || _editingCategory == null) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var oldName = _editingCategory.Name;
        var oldDescription = _editingCategory.Description;
        var oldIcon = _editingCategory.Icon;

        var newName = ModalCategoryName.Trim();
        var newDescription = string.IsNullOrWhiteSpace(ModalDescription) ? null : ModalDescription.Trim();
        var newIcon = ModalSelectedIcon;

        // Check if anything actually changed
        var hasChanges = oldName != newName ||
                         oldDescription != newDescription ||
                         oldIcon != newIcon;

        // If nothing changed, just close the modal without recording an action
        if (!hasChanges)
        {
            CloseEditModal();
            return;
        }

        var categoryToEdit = _editingCategory;
        var changes = new Dictionary<string, FieldChange>();
        if (oldName != newName) changes["Name"] = new FieldChange { OldValue = oldName, NewValue = newName };
        if (oldDescription != newDescription) changes["Description"] = new FieldChange { OldValue = oldDescription ?? "", NewValue = newDescription ?? "" };
        if (oldIcon != newIcon) changes["Icon"] = new FieldChange { OldValue = oldIcon, NewValue = newIcon };
        if (changes.Count > 0) App.EventLogService?.SetPendingChanges(changes);
        categoryToEdit.Name = newName;
        categoryToEdit.Description = newDescription;
        categoryToEdit.Icon = newIcon;
        companyData.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Edit category '{newName}'",
            () => { categoryToEdit.Name = oldName; categoryToEdit.Description = oldDescription; categoryToEdit.Icon = oldIcon; companyData.MarkAsModified(); CategorySaved?.Invoke(this, EventArgs.Empty); },
            () => { categoryToEdit.Name = newName; categoryToEdit.Description = newDescription; categoryToEdit.Icon = newIcon; companyData.MarkAsModified(); CategorySaved?.Invoke(this, EventArgs.Empty); }));

        CategorySaved?.Invoke(this, EventArgs.Empty);
        CloseEditModal();
    }

    #endregion

    #region Delete Category

    public async void OpenDeleteConfirm(CategoryDisplayItem? item)
    {
        try
        {
            if (item == null) return;

            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null) return;

            if (await BlockIfInUseAsync(
                    usages => "This category cannot be deleted because it is referenced by one or more: {0}.".TranslateFormat(usages),
                    (companyData.Products.Any(p => p.CategoryId == item.Id), "Product".Translate()),
                    (companyData.BankCategoryRules.Any(r => r.CategoryId == item.Id), "Bank Rule".Translate())))
                return;

            var category = companyData.GetCategory(item.Id);
            if (category == null) return;

            var children = companyData.Categories.Where(c => c.ParentId == category.Id).ToList();

            // A category with children asks whether they go too or move to the top level.
            var deleteSubcategories = false;
            if (children.Count > 0)
            {
                var dialog = App.ConfirmationDialog;
                if (dialog == null) return;

                var subResult = await dialog.ShowAsync(new ConfirmationDialogOptions
                {
                    Title = "Delete Category".Translate(),
                    Message = "This category has {0} subcategories.\n\nDo you want to delete them as well, or move them to the top level?".TranslateFormat(children.Count),
                    PrimaryButtonText = "Delete All".Translate(),
                    SecondaryButtonText = "Move to Top Level".Translate(),
                    CancelButtonText = "Cancel".Translate(),
                    IsPrimaryDestructive = true
                });

                if (subResult == ConfirmationResult.Cancel || subResult == ConfirmationResult.None)
                    return;

                deleteSubcategories = subResult == ConfirmationResult.Primary;

                var childIds = children.Select(c => c.Id).ToHashSet();
                if (deleteSubcategories &&
                    companyData.Products.Any(p => p.CategoryId != null && childIds.Contains(p.CategoryId)))
                {
                    await App.ShowWarningDialogAsync(
                        "Cannot Delete".Translate(),
                        "A subcategory of this category is used by one or more products, so it cannot be deleted. Choose Move to Top Level instead.".Translate());
                    return;
                }

                if (deleteSubcategories && companyData.BankCategoryRules.Any(r => childIds.Contains(r.CategoryId)))
                {
                    await App.ShowWarningDialogAsync(
                        "Cannot Delete".Translate(),
                        "A subcategory of this category is used by one or more bank rules, so it cannot be deleted. Choose Move to Top Level instead.".Translate());
                    return;
                }
            }
            else if (!await ConfirmDeleteAsync("Delete Category".Translate(),
                         "Are you sure you want to delete this category?\n\n{0}".TranslateFormat(item.Name)))
            {
                return;
            }

            RemoveWithUndo(companyData, companyData.Categories, category, $"Delete category '{category.Name}'",
                () => CategoryDeleted?.Invoke(this, EventArgs.Empty),
                onRemove: () =>
                {
                    foreach (var child in children)
                    {
                        if (deleteSubcategories)
                            companyData.Categories.Remove(child);
                        else
                            child.ParentId = null;
                    }
                },
                onRestore: () =>
                {
                    foreach (var child in children)
                    {
                        if (deleteSubcategories)
                            companyData.Categories.Add(child);
                        else
                            child.ParentId = category.Id;
                    }
                });
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Validation, "Category.OpenDeleteConfirm");
        }
    }

    #endregion

    #region Move Category

    public void OpenMoveModal(CategoryDisplayItem? item, bool isExpensesTab)
    {
        if (item == null) return;
        _isExpensesTab = isExpensesTab;
        _movingCategory = item;
        MoveError = null;
        UpdateMoveTargetCategories();
        OnPropertyChanged(nameof(MovingCategoryName));
        IsMoveModalOpen = true;
    }

    [RelayCommand]
    public void CloseMoveModal()
    {
        IsMoveModalOpen = false;
        _movingCategory = null;
        MoveTargetCategory = null;
        MoveError = null;
    }

    [RelayCommand]
    public void ConfirmMove()
    {
        if (_movingCategory == null || MoveTargetCategory == null)
        {
            MoveError = "Please select a target category.".Translate();
            return;
        }

        var companyData = App.CompanyManager?.CompanyData;

        var category = companyData?.GetCategory(_movingCategory.Id);
        if (category == null) return;

        var oldParentId = category.ParentId;
        var newParentId = string.IsNullOrEmpty(MoveTargetCategory.Id) ? null : MoveTargetCategory.Id;

        if (oldParentId == newParentId)
        {
            MoveError = "Category is already under this parent.".Translate();
            return;
        }

        // The Categories page shows two levels, so its subcategories would drop out of sight.
        if (newParentId != null && companyData!.Categories.Any(c => c.ParentId == category.Id))
        {
            MoveError = "A category with subcategories can't be moved under another category.".Translate();
            return;
        }

        category.ParentId = newParentId;
        companyData?.MarkAsModified();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Move category '{category.Name}'",
            () => { category.ParentId = oldParentId; companyData?.MarkAsModified(); CategorySaved?.Invoke(this, EventArgs.Empty); },
            () => { category.ParentId = newParentId; companyData?.MarkAsModified(); CategorySaved?.Invoke(this, EventArgs.Empty); }));

        CategorySaved?.Invoke(this, EventArgs.Empty);
        CloseMoveModal();
    }

    private void UpdateMoveTargetCategories()
    {
        MoveTargetCategories.Clear();
        // No icon on the sentinel: CategoryDisplayItem.Icon defaults to a box emoji, and the
        // move dropdown hides the icon slot only when it's empty.
        MoveTargetCategories.Add(new CategoryDisplayItem { Id = string.Empty, Name = "None (Make Top-Level)", Icon = string.Empty });

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var targetType = _isExpensesTab ? CategoryType.Expense : CategoryType.Revenue;
        var topLevelCategories = companyData.Categories
            .Where(c => c.Type == targetType && string.IsNullOrEmpty(c.ParentId))
            .OrderBy(c => c.Name);

        foreach (var cat in topLevelCategories)
        {
            if (_movingCategory != null && cat.Id == _movingCategory.Id) continue;
            MoveTargetCategories.Add(new CategoryDisplayItem { Id = cat.Id, Name = cat.Name, Icon = cat.Icon });
        }

        if (_movingCategory?.ParentId != null)
        {
            MoveTargetCategory = MoveTargetCategories.FirstOrDefault(c => c.Id == _movingCategory.ParentId);
        }
    }

    #endregion

    #region Property Changed Handlers

    partial void OnModalCategoryNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ModalCategoryNameError = null;
        }
    }

    #endregion

    #region Helpers

    private void ClearModalFields()
    {
        ModalCategoryName = string.Empty;
        ModalDescription = string.Empty;
        ModalSelectedIcon = "📦";
        ModalError = null;
        ModalCategoryNameError = null;
        _addingSubCategoryParent = null;
    }

    private bool ValidateModal()
    {
        ModalError = null;
        ModalCategoryNameError = null;
        var isValid = true;

        if (string.IsNullOrWhiteSpace(ModalCategoryName))
        {
            ModalCategoryNameError = "Category name is required.".Translate();
            isValid = false;
        }
        else
        {
            var companyData = App.CompanyManager?.CompanyData;
            var targetType = _isExpensesTab ? CategoryType.Expense : CategoryType.Revenue;
            var existingWithSameName = companyData?.Categories.Any(c =>
                c.Type == targetType &&
                c.Name.Equals(ModalCategoryName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (_editingCategory == null || c.Id != _editingCategory.Id)) ?? false;

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
