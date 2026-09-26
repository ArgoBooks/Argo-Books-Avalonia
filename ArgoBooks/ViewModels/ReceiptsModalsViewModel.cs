using System.Collections.ObjectModel;
using System.Globalization;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Data;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for Receipts modals (Filter, AI Scan Review).
/// </summary>
public partial class ReceiptsModalsViewModel : ViewModelBase
{
    /// <summary>
    /// Design-time instance for Avalonia previewer in Rider/VS.
    /// Provides sample data so the scan review modal is visible in the preview.
    /// </summary>
    public static ReceiptsModalsViewModel DesignInstance { get; } = CreateDesignInstance();

    private static ReceiptsModalsViewModel CreateDesignInstance()
    {
        var sampleProduct = new ProductOption { Id = "1", Name = "Classico Traditional Pizza Sauce", UnitPrice = 11.88m };
        var sampleSupplier = new SupplierOption { Id = "1", Name = "Independent Grocer" };

        var vm = new ReceiptsModalsViewModel
        {
            IsScanReviewModalOpen = true,
            HasScanResult = true,
            IsHighConfidence = true,
            ConfidenceText = "95%",
            ExtractedTotal = "25.74",
            ExtractedSubtotal = "22.86",
            ExtractedTax = "2.88",
            ExtractedDiscount = "0.00",
            ExtractedShipping = "0.00",
            ExtractedSupplier = "Independent Grocer",
            SelectedPaymentMethod = "Debit Card"
        };
        vm.ProductOptions.Add(sampleProduct);
        vm.SupplierOptions.Add(sampleSupplier);
        vm.SelectedSupplier = sampleSupplier;
        vm.LineItems.Add(new ScannedLineItemViewModel { Description = "CLSO TRAD PZA S MRJ", Quantity = "1", UnitPrice = "11.88", TotalPrice = "11.88", SelectedProduct = sampleProduct });
        vm.LineItems.Add(new ScannedLineItemViewModel { Description = "CLSO SORNT ONION MRJ", Quantity = "1", UnitPrice = "2.97", TotalPrice = "2.97", ShowCreateProductSuggestion = true, SuggestedProductName = "Clso Sornt Onion" });
        vm.LineItems.Add(new ScannedLineItemViewModel { Description = "UH SOYA SCE MRJ", Quantity = "1", UnitPrice = "4.79", TotalPrice = "4.79" });
        return vm;
    }

    #region Events

    /// <summary>
    /// Raised when filters are applied.
    /// </summary>
    public event EventHandler? FiltersApplied;

    /// <summary>
    /// Raised when filters are cleared.
    /// </summary>
    public event EventHandler? FiltersCleared;

    /// <summary>
    /// Raised when a receipt scan creates a new expense.
    /// </summary>
    public event EventHandler? ReceiptScanned;

    #endregion

    #region Filter Modal State

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private string _filterType = "All";

    [ObservableProperty]
    private DateTimeOffset? _filterDateFrom;

    [ObservableProperty]
    private DateTimeOffset? _filterDateTo;

    [ObservableProperty]
    private string? _filterAmountMin;

    [ObservableProperty]
    private string? _filterAmountMax;

    [ObservableProperty]
    private string _filterSource = "All";

    [ObservableProperty]
    private string _filterFileType = "All";

    /// <summary>
    /// Receipt type filter options.
    /// </summary>
    public ObservableCollection<string> TypeOptions { get; } = ["All", "Expense", "Revenue"];

    /// <summary>
    /// Source filter options.
    /// </summary>
    public ObservableCollection<string> SourceOptions { get; } = ["All", "Manual", "AI Scanned"];

    /// <summary>
    /// File type filter options.
    /// </summary>
    public ObservableCollection<string> FileTypeOptions { get; } = ["All", "Image", "PDF"];

    private sealed record FilterValues(
        string Type, DateTimeOffset? DateFrom, DateTimeOffset? DateTo,
        string? AmountMin, string? AmountMax, string Source, string FileType)
    {
        public static readonly FilterValues Default = new("All", null, null, null, null, "All", "All");
    }

    private FilterSnapshot<FilterValues>? _filters;

    private FilterSnapshot<FilterValues> Filters => _filters ??= new(FilterValues.Default,
        () => new(FilterType, FilterDateFrom, FilterDateTo, FilterAmountMin, FilterAmountMax, FilterSource, FilterFileType),
        v =>
        {
            FilterType = v.Type;
            FilterDateFrom = v.DateFrom;
            FilterDateTo = v.DateTo;
            FilterAmountMin = v.AmountMin;
            FilterAmountMax = v.AmountMax;
            FilterSource = v.Source;
            FilterFileType = v.FileType;
        });

    #endregion

    #region Filter Modal Commands

    /// <summary>
    /// Opens the filter modal.
    /// </summary>
    public void OpenFilterModal()
    {
        Filters.Capture();
        IsFilterModalOpen = true;
    }

    private void CloseFilterModal() => IsFilterModalOpen = false;

    /// <summary>
    /// Closes the filter modal, asking first and putting the filters back if they were changed.
    /// </summary>
    [RelayCommand]
    public async Task RequestCloseFilterModalAsync()
    {
        if (await Filters.ConfirmDiscardAsync(ConfirmDiscardFiltersAsync))
            CloseFilterModal();
    }

    /// <summary>How many filters are applied, for the page's Filter button.</summary>
    public int ActiveFilterCount { get; private set; }

    /// <summary>
    /// Applies the current filters.
    /// </summary>
    [RelayCommand]
    private void ApplyFilters()
    {
        ActiveFilterCount = Filters.ActiveCount;
        FiltersApplied?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        Filters.Reset();
        ActiveFilterCount = 0;
        FiltersCleared?.Invoke(this, EventArgs.Empty);
        CloseFilterModal();
    }

    #endregion

    #region AI Scan Review Modal State

    private IReceiptScannerService? _scannerService;
    private IUsageLimitService? _usageService;

    /// <summary>
    /// Invalidates cached scan services so the next scan attempt picks up
    /// current license/usage state. Call after plan status changes (upgrade/downgrade).
    /// </summary>
    public void InvalidateScanServices()
    {
        _usageService?.InvalidateCache();
        // Dispose before dropping the reference: the service owns an HttpClient.
        _usageService?.Dispose();
        _usageService = null;
        _scannerService = null;
    }
    private byte[]? _currentImageData;
    private string? _currentFileName;
    private bool _suppressAiSuggestions;

    // Track entities created during receipt flow for undo. A bulk review creates suppliers for
    // several receipts, so every one is kept, not just the latest.
    private readonly List<Supplier> _createdSuppliersForUndo = new();
    private Category? _createdCategoryForUndo;
    private readonly List<Product> _createdProductsForUndo = new();

    // Set once a transaction is created so the auto-created entities are handed off to the
    // undo action instead of being rolled back when the modal closes.
    private bool _createdEntitiesCommitted;

    private const string SampleReceiptFileName = "sample-receipt.png";

    [ObservableProperty]
    private bool _isScanReviewModalOpen;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanningMessage = "Scanning receipt...";

    [ObservableProperty]
    private double _scanProgress;

    [ObservableProperty]
    private bool _showScanProgress;

    [ObservableProperty]
    private bool _hasScanError;

    [ObservableProperty]
    private string _scanErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasScanResult;

    [ObservableProperty]
    private bool _isFullscreen;

    /// <summary>
    /// Gets the modal width based on current state.
    /// Narrower for loading/error states, wider for results.
    /// NaN when fullscreen (stretches to fill).
    /// </summary>
    public double ModalWidth => IsFullscreen ? double.NaN : (HasScanResult || IsBulkReviewOpen ? 1100 : 520);

    /// <summary>
    /// Gets the modal height. NaN when fullscreen (stretches to fill).
    /// </summary>
    public double ModalHeight => IsFullscreen ? double.NaN : (HasScanResult || IsBulkReviewOpen ? 850 : 400);

    /// <summary>
    /// Gets the modal margin. Zero when fullscreen, auto-centered otherwise.
    /// </summary>
    public Avalonia.Thickness ModalMargin => IsFullscreen ? new Avalonia.Thickness(8) : new Avalonia.Thickness(0);

    /// <summary>
    /// Gets modal horizontal alignment. Stretch when fullscreen.
    /// </summary>
    public Avalonia.Layout.HorizontalAlignment ModalHorizontalAlignment =>
        IsFullscreen ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Center;

    /// <summary>
    /// Gets modal vertical alignment. Stretch when fullscreen.
    /// </summary>
    public Avalonia.Layout.VerticalAlignment ModalVerticalAlignment =>
        IsFullscreen ? Avalonia.Layout.VerticalAlignment.Stretch : Avalonia.Layout.VerticalAlignment.Center;

    partial void OnHasScanResultChanged(bool value)
    {
        OnPropertyChanged(nameof(ModalWidth));
        OnPropertyChanged(nameof(ModalHeight));
    }

    partial void OnIsScanningChanged(bool value)
    {
        // Clear any leftover progress from the previous scan the moment scanning (re)starts, so the
        // bar doesn't briefly flash the prior scan's 100% before the new scan's reset runs. Covers
        // every entry point (open + retry).
        if (value)
        {
            ScanProgress = 0;
            ShowScanProgress = false;
        }
    }

    partial void OnIsFullscreenChanged(bool value)
    {
        OnPropertyChanged(nameof(ModalWidth));
        OnPropertyChanged(nameof(ModalHeight));
        OnPropertyChanged(nameof(ModalMargin));
        OnPropertyChanged(nameof(ModalHorizontalAlignment));
        OnPropertyChanged(nameof(ModalVerticalAlignment));
    }

    /// <summary>
    /// Page images shown in the scan/bulk review preview (one entry per PDF page, single entry
    /// for raster images), so multi-page PDFs render all pages stacked.
    /// </summary>
    public ObservableCollection<string> ScanPreviewPages { get; } = new();

    /// <summary>
    /// Secondary line shown under the scanning spinner. Warns when the receipt is a multi-page
    /// PDF (which takes longer to scan), otherwise the generic "this may take a moment" message.
    /// </summary>
    public string ScanSubMessage => ScanPreviewPages.Count > 1
        ? "Multi-page receipts may take a few seconds longer.".Translate()
        : "This may take a few seconds...".Translate();

    // Guards against a stale async preview render finishing after navigation/reset.
    private int _previewToken;

    /// <summary>Sets the preview to the given page paths immediately (raster images / fallbacks).</summary>
    private void SetScanPreview(params string?[] paths)
    {
        ++_previewToken;
        ScanPreviewPages.Clear();
        foreach (var p in paths)
            if (!string.IsNullOrEmpty(p))
                ScanPreviewPages.Add(p);
        OnPropertyChanged(nameof(ScanSubMessage));
    }

