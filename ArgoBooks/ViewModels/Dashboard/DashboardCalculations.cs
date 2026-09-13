using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
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
