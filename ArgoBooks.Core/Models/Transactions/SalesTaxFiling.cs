using System.Text.Json.Serialization;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Models.Transactions;

/// <summary>
/// A sales tax return filed for one period, and the payment made with it (docs/Calculations.md §17). The
/// payment comes off sales tax payable and out of cash, and is never an expense, since the tax was never
/// the business's money. A negative <see cref="Amount"/> is a refund the government paid back.
/// </summary>
public class SalesTaxFiling : IRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("frequency")]
    public SalesTaxFrequency Frequency { get; set; }

    [JsonPropertyName("periodStart")]
    public DateTime PeriodStart { get; set; }

    [JsonPropertyName("periodEnd")]
    public DateTime PeriodEnd { get; set; }

    [JsonPropertyName("dueDate")]
    public DateTime DueDate { get; set; }

    /// <summary>The day the payment was made, or the refund received.</summary>
    [JsonPropertyName("paidDate")]
    public DateTime PaidDate { get; set; }

    /// <summary>The currency the amounts below are in, the company's when it was filed.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    /// <summary>Tax collected on the period's sales, as the return reported it.</summary>
    [JsonPropertyName("collected")]
    public decimal Collected { get; set; }

    /// <summary>Tax handed back on the period's refunds.</summary>
    [JsonPropertyName("refunded")]
    public decimal Refunded { get; set; }

    /// <summary>Tax on the period's purchases claimed back.</summary>
    [JsonPropertyName("claimedBack")]
    public decimal ClaimedBack { get; set; }

    [JsonPropertyName("paymentMethod")]
    public string PaymentMethod { get; set; } = string.Empty;

    [JsonPropertyName("confirmationNumber")]
    public string ConfirmationNumber { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether a bank statement line has been matched to this payment.</summary>
    [JsonPropertyName("bankMatched")]
    public bool BankMatched { get; set; }

    [JsonPropertyName("bankMatchedDate")]
    public DateTime? BankMatchedDate { get; set; }

    [JsonPropertyName("bankMatchedLineId")]
    public string? BankMatchedLineId { get; set; }

    /// <summary>What was paid with the return, or received back when below zero.</summary>
    [JsonIgnore]
    public decimal Amount => Collected - Refunded - ClaimedBack;
}
