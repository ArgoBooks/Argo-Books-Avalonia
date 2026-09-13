using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks;

/// <summary>
/// Whole-file AI rescue fallback for spreadsheet imports, split out of App.axaml.cs to keep
/// that file focused. These are members of the same partial App class; behavior is unchanged.
/// </summary>
public partial class App
{
    /// <summary>
    /// Runs the whole-file AI rescue pass when the normal analyzer could not recognize the file.
    /// Either extracts records and shows the normal result dialog, or shows the vetted rejection
    /// message for the resolved reason code.
    /// </summary>
    private static async Task TryRescueImportAsync(
        string filePath,
        bool isCsv,
        CompanyData companyData,
        SpreadsheetAnalysisService analysisService,
        SpreadsheetImportService importService,
        string originalFileName,
        AiImportUsageService usageService)
    {
        using var rescueCts = new CancellationTokenSource();
        _mainWindowViewModel?.ShowLoading(
            "AI processing...".Translate(),
            "Trying to organize the file...".Translate(),
            0, rescueCts, ConfirmCancelAsync);

        // RescueAsync reports the row total once, up front (the file total), and then per-sheet totals
        // during extraction. Capture the first report's total (the stable file-wide figure) so the large
        // file warning doesn't flip back off once a multi-sheet file's smaller per-sheet totals arrive.
        int? fileTotal = null;
        var rescueProgress = new Progress<(int processed, int total)>(p =>
        {
            fileTotal ??= p.total;
            var detail = fileTotal > SpreadsheetAnalysisService.RescueLargeFileWarnRows
                ? "This is a large file, this may take a while...".Translate()
                : "Trying to organize the file...".Translate();
            _mainWindowViewModel?.ShowLoading(
                "AI processing...".Translate(), detail, cts: rescueCts, cancelConfirmation: ConfirmCancelAsync);
        });

        ImportRescueResult rescue;
        try
        {
            rescue = await Task.Run(
                () => analysisService.RescueAsync(filePath, isCsv, rescueProgress, rescueCts.Token),
                rescueCts.Token);
        }
        catch (OperationCanceledException)
        {
            _mainWindowViewModel?.HideLoading();
            return;
        }

        await Task.Yield();
        _mainWindowViewModel?.HideLoading();

        // Nothing importable: show the vetted message for the resolved reason code (never raw AI text).
        if (rescue.Outcome == ImportRescueOutcome.Rejected)
        {
            await ShowInfoMessageBoxAsync("Import".Translate(), ImportRescueMessages.ForReason(rescue.ReasonCode));
            return;
        }

        // Commit extracted entities, mirroring the Tier-2 commit path.
        var importStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = CreateCompanyDataSnapshot(companyData);
        var queuedBeforeImport = companyData.PendingConversions.ToHashSet();
        var importOptions = new ImportOptions();

        using var importCts = new CancellationTokenSource();
        _mainWindowViewModel?.ShowLoading("Importing data...".Translate(), cts: importCts, cancelConfirmation: ConfirmCancelAsync);

        var sheetResults = new List<SheetImportResult>();
        foreach (var extraction in rescue.Extractions)
        {
            var result = importService.ImportProcessedEntities(
                companyData, extraction.ProcessedData, extraction.SheetName, importOptions);
            sheetResults.Add(result);
        }

        await importService.AiCategorizeMissingProductsAsync(companyData, importCts.Token);
        await Task.Yield();
        _mainWindowViewModel?.HideLoading();

        MirrorQueuedConversions(companyData, companyData.PendingConversions
            .Where(p => !queuedBeforeImport.Contains(p)).Select(p => p.TransactionId));

        var importedSnapshot = CreateCompanyDataSnapshot(companyData);
        void RestoreImportSnapshotAndRefresh(string snapshotJson)
        {
            RestoreCompanyDataFromSnapshot(companyData, snapshotJson);
            CompanyManager?.MarkAsChanged();
            _bankMatchingPageViewModel?.Reload();
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => NavigationService?.RefreshCurrentPage(),
                Avalonia.Threading.DispatcherPriority.Background);
        }

        UndoRedoManager.RecordAction(new DelegateAction(
            "AI import spreadsheet data".Translate(),
            () => RestoreImportSnapshotAndRefresh(snapshot),
            () => RestoreImportSnapshotAndRefresh(importedSnapshot)));

        CompanyManager?.MarkAsChanged();
        ChartSettingsService.Instance.SelectedDateRange = "All Time";

        var totalImported = sheetResults.Sum(s => s.Inserted);
        var totalUpdated = sheetResults.Sum(s => s.Updated);
        var totalSkipped = sheetResults.Sum(s => s.Skipped);
        var totalProcessed = totalImported + totalUpdated;
        var totalBankRouted = sheetResults.Sum(s => s.BankMatchingImported);

        var allWarnings = sheetResults.SelectMany(s => s.Warnings).Distinct().ToList();
        var allSkipReasons = sheetResults
            .SelectMany(s => s.SkipReasons)
            .GroupBy(r => r)
            .Select(g => g.Count() > 1 ? $"{g.Key} (×{g.Count()})" : g.Key)
            .ToList();
        var allUnimported = sheetResults.SelectMany(s => s.UnimportedRows).ToList();

        importStopwatch.Stop();
        _ = TelemetryManager?.TrackFeatureAsync(
            FeatureName.DataImported,
            isCsv ? "ai-csv-rescue" : "ai-xlsx-rescue",
            importStopwatch.ElapsedMilliseconds);

        // Only a run that brought something in uses up an import.
        if (totalProcessed > 0 || totalBankRouted > 0)
            await usageService.IncrementUsageAsync();

        var resultDialog = _appShellViewModel?.ImportResultDialogViewModel;
        if (resultDialog != null)
        {
            await resultDialog.ShowAsync(
                originalFileName,
                sheetResults,
                totalImported, totalUpdated, totalSkipped,
                allSkipReasons, allWarnings,
                totalProcessed > 0 || totalBankRouted > 0,
                allUnimported);
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => NavigationService?.RefreshCurrentPage(),
            Avalonia.Threading.DispatcherPriority.Background);
    }
}
