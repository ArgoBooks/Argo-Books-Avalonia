using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Transactions;

/// <summary>
/// Represents a revenue transaction.
/// </summary>
public class Revenue : Transaction
{
    /// <summary>
    /// Customer ID.
    /// </summary>
    [JsonPropertyName("customerId")]
    public string? CustomerId { get; set; }

    /// <summary>
    /// Subtotal before tax.
    /// </summary>
    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Collection state: Paid / Complete = fully collected; everything else
    /// is excluded from cash-basis dashboard aggregations. See
    /// docs/Calculations.md §7.
    /// </summary>
    [JsonPropertyName("paymentStatus")]
    public RevenuePaymentStatus PaymentStatus { get; set; } = RevenuePaymentStatus.Paid;

    /// <summary>
    /// Associated invoice ID, if this revenue is linked to an invoice.
    /// </summary>
    [JsonPropertyName("invoiceId")]
    public string? InvoiceId { get; set; }

    /// <summary>
    /// A security deposit the business kept when a rental came back, which makes it revenue
    /// (docs/Calculations.md §4). Linked to the invoice that collected it.
    /// </summary>
    [JsonPropertyName("isKeptDeposit")]
    public bool IsKeptDeposit { get; set; }
}