    /// <summary>
    /// Renders the preview pages off the UI thread (all pages for PDFs) and applies them, unless a
    /// newer preview request has superseded this one.
    /// </summary>
    private async Task LoadPreviewPagesAsync(byte[] data, string fileName, bool isPdf, string tempSubdir)
    {
        var token = ++_previewToken;
        var paths = await Task.Run(() => RenderPreviewPagesAsync(data, fileName, isPdf, tempSubdir));
        if (token != _previewToken) return;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token != _previewToken) return;
            ScanPreviewPages.Clear();
            foreach (var p in paths)
                if (!string.IsNullOrEmpty(p))
                    ScanPreviewPages.Add(p);
            OnPropertyChanged(nameof(ScanSubMessage));
        });
    }

    internal static async Task<List<string>> RenderPreviewPagesAsync(byte[] data, string fileName, bool isPdf, string tempSubdir)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ArgoBooks", tempSubdir);
        Directory.CreateDirectory(tempDir);
        var paths = new List<string>();

        // Keyed by content as well as name: scanned files often share a name (Scan.pdf).
        var key = ReceiptPageRenderer.ContentKey(fileName, data);
        if (isPdf)
        {
            var pages = await PdfThumbnailService.Instance.RenderPdfAllPagesAsync(data);
            if (pages == null) return paths;
            for (var i = 0; i < pages.Length; i++)
            {
                var p = Path.Combine(tempDir, $"{key}_p{i + 1}.jpg");
                await File.WriteAllBytesAsync(p, pages[i]);
                paths.Add(p);
            }
        }
        else
        {
            var p = Path.Combine(tempDir, $"{key}.jpg");
            await File.WriteAllBytesAsync(p, data);
            paths.Add(p);
        }

        return paths;
    }

    [ObservableProperty]
    private string _extractedSupplier = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _extractedDate;

    [ObservableProperty]
    private string _extractedSubtotal = string.Empty;

    [ObservableProperty]
    private string _extractedTax = string.Empty;

    [ObservableProperty]
    private string _extractedDiscount = string.Empty;

    [ObservableProperty]
    private string _extractedShipping = string.Empty;

    [ObservableProperty]
    private string _extractedTotal = string.Empty;

    [ObservableProperty]
    private double _confidenceScore;

    [ObservableProperty]
    private string _confidenceText = string.Empty;

    [ObservableProperty]
    private bool _isHighConfidence;

    [ObservableProperty]
    private bool _isMediumConfidence;

    [ObservableProperty]
    private bool _isLowConfidence;

    public ObservableCollection<ScannedLineItemViewModel> LineItems { get; } = [];

    [ObservableProperty]
    private SupplierOption? _selectedSupplier;

    [ObservableProperty]
    private string _selectedPaymentMethod = "Cash";

    /// <summary>
    /// The currency the reviewed receipt is saved in. Starts on the scanned one (ReceiptCurrency),
    /// so the user can correct it before saving.
    /// </summary>
    [ObservableProperty]
    private string _selectedScanCurrency = string.Empty;

    public IReadOnlyList<string> CurrencyOptions => Currencies.All;

    private string ScanCurrencyCode => string.IsNullOrEmpty(SelectedScanCurrency)
        ? CurrencyService.CurrentCurrencyCode
        : CurrencyService.ParseCurrencyCode(SelectedScanCurrency);

    [ObservableProperty]
    private string _notes = string.Empty;

    /// <summary>
    /// Whether this is a revenue transaction (true) or expense transaction (false).
    /// Determined by comparing extracted merchant name against the user's company name.
    /// </summary>
    [ObservableProperty]
    private bool _isRevenue;

    partial void OnIsRevenueChanged(bool value)
    {
        ValidateCurrentBulkItem();
    }

    /// <summary>
    /// Sets the transaction type (expense or revenue).
    /// </summary>
    [RelayCommand]
    private void SetTransactionType(string type)
    {
        IsRevenue = type == "Revenue";
    }

    public ObservableCollection<SupplierOption> SupplierOptions { get; } = [];
    public ObservableCollection<ProductOption> ProductOptions { get; } = [];
    public ObservableCollection<string> PaymentMethodOptions { get; } = new(PaymentMethodExtensions.GetCommonOptions());

    [ObservableProperty]
    private bool _hasTotalError;

    [ObservableProperty]
    private string _totalErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasUnmatchedProducts;

    [ObservableProperty]
    private bool _hasTotalMismatchWarning;

    [ObservableProperty]
    private string _totalMismatchWarningMessage = string.Empty;

    [ObservableProperty]
    private bool _hasLineItemsError;

    [ObservableProperty]
    private string _lineItemsErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasSupplierError;

    [ObservableProperty]
    private string _supplierErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationMessage;

    [ObservableProperty]
    private bool _hasBulkIncompleteWarning;

    [ObservableProperty]
    private string _bulkIncompleteWarningMessage = string.Empty;

    partial void OnSelectedSupplierChanged(SupplierOption? value)
    {
        if (value != null)
        {
            HasSupplierError = false;
            SupplierErrorMessage = string.Empty;
            ClearValidationMessageIfNoErrors();
        }
        ValidateCurrentBulkItem();
    }

    partial void OnExtractedTotalChanged(string value) { ValidateTotals(); ValidateCurrentBulkItem(); }
    partial void OnExtractedSubtotalChanged(string value) => ValidateTotals();
    partial void OnExtractedTaxChanged(string value) => ValidateTotals();
    partial void OnExtractedDiscountChanged(string value) => ValidateTotals();
    partial void OnExtractedShippingChanged(string value) => ValidateTotals();

    partial void OnHasTotalErrorChanged(bool value)
    {
        if (!value) ClearValidationMessageIfNoErrors();
    }

    partial void OnHasLineItemsErrorChanged(bool value)
    {
        if (!value) ClearValidationMessageIfNoErrors();
    }

    private void ClearValidationMessageIfNoErrors()
    {
        if (!HasTotalError && !HasSupplierError && !HasLineItemsError &&
            LineItems.All(li => !li.HasProductError))
        {
            HasValidationMessage = false;
        }
    }

    // AI Suggestion State
    [ObservableProperty]
    private bool _isLoadingAiSuggestions;

    private SupplierCategorySuggestion? _aiSuggestion;

    // Bumped whenever the scan modal resets (close or a new scan starts). An in-flight AI suggestion
    // call captures the current value and discards its result if the generation has moved on, so a
    // late suggestion for a previous receipt can't land on the receipt now on screen (single-scan flow).
    private int _scanGeneration;

    [ObservableProperty]
    private bool _showCreateSupplierSuggestion;

    [ObservableProperty]
    private string _suggestedSupplierName = string.Empty;

    // Usage tracking state
    [ObservableProperty]
    private int _scansUsed;

    [ObservableProperty]
    private int _scansLimit;

    [ObservableProperty]
    private int _scansRemaining;

    [ObservableProperty]
    private bool _hasUsageInfo;

    [ObservableProperty]
    private bool _isNearLimit;

    [ObservableProperty]
    private string? _usageTier;

    [ObservableProperty]
    private string? _resetsAt;

    #endregion

    #region Bulk Scan State

    [ObservableProperty]
    private bool _isBulkDropZoneOpen;

    [ObservableProperty]
    private bool _isBulkScanning;

    [ObservableProperty]
    private bool _isBulkScanComplete;

    [ObservableProperty]
    private bool _isBulkReviewOpen;

    public ObservableCollection<BulkScanItem> BulkItems { get; } = [];

    /// <summary>
    /// Feedback shown in the drop zone after a drop, e.g. when some files were
    /// skipped for being an unsupported format or already queued. Empty when the
    /// drop was clean (nothing to report).
    /// </summary>
    [ObservableProperty]
    private string _bulkDropMessage = string.Empty;

    [ObservableProperty]
    private BulkScanItem? _currentBulkItem;

    /// <summary>
    /// Whether the Approve button should be visible. False when no item is loaded
    /// or when the current item is already approved. Bound directly so the XAML
    /// doesn't have to traverse a possibly-null CurrentBulkItem path.
    /// </summary>
    public bool ShowApproveBulkButton => CurrentBulkItem is { IsApproved: false };

    /// <summary>
    /// Whether the Un-approve button should be visible. True only when the current
    /// item is loaded and already approved.
    /// </summary>
    public bool ShowUnapproveBulkButton => CurrentBulkItem is { IsApproved: true };

    partial void OnCurrentBulkItemChanging(BulkScanItem? oldValue, BulkScanItem? newValue)
    {
        if (oldValue != null) oldValue.PropertyChanged -= OnCurrentBulkItemPropertyChanged;
        if (newValue != null) newValue.PropertyChanged += OnCurrentBulkItemPropertyChanged;
    }

    partial void OnCurrentBulkItemChanged(BulkScanItem? value)
    {
        OnPropertyChanged(nameof(ShowApproveBulkButton));
        OnPropertyChanged(nameof(ShowUnapproveBulkButton));
    }

    private void OnCurrentBulkItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BulkScanItem.IsApproved))
        {
            OnPropertyChanged(nameof(ShowApproveBulkButton));
            OnPropertyChanged(nameof(ShowUnapproveBulkButton));
        }
    }

    [ObservableProperty]
    private int _currentBulkIndex;

    [ObservableProperty]
    private int _bulkScansCompleted;

    [ObservableProperty]
    private int _bulkScansSucceeded;

    [ObservableProperty]
    private int _bulkScansFailed;

    public int BulkApprovedCount => BulkItems.Count(i => i.IsApproved);

    /// <summary>
    /// True when every succeeded item has been either approved or skipped (reviewed),
    /// and no approved items have validation errors.
    /// </summary>
    public bool BulkAllReviewed =>
        BulkApprovedCount > 0 &&
        BulkItems.Where(i => i.Status == BulkScanStatus.Succeeded).All(i => i.IsApproved || i.IsSkipped) &&
        !BulkItems.Any(i => i.IsApproved && i.HasValidationErrors);

    public int BulkIncompleteCount => BulkItems.Count(i => i.IsApproved && i.HasValidationErrors);

    public bool HasBulkIncompleteItems => BulkIncompleteCount > 0;

    private List<BulkScanItem>? _bulkSucceededItemsCache;

    public IReadOnlyList<BulkScanItem> BulkSucceededItems
    {
        get
        {
            _bulkSucceededItemsCache ??= BulkItems
                .Where(i => i.Status == BulkScanStatus.Succeeded).ToList();
            return _bulkSucceededItemsCache;
        }
    }

    public int BulkProgressPercent => BulkItems.Count > 0
        ? (int)(BulkScansCompleted / (double)BulkItems.Count * 100)
        : 0;

    /// <summary>
    /// Usage summary text for the drop zone, e.g., "This will use 3 scans. 12/50 used this month."
    /// </summary>
    public string BulkUsageSummary
    {
        get
        {
            var count = BulkItems.Count;
            var scanWord = count == 1 ? "scan" : "scans";
            return $"This will use {count} {scanWord}. {ScansUsed}/{ScansLimit} used this month.";
        }
    }

    partial void OnIsBulkReviewOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ModalWidth));
        OnPropertyChanged(nameof(ModalHeight));
    }

    partial void OnBulkScansCompletedChanged(int value)
    {
        _bulkSucceededItemsCache = null;
        OnPropertyChanged(nameof(BulkProgressPercent));
        OnPropertyChanged(nameof(BulkSucceededItems));
    }

    private CancellationTokenSource? _bulkCancellationSource;

    /// <summary>
    /// Set once the server refuses a scan for the monthly allowance, and shown on every item
    /// still queued behind it.
    ///
    /// Holds the server's own wording rather than a bool, because it names the count, the limit
    /// and the reset date, and repeating that on each remaining card is more use than a generic
    /// failure on all of them. Written from several scan tasks at once; a plain reference
    /// assignment is atomic and they all write the same thing, so the last one wins harmlessly.
    /// </summary>
    private string? _bulkScanLimitMessage;

    /// <summary>
    /// Cancels the in-flight single receipt scan. Renewed for each scan/retry and cancelled when the
    /// scan review modal closes, so an aborted scan doesn't keep its API call (and the awaiting
    /// command chain) alive after the user has moved on.
    /// </summary>
    private CancellationTokenSource? _scanCancellationSource;

    #endregion

    #region Bulk Scan Commands

    public async void OpenBulkDropZone()
    {
        BulkItems.Clear();
        BulkDropMessage = string.Empty;
        BulkScansCompleted = 0;
        BulkScansSucceeded = 0;
        BulkScansFailed = 0;
        IsBulkDropZoneOpen = true;
        IsBulkScanning = false;
        IsBulkScanComplete = false;
        IsBulkReviewOpen = false;

        // Fetch current usage so the summary text is accurate
        _usageService ??= CreateUsageService();
        if (_usageService != null)
        {
            try
            {
                var usageCheck = await _usageService.CheckUsageAsync();
                UpdateUsageDisplay(usageCheck);
                OnPropertyChanged(nameof(BulkUsageSummary));
            }
            catch { /* Non-critical, summary will show 0/0 until scan starts */ }
        }
    }

    public void AddFilesToQueue(IEnumerable<string> filePaths)
    {
        var added = 0;
        var skippedUnsupported = 0;
        var skippedDuplicate = 0;

        foreach (var path in filePaths)
        {
            if (!FilePickerTypes.IsSupportedReceiptFile(path))
            {
                skippedUnsupported++;
                continue;
            }

            if (BulkItems.Any(i => i.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                skippedDuplicate++;
                continue;
            }

            var item = new BulkScanItem
            {
                FilePath = path,
                FileName = Path.GetFileName(path)
            };
            BulkItems.Add(item);
            added++;

            // Generate preview thumbnail in background for the drop zone cards
            _ = GenerateQueuePreviewAsync(item);
        }

        BulkDropMessage = BuildDropMessage(added, skippedUnsupported, skippedDuplicate);

        OnPropertyChanged(nameof(BulkApprovedCount));
        OnPropertyChanged(nameof(BulkUsageSummary));
    }

    /// <summary>
    /// Builds the drop-zone feedback line. Returns empty when every file was added
    /// (a clean drop needs no message), otherwise explains what was skipped.
    /// </summary>
    private static string BuildDropMessage(int added, int skippedUnsupported, int skippedDuplicate)
    {
        if (skippedUnsupported == 0 && skippedDuplicate == 0)
            return string.Empty;

        var reasons = new List<string>();
        if (skippedUnsupported > 0)
            reasons.Add($"{skippedUnsupported} skipped (unsupported format)");
        if (skippedDuplicate > 0)
            reasons.Add($"{skippedDuplicate} skipped (already added)");
        var skipped = string.Join(", ", reasons);

        // When nothing was added, spell out which formats are accepted so the user
        // knows what to try instead.
        return added > 0
            ? $"{added} added. {skipped}."
            : $"{skipped}. Supported formats: {FilePickerTypes.SupportedReceiptFormats}.";
    }

    private static async Task GenerateQueuePreviewAsync(BulkScanItem item)
    {
        try
        {
            var fileData = await SharedFileReader.ReadAllBytesAsync(item.FilePath);
            var isPdf = item.IsPdf;

            var tempDir = Path.Combine(Path.GetTempPath(), "ArgoBooks", "BulkScanPreview");
            Directory.CreateDirectory(tempDir);
            var thumbPath = Path.Combine(tempDir,
                $"{Path.GetFileNameWithoutExtension(item.FileName)}_{Guid.NewGuid():N}_thumb.jpg");

            byte[]? thumbBytes = null;
            await Task.Run(async () =>
            {
                // Use lightweight thumbnail for queue cards, skip heavy OCR preprocessing
                if (isPdf)
                {
                    // Capture the page count so the scanning label can warn about multi-page PDFs.
                    var rendered = await PdfThumbnailService.Instance.RenderPdfFirstPageAsync(fileData);
                    thumbBytes = rendered?.Image;
                    if (rendered != null)
                    {
                        var pageCount = rendered.Value.PageCount;
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => item.PageCount = pageCount);
                    }
                }
                else
                {
                    thumbBytes = ReceiptImageHelper.GenerateThumbnail(fileData);
                }

                if (thumbBytes != null)
                    await File.WriteAllBytesAsync(thumbPath, thumbBytes);
            });

            if (thumbBytes != null)
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    item.ThumbnailPath = thumbPath);
        }
        catch
        {
            // Non-critical, card will show placeholder icon
        }
    }

    [RelayCommand]
    private void RemoveFromQueue(BulkScanItem? item)
    {
        if (item != null)
        {
            BulkItems.Remove(item);
            OnPropertyChanged(nameof(BulkUsageSummary));
        }
    }

    [RelayCommand]
    private void CloseBulkDropZone()
    {
        IsBulkDropZoneOpen = false;
        BulkItems.Clear();
    }

    /// <summary>
    /// Requests to close the drop zone, showing a discard confirmation if files are queued.
    /// </summary>
    [RelayCommand]
    private async Task RequestCloseBulkDropZone()
    {
        if (BulkItems.Count > 0)
        {
            if (!await ConfirmDiscardNewAsync()) return;
        }

        CloseBulkDropZone();
    }

    [RelayCommand]
    private async Task StartBulkScan()
    {
        if (BulkItems.Count == 0) return;

        // A single receipt doesn't need the bulk review carousel. Route it through the
        // standard single-scan flow so the user just reviews the result and clicks Add.
        if (BulkItems.Count == 1)
        {
            var single = BulkItems[0];
            IsBulkDropZoneOpen = false;
            BulkItems.Clear();

            // Launch the single-scan modal without awaiting it. This command is an
            // AsyncRelayCommand (AllowConcurrentExecutions = false), so awaiting the whole scan
            // here would keep the command "running" - and the Scan All button disabled - for the
            // entire scan. Decoupling lets the command finish immediately once the modal is up, so
            // the button re-enables right away even if the previous scan was cancelled mid-flight.
            // OpenScanModalAsync handles its own errors and drives the modal's own state.
            _ = OpenScanModalAsync(single.FilePath);
            return;
        }

        IsBulkDropZoneOpen = false;
        IsBulkScanning = true;
        IsBulkScanComplete = false;
        BulkScansCompleted = 0;
        BulkScansSucceeded = 0;
        BulkScansFailed = 0;

        _scannerService ??= CreateScannerService();
        _usageService ??= CreateUsageService();
        _bulkCancellationSource = new CancellationTokenSource();
        _bulkScanLimitMessage = null;
        var token = _bulkCancellationSource.Token;

        if (_usageService != null)
        {
            var usageCheck = await _usageService.CheckUsageAsync();
            UpdateUsageDisplay(usageCheck);

            if (!usageCheck.Allowed)
            {
                IsBulkScanning = false;
                if (!string.IsNullOrEmpty(usageCheck.ErrorMessage))
                {
                    await UpgradePromptHelper.ShowUsageCheckFailedAsync(usageCheck.ErrorMessage);
                }
                else
                {
                    await UpgradePromptHelper.ShowReceiptScanLimitPromptAsync(
                        usageCheck.Used, usageCheck.MonthlyLimit, usageCheck.ResetsAt);
                }
                return;
            }

            // Ask, then scan only what the allowance covers.
            if (!usageCheck.IsOffline && usageCheck.Remaining < BulkItems.Count)
            {
                ScansRemaining = usageCheck.Remaining;

                var proceed = await UpgradePromptHelper.ConfirmPartialReceiptScanAsync(
                    BulkItems.Count, usageCheck.Remaining, usageCheck.MonthlyLimit, usageCheck.ResetsAt);

                if (!proceed)
                {
                    IsBulkScanning = false;
                    return;
                }

                // Drop the ones the allowance does not cover before any scanning starts.
                // The dialog named this number, and the list is what the user watches, so
                // leaving them queued would look stuck and scanning them would overspend.
                while (BulkItems.Count > usageCheck.Remaining)
                {
                    BulkItems.RemoveAt(BulkItems.Count - 1);
                }
            }
        }

        // Pipeline: each item reads, preprocesses, generates preview, and scans
        // in a single task. SemaphoreSlim gates concurrency across all stages.
        //
        // Three rather than five: at five, observed scans averaged 39 seconds and the
        // slowest successful one took 85, because the requests compete with each other
        // upstream. Fewer in flight means each finishes sooner, which matters more than
        // raw parallelism when a slow scan is what pushes one past its timeout.
        var semaphore = new SemaphoreSlim(3);
        var tasks = BulkItems
            .Select(item => ProcessAndScanItemAsync(item, semaphore, token))
            .ToList();

        await Task.WhenAll(tasks);

        IsBulkScanning = false;
        IsBulkScanComplete = true;
    }

    /// <summary>
    /// Pipelines a single item through: read file → preprocess → generate preview → scan API.
    /// All stages run within the semaphore so up to 3 items process simultaneously.
    /// </summary>
    private async Task ProcessAndScanItemAsync(BulkScanItem item, SemaphoreSlim semaphore, CancellationToken token)
    {
        try
        {
            await semaphore.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Cancelled while waiting for a concurrency slot. The semaphore was never
            // acquired, so it must not be released. Mark the item cancelled and finish
            // cleanly so the task doesn't fault and crash Task.WhenAll in StartBulkScan.
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                item.Status = BulkScanStatus.Failed;
                item.ErrorMessage = "Cancelled";
                BulkScansCompleted++;
                BulkScansFailed++;
            });
            return;
        }

        try
        {
            if (token.IsCancellationRequested) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                item.Status = BulkScanStatus.Scanning);

            // 1. Read file
            item.FileData = await SharedFileReader.ReadAllBytesAsync(item.FilePath, token);
            var isPdf = item.IsPdf;
            var fileData = item.FileData;
            var fileName = item.FileName;

            // 2. Preprocess (CPU-bound: EXIF fix, contrast, sharpen)
            var convertedToJpeg = false;
            item.PreprocessedData = await Task.Run(
                () => ReceiptImageHelper.PreprocessForOcr(fileData, fileName, out convertedToJpeg), token);
            // Only a real re-encode earns the .jpg name. HEIC comes back untouched.
            item.ScanFileName = convertedToJpeg ? Path.ChangeExtension(fileName, ".jpg") : fileName;

            // 3. Generate preview (fire-and-forget, non-blocking), skip if already generated in queue
            if (string.IsNullOrEmpty(item.PreviewImagePath))
            {
                _ = Task.Run(async () =>
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "ArgoBooks", "BulkScanPreview");
                    Directory.CreateDirectory(tempDir);
                    var previewPath = Path.Combine(tempDir, $"{Path.GetFileNameWithoutExtension(fileName)}_{Guid.NewGuid():N}.jpg");

                    byte[]? previewBytes;
                    if (isPdf)
                    {
                        var rendered = await PdfThumbnailService.Instance.RenderPdfFirstPageAsync(fileData);
                        previewBytes = rendered?.Image;
                        if (rendered != null)
                        {
                            var pageCount = rendered.Value.PageCount;
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => item.PageCount = pageCount);
                        }
                    }
                    else
                    {
                        previewBytes = item.PreprocessedData;
                    }

                    if (previewBytes != null)
                    {
                        await File.WriteAllBytesAsync(previewPath, previewBytes);
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            item.PreviewImagePath = previewPath);
                    }
                }, token);
            }

            // Free original file bytes, local `fileData` var keeps the reference for the preview task
            item.FileData = null;

            // 4. Check usage
            //
            // The server is the authority and refuses on its own, so this is here to avoid
            // sending a request that is certain to be refused. It cannot be relied on alone:
            // three items scan at once, so the allowance can run out between this check and the
            // request below. That race is what the _bulkScanLimitMessage guard covers.
            if (_bulkScanLimitMessage != null)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    item.Status = BulkScanStatus.Failed;
                    item.ErrorMessage = _bulkScanLimitMessage;
                    BulkScansCompleted++;
                    BulkScansFailed++;
                });
                return;
            }

            if (_usageService != null)
            {
                var usageCheck = await _usageService.CheckUsageAsync();
                if (!usageCheck.Allowed)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        item.Status = BulkScanStatus.Failed;
                        item.ErrorMessage = usageCheck.ErrorMessage?.Translate() ?? "Monthly scan limit reached".Translate();
                        BulkScansCompleted++;
                        BulkScansFailed++;
                    });
                    return;
                }
            }

            // 5. Scan via API
            var result = await Task.Run(() =>
                _scannerService!.ScanReceiptAsync(item.PreprocessedData!, item.ScanFileName, skipPreprocessing: true, token), token);

            if (result.IsSuccess)
            {
                if (_usageService != null)
                    await _usageService.IncrementUsageAsync();

                var supplier = result.SupplierName ?? "Unknown";
                var total = result.TotalAmount?.ToString("F2") ?? "0.00";

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    item.ScanResult = result;
                    item.Status = BulkScanStatus.Succeeded;
                    item.SummaryText = $"{supplier} · ${total}";
                    item.ConfidenceText = result.Confidence >= 0.85 ? "High" : result.Confidence >= 0.6 ? "Medium" : "Low";
                    BulkScansCompleted++;
                    BulkScansSucceeded++;
                });
            }
            else
            {
                // Once the allowance is gone every item still queued would be refused too, so
                // the reason is recorded here and the rest short-circuit rather than each
                // spending a request to be told the same thing.
                if (result.IsScanLimitReached)
                {
                    _bulkScanLimitMessage = result.ErrorMessage ?? "Monthly scan limit reached".Translate();
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    item.Status = BulkScanStatus.Failed;
                    item.ErrorMessage = result.ErrorMessage ?? "Unknown error";
                    BulkScansCompleted++;
                    BulkScansFailed++;
                });
            }
        }
        catch (OperationCanceledException)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                item.Status = BulkScanStatus.Failed;
                item.ErrorMessage = "Cancelled";
                BulkScansCompleted++;
                BulkScansFailed++;
            });
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                item.Status = BulkScanStatus.Failed;
                item.ErrorMessage = ex.Message;
                BulkScansCompleted++;
                BulkScansFailed++;
            });
        }
        finally
        {
            semaphore.Release();
        }
    }

    [RelayCommand]
    private async Task RetryBulkItem(BulkScanItem? item)
    {
        if (item == null || item.PreprocessedData == null) return;

        // Don't allow retry while scanning is still in progress (would cause duplicate concurrent scan)
        if (IsBulkScanning) return;

        // Reset cancelled token if user cancelled a previous batch
        if (_bulkCancellationSource?.IsCancellationRequested == true)
            _bulkCancellationSource = new CancellationTokenSource();

        item.Status = BulkScanStatus.Queued;
        item.ErrorMessage = string.Empty;
        BulkScansFailed--;
        BulkScansCompleted--;

        var semaphore = new SemaphoreSlim(1);
        await ProcessAndScanItemAsync(item, semaphore, _bulkCancellationSource?.Token ?? CancellationToken.None);
    }

    [RelayCommand]
    private void CancelBulkScan()
    {
        // Signal cancellation, Task.WhenAll in StartBulkScan will set IsBulkScanComplete
        // when all tasks finish. Don't set it here to avoid counter corruption from
        // tasks that complete after this point.
        _bulkCancellationSource?.Cancel();
    }

    /// <summary>
    /// Requests to cancel bulk scan with discard confirmation.
    /// </summary>
    [RelayCommand]
    private async Task RequestCancelBulkScan()
    {
        if (!await ConfirmDiscardNewAsync()) return;
        CancelBulkScan();
        CloseBulkReview();
    }

    [RelayCommand]
    private void OpenBulkReview()
    {
        var succeeded = BulkSucceededItems;
        if (succeeded.Count == 0) return;

        IsBulkScanComplete = false;
        IsBulkReviewOpen = true;

        LoadSupplierOptions();
        LoadProductOptions();

        NavigateToBulkItem(0);
    }

    private void NavigateToBulkItem(int index)
    {
        var succeeded = BulkSucceededItems;
        if (index < 0 || index >= succeeded.Count) return;

        // Save current form state before navigating away and mark as reviewed
        if (CurrentBulkItem != null)
        {
            SaveCurrentFormToItem(CurrentBulkItem);
            CurrentBulkItem.IsReviewed = true;
            CurrentBulkItem.IsActive = false;
            OnPropertyChanged(nameof(BulkAllReviewed));
        }

        CurrentBulkIndex = index;
        CurrentBulkItem = succeeded[index];
        CurrentBulkItem.IsActive = true;

        PopulateScanResultsForBulkItem(CurrentBulkItem);
        ValidateCurrentBulkItem();
    }

    [RelayCommand]
    private void NavigateToBulkItemByRef(BulkScanItem? item)
    {
        if (item == null) return;
        var succeeded = BulkSucceededItems;
        var index = ((IList<BulkScanItem>)succeeded).IndexOf(item);
        if (index >= 0)
            NavigateToBulkItem(index);
    }

    private void PopulateScanResultsForBulkItem(BulkScanItem item)
    {
        if (item.ScanResult == null) return;

        _currentImageData = item.PreprocessedData;
        _currentFileName = item.ScanFileName;

        // Show all pages for PDFs (PreprocessedData holds the raw PDF bytes), else the preview image.
        if (item.IsPdf && item.PreprocessedData != null)
            _ = LoadPreviewPagesAsync(item.PreprocessedData, item.FileName, isPdf: true, "BulkScanPreview");
        else
            SetScanPreview(item.PreviewImagePath);

        // Only suppress AI suggestions if they've already run for this item.
        // On first visit, let them run so supplier matching/suggestion works.
        _suppressAiSuggestions = item.HasAiSuggestionsRun;
        PopulateScanResults(item.ScanResult);
        _suppressAiSuggestions = false;

        // Restore per-item state that isn't on ScanResult
        Notes = item.Notes;

        if (item.CurrencyCode != null)
            SelectedScanCurrency = CurrencyService.GetDisplayString(item.CurrencyCode);

        if (item.IsRevenueOverride.HasValue)
            IsRevenue = item.IsRevenueOverride.Value;

        if (item.SelectedSupplierId != null)
        {
            var supplier = SupplierOptions.FirstOrDefault(s => s.Id == item.SelectedSupplierId);
            if (supplier != null)
                SelectedSupplier = supplier;
        }

        // Restore supplier suggestion state from previous visit
        if (item.HasAiSuggestionsRun && item.SelectedSupplierId == null)
        {
            ShowCreateSupplierSuggestion = item.ShowCreateSupplierSuggestion;
            SuggestedSupplierName = item.SuggestedSupplierName ?? string.Empty;
        }
    }

    [RelayCommand]
    private void ApproveBulkItem()
    {
        if (CurrentBulkItem == null) return;

        SaveCurrentFormToItem(CurrentBulkItem);
        CurrentBulkItem.IsApproved = true;
        CurrentBulkItem.IsSkipped = false;
        CurrentBulkItem.IsReviewed = true;
        OnPropertyChanged(nameof(BulkApprovedCount));
        OnPropertyChanged(nameof(BulkAllReviewed));

        var succeeded = BulkSucceededItems;
        var nextIndex = -1;

        for (var i = CurrentBulkIndex + 1; i < succeeded.Count; i++)
        {
            if (!succeeded[i].IsApproved)
            {
                nextIndex = i;
                break;
            }
        }

        if (nextIndex == -1)
        {
            for (var i = 0; i < CurrentBulkIndex; i++)
            {
                if (!succeeded[i].IsApproved)
                {
                    nextIndex = i;
                    break;
                }
            }
        }

        if (nextIndex >= 0)
            NavigateToBulkItem(nextIndex);
    }

    [RelayCommand]
    private void UnapproveBulkItem()
    {
        if (CurrentBulkItem == null) return;
        CurrentBulkItem.IsApproved = false;
        OnPropertyChanged(nameof(BulkApprovedCount));
        OnPropertyChanged(nameof(BulkAllReviewed));
        OnPropertyChanged(nameof(BulkIncompleteCount));
        OnPropertyChanged(nameof(HasBulkIncompleteItems));
    }

    [RelayCommand]
    private void SkipBulkItem()
    {
        if (CurrentBulkItem != null)
        {
            CurrentBulkItem.IsApproved = false;
            CurrentBulkItem.IsSkipped = true;
            CurrentBulkItem.IsReviewed = true;
            OnPropertyChanged(nameof(BulkApprovedCount));
            OnPropertyChanged(nameof(BulkAllReviewed));
            OnPropertyChanged(nameof(BulkIncompleteCount));
        OnPropertyChanged(nameof(HasBulkIncompleteItems));
        }

        var succeeded = BulkSucceededItems;
        var nextIndex = CurrentBulkIndex + 1;
        if (nextIndex < succeeded.Count)
            NavigateToBulkItem(nextIndex);
    }

    private void SaveCurrentFormToItem(BulkScanItem item)
    {
        if (item.ScanResult == null) return;

        item.ScanResult.SupplierName = ExtractedSupplier;
        item.ScanResult.TransactionDate = ExtractedDate?.DateTime;

        if (decimal.TryParse(ExtractedSubtotal, out var sub)) item.ScanResult.Subtotal = sub;
        if (decimal.TryParse(ExtractedTax, out var tax)) item.ScanResult.TaxAmount = tax;
        if (decimal.TryParse(ExtractedTotal, out var tot)) item.ScanResult.TotalAmount = tot;
        if (decimal.TryParse(ExtractedDiscount, out var disc)) item.ScanResult.Discount = disc;
        if (decimal.TryParse(ExtractedShipping, out var ship)) item.ScanResult.Shipping = ship;

        item.ScanResult.PaymentMethod = SelectedPaymentMethod;

        // Persist per-item state that isn't on ScanResult
        item.Notes = Notes;
        item.CurrencyCode = ScanCurrencyCode;
        item.IsRevenueOverride = IsRevenue;
        item.SelectedSupplierId = SelectedSupplier?.Id;
        item.ShowCreateSupplierSuggestion = ShowCreateSupplierSuggestion;
        item.SuggestedSupplierName = SuggestedSupplierName;
        item.HasAiSuggestionsRun = true;

        item.ScanResult.LineItems = LineItems.Select(li =>
        {
            decimal.TryParse(li.Quantity, out var qty);
            decimal.TryParse(li.UnitPrice, out var up);
            decimal.TryParse(li.TotalPrice, out var tp);
            return new ScannedLineItem
            {
                Description = li.Description,
                Quantity = qty,
                UnitPrice = up,
                TotalPrice = tp,
                Confidence = li.Confidence
            };
        }).ToList();

        item.LineItemProductIds = LineItems.Select(li => li.SelectedProduct?.Id).ToList();

        var supplier = ExtractedSupplier;
        if (string.IsNullOrEmpty(supplier) && SelectedSupplier != null)
            supplier = SelectedSupplier.Name;
        item.SummaryText = $"{supplier} · ${ExtractedTotal}";

        item.UpdateValidation();
        OnPropertyChanged(nameof(BulkAllReviewed));
        OnPropertyChanged(nameof(BulkIncompleteCount));
        OnPropertyChanged(nameof(HasBulkIncompleteItems));
    }

    [RelayCommand]
    private async Task CreateAllApprovedTransactions()
    {
        var approvedItems = BulkItems.Where(i => i.IsApproved && i.ScanResult != null).ToList();
        if (approvedItems.Count == 0) return;

        if (CurrentBulkItem != null && CurrentBulkItem.IsApproved)
            SaveCurrentFormToItem(CurrentBulkItem);

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var createdExpenses = new List<Expense>();
        var createdRevenues = new List<Revenue>();
        var createdReceipts = new List<Receipt>();

        // Pre-encode all receipt images in parallel (CPU-bound FixOrientation + Base64)
        var encodedImages = await Task.Run(() =>
        {
            var results = new Dictionary<BulkScanItem, string?>();
            Parallel.ForEach(approvedItems, item =>
            {
                string? encoded = null;
                if (item.PreprocessedData != null)
                {
                    var orientedData = ReceiptImageHelper.FixOrientation(item.PreprocessedData);
                    encoded = Convert.ToBase64String(orientedData);
                }
                lock (results) results[item] = encoded;
            });
            return results;
        });

        foreach (var item in approvedItems)
        {
            var scanResult = item.ScanResult!;
            var total = scanResult.TotalAmount ?? 0;
            var subtotal = scanResult.Subtotal ?? 0;
            var taxAmount = scanResult.TaxAmount ?? 0;
            var discount = scanResult.Discount ?? 0;
            var shipping = scanResult.Shipping ?? 0;
            var supplierName = scanResult.SupplierName ?? string.Empty;
            var transactionDate = scanResult.TransactionDate ?? DateTime.Now;
            var currency = item.CurrencyCode ?? ReceiptCurrency(scanResult.CurrencyCode);
            // Fetch this receipt's date rate up front so the row shows its amount, not "Pending".
            await CurrencyService.WarmRateForDateAsync(transactionDate, currency);
            var isRevenue = item.IsRevenueOverride ?? false;
            var notes = item.Notes ?? string.Empty;
            var paymentMethod = scanResult.PaymentMethod ?? "Cash";

            if (total <= 0) continue;

            // Build line items directly from saved scan result + product IDs
            var productIds = item.LineItemProductIds;
            var lineItems = scanResult.LineItems.Select((li, idx) =>
            {
                var productId = productIds != null && idx < productIds.Count ? productIds[idx] : null;
                Product? product = null;
                if (productId != null)
                    product = companyData.Products?.FirstOrDefault(p => p.Id == productId);
                return new LineItem
                {
                    ProductId = productId,
                    Description = product?.Name ?? li.Description,
                    Quantity = li.Quantity > 0 ? li.Quantity : 1,
                    UnitPrice = li.UnitPrice
                };
            }).Where(li => !string.IsNullOrWhiteSpace(li.Description) || li.ProductId != null).ToList();

            var receiptId = new IdGenerator(companyData).NextReceiptId();

            var fileData = encodedImages.GetValueOrDefault(item);

            var receipt = new Receipt
            {
                Id = receiptId,
                TransactionType = isRevenue ? "Revenue" : "Expense",
                FileName = item.ScanFileName,
                FileType = GetMimeType(item.ScanFileName),
                FileSize = item.PreprocessedData?.Length ?? 0,
                FileData = fileData,
                Amount = total,
                Date = transactionDate,
                Supplier = supplierName,
                Source = "AI Scanned",
                OcrData = CreateOcrDataFromScanResult(scanResult),
                CreatedAt = DateTime.UtcNow
            };

            if (isRevenue)
            {
                var revenueId = new Core.Data.IdGenerator(companyData).NextRevenueId(transactionDate);

                var revenue = new Revenue
                {
                    Id = revenueId,
                    Date = transactionDate,
                    CustomerId = null,
                    Description = lineItems.Count > 0 ? lineItems[0].Description : supplierName,
                    LineItems = lineItems,
                    Quantity = lineItems.Sum(li => li.Quantity),
                    UnitPrice = lineItems.Count > 0 ? lineItems.Average(li => li.UnitPrice) : subtotal,
                    Amount = subtotal > 0 ? subtotal : total,
                    Subtotal = subtotal > 0 ? subtotal : total,
                    TaxRate = subtotal > 0 && taxAmount > 0 ? (taxAmount / subtotal) * 100 : 0,
                    TaxAmount = taxAmount,
                    Discount = discount,
                    ShippingCost = shipping,
                    Total = total,
                    PaymentMethod = Enum.TryParse<PaymentMethod>(paymentMethod.Replace(" ", ""), out var rpm) ? rpm : PaymentMethod.Cash,
                    PaymentStatus = RevenuePaymentStatus.Paid,
                    Notes = notes,
                    ReceiptId = receiptId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                ApplyDisplayCurrency(companyData, revenue, currency);

                receipt.TransactionId = revenueId;
                companyData.Revenues.Add(revenue);
                createdRevenues.Add(revenue);
            }
            else
            {
                var expenseId = new Core.Data.IdGenerator(companyData).NextExpenseId(transactionDate);

                var expense = new Expense
                {
                    Id = expenseId,
                    Date = transactionDate,
                    SupplierId = item.SelectedSupplierId,
                    Description = lineItems.Count > 0 ? lineItems[0].Description : supplierName,
                    LineItems = lineItems,
                    Quantity = lineItems.Sum(li => li.Quantity),
                    UnitPrice = lineItems.Count > 0 ? lineItems.Average(li => li.UnitPrice) : subtotal,
                    Amount = subtotal > 0 ? subtotal : total,
                    TaxRate = subtotal > 0 && taxAmount > 0 ? (taxAmount / subtotal) * 100 : 0,
                    TaxAmount = taxAmount,
                    Discount = discount,
                    ShippingCost = shipping,
                    Total = total,
                    PaymentMethod = Enum.TryParse<PaymentMethod>(paymentMethod.Replace(" ", ""), out var epm) ? epm : PaymentMethod.Cash,
                    Notes = notes,
                    ReceiptId = receiptId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                ApplyDisplayCurrency(companyData, expense, currency);

                receipt.TransactionId = expenseId;
                companyData.Expenses.Add(expense);
                createdExpenses.Add(expense);
            }

            companyData.Receipts.Add(receipt);
            createdReceipts.Add(receipt);
        }

        RemoveCreatedEntitiesNotUsedBy(companyData, createdExpenses, createdRevenues);
        var createdEntities = CaptureCreatedEntities();
        var createdTransactions = createdExpenses.Cast<Transaction>().Concat(createdRevenues).ToList();

        var action = new DelegateAction(
            $"Bulk scan {approvedItems.Count} receipts",
            () =>
            {
                foreach (var e in createdExpenses) companyData.Expenses.RemoveRecord(e);
                foreach (var r in createdRevenues) companyData.Revenues.RemoveRecord(r);
                foreach (var r in createdReceipts) companyData.Receipts.RemoveRecord(r);
                WithdrawPendingConversions(companyData, createdTransactions);
                createdEntities.Remove(companyData);
            },
            () =>
            {
                createdEntities.Restore(companyData);
                foreach (var e in createdExpenses) companyData.Expenses.RestoreRecord(e);
                foreach (var r in createdRevenues) companyData.Revenues.RestoreRecord(r);
                foreach (var r in createdReceipts) companyData.Receipts.RestoreRecord(r);
                RequeuePendingConversions(companyData, createdTransactions);
            });

        // The auto-created entities now belong to the undo action, so closing must not roll them back.
        _createdEntitiesCommitted = true;

        App.UndoRedoManager.RecordAction(action);
        App.CompanyManager?.MarkAsChanged();
        ReceiptScanned?.Invoke(this, EventArgs.Empty);
        CloseBulkReview();
    }

    [RelayCommand]
    private void CloseBulkReview()
    {
        IsBulkReviewOpen = false;
        IsBulkScanComplete = false;
        IsBulkScanning = false;
        _bulkSucceededItemsCache = null;
        BulkItems.Clear();
        CurrentBulkItem = null;

        // As in the single scan, what the review created from suggestions only stays once the
        // approved receipts are saved.
        if (!_createdEntitiesCommitted)
            RollbackUncommittedCreatedEntities();

        ResetScanModal();
    }

    /// <summary>
    /// Requests to close bulk review/complete modals, showing discard confirmation.
    /// </summary>
    [RelayCommand]
    private async Task RequestCloseBulkReview()
    {
        if (!await ConfirmDiscardNewAsync()) return;
        CloseBulkReview();
    }

    #endregion

    #region AI Scan Commands

    /// <summary>
    /// Checks whether the user can scan a receipt. If the limit is reached,
    /// shows the upgrade prompt and returns false.
    /// Call this before opening a file picker.
    /// </summary>
    public async Task<bool> CanScanOrShowLimitAsync()
    {
        _usageService ??= CreateUsageService();
        var usageCheck = await _usageService.CheckUsageAsync();
        if (!usageCheck.Allowed)
        {
            if (!string.IsNullOrEmpty(usageCheck.ErrorMessage))
            {
                await UpgradePromptHelper.ShowUsageCheckFailedAsync(usageCheck.ErrorMessage);
            }
            else
            {
                await UpgradePromptHelper.ShowReceiptScanLimitPromptAsync(
                    usageCheck.Used,
                    usageCheck.MonthlyLimit,
                    usageCheck.ResetsAt);
            }
            return false;
        }
        return true;
    }

    /// <summary>
    /// Opens the scan review modal and starts scanning.
    /// </summary>
    public async Task OpenScanModalAsync(string filePath)
    {
        // Before the file check: picking an unreadable file is still an attempt.
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ReceiptScanOpened);

        if (!File.Exists(filePath))
        {
            await App.ShowErrorDialogAsync(
                "Error".Translate(),
                "File not found.".Translate());
            return;
        }

        try
        {
            var imageData = await SharedFileReader.ReadAllBytesAsync(filePath);
            var fileName = Path.GetFileName(filePath);
            await OpenScanModalWithDataAsync(imageData, fileName, filePath);
        }
        catch (Exception ex)
        {
            // If file read fails before OpenScanModalWithDataAsync, show error in modal
            IsScanReviewModalOpen = true;
            IsScanning = false;
            HasScanError = true;
            ScanErrorMessage = "Failed to read file: {0}".TranslateFormat(ex.Message);
        }
    }

    /// <summary>
    /// Opens the scan review modal with image data and starts scanning.
    /// </summary>
    public async Task OpenScanModalWithDataAsync(byte[] imageData, string fileName, string? tempFilePath = null)
    {
        // Reset state first, before setting new data
        ResetScanModal();

        _currentImageData = imageData;
        _currentFileName = fileName;

        // Show modal immediately with loading state
        IsScanReviewModalOpen = true;
        IsScanning = true;
        HasScanError = false;
        HasScanResult = false;

        // Yield to let the UI render the modal and start the spinner before doing sync work
        await Task.Delay(1);

        LoadSupplierOptions();
        LoadProductOptions();

        // Preprocess the image once on a background thread (EXIF fix + contrast + sharpen).
        // The result is used for both the preview image and the API call, avoiding
        // a redundant FixOrientation decode/encode cycle. PDFs are returned unchanged.
        var isPdf = Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        var convertedToJpeg = false;
        var preprocessedData = await Task.Run(
            () => ReceiptImageHelper.PreprocessForOcr(imageData, fileName, out convertedToJpeg));
        _currentImageData = preprocessedData;
        _currentFileName = convertedToJpeg ? Path.ChangeExtension(fileName, ".jpg") : fileName;

        // Render preview pages off the UI thread (all pages for PDFs, single image otherwise).
        _ = LoadPreviewPagesAsync(preprocessedData, fileName, isPdf, "ScanPreview");

        // Start scanning (image is already preprocessed)
        await ScanReceiptAsync();
    }

    /// <summary>
    /// Opens the scan review modal with the bundled sample receipt and simulates a scan.
    /// Backs the "Scan a receipt" setup step so a brand-new user can see the scanner work
    /// without having a receipt to hand.
    ///
    /// The parse is canned rather than a real API call, which keeps it off the monthly scan
    /// quota, working offline, and immune to a scanning failure ruining a first run. What it
    /// produces is NOT fake: the user still reviews and confirms, and the category, product
    /// and transaction created are real records.
    ///
    /// Returns false when the sample asset is missing, so the caller can fall back to the
    /// normal file picker instead of dead-ending the checklist step.
    /// </summary>
    public async Task<bool> OpenScanModalWithSampleAsync()
    {
        var imageData = TryRenderSampleReceipt();
        if (imageData == null)
            return false;

        ResetScanModal();

        _currentImageData = imageData;
        _currentFileName = SampleReceiptFileName;

        IsScanReviewModalOpen = true;
        IsScanning = true;
        HasScanError = false;
        HasScanResult = false;

        // Yield so the modal and spinner render before the simulated work starts.
        await Task.Delay(1);

        LoadSupplierOptions();
        LoadProductOptions();

        _ = LoadPreviewPagesAsync(imageData, SampleReceiptFileName, isPdf: false, "ScanPreview");

        await SimulateSampleScanAsync();
        return true;
    }

    /// <summary>
    /// The sample receipt's contents. This is the single source of truth: both the drawn
    /// image and the canned scan result are built from it, so the receipt on screen and the
    /// extracted values can never disagree.
    ///
    /// It also exists because the date has to move. A static image ages, and a tutorial that
    /// files an expense from over a year ago drops a stale transaction into the user's books.
    /// Drawing the receipt at runtime keeps it a few days old forever.
    /// </summary>
    private sealed record SampleReceiptData(
        string SupplierName,
        string AddressLine,
        string CityLine,
        DateTime Date,
        IReadOnlyList<(string Name, decimal Price)> Items,
        decimal Subtotal,
        decimal Tax,
        decimal Total,
        decimal TaxRate);

    private static SampleReceiptData BuildSampleReceiptData()
    {
        // Office supplies rather than groceries: unambiguously a business expense, plausible
        // for any industry, and the category and products it creates are ones a real user
        // would be content to keep rather than having to clean up.
        (string Name, decimal Price)[] items =
        [
            ("Printer Paper, 500 sheets", 12.99m),
            ("Ink Cartridge, Black", 34.50m),
            ("Notebooks, pack of 5", 8.75m),
            ("Ballpoint Pens, box of 12", 4.25m)
        ];

        const decimal taxRate = 0.13m;
        var subtotal = items.Sum(i => i.Price);
        var tax = Math.Round(subtotal * taxRate, 2, MidpointRounding.AwayFromZero);

        return new SampleReceiptData(
            "Riverside Office Supply",
            "48 Riverside Drive",
            "Hamilton, ON  L8P 1A2",
            DateTime.Today.AddDays(-3),
            items,
            subtotal,
            tax,
            subtotal + tax,
            taxRate);
    }

    /// <summary>
    /// Draws the sample receipt to a PNG at runtime. Returns null on any failure rather than
    /// throwing into a first-run experience.
    /// </summary>
    private static byte[]? TryRenderSampleReceipt()
    {
        try
        {
            var data = BuildSampleReceiptData();

            const int width = 720;
            const int margin = 56;
            const float body = 26f;
            const float lineGap = 42f;

            var height = (int)(600 + data.Items.Count * lineGap);

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            using var ink = new SKPaint { Color = new SKColor(0x22, 0x22, 0x22), IsAntialias = true };
            using var rule = new SKPaint
            {
                Color = new SKColor(0xBB, 0xBB, 0xBB),
                IsAntialias = true,
                StrokeWidth = 2,
                Style = SKPaintStyle.Stroke
            };

            using var headerFont = new SKFont(Core.Services.PlatformTypefaces.Resolve("Arial", SKFontStyle.Bold), 42f);
            using var bodyFont = new SKFont(Core.Services.PlatformTypefaces.Resolve("Arial", SKFontStyle.Normal), body);
            using var totalFont = new SKFont(Core.Services.PlatformTypefaces.Resolve("Arial", SKFontStyle.Bold), 34f);

            var mid = width / 2f;
            const float left = margin;
            const float right = width - margin;
            var y = (float)margin + 42f;

            canvas.DrawText(data.SupplierName.ToUpperInvariant(), mid, y, SKTextAlign.Center, headerFont, ink);
            y += lineGap;
            canvas.DrawText(data.AddressLine, mid, y, SKTextAlign.Center, bodyFont, ink);
            y += lineGap * 0.8f;
            canvas.DrawText(data.CityLine, mid, y, SKTextAlign.Center, bodyFont, ink);
            y += lineGap * 0.9f;

            canvas.DrawLine(left, y, right, y, rule);
            y += lineGap;

            canvas.DrawText(data.Date.ToString("MMM d, yyyy"), left, y, SKTextAlign.Left, bodyFont, ink);
            canvas.DrawText("2:14 PM", right, y, SKTextAlign.Right, bodyFont, ink);
            y += lineGap * 0.5f;
            canvas.DrawLine(left, y, right, y, rule);
            y += lineGap;

            foreach (var (name, price) in data.Items)
            {
                canvas.DrawText(name, left, y, SKTextAlign.Left, bodyFont, ink);
                canvas.DrawText(price.ToString("0.00"), right, y, SKTextAlign.Right, bodyFont, ink);
                y += lineGap;
            }

            canvas.DrawLine(left, y - lineGap * 0.55f, right, y - lineGap * 0.55f, rule);

            canvas.DrawText("Subtotal", left, y, SKTextAlign.Left, bodyFont, ink);
            canvas.DrawText(data.Subtotal.ToString("0.00"), right, y, SKTextAlign.Right, bodyFont, ink);
            y += lineGap * 0.9f;

            canvas.DrawText($"Tax ({data.TaxRate:P0})", left, y, SKTextAlign.Left, bodyFont, ink);
            canvas.DrawText(data.Tax.ToString("0.00"), right, y, SKTextAlign.Right, bodyFont, ink);
            y += lineGap * 0.9f;

            canvas.DrawLine(left, y, right, y, rule);
            y += lineGap;

            canvas.DrawText("TOTAL", left, y, SKTextAlign.Left, totalFont, ink);
            canvas.DrawText($"${data.Total:0.00}", right, y, SKTextAlign.Right, totalFont, ink);
            y += lineGap * 1.3f;

            canvas.DrawText("Thank you for your business", mid, y, SKTextAlign.Center, bodyFont, ink);

            using var image = surface.Snapshot();
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded?.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Drives the scan progress UI for the sample receipt, then applies the canned result.
    /// The delay is deliberate: watching the scan work is the point of the step, and a result
    /// that appeared instantly would read as a mock-up rather than the product.
    /// </summary>
    private async Task SimulateSampleScanAsync()
    {
        ScanningMessage = "Scanning receipt...".Translate();
        ScanProgress = 0;
        ShowScanProgress = true;

        const int totalMs = 3200;
        const int steps = 32;
        for (var step = 1; step <= steps; step++)
        {
            await Task.Delay(totalMs / steps);
            ScanProgress = (double)step / steps * 100;
        }

        // Tracked separately from real scans via the context, so sample runs can be counted
        // (or filtered out) without polluting genuine scan numbers.
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ReceiptScanned, "sample");
        App.MainWindowViewModel?.NoteSampleReceiptScan();

        PopulateScanResults(BuildSampleScanResult());

        HasScanResult = true;
        IsScanning = false;
    }

    /// <summary>
    /// The canned parse for the sample receipt, built from the same data the image is drawn
    /// from so the two cannot drift apart.
    /// </summary>
    private static ReceiptScanResult BuildSampleScanResult()
    {
        var data = BuildSampleReceiptData();

        return new ReceiptScanResult
        {
            IsSuccess = true,
            SupplierName = data.SupplierName,
            TransactionDate = data.Date,
            Subtotal = data.Subtotal,
            TaxAmount = data.Tax,
            TotalAmount = data.Total,
            Discount = 0m,
            Shipping = 0m,
            Confidence = 0.97,
            PaymentMethod = "Card",
            LineItems = [.. data.Items.Select(item => new ScannedLineItem
            {
                Description = item.Name,
                Quantity = 1,
                UnitPrice = item.Price,
                TotalPrice = item.Price,
                Confidence = 0.97
            })]
        };
    }

    private async Task ScanReceiptAsync()
    {
        // Renew the cancellation source so closing the modal (or a retry) aborts this scan's API
        // call promptly instead of letting it run to completion in the background.
        _scanCancellationSource?.Cancel();
        _scanCancellationSource?.Dispose();
        _scanCancellationSource = new CancellationTokenSource();
        var token = _scanCancellationSource.Token;

        try
        {
            _scannerService ??= CreateScannerService();
            _usageService ??= CreateUsageService();

            if (_scannerService == null || !_scannerService.IsConfigured)
            {
                HasScanError = true;
                ScanErrorMessage = "AI Receipt Scanning requires an active premium license.".Translate();
                IsScanning = false;
                return;
            }

            if (_currentImageData == null || _currentFileName == null)
            {
                HasScanError = true;
                ScanErrorMessage = "No image data to scan.".Translate();
                IsScanning = false;
                return;
            }

            // Check usage limit before scanning
            ScanningMessage = "Checking usage limits...".Translate();
            ShowScanProgress = false;
            ScanProgress = 0;
            if (_usageService != null)
            {
                var usageCheck = await _usageService.CheckUsageAsync();
                UpdateUsageDisplay(usageCheck);

                if (!usageCheck.Allowed)
                {
                    IsScanning = false;

                    // A populated ErrorMessage means the check itself failed (offline or
                    // server unreachable), not that a real limit was hit. Surface that
                    // instead of a "0/0" scan-limit prompt.
                    if (!string.IsNullOrEmpty(usageCheck.ErrorMessage))
                    {
                        HasScanError = true;
                        ScanErrorMessage = usageCheck.ErrorMessage.Translate();
                    }
                    else
                    {
                        await UpgradePromptHelper.ShowReceiptScanLimitPromptAsync(
                            usageCheck.Used,
                            usageCheck.MonthlyLimit,
                            usageCheck.ResetsAt);
                    }
                    return;
                }
            }

            ScanningMessage = "Scanning receipt...".Translate();
            ScanProgress = 0;
            ShowScanProgress = true;

            // Run API call off the UI thread to keep the spinner smooth.
            // Image is already preprocessed in OpenScanModalWithDataAsync, so skip it here.
            var imageData = _currentImageData;
            var fileName = _currentFileName;
            using var scanTicker = new EstimatedProgressTicker(
                OperationKind.ReceiptScan, pct => ScanProgress = pct,
                sizeFeature: imageData.Length, uploadBytes: imageData.Length);
            scanTicker.Start();
            var result = await Task.Run(() => _scannerService.ScanReceiptAsync(imageData, fileName, skipPreprocessing: true, token), token);
            scanTicker.Complete();

            // If this scan was cancelled or superseded by a newer one while the API call was in
            // flight, the shared modal state now belongs to that newer scan. Bail out without
            // writing anything, otherwise a cancelled scan's late "cancelled/timed out" result
            // would clobber the result the user is actually looking at.
            if (token.IsCancellationRequested)
                return;

            if (!result.IsSuccess)
            {
                HasScanError = true;
                ScanErrorMessage = result.ErrorMessage ?? "Unknown error occurred.".Translate();
                IsScanning = false;
                return;
            }

            // Increment usage after successful scan
            if (_usageService != null)
            {
                var incrementResult = await _usageService.IncrementUsageAsync();
                if (incrementResult.Success && !incrementResult.IsOffline)
                {
                    ScansUsed = incrementResult.Used;
                    ScansRemaining = incrementResult.Remaining;
                    IsNearLimit = incrementResult.MonthlyLimit > 0 && incrementResult.Remaining > 0 && incrementResult.Remaining <= incrementResult.MonthlyLimit / 10;
                }
            }

            // Populate fields with extracted data
            PopulateScanResults(result);

            HasScanResult = true;
            IsScanning = false;
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled or superseded. The modal state was already reset by the caller (or
            // is owned by the newer scan), so don't touch any shared state here.
        }
        catch (Exception ex)
        {
            // A stale scan failing during teardown must not overwrite the current scan's state.
            if (token.IsCancellationRequested)
                return;

            HasScanError = true;
            ScanErrorMessage = "Failed to scan receipt: {0}".TranslateFormat(ex.Message);
            IsScanning = false;
        }
    }

    private void UpdateUsageDisplay(UsageCheckResult usageCheck)
    {
        if (usageCheck.IsOffline) return;
        HasUsageInfo = true;
        ScansUsed = usageCheck.Used;
        ScansLimit = usageCheck.MonthlyLimit;
        ScansRemaining = usageCheck.Remaining;
        UsageTier = usageCheck.Tier;
        ResetsAt = usageCheck.ResetsAt;
        IsNearLimit = usageCheck.MonthlyLimit > 0 && usageCheck.Remaining > 0 && usageCheck.Remaining <= usageCheck.MonthlyLimit / 10;
    }

    private void PopulateScanResults(ReceiptScanResult result)
    {
        try
        {
            SelectedScanCurrency = CurrencyService.GetDisplayString(ReceiptCurrency(result.CurrencyCode));
            ExtractedSupplier = result.SupplierName ?? string.Empty;
            ExtractedDate = result.TransactionDate.HasValue
                ? new DateTimeOffset(result.TransactionDate.Value)
                : DateTimeOffset.Now;
            ExtractedSubtotal = result.Subtotal?.ToString("F2") ?? "0.00";
            ExtractedTax = result.TaxAmount?.ToString("F2") ?? "0.00";
            ExtractedTotal = result.TotalAmount?.ToString("F2") ?? "0.00";

            // Detect if this is a revenue or expense based on merchant name matching company name
            DetectTransactionType(result.SupplierName);

            // Set payment method if detected
            if (!string.IsNullOrEmpty(result.PaymentMethod) && PaymentMethodOptions.Contains(result.PaymentMethod))
            {
                SelectedPaymentMethod = result.PaymentMethod;
            }

            // Confidence
            ConfidenceScore = result.Confidence;
            ConfidenceText = $"{result.Confidence:P0}";
            IsHighConfidence = result.Confidence >= 0.85;
            IsMediumConfidence = result.Confidence >= 0.6 && result.Confidence < 0.85;
            IsLowConfidence = result.Confidence < 0.6;

            // Use discount from scanner if available, fall back to line-item heuristic
            var discountTotal = result.Discount ?? result.LineItems
                .Where(IsDiscountLine)
                .Sum(item => Math.Abs(item.TotalPrice));
            ExtractedDiscount = discountTotal > 0 ? discountTotal.ToString("F2") : "0.00";

            ExtractedShipping = result.Shipping is > 0 ? result.Shipping.Value.ToString("F2") : "0.00";

            // Line items (filter out discounts and non-product lines)
            LineItems.Clear();
            foreach (var item in result.LineItems)
            {
                // Skip discount lines, negative-amount adjustments, and $0 items
                if (IsDiscountLine(item) || item.TotalPrice == 0)
                    continue;

                var lineItem = new ScannedLineItemViewModel
                {
                    Description = CleanOcrText(item.Description),
                    Quantity = item.Quantity.ToString("0.####"),
                    UnitPrice = item.UnitPrice.ToString("F2"),
                    TotalPrice = item.TotalPrice.ToString("F2"),
                    Confidence = item.Confidence
                };

                // Try to match to existing product
                TryMatchProduct(lineItem, item.Description);

                lineItem.OnProductErrorCleared = ClearValidationMessageIfNoErrors;
                lineItem.OnTotalPriceEdited = ValidateTotals;
                LineItems.Add(lineItem);
            }

            UpdateHasUnmatchedProducts();
            ValidateTotals();

            // Fire AI suggestions in the background, don't block showing scan results
            // Suppressed during bulk review carousel navigation to prevent cross-item suggestion corruption
            if (!_suppressAiSuggestions)
                _ = GetAiSuggestionsAsync(result);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "PopulateScanResults");
        }
    }

    [RelayCommand]
    private void CloseScanReviewModal()
    {
        // Abort any in-flight scan so its API call (and the command chain awaiting it) don't keep
        // running after the modal is dismissed, which would leave the Scan All button disabled.
        _scanCancellationSource?.Cancel();

        IsScanReviewModalOpen = false;
        IsFullscreen = false;

        // Suggested products/supplier/category are created eagerly while the user reviews.
        // If the modal is closed without adding the receipt, roll them back so they only
        // persist when a transaction is actually created. When a transaction was created,
        // ownership transfers to the undo action, so this is skipped.
        if (!_createdEntitiesCommitted)
            RollbackUncommittedCreatedEntities();

        ResetScanModal();
    }

    /// <summary>
    /// Removes supplier/category/products that were auto-created from receipt suggestions
    /// but never committed to a transaction.
    /// </summary>
    private void RollbackUncommittedCreatedEntities()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        if (CaptureCreatedEntities().Remove(companyData))
            companyData.MarkAsModified();
    }

    private CreatedEntities CaptureCreatedEntities()
        => new(_createdSuppliersForUndo.ToList(), _createdCategoryForUndo, _createdProductsForUndo.ToList());

    /// <summary>
    /// Removes the suggestion-created supplier/products/category that no saved transaction uses, such
    /// as ones created while viewing a receipt that was then skipped.
    /// </summary>
    private void RemoveCreatedEntitiesNotUsedBy(CompanyData companyData, List<Expense> expenses, List<Revenue> revenues)
    {
        var supplierIds = expenses.Select(e => e.SupplierId).OfType<string>().ToHashSet();
        var productIds = expenses.SelectMany(e => e.LineItems).Concat(revenues.SelectMany(r => r.LineItems))
            .Select(li => li.ProductId).OfType<string>().ToHashSet();

        var unusedSuppliers = _createdSuppliersForUndo.Where(s => !supplierIds.Contains(s.Id)).ToList();
        var unusedProducts = _createdProductsForUndo.Where(p => !productIds.Contains(p.Id)).ToList();
        _createdSuppliersForUndo.RemoveAll(unusedSuppliers.Contains);
        _createdProductsForUndo.RemoveAll(unusedProducts.Contains);

        var unusedCategory = _createdCategoryForUndo is { } category
                             && _createdProductsForUndo.All(p => p.CategoryId != category.Id)
            ? category
            : null;
        if (unusedCategory != null) _createdCategoryForUndo = null;

        new CreatedEntities(unusedSuppliers, unusedCategory, unusedProducts).Remove(companyData);
    }

    /// <summary>
    /// The supplier/category/products auto-created from suggestions during a review, captured so
    /// the undo of the transactions they were created for removes them and redo puts them back.
    /// </summary>
    private sealed record CreatedEntities(List<Supplier> Suppliers, Category? Category, List<Product> Products)
    {
        /// <summary>Returns true when anything was removed.</summary>
        public bool Remove(CompanyData companyData)
        {
            var removedAny = false;
            foreach (var product in Products)
                removedAny |= companyData.Products?.RemoveRecord(product) == true;
            if (Category != null)
                removedAny |= companyData.Categories.RemoveRecord(Category);
            foreach (var supplier in Suppliers)
                removedAny |= companyData.Suppliers.RemoveRecord(supplier);
            return removedAny;
        }

        public void Restore(CompanyData companyData)
        {
            foreach (var supplier in Suppliers)
                companyData.Suppliers.RestoreRecord(supplier);
            if (Category != null)
                companyData.Categories.RestoreRecord(Category);
            foreach (var product in Products)
                companyData.Products?.RestoreRecord(product);
        }
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
    }

    /// <summary>
    /// Requests to close the scan review modal, showing confirmation if scanning or data is present.
    /// </summary>
    [RelayCommand]
    private async Task RequestCloseScanReviewModalAsync()
    {
        if (HasScanResult || IsScanning)
        {
            if (!await ConfirmDiscardNewAsync()) return;
        }

        CloseScanReviewModal();
    }

    [RelayCommand]
    private async Task RetryScan()
    {
        if (_currentImageData != null && _currentFileName != null)
        {
            // Clear error/result state first to avoid UI overlap
            HasScanError = false;
            HasScanResult = false;

            // Then show scanning state
            IsScanning = true;
            ScanningMessage = "Retrying...".Translate();

            // Start the scan
            await ScanReceiptAsync();
        }
    }

    [RelayCommand]
    private void AddLineItem()
    {
        var lineItem = new ScannedLineItemViewModel
        {
            Description = string.Empty,
            Quantity = "1",
            UnitPrice = "0.00",
            TotalPrice = "0.00",
            Confidence = 1.0,
            IsManuallyAdded = true,
            OnProductErrorCleared = ClearValidationMessageIfNoErrors,
            OnTotalPriceEdited = ValidateTotals
        };
        LineItems.Add(lineItem);
        ValidateCurrentBulkItem();
    }

    [RelayCommand]
    private void RemoveLineItem(ScannedLineItemViewModel? item)
    {
        if (item != null)
        {
            LineItems.Remove(item);
            ValidateTotals();
            ValidateCurrentBulkItem();
        }
    }

    [RelayCommand]
    private async Task CreateTransactionAsync()
    {
        // Validate
        HasValidationMessage = false;
        HasTotalError = false;
        HasSupplierError = false;
        HasLineItemsError = false;

        // Clear product errors on all line items
        foreach (var lineItem in LineItems)
        {
            lineItem.HasProductError = false;
            lineItem.ProductErrorMessage = string.Empty;
        }

        var hasErrors = false;

        if (!decimal.TryParse(ExtractedTotal, out var total) || total <= 0)
        {
            HasTotalError = true;
            TotalErrorMessage = "Please enter a valid total amount.".Translate();
            hasErrors = true;
        }

        // A refund's negative lines were read as a discount, so it also has no line items. Saying
        // it's a refund explains both, so the other field errors would only add noise.
        if (total < 0)
        {
            TotalErrorMessage = RefundReceiptMessage;
            HasValidationMessage = true;
            return;
        }

        // Supplier is required for expenses, optional for revenue
        if (!IsRevenue && SelectedSupplier == null)
        {
            HasSupplierError = true;
            SupplierErrorMessage = "Please select a supplier.".Translate();
            hasErrors = true;
        }

        // At least one line item is required
        if (LineItems.Count == 0)
        {
            HasLineItemsError = true;
            LineItemsErrorMessage = "Please add at least one line item.".Translate();
            hasErrors = true;
        }

        // Validate line items have products selected
        foreach (var lineItem in LineItems)
        {
            if (lineItem.SelectedProduct == null)
            {
                lineItem.HasProductError = true;
                lineItem.ProductErrorMessage = "Please select a product.".Translate();
                hasErrors = true;
            }
        }

        HasValidationMessage = hasErrors;
        if (hasErrors)
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
        {
            await App.ShowErrorDialogAsync(
                "Error".Translate(),
                "No company is open.".Translate());
            return;
        }

        // Parse values
        decimal.TryParse(ExtractedSubtotal, out var subtotal);
        decimal.TryParse(ExtractedTax, out var taxAmount);
        decimal.TryParse(ExtractedDiscount, out var discount);
        decimal.TryParse(ExtractedShipping, out var shipping);

        var lineItems = LineItems.Select(li =>
        {
            decimal.TryParse(li.Quantity, out var qty);
            decimal.TryParse(li.UnitPrice, out var unitPrice);
            return new LineItem
            {
                ProductId = li.SelectedProduct?.Id,
                Description = li.SelectedProduct?.Name ?? li.Description,
                Quantity = qty > 0 ? qty : 1,
                UnitPrice = unitPrice
            };
        }).Where(li => !string.IsNullOrWhiteSpace(li.Description) || li.ProductId != null).ToList();

        // Create receipt first (common for both transaction types)
        var receiptId = new IdGenerator(companyData).NextReceiptId();

        string? fileData = null;
        if (_currentImageData != null)
        {
            var imageBytes = _currentImageData;
            fileData = await Task.Run(() =>
            {
                var orientedData = ReceiptImageHelper.FixOrientation(imageBytes);
                return Convert.ToBase64String(orientedData);
            });
        }

        // Fetch the receipt-date rate up front (like manual entry) so the saved row shows its amount
        // immediately instead of a momentary "Pending".
        await CurrencyService.WarmRateForDateAsync(ExtractedDate?.DateTime ?? DateTime.Now, ScanCurrencyCode);

        if (IsRevenue)
        {
            CreateRevenueTransaction(companyData, receiptId, fileData, total, subtotal, taxAmount, discount, shipping, lineItems);
        }
        else
        {
            CreateExpenseTransaction(companyData, receiptId, fileData, total, subtotal, taxAmount, discount, shipping, lineItems);
        }

        // The receipt was added: ownership of the auto-created supplier/category/products
        // transfers to the undo action, so don't roll them back when the modal closes.
        _createdEntitiesCommitted = true;

        // Credit the scan step.
        // This fires at the commit point rather than where the entities are created, because a
        // review that gets cancelled rolls those entities back and must not leave a step ticked.
        TutorialService.Instance.CompleteChecklistItem(TutorialService.ChecklistItems.ScanReceipt);

        App.CompanyManager?.MarkAsChanged();
        ReceiptScanned?.Invoke(this, EventArgs.Empty);
        CloseScanReviewModal();
    }

    /// <summary>
    /// The currency a scanned receipt's review starts on: the one the scan detected, unless the app doesn't
    /// know it or it shares its symbol with the company's own ("$" is USD, CAD and AUD alike), when the
    /// scan can't tell the two apart and the company currency stands.
    /// </summary>
    private static string ReceiptCurrency(string? detectedCode)
    {
        var companyCurrency = CurrencyService.CurrentCurrencyCode;
        if (string.IsNullOrWhiteSpace(detectedCode)
            || !CurrencyInfo.All.TryGetValue(detectedCode.Trim(), out var detected)
            || detected.Symbol == CurrencyInfo.GetSymbol(companyCurrency))
            return companyCurrency;
        return detected.Code;
    }

    /// <summary>
    /// Tags a receipt-created transaction with the receipt's currency and its USD amounts, like the
    /// normal expense/revenue save: converted at its own date when that rate is held, otherwise
    /// pending and queued until it is (Calculations.md Rule 3a).
    /// </summary>
    private static void ApplyDisplayCurrency(CompanyData companyData, Transaction txn, string currency)
    {
        txn.OriginalCurrency = currency;
        UsdConversion.Apply(companyData, txn, UsdConversion.CachedRate(currency, txn.Date));
    }

    /// <summary>
    /// Takes undone rows' conversions out of the company's queue and the conversion service's copy.
    /// Processing an entry whose row is gone drops it for good.
    /// </summary>
    private static void WithdrawPendingConversions(CompanyData companyData, IEnumerable<Transaction> transactions) =>
        UsdConversion.Restore(companyData, transactions.Select(UsdConversion.KeyOf), []);

    /// <summary>Queues redone rows again, those still waiting for their rate.</summary>
    private static void RequeuePendingConversions(CompanyData companyData, IEnumerable<Transaction> transactions)
    {
        foreach (var txn in transactions)
            UsdConversion.Requeue(companyData, txn);
    }

    private void CreateExpenseTransaction(CompanyData companyData, string receiptId, string? fileData,
        decimal total, decimal subtotal, decimal taxAmount, decimal discount, decimal shipping, List<LineItem> lineItems)
    {
        var currency = ScanCurrencyCode;
        var expenseDate = ExtractedDate?.DateTime ?? DateTime.Now;
        var expenseId = new Core.Data.IdGenerator(companyData).NextExpenseId(expenseDate);

        var expense = new Expense
        {
            Id = expenseId,
            Date = expenseDate,
            SupplierId = SelectedSupplier?.Id,
            Description = lineItems.Count > 0 ? lineItems[0].Description : ExtractedSupplier,
            LineItems = lineItems,
            Quantity = lineItems.Sum(li => li.Quantity),
            UnitPrice = lineItems.Count > 0 ? lineItems.Average(li => li.UnitPrice) : subtotal,
            Amount = subtotal > 0 ? subtotal : total,
            TaxRate = subtotal > 0 && taxAmount > 0 ? (taxAmount / subtotal) * 100 : 0,
            TaxAmount = taxAmount,
            Discount = discount,
            ShippingCost = shipping,
            Total = total,
            PaymentMethod = Enum.TryParse<PaymentMethod>(SelectedPaymentMethod.Replace(" ", ""), out var pm) ? pm : PaymentMethod.Cash,
            Notes = Notes,
            ReceiptId = receiptId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ApplyDisplayCurrency(companyData, expense, currency);

        var receipt = new Receipt
        {
            Id = receiptId,
            TransactionId = expenseId,
            TransactionType = "Expense",
            FileName = _currentFileName ?? "receipt.jpg",
            FileType = GetMimeType(_currentFileName ?? "receipt.jpg"),
            FileSize = _currentImageData?.Length ?? 0,
            FileData = fileData,
            Amount = total,
            Date = ExtractedDate?.DateTime ?? DateTime.Now,
            Supplier = ExtractedSupplier,
            Source = "AI Scanned",
            OcrData = CreateOcrData(),
            CreatedAt = DateTime.UtcNow
        };

        // Capture auto-created entities for undo
        var capturedReceipt = receipt;
        var capturedExpense = expense;
        var createdEntities = CaptureCreatedEntities();

        var action = new DelegateAction(
            $"AI scan expense {expenseId}",
            () =>
            {
                companyData.Expenses.RemoveRecord(capturedExpense);
                companyData.Receipts.RemoveRecord(capturedReceipt);
                WithdrawPendingConversions(companyData, [capturedExpense]);
                createdEntities.Remove(companyData);
            },
            () =>
            {
                createdEntities.Restore(companyData);
                companyData.Expenses.RestoreRecord(capturedExpense);
                companyData.Receipts.RestoreRecord(capturedReceipt);
                RequeuePendingConversions(companyData, [capturedExpense]);
            });

        companyData.Expenses.Add(expense);
        companyData.Receipts.Add(receipt);
        App.UndoRedoManager.RecordAction(action);
    }

    private void CreateRevenueTransaction(CompanyData companyData, string receiptId, string? fileData,
        decimal total, decimal subtotal, decimal taxAmount, decimal discount, decimal shipping, List<LineItem> lineItems)
    {
        var currency = ScanCurrencyCode;
        var revenueDate = ExtractedDate?.DateTime ?? DateTime.Now;
        var revenueId = new Core.Data.IdGenerator(companyData).NextRevenueId(revenueDate);

        var revenue = new Revenue
        {
            Id = revenueId,
            Date = revenueDate,
            CustomerId = null, // Could be linked to customer if we add customer selection later
            Description = lineItems.Count > 0 ? lineItems[0].Description : ExtractedSupplier,
            LineItems = lineItems,
            Quantity = lineItems.Sum(li => li.Quantity),
            UnitPrice = lineItems.Count > 0 ? lineItems.Average(li => li.UnitPrice) : subtotal,
            Amount = subtotal > 0 ? subtotal : total,
            Subtotal = subtotal > 0 ? subtotal : total,
            TaxRate = subtotal > 0 && taxAmount > 0 ? (taxAmount / subtotal) * 100 : 0,
            TaxAmount = taxAmount,
            Discount = discount,
            ShippingCost = shipping,
            Total = total,
            PaymentMethod = Enum.TryParse<PaymentMethod>(SelectedPaymentMethod.Replace(" ", ""), out var pm) ? pm : PaymentMethod.Cash,
            PaymentStatus = RevenuePaymentStatus.Paid,
            Notes = Notes,
            ReceiptId = receiptId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ApplyDisplayCurrency(companyData, revenue, currency);

        var receipt = new Receipt
        {
            Id = receiptId,
            TransactionId = revenueId,
            TransactionType = "Revenue",
            FileName = _currentFileName ?? "receipt.jpg",
            FileType = GetMimeType(_currentFileName ?? "receipt.jpg"),
            FileSize = _currentImageData?.Length ?? 0,
            FileData = fileData,
            Amount = total,
            Date = ExtractedDate?.DateTime ?? DateTime.Now,
            Supplier = ExtractedSupplier, // Still store the merchant name for reference
            Source = "AI Scanned",
            OcrData = CreateOcrData(),
            CreatedAt = DateTime.UtcNow
        };

        // Capture auto-created entities for undo
        var capturedReceipt = receipt;
        var capturedRevenue = revenue;
        var createdEntities = CaptureCreatedEntities();

        var action = new DelegateAction(
            $"AI scan revenue {revenueId}",
            () =>
            {
                companyData.Revenues.RemoveRecord(capturedRevenue);
                companyData.Receipts.RemoveRecord(capturedReceipt);
                WithdrawPendingConversions(companyData, [capturedRevenue]);
                createdEntities.Remove(companyData);
            },
            () =>
            {
                createdEntities.Restore(companyData);
                companyData.Revenues.RestoreRecord(capturedRevenue);
                companyData.Receipts.RestoreRecord(capturedReceipt);
                RequeuePendingConversions(companyData, [capturedRevenue]);
            });

        companyData.Revenues.Add(revenue);
        companyData.Receipts.Add(receipt);
        App.UndoRedoManager.RecordAction(action);
    }

    // One-shot handlers for the "create entity from this modal" flows. Stored so a cancelled create
    // (which never raises the *Saved event) can be detached before the next attempt, instead of
    // leaking onto the singleton create-modal VMs. See CreateModalSubscription.
    private EventHandler? _supplierSavedHandler;
    private EventHandler? _productSavedHandler;

    [RelayCommand]
    private void OpenCreateSupplier()
    {
        var supplierModals = App.SupplierModalsViewModel;
        if (supplierModals == null) return;

        CreateModalSubscription.RearmOnce(ref _supplierSavedHandler,
            h => supplierModals.SupplierSaved += h,
            h => supplierModals.SupplierSaved -= h,
            () =>
            {
                LoadSupplierOptions();

                // Auto-select the supplier the user just created.
                var newSupplier = SupplierOptions.FirstOrDefault(sup => sup.Id == supplierModals.LastSavedSupplierId);
                if (newSupplier != null)
                    SelectedSupplier = newSupplier;
            });
        supplierModals.OpenAddModal();
    }

    [RelayCommand]
    private void OpenCreateProduct(ScannedLineItemViewModel? lineItem)
    {
        var productModals = App.ProductModalsViewModel;
        if (productModals == null) return;

        CreateModalSubscription.RearmOnce(ref _productSavedHandler,
            h => productModals.ProductSaved += h,
            h => productModals.ProductSaved -= h,
            () =>
            {
                LoadProductOptions();

                // Auto-select the new product into the line item whose dropdown launched the create.
                if (lineItem != null)
                {
                    var newProduct = ProductOptions.FirstOrDefault(p => p.Id == productModals.LastSavedProductId);
                    if (newProduct != null)
                        lineItem.SelectedProduct = newProduct;
                }
            });
        productModals.OpenAddModal();
    }

    /// <summary>
    /// Creates a new product from a line item suggestion.
    /// </summary>
    [RelayCommand]
    private void CreateSuggestedProduct(ScannedLineItemViewModel? lineItem)
    {
        CreateSuggestedProductCore(lineItem);
        UpdateHasUnmatchedProducts();
    }

    private void CreateSuggestedProductCore(ScannedLineItemViewModel? lineItem)
    {
        if (lineItem == null || string.IsNullOrEmpty(lineItem.SuggestedProductName))
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Products == null) return;

        // Find or create an expense category for the product.
        // Prefer the AI-suggested category, then fall back to any existing expense category.
        var category = _createdCategoryForUndo
            ?? companyData.Categories.FirstOrDefault(c => c.Type == CategoryType.Expense);

        if (category == null)
        {
            // No expense category exists, create one from AI suggestion if available
            var aiCategory = _aiSuggestion?.NewCategory;
            var categoryName = aiCategory?.Name ?? "General Expenses";

            category = new Category
            {
                Id = new Core.Data.IdGenerator(companyData).NextCategoryId(CategoryType.Expense),
                Name = categoryName,
                Type = CategoryType.Expense,
                Description = aiCategory?.Description
            };
            companyData.Categories.Add(category);
            _createdCategoryForUndo = category;
        }

        var newId = new Core.Data.IdGenerator(companyData).NextProductId();

        var newProduct = new Product
        {
            Id = newId,
            Name = lineItem.SuggestedProductName,
            Description = string.Empty,
            CostPrice = decimal.TryParse(lineItem.UnitPrice, out var price) ? price : 0,
            UnitPrice = 0,
            CategoryId = category.Id,
            Type = CategoryType.Expense
        };

        companyData.Products.Add(newProduct);
        _createdProductsForUndo.Add(newProduct);

        // Add to options and select
        var option = new ProductOption
        {
            Id = newId,
            Name = newProduct.Name,
            Description = string.Empty,
            UnitPrice = newProduct.CostPrice
        };
        ProductOptions.Add(option);
        lineItem.SelectedProduct = option;
        lineItem.ShowCreateProductSuggestion = false;

        companyData.MarkAsModified();
    }

    /// <summary>
    /// Creates products for all unmatched line items at once.
    /// </summary>
    [RelayCommand]
    private void CreateAllSuggestedProducts()
    {
        var unmatched = LineItems.Where(li => li.ShowCreateProductSuggestion).ToList();
        foreach (var lineItem in unmatched)
        {
            CreateSuggestedProductCore(lineItem);
        }
        UpdateHasUnmatchedProducts();
    }

    /// <summary>
    /// Dismisses the create product suggestion for a line item.
    /// </summary>
    [RelayCommand]
    private void DismissProductSuggestion(ScannedLineItemViewModel? lineItem)
    {
        if (lineItem != null)
        {
            lineItem.ShowCreateProductSuggestion = false;
            UpdateHasUnmatchedProducts();
        }
    }

    private void UpdateHasUnmatchedProducts()
    {
        HasUnmatchedProducts = LineItems.Any(li => li.ShowCreateProductSuggestion);
        ValidateCurrentBulkItem();
    }

    /// <summary>
    /// Gets AI suggestions for supplier and category based on receipt data.
    /// </summary>
    private async Task GetAiSuggestionsAsync(ReceiptScanResult result)
    {
        // Capture the bulk item (if any) so we can discard results if the user navigated away
        var bulkItemAtStart = CurrentBulkItem;
        // Capture the scan generation so we can discard results if the modal was reset / a new scan
        // started while this AI call was in flight (single-scan flow).
        var scanGenerationAtStart = _scanGeneration;

        var geminiService = new GeminiService(App.ErrorLogger, App.TelemetryManager);

        if (!geminiService.IsConfigured)
        {
            // Fall back to basic matching
            TryBasicSupplierMatch(result.SupplierName);
            return;
        }

        IsLoadingAiSuggestions = true;
        ShowCreateSupplierSuggestion = false;

        try
        {
            var companyData = App.CompanyManager?.CompanyData;
            if (companyData == null)
            {
                TryBasicSupplierMatch(result.SupplierName);
                return;
            }

            var request = new ReceiptAnalysisRequest
            {
                SupplierName = result.SupplierName ?? string.Empty,
                RawText = result.RawText,
                LineItemDescriptions = result.LineItems.Select(li => li.Description).ToList(),
                TotalAmount = result.TotalAmount ?? 0,
                ExistingSuppliers = companyData.Suppliers.Select(s => new ExistingSupplierInfo
                {
                    Id = s.Id,
                    Name = s.Name
                }).ToList(),
                ExistingCategories = companyData.Categories
                    .Where(c => c.Type == CategoryType.Expense)
                    .Select(c => new ExistingCategoryInfo
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Description = c.Description
                    }).ToList()
            };

            var suggestion = await geminiService.GetSupplierCategorySuggestionAsync(request);

            // Discard stale results: either the modal was reset / a new scan started (single-scan
            // flow), or in bulk review the user navigated to a different item.
            if (_scanGeneration != scanGenerationAtStart)
                return;
            if (IsBulkReviewOpen && CurrentBulkItem != bulkItemAtStart)
                return;

            if (suggestion != null)
            {
                ApplyAiSuggestion(suggestion);
            }
            else
            {
                // AI failed, fall back to basic matching
                TryBasicSupplierMatch(result.SupplierName);
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "AI suggestion failed");
            TryBasicSupplierMatch(result.SupplierName);
        }
        finally
        {
            IsLoadingAiSuggestions = false;
        }
    }

    /// <summary>
    /// Applies AI suggestions to the form.
    /// </summary>
    private void ApplyAiSuggestion(SupplierCategorySuggestion suggestion)
    {
        _aiSuggestion = suggestion;

        if (!string.IsNullOrEmpty(suggestion.MatchedSupplierId))
        {
            var supplier = SupplierOptions.FirstOrDefault(s => s.Id == suggestion.MatchedSupplierId);
            if (supplier != null)
            {
                SelectedSupplier = supplier;
            }
        }
        else if (suggestion.ShouldCreateNewSupplier && suggestion.NewSupplier != null)
        {
            ShowCreateSupplierSuggestion = true;
            SuggestedSupplierName = ToTitleCase(suggestion.NewSupplier.Name);
        }
    }

    private void ValidateTotals()
    {
        HasTotalMismatchWarning = false;
        TotalMismatchWarningMessage = string.Empty;

        decimal.TryParse(ExtractedTotal, out var total);
        if (total == 0) return;

        decimal.TryParse(ExtractedSubtotal, out var subtotal);
        decimal.TryParse(ExtractedTax, out var tax);
        decimal.TryParse(ExtractedDiscount, out var discount);
        decimal.TryParse(ExtractedShipping, out var shipping);

        var lineItemSum = LineItems
            .Sum(li => decimal.TryParse(li.TotalPrice, out var p) ? p : 0);

        // Check line items + tax + shipping - discount against total
        var expectedTotal = lineItemSum + tax + shipping - discount;
        var diff = Math.Abs(expectedTotal - total);

        if (diff > 0.02m)
        {
            HasTotalMismatchWarning = true;
            string Money(decimal amount) => CurrencyInfo.FormatAmount(amount, ScanCurrencyCode);
            TotalMismatchWarningMessage = string.Format(
                "Line items ({0}) + tax ({1}) + shipping ({2}) - discount ({3}) = {4}, but total is {5}. Some items may be incorrect.".Translate(),
                Money(lineItemSum), Money(tax), Money(shipping), Money(discount), Money(expectedTotal), Money(total));
        }
    }

    /// <summary>
    /// Validates the current bulk item's required fields from live form state and updates
    /// the item's validation errors, carousel badge, and warning message in real time.
    /// </summary>
    private void ValidateCurrentBulkItem()
    {
        if (!IsBulkReviewOpen || CurrentBulkItem == null || !CurrentBulkItem.IsApproved)
        {
            HasBulkIncompleteWarning = false;
            BulkIncompleteWarningMessage = string.Empty;
            return;
        }

        var missing = new List<string>();

        if (!decimal.TryParse(ExtractedTotal, out var total) || total <= 0)
            missing.Add("total amount".Translate());

        if (!IsRevenue && SelectedSupplier == null)
            missing.Add("supplier".Translate());

        if (LineItems.Count == 0)
            missing.Add("at least one line item".Translate());
        else if (LineItems.Any(li => li.SelectedProduct == null))
            missing.Add("product selection for all line items".Translate());

        var hasErrors = missing.Count > 0;

        // Update the BulkScanItem directly from live form state
        CurrentBulkItem.LineItemProductIds = LineItems.Select(li => li.SelectedProduct?.Id).ToList();
        CurrentBulkItem.IsRevenueOverride = IsRevenue;
        CurrentBulkItem.SelectedSupplierId = SelectedSupplier?.Id;
        if (CurrentBulkItem.ScanResult != null && decimal.TryParse(ExtractedTotal, out var t))
            CurrentBulkItem.ScanResult.TotalAmount = t;

        // Setting HasValidationErrors triggers OnHasValidationErrorsChanged which notifies
        // IsApprovedWithErrors/IsApprovedWithoutErrors for carousel badge updates
        CurrentBulkItem.HasValidationErrors = hasErrors;

        OnPropertyChanged(nameof(BulkAllReviewed));
        OnPropertyChanged(nameof(BulkIncompleteCount));
        OnPropertyChanged(nameof(HasBulkIncompleteItems));

        if (total < 0)
        {
            HasBulkIncompleteWarning = true;
            BulkIncompleteWarningMessage = RefundReceiptMessage;
        }
        else if (hasErrors)
        {
            HasBulkIncompleteWarning = true;
            BulkIncompleteWarningMessage = string.Format(
                "This receipt is missing required fields: {0}. Fill these in to enable bulk creation.".Translate(),
                string.Join(", ", missing));
        }
        else
        {
            HasBulkIncompleteWarning = false;
            BulkIncompleteWarningMessage = string.Empty;
        }
    }

    // A refund or return slip scans with a negative total. The review only creates new expenses and
    // revenue, so it can't book one; saying so beats "enter a valid total".
    private static string RefundReceiptMessage =>
        "This receipt is a refund, so it can't be added as a new expense or revenue.".Translate();

    /// <summary>
    /// Determines if a line item is a discount rather than a product.
    /// Only filters based on description keywords to avoid incorrectly
    /// removing legitimate line items with negative amounts.
    /// </summary>
    private static bool IsDiscountLine(ScannedLineItem item)
    {
        var desc = item.Description.ToLowerInvariant();
        return desc.Contains("discount") || desc.Contains("% off") || desc.Contains("coupon")
            || desc.Contains("promo");
    }

    /// <summary>
    /// Cleans OCR artifacts from a description string.
    /// Removes *, normalizes newlines to spaces, and collapses multiple spaces.
    /// </summary>
    private static string CleanOcrText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var cleaned = text
            .Replace("*", "")
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ");

        // Collapse multiple spaces into one
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");

        return cleaned.Trim();
    }

    /// <summary>
    /// Converts a string to title case and cleans up OCR artifacts
    /// (e.g., "LARGE DRINK*" -> "Large Drink", "MD HOOK\nwheat" -> "Md Hook Wheat").
    /// </summary>
    private static string ToTitleCase(string text)
    {
        var cleaned = CleanOcrText(text);
        if (string.IsNullOrEmpty(cleaned))
            return text.Trim();

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.ToLower());
    }

    /// <summary>
    /// Detects whether the transaction should be revenue or expense based on
    /// comparing the extracted merchant name against the user's company name.
    /// If the merchant matches (or closely matches) the company name → Revenue.
    /// If it doesn't match → Expense.
    /// </summary>
    private void DetectTransactionType(string? merchantName)
    {
        var companyData = App.CompanyManager?.CompanyData;
        var companyName = companyData?.Settings.Company.Name;

        if (string.IsNullOrWhiteSpace(merchantName) || string.IsNullOrWhiteSpace(companyName))
        {
            // Default to expense if we can't compare
            IsRevenue = false;
            return;
        }

        // Normalize both strings for comparison
        var normalizedMerchant = NormalizeForComparison(merchantName);
        var normalizedCompany = NormalizeForComparison(companyName);

        // Check for exact match
        if (normalizedMerchant.Equals(normalizedCompany, StringComparison.OrdinalIgnoreCase))
        {
            IsRevenue = true;
            return;
        }

        // Check if one contains the other (handles cases like "ACME Inc" vs "ACME")
        if (normalizedMerchant.Contains(normalizedCompany, StringComparison.OrdinalIgnoreCase) ||
            normalizedCompany.Contains(normalizedMerchant, StringComparison.OrdinalIgnoreCase))
        {
            IsRevenue = true;
            return;
        }

        // Check word-level similarity (handles word order differences)
        var merchantWords = normalizedMerchant.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var companyWords = normalizedCompany.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // If most significant words match, consider it a match
        var matchingWords = merchantWords.Count(mw =>
            companyWords.Any(cw => mw.Equals(cw, StringComparison.OrdinalIgnoreCase) ||
                                   (mw.Length > 3 && cw.Length > 3 &&
                                    (mw.Contains(cw, StringComparison.OrdinalIgnoreCase) ||
                                     cw.Contains(mw, StringComparison.OrdinalIgnoreCase)))));

        var totalWords = Math.Max(merchantWords.Length, companyWords.Length);
        if (totalWords > 0 && (double)matchingWords / totalWords >= 0.5)
        {
            IsRevenue = true;
            return;
        }

        // Default to expense if no match found
        IsRevenue = false;
    }

    /// <summary>
    /// Normalizes a string for comparison by removing common business suffixes,
    /// punctuation, and extra whitespace.
    /// </summary>
    private static string NormalizeForComparison(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Convert to lowercase
        var normalized = text.ToLowerInvariant();

        // Remove common business suffixes
        string[] suffixes = ["inc", "inc.", "llc", "llc.", "ltd", "ltd.", "corp", "corp.",
                            "corporation", "company", "co", "co.", "limited", "gmbh",
                            "plc", "pty", "pvt", "s.a.", "sa", "ag"];

        foreach (var suffix in suffixes)
        {
            if (normalized.EndsWith(" " + suffix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^(suffix.Length + 1)];
            }
        }

        // Remove punctuation and extra whitespace
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^\w\s]", " ");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");

        return normalized.Trim();
    }

    /// <summary>
    /// Falls back to basic string matching for supplier.
    /// </summary>
    private void TryBasicSupplierMatch(string? supplierName)
    {
        if (string.IsNullOrEmpty(supplierName))
            return;

        var matchedSupplier = SupplierOptions.FirstOrDefault(s =>
            s.Name.Contains(supplierName, StringComparison.OrdinalIgnoreCase) ||
            supplierName.Contains(s.Name, StringComparison.OrdinalIgnoreCase));

        if (matchedSupplier != null)
        {
            SelectedSupplier = matchedSupplier;
        }
        else
        {
            // No match found, suggest creating a new supplier
            ShowCreateSupplierSuggestion = true;
            SuggestedSupplierName = ToTitleCase(supplierName);
        }
    }

    /// <summary>
    /// Tries to match a scanned line item description to an existing product.
    /// </summary>
    private void TryMatchProduct(ScannedLineItemViewModel lineItem, string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            lineItem.SuggestedProductName = string.Empty;
            lineItem.ShowCreateProductSuggestion = false;
            return;
        }

        // Clean OCR artifacts before matching
        var cleanedDescription = CleanOcrText(description);

        // Try to find exact or close match
        var matchedProduct = ProductOptions.FirstOrDefault(p =>
            p.Name.Equals(cleanedDescription, StringComparison.OrdinalIgnoreCase));

        if (matchedProduct == null)
        {
            // Try partial match
            matchedProduct = ProductOptions.FirstOrDefault(p =>
                p.Name.Contains(cleanedDescription, StringComparison.OrdinalIgnoreCase) ||
                cleanedDescription.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (matchedProduct != null)
        {
            lineItem.SelectedProduct = matchedProduct;
            lineItem.ShowCreateProductSuggestion = false;
        }
        else
        {
            // No match found - suggest creating new product
            lineItem.SuggestedProductName = ToTitleCase(description);
            lineItem.ShowCreateProductSuggestion = true;
        }
    }

    /// <summary>
    /// Creates a new supplier from the AI suggestion or basic match suggestion.
    /// </summary>
    [RelayCommand]
    private void CreateSuggestedSupplier()
    {
        if (string.IsNullOrEmpty(SuggestedSupplierName))
            return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null) return;

        var newId = new Core.Data.IdGenerator(companyData).NextSupplierId();

        // Create supplier
        var newSupplier = new Supplier
        {
            Id = newId,
            Name = SuggestedSupplierName,
            Notes = _aiSuggestion?.NewSupplier?.Notes ?? "Created from receipt scan".Translate()
        };

        companyData.Suppliers.Add(newSupplier);
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.SupplierCreated);
        _createdSuppliersForUndo.Add(newSupplier);

        // Add to options and select
        var option = new SupplierOption { Id = newId, Name = newSupplier.Name };
        SupplierOptions.Add(option);
        SelectedSupplier = option;

        ShowCreateSupplierSuggestion = false;

        App.CompanyManager?.MarkAsChanged();
    }

    /// <summary>
    /// Dismisses the supplier suggestion without creating.
    /// </summary>
    [RelayCommand]
    private void DismissSupplierSuggestion()
    {
        ShowCreateSupplierSuggestion = false;
    }

    #endregion

    #region Helper Methods

    private void ResetScanModal()
    {
        IsScanning = false;
        HasScanError = false;
        HasScanResult = false;
        IsFullscreen = false;
        ScanErrorMessage = string.Empty;
        SetScanPreview();
        ExtractedSupplier = string.Empty;
        ExtractedDate = DateTimeOffset.Now;
        ExtractedSubtotal = string.Empty;
        ExtractedTax = string.Empty;
        ExtractedDiscount = string.Empty;
        ExtractedShipping = string.Empty;
        ExtractedTotal = string.Empty;
        SelectedScanCurrency = string.Empty;
        ConfidenceScore = 0;
        ConfidenceText = string.Empty;
        IsHighConfidence = false;
        IsMediumConfidence = false;
        IsLowConfidence = false;
        LineItems.Clear();
        SelectedSupplier = null;
        SelectedPaymentMethod = "Cash";
        Notes = string.Empty;
        IsRevenue = false;
        HasValidationMessage = false;
        HasTotalError = false;
        HasSupplierError = false;
        HasLineItemsError = false;
        LineItemsErrorMessage = string.Empty;
        HasTotalMismatchWarning = false;
        TotalMismatchWarningMessage = string.Empty;
        _currentImageData = null;
        _currentFileName = null;

        // Reset AI suggestion state. Bump the generation so any in-flight suggestion call for the
        // previous receipt discards its result instead of applying it to the next receipt.
        _scanGeneration++;
        IsLoadingAiSuggestions = false;
        _aiSuggestion = null;
        ShowCreateSupplierSuggestion = false;
        SuggestedSupplierName = string.Empty;

        // Reset undo tracking for auto-created entities
        _createdSuppliersForUndo.Clear();
        _createdCategoryForUndo = null;
        _createdProductsForUndo.Clear();
        _createdEntitiesCommitted = false;

        // Reset usage state (keep cached values for display)
        IsNearLimit = false;
    }

    private void LoadSupplierOptions()
    {
        OptionLoader.Fill(SupplierOptions, OptionLoader.Suppliers(App.CompanyManager?.CompanyData).AsOptions<SupplierOption>());
    }

    private void LoadProductOptions()
    {
        ProductOptions.Clear();
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData?.Products == null) return;

        foreach (var product in companyData.Products.OrderBy(p => p.Name))
        {
            // Filter to purchase-type products (for expense receipts)
            var category = companyData.Categories?.FirstOrDefault(c => c.Id == product.CategoryId);
            if (category?.Type == CategoryType.Revenue)
                continue;

            ProductOptions.Add(new ProductOption
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                UnitPrice = product.CostPrice // Use cost price for expenses
            });
        }
    }

    private IReceiptScannerService CreateScannerService()
    {
        return new GeminiReceiptScannerService(
            ApiConfig.BaseUrl,
            new LicenseApiAuth(),
            App.ErrorLogger,
            App.TelemetryManager,
            onTimingRecorded: (serverMs, wallClockMs, uploadBytes, loadFactor) =>
                OperationTimingService.Instance?.RecordResult(OperationKind.ReceiptScan, serverMs, wallClockMs, uploadBytes, loadFactor));
    }

    private IUsageLimitService CreateUsageService() =>
        new UsageLimitService(UsageLimit.ReceiptScans, App.LicenseService, App.ErrorLogger);

    private OcrData CreateOcrData()
    {
        decimal.TryParse(ExtractedTotal, out var total);
        decimal.TryParse(ExtractedSubtotal, out var subtotal);
        decimal.TryParse(ExtractedTax, out var tax);
        decimal.TryParse(ExtractedShipping, out var shipping);

        return new OcrData
        {
            ExtractedSupplier = ExtractedSupplier,
            ExtractedDate = ExtractedDate?.DateTime,
            ExtractedAmount = total,
            ExtractedSubtotal = subtotal,
            ExtractedTaxAmount = tax,
            ExtractedShipping = shipping,
            Confidence = ConfidenceScore,
            LineItems = LineItems.Select(li =>
            {
                decimal.TryParse(li.Quantity, out var qty);
                decimal.TryParse(li.UnitPrice, out var unitPrice);
                decimal.TryParse(li.TotalPrice, out var totalPrice);
                return new OcrLineItem
                {
                    Description = li.Description,
                    Quantity = qty,
                    UnitPrice = unitPrice,
                    TotalPrice = totalPrice,
                    Confidence = li.Confidence
                };
            }).ToList(),
            ExtractedItems = LineItems.Select(li => li.Description).ToList()
        };
    }

    private static OcrData CreateOcrDataFromScanResult(ReceiptScanResult result)
    {
        return new OcrData
        {
            ExtractedSupplier = result.SupplierName ?? string.Empty,
            ExtractedDate = result.TransactionDate,
            ExtractedAmount = result.TotalAmount ?? 0,
            ExtractedSubtotal = result.Subtotal ?? 0,
            ExtractedTaxAmount = result.TaxAmount ?? 0,
            ExtractedShipping = result.Shipping ?? 0,
            Confidence = result.Confidence,
            LineItems = result.LineItems.Select(li => new OcrLineItem
            {
                Description = li.Description,
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                TotalPrice = li.TotalPrice,
                Confidence = li.Confidence
            }).ToList(),
            ExtractedItems = result.LineItems.Select(li => li.Description).ToList()
        };
    }

    /// <summary>
    /// The content type stored on a receipt. Accepted formats come from the shared list, so a
    /// new one is typed correctly here without a second edit. The two below are not offered by
    /// the picker but can still arrive on a receipt carried over from an import.
    /// </summary>
    private static string GetMimeType(string fileName)
    {
        var known = FilePickerTypes.GetReceiptContentType(fileName);
        if (known != null) return known;

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    #endregion
}

