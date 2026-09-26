using ArgoBooks.Core.Services;

namespace ArgoBooks.Core.Models.Inventory;

/// <summary>
/// Settings for the purchase order email API integration.
/// Mirrors InvoiceEmailSettings: authentication is handled via the portal API key.
/// </summary>
public class PurchaseOrderEmailSettings
{
    /// <summary>
    /// The purchase order email API endpoint URL.
    /// </summary>
    public static readonly string ApiEndpoint = $"{ApiConfig.BaseUrl}/api/purchase-order/send-email.php";

    [JsonPropertyName("fromName")]
    public string FromName { get; set; } = string.Empty;

    [JsonPropertyName("fromEmail")]
    public string FromEmail { get; set; } = string.Empty;

    [JsonPropertyName("replyToEmail")]
    public string ReplyToEmail { get; set; } = string.Empty;

    [JsonPropertyName("bccEmail")]
    public string BccEmail { get; set; } = string.Empty;

    /// <summary>
    /// Default subject template. Placeholders: {PoNumber}, {CompanyName}, {OrderDate}, {Total}.
    /// </summary>
    [JsonPropertyName("subjectTemplate")]
    public string SubjectTemplate { get; set; } = "Purchase Order {PoNumber} from {CompanyName}";

    /// <summary>
    /// Default message body template. Placeholders: {SupplierName}, {PoNumber}, {CompanyName}, {OrderDate}, {ExpectedDeliveryDate}, {Total}.
    /// </summary>
    [JsonPropertyName("bodyTemplate")]
    public string BodyTemplate { get; set; } =
        "Hi {SupplierName},\n\nPlease find attached purchase order {PoNumber} for your reference. " +
        "We would appreciate delivery by {ExpectedDeliveryDate}.\n\n" +
        "Total: {Total}\n\nThanks,\n{CompanyName}";
}

/// <summary>
/// Request payload for sending a purchase order email via the API.
/// </summary>
public class PurchaseOrderEmailRequest
{
    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("toName")]
    public string ToName { get; set; } = string.Empty;

    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("fromName")]
    public string FromName { get; set; } = string.Empty;

    [JsonPropertyName("replyTo")]
    public string? ReplyTo { get; set; }

    [JsonPropertyName("cc")]
    public string? Cc { get; set; }

    [JsonPropertyName("bcc")]
    public string? Bcc { get; set; }

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("purchaseOrderId")]
    public string PurchaseOrderId { get; set; } = string.Empty;

    [JsonPropertyName("pdfAttachment")]
    public string? PdfAttachment { get; set; }

    [JsonPropertyName("pdfFilename")]
    public string? PdfFilename { get; set; }
}

/// <summary>
/// Response from the purchase order email API.
/// </summary>
public class PurchaseOrderEmailResponse : ArgoBooks.Core.Services.IEmailApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("messageId")]
    public string? MessageId { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
}
