using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Status of a single receipt in the bulk scan pipeline.
/// </summary>
public enum BulkScanStatus
{
    Queued,
    Scanning,
    Succeeded,
    Failed
}

/// <summary>
/// Tracks the state of a single receipt through bulk scanning: file data, scan status,
/// result, approval state, and preview image path.
/// </summary>
public partial class BulkScanItem : ObservableObject
{
    /// <summary>
    /// Original file path selected by the user.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Display file name (e.g., "receipt1.jpg").
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Raw file bytes read from disk.
    /// </summary>
    public byte[]? FileData { get; set; }

    /// <summary>
    /// Preprocessed image bytes (EXIF-fixed, contrast-boosted) ready for API.
    /// For PDFs this is the raw PDF bytes unchanged.
    /// </summary>
    public byte[]? PreprocessedData { get; set; }

    /// <summary>
    /// The file name to send to the scanner (may be changed to .jpg after preprocessing).
    /// </summary>
    public string ScanFileName { get; set; } = string.Empty;

    /// <summary>
    /// Current scan status.
    /// </summary>
    [ObservableProperty]
    private BulkScanStatus _status = BulkScanStatus.Queued;

    /// <summary>
    /// Scan result if status is Succeeded.
    /// </summary>
    public ReceiptScanResult? ScanResult { get; set; }

    /// <summary>
    /// Error message if status is Failed.
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Path to preview image on disk (JPEG for both images and PDFs).
    /// </summary>
    [ObservableProperty]
    private string? _previewImagePath;

    /// <summary>
    /// Path to a small thumbnail for queue/carousel cards (fast to generate and display).
    /// Falls back to <see cref="PreviewImagePath"/> if not set.
    /// </summary>
    [ObservableProperty]
    private string? _thumbnailPath;

    /// <summary>
    /// Whether the user has approved this receipt for transaction creation.
    /// </summary>
    [ObservableProperty]
    private bool _isApproved;

    /// <summary>
    /// Whether this item is currently being viewed in the carousel.
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// Whether the user has reviewed this item (approved or skipped past it).
    /// </summary>
    [ObservableProperty]
    private bool _isReviewed;

    /// <summary>
    /// Whether the user explicitly skipped this item.
    /// </summary>
    [ObservableProperty]
    private bool _isSkipped;

    /// <summary>
    /// Per-item notes entered by the user during review.
    /// </summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Per-item currency left in the review (null until the item is first reviewed).
    /// </summary>
    public string? CurrencyCode { get; set; }

    /// <summary>
    /// Per-item transaction type override (user may change from the AI-detected value).
    /// </summary>
    public bool? IsRevenueOverride { get; set; }

    /// <summary>
    /// Per-item selected supplier ID (persisted across navigation).
    /// </summary>
    public string? SelectedSupplierId { get; set; }

    /// <summary>
    /// Whether the supplier suggestion UI state has been saved for this item.
    /// </summary>
    public bool ShowCreateSupplierSuggestion { get; set; }

    /// <summary>
    /// The suggested supplier name saved for this item.
    /// </summary>
    public string? SuggestedSupplierName { get; set; }

    /// <summary>
    /// Whether AI suggestions have already been processed for this item.
    /// </summary>
    public bool HasAiSuggestionsRun { get; set; }

    /// <summary>
    /// Saved product IDs for each line item (parallel to ScanResult.LineItems).
    /// Null entries mean no product was selected for that line item.
    /// </summary>
    public List<string?> LineItemProductIds { get; set; } = [];

    /// <summary>
    /// Whether this approved item has validation errors that prevent creation.
    /// True when required fields are missing (total, supplier for expenses, products on line items).
    /// </summary>
    [ObservableProperty]
    private bool _hasValidationErrors;

    /// <summary>
    /// Quick summary text shown on progress screen and carousel thumbnail.
    /// e.g., "Costco · $142.57"
    /// </summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>
    /// Confidence level text for display.
    /// </summary>
    [ObservableProperty]
    private string _confidenceText = string.Empty;

    /// <summary>
    /// Whether this is a PDF file.
    /// </summary>
    public bool IsPdf => Path.GetExtension(FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Number of pages in the source file (PDFs report their real count; 1 otherwise).
    /// Populated when the preview/thumbnail is rendered.
    /// </summary>
    [ObservableProperty]
    private int _pageCount = 1;

    /// <summary>True for PDFs with more than one page (these take longer to scan).</summary>
    public bool IsMultiPagePdf => IsPdf && PageCount > 1;

    /// <summary>
    /// Status label shown while scanning; warns that multi-page PDFs take longer.
    /// </summary>
    public string ScanningStatusText => IsMultiPagePdf
        ? "Scanning... Multi-page receipts may take a few seconds longer.".Translate()
        : "Scanning...".Translate();

    partial void OnPageCountChanged(int value)
    {
        OnPropertyChanged(nameof(IsMultiPagePdf));
        OnPropertyChanged(nameof(ScanningStatusText));
    }

    // Computed status booleans for UI visibility bindings
    public bool IsQueued => Status == BulkScanStatus.Queued;
    public bool IsScanning => Status == BulkScanStatus.Scanning;
    public bool IsSucceeded => Status == BulkScanStatus.Succeeded;
    public bool IsFailed => Status == BulkScanStatus.Failed;

    /// <summary>
    /// Whether this item is approved but has incomplete required fields.
    /// Used to show the orange warning state on carousel thumbnails.
    /// </summary>
    public bool IsApprovedWithErrors => IsApproved && HasValidationErrors;

    /// <summary>
    /// Whether this item is approved and all required fields are complete.
    /// Used to show the green approved state on carousel thumbnails.
    /// </summary>
    public bool IsApprovedWithoutErrors => IsApproved && !HasValidationErrors;

    /// <summary>
    /// Recomputes HasValidationErrors based on the saved item state.
    /// Call after saving form data or changing approval.
    /// </summary>
    public void UpdateValidation()
    {
        var hasErrors = false;

        if (ScanResult != null)
        {
            // Total must be > 0
            if ((ScanResult.TotalAmount ?? 0) <= 0)
                hasErrors = true;

            // Supplier required for expenses (not revenue)
            var isRevenue = IsRevenueOverride == true;
            if (!isRevenue && string.IsNullOrEmpty(SelectedSupplierId))
                hasErrors = true;

            // At least one line item required
            if (ScanResult.LineItems.Count == 0)
                hasErrors = true;

            // Every line item must have a product selected
            if (LineItemProductIds.Count > 0 &&
                LineItemProductIds.Any(id => string.IsNullOrEmpty(id)))
                hasErrors = true;
            else if (LineItemProductIds.Count == 0 && ScanResult.LineItems.Count > 0)
                hasErrors = true;
        }

        HasValidationErrors = hasErrors;
    }

    partial void OnIsApprovedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsApprovedWithErrors));
        OnPropertyChanged(nameof(IsApprovedWithoutErrors));
    }

    partial void OnHasValidationErrorsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsApprovedWithErrors));
        OnPropertyChanged(nameof(IsApprovedWithoutErrors));
    }

    partial void OnStatusChanged(BulkScanStatus value)
    {
        OnPropertyChanged(nameof(IsQueued));
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(IsSucceeded));
        OnPropertyChanged(nameof(IsFailed));
    }
}
