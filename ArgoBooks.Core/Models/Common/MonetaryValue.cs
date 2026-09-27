namespace ArgoBooks.Core.Models.Common;

/// <summary>
/// Represents a monetary value with support for currency conversion.
/// Stores both the original entry value and the USD equivalent for accurate conversions.
/// </summary>
public class MonetaryValue
{
    /// <summary>
    /// The amount in the original currency it was entered in.
    /// This value is never modified and can be restored exactly when switching back to the original currency.
    /// </summary>
    [JsonPropertyName("originalAmount")]
    public decimal OriginalAmount { get; set; }

    /// <summary>
    /// The ISO currency code of the original entry currency (e.g., "USD", "EUR", "CAD").
    /// </summary>
    [JsonPropertyName("originalCurrency")]
    public string OriginalCurrency { get; set; } = "USD";

    /// <summary>
    /// The amount converted to USD at the time of entry.
    /// USD is used as the base currency for all conversions to avoid compounding rounding errors.
    /// </summary>
    [JsonPropertyName("amountUSD")]
    public decimal AmountUSD { get; set; }

    /// <summary>
    /// The date when the exchange rate was captured (typically the transaction date).
    /// Used for historical rate lookups.
    /// </summary>
    [JsonPropertyName("rateDate")]
    public DateTime RateDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a new MonetaryValue with default values (0 USD).
    /// </summary>
    public MonetaryValue()
    {
    }

    /// <summary>
    /// Creates a new MonetaryValue with the specified amount in USD.
    /// Use this constructor for values that are already in USD.
    /// </summary>
    /// <param name="amountUSD">The amount in USD.</param>
    public MonetaryValue(decimal amountUSD)
    {
        OriginalAmount = amountUSD;
        OriginalCurrency = "USD";
        AmountUSD = amountUSD;
        RateDate = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a new MonetaryValue with the specified original amount, currency, and USD equivalent.
    /// </summary>
    /// <param name="originalAmount">The amount in the original currency.</param>
    /// <param name="originalCurrency">The ISO currency code (e.g., "CAD").</param>
    /// <param name="amountUSD">The amount converted to USD.</param>
    /// <param name="rateDate">The date of the exchange rate.</param>
    public MonetaryValue(decimal originalAmount, string originalCurrency, decimal amountUSD, DateTime rateDate)
    {
        OriginalAmount = originalAmount;
        OriginalCurrency = originalCurrency;
        AmountUSD = amountUSD;
        RateDate = rateDate;
    }

    /// <summary>
    /// Implicit conversion from decimal to MonetaryValue (assumes USD).
    /// </summary>
    public static implicit operator MonetaryValue(decimal amount) => new(amount);

    /// <summary>
    /// Exact-date display amount for <paramref name="targetCurrency"/>. Returns the exact original
    /// amount when the target is the original currency; the stored USD when the target is USD;
    /// otherwise converts USD-&gt;target via <paramref name="tryConvert"/>. Returns
    /// <see langword="false"/> when the exact-date rate is unavailable, so the caller shows a
    /// pending marker rather than a wrong-date number. See docs/Calculations.md (Rule 4).
    /// </summary>
    public bool TryGetDisplayAmount(
        string targetCurrency,
        Func<string, string, DateTime, (bool ok, decimal value)> tryConvert,
        out decimal result)
    {
        if (string.Equals(targetCurrency, OriginalCurrency, StringComparison.OrdinalIgnoreCase))
        {
            result = OriginalAmount;
            return true;
        }
        if (string.Equals(targetCurrency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            result = AmountUSD;
            return true;
        }
        var (ok, value) = tryConvert("USD", targetCurrency, RateDate);
        result = ok ? value : 0m;
        return ok;
    }

    /// <summary>
    /// Returns the USD amount for calculations and comparisons.
    /// </summary>
    public override string ToString() => $"{AmountUSD:F2} USD (original: {OriginalAmount:F2} {OriginalCurrency})";
}
