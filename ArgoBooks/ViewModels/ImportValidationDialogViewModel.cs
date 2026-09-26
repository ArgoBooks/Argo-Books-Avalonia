using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Result of the import validation dialog.
/// </summary>
public enum ImportValidationDialogResult
{
    Cancel,
    ImportAnyway,
    CreateMissingAndImport
}

/// <summary>
/// Group of validation issues for a single sheet.
/// </summary>
public partial class SheetIssueGroup : ObservableObject
{
    [ObservableProperty]
    private string _sheetName = string.Empty;

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<ValidationIssueViewModel> Issues { get; } = [];

    public int IssueCount => Issues.Count;
}

/// <summary>
/// ViewModel wrapper for a ValidationIssue.
/// </summary>
public class ValidationIssueViewModel(ValidationIssue issue) : ObservableObject
{
    public int RowNumber => issue.RowNumber;
    public string ColumnName => issue.ColumnName;
    public string InvalidValue => issue.InvalidValue;
    public string Description => issue.Description;
    public string ReferenceType => issue.ReferenceType;
    public string RowId => issue.RowId;
    public bool IsWarning => issue.Severity == ValidationIssueSeverity.Warning;
    public bool IsAutoFixable => issue.IsAutoFixable;

    public string DisplayValue => string.IsNullOrEmpty(InvalidValue) ? "(empty)" : InvalidValue;

}

/// <summary>
/// ViewModel for the Import Validation Dialog.
/// </summary>
public partial class ImportValidationDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "Import Validation";

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool _hasErrors;

    [ObservableProperty]
    private bool _hasWarnings;

    [ObservableProperty]
    private bool _hasMissingReferences;

    [ObservableProperty]
    private bool _hasIssues;

    [ObservableProperty]
    private int _totalIssues;

    [ObservableProperty]
    private int _totalMissingReferences;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool _hasNonAutoFixableIssues;

    [ObservableProperty]
    private int _autoFixableCount;

    [ObservableProperty]
    private int _nonAutoFixableCount;

    public ObservableCollection<SheetIssueGroup> SheetGroups { get; } = [];

    public ObservableCollection<string> GeneralErrors { get; } = [];

    public ObservableCollection<string> GeneralWarnings { get; } = [];

    private TaskCompletionSource<ImportValidationDialogResult>? _completionSource;

    /// <summary>
    /// Shows the dialog with the validation result.
    /// </summary>
    public Task<ImportValidationDialogResult> ShowAsync(ImportValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        // Clear previous state
        SheetGroups.Clear();
        GeneralErrors.Clear();
        GeneralWarnings.Clear();

        // Populate general errors
        foreach (var error in validationResult.Errors)
        {
            GeneralErrors.Add(error);
        }

        // Populate general warnings (legacy)
        foreach (var warning in validationResult.Warnings)
        {
            GeneralWarnings.Add(warning);
        }

        // Group issues by sheet, but only show issues the user must act on. Auto-fixable
        // missing references are created silently during import, so we never surface them.
        var issuesBySheet = validationResult.GetIssuesBySheet();
        foreach (var (sheetName, issues) in issuesBySheet.OrderBy(g => g.Key))
        {
            var manualIssues = issues.Where(i => !i.IsAutoFixable).OrderBy(i => i.RowNumber).ToList();
            if (manualIssues.Count == 0)
                continue;

            var group = new SheetIssueGroup { SheetName = sheetName };
            foreach (var issue in manualIssues)
            {
                group.Issues.Add(new ValidationIssueViewModel(issue));
            }
            SheetGroups.Add(group);
        }

        // Update summary
        HasErrors = validationResult.Errors.Count > 0;
        HasWarnings = validationResult.Warnings.Count > 0 || validationResult.Issues.Any(i => i.Severity == ValidationIssueSeverity.Warning);
        HasMissingReferences = validationResult.HasMissingReferences;
        TotalIssues = validationResult.Issues.Count + validationResult.Errors.Count + validationResult.Warnings.Count;
        TotalMissingReferences = validationResult.TotalMissingReferences;
        HasIssues = TotalIssues > 0;
        HasNonAutoFixableIssues = validationResult.HasNonAutoFixableIssues;
        AutoFixableCount = validationResult.AutoFixableIssueCount;
        NonAutoFixableCount = validationResult.NonAutoFixableIssueCount;

        // Build summary message. Auto-fixable references are handled silently, so the
        // dialog only ever speaks to errors and issues that need a manual spreadsheet fix.
        var summaryParts = new List<string>();
        if (HasErrors)
        {
            summaryParts.Add($"{validationResult.Errors.Count} error(s) that prevent import");
        }
        if (NonAutoFixableCount > 0)
        {
            summaryParts.Add($"{NonAutoFixableCount} issue(s) that require fixing the spreadsheet");
        }
        Summary = summaryParts.Count > 0
            ? string.Join(", ", summaryParts) + "."
            : "No issues found.";

        Title = HasErrors
            ? "Import Cannot Continue"
            : HasNonAutoFixableIssues
                ? "Spreadsheet Needs Fixing"
                : "Import Issues Found";

        IsOpen = true;
        _completionSource = new TaskCompletionSource<ImportValidationDialogResult>();
        return _completionSource.Task;
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        _completionSource?.TrySetResult(ImportValidationDialogResult.Cancel);
    }

    [RelayCommand]
    private async Task RequestCloseAsync()
    {
        var dialog = App.ConfirmationDialog;
        if (dialog != null)
        {
            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Close Import Issues?".Translate(),
                Message = "Are you sure you want to close? The import will be cancelled.".Translate(),
                PrimaryButtonText = "Close".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary)
                return;
        }

        Cancel();
    }

    [RelayCommand]
    private void ToggleSheetGroup(SheetIssueGroup group)
    {
        group.IsExpanded = !group.IsExpanded;
    }

    [RelayCommand]
    private void ImportAnyway()
    {
        IsOpen = false;
        _completionSource?.TrySetResult(ImportValidationDialogResult.ImportAnyway);
    }

    [RelayCommand]
    private void CreateMissingAndImport()
    {
        IsOpen = false;
        _completionSource?.TrySetResult(ImportValidationDialogResult.CreateMissingAndImport);
    }

    /// <summary>
    /// Gets whether the import can proceed (no critical errors).
    /// When there are non-auto-fixable issues, the user will need to fix the spreadsheet first.
    /// </summary>
    public bool CanImport => !HasErrors && !HasNonAutoFixableIssues;
}
