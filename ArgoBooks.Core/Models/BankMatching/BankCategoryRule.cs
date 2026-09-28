using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Models.BankMatching;

public enum RuleMatchType { Contains, Exact }
public enum RuleSource { Learned, Manual }

/// <summary>
/// Maps a normalized merchant token to a category (and optionally a transaction type and
/// supplier/customer). Stored per company; learned automatically when the user categorizes
/// a created line, or added/edited manually in Settings.
/// </summary>
public class BankCategoryRule : IRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    [JsonPropertyName("matchType")]
    public RuleMatchType MatchType { get; set; } = RuleMatchType.Contains;

    [JsonPropertyName("categoryId")]
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>Existing product this merchant maps to, so a repeat import pre-fills the product directly.</summary>
    [JsonPropertyName("productId")]
    public string? ProductId { get; set; }

    [JsonPropertyName("transactionType")]
    public BookRecordType? TransactionType { get; set; }

    [JsonPropertyName("counterpartyId")]
    public string? CounterpartyId { get; set; }

    [JsonPropertyName("source")]
    public RuleSource Source { get; set; } = RuleSource.Learned;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
