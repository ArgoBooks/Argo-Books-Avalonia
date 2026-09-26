using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Rentals;

/// <summary>
/// Represents a rental record/transaction.
/// </summary>
public class RentalRecord : IRecord
{
    /// <summary>
    /// Unique identifier (e.g., RNT-001).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Rental item ID.
    /// </summary>
    [JsonPropertyName("rentalItemId")]
    public string RentalItemId { get; set; } = string.Empty;

    /// <summary>
    /// Customer ID.
    /// </summary>
    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Accountant ID who processed the rental.
    /// </summary>
    [JsonPropertyName("accountantId")]
    public string? AccountantId { get; set; }

    /// <summary>
    /// Quantity rented.
    /// </summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    /// <summary>
    /// Rate type (Daily, Weekly, Monthly).
    /// </summary>
    [JsonPropertyName("rateType")]
    public RateType RateType { get; set; }

    /// <summary>
    /// Rate amount being charged.
    /// </summary>
    [JsonPropertyName("rateAmount")]
    public decimal RateAmount { get; set; }

    /// <summary>
    /// Security deposit collected.
    /// </summary>
    [JsonPropertyName("securityDeposit")]
    public decimal SecurityDeposit { get; set; }

    /// <summary>
    /// Rental start date.
    /// </summary>
    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Due date for return.
    /// </summary>
    [JsonPropertyName("dueDate")]
    public DateTime DueDate { get; set; }

    /// <summary>
    /// Actual return date.
    /// </summary>
    [JsonPropertyName("returnDate")]
    public DateTime? ReturnDate { get; set; }

    /// <summary>
    /// Rental status.
    /// </summary>
    [JsonPropertyName("status")]
    public RentalStatus Status { get; set; } = RentalStatus.Active;

    /// <summary>
    /// Total cost (calculated at return).
    /// </summary>
    [JsonPropertyName("totalCost")]
    public decimal? TotalCost { get; set; }

    /// <summary>
    /// Amount of deposit refunded.
    /// </summary>
    [JsonPropertyName("depositRefunded")]
    public decimal? DepositRefunded { get; set; }

    /// <summary>
    /// Whether the rental has been paid.
    /// </summary>
    [JsonPropertyName("paid")]
    public bool Paid { get; set; }

    /// <summary>
    /// Late fees or damage billed on top of the rental when it came back. Included in <see cref="TotalCost"/>.
    /// </summary>
    [JsonPropertyName("extraCharges")]
    public decimal ExtraCharges { get; set; }

    /// <summary>
    /// What the extra charges were for.
    /// </summary>
    [JsonPropertyName("extraChargesNote")]
    public string ExtraChargesNote { get; set; } = string.Empty;

    /// <summary>
    /// The revenue recorded when the rental was marked paid without an invoice.
    /// </summary>
    [JsonPropertyName("revenueId")]
    public string? RevenueId { get; set; }

    /// <summary>
    /// Line items for multi-item rentals.
    /// When populated, these take precedence over the top-level RentalItemId/Quantity/RateType/RateAmount/SecurityDeposit fields.
    /// </summary>
    [JsonPropertyName("lineItems")]
    public List<RentalLineItem> LineItems { get; set; } = [];

    /// <summary>
    /// Additional notes.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// IDs of invoices generated for this rental.
    /// </summary>
    [JsonPropertyName("invoiceIds")]
    public List<string> InvoiceIds { get; set; } = [];

    /// <summary>
    /// Whether this rental has any associated invoices.
    /// </summary>
    [JsonIgnore]
    public bool HasInvoices => InvoiceIds.Count > 0;

    /// <summary>
    /// The rental's lines. A record saved by the old Rent Out action has none and keeps its item,
    /// quantity and total deposit on the record itself, so one line is built from those. A line's
    /// deposit is per unit.
    /// </summary>
    public List<RentalLineItem> EffectiveLineItems()
    {
        if (LineItems.Count > 0 || string.IsNullOrEmpty(RentalItemId))
            return LineItems;

        return
        [
            new RentalLineItem
            {
                RentalItemId = RentalItemId,
                Quantity = Quantity,
                RateType = RateType,
                RateAmount = RateAmount,
                SecurityDeposit = Quantity > 0 ? SecurityDeposit / Quantity : SecurityDeposit
            }
        ];
    }

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
    /// Whether the rental is overdue.
    /// </summary>
    [JsonIgnore]
    public bool IsOverdue => Status == RentalStatus.Active && DateTime.Today > DueDate.Date;

    /// <summary>
    /// Number of days overdue (0 if not overdue).
    /// </summary>
    [JsonIgnore]
    public int DaysOverdue => IsOverdue ? (int)(DateTime.Today - DueDate.Date).TotalDays : 0;

    /// <summary>
    /// Days a still-out rental is past its due date for DISPLAY. Unlike <see cref="DaysOverdue"/>
    /// (which counts only Active records), this also counts records already flagged
    /// <see cref="RentalStatus.Overdue"/> - the page marks a late rental Overdue before reading this,
    /// which would otherwise make the day count read 0. Zero once the rental is returned/cancelled.
    /// </summary>
    [JsonIgnore]
    public int EffectiveDaysOverdue =>
        (Status == RentalStatus.Active || Status == RentalStatus.Overdue) && DateTime.Today > DueDate.Date
            ? (int)(DateTime.Today - DueDate.Date).TotalDays
            : 0;
}
