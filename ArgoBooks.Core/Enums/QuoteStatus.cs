namespace ArgoBooks.Core.Enums;

/// <summary>
/// Status of a quote (estimate) sent to a customer.
/// </summary>
/// <remarks>
/// "Expired" is deliberately absent: it is a view of <see cref="Sent"/> plus a valid-until date in
/// the past, so it can never go stale in a saved file the way a stored status would.
/// </remarks>
public enum QuoteStatus
{
    /// <summary>Quote is being prepared.</summary>
    Draft,

    /// <summary>Quote has been sent to the customer.</summary>
    Sent,

    /// <summary>Customer accepted the quote.</summary>
    Accepted,

    /// <summary>Customer declined the quote.</summary>
    Declined,

    /// <summary>Quote became a draft invoice.</summary>
    Converted
}

/// <summary>
/// Extension methods for QuoteStatus.
/// </summary>
public static class QuoteStatusExtensions
{
    /// <summary>The display-only status for a sent quote whose valid-until date has passed.</summary>
    public const string Expired = "Expired";

    /// <summary>
    /// Gets the modal status options (statuses selectable when creating/editing).
    /// Converted is absent: only "Convert to invoice" may set it.
    /// </summary>
    public static string[] GetModalOptions()
    {
        return
        [
            nameof(QuoteStatus.Draft),
            nameof(QuoteStatus.Sent),
            nameof(QuoteStatus.Accepted),
            nameof(QuoteStatus.Declined)
        ];
    }

    /// <summary>
    /// Gets filter options including "All" as the first entry.
    /// </summary>
    public static string[] GetFilterOptions()
    {
        return
        [
            "All",
            nameof(QuoteStatus.Draft),
            nameof(QuoteStatus.Sent),
            Expired,
            nameof(QuoteStatus.Accepted),
            nameof(QuoteStatus.Declined),
            nameof(QuoteStatus.Converted)
        ];
    }

    /// <summary>
    /// Parses a status name back to the enum. Returns null for "All" and for the display-only
    /// "Expired", which the caller filters on separately.
    /// </summary>
    public static QuoteStatus? ParseQuoteStatus(string? value) =>
        Enum.TryParse<QuoteStatus>(value, ignoreCase: true, out var parsed) ? parsed : null;
}
