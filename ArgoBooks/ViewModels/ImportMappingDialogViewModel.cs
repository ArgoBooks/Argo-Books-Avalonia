using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Result of the import mapping review dialog.
/// </summary>
public enum ImportMappingDialogResult
{
    Cancel,
    Accept
}

/// <summary>
/// Read-only ViewModel for a sheet that cannot be imported due to an unrecognised type.
/// </summary>
public sealed class UnsupportedSheetViewModel
{
    private readonly SheetAnalysis _analysis;

    public UnsupportedSheetViewModel(SheetAnalysis analysis)
    {
        _analysis = analysis;
    }

    public string SourceSheetName => _analysis.SourceSheetName;
    public string Reason => _analysis.UnsupportedReason ?? string.Empty;
    public int RowCount => _analysis.RowCount;
    public string RowCountDisplay => $"{RowCount:N0} rows";
}

/// <summary>
/// ViewModel wrapper for a SheetAnalysis, making it observable for the UI.
/// </summary>
public partial class SheetAnalysisViewModel : ObservableObject
{
    private readonly SheetAnalysis _analysis;

    public SheetAnalysisViewModel(SheetAnalysis analysis)
    {
        _analysis = analysis;
        DetectedType = analysis.DetectedType;
        IsIncluded = analysis.IsIncluded;

        foreach (var mapping in analysis.ColumnMappings)
            ColumnMappings.Add(new ColumnMappingViewModel(mapping));

        foreach (var col in analysis.UnmappedSourceColumns)
            UnmappedSourceColumns.Add(col);

        foreach (var col in analysis.UnmappedTargetColumns)
            UnmappedTargetColumns.Add(col);
    }

    public string SourceSheetName => _analysis.SourceSheetName;
    public double Confidence => _analysis.Confidence;
    public ProcessingTier Tier => _analysis.Tier;
    public string TierReason => _analysis.TierReason;
    public int RowCount => _analysis.RowCount;
    public int MappedColumnCount => ColumnMappings.Count;
    public bool IsTier2 => Tier == ProcessingTier.Tier2_LlmProcessing;

    public string ConfidenceDisplay => $"Match: {Confidence:P0}";
    public string RowCountDisplay => $"{RowCount:N0} rows";

    public string TierDisplay => Tier == ProcessingTier.Tier1_Mapping
        ? "Direct Mapping"
        : "AI Processing";

    /// <summary>
    /// Confidence color category: high (>0.9), medium (0.7-0.9), low (&lt;0.7).
    /// </summary>
    public string ConfidenceLevel => Confidence switch
    {
        > 0.9 => "High",
        > 0.7 => "Medium",
        _ => "Low"
    };

    // Theme-aware badge styling keys off these booleans (see ImportMappingDialog.axaml styles),
    // so the badge colors come from the theme dictionaries rather than a theme-guessing converter.
    public bool IsHighConfidence => Confidence > 0.9;
    public bool IsMediumConfidence => Confidence is > 0.7 and <= 0.9;
    public bool IsLowConfidence => Confidence <= 0.7;

    [ObservableProperty]
    private SpreadsheetSheetType _detectedType;

    [ObservableProperty]
    private bool _isIncluded;

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<ColumnMappingViewModel> ColumnMappings { get; } = [];
    public ObservableCollection<string> UnmappedSourceColumns { get; } = [];
    public ObservableCollection<string> UnmappedTargetColumns { get; } = [];

    public bool HasUnmappedSource => UnmappedSourceColumns.Count > 0;
    public bool HasUnmappedTarget => UnmappedTargetColumns.Count > 0;

