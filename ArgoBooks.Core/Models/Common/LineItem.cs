
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
    /// The location whose stock this line moved, for a product that tracks inventory.
    /// </summary>
    [JsonPropertyName("locationId")]
    public string? LocationId { get; set; }

    /// <summary>
    /// What the tracked stock this sale line took cost, in USD, fixed when the sale was saved.
    /// Null on a line that sold no tracked stock. See docs/Calculations.md §14.
    /// </summary>
    [JsonPropertyName("costOfGoodsUSD")]
    public decimal? CostOfGoodsUSD { get; set; }

    /// <summary>
    /// True when this sale line took stock whose cost was still waiting for its exchange rate.
    /// <see cref="CostOfGoodsUSD"/> is 0 until that cost converts, and is then filled in.
    /// </summary>
    [JsonPropertyName("isCostOfGoodsPending")]
    public bool IsCostOfGoodsPending { get; set; }

    /// <summary>
    /// Opening units this sale line used up, which carry no cost. Given back when the sale
    /// is edited or deleted.
    /// </summary>
    [JsonPropertyName("openingUnitsUsed")]
    public decimal OpeningUnitsUsed { get; set; }

    /// <summary>
    /// True when this purchase line bought tracked stock, so its amount counts as stock
    /// rather than an expense.
    /// </summary>
    [JsonPropertyName("isStockPurchase")]
    public bool IsStockPurchase { get; set; }

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

    /// <summary>A copy with every field, including what stock the line took.</summary>
    public LineItem Clone() => (LineItem)MemberwiseClone();
}
