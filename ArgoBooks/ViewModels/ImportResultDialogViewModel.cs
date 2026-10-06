using System.Collections.ObjectModel;
using System.Text;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Represents a single sheet's import result for display.
/// </summary>
public class SheetResultItem
{
    public required string DisplayLabel { get; init; }
    public int Inserted { get; init; }
    public int Updated { get; init; }
    public int Skipped { get; init; }
    public int Imported { get; init; }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            // Whole phrases, not a number glued to a word. A language that puts the count after
            // the noun cannot be served by the latter, and a bare word collides with the tile
            // heading above, which is the same word with a capital.
            if (Inserted > 0) parts.Add("{0} new".TranslateFormat(Inserted.ToString("N0")));
            if (Updated > 0) parts.Add("{0} updated".TranslateFormat(Updated.ToString("N0")));
            if (Imported > 0) parts.Add("{0} imported".TranslateFormat(Imported.ToString("N0")));
            if (Skipped > 0) parts.Add("{0} skipped".TranslateFormat(Skipped.ToString("N0")));
            return string.Join(", ", parts);
        }
    }
}

/// <summary>
/// ViewModel for the Import Result Dialog.
/// </summary>
public partial class ImportResultDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private int _totalNew;

    [ObservableProperty]
    private int _totalUpdated;

    [ObservableProperty]
    private int _totalSkipped;

    [ObservableProperty]
    private bool _hasUpdated;

    [ObservableProperty]
    private bool _hasSkipped;

    [ObservableProperty]
    private bool _hasMultipleSheets;

    [ObservableProperty]
    private bool _hasSkipReasons;

    [ObservableProperty]
    private bool _hasWarnings;

    [ObservableProperty]
    private bool _needsSave;

    [ObservableProperty]
    private bool _hasUnimportedRows;

    public ObservableCollection<SheetResultItem> SheetResults { get; } = [];
    public ObservableCollection<string> SkipReasons { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<UnimportedRow> UnimportedRows { get; } = [];

    private TaskCompletionSource? _completionSource;

    public Task ShowAsync(
        string fileName,
        List<SheetImportResult> sheetResults,
        int totalNew,
        int totalUpdated,
        int totalSkipped,
        List<string> skipReasons,
        List<string> warnings,
        bool needsSave,
        List<UnimportedRow>? unimportedRows = null)
    {
        // Clear previous state
        SheetResults.Clear();
        SkipReasons.Clear();
        Warnings.Clear();
        UnimportedRows.Clear();

        FileName = fileName;
        TotalNew = totalNew;
        TotalUpdated = totalUpdated;
        TotalSkipped = totalSkipped;
        HasUpdated = totalUpdated > 0;
        HasSkipped = totalSkipped > 0;
        NeedsSave = needsSave;
        IsSuccess = totalSkipped == 0 && warnings.Count == 0;

        // Build per-sheet results
        foreach (var sr in sheetResults)
        {
            if (sr.Inserted == 0 && sr.Updated == 0 && sr.Skipped == 0 && sr.BankMatchingImported == 0) continue;

            var label = string.Equals(sr.SheetName, sr.EntityType, StringComparison.OrdinalIgnoreCase)
                ? sr.SheetName
                : $"{sr.SheetName} \u2192 {sr.EntityType}";

            SheetResults.Add(new SheetResultItem
            {
                DisplayLabel = label,
                Inserted = sr.Inserted,
                Updated = sr.Updated,
                Skipped = sr.Skipped,
                Imported = sr.BankMatchingImported
            });
        }
        HasMultipleSheets = SheetResults.Count > 1;

        // Skip reasons (capped at 10)
        foreach (var reason in skipReasons.Take(10))
            SkipReasons.Add(reason);
        if (skipReasons.Count > 10)
            SkipReasons.Add("and {0} more".TranslateFormat(skipReasons.Count - 10) + "...");
        HasSkipReasons = SkipReasons.Count > 0;

        // Warnings
        foreach (var warning in warnings)
            Warnings.Add(warning);
        HasWarnings = Warnings.Count > 0;

        if (unimportedRows != null)
        {
            foreach (var row in unimportedRows)
                UnimportedRows.Add(row);
        }
        HasUnimportedRows = UnimportedRows.Count > 0;

        IsOpen = true;
        _completionSource = new TaskCompletionSource();
        return _completionSource.Task;
    }

    /// <summary>
    /// Builds CSV text for all unimported rows with header Sheet,Row,Reason,Value.
    /// Fields are RFC-4180 quoted when they contain commas, double-quotes, or newlines.
    /// </summary>
    public string BuildUnimportedCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sheet,Row,Reason,Value");
        foreach (var row in UnimportedRows)
        {
            sb.Append(CsvQuote(row.Sheet));
            sb.Append(',');
            sb.Append(row.RowNumber == 0 ? "" : row.RowNumber.ToString());
            sb.Append(',');
            sb.Append(CsvQuote(row.Reason));
            sb.Append(',');
            sb.AppendLine(CsvQuote(row.RawValue ?? ""));
        }
        return sb.ToString();

        static string CsvQuote(string field) => CsvWriter.QuoteField(field);
    }

    [RelayCommand]
    private async Task ExportUnimported()
    {
        try
        {
            var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            if (topLevel?.StorageProvider == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Rows That Could Not Be Imported",
                SuggestedFileName = "unimported-rows.csv",
                DefaultExtension = "csv",
                FileTypeChoices = [new FilePickerFileType("CSV file") { Patterns = ["*.csv"] }]
            });

            if (file == null) return;

            var csv = BuildUnimportedCsv();
            await File.WriteAllTextAsync(file.Path.LocalPath, csv, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Export, "ExportUnimported");
        }
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        _completionSource?.TrySetResult();
    }
}
