using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Inventory;

/// <summary>
/// Represents a transfer of stock between locations.
/// </summary>
public class StockTransfer : IRecord
{
    /// <summary>
    /// Unique identifier (e.g., TRF-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Inventory item ID being transferred.
    /// </summary>
    [JsonPropertyName("inventoryItemId")]
    public string InventoryItemId { get; set; } = string.Empty;

    /// <summary>
    /// Source location ID.
    /// </summary>
    [JsonPropertyName("sourceLocationId")]
    public string SourceLocationId { get; set; } = string.Empty;

    /// <summary>
    /// Destination location ID.
    /// </summary>
    [JsonPropertyName("destinationLocationId")]
    public string DestinationLocationId { get; set; } = string.Empty;

    /// <summary>
    /// Quantity being transferred.
    /// </summary>
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Date of transfer.
    /// </summary>
    [JsonPropertyName("transferDate")]
    public DateTime TransferDate { get; set; }

    /// <summary>
    /// Transfer status.
    /// </summary>
    [JsonPropertyName("status")]
    public TransferStatus Status { get; set; } = TransferStatus.Pending;

    /// <summary>
    /// Additional notes.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// When the transfer was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the transfer was completed.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }
}
