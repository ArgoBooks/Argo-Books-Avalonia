using System.Collections.ObjectModel;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Services;
using ArgoBooks.Helpers;
using ArgoBooks.Services;
using ArgoBooks.Localization;
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
public partial class ReceiptsPageViewModel : ViewModelBase, ICleanupViewModel
{
    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

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
    private void ToggleSelectionMode()
    {
        IsSelectionMode = !IsSelectionMode;
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

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private int _pageSize = 25; // Default to match ArgoTable's PageSizeOptions

    partial void OnPageSizeChanged(int value)
    {
        CurrentPage = 1;
        FilterReceipts();
    }

    [ObservableProperty]
    private string _paginationText = "0 receipts";

    public ObservableCollection<int> PageNumbers { get; } = [];

    public bool CanGoToPreviousPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    partial void OnCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        FilterReceipts();
    }

    [RelayCommand]
    private void GoToPreviousPage()
    {
        if (CanGoToPreviousPage)
            CurrentPage--;
    }

    [RelayCommand]
    private void GoToNextPage()
    {
        if (CanGoToNextPage)
            CurrentPage++;
    }

    [RelayCommand]
    private void GoToPage(int page)
    {
        if (page >= 1 && page <= TotalPages)
            CurrentPage = page;
    }

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
    private bool _isColumnMenuOpen;

    [ObservableProperty]
    private double _columnMenuX;

    [ObservableProperty]
    private double _columnMenuY;

    public ReceiptsTableColumnWidths ColumnWidths => App.ReceiptsColumnWidths;

    [ObservableProperty]
    private bool _showIdColumn = ColumnVisibilityHelper.Load("Receipts", "Id", true);

    [ObservableProperty]
    private bool _showSupplierColumn = ColumnVisibilityHelper.Load("Receipts", "Supplier", true);

    [ObservableProperty]
    private bool _showDateColumn = ColumnVisibilityHelper.Load("Receipts", "Date", true);

    [ObservableProperty]
    private bool _showTypeColumn = ColumnVisibilityHelper.Load("Receipts", "Type", true);

    [ObservableProperty]
    private bool _showAmountColumn = ColumnVisibilityHelper.Load("Receipts", "Amount", true);

    partial void OnShowIdColumnChanged(bool value)
    {
        ColumnWidths.SetColumnVisibility("Id", value);
        ColumnVisibilityHelper.Save("Receipts", "Id", value);
    }

    partial void OnShowSupplierColumnChanged(bool value)
    {
        ColumnWidths.SetColumnVisibility("Supplier", value);
        ColumnVisibilityHelper.Save("Receipts", "Supplier", value);
    }

    partial void OnShowDateColumnChanged(bool value)
    {
        ColumnWidths.SetColumnVisibility("Date", value);
        ColumnVisibilityHelper.Save("Receipts", "Date", value);
    }

    partial void OnShowTypeColumnChanged(bool value)
    {
        ColumnWidths.SetColumnVisibility("Type", value);
        ColumnVisibilityHelper.Save("Receipts", "Type", value);
    }

    partial void OnShowAmountColumnChanged(bool value)
    {
        ColumnWidths.SetColumnVisibility("Amount", value);
        ColumnVisibilityHelper.Save("Receipts", "Amount", value);
    }

    [RelayCommand]
    private void ToggleColumnMenu()
    {
        IsColumnMenuOpen = !IsColumnMenuOpen;
    }

    [RelayCommand]
    private void CloseColumnMenu()
    {
        IsColumnMenuOpen = false;
    }

    [RelayCommand]
    private void ResetColumnVisibility()
    {
        ColumnWidths.ResetWidths();
        ColumnVisibilityHelper.ResetPage("Receipts");
        ShowIdColumn = true;
        ShowSupplierColumn = true;
        ShowDateColumn = true;
        ShowTypeColumn = true;
        ShowAmountColumn = true;
    }

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
        ColumnVisibilityHelper.SyncToManager(this);
        LoadReceipts();
        CheckScannerConfiguration();

