using System.Collections.ObjectModel;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Services;
using ArgoBooks.Helpers;
using ArgoBooks.Services;
using ArgoBooks.Localization;
using ArgoBooks.Controls;
using ArgoBooks.Utilities;
using ArgoBooks.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Receipts page displaying receipt archive management.
/// </summary>
public partial class ReceiptsPageViewModel : SortablePageViewModelBase
{
    #region Statistics

    [ObservableProperty]
    private int _totalReceipts;

    [ObservableProperty]
    private int _expenseReceipts;

    [ObservableProperty]
    private int _revenueReceipts;

    [ObservableProperty]
    private int _aiScannedReceipts;

    /// <summary>
    /// This month's scan allowance, as "3 of 10 scans used".
    ///
    /// Distinct from the AI Scanned stat card, which counts receipts in this company file that
    /// were scanned at any time. This is the monthly allowance the server meters, which is what
    /// actually decides whether the next scan works.
    ///
    /// Empty rather than "0 of 0" while it is unknown, and the label hides itself: an offline
    /// moment showing zero remaining would read as an exhausted allowance.
    /// </summary>
    [ObservableProperty]
    private string _scanUsage = string.Empty;

    #endregion

    #region Plan Status

    [ObservableProperty]
    private bool _hasPremium;

    #endregion

    #region View Mode

    [ObservableProperty]
    private bool _isGridView = true;

    [ObservableProperty]
    private bool _isListView;

    partial void OnIsGridViewChanged(bool value)
    {
        if (value) IsListView = false;
        if (value) ColumnWidths.NeedsHorizontalScroll = false;
    }

    partial void OnIsListViewChanged(bool value)
    {
        if (value) IsGridView = false;
    }

    [RelayCommand]
    private void SetGridView()
    {
        IsGridView = true;
    }

    [RelayCommand]
    private void SetListView()
    {
        IsListView = true;
    }

    #endregion

