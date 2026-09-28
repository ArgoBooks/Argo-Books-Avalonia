using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Tracking;

/// <summary>
/// Represents a record of lost or damaged inventory.
/// </summary>
public class LostDamaged : IRecord
{
    /// <summary>
    /// Unique identifier (e.g., LOST-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Product ID.
    /// </summary>
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Inventory item ID.
    /// </summary>
    [JsonPropertyName("inventoryItemId")]
    public string? InventoryItemId { get; set; }

    /// <summary>
    /// Quantity lost or damaged.
    /// </summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    /// <summary>
    /// Reason for loss/damage.
    /// </summary>
    [JsonPropertyName("reason")]
    public LostDamagedReason Reason { get; set; }

    /// <summary>
    /// Date the loss/damage was discovered.
    /// </summary>
    [JsonPropertyName("dateDiscovered")]
    public DateTime DateDiscovered { get; set; }

    /// <summary>
    /// Monetary value lost.
    /// </summary>
    [JsonPropertyName("valueLost")]
    public decimal ValueLost { get; set; }

    /// <summary>
    /// Detailed notes about the incident.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Whether an insurance claim was filed.
    /// </summary>
    [JsonPropertyName("insuranceClaim")]
    public bool InsuranceClaim { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
