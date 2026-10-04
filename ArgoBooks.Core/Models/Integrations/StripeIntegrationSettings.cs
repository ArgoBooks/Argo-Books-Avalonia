namespace ArgoBooks.Core.Models.Integrations;

/// <summary>
/// Connection state for the user's own Stripe account (their business revenue),
/// stored per company in the .argo file. Distinct from the payment portal's
/// Stripe Connect (which accepts customer payments).
/// </summary>
public class StripeIntegrationSettings
{
    /// <summary>The user-pasted restricted read-only Stripe secret key.</summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    /// <summary>True once a key has validated against Stripe.</summary>
    [JsonPropertyName("connected")]
    public bool Connected { get; set; }

    /// <summary>Human-readable label for the connected account (business name or email).</summary>
    [JsonPropertyName("accountLabel")]
    public string? AccountLabel { get; set; }

    /// <summary>Cursor (a Stripe balance-transaction id) marking the last synced activity.</summary>
    [JsonPropertyName("lastSyncCursor")]
    public string? LastSyncCursor { get; set; }

    /// <summary>
    /// Charges that had not gone through when they were last seen. Each is asked about by id at
    /// the next sync and imported once it succeeds.
    /// </summary>
    [JsonPropertyName("pendingChargeIds")]
    public List<string> PendingChargeIds { get; set; } = new();

    /// <summary>The newest Stripe refund seen at the last sync. Refunds are read back to it.</summary>
    [JsonPropertyName("lastRefundCursor")]
    public string? LastRefundCursor { get; set; }

    /// <summary>Timestamp of the last successful sync.</summary>
    [JsonPropertyName("lastSyncTime")]
    public DateTime? LastSyncTime { get; set; }

    /// <summary>Stripe payouts that have been imported, for deduplication during bank import.</summary>
    [JsonPropertyName("importedPayouts")]
    public List<StripePayoutRecord> ImportedPayouts { get; set; } = new();
}
