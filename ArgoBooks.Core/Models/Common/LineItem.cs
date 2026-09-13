
namespace ArgoBooks.Core.Models.Common;

/// <summary>
/// Represents a line item in a transaction (revenue, invoice, expense order, etc.).
/// </summary>
public class LineItem
{
    /// <summary>
    /// Product ID if linked to a product.
    /// </summary>
    [JsonPropertyName("productId")]
    public string? ProductId { get; set; }

    /// <summary>
    /// Rental record ID if this line item is for a rental charge.
    /// </summary>
    [JsonPropertyName("rentalRecordId")]
    public string? RentalRecordId { get; set; }

    /// <summary>
    /// Revenue record ID if this line item is for a revenue transaction.
    /// </summary>
    [JsonPropertyName("revenueRecordId")]
    public string? RevenueRecordId { get; set; }

    /// <summary>
    /// Description of the item or service.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Quantity of items.
    /// </summary>
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Unit price or rate per item.
    /// </summary>
    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Tax rate as a decimal (e.g., 0.08 for 8%).
    /// </summary>
    [JsonPropertyName("taxRate")]
    public decimal TaxRate { get; set; }

    /// <summary>
    /// Discount amount applied to this line item.
    /// </summary>
    [JsonPropertyName("discount")]
    public decimal Discount { get; set; }

    /// <summary>
    /// Calculated subtotal (quantity * unitPrice - discount).
    /// </summary>
    [JsonIgnore]
    public decimal Subtotal => SubtotalOf(Quantity, UnitPrice, Discount);

    /// <summary>
    /// The line-subtotal formula on its own, for the invoice form's line rows, which hold the same
    /// three numbers in nullable fields. A discount bigger than the line zeroes it rather than
    /// turning the line, and everything summed from it, negative.
    /// </summary>
    public static decimal SubtotalOf(decimal quantity, decimal unitPrice, decimal discount) =>
        Math.Round(Math.Max(0, (quantity * unitPrice) - discount), 2);

    /// <summary>
    /// Calculated tax amount.
    /// </summary>
    [JsonIgnore]
    public decimal TaxAmount => Math.Round(Subtotal * TaxRate, 2);

    /// <summary>
    /// Calculated total amount including tax.
    /// </summary>
    [JsonIgnore]
    public decimal Amount => Math.Round(Subtotal + TaxAmount, 2);
}
