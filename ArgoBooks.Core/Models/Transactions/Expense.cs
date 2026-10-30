namespace ArgoBooks.Core.Models.Transactions;

/// <summary>
/// Represents an expense transaction.
/// </summary>
public class Expense : Transaction
{
    /// <summary>
    /// Supplier ID.
    /// </summary>
    [JsonPropertyName("supplierId")]
    public string? SupplierId { get; set; }

    /// <summary>
    /// When a bill, paid after it was received, is due. Null for an expense paid when it was
    /// recorded, which is every expense saved before bills existed.
    /// </summary>
    [JsonPropertyName("dueDate")]
    public DateTime? DueDate { get; set; }

    /// <summary>When a bill was paid, or null while it is still owed. Only a bill has one.</summary>
    [JsonPropertyName("paidDate")]
    public DateTime? PaidDate { get; set; }

    /// <summary>The bank line whose match marked this bill paid, so unmatching it makes the bill owed again.</summary>
    [JsonPropertyName("paidByBankLineId")]
    public string? PaidByBankLineId { get; set; }

    /// <summary>Whether this is a bill, paid after it was received rather than on the day.</summary>
    [JsonIgnore]
    public bool IsBill => DueDate != null;

    /// <summary>Whether the money has left the business: always for an expense, once paid for a bill.</summary>
    [JsonIgnore]
    public bool IsPaid => DueDate == null || PaidDate != null;

    /// <summary>The day the money left the business, or null while a bill is unpaid.</summary>
    [JsonIgnore]
    public DateTime? CashDate => DueDate == null ? Date : PaidDate;

    /// <summary>Whether this bill is still owed at the end of <paramref name="asOf"/>.</summary>
    public bool IsOwedOn(DateTime asOf) =>
        IsBill && Date.Date <= asOf.Date && (PaidDate == null || PaidDate.Value.Date > asOf.Date);

    /// <summary>Whether this bill is unpaid and past its due date on <paramref name="today"/>.</summary>
    public bool IsOverdueOn(DateTime today) =>
        !IsPaid && DueDate!.Value.Date < today.Date;
}
