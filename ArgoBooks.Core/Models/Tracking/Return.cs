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
    /// The tax part of <see cref="RefundAmount"/>, the returned items' share of the transaction's tax.
    /// Null for a return recorded before returns could be partial.
    /// </summary>
    [JsonPropertyName("refundTax")]
    public decimal? RefundTax { get; set; }

    /// <summary>
    /// The credit note for the part of the refund taken off what the customer still owed on the sale,
    /// when they had not paid it all. <see cref="RefundRecordId"/> is the refund of the rest, if any.
    /// </summary>
    [JsonPropertyName("creditNoteId")]
    public string? CreditNoteId { get; set; }

    /// <summary>Whether the refund includes the transaction's shipping.</summary>
    [JsonPropertyName("includesShipping")]
    public bool IncludesShipping { get; set; }

    /// <summary>Whether the refund includes the transaction's fee.</summary>
    [JsonPropertyName("includesFee")]
    public bool IncludesFee { get; set; }

    /// <summary>Whether the returned goods went back into stock, for a sale, or out of it, for a purchase.</summary>
    [JsonPropertyName("restocked")]
    public bool Restocked { get; set; }

    /// <summary>
    /// What the sold goods that went back into stock had cost, in USD, which comes off cost of goods sold
    /// on the return date (docs/Calculations.md §14). Zero for a purchase, or goods not put back.
    /// </summary>
    [JsonPropertyName("costOfGoodsReturnedUSD")]
    public decimal CostOfGoodsReturnedUSD { get; set; }

    /// <summary>
    /// The refund recorded with the return: a refund payment for a sale, or a supplier credit (an expense
    /// below zero) for a purchase. Null when nothing was paid back, as for a sale that was never paid.
    /// </summary>
    [JsonPropertyName("refundRecordId")]
    public string? RefundRecordId { get; set; }

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
