namespace ArgoBooks.Core.Services;

/// <summary>
/// Result of scanning a receipt image using AI/OCR.
/// </summary>
public class ReceiptScanResult
{
    /// <summary>
    /// Whether the scan was successful.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if scan failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Extracted supplier name.
    /// </summary>
    public string? SupplierName { get; set; }

    /// <summary>
    /// Extracted transaction date.
    /// </summary>
    public DateTime? TransactionDate { get; set; }

    /// <summary>
    /// Extracted subtotal (before tax).
    /// </summary>
    public decimal? Subtotal { get; set; }

    /// <summary>
    /// Extracted tax amount.
    /// </summary>
    public decimal? TaxAmount { get; set; }

    /// <summary>
    /// Extracted total amount.
    /// </summary>
    public decimal? TotalAmount { get; set; }

    /// <summary>
    /// Extracted discount amount (coupons, promos, loyalty discounts).
    /// </summary>
    public decimal? Discount { get; set; }

    /// <summary>
    /// Extracted shipping / delivery / freight cost.
    /// </summary>
    public decimal? Shipping { get; set; }

    /// <summary>
    /// Extracted line items from the receipt.
    /// </summary>
    public List<ScannedLineItem> LineItems { get; set; } = [];

    /// <summary>
    /// Overall confidence score (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Raw extracted text from the receipt.
    /// </summary>
    public string? RawText { get; set; }

    /// <summary>
    /// Detected currency code (e.g., "USD", "EUR").
    /// </summary>
    public string? CurrencyCode { get; set; }

    /// <summary>
    /// Detected payment method (e.g., "Credit Card", "Cash", "Debit Card").
    /// </summary>
    public string? PaymentMethod { get; set; }

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    /// <summary>
    /// The server's own machine-readable reason, when it gave one.
    ///
    /// Carried alongside the message because a caller sometimes has to ACT on the reason rather
    /// than only show it: a bulk scan that hits the monthly allowance must stop, since every
    /// remaining file will be refused too, while it should keep going after a one-off upstream
    /// failure. Null when the failure never reached the server.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>True when the monthly scan allowance is gone, so retrying cannot help.</summary>
    public bool IsScanLimitReached =>
        string.Equals(ErrorCode, "SCAN_LIMIT_REACHED", StringComparison.OrdinalIgnoreCase);

    public static ReceiptScanResult Failed(string errorMessage, string? errorCode = null) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage,
        ErrorCode = errorCode
    };
}

/// <summary>
/// A line item extracted from a scanned receipt.
/// </summary>
public class ScannedLineItem
{
    /// <summary>
    /// Item description/name.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Quantity of items.
    /// </summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// Unit price per item.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Total price for this line item.
    /// </summary>
    public decimal TotalPrice { get; set; }

    /// <summary>
    /// Confidence score for this line item extraction (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Service interface for AI-powered receipt scanning.
/// </summary>
public interface IReceiptScannerService
{
    /// <summary>
    /// Whether the service is configured and ready to use.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Scans a receipt image and extracts data.
    /// </summary>
    /// <param name="imageData">The raw image bytes (JPEG, PNG, or PDF).</param>
    /// <param name="fileName">The original file name (used for MIME type detection).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The scan result containing extracted data.</returns>
    Task<ReceiptScanResult> ScanReceiptAsync(byte[] imageData, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a receipt image with an option to skip preprocessing if the caller has already
    /// run <see cref="ReceiptImageHelper.PreprocessForOcr"/> on the image data.
    /// </summary>
    Task<ReceiptScanResult> ScanReceiptAsync(byte[] imageData, string fileName, bool skipPreprocessing, CancellationToken cancellationToken = default);
}