    /// <summary>
    /// Updates the underlying analysis object with any user changes.
    /// </summary>
    public void ApplyChanges()
    {
        // If the user reclassified the sheet, the AI-assigned Tier (and the Tier-2 row-processing
        // reasoning behind it) no longer applies to the new type. Route the user-asserted type
        // through deterministic direct mapping so the tier split and the currency scan, which both
        // key off Tier, stay consistent with the type the user chose.
        if (DetectedType != _analysis.DetectedType)
            _analysis.Tier = ProcessingTier.Tier1_Mapping;
        _analysis.DetectedType = DetectedType;
        _analysis.IsIncluded = IsIncluded;
    }

}

/// <summary>
/// ViewModel wrapper for a ColumnMapping.
/// </summary>
public partial class ColumnMappingViewModel : ObservableObject
{
    public ColumnMappingViewModel(ColumnMapping mapping)
    {
        SourceColumn = mapping.SourceColumn;
        TargetColumn = mapping.TargetColumn;
        Confidence = mapping.Confidence;
        TransformHint = mapping.TransformHint;
    }

    public string SourceColumn { get; }

    [ObservableProperty]
    private string _targetColumn = string.Empty;

    public double Confidence { get; }
    public string? TransformHint { get; }

    public string ConfidenceDisplay => $"Match: {Confidence:P0}";

    public string ConfidenceLevel => Confidence switch
    {
        > 0.9 => "High",
        > 0.7 => "Medium",
        _ => "Low"
    };

    public bool IsHighConfidence => Confidence > 0.9;
    public bool IsMediumConfidence => Confidence is > 0.7 and <= 0.9;
    public bool IsLowConfidence => Confidence <= 0.7;

}

