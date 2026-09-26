namespace ArgoBooks.Core.Models.Common;

/// <summary>
/// Represents a transaction that was saved offline without USD conversion.
/// Stored in a persistent queue that survives between sessions.
/// </summary>
public class PendingConversion
{
    /// <summary>
    /// The ID of the transaction awaiting conversion (e.g., "PUR-2026-00001" or "REV-2026-00001").
    /// </summary>
    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = "";

    /// <summary>
    /// The type of record: "Revenue", "Expense", "Payment", "PurchaseOrder", "Invoice", or
    /// "InventoryItem". For Payment and PurchaseOrder only <see cref="Total"/> is used (the single
    /// amount to convert); for InventoryItem, <see cref="Total"/> is the stock record's unit cost in
    /// <see cref="OriginalCurrency"/>; for Invoice, <see cref="Total"/> and <see cref="Balance"/> are
    /// used; the other amount fields stay 0.
    /// </summary>
    [JsonPropertyName("transactionType")]
    public string TransactionType { get; set; } = "";

    /// <summary>
    /// The ISO currency code of the original transaction (e.g., "EUR", "CAD").
    /// </summary>
    [JsonPropertyName("originalCurrency")]
    public string OriginalCurrency { get; set; } = "";

    /// <summary>
    /// The date of the transaction, used for exchange rate lookup.
    /// </summary>
    [JsonPropertyName("transactionDate")]
    public DateTime TransactionDate { get; set; }

    /// <summary>
    /// When this pending conversion entry was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Original amounts stored as backup for conversion

    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    [JsonPropertyName("taxAmount")]
    public decimal TaxAmount { get; set; }

    [JsonPropertyName("shippingCost")]
    public decimal ShippingCost { get; set; }

    [JsonPropertyName("discount")]
    public decimal Discount { get; set; }

    [JsonPropertyName("fee")]
    public decimal Fee { get; set; }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    /// <summary>Invoice outstanding balance to convert (Invoice entries only; 0 otherwise).</summary>
    [JsonPropertyName("balance")]
    public decimal Balance { get; set; }

    /// <summary>
    /// Which record the entry is for. Ids alone are not unique across types: an imported sheet
    /// keeps its own ids, so a stock record and a revenue can both be "1".
    /// </summary>
    [JsonIgnore]
    public PendingConversionKey Key => new(TransactionId, TransactionType);

    /// <summary>
    /// The rate the queue converted this entry at, set when it leaves the queue. Lets undo put a
    /// record back as converted rather than pending again. Not saved: undo history isn't either.
    /// </summary>
    [JsonIgnore]
    public decimal? ConvertedRate { get; set; }
}

/// <summary>Identifies a queued record by id and type together.</summary>
public readonly record struct PendingConversionKey(string TransactionId, string TransactionType);

/// <summary>The record types the conversion queue converts.</summary>
public static class PendingConversionType
{
    public const string Revenue = "Revenue";
    public const string Expense = "Expense";
    public const string Payment = "Payment";
    public const string PurchaseOrder = "PurchaseOrder";
    public const string Invoice = "Invoice";
    public const string InventoryItem = "InventoryItem";
}
