using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Helpers for aggregating Expense rows in USD. Like revenue, the dashboard, Analytics and charts
/// count only money that has actually gone out: an expense is paid when it is recorded, and a bill
/// once it is marked paid (<see cref="Expense.IsPaid"/>). A paid bill counts on its own date, as
/// collected revenue does; only the cash figures of the formal reports use the day it was paid.
///
/// All expense aggregations use EffectiveTotalUSD (gross of any tax paid
/// to suppliers), that's the actual cash that left the business.
///
/// See docs/Calculations.md §9 for the standard.
/// </summary>
public static class ExpenseAggregator
{
    /// <summary>Whether the expense's money has gone out. Use this rather than reading the bill dates.</summary>
    public static bool IsPaid(Expense expense) => expense.IsPaid;

    /// <summary>Filters an Expense sequence to the paid ones.</summary>
    public static IEnumerable<Expense> OnlyPaid(IEnumerable<Expense> expenses) => expenses.Where(IsPaid);

    /// <summary>
    /// Sum gross USD expenses (EffectiveTotalUSD) inside a date range, paid ones only.
    /// </summary>
    public static decimal SumExpensesUSD(
        IEnumerable<Expense> expenses, DateTime start, DateTime end)
    {
        return expenses
            .Where(e => e.IsPaid && e.Date >= start && e.Date <= end)
            .Sum(e => e.EffectiveTotalUSD);
    }

    /// <summary>Whether a paid expense in the range is still waiting for its rate, which a display total counts as 0 and so must show as Pending.</summary>
    public static bool AnyWaiting(IEnumerable<Expense> expenses, DateTime start, DateTime end) =>
        expenses.Any(e => e.IsPendingConversion && e.IsPaid && e.Date >= start && e.Date <= end);

    /// <summary>
    /// Display-currency variant of <see cref="SumExpensesUSD"/>: converts each expense to the
    /// display currency at its OWN date via <paramref name="toDisplay"/> before summing, per the
    /// totals rule (docs/Calculations.md Rule 4). Pass <c>CurrencyService.GetDisplayAmount</c>.
    /// For a USD display currency this equals <see cref="SumExpensesUSD"/>.
    /// </summary>
    public static decimal SumExpensesDisplay(
        IEnumerable<Expense> expenses, DateTime start, DateTime end, Func<decimal, DateTime, decimal> toDisplay)
    {
        return expenses
            .Where(e => e.IsPaid && e.Date >= start && e.Date <= end)
            .Sum(e => toDisplay(e.EffectiveTotalUSD, e.Date));
    }

    /// <summary>
    /// The cash paid out for expenses between <paramref name="start"/> and <paramref name="end"/>,
    /// placed on the day it left (<see cref="Expense.CashDate"/>): a bill on the day it was paid.
    /// Each is converted at its own date, the rate its USD amount was stored at (Rule 4).
    /// </summary>
    public static decimal SumCashPaidDisplay(
        IEnumerable<Expense> expenses, DateTime start, DateTime end, Func<decimal, DateTime, decimal> toDisplay)
    {
        return expenses
            .Where(e => e.CashDate is { } paid && paid >= start && paid <= end)
            .Sum(e => toDisplay(e.EffectiveTotalUSD, e.Date));
    }

    /// <summary>Gross USD expenses by day, paid ones only, for time-series charts.</summary>
    public static Dictionary<DateTime, decimal> GroupExpensesByDayUSD(
        IEnumerable<Expense> expenses, DateTime start, DateTime end)
    {
        return expenses
            .Where(e => e.IsPaid && e.Date >= start && e.Date <= end)
            .GroupBy(e => e.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.EffectiveTotalUSD));
    }
}
