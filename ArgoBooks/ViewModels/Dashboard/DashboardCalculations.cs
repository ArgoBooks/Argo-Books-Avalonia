using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;

namespace ArgoBooks.ViewModels.Dashboard;

/// <summary>
/// Shared calculation helpers used by dashboard widgets and the main dashboard ViewModel.
/// </summary>
public static class DashboardCalculations
{
    public static (DateTime prevStart, DateTime prevEnd) GetComparisonPeriod()
    {
        var chartSettings = ChartSettingsService.Instance;
        return ComparisonPeriod.For(
            DateRangePresetExtensions.ParseDateRange(chartSettings.SelectedDateRange),
            chartSettings.StartDate, chartSettings.EndDate);
    }

    /// <summary>
    /// The Total Revenue figure (Rule 1, Rule 2, §8): collected revenue less refunds, each converted at
    /// its own date, or Pending. The dashboard card and the Revenue page card both show this.
    /// </summary>
    public static string FormatRevenue(CompanyData data, DateTime start, DateTime end) =>
        CurrencyService.FormatTotalOrPending(convert =>
            RevenueAggregator.SumCollectedRevenueDisplay(data.Revenues, start, end, convert)
            - RefundAggregator.GetRefundedInDateRangeDisplay(data.Payments, start, end, convert));

    /// <summary>
    /// The Total Expenses figure (§9): every expense, tax included, each converted at its own date, or
    /// Pending. The dashboard card and the Expenses page card both show this.
    /// </summary>
    public static string FormatExpenses(CompanyData data, DateTime start, DateTime end) =>
        CurrencyService.FormatTotalOrPending(convert =>
            ExpenseAggregator.SumExpensesDisplay(data.Expenses, start, end, convert));

    /// <summary>This month so far, as the dashboard's This Month preset has it.</summary>
    public static (DateTime Start, DateTime End) ThisMonth() => DatePresetNames.GetDateRange(DatePresetNames.ThisMonth);

    public static bool HasSufficientPriorData(CompanyData data, DateTime prevStartDate)
    {
        var earliestRevenue = data.Revenues.Count > 0 ? data.Revenues.Min(r => r.Date) : DateTime.MaxValue;
        var earliestExpense = data.Expenses.Count > 0 ? data.Expenses.Min(e => e.Date) : DateTime.MaxValue;
        var earliestDate = earliestRevenue < earliestExpense ? earliestRevenue : earliestExpense;
        return earliestDate != DateTime.MaxValue && earliestDate <= prevStartDate;
    }

    public static double? CalculatePercentageChange(decimal previous, decimal current)
    {
        if (previous == 0) return null;
        return (double)((current - previous) / previous * 100);
    }

    public static string? FormatPercentageChange(double? change)
    {
        if (!change.HasValue) return null;
        return $"{Math.Abs(change.Value):F1}%";
    }
}