/// <summary>
/// View model for a scanned line item.
/// </summary>
public partial class ScannedLineItemViewModel : ObservableObject
{
    /// <summary>
    /// Callback invoked when a product error is cleared.
    /// </summary>
    public Action? OnProductErrorCleared { get; set; }

    /// <summary>
    /// Callback invoked when the total price changes so the parent can revalidate totals.
    /// </summary>
    public Action? OnTotalPriceEdited { get; set; }

    partial void OnTotalPriceChanged(string value) => OnTotalPriceEdited?.Invoke();

    [ObservableProperty]
    private ProductOption? _selectedProduct;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _quantity = "1";

    [ObservableProperty]
    private string _unitPrice = "0.00";

    [ObservableProperty]
    private string _totalPrice = "0.00";

    [ObservableProperty]
    private double _confidence = 1.0;

    /// <summary>
    /// Whether this line item has low confidence and should show a warning icon.
    /// </summary>
    public bool IsLowConfidence => Confidence < 0.85 && !IsManuallyAdded;

    partial void OnConfidenceChanged(double value) => OnPropertyChanged(nameof(IsLowConfidence));

    [ObservableProperty]
    private bool _isManuallyAdded;

    partial void OnIsManuallyAddedChanged(bool value) => OnPropertyChanged(nameof(IsLowConfidence));