/// <summary>
/// ViewModel for the Import Mapping Review Dialog.
/// Shows the AI-detected sheet types and column mappings for user review before import.
/// </summary>
public partial class ImportMappingDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private int _totalSheets;

    [ObservableProperty]
    private int _totalRows;

    [ObservableProperty]
    private int _totalMappedColumns;

    [ObservableProperty]
    private int _tier1SheetCount;

    [ObservableProperty]
    private int _tier2SheetCount;

    [ObservableProperty]
    private string _rateLimitDisplay = string.Empty;

    [ObservableProperty]
    private bool _showRateLimit;

    [ObservableProperty]
    private bool _skipExistingRecords;

    private bool _suppressSkipConfirmation;

    async partial void OnSkipExistingRecordsChanged(bool value)
    {
        if (value || _suppressSkipConfirmation)
            return;

        var dialog = App.ConfirmationDialog;
        if (dialog != null)
        {
            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Overwrite Existing Records?".Translate(),
                Message = "This may overwrite existing data in your company file with values from the spreadsheet. Are you sure?".Translate(),
                PrimaryButtonText = "Allow Overwrite".Translate(),
                CancelButtonText = "Keep Safe".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary)
            {
                _suppressSkipConfirmation = true;
                SkipExistingRecords = true;
                _suppressSkipConfirmation = false;
            }
        }
    }

    public ObservableCollection<SheetAnalysisViewModel> Sheets { get; } = [];

    /// <summary>
    /// Sheets that could not be matched to a supported Argo Books entity type.
    /// Displayed in a read-only "Cannot import" section in the dialog.
    /// </summary>
    public ObservableCollection<UnsupportedSheetViewModel> UnsupportedSheets { get; } = [];

    public bool HasUnsupportedSheets => UnsupportedSheets.Count > 0;

    /// <summary>
    /// True when at least one sheet can actually be imported. When false the file
    /// produced nothing importable (every sheet was unsupported), so the dialog
    /// becomes an informational "Close" rather than an "Accept &amp; Import".
    /// </summary>
    public bool HasImportableContent => Sheets.Count > 0;

    /// <summary>
    /// Primary button caption: an import action when there's content, otherwise a plain close.
    /// </summary>
    public string AcceptButtonText => HasImportableContent ? "Accept & Import" : "Close";

    /// <summary>
    /// Footer hint text, reflecting whether there's anything to import.
    /// </summary>
    public string FooterHint => HasImportableContent
        ? "Review the detected mappings above, then click Accept to proceed with import."
        : "There's nothing here that Argo Books can import. The sheets above don't match a supported data type and will be skipped.";

    private TaskCompletionSource<ImportMappingDialogResult>? _completionSource;
    private SpreadsheetAnalysisResult? _analysisResult;

    /// <summary>
    /// Shows the dialog with the analysis result for user review.
    /// </summary>
    public Task<ImportMappingDialogResult> ShowAsync(
        SpreadsheetAnalysisResult analysis,
        int remainingImports = -1,
        int maxImports = -1)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        _analysisResult = analysis;

        // Clear previous state
        Sheets.Clear();
        UnsupportedSheets.Clear();
        SkipExistingRecords = true;

        FileName = analysis.FileName;

        // Populate sheets: supported sheets go to the main list, unsupported to their own list
        foreach (var sheet in analysis.Sheets)
        {
            if (sheet.UnsupportedReason != null)
                UnsupportedSheets.Add(new UnsupportedSheetViewModel(sheet));
            else
                Sheets.Add(new SheetAnalysisViewModel(sheet));
        }

        // Calculate summary stats (exclude unsupported sheets from the totals)
        var supportedSheets = analysis.Sheets.Where(s => s.UnsupportedReason == null).ToList();
        TotalSheets = supportedSheets.Count;
        TotalRows = supportedSheets.Sum(s => s.RowCount);
        TotalMappedColumns = supportedSheets.Sum(s => s.ColumnMappings.Count);
        Tier1SheetCount = supportedSheets.Count(s => s.Tier == ProcessingTier.Tier1_Mapping);
        Tier2SheetCount = supportedSheets.Count(s => s.Tier == ProcessingTier.Tier2_LlmProcessing);

        // Rate limit display
        if (remainingImports >= 0 && maxImports > 0)
        {
            RateLimitDisplay = $"{remainingImports}/{maxImports} remaining this month";
            ShowRateLimit = true;
        }
        else
        {
            ShowRateLimit = false;
        }

        OnPropertyChanged(nameof(HasUnsupportedSheets));
        OnPropertyChanged(nameof(HasImportableContent));
        OnPropertyChanged(nameof(AcceptButtonText));
        OnPropertyChanged(nameof(FooterHint));

        IsOpen = true;
        _completionSource = new TaskCompletionSource<ImportMappingDialogResult>();
        return _completionSource.Task;
    }

    /// <summary>
    /// Gets the updated analysis result after user may have made changes.
    /// </summary>
    public SpreadsheetAnalysisResult? GetUpdatedAnalysis()
    {
        if (_analysisResult == null) return null;

        foreach (var sheetVm in Sheets)
        {
            sheetVm.ApplyChanges();
        }

        return _analysisResult;
    }

    [RelayCommand]
    private async Task RequestCloseAsync()
    {
        var dialog = App.ConfirmationDialog;
        if (dialog != null)
        {
            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Cancel Import?".Translate(),
                Message = "Are you sure you want to cancel? The analysis results will be lost.".Translate(),
                PrimaryButtonText = "Cancel Import".Translate(),
                CancelButtonText = "Continue Reviewing".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary)
                return;
        }

        IsOpen = false;
        _completionSource?.TrySetResult(ImportMappingDialogResult.Cancel);
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        _completionSource?.TrySetResult(ImportMappingDialogResult.Cancel);
    }

    [RelayCommand]
    private void Accept()
    {
        // Nothing importable: the primary button is just a "Close", so don't kick off an import.
        if (!HasImportableContent)
        {
            IsOpen = false;
            _completionSource?.TrySetResult(ImportMappingDialogResult.Cancel);
            return;
        }

        // Apply any user edits back to the analysis
        foreach (var sheetVm in Sheets)
        {
            sheetVm.ApplyChanges();
        }

        IsOpen = false;
        _completionSource?.TrySetResult(ImportMappingDialogResult.Accept);
    }
}
