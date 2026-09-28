using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Models.Tracking;

/// <summary>
/// Represents a receipt attachment.
/// </summary>
public class Receipt : IRecord
{
    /// <summary>
    /// Unique identifier (e.g., RCP-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Associated transaction ID.
    /// </summary>
    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Type of transaction (Expense, Revenue).
    /// </summary>
    [JsonPropertyName("transactionType")]
    public string TransactionType { get; set; } = string.Empty;

    /// <summary>
    /// File name in storage.
    /// </summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME type (e.g., image/jpeg, application/pdf).
    /// </summary>
    [JsonPropertyName("fileType")]
    public string FileType { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    /// <summary>
    /// Receipt amount.
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Receipt date.
    /// </summary>
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Supplier name.
    /// </summary>
    [JsonPropertyName("supplier")]
    public string Supplier { get; set; } = string.Empty;

    /// <summary>
    /// Source of the receipt (Manual, AI Scanned).
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "Manual";

    /// <summary>
    /// Base64 encoded file data stored in the company file.
    /// This ensures the receipt is saved even if the original file is moved.
    /// </summary>
    [JsonPropertyName("fileData")]
    public string? FileData { get; set; }

    /// <summary>
    /// Original file path (for reference, may not exist if file was moved).
    /// </summary>
    [JsonPropertyName("originalFilePath")]
    public string? OriginalFilePath { get; set; }

    /// <summary>
    /// OCR extracted data (if AI scanned).
    /// </summary>
    [JsonPropertyName("ocrData")]
    public OcrData? OcrData { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this receipt was AI scanned.
    /// </summary>
    [JsonIgnore]
    public bool IsAiScanned => Source == "AI Scanned" && OcrData != null;
}
