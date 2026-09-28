using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Transactions;

/// <summary>Lifecycle of a recurring schedule.</summary>
public enum RecurringTransactionStatus
{
    Active,
    Paused,
    Completed
}

/// <summary>
/// A transaction that repeats on a schedule. The payload lives in <see cref="Template"/> so a
/// future field on Transaction needs no change here, matching how RecurringInvoice works.
/// </summary>
public class RecurringTransaction : IRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public CategoryType Type { get; set; } = CategoryType.Expense;

    [JsonPropertyName("frequency")]
    public Frequency Frequency { get; set; } = Frequency.Monthly;

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; set; }

    [JsonPropertyName("nextDate")]
    public DateTime NextDate { get; set; }

    [JsonPropertyName("lastGeneratedAt")]
    public DateTime? LastGeneratedAt { get; set; }

    [JsonPropertyName("status")]
    public RecurringTransactionStatus Status { get; set; } = RecurringTransactionStatus.Active;

    /// <summary>Occurrence dates the user chose to skip, or undid after generation.</summary>
    [JsonPropertyName("skippedDates")]
    public List<DateTime> SkippedDates { get; set; } = [];

    /// <summary>
    /// The payload is held as two concrete fields rather than one Transaction, because
    /// System.Text.Json cannot round-trip the abstract base without polymorphic attributes on
    /// every transaction in the file.
    /// </summary>
    [JsonPropertyName("expenseTemplate")]
    public Expense? ExpenseTemplate { get; set; }

    [JsonPropertyName("revenueTemplate")]
    public Revenue? RevenueTemplate { get; set; }

    [JsonIgnore]
    public Transaction? Template => Type == CategoryType.Revenue
        ? RevenueTemplate
        : ExpenseTemplate;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
