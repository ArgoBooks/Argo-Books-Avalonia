using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Models.Tracking;

/// <summary>
/// Represents a product return.
/// </summary>
public class Return : IRecord
{
    /// <summary>
    /// Unique identifier (e.g., RET-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Original revenue transaction ID.
    /// </summary>
    [JsonPropertyName("originalTransactionId")]
    public string OriginalTransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Return type: "Customer" or "Expense".
    /// </summary>
    [JsonPropertyName("returnType")]
    public string ReturnType { get; set; } = "Customer";

    /// <summary>
    /// Customer ID (for customer returns).
    /// </summary>
    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Supplier ID (for expense returns).
    /// </summary>
    [JsonPropertyName("supplierId")]
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>
    /// Date of return.
    /// </summary>
    [JsonPropertyName("returnDate")]
    public DateTime ReturnDate { get; set; }

    /// <summary>
    /// Items being returned.
    /// </summary>
    [JsonPropertyName("items")]
    public List<ReturnItem> Items { get; set; } = [];

    /// <summary>
    /// Refund amount.
    /// </summary>
    [JsonPropertyName("refundAmount")]
    public decimal RefundAmount { get; set; }

    /// <summary>
    /// Restocking fee charged.
    /// </summary>
    [JsonPropertyName("restockingFee")]
    public decimal RestockingFee { get; set; }

    /// <summary>
    /// Return status.
    /// </summary>
    [JsonPropertyName("status")]
    public ReturnStatus Status { get; set; } = ReturnStatus.Pending;

    /// <summary>
    /// Additional notes.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Accountant who processed the return.
    /// </summary>
    [JsonPropertyName("processedBy")]
    public string? ProcessedBy { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Net refund after restocking fee.
    /// </summary>
    [JsonIgnore]
    public decimal NetRefund => RefundAmount - RestockingFee;
}