    [ObservableProperty]
    private bool _hasProductError;

    [ObservableProperty]
    private string _productErrorMessage = string.Empty;

    /// <summary>
    /// Suggested product name when no match found (for creating new product).
    /// </summary>
    [ObservableProperty]
    private string _suggestedProductName = string.Empty;

    /// <summary>
    /// Whether to show the create product suggestion.
    /// </summary>
    [ObservableProperty]
    private bool _showCreateProductSuggestion;

    partial void OnSelectedProductChanged(ProductOption? value)
    {
        if (value != null)
        {
            Description = value.Name;
            // Only update unit price if it's currently 0 or not set
            if (UnitPrice == "0.00" || string.IsNullOrEmpty(UnitPrice))
            {
                UnitPrice = value.UnitPrice.ToString("F2");
            }
            HasProductError = false;
            ProductErrorMessage = string.Empty;
            ShowCreateProductSuggestion = false;
            OnProductErrorCleared?.Invoke();
        }
        RecalculateTotal();
    }

    partial void OnQuantityChanged(string value)
    {
        RecalculateTotal();
    }

    partial void OnUnitPriceChanged(string value)
    {
        RecalculateTotal();
    }

    private void RecalculateTotal()
    {
        if (decimal.TryParse(Quantity, out var qty) && decimal.TryParse(UnitPrice, out var price))
        {
            TotalPrice = (qty * price).ToString("F2");
        }
    }
}
