using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Core.Models.Transactions;

/// <summary>
/// Represents an invoice.
/// </summary>
/// <remarks>
/// Inherits from <see cref="ObservableObject"/> so that fields written by
/// <see cref="Services.InvoiceTotalsService.Recalculate"/> during portal sync
/// (Status, AmountPaid, AmountRefunded, Balance, BalanceUSD) raise
/// PropertyChanged and refresh any bound UI immediately. The remaining
/// properties are static after creation and remain plain auto-properties.
/// </remarks>
public partial class Invoice : ObservableObject
{
    /// <summary>
    /// Unique identifier (e.g., INV-2024-00001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display invoice number (e.g., #INV-2024-001).
    /// </summary>
    [JsonPropertyName("invoiceNumber")]
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>
    /// Customer ID.
    /// </summary>
    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Date the invoice was issued.
    /// </summary>
    [JsonPropertyName("issueDate")]
    public DateTime IssueDate { get; set; }

    /// <summary>
    /// Payment due date.
    /// </summary>
    [JsonPropertyName("dueDate")]
    public DateTime DueDate { get; set; }

    /// <summary>
    /// Line items on the invoice.
    /// </summary>
    [JsonPropertyName("lineItems")]
    public List<LineItem> LineItems { get; set; } = [];

    /// <summary>
    /// Subtotal before tax.
    /// </summary>
    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Tax rate as a decimal.
    /// </summary>
    [JsonPropertyName("taxRate")]
    public decimal TaxRate { get; set; }

    /// <summary>
    /// Total tax amount.
    /// </summary>
    [JsonPropertyName("taxAmount")]
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// Security deposit amount.
    /// </summary>
    [JsonPropertyName("securityDeposit")]
    public decimal SecurityDeposit { get; set; }

    /// <summary>
    /// Custom fee label (e.g., "Setup Fee", "Rush Delivery").
    /// </summary>
    [JsonPropertyName("customFeeLabel")]
    public string CustomFeeLabel { get; set; } = string.Empty;

    /// <summary>
    /// Custom fee value (flat $ or %).
    /// </summary>
    [JsonPropertyName("customFeeAmount")]
    public decimal CustomFeeAmount { get; set; }

    /// <summary>
    /// Whether the custom fee is a percentage of subtotal.
    /// </summary>
    [JsonPropertyName("customFeeIsPercent")]
    public bool CustomFeeIsPercent { get; set; }

    /// <summary>
    /// Discount value (flat $ or %).
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
    /// Whether the tax value is a flat amount rather than a percentage of the taxable base.
    /// </summary>
    [JsonPropertyName("taxIsFixed")]
    public bool TaxIsFixed { get; set; }

    /// <summary>Id of the template this invoice was created with, so it can be re-rendered (e.g. the
    /// read-only viewer) with the layout the customer received. Empty for older invoices, which fall
    /// back to the default template.</summary>
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = string.Empty;

    // Per-invoice display overrides. Null = inherit the selected template's setting; a value
    // overrides it for this invoice only, so the invoice's appearance is frozen at creation and
    // isn't changed by later template edits.

    /// <summary>Override: pass the card processing fee to the customer on online payments.</summary>
    [JsonPropertyName("passProcessingFee")]
    public bool? PassProcessingFee { get; set; }

    /// <summary>Override: show the company address block.</summary>
    [JsonPropertyName("showCompanyAddress")]
    public bool? ShowCompanyAddress { get; set; }

    /// <summary>Override: show the company phone number.</summary>
    [JsonPropertyName("showCompanyPhone")]
    public bool? ShowCompanyPhone { get; set; }

    /// <summary>Override: show the prominent "due date" badge.</summary>
    [JsonPropertyName("showDueDateProminent")]
    public bool? ShowDueDateProminent { get; set; }

