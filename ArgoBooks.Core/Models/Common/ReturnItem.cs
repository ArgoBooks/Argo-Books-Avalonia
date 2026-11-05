
namespace ArgoBooks.Core.Models.Common;

/// <summary>
/// Represents an item being returned.
/// </summary>
public class ReturnItem
{
    /// <summary>
    /// Product ID of the returned item.
    /// </summary>
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Quantity being returned.
    /// </summary>
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// The line of the sale or purchase it came from, by position. Null for a return recorded before
    /// returns could be partial, which took back the whole transaction.
    /// </summary>
    [JsonPropertyName("lineIndex")]
    public int? LineIndex { get; set; }

    /// <summary>
    /// Reason for the return.
    /// </summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}
