using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Cost of goods sold, and expenses with tracked stock purchases taken out. Profit subtracts both
/// in place of all expenses. See docs/Calculations.md §14.
/// </summary>
public static class CostOfGoodsAggregator
{
    /// <summary>What the tracked stock on this sale cost, in USD, as saved on its lines.</summary>
    public static decimal CostOfGoodsSoldUSD(Transaction transaction) =>
        transaction.IsPendingConversion ? 0 : transaction.LineItems.Sum(li => li.CostOfGoodsUSD ?? 0);

    /// <summary>
    /// The part of an expense that bought tracked stock, in USD: those lines' pre-tax amounts at the
    /// expense's own rate. Tax, shipping and fees on the expense stay expenses.
    /// </summary>
    public static decimal StockPurchaseUSD(Expense expense)
    {
        if (expense.IsPendingConversion || expense.Total == 0)
            return 0;

        var stockNative = expense.LineItems.Where(li => li.IsStockPurchase).Sum(li => li.Subtotal);
        if (stockNative == 0)
            return 0;

        var stockUSD = stockNative * (expense.EffectiveTotalUSD / expense.Total);
        return Math.Min(stockUSD, expense.EffectiveTotalUSD);
    }

    public static decimal OperatingExpenseUSD(Expense expense) =>
        expense.EffectiveTotalUSD - StockPurchaseUSD(expense);

    public static decimal SumOperatingExpensesUSD(IEnumerable<Expense> expenses, DateTime start, DateTime end) =>
        expenses
            .Where(e => e.Date >= start && e.Date <= end)
            .Sum(OperatingExpenseUSD);

    public static decimal SumOperatingExpensesDisplay(
        IEnumerable<Expense> expenses, DateTime start, DateTime end, Func<decimal, DateTime, decimal> toDisplay) =>
        expenses
            .Where(e => e.Date >= start && e.Date <= end)
            .Sum(e => toDisplay(OperatingExpenseUSD(e), e.Date));

    /// <summary>
    /// Cost of goods sold on sales dated in the range. <paramref name="collectedOnly"/> matches the
    /// revenue it is set against: paid-only on the dashboard, every sale on formal reports.
    /// </summary>
    public static decimal SumCostOfGoodsSoldUSD(
        IEnumerable<Revenue> revenues, DateTime start, DateTime end, bool collectedOnly) =>
        InRange(revenues, start, end, collectedOnly).Sum(CostOfGoodsSoldUSD);

    public static decimal SumCostOfGoodsSoldDisplay(
        IEnumerable<Revenue> revenues, DateTime start, DateTime end, bool collectedOnly,
        Func<decimal, DateTime, decimal> toDisplay) =>
        InRange(revenues, start, end, collectedOnly).Sum(r => toDisplay(CostOfGoodsSoldUSD(r), r.Date));

    private static IEnumerable<Revenue> InRange(
        IEnumerable<Revenue> revenues, DateTime start, DateTime end, bool collectedOnly)
    {
        var inRange = revenues.Where(r => r.Date >= start && r.Date <= end);
        return collectedOnly ? inRange.Where(RevenueAggregator.IsCollected) : inRange;
    }
}
