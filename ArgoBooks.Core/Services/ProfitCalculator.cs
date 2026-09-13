using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Single source of truth for net profit and net profit by day.
///
/// Profit = pre-tax revenue (collected) − total expenses − pre-tax refunds.
/// Sales tax we collect is owed to the government, so it isn't profit;
/// tax we paid suppliers is real cash out, so expenses stay gross. See
/// docs/Calculations.md §2 and §8 for the full rules.
/// </summary>
public static class ProfitCalculator
{
    /// <summary>
    /// Net profit USD inside a date range, paid-only, refunds adjusted for
    /// tax. Returns a plain decimal; callers format and apply currency
    /// conversion at the display layer.
    /// </summary>
    public static decimal CalculateNetProfitUSD(
        CompanyData data, DateTime start, DateTime end)
    {
        var revenuePreTax = RevenueAggregator.SumCollectedRevenuePreTaxUSD(
            data.Revenues, start, end);
        var expenses = ExpenseAggregator.SumExpensesUSD(data.Expenses, start, end);
        var refundsPreTax = RefundAggregator.GetRefundedPreTaxInDateRangeUSD(
            data.Payments, BuildInvoiceLookup(data.Invoices), start, end);
        return revenuePreTax - expenses - refundsPreTax;
    }

    /// <summary>
    /// Display-currency variant of <see cref="CalculateNetProfitUSD"/>: each component (revenue,
    /// expenses, refunds) is converted to the display currency at each transaction's OWN date via
    /// <paramref name="toDisplay"/> before the profit subtraction, per docs/Calculations.md §3a
    /// Phase 2. Pass <c>CurrencyService.GetDisplayAmount</c>. Equals the USD profit for USD display.
    /// </summary>
    public static decimal CalculateNetProfitDisplay(
        CompanyData data, DateTime start, DateTime end, Func<decimal, DateTime, decimal> toDisplay)
    {
        var revenuePreTax = RevenueAggregator.SumCollectedRevenuePreTaxDisplay(
            data.Revenues, start, end, toDisplay);
        var expenses = ExpenseAggregator.SumExpensesDisplay(data.Expenses, start, end, toDisplay);
        var refundsPreTax = RefundAggregator.GetRefundedPreTaxInDateRangeDisplay(
            data.Payments, BuildInvoiceLookup(data.Invoices), start, end, toDisplay);
        return revenuePreTax - expenses - refundsPreTax;
    }

    /// <summary>
    /// Profit per day inside the range, suitable for time-series charts.
    /// Each day's value is collected pre-tax revenue minus pre-tax refunds
    /// (both keyed by the day they happened) minus expenses on that day.
    /// </summary>
    public static Dictionary<DateTime, decimal> CalculateNetProfitByDayUSD(
        CompanyData data, DateTime start, DateTime end)
    {
        var revenueByDay = data.Revenues
            .Where(s => s.Date >= start && s.Date <= end)
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => s.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.EffectiveSubtotalUSD));

        var expensesByDay = data.Expenses
            .Where(p => p.Date >= start && p.Date <= end)
            .GroupBy(p => p.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.EffectiveTotalUSD));

        var refundsByDay = BuildPreTaxRefundsByDay(data, start, end);

        var allDays = revenueByDay.Keys
            .Union(expensesByDay.Keys)
            .Union(refundsByDay.Keys);

        return allDays.ToDictionary(
            day => day,
            day =>
                revenueByDay.GetValueOrDefault(day, 0m)
                - refundsByDay.GetValueOrDefault(day, 0m)
                - expensesByDay.GetValueOrDefault(day, 0m));
    }

    internal static Dictionary<string, Invoice> BuildInvoiceLookup(IEnumerable<Invoice> invoices)
    {
        var dict = new Dictionary<string, Invoice>();
        foreach (var inv in invoices)
        {
            if (!string.IsNullOrEmpty(inv.Id))
                dict[inv.Id] = inv;
        }
        return dict;
    }

    private static Dictionary<DateTime, decimal> BuildPreTaxRefundsByDay(
        CompanyData data, DateTime start, DateTime end)
    {
        if (data.Payments == null) return new Dictionary<DateTime, decimal>();

        var invoicesById = BuildInvoiceLookup(data.Invoices);
        var byDay = new Dictionary<DateTime, decimal>();

        foreach (var p in data.Payments.Where(x => x.IsRefund && x.Date >= start && x.Date <= end))
        {
            var refundTotalUSD = Math.Abs(p.EffectiveAmountUSD) * p.RevenueShare;
            decimal preTax;
            if (!string.IsNullOrEmpty(p.InvoiceId)
                && invoicesById.TryGetValue(p.InvoiceId, out var invoice)
                && invoice.Total > 0)
            {
                preTax = refundTotalUSD * RefundAggregator.PreTaxShare(invoice);
            }
            else
            {
                preTax = refundTotalUSD;
            }
            var day = p.Date.Date;
            byDay[day] = byDay.GetValueOrDefault(day, 0m) + preTax;
        }
        return byDay;
    }
}
