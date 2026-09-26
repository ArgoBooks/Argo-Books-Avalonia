using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Models.Entities;

/// <summary>
/// Represents a warehouse or building.
/// </summary>
public class Location : IRecord
{
    /// <summary>
    /// Unique identifier (e.g., LOC-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Location name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Physical address.
    /// </summary>
    [JsonPropertyName("address")]
    public Address Address { get; set; } = new();

    /// <summary>
    /// Contact person at this location.
    /// </summary>
    [JsonPropertyName("contactPerson")]
    public string ContactPerson { get; set; } = string.Empty;

    /// <summary>
    /// Phone number.
    /// </summary>
    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Maximum capacity (in units).
    /// </summary>
    [JsonPropertyName("capacity")]
    public int Capacity { get; set; }

    /// <summary>
    /// Current utilization (units in use).
    /// </summary>
    [JsonPropertyName("currentUtilization")]
    public int CurrentUtilization { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Calculated utilization percentage.
    /// </summary>
    [JsonIgnore]
    public double UtilizationPercentage => Capacity > 0 ? (double)CurrentUtilization / Capacity * 100 : 0;
}
