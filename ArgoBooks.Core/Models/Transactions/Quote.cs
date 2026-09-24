using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Core.Models.Transactions;

/// <summary>
/// A price quote (estimate) offered to a customer.
/// </summary>
/// <remarks>
/// Quotes are their own records, never invoices with a flag, so nothing a quote holds can reach
/// revenue, amounts owed, reminders or reports. It carries only the money fields the shared
/// invoice templates and <see cref="Services.InvoiceMath"/> need, plus its own answer tracking:
/// no payments, balance, recurring schedule, bank matching or USD conversion.
/// </remarks>
public partial class Quote : ObservableObject
{
    /// <summary>
    /// Unique identifier (e.g., QUO-2026-00001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display quote number (e.g., #QUO-2026-00001).
    /// </summary>
    [JsonPropertyName("quoteNumber")]
    public string QuoteNumber { get; set; } = string.Empty;

    /// <summary>
    /// Customer ID.
    /// </summary>
    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Date the quote was issued.
    /// </summary>
    [JsonPropertyName("issueDate")]
    public DateTime IssueDate { get; set; }

    /// <summary>
    /// Date the quoted price stops being offered.
    /// </summary>
    [JsonPropertyName("validUntil")]
    public DateTime ValidUntil { get; set; }

    /// <summary>
    /// Line items on the quote.
    /// </summary>
    [JsonPropertyName("lineItems")]
    public List<LineItem> LineItems { get; set; } = [];

    /// <summary>
    /// Subtotal before tax.
    /// </summary>
    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Tax rate as a percentage, or a flat amount when <see cref="TaxIsFixed"/> is set.
    /// </summary>
    [JsonPropertyName("taxRate")]
    public decimal TaxRate { get; set; }

    /// <summary>
    /// Total tax amount.
    /// </summary>
    [JsonPropertyName("taxAmount")]
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// Whether the tax value is a flat amount rather than a percentage of the taxable base.
    /// </summary>
    [JsonPropertyName("taxIsFixed")]
    public bool TaxIsFixed { get; set; }

    /// <summary>
    /// Custom fee label (e.g., "Setup Fee", "Rush Delivery").
    /// </summary>
    [JsonPropertyName("customFeeLabel")]
    public string CustomFeeLabel { get; set; } = string.Empty;

    /// <summary>
    /// Custom fee value (flat or %).
    /// </summary>
    [JsonPropertyName("customFeeAmount")]
    public decimal CustomFeeAmount { get; set; }

    /// <summary>
    /// Whether the custom fee is a percentage of subtotal.
    /// </summary>
    [JsonPropertyName("customFeeIsPercent")]
    public bool CustomFeeIsPercent { get; set; }

    /// <summary>
    /// Discount value (flat or %).
    /// </summary>
    [JsonPropertyName("discountAmount")]
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Whether the discount is a percentage of subtotal.
    /// </summary>
    [JsonPropertyName("discountIsPercent")]
    public bool DiscountIsPercent { get; set; }

    /// <summary>
    /// Flat shipping amount added to the taxable base.
    /// </summary>
    [JsonPropertyName("shippingAmount")]
    public decimal ShippingAmount { get; set; }

    /// <summary>
    /// Total quoted amount.
    /// </summary>
    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    /// <summary>
    /// Id of the template this quote was created with, so it re-renders with the layout the
    /// customer received. Empty for quotes whose template has since been deleted.
    /// </summary>
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>The logo this went out with, set when the company logo is replaced after it was
    /// sent. Null means it follows whatever the template carries.</summary>
    [JsonPropertyName("logoId")]
    public string? LogoId { get; set; }

    /// <summary>
    /// Additional notes, printed in the document footer.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// The ISO currency code the quote was priced in (e.g., "USD", "EUR", "CAD").
    /// </summary>
    [JsonPropertyName("originalCurrency")]
    public string OriginalCurrency { get; set; } = "USD";

    /// <summary>
    /// Quote status. Observable so a portal sync that flips it to Accepted refreshes the list.
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("status")]
    private QuoteStatus _status = QuoteStatus.Draft;

    /// <summary>
    /// When the quote was last sent to the customer.
    /// </summary>
    [JsonPropertyName("sentAt")]
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// When the customer accepted or declined.
    /// </summary>
    [JsonPropertyName("respondedAt")]
    public DateTime? RespondedAt { get; set; }

    /// <summary>
    /// The optional reason the customer gave with their answer.
    /// </summary>
    [JsonPropertyName("responseNote")]
    public string? ResponseNote { get; set; }

    /// <summary>
    /// The draft invoice this quote became, if it was converted.
    /// </summary>
    [JsonPropertyName("convertedInvoiceId")]
    public string? ConvertedInvoiceId { get; set; }

    /// <summary>
    /// Quote history (actions taken).
    /// </summary>
    [JsonPropertyName("history")]
    public List<InvoiceHistoryEntry> History { get; set; } = [];

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

    /// <summary>
    /// A sent quote the customer can no longer answer because its valid-until date has passed.
    /// Never stored, so it cannot disagree with the date it is derived from.
    /// </summary>
    [JsonIgnore]
    public bool IsExpired => Status == QuoteStatus.Sent && ValidUntil.Date < DateTime.Today;

    /// <summary>
    /// Whether the quote has been published to the portal, i.e. there is a customer-facing link
    /// that would keep accepting answers unless it is cancelled.
    /// </summary>
    [JsonIgnore]
    public bool HasBeenPublished => SentAt.HasValue;

    /// <summary>
    /// The quote as an <see cref="Invoice"/> for the shared template renderer, which takes one.
    /// Throwaway and never stored: it carries no id the books could link to, and the payment
    /// fields stay at zero so the amount-to-pay block has nothing to print even if it were shown.
    /// </summary>
    public Invoice ToRenderableInvoice() => new()
    {
        Id = Id,
        InvoiceNumber = QuoteNumber,
        CustomerId = CustomerId,
        IssueDate = IssueDate,
        DueDate = ValidUntil,
        LineItems = LineItems,
        Subtotal = Subtotal,
        TaxRate = TaxRate,
        TaxAmount = TaxAmount,
        TaxIsFixed = TaxIsFixed,
        CustomFeeLabel = CustomFeeLabel,
        CustomFeeAmount = CustomFeeAmount,
        CustomFeeIsPercent = CustomFeeIsPercent,
        DiscountAmount = DiscountAmount,
        DiscountIsPercent = DiscountIsPercent,
        ShippingAmount = ShippingAmount,
        Total = Total,
        TemplateId = TemplateId,
        LogoId = LogoId,
        Notes = Notes,
        OriginalCurrency = OriginalCurrency,
        PassProcessingFee = false
    };
}
