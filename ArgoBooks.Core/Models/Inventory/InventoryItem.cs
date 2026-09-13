using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Inventory;

/// <summary>
/// Represents stock levels for a product at a location.
/// </summary>
public class InventoryItem
{
    /// <summary>
    /// Unique identifier (e.g., INV-ITM-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Product ID.
    /// </summary>
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Stock Keeping Unit.
    /// </summary>
    [JsonPropertyName("sku")]
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// Location ID where stock is stored.
    /// </summary>
    [JsonPropertyName("locationId")]
    public string LocationId { get; set; } = string.Empty;

    /// <summary>
    /// Total quantity in stock.
    /// </summary>
    [JsonPropertyName("inStock")]
    public int InStock { get; set; }

    /// <summary>
    /// Quantity reserved for orders.
    /// </summary>
    [JsonPropertyName("reserved")]
    public int Reserved { get; set; }

    /// <summary>
    /// Quantity available for sale.
    /// </summary>
    [JsonIgnore]
    public int Available => InStock - Reserved;

    /// <summary>
    /// Stock level at which to reorder.
    /// </summary>
    [JsonPropertyName("reorderPoint")]
    public int ReorderPoint { get; set; }

    /// <summary>
    /// Stock level considered overstock.
    /// </summary>
    [JsonPropertyName("overstockThreshold")]
    public int OverstockThreshold { get; set; }

    /// <summary>
    /// Cost per unit.
    /// </summary>
    [JsonPropertyName("unitCost")]
    public decimal UnitCost { get; set; }

    /// <summary>
    /// Unit of measure (e.g., Each, Box, Case).
    /// </summary>
    [JsonPropertyName("unitOfMeasure")]
    public string UnitOfMeasure { get; set; } = "Each";

    /// <summary>
    /// Inventory status based on stock levels.
    /// </summary>
    [JsonPropertyName("status")]
    public InventoryStatus Status { get; set; } = InventoryStatus.InStock;

    /// <summary>
    /// When stock levels were last updated.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Calculates the current status based on stock levels.
    /// </summary>
    public InventoryStatus CalculateStatus() => CalculateStatus(InStock);

    /// <summary>
    /// Calculates the status this item would have at the given stock level.
    /// </summary>
    public InventoryStatus CalculateStatus(int stock)
    {
        if (stock == 0)
            return InventoryStatus.OutOfStock;
        if (stock >= OverstockThreshold && OverstockThreshold > 0)
            return InventoryStatus.Overstock;
        if (stock <= ReorderPoint)
            return InventoryStatus.LowStock;
        return InventoryStatus.InStock;
    }

    /// <summary>
    /// Total value of inventory at cost.
    /// </summary>
    [JsonIgnore]
    public decimal TotalValue => InStock * UnitCost;
}
