namespace ArgoBooks.Core.Services.Integrations;

/// <summary>
/// One Stripe charge with the detail needed for a full book entry. Amounts in the currency's
/// smallest unit (cents, or whole yen for a zero-decimal currency).
///
/// <see cref="FeeCurrency"/> is separate because the fee comes from the balance transaction,
/// which Stripe states in the settlement currency, not the charge's. Null means the same as
/// <see cref="Currency"/>.
/// </summary>
public record StripeChargeDetail(
    string ChargeId,
    long CreatedUnix,
    long GrossCents,
    long FeeCents,
    string Currency,
    string? CustomerName,
    string? CustomerEmail,
    string ProductName,
    long TaxCents,
    long DiscountCents,
    long AmountRefundedCents,
    string? FeeCurrency = null);
