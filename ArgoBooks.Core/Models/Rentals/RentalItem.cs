using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Rentals;

/// <summary>
/// Represents an item available for rental, linked to an inventory item.
/// Stock is tracked via the linked InventoryItem.InStock, no local quantity fields.
/// </summary>
public class RentalItem : IRecord
{
    /// <summary>
    /// Unique identifier (e.g., RNT-ITM-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Inventory item ID (linked to an InventoryItem for stock tracking).
    /// </summary>
    [JsonPropertyName("inventoryItemId")]
    public string InventoryItemId { get; set; } = string.Empty;

    /// <summary>
    /// Daily rental rate.
    /// </summary>
    [JsonPropertyName("dailyRate")]
    public decimal DailyRate { get; set; }

    /// <summary>
    /// Weekly rental rate.
    /// </summary>
    [JsonPropertyName("weeklyRate")]
    public decimal WeeklyRate { get; set; }

    /// <summary>
    /// Monthly rental rate.
    /// </summary>
    [JsonPropertyName("monthlyRate")]
    public decimal MonthlyRate { get; set; }

    /// <summary>
    /// Security deposit amount.
    /// </summary>
    [JsonPropertyName("securityDeposit")]
    public decimal SecurityDeposit { get; set; }

    /// <summary>
    /// Item status.
    /// </summary>
    [JsonPropertyName("status")]
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    /// <summary>
    /// Additional notes.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// When the record was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the record was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