        // Subscribe to undo/redo state changes to refresh UI
        App.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

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
    public void Cleanup()
    {
        CancelPendingSearch();
        App.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
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
        CurrentPage = 1;
        FilterReceipts();
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        SearchQuery = null;
        CurrentPage = 1;
        FilterReceipts();
    }

    private bool _needsRefresh;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (App.NavigationService?.CurrentPageName != PageNames.Receipts)
        {
            _needsRefresh = true;
            return;
        }
        LoadReceipts();
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName == PageNames.Receipts && _needsRefresh)
        {
            _needsRefresh = false;
            LoadReceipts();
        }
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
            var query = SearchQuery.ToLowerInvariant();
            filtered = filtered.Where(r =>
                r.Id.ToLowerInvariant().Contains(query) ||
                r.Supplier.ToLowerInvariant().Contains(query) ||
                r.FileName.ToLowerInvariant().Contains(query) ||
                r.TransactionId.ToLowerInvariant().Contains(query)
            );
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

        // Sort by date descending (newest first), materialize for .Count and pagination
        var sortedFiltered = filtered.OrderByDescending(r => r.Date).ToList();

        // Calculate pagination on raw receipts (before creating display items)
        var totalCount = sortedFiltered.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdatePageNumbers();
        UpdatePaginationText(totalCount);

        // Paginate BEFORE creating display items, only process the visible page
        var pagedReceipts = sortedFiltered
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

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

    private void UpdatePageNumbers()
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
            totalCount, CurrentPage, PageSize, TotalPages, "receipt");
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
            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null) return;

            var receipt = companyData.Receipts.FirstOrDefault(r => r.Id == item.Id);
            if (receipt == null) return;

            var dialog = App.ConfirmationDialog;
            if (dialog == null) return;

            var isLinked = !string.IsNullOrEmpty(receipt.TransactionId);
            var message = "Are you sure you want to delete this receipt?\n\nID: {0}\nSupplier: {1}".TranslateFormat(item.Id, item.Supplier);
            if (isLinked)
            {
                message += "\n\n" + "This receipt is linked to a {0} transaction ({1}). The receipt will be removed from the transaction.".TranslateFormat(
                    receipt.TransactionType, receipt.TransactionId);
            }

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete Receipt".Translate(),
                Message = message,
                PrimaryButtonText = "Delete".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary) return;

            string? linkedTransactionId = null;
            string? linkedTransactionType = null;
            if (isLinked)
            {
                linkedTransactionId = receipt.TransactionId;
                linkedTransactionType = receipt.TransactionType;

                if (receipt.TransactionType == "Expense")
                {
                    var expense = companyData.Expenses.FirstOrDefault(e => e.Id == receipt.TransactionId);
                    if (expense != null) expense.ReceiptId = null;
                }
                else if (receipt.TransactionType == "Revenue")
                {
                    var revenue = companyData.Revenues.FirstOrDefault(r => r.Id == receipt.TransactionId);
                    if (revenue != null) revenue.ReceiptId = null;
                }
            }

            companyData.Receipts.Remove(receipt);

            var deletedReceipt = receipt;
            var action = new DelegateAction(
                $"Delete receipt {deletedReceipt.Id}",
                () =>
                {
                    companyData.Receipts.Add(deletedReceipt);
                    if (linkedTransactionType == "Expense")
                    {
                        var expense = companyData.Expenses.FirstOrDefault(e => e.Id == linkedTransactionId);
                        if (expense != null) expense.ReceiptId = deletedReceipt.Id;
                    }
                    else if (linkedTransactionType == "Revenue")
                    {
                        var revenue = companyData.Revenues.FirstOrDefault(r => r.Id == linkedTransactionId);
                        if (revenue != null) revenue.ReceiptId = deletedReceipt.Id;
                    }
                },
                () =>
                {
                    companyData.Receipts.Remove(deletedReceipt);
                    if (linkedTransactionType == "Expense")
                    {
                        var expense = companyData.Expenses.FirstOrDefault(e => e.Id == linkedTransactionId);
                        if (expense != null) expense.ReceiptId = null;
                    }
                    else if (linkedTransactionType == "Revenue")
                    {
                        var revenue = companyData.Revenues.FirstOrDefault(r => r.Id == linkedTransactionId);
                        if (revenue != null) revenue.ReceiptId = null;
                    }
                });

            App.UndoRedoManager.RecordAction(action);
            App.CompanyManager?.MarkAsChanged();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Receipt.DeleteReceipt");
        }
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
    private void DeselectAll()
    {
        foreach (var receipt in Receipts)
        {
            receipt.IsSelected = false;
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
            var dialog = App.ConfirmationDialog;
            if (dialog == null) return;

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

            var result = await dialog.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Delete Receipts".Translate(),
                Message = message,
                PrimaryButtonText = "Delete".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true
            });

            if (result != ConfirmationResult.Primary) return;

            // Find actual Receipt objects to delete and track transaction links for undo
            var receiptsToDelete = new List<Receipt>();
            var transactionLinks = new List<(string TransactionId, string TransactionType, string ReceiptId)>();

            foreach (var displayItem in selectedReceipts)
            {
                var receipt = companyData.Receipts.FirstOrDefault(r => r.Id == displayItem.Id);
                if (receipt == null) continue;

                receiptsToDelete.Add(receipt);

                // Unlink from transaction
                if (!string.IsNullOrEmpty(receipt.TransactionId))
                {
                    transactionLinks.Add((receipt.TransactionId, receipt.TransactionType, receipt.Id));

                    if (receipt.TransactionType == "Expense")
                    {
                        var expense = companyData.Expenses.FirstOrDefault(e => e.Id == receipt.TransactionId);
                        if (expense != null) expense.ReceiptId = null;
                    }
                    else if (receipt.TransactionType == "Revenue")
                    {
                        var revenue = companyData.Revenues.FirstOrDefault(r => r.Id == receipt.TransactionId);
                        if (revenue != null) revenue.ReceiptId = null;
                    }
                }
            }

            if (receiptsToDelete.Count == 0) return;

            // Remove all receipts
            foreach (var receipt in receiptsToDelete)
            {
                companyData.Receipts.Remove(receipt);
            }

            // Record undo/redo action
            var capturedReceipts = receiptsToDelete.ToList();
            var capturedLinks = transactionLinks.ToList();
            var action = new DelegateAction(
                $"Delete {capturedReceipts.Count} receipt(s)",
                () =>
                {
                    foreach (var r in capturedReceipts)
                        companyData.Receipts.Add(r);
                    foreach (var link in capturedLinks)
                    {
                        if (link.TransactionType == "Expense")
                        {
                            var expense = companyData.Expenses.FirstOrDefault(e => e.Id == link.TransactionId);
                            if (expense != null) expense.ReceiptId = link.ReceiptId;
                        }
                        else if (link.TransactionType == "Revenue")
                        {
                            var revenue = companyData.Revenues.FirstOrDefault(r => r.Id == link.TransactionId);
                            if (revenue != null) revenue.ReceiptId = link.ReceiptId;
                        }
                    }
                },
                () =>
                {
                    foreach (var r in capturedReceipts)
                        companyData.Receipts.Remove(r);
                    foreach (var link in capturedLinks)
                    {
                        if (link.TransactionType == "Expense")
                        {
                            var expense = companyData.Expenses.FirstOrDefault(e => e.Id == link.TransactionId);
                            if (expense != null) expense.ReceiptId = null;
                        }
                        else if (link.TransactionType == "Revenue")
                        {
                            var revenue = companyData.Revenues.FirstOrDefault(r => r.Id == link.TransactionId);
                            if (revenue != null) revenue.ReceiptId = null;
                        }
                    }
                });

            App.UndoRedoManager.RecordAction(action);
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
