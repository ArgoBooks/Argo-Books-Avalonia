using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Transactions;

/// <summary>
/// Represents a payment received for an invoice.
/// </summary>
public class Payment
{
    /// <summary>
    /// Unique identifier (e.g., PAY-2024-00001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Invoice ID this payment is for. A payment is linked to either an invoice or a revenue.
    /// </summary>
    [JsonPropertyName("invoiceId")]
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>
    /// Revenue ID this payment is for, when it is recorded directly against a revenue rather than
    /// an invoice (e.g. a direct cash sale). Empty when the payment is linked to an invoice.
    /// </summary>
    [JsonPropertyName("revenueId")]
    public string RevenueId { get; set; } = string.Empty;

    /// <summary>
    /// Customer ID.
    /// </summary>
    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Date of payment.
    /// </summary>
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Payment amount.
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Payment method used.
    /// </summary>
    [JsonPropertyName("paymentMethod")]
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>
    /// Reference/transaction number.
    /// </summary>
    [JsonPropertyName("referenceNumber")]
    public string? ReferenceNumber { get; set; }

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

    #region Portal Support

    /// <summary>
    /// The source of this payment: Manual (entered in Argo Books) or
    /// Online (received via payment portal). Defaults to Manual.
    /// </summary>
    [JsonPropertyName("source")]
    public PaymentSource Source { get; set; } = PaymentSource.Manual;

    /// <summary>
    /// The portal payment ID from the server, used to prevent duplicate syncs.
    /// </summary>
    [JsonPropertyName("portalPaymentId")]
    public string? PortalPaymentId { get; set; }

    /// <summary>
    /// The provider's payment intent / order / capture ID. Used by the refund
    /// flow as the <c>provider_payment_id</c> parameter when calling the server.
    /// </summary>
    [JsonPropertyName("providerPaymentId")]
    public string? ProviderPaymentId { get; set; }

    /// <summary>
    /// The processing fee the customer paid on top of the invoice (for
    /// <c>pass_processing_fee</c> invoices). Zero when the fee was absorbed
    /// by the merchant or for manual / non-portal payments. The refund modal
    /// shows this as a checkable line so the merchant can return it to the
    /// customer along with the invoice items.
    /// </summary>
    [JsonPropertyName("processingFee")]
    public decimal ProcessingFee { get; set; }

    #endregion

    #region Currency Support

    /// <summary>
    /// The ISO currency code in which this payment was received (e.g., "USD", "EUR", "CAD").
    /// Defaults to "USD" for backward compatibility with existing data.
    /// </summary>
    [JsonPropertyName("originalCurrency")]
    public string OriginalCurrency { get; set; } = "USD";

    /// <summary>
    /// The payment amount converted to USD at the time of entry.
    /// </summary>
    [JsonPropertyName("amountUSD")]
    public decimal AmountUSD { get; set; }

    /// <summary>
    /// True when this payment was imported/saved without a USD conversion because its exact-date
    /// rate was unavailable (future-dated, or a gate miss). While pending, <see cref="EffectiveAmountUSD"/>
    /// reports 0 (Calculations.md §3) and <c>PendingConversionService</c> fills in <see cref="AmountUSD"/>
    /// once that date's rate is fetchable. Defaults to false for backward compatibility.
    /// </summary>
    [JsonPropertyName("isPendingConversion")]
    public bool IsPendingConversion { get; set; }

    /// <summary>
    /// Gets the effective amount in USD. Returns 0 while the conversion is pending (per Calculations.md
    /// §3, so a not-yet-converted row never contaminates a USD aggregate). For USD payments (including
    /// legacy data), returns Amount directly; for non-USD payments, returns the converted AmountUSD.
    /// </summary>
    [JsonIgnore]
    public decimal EffectiveAmountUSD =>
        IsPendingConversion ? 0m
        : string.Equals(OriginalCurrency, "USD", StringComparison.OrdinalIgnoreCase) ? Amount : AmountUSD;

    #endregion

    #region Refund Support

    /// <summary>
    /// True when this row represents a refund rather than a payment received.
    /// Refund rows store <see cref="Amount"/> as a negative value so existing
    /// aggregations (sum-of-payments) naturally yield net paid.
    /// </summary>
    [JsonPropertyName("isRefund")]
    public bool IsRefund { get; set; }

    /// <summary>
    /// Local Payment.Id of the original payment this refund offsets, when known.
    /// Null for refunds that arrived before the corresponding payment was synced
    /// (rare, but possible if sync is out of order).
    /// </summary>
    [JsonPropertyName("refundedFromPaymentId")]
    public string? RefundedFromPaymentId { get; set; }

    /// <summary>
    /// Server-side refund_requests.id when this refund was initiated through the
    /// Argo Books refund flow. Null for refunds created via the provider's own
    /// dashboard (e.g. Stripe Dashboard) that we received via webhook.
    /// </summary>
    [JsonPropertyName("refundRequestId")]
    public string? RefundRequestId { get; set; }

    /// <summary>
    /// User-supplied reason for the refund, captured at request time.
    /// </summary>
    [JsonPropertyName("refundReason")]
    public string? RefundReason { get; set; }

    /// <summary>
    /// For a refund, the part that gave back the invoice's security deposit, in the refund's
    /// currency. A deposit isn't revenue, so only the rest of the refund comes off revenue
    /// (docs/Calculations.md §8).
    /// </summary>
    [JsonPropertyName("depositAmount")]
    public decimal DepositAmount { get; set; }

    /// <summary>
    /// The share of a refund that came off revenue: all of it, less any security deposit it gave
    /// back (<see cref="DepositAmount"/>), which was never revenue.
    /// </summary>
    [JsonIgnore]
    public decimal RevenueShare => IsRefund && Amount != 0
        ? Math.Clamp(1m - DepositAmount / Math.Abs(Amount), 0m, 1m)
        : 1m;

    #endregion

    #region Bank Matching

    /// <summary>
    /// Whether this payment has been matched to a line on an imported bank statement.
    /// Defaults to false for backward compatibility with existing files.
    /// </summary>
    [JsonPropertyName("bankMatched")]
    public bool BankMatched { get; set; }

    /// <summary>
    /// When this payment was matched to a bank statement line, if matched.
    /// </summary>
    [JsonPropertyName("bankMatchedDate")]
    public DateTime? BankMatchedDate { get; set; }

    /// <summary>
    /// Id of the matched bank statement line, if matched.
    /// </summary>
    [JsonPropertyName("bankMatchedLineId")]
    public string? BankMatchedLineId { get; set; }

    #endregion
}