    #region Search and Filter

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            FilterReceipts();
        });

    #endregion

    #region Selection

    [ObservableProperty]
    private bool _isSelectionMode;

    [ObservableProperty]
    private bool _hasSelectedReceipts;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _isAllSelected;

    partial void OnIsSelectionModeChanged(bool value)
    {
        // Clear selection when exiting selection mode
        if (!value)
        {
            foreach (var receipt in Receipts)
            {
                receipt.IsSelected = false;
            }
            UpdateSelectionState();
        }
    }

    partial void OnIsAllSelectedChanged(bool value)
    {
        foreach (var receipt in Receipts)
        {
            receipt.IsSelected = value;
        }
        UpdateSelectionState();
    }

    [RelayCommand]
    private void ExitSelectionMode()
    {
        IsSelectionMode = false;
    }

    [RelayCommand]
    private void ToggleReceiptSelection(ReceiptDisplayItem? receipt)
    {
        if (receipt == null) return;
        receipt.IsSelected = !receipt.IsSelected;
        UpdateSelectionState();
    }

    #endregion

    #region Receipts Collection

    private readonly List<Receipt> _allReceipts = [];
    private CancellationTokenSource? _imageLoadCts;

    public BatchObservableCollection<ReceiptDisplayItem> Receipts { get; } = [];

    #endregion

    #region Pagination

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => FilterReceipts();

    #endregion

    #region Preview Modal

    [RelayCommand]
    private void OpenPreview(ReceiptDisplayItem? receipt)
    {
        if (receipt == null) return;

        var title = $"Receipt #{receipt.Id}";
        if (!string.IsNullOrEmpty(receipt.Supplier))
            title += $"\n{receipt.Supplier}";

        App.ReceiptViewerModal?.Show(receipt.Id, title);
    }

    #endregion

    #region Column Management

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    public ReceiptsTableColumnWidths ColumnWidths => App.ReceiptsColumnWidths;

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("Receipts", new Dictionary<string, bool>
    {
        ["Id"] = true,
        ["Supplier"] = true,
        ["Date"] = true,
        ["Type"] = true,
        ["Amount"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showIdColumn = ColumnDefaults.Load("Id");

    [ObservableProperty]
    private bool _showSupplierColumn = ColumnDefaults.Load("Supplier");

    [ObservableProperty]
    private bool _showDateColumn = ColumnDefaults.Load("Date");

    [ObservableProperty]
    private bool _showTypeColumn = ColumnDefaults.Load("Type");

    [ObservableProperty]
    private bool _showAmountColumn = ColumnDefaults.Load("Amount");

    #endregion

    #region AI Scan State

    [ObservableProperty]
    private bool _isDragOver;

    [ObservableProperty]
    private bool _isScannerConfigured;

    /// <summary>
    /// Event raised when a file needs to be scanned via file picker.
    /// The view handles the file picker and passes the result.
    /// </summary>
    public event EventHandler? ScanFileRequested;

    partial void OnHasPremiumChanged(bool value)
    {
        CheckScannerConfiguration();
    }

    private void CheckScannerConfiguration()
    {
        IsScannerConfigured = PortalSettings.IsConfigured;
    }

    /// <summary>
    /// Called by the view when files are dropped on the receipts page.
    /// </summary>
    public async Task HandleFilesDroppedAsync(IEnumerable<string> filePaths)
    {
        var validPaths = filePaths
            .Where(FilePickerTypes.IsSupportedReceiptFile)
            .ToList();

        if (validPaths.Count == 0)
        {
            await App.ShowWarningMessageBoxAsync(
                Loc.Tr("Invalid File"),
                Loc.Tr("Please drop {0} files.", FilePickerTypes.SupportedReceiptFormats));
            return;
        }

        // Open bulk drop zone and add the files
        var modalsVm = App.ReceiptsModalsViewModel;
        if (modalsVm == null) return;

        if (!modalsVm.IsBulkDropZoneOpen)
            modalsVm.OpenBulkDropZone();

        modalsVm.AddFilesToQueue(validPaths);
    }

    #endregion

    #region Constructor

    public ReceiptsPageViewModel()
    {
        // Default to match ArgoTable's PageSizeOptions
        PageSize = 25;

        SortColumn = "Date";
        SortDirection = SortDirection.Descending;

        LoadReceipts();
        CheckScannerConfiguration();

        EnableDeferredUndoRefresh(p => p == PageNames.Receipts, LoadReceipts);

        // Subscribe to filter modal events
        if (App.ReceiptsModalsViewModel != null)
        {
            App.ReceiptsModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.ReceiptsModalsViewModel.FiltersCleared += OnFiltersCleared;
        }
    }

    /// <summary>
    /// Unsubscribes from app-level and singleton events, plus the per-item PropertyChanged handlers,
    /// so this page VM can be garbage collected when the company is switched. Called by
    /// ClearPageCaches via <see cref="ICleanupViewModel"/>.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.ReceiptsModalsViewModel != null)
        {
            App.ReceiptsModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.ReceiptsModalsViewModel.FiltersCleared -= OnFiltersCleared;
        }
        foreach (var item in Receipts)
            item.PropertyChanged -= OnReceiptItemPropertyChanged;
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        ActiveFilterCount = App.ReceiptsModalsViewModel?.ActiveFilterCount ?? 0;
        CurrentPage = 1;
        FilterReceipts();
    }

    protected override void ClearTableFilters() => App.ReceiptsModalsViewModel?.ClearFiltersCommand.Execute(null);

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        ActiveFilterCount = 0;
        SearchQuery = null;
        CurrentPage = 1;
        FilterReceipts();
    }

    #endregion

    #region Data Loading

    private async void LoadReceipts()
    {
        // Receipts load in the background after a company opens; make sure they're
        // merged before reading them. EnsureReceiptsLoadedAsync completes synchronously
        // once merged, so this only actually waits on the first call right after open.
        var manager = App.CompanyManager;
        if (manager != null)
        {
            try { await manager.EnsureReceiptsLoadedAsync(); }
            catch { /* fall through and show whatever is available */ }
        }

        _allReceipts.Clear();
        Receipts.Clear();

        var companyData = manager?.CompanyData;
        if (companyData?.Receipts == null)
            return;

        _allReceipts.AddRange(companyData.Receipts);
        UpdateStatistics();
        FilterReceipts();
    }

    private void UpdateStatistics()
    {
        TotalReceipts = _allReceipts.Count;
        ExpenseReceipts = _allReceipts.Count(r => r.TransactionType == "Expense");
        RevenueReceipts = _allReceipts.Count(r => r.TransactionType == "Revenue");
        AiScannedReceipts = _allReceipts.Count(r => r.IsAiScanned);

        _ = RefreshScanUsageAsync();
    }

    /// <summary>
    /// Reads this month's allowance from the server.
    ///
    /// Deliberately not awaited by the caller: the receipt list must not wait on a network call
    /// to render, and the card fills in a moment later. A failure leaves the dash rather than
    /// reporting a number nobody can trust.
    /// </summary>
    private async Task RefreshScanUsageAsync()
    {
        try
        {
            // Disposed each time: this constructor builds its own HttpClient and owns it, so a
            // fresh one per refresh with no dispose leaks a socket handle per page load.
            using var usageService = new ReceiptUsageService(App.LicenseService, App.ErrorLogger);
            var usage = await usageService.CheckUsageAsync();

            if (usage.MonthlyLimit > 0)
            {
                ScanUsage = "{0} of {1} scans used".TranslateFormat(usage.ScanCount, usage.MonthlyLimit);
                return;
            }

            ScanUsage = string.Empty;

            // Every failure path in CheckUsageAsync returns a zero limit, so a hidden label and a
            // genuine "no allowance configured" look identical from here. Recorded rather than
            // swallowed.
            App.ErrorLogger?.LogWarning(
                $"Scan usage unavailable: {usage.ErrorMessage ?? "no limit returned"}",
                "ReceiptsPageViewModel.RefreshScanUsageAsync",
                Core.Models.Telemetry.ErrorCategory.Api,
                "ScanUsageUnavailable");
        }
        catch (Exception ex)
        {
            // Not worth an error dialog on a page the user opened to look at receipts, but it
            // must not vanish either.
            ScanUsage = string.Empty;
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Api, "Scan usage lookup");
        }
    }

    [RelayCommand]
    private void RefreshReceipts()
    {
        LoadReceipts();
    }

    private void FilterReceipts()
    {
        IEnumerable<Receipt> filtered = _allReceipts;

        // Get filter values from modals view model
        var modals = App.ReceiptsModalsViewModel;
        var filterType = modals?.FilterType ?? "All";
        var filterSource = modals?.FilterSource ?? "All";
        var filterFileType = modals?.FilterFileType ?? "All";
        var filterAmountMin = modals?.FilterAmountMin;
        var filterAmountMax = modals?.FilterAmountMax;
        var filterDateFrom = modals?.FilterDateFrom;
        var filterDateTo = modals?.FilterDateTo;

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered.RankBySearch(SearchQuery, r => [r.Id, r.Supplier, r.FileName, r.TransactionId]);
        }

        if (filterType != "All")
        {
            filtered = filtered.Where(r => r.TransactionType == filterType);
        }

        if (filterSource != "All")
        {
            filtered = filterSource switch
            {
                "AI Scanned" => filtered.Where(r => r.IsAiScanned),
                "Manual" => filtered.Where(r => !r.IsAiScanned),
                _ => filtered
            };
        }

        if (filterFileType != "All")
        {
            filtered = filterFileType switch
            {
                "Image" => filtered.Where(r => IsImageFile(r.FileType)),
                "PDF" => filtered.Where(r => r.FileType.Contains("pdf", StringComparison.OrdinalIgnoreCase)),
                _ => filtered
            };
        }

        if (decimal.TryParse(filterAmountMin, out var minAmount))
        {
            filtered = filtered.Where(r => r.Amount >= minAmount);
        }
        if (decimal.TryParse(filterAmountMax, out var maxAmount))
        {
            filtered = filtered.Where(r => r.Amount <= maxAmount);
        }

        if (filterDateFrom.HasValue)
        {
            filtered = filtered.Where(r => r.Date >= filterDateFrom.Value.DateTime);
        }
        if (filterDateTo.HasValue)
        {
            filtered = filtered.Where(r => r.Date <= filterDateTo.Value.DateTime);
        }

        // Sorted on the receipt itself, not the row: rows are built after paging so the page
        // only pays for the images it shows.
        var sortedFiltered = filtered.ToList().ApplySort(
            SortColumn,
            SortDirection,
            new Dictionary<string, Func<Receipt, object?>>
            {
                ["Id"] = r => r.Id,
                ["Type"] = r => r.TransactionType,
                ["Supplier"] = r => r.Supplier,
                ["Date"] = r => r.Date,
                ["Amount"] = r => r.Amount
            },
            r => r.Date);

        // Paginate BEFORE creating display items, only process the visible page
        var pagedReceipts = Paginate(sortedFiltered, "receipt");

        // Create display items with cached image paths (no file I/O for cache hits)
        var displayItems = pagedReceipts.Select(receipt => new ReceiptDisplayItem
        {
            Id = receipt.Id,
            TransactionId = receipt.TransactionId,
            TransactionType = receipt.TransactionType,
            FileName = receipt.FileName,
            FileType = receipt.FileType,
            FileSize = receipt.FileSize,
            Amount = receipt.Amount,
            Date = receipt.Date,
            Supplier = receipt.Supplier,
            Source = receipt.Source,
            IsAiScanned = receipt.IsAiScanned,
            CreatedAt = receipt.CreatedAt,
            ImagePath = GetCachedReceiptImagePath(receipt),
            PageCount = ReceiptPageRenderer.CachedPageCount(receipt)
        }).ToList();

        // Unsubscribe from previous receipt items before replacing
        foreach (var oldItem in Receipts)
        {
            oldItem.PropertyChanged -= OnReceiptItemPropertyChanged;
        }

        foreach (var item in displayItems)
        {
            item.PropertyChanged += OnReceiptItemPropertyChanged;
        }

        Receipts.ReplaceAll(displayItems);

        // Generate images async for cache misses (off the UI thread)
        _ = LoadMissingImagesAsync(displayItems, pagedReceipts);
    }

    private void OnReceiptItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReceiptDisplayItem.IsSelected))
        {
            UpdateSelectionState();
        }
    }

    private static bool IsImageFile(string fileType)
    {
        return fileType.Contains("image", StringComparison.OrdinalIgnoreCase) ||
               fileType.EndsWith("jpg", StringComparison.OrdinalIgnoreCase) ||
               fileType.EndsWith("jpeg", StringComparison.OrdinalIgnoreCase) ||
               fileType.EndsWith("png", StringComparison.OrdinalIgnoreCase) ||
               fileType.EndsWith("gif", StringComparison.OrdinalIgnoreCase) ||
               fileType.EndsWith("webp", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSelectionState()
    {
        SelectedCount = Receipts.Count(r => r.IsSelected);
        HasSelectedReceipts = SelectedCount > 0;

        // Auto-enter selection mode when items are selected (e.g., via checkbox)
        if (HasSelectedReceipts && !IsSelectionMode)
        {
            IsSelectionMode = true;
        }
    }

    private static string GetCachedReceiptImagePath(Receipt receipt)
    {
        if (string.IsNullOrEmpty(receipt.FileData))
            return string.Empty;

        try
        {
            var isPdf = receipt.FileType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true
                        || receipt.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            // The same cache paths the viewer uses, so each reuses what the other rendered.
            var path = isPdf
                ? ReceiptPageRenderer.PagePath(receipt, 0)
                : ReceiptPageRenderer.ImagePath(receipt);

            return File.Exists(path) ? path : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<(string Path, int PageCount)> GenerateReceiptImagePathAsync(Receipt receipt)
    {
        if (string.IsNullOrEmpty(receipt.FileData))
            return (string.Empty, 1);

        try
        {
            ReceiptPageRenderer.EnsureTempDir();
            var bytes = Convert.FromBase64String(receipt.FileData);

            var isPdf = receipt.FileType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true
                        || receipt.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            if (isPdf)
            {
                var rendered = await PdfThumbnailService.Instance.RenderPdfFirstPageAsync(bytes);
                if (rendered == null) return (string.Empty, 1);
                // Cache page 1 where the viewer looks for it and record the page count, so the
                // viewer can reuse this page and only render the rest.
                var pdfPreviewPath = ReceiptPageRenderer.PagePath(receipt, 0);
                await File.WriteAllBytesAsync(pdfPreviewPath, rendered.Value.Image);
                ReceiptPageRenderer.WritePageCount(receipt, rendered.Value.PageCount);
                return (pdfPreviewPath, rendered.Value.PageCount);
            }

            var tempPath = ReceiptPageRenderer.ImagePath(receipt);
            var output = ReceiptImageHelper.FixOrientation(bytes);
            File.WriteAllBytes(tempPath, output);
            return (tempPath, 1);
        }
        catch
        {
            return (string.Empty, 1);
        }
    }

    private async Task LoadMissingImagesAsync(List<ReceiptDisplayItem> displayItems, List<Receipt> receipts)
    {
        _imageLoadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _imageLoadCts = cts;

        try
        {
            var toLoad = new List<(ReceiptDisplayItem Display, Receipt Receipt)>();
            for (var i = 0; i < displayItems.Count; i++)
            {
                if (string.IsNullOrEmpty(displayItems[i].ImagePath) && !string.IsNullOrEmpty(receipts[i].FileData))
                    toLoad.Add((displayItems[i], receipts[i]));
            }

            if (toLoad.Count == 0) return;

            // Process images in parallel (up to 4 at a time) instead of sequentially
            const int maxParallelism = 4;
            var semaphore = new SemaphoreSlim(maxParallelism);
            var results = await Task.Run(async () =>
            {
                var tasks = toLoad.Select(async item =>
                {
                    await semaphore.WaitAsync(cts.Token);
                    try
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        var rendered = await GenerateReceiptImagePathAsync(item.Receipt);
                        return !string.IsNullOrEmpty(rendered.Path)
                            ? (item.Display, rendered.Path, rendered.PageCount)
                            : default;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                var all = await Task.WhenAll(tasks);
                return all.Where(r => r.Display != null).ToList();
            }, cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var (display, path, pageCount) in results)
                {
                    display.ImagePath = path;
                    display.PageCount = pageCount;
                }
            });
        }
        catch (OperationCanceledException) { }
    }

    #endregion

    #region Filter Modal Commands

    [RelayCommand]
    private void OpenFilterModal()
    {
        App.ReceiptsModalsViewModel?.OpenFilterModal();
    }

    #endregion

    #region Action Commands

    [RelayCommand]
    private async Task AiScanReceipt()
    {
        if (App.ReceiptsModalsViewModel == null) return;

        // Trigger file picker in the view, usage limit is checked after modal opens
        ScanFileRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes a receipt's stored file into a folder and returns its path, or null when it has no file.
    /// The display image can't be used: for a PDF it is only page 1 as a JPEG.
    /// </summary>
    internal static string? WriteExportFile(Receipt receipt, string folder, string baseName)
    {
        if (string.IsNullOrEmpty(receipt.FileData)) return null;

        var path = Path.Combine(folder, baseName + Path.GetExtension(receipt.FileName));
        File.WriteAllBytes(path, Convert.FromBase64String(receipt.FileData));
        return path;
    }

    [RelayCommand]
    private async Task ExportSelected()
    {
        var selectedReceipts = Receipts.Where(r => r.IsSelected).ToList();
        if (selectedReceipts.Count == 0) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        try
        {
            var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow as MainWindow
                : null;

            if (mainWindow?.StorageProvider == null) return;

            // Let user pick a folder to export to
            var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Export Folder",
                AllowMultiple = false
            });

            if (folders.Count == 0) return;

            var baseFolder = folders[0].Path.LocalPath;

            // Create subfolder with company name and date
            var companyName = App.CompanyManager?.CurrentCompanyName ?? "Receipts";
            var safeName = string.Join("_", companyName.Split(Path.GetInvalidFileNameChars()));
            var exportFolderName = $"{safeName}_{DateTime.Now:yyyy-MM-dd}";
            var exportFolder = Path.Combine(baseFolder, exportFolderName);

            Directory.CreateDirectory(exportFolder);

            var exportedCount = 0;

            foreach (var item in selectedReceipts)
            {
                var receipt = companyData.Receipts.FirstOrDefault(r => r.Id == item.Id);
                if (receipt == null) continue;

                var baseName = $"Receipt_{item.Id}_{item.DateFormatted.Replace(",", "").Replace(" ", "_")}";
                if (WriteExportFile(receipt, exportFolder, baseName) != null)
                    exportedCount++;
            }

            if (exportedCount > 0)
            {
                // Exit selection mode after successful export
                IsSelectionMode = false;
            }
            else
            {
                // Show error message box
                if (mainWindow.MessageBoxService != null)
                {
                    await mainWindow.MessageBoxService.ShowWarningAsync(
                        "Export Failed",
                        "No receipts could be exported. Files may be missing.");
                }
            }
        }
        catch (Exception ex)
        {
            var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow as MainWindow
                : null;

            if (mainWindow?.MessageBoxService != null)
            {
                await mainWindow.MessageBoxService.ShowErrorAsync(
                    "Export Error",
                    $"Failed to export receipts: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task DownloadReceipt(ReceiptDisplayItem? receipt)
    {
        if (receipt == null || string.IsNullOrEmpty(receipt.ImagePath)) return;

        try
        {
            var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel?.StorageProvider == null) return;

            // Determine file extension from source
            var sourceExtension = Path.GetExtension(receipt.ImagePath);
            if (string.IsNullOrEmpty(sourceExtension))
                sourceExtension = ".png";

            var filters = new[]
            {
                new FilePickerFileType("Image files") { Patterns = [$"*{sourceExtension}"] }
            };

            var suggestedName = $"Receipt_{receipt.Id}{sourceExtension}";

            var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Receipt Image",
                SuggestedFileName = suggestedName,
                FileTypeChoices = filters,
                DefaultExtension = sourceExtension.TrimStart('.')
            });

            if (result != null)
            {
                var destinationPath = result.Path.LocalPath;

                // Copy the file
                if (File.Exists(receipt.ImagePath))
                {
                    File.Copy(receipt.ImagePath, destinationPath, overwrite: true);
                    App.AddNotification("Success", "Receipt saved successfully", NotificationType.Success);
                }
            }
        }
        catch (Exception ex)
        {
            await (App.ConfirmationDialog?.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Error",
                Message = $"Failed to save receipt: {ex.Message}",
                PrimaryButtonText = "OK",
                CancelButtonText = null
            }) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task DeleteReceipt(ReceiptDisplayItem? item)
    {
        if (item == null) return;

        try
        {
            await ConfirmAndDeleteReceiptAsync(item.Id);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Receipt.DeleteReceipt");
        }
    }

    /// <summary>
    /// Asks, then deletes a receipt and unlinks it from its transaction, with undo. Shared by the
    /// receipts list and the receipt viewer. Returns true when the receipt was deleted.
    /// </summary>
    internal static async Task<bool> ConfirmAndDeleteReceiptAsync(string receiptId)
    {
        var companyData = App.CompanyManager?.CompanyData;
        var receipt = companyData?.Receipts.FirstOrDefault(r => r.Id == receiptId);
        if (companyData == null || receipt == null) return false;

        var isLinked = !string.IsNullOrEmpty(receipt.TransactionId);
        var message = "Are you sure you want to delete this receipt?\n\nID: {0}\nSupplier: {1}".TranslateFormat(receipt.Id, receipt.Supplier);
        if (isLinked)
        {
            message += "\n\n" + "This receipt is linked to a {0} transaction ({1}). The receipt will be removed from the transaction.".TranslateFormat(
                receipt.TransactionType, receipt.TransactionId);
        }

        if (!await ConfirmDeleteAsync("Delete Receipt".Translate(), message)) return false;

        RemoveWithUndo(companyData, companyData.Receipts, receipt, $"Delete receipt {receipt.Id}", notify: null,
            onRemove: () =>
            {
                if (isLinked) SetTransactionReceiptId(companyData, receipt.TransactionType, receipt.TransactionId, null);
            },
            onRestore: () =>
            {
                if (isLinked) SetTransactionReceiptId(companyData, receipt.TransactionType, receipt.TransactionId, receipt.Id);
            });

        App.CompanyManager?.MarkAsChanged();
        return true;
    }

    private static void SetTransactionReceiptId(Core.Data.CompanyData companyData, string transactionType, string? transactionId, string? receiptId)
    {
        Core.Models.Transactions.Transaction? transaction = transactionType switch
        {
            "Expense" => companyData.Expenses.FirstOrDefault(e => e.Id == transactionId),
            "Revenue" => companyData.Revenues.FirstOrDefault(r => r.Id == transactionId),
            _ => null
        };

        if (transaction != null)
            transaction.ReceiptId = receiptId;
    }

    [RelayCommand]
    private async Task SwitchType(ReceiptDisplayItem? item)
    {
        if (item == null) return;

        if (await ReceiptTypeSwitchService.SwitchAsync(item.Id))
        {
            LoadReceipts();
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var receipt in Receipts)
        {
            receipt.IsSelected = true;
        }
        UpdateSelectionState();
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        var selectedReceipts = Receipts.Where(r => r.IsSelected).ToList();
        if (selectedReceipts.Count == 0) return;

        try
        {
            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null) return;

            // Check how many are linked to transactions
            var linkedCount = selectedReceipts.Count(r => !string.IsNullOrEmpty(r.TransactionId));

            var message = selectedReceipts.Count == 1
                ? "Are you sure you want to delete this receipt?\n\nID: {0}\nSupplier: {1}".TranslateFormat(selectedReceipts[0].Id, selectedReceipts[0].Supplier)
                : "Are you sure you want to delete {0} receipts?".TranslateFormat(selectedReceipts.Count);

            if (linkedCount > 0)
            {
                message += "\n\n" + "{0} of the selected receipts are linked to transactions. The receipts will be removed from those transactions.".TranslateFormat(linkedCount);
            }

            if (!await ConfirmDeleteAsync("Delete Receipts".Translate(), message)) return;

            var receiptsToDelete = selectedReceipts
                .Select(displayItem => companyData.Receipts.FirstOrDefault(r => r.Id == displayItem.Id))
                .OfType<Receipt>()
                .ToList();
            if (receiptsToDelete.Count == 0) return;

            void Remove()
            {
                foreach (var receipt in receiptsToDelete)
                {
                    companyData.Receipts.Remove(receipt);
                    if (!string.IsNullOrEmpty(receipt.TransactionId))
                        SetTransactionReceiptId(companyData, receipt.TransactionType, receipt.TransactionId, null);
                }
            }

            void Restore()
            {
                foreach (var receipt in receiptsToDelete)
                {
                    companyData.Receipts.Add(receipt);
                    if (!string.IsNullOrEmpty(receipt.TransactionId))
                        SetTransactionReceiptId(companyData, receipt.TransactionType, receipt.TransactionId, receipt.Id);
                }
            }

            Remove();
            App.UndoRedoManager.RecordAction(new DelegateAction($"Delete {receiptsToDelete.Count} receipt(s)", Restore, Remove));
            App.CompanyManager?.MarkAsChanged();

            // Exit selection mode and reload
            IsSelectionMode = false;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Receipt.DeleteSelected");
        }
    }

    #endregion
}

/// <summary>
/// Display model for receipts in the UI.
/// </summary>
public partial class ReceiptDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _transactionId = string.Empty;

    [ObservableProperty]
    private string _transactionType = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fileType = string.Empty;

    [ObservableProperty]
    private long _fileSize;

    [ObservableProperty]
    private decimal _amount;

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    private string _supplier = string.Empty;

    [ObservableProperty]
    private string _source = string.Empty;

    [ObservableProperty]
    private bool _isAiScanned;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _imagePath = string.Empty;

    [ObservableProperty]
    private int _pageCount = 1;

    partial void OnImagePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasImage));
    }

    partial void OnPageCountChanged(int value)
    {
        OnPropertyChanged(nameof(IsMultiPage));
    }

    public bool IsMultiPage => PageCount > 1;

    // Computed properties for display
    public string DateFormatted => Date.ToString("MMM d, yyyy");
    public string AmountFormatted => CurrencyService.Format(Amount);

    public bool IsExpense => TransactionType == "Expense";
    public bool IsRevenue => TransactionType == "Revenue";

    public bool IsImage => FileType.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                           FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                           FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                           FileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    public bool IsPdf => FileType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
                         FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public bool HasImage => !string.IsNullOrEmpty(ImagePath);

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
