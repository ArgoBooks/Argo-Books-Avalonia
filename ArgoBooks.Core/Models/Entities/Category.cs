using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Entities;

/// <summary>
/// Represents a category for organizing products.
/// </summary>
public class Category : IRecord
{
    /// <summary>
    /// Unique identifier (e.g., CAT-REV-001; older files also hold CAT-SAL-001 and CAT-PUR-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Category type (Sales, Purchase, Rental).
    /// </summary>
    [JsonPropertyName("type")]
    public CategoryType Type { get; set; }

    /// <summary>
    /// Category name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parent category ID for hierarchical categories.
    /// </summary>
    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    /// <summary>
    /// Description of the category.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Color code for UI display (hex format).
    /// </summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = AppColors.CategoryDefault;

    /// <summary>
    /// Icon emoji for UI display.
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "📦";

}
