using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Models.Transactions;

/// <summary>
/// Base class for financial transactions (purchases and sales).
/// </summary>
public abstract class Transaction : IRecord
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Date and time of the transaction.
    /// </summary>
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Accountant ID who processed the transaction.
    /// </summary>
    [JsonPropertyName("accountantId")]
    public string? AccountantId { get; set; }

    /// <summary>
    /// Description of the transaction.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Line items in this transaction.
    /// </summary>
    [JsonPropertyName("lineItems")]
    public List<LineItem> LineItems { get; set; } = [];

    /// <summary>
    /// Quantity.
    /// </summary>
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// Unit price.
    /// </summary>
    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Amount before tax (Quantity * UnitPrice).
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Tax rate percentage.
    /// </summary>
    [JsonPropertyName("taxRate")]
    public decimal TaxRate { get; set; }

    /// <summary>
    /// Tax amount.
    /// </summary>
    [JsonPropertyName("taxAmount")]
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// Shipping cost.
    /// </summary>
    [JsonPropertyName("shippingCost")]
    public decimal ShippingCost { get; set; }

    /// <summary>
    /// Discount amount.
    /// </summary>
    [JsonPropertyName("discount")]
    public decimal Discount { get; set; }

    /// <summary>
    /// Fee amount (e.g., service fee, processing fee).
    /// </summary>
    [JsonPropertyName("fee")]
    public decimal Fee { get; set; }

    /// <summary>
    /// Total amount including tax.
    /// </summary>
    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    /// <summary>
    /// External reference number imported from spreadsheets or payment portals.
    /// This is an internal/system field used for data import/export round-tripping
    /// and should not be displayed to users. Use <see cref="TransactionBase.Id"/> for display purposes.
    /// </summary>
    [JsonPropertyName("referenceNumber")]
    public string ReferenceNumber { get; set; } = string.Empty;

    /// <summary>
    /// Payment method used.
    /// </summary>
    [JsonPropertyName("paymentMethod")]
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>
    /// Additional notes.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Associated receipt ID.
    /// </summary>
    [JsonPropertyName("receiptId")]
    public string? ReceiptId { get; set; }

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

    #region Bank Matching

    /// <summary>
    /// Whether this transaction has been matched to a line on an imported bank statement.
    /// Defaults to false for backward compatibility with existing files.
    /// </summary>
    [JsonPropertyName("bankMatched")]
    public bool BankMatched { get; set; }

    /// <summary>
    /// When this transaction was matched to a bank statement line, if matched.
    /// </summary>
    [JsonPropertyName("bankMatchedDate")]
    public DateTime? BankMatchedDate { get; set; }

    /// <summary>
    /// Id of the matched bank statement line, if matched.
    /// </summary>
    [JsonPropertyName("bankMatchedLineId")]
    public string? BankMatchedLineId { get; set; }

    #endregion

    #region Currency Support

    /// <summary>
    /// The ISO currency code in which this transaction was originally entered (e.g., "USD", "EUR", "CAD").
    /// Defaults to "USD" for backward compatibility with existing data.
    /// </summary>
    [JsonPropertyName("originalCurrency")]
    public string OriginalCurrency { get; set; } = "USD";

    /// <summary>
    /// The total amount converted to USD at the time of entry.
    /// Used as the base for all currency conversions.
    /// If null/0, falls back to Total (assumed to be USD for legacy data).
    /// </summary>
    [JsonPropertyName("totalUSD")]
    public decimal TotalUSD { get; set; }

    /// <summary>
    /// The unit price converted to USD at the time of entry.
    /// </summary>
    [JsonPropertyName("unitPriceUSD")]
    public decimal UnitPriceUSD { get; set; }

    /// <summary>
    /// The shipping cost converted to USD at the time of entry.
    /// </summary>
    [JsonPropertyName("shippingCostUSD")]
    public decimal ShippingCostUSD { get; set; }

    /// <summary>
    /// The tax amount converted to USD at the time of entry.
    /// </summary>
    [JsonPropertyName("taxAmountUSD")]
    public decimal TaxAmountUSD { get; set; }

    /// <summary>
    /// The discount amount converted to USD at the time of entry.
    /// </summary>
    [JsonPropertyName("discountUSD")]
    public decimal DiscountUSD { get; set; }

    /// <summary>
    /// The fee amount converted to USD at the time of entry.
    /// </summary>
    [JsonPropertyName("feeUSD")]
    public decimal FeeUSD { get; set; }

    /// <summary>
    /// Whether this transaction was saved offline and is awaiting USD conversion.
    /// When true, all Effective*USD properties return 0 to prevent wrong cross-currency aggregation.
    /// </summary>
    [JsonPropertyName("isPendingConversion")]
    public bool IsPendingConversion { get; set; }

    /// <summary>
    /// Whether this transaction's original currency is USD (including legacy data which defaults to USD).
    /// </summary>
    [JsonIgnore]
    private bool IsUSD => string.Equals(OriginalCurrency, "USD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the effective total in USD. For USD transactions (including legacy data), returns Total directly.
    /// For non-USD transactions, returns the converted TotalUSD value.
    /// Returns 0 for pending-conversion transactions to avoid cross-currency contamination.
    /// </summary>
    [JsonIgnore]
    public decimal EffectiveTotalUSD => IsPendingConversion ? 0 : IsUSD ? Total : TotalUSD;

    /// <summary>
    /// Gets the effective tax amount in USD. Uses TaxAmountUSD when available,
    /// derives from conversion ratio (TaxAmount * TotalUSD/Total) for foreign-currency
    /// transactions missing TaxAmountUSD, or falls back to TaxAmount for legacy USD data.
    /// Returns 0 for pending-conversion transactions and non-USD transactions without conversion data.
    /// </summary>
    [JsonIgnore]
    public decimal EffectiveTaxAmountUSD => IsPendingConversion ? 0
        : TaxAmountUSD > 0 ? TaxAmountUSD
        // Full-precision derivation (no 2dp round) so it stays consistent with the unrounded USD base
        // and EffectiveSubtotalUSD; display rounds at the boundary. See docs/Calculations.md Rule 3.
        : TotalUSD > 0 && Total > 0 ? TaxAmount * (TotalUSD / Total)
        : IsUSD ? TaxAmount : 0;

    /// <summary>
    /// Gets the effective pre-tax subtotal in USD (excludes tax).
    /// Tax is a liability, not revenue/expense, so this matches Income Statement treatment.
    /// Returns 0 for pending-conversion transactions.
    /// </summary>
    [JsonIgnore]
    public decimal EffectiveSubtotalUSD => IsPendingConversion ? 0 : (EffectiveTotalUSD - EffectiveTaxAmountUSD);

    /// <summary>
    /// Gets the effective unit price in USD. For USD transactions, returns UnitPrice directly.
    /// For non-USD transactions, returns the converted UnitPriceUSD value.
    /// Returns 0 for pending-conversion transactions.
    /// </summary>
    [JsonIgnore]
    public decimal EffectiveUnitPriceUSD => IsPendingConversion ? 0 : IsUSD ? UnitPrice : UnitPriceUSD;

    /// <summary>
    /// Gets the effective shipping cost in USD. For USD transactions, returns ShippingCost directly.
    /// For non-USD transactions, returns the converted ShippingCostUSD value.
    /// Returns 0 for pending-conversion transactions.
    /// </summary>
    [JsonIgnore]
    public decimal EffectiveShippingCostUSD => IsPendingConversion ? 0 : IsUSD ? ShippingCost : ShippingCostUSD;

    #endregion

    #region Recurring

    /// <summary>Set on entries produced by a recurring schedule, so an occurrence traces back.</summary>
    [JsonPropertyName("recurringScheduleId")]
    public string? RecurringScheduleId { get; set; }

    /// <summary>The scheduled date this was generated for, which Date may later diverge from.</summary>
    [JsonPropertyName("occurrenceDate")]
    public DateTime? OccurrenceDate { get; set; }

    /// <summary>Display-only flag. Reports and totals still include the transaction.</summary>
    [JsonPropertyName("needsReview")]
    public bool NeedsReview { get; set; }

    #endregion
}
