using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Inventory;

/// <summary>
/// Line item for a purchase order.
/// </summary>
public class PurchaseOrderLineItem
{
    /// <summary>
    /// Product ID.
    /// </summary>
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Quantity ordered.
    /// </summary>
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Quantity received so far.
    /// </summary>
    [JsonPropertyName("quantityReceived")]
    public decimal QuantityReceived { get; set; }

    /// <summary>
    /// Unit cost.
    /// </summary>
    [JsonPropertyName("unitCost")]
    public decimal UnitCost { get; set; }

    /// <summary>
    /// Line total.
    /// </summary>
    [JsonPropertyName("total")]
    public decimal Total => Quantity * UnitCost;

    /// <summary>
    /// Whether this line is fully received.
    /// </summary>
    [JsonIgnore]
    public bool IsFullyReceived => QuantityReceived >= Quantity;
}

/// <summary>
/// Represents a purchase order to a supplier.
/// </summary>
public class PurchaseOrder
{
    /// <summary>
    /// Unique identifier (e.g., PO-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display PO number (e.g., #PO-2024-001).
    /// </summary>
    [JsonPropertyName("poNumber")]
    public string PoNumber { get; set; } = string.Empty;

    /// <summary>
    /// Supplier ID.
    /// </summary>
    [JsonPropertyName("supplierId")]
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>
    /// Date the order was placed.
    /// </summary>
    [JsonPropertyName("orderDate")]
    public DateTime OrderDate { get; set; }

    /// <summary>
    /// Expected delivery date.
    /// </summary>
    [JsonPropertyName("expectedDeliveryDate")]
    public DateTime ExpectedDeliveryDate { get; set; }

    /// <summary>
    /// Line items in this order.
    /// </summary>
    [JsonPropertyName("lineItems")]
    public List<PurchaseOrderLineItem> LineItems { get; set; } = [];

    /// <summary>
    /// Subtotal before shipping.
    /// </summary>
    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Shipping cost.
    /// </summary>
    [JsonPropertyName("shippingCost")]
    public decimal ShippingCost { get; set; }

    /// <summary>
    /// Total order amount.
    /// </summary>
    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    /// <summary>
    /// Purchase order status.
    /// </summary>
    [JsonPropertyName("status")]
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    /// <summary>
    /// Additional notes.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// When the order was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the order was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether all items have been received.
    /// </summary>
    [JsonIgnore]
    public bool IsFullyReceived => LineItems.Count > 0 && LineItems.All(li => li.IsFullyReceived);

    #region Currency Support

    /// <summary>
    /// The ISO currency code in which this PO was originally created (e.g., "USD", "EUR").
    /// Defaults to "USD" for backward compatibility with existing data.
    /// </summary>
    [JsonPropertyName("originalCurrency")]
    public string OriginalCurrency { get; set; } = "USD";

    /// <summary>
    /// The total amount converted to USD at the time of creation.
    /// </summary>
    [JsonPropertyName("totalUSD")]
    public decimal TotalUSD { get; set; }

    /// <summary>
    /// True when this PO was imported without a USD conversion because its exact-date rate was
    /// unavailable (future-dated, or a gate miss). While pending, <see cref="EffectiveTotalUSD"/>
    /// reports 0 (Calculations.md §3) and <c>PendingConversionService</c> fills in <see cref="TotalUSD"/>
    /// once that date's rate is fetchable. Defaults to false for backward compatibility.
    /// </summary>
    [JsonPropertyName("isPendingConversion")]
    public bool IsPendingConversion { get; set; }

    /// <summary>
    /// Whether this PO's original currency is USD (including legacy data which defaults to USD).
    /// </summary>
    [JsonIgnore]
    private bool IsUSD => string.Equals(OriginalCurrency, "USD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the effective total in USD. Returns 0 while the conversion is pending (per Calculations.md
    /// §3). For USD POs (including legacy data), returns Total directly; for non-USD POs, returns TotalUSD
    /// (or 0 if conversion data is missing).
    /// </summary>
    [JsonIgnore]
    public decimal EffectiveTotalUSD => IsPendingConversion ? 0m : TotalUSD > 0 ? TotalUSD : IsUSD ? Total : 0;

    #endregion
}
