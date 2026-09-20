namespace ArgoBooks.Core.Models.Portal;

/// <summary>
/// A quote as the portal publishes it, i.e. what the customer sees and answers.
/// </summary>
public class PortalQuotePublishRequest
{
    [JsonPropertyName("quoteId")]
    public string QuoteId { get; set; } = string.Empty;

    [JsonPropertyName("quoteNumber")]
    public string QuoteNumber { get; set; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("customerEmail")]
    public string? CustomerEmail { get; set; }

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>yyyy-MM-dd.</summary>
    [JsonPropertyName("issueDate")]
    public string IssueDate { get; set; } = string.Empty;

    /// <summary>yyyy-MM-dd, or null when the quote has no expiry.</summary>
    [JsonPropertyName("validUntil")]
    public string? ValidUntil { get; set; }

    [JsonPropertyName("lineItems")]
    public List<PortalLineItem> LineItems { get; set; } = [];

    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }

    [JsonPropertyName("taxAmount")]
    public decimal TaxAmount { get; set; }

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>"sent" or "cancelled".</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "sent";

    /// <summary>
    /// A deliberately revised quote, which clears an answer the customer already gave and asks
    /// them again. A plain "sent" publish leaves an existing answer alone and emails nobody, so
    /// this must only ever be true when the user was asked and said yes.
    /// </summary>
    [JsonPropertyName("revision")]
    public bool Revision { get; set; }

    [JsonPropertyName("sendEmail")]
    public bool SendEmail { get; set; }

    /// <summary>The user's optional personal note, capped at 500 characters by the caller.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("replyTo")]
    public string? ReplyTo { get; set; }

    /// <summary>The quote rendered with the user's own invoice template.</summary>
    [JsonPropertyName("customQuoteHtml")]
    public string? CustomQuoteHtml { get; set; }
}

/// <summary>
/// Response from publishing a quote to the portal.
/// </summary>
public class PortalQuotePublishResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("quoteToken")]
    public string? QuoteToken { get; set; }

    [JsonPropertyName("quoteUrl")]
    public string? QuoteUrl { get; set; }

    /// <summary>"sent", "accepted", "declined" or "cancelled".</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("respondedAt")]
    public DateTime? RespondedAt { get; set; }

    [JsonPropertyName("responseNote")]
    public string? ResponseNote { get; set; }

    [JsonPropertyName("emailSent")]
    public bool EmailSent { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// A failed publish that leaves open whether the portal saved the quote and emailed the
    /// customer: a timeout, a dropped connection, a server fault. Not part of the response body.
    /// </summary>
    [JsonIgnore]
    public bool MayHavePublished { get; set; }
}

/// <summary>
/// A customer's answer to a quote, as returned by the sync endpoint.
/// </summary>
public class PortalQuoteResponseRecord
{
    [JsonPropertyName("quoteId")]
    public string QuoteId { get; set; } = string.Empty;

    /// <summary>"accepted" or "declined".</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("respondedAt")]
    public DateTime? RespondedAt { get; set; }

    [JsonPropertyName("responseNote")]
    public string? ResponseNote { get; set; }
}

/// <summary>
/// Response from the quote sync endpoint: the answers this device hasn't confirmed yet.
/// </summary>
public class PortalQuoteSyncResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("quotes")]
    public List<PortalQuoteResponseRecord> Quotes { get; set; } = [];

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("syncTimestamp")]
    public DateTime? SyncTimestamp { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Request confirming which quote answers have been applied locally.
/// </summary>
public class PortalQuoteSyncConfirmRequest
{
    [JsonPropertyName("quoteIds")]
    public List<string> QuoteIds { get; set; } = [];
}
