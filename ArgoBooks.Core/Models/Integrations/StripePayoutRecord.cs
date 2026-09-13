namespace ArgoBooks.Core.Models.Integrations;

/// <summary>
/// A Stripe payout that has been imported, remembered so a later bank-statement
/// import can auto-ignore the matching deposit instead of double-counting it.
/// </summary>
public class StripePayoutRecord
{
    [JsonPropertyName("payoutId")]
    public string StripePayoutId { get; set; } = string.Empty;

    /// <summary>In the smallest unit of <see cref="Currency"/>: cents for USD, whole yen for JPY.</summary>
    [JsonPropertyName("amountCents")]
    public long AmountCents { get; set; }

    /// <summary>Null on payouts saved before the currency was recorded.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }
}
