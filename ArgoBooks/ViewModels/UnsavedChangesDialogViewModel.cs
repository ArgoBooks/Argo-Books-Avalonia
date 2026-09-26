using System.Collections.ObjectModel;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Represents a single change item in the unsaved changes list.
/// </summary>
public class ChangeItem
{
    /// <summary>
    /// Gets or sets the description of what changed.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of change (Added, Modified, Deleted).
    /// </summary>
    public ChangeType ChangeType { get; set; } = ChangeType.Modified;
}

/// <summary>
/// Type of change made to an item.
/// </summary>
public enum ChangeType
{
    Added,
    Modified,
    Deleted
}

/// <summary>
/// Represents a category of changes (e.g., Customers, Products).
/// </summary>
public partial class ChangeCategory : ObservableObject
{
    /// <summary>
    /// Gets or sets the name of the category.
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets the icon name for the category.
    /// </summary>
    [ObservableProperty]
    private string _iconName = "Folder";

    /// <summary>
    /// Gets or sets whether the category is expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Gets the collection of changes in this category.
    /// </summary>
    public ObservableCollection<ChangeItem> Changes { get; } = [];

    /// <summary>
    /// Gets the count of changes in this category.
    /// </summary>
    public int ChangeCount => Changes.Count;
}

/// <summary>
/// Result of the unsaved changes dialog.
/// </summary>
public enum UnsavedChangesResult
{
    None,
    Save,
    DontSave,
    Cancel
}

/// <summary>
/// ViewModel for the unsaved changes dialog that displays a list of all pending changes.
/// </summary>
public partial class UnsavedChangesDialogViewModel : ViewModelBase
{
    private TaskCompletionSource<UnsavedChangesResult>? _completionSource;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "Unsaved Changes".Translate();

    [ObservableProperty]
    private string _message = "You have unsaved changes. Would you like to save them before closing?".Translate();

    [ObservableProperty]
    private string _saveButtonText = "Save".Translate();

    [ObservableProperty]
    private string _dontSaveButtonText = "Don't Save".Translate();

    [ObservableProperty]
    private string _cancelButtonText = "Cancel".Translate();

    public ObservableCollection<ChangeCategory> Categories { get; } = [];

    /// <summary>
    /// Gets whether there are any changes to display.
    /// </summary>
    public bool HasChanges => Categories.Count > 0 && Categories.Any(c => c.Changes.Count > 0);

    /// <summary>
    /// Gets the total number of changes across all categories.
    /// </summary>
    public int TotalChangeCount => Categories.Sum(c => c.Changes.Count);

    /// <summary>
    /// Gets the localized text showing the total change count.
    /// </summary>
    public string UnsavedChangeCountText => "{0} unsaved change(s)".TranslateFormat(TotalChangeCount);

    /// <summary>
    /// Shows the dialog with the specified changes.
    /// </summary>
    /// <param name="categories">The change categories to display.</param>
    /// <param name="title">Optional custom title.</param>
    /// <param name="message">Optional custom message.</param>
    /// <returns>The result indicating which button was clicked.</returns>
    public Task<UnsavedChangesResult> ShowAsync(
        IEnumerable<ChangeCategory>? categories = null,
        string? title = null,
        string? message = null)
    {
        // Set custom text if provided
        if (title != null) Title = title;
        if (message != null) Message = message;

        Categories.Clear();
        if (categories != null)
        {
            foreach (var category in categories.Where(c => c.Changes.Count > 0))
            {
                Categories.Add(category);
            }
        }

        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(TotalChangeCount));
        OnPropertyChanged(nameof(UnsavedChangeCountText));

        IsOpen = true;
        _completionSource = new TaskCompletionSource<UnsavedChangesResult>();
        return _completionSource.Task;
    }

    /// <summary>
    /// Shows a simple unsaved changes dialog without a detailed list.
    /// </summary>
    /// <param name="title">Optional custom title.</param>
    /// <param name="message">Optional custom message.</param>
    /// <returns>The result indicating which button was clicked.</returns>
    public Task<UnsavedChangesResult> ShowSimpleAsync(
        string? title = null,
        string? message = null)
    {
        return ShowAsync(null, title, message);
    }

    [RelayCommand]
    private void Save()
    {
        IsOpen = false;
        _completionSource?.TrySetResult(UnsavedChangesResult.Save);
    }

    [RelayCommand]
    private void DontSave()
    {
        IsOpen = false;
        _completionSource?.TrySetResult(UnsavedChangesResult.DontSave);
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        _completionSource?.TrySetResult(UnsavedChangesResult.Cancel);
    }

    /// <summary>
    /// Closes the dialog without a result (e.g., when clicking backdrop).
    /// </summary>
    public void Close()
    {
        IsOpen = false;
        _completionSource?.TrySetResult(UnsavedChangesResult.None);
    }

    /// <summary>
    /// Toggles the expanded state of a category.
    /// </summary>
    [RelayCommand]
    private void ToggleCategory(ChangeCategory category)
    {
        category.IsExpanded = !category.IsExpanded;
    }

    /// <summary>
    /// Expands all categories.
    /// </summary>
    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var category in Categories)
        {
            category.IsExpanded = true;
        }
    }

    /// <summary>
    /// Collapses all categories.
    /// </summary>
    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var category in Categories)
        {
            category.IsExpanded = false;
        }
    }
}
