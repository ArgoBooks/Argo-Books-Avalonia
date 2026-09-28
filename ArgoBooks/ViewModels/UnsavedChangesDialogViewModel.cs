using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

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
/// ViewModel for the dialog that asks whether to save unsaved changes.
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

    /// <summary>
    /// Shows the dialog.
    /// </summary>
    /// <param name="title">Optional custom title.</param>
    /// <param name="message">Optional custom message.</param>
    /// <returns>The result indicating which button was clicked.</returns>
    public Task<UnsavedChangesResult> ShowAsync(string? title = null, string? message = null)
    {
        if (title != null) Title = title;
        if (message != null) Message = message;

        IsOpen = true;
        _completionSource = new TaskCompletionSource<UnsavedChangesResult>();
        return _completionSource.Task;
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
}
