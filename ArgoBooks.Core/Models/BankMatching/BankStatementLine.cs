using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.BankMatching;

/// <summary>
/// A single line imported from a bank statement. These are reference data used to
/// verify the books; they are never committed as expense/revenue transactions.
/// </summary>
public class BankStatementLine
{
    /// <summary>Unique identifier for this line within its import session.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Date the bank posted the transaction.</summary>
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>Raw memo/description from the bank.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Signed canonical amount: negative = money out of the account, positive = money in.
    /// Derived from a signed Amount column when present, otherwise Credit - Debit.
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>Raw debit value as imported, if the statement used separate debit/credit columns.</summary>
    [JsonPropertyName("debit")]
    public decimal? Debit { get; set; }

    /// <summary>Raw credit value as imported, if the statement used separate debit/credit columns.</summary>
    [JsonPropertyName("credit")]
    public decimal? Credit { get; set; }

    /// <summary>Running balance reported by the statement, if present.</summary>
    [JsonPropertyName("balance")]
    public decimal? Balance { get; set; }

    /// <summary>Bank-supplied reference / check number, if present.</summary>
    [JsonPropertyName("rawReference")]
    public string RawReference { get; set; } = string.Empty;

    /// <summary>Current match state of this line.</summary>
    [JsonPropertyName("matchStatus")]
    public BankLineMatchStatus MatchStatus { get; set; } = BankLineMatchStatus.Unmatched;

    /// <summary>The kind of book record this line is matched to, if matched.</summary>
    [JsonPropertyName("matchedRecordType")]
    public BookRecordType? MatchedRecordType { get; set; }

    /// <summary>The id of the matched book record, if matched.</summary>
    [JsonPropertyName("matchedRecordId")]
    public string? MatchedRecordId { get; set; }

    /// <summary>When the match was confirmed.</summary>
    [JsonPropertyName("matchedDate")]
    public DateTime? MatchedDate { get; set; }

    /// <summary>Confidence (0..1) of the confirmed match.</summary>
    [JsonPropertyName("matchConfidence")]
    public double MatchConfidence { get; set; }

    /// <summary>When Ignored, an optional human reason (e.g. an auto-ignored Stripe payout).</summary>
    [JsonPropertyName("ignoreReason")]
    public string? IgnoreReason { get; set; }

    /// <summary>
    /// When auto-ignored as an imported Stripe payout, the payout it was ignored for, so that one
    /// payout never accounts for a second deposit.
    /// </summary>
    [JsonPropertyName("stripePayoutId")]
    public string? StripePayoutId { get; set; }

    /// <summary>Zero-based source row index in the imported file (for diagnostics only).</summary>
    [JsonIgnore]
    public int SourceRowIndex { get; set; }
}
