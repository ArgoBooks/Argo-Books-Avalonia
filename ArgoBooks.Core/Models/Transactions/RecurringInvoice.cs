using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Transactions;

/// <summary>
/// Represents a recurring invoice schedule.
/// </summary>
public class RecurringInvoice
{
    /// <summary>
    /// Unique identifier (e.g., REC-INV-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Customer ID.
    /// </summary>
    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Invoice amount.
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Description of recurring charge.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// How often to generate invoices.
    /// </summary>
    [JsonPropertyName("frequency")]
    public Frequency Frequency { get; set; }

    /// <summary>
    /// When the recurring schedule starts.
    /// </summary>
    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; set; }

    /// <summary>
    /// When the recurring schedule ends (null for indefinite).
    /// </summary>
    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Date of next invoice generation.
    /// </summary>
    [JsonPropertyName("nextInvoiceDate")]
    public DateTime NextInvoiceDate { get; set; }

    /// <summary>
    /// Payment terms (e.g., Net 30).
    /// </summary>
    [JsonPropertyName("paymentTerms")]
    public string PaymentTerms { get; set; } = "Net 30";

    /// <summary>
    /// Status of the recurring invoice.
    /// </summary>
    [JsonPropertyName("status")]
    public RecurringInvoiceStatus Status { get; set; } = RecurringInvoiceStatus.Active;

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

    /// <summary>
    /// When an invoice was last generated.
    /// </summary>
    [JsonPropertyName("lastGeneratedAt")]
    public DateTime? LastGeneratedAt { get; set; }

    /// <summary>
    /// Full invoice content (customer, line items, tax, discount, currency) cloned on each
    /// generation so regenerated invoices match what the user designed in the invoice modal.
    /// Null for legacy schedules that only carry <see cref="Amount"/>/<see cref="Description"/>;
    /// those are skipped by the generator.
    /// </summary>
    [JsonPropertyName("template")]
    public Invoice? Template { get; set; }

    /// <summary>
    /// Whether the recurring schedule is active.
    /// </summary>
    [JsonIgnore]
    public bool IsActive => Status == RecurringInvoiceStatus.Active &&
                           (EndDate == null || DateTime.UtcNow.Date <= EndDate.Value.Date);
}