    /// <summary>
    /// Total amount due.
    /// </summary>
    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    /// <summary>
    /// Gross amount paid by the customer (sum of positive Payment rows for this invoice).
    /// Refunds do NOT reduce this, see <see cref="AmountRefunded"/> and <see cref="NetPaid"/>.
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("amountPaid")]
    [NotifyPropertyChangedFor(nameof(NetPaid))]
    private decimal _amountPaid;

    /// <summary>
    /// Amount returned to the customer via refunds (absolute value, always &gt;= 0).
    /// Sum of |Amount| over Payment rows where IsRefund is true for this invoice.
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("amountRefunded")]
    [NotifyPropertyChangedFor(nameof(NetPaid))]
    private decimal _amountRefunded;

    /// <summary>
    /// Remaining balance the customer still owes. Computed as Math.Max(0, Total - AmountPaid).
    /// Refunds do NOT raise the balance, once a customer paid, they don't owe again just
    /// because we returned money to them.
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("balance")]
    [NotifyPropertyChangedFor(nameof(EffectiveBalanceUSD))]
    private decimal _balance;

    /// <summary>
    /// Net amount kept after refunds. Used for revenue/profit aggregations.
    /// </summary>
    [JsonIgnore]
    public decimal NetPaid => AmountPaid - AmountRefunded;

    /// <summary>
    /// Invoice status.
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("status")]
    [NotifyPropertyChangedFor(nameof(IsOverdue))]
    private InvoiceStatus _status = InvoiceStatus.Draft;

    /// <summary>
    /// The lifecycle status (Draft, Sent, ...) the invoice had when a payment moved it to a payment
    /// status, so it can go back there if every payment is removed. Null while no payment has.
    /// </summary>
    [JsonPropertyName("statusBeforePayment")]
    public InvoiceStatus? StatusBeforePayment { get; set; }

    /// <summary>
    /// Additional notes.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Recurring invoice ID if generated from recurring.
    /// </summary>
    [JsonPropertyName("recurringInvoiceId")]
    public string? RecurringInvoiceId { get; set; }

    /// <summary>
    /// ID of the accountant who created or manages this invoice.
    /// </summary>
    [JsonPropertyName("accountantId")]
    public string? AccountantId { get; set; }

    /// <summary>
    /// Payment reminder settings.
    /// </summary>
    [JsonPropertyName("reminderSettings")]
    public ReminderSettings ReminderSettings { get; set; } = new();

    /// <summary>
    /// Invoice history (actions taken).
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
    /// Whether the invoice is past due with something still owed. One paid in full and then
    /// refunded, wholly or in part, owes nothing.
    /// </summary>
    [JsonIgnore]
    public bool IsOverdue => Status != InvoiceStatus.Paid &&
                             Status != InvoiceStatus.Cancelled &&
                             Status != InvoiceStatus.Refunded &&
                             !(AmountPaid > 0 && Balance <= 0) &&
                             DateTime.Today > DueDate.Date;

    #region Currency Support

    /// <summary>
    /// The ISO currency code in which this invoice was originally created (e.g., "USD", "EUR", "CAD").
    /// Defaults to "USD" for backward compatibility with existing data.
    /// </summary>
    [JsonPropertyName("originalCurrency")]
    public string OriginalCurrency { get; set; } = "USD";

    /// <summary>
    /// The total amount converted to USD at the time of creation.
    /// </summary>
    [JsonPropertyName("totalUSD")]
    public decimal TotalUSD { get; set; }

    /// <summary>
    /// The balance converted to USD.
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("balanceUSD")]
    [NotifyPropertyChangedFor(nameof(EffectiveBalanceUSD))]
    private decimal _balanceUSD;

    /// <summary>
    /// Whether this invoice's original currency is USD (including legacy data which defaults to USD).
    /// </summary>
    [JsonIgnore]
    public bool IsUSD => string.Equals(OriginalCurrency, "USD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this invoice was saved offline and is awaiting USD conversion.
    /// When true, all Effective*USD properties return 0 to prevent wrong cross-currency aggregation.
    /// </summary>
    [JsonPropertyName("isPendingConversion")]
    public bool IsPendingConversion { get; set; }

    /// <summary>
    /// Gets the effective total in USD. For USD invoices (including legacy data), returns Total directly.
    /// For non-USD invoices, returns the converted TotalUSD value.
    /// Returns 0 for pending-conversion invoices.
    /// </summary>
    [JsonIgnore]
    public decimal EffectiveTotalUSD => IsPendingConversion ? 0 : (IsUSD ? Total : TotalUSD);

    /// <summary>
    /// Gets the effective balance in USD. For USD invoices (including legacy data), returns Balance directly.
    /// For non-USD invoices, returns the converted BalanceUSD value.
    /// Returns 0 for pending-conversion invoices.
    /// </summary>
    [JsonIgnore]
    public decimal EffectiveBalanceUSD => IsPendingConversion ? 0 : (IsUSD ? Balance : BalanceUSD);

    #endregion

    #region Bank Matching

    /// <summary>
    /// Whether this invoice has been matched to a line on an imported bank statement.
    /// Defaults to false for backward compatibility with existing files.
    /// </summary>
    [JsonPropertyName("bankMatched")]
    public bool BankMatched { get; set; }

    /// <summary>
    /// When this invoice was matched to a bank statement line, if matched.
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
