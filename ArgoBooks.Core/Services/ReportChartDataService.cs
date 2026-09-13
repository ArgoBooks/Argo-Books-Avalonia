using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Charts;
using ArgoBooks.Core.Models.Reports;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for generating chart data for reports.
/// </summary>
public class ReportChartDataService(CompanyData? companyData, ReportFilters filters)
{
    private readonly Dictionary<ChartDataType, object> _cache = new();

    /// <summary>
    /// Gets the date range based on filters (delegates to shared ReportFilters.GetDateRange).
    /// </summary>
    private (DateTime Start, DateTime End) GetDateRange() => filters.GetDateRange(companyData?.GetEarliestDate());

    #region Revenue Charts

    /// <summary>
    /// Gets revenue over time data (in USD for consistent calculations).
    /// </summary>
    public List<ChartDataPoint> GetRevenueOverTime()
    {
        if (companyData?.Revenues == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var grossByDay = companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate)
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => s.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.EffectiveTotalUSD));

        // Subtract refund Payments cash-basis: a refund reduces revenue on the
        // date the refund was issued. Same-day refund nets to 0; multi-day
        // leaves the original day's revenue intact and shows a negative on
        // the refund's day.
        foreach (var (day, refunded) in RefundAggregator.GroupRefundsByDayUSD(
                     companyData.Payments, startDate, endDate))
        {
            grossByDay[day] = (grossByDay.TryGetValue(day, out var v) ? v : 0m) - refunded;
        }

        return grossByDay
            .OrderBy(kv => kv.Key)
            .Select(kv => new ChartDataPoint
            {
                Label = kv.Key.ToString("MMM dd"),
                Value = (double)kv.Value,
                Date = kv.Key
            })
            .ToList();
    }

    /// <summary>
    /// Gets revenue distribution by category (in USD).
    /// Allocates multi-line-item transactions proportionally across categories.
    /// </summary>
    public List<ChartDataPoint> GetRevenueDistribution(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData?.Revenues == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var categoryTotals = new Dictionary<string, decimal>();

        foreach (var s in companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate)
            .Where(RevenueAggregator.IsCollected))
        {
            // When a display converter is supplied, convert each transaction's USD amount at the
            // transaction's OWN date before grouping (Calculations.md §3a Phase 2) so the category
            // totals aren't re-priced at one date. Default (null) keeps USD for the formal report
            // path (a documented exception) and unit tests.
            decimal Display(decimal amountUSD) => toDisplay != null ? toDisplay(amountUSD, s.Date) : amountUSD;

            if (s.LineItems.Count > 0)
            {
                var lineItemsTotal = s.LineItems.Sum(li => li.Subtotal);
                var subtotalUSD = s.EffectiveTotalUSD;

                if (lineItemsTotal != 0)
                {
                    foreach (var li in s.LineItems)
                    {
                        var product = li.ProductId != null ? companyData.GetProduct(li.ProductId) : null;
                        var categoryName = product?.CategoryId != null
                            ? companyData.GetCategory(product.CategoryId)?.Name ?? "Other"
                            : "Other";
                        categoryTotals.TryAdd(categoryName, 0);
                        categoryTotals[categoryName] += Display(li.Subtotal / lineItemsTotal * subtotalUSD);
                    }
                }
                else
                {
                    // Line items sum to 0, allocate full transaction amount to "Other"
                    categoryTotals.TryAdd("Other", 0);
                    categoryTotals["Other"] += Display(subtotalUSD);
                }
            }
            else
            {
                categoryTotals.TryAdd("Other", 0);
                categoryTotals["Other"] += Display(s.EffectiveTotalUSD);
            }
        }

        return categoryTotals
            .Select(kvp => new ChartDataPoint { Label = kvp.Key, Value = (double)kvp.Value })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets total revenue value (in USD).
    /// </summary>
    public decimal GetTotalRevenue()
    {
        if (companyData?.Revenues == null)
            return 0;

        var (startDate, endDate) = GetDateRange();
        return RevenueAggregator.SumCollectedRevenueUSD(companyData.Revenues, startDate, endDate);
    }

    #endregion

    #region Expense Charts

    /// <summary>
    /// Gets expenses over time data (in USD for consistent calculations).
    /// </summary>
    public List<ChartDataPoint> GetExpensesOverTime()
    {
        if (companyData?.Expenses == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.Expenses
            .Where(p => p.Date >= startDate && p.Date <= endDate)
            .GroupBy(p => p.Date.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ChartDataPoint
            {
                Label = g.Key.ToString("MMM dd"),
                Value = (double)g.Sum(p => p.EffectiveTotalUSD),
                Date = g.Key
            })
            .ToList();
    }

    /// <summary>
    /// Gets expense distribution by category (in USD).
    /// Allocates multi-line-item transactions proportionally across categories.
    /// </summary>
    public List<ChartDataPoint> GetExpenseDistribution(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData?.Expenses == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var categoryTotals = new Dictionary<string, decimal>();

        foreach (var p in companyData.Expenses.Where(p => p.Date >= startDate && p.Date <= endDate))
        {
            // When a display converter is supplied, convert each transaction's USD amount at the
            // transaction's OWN date before grouping (Calculations.md §3a Phase 2). Default (null)
            // keeps USD for the formal report path and unit tests.
            decimal Display(decimal amountUSD) => toDisplay != null ? toDisplay(amountUSD, p.Date) : amountUSD;

            if (p.LineItems.Count > 0)
            {
                var lineItemsTotal = p.LineItems.Sum(li => li.Subtotal);
                var subtotalUSD = p.EffectiveTotalUSD;

                if (lineItemsTotal != 0)
                {
                    foreach (var li in p.LineItems)
                    {
                        var product = li.ProductId != null ? companyData.GetProduct(li.ProductId) : null;
                        var categoryName = product?.CategoryId != null
                            ? companyData.GetCategory(product.CategoryId)?.Name ?? "Other"
                            : "Other";
                        categoryTotals.TryAdd(categoryName, 0);
                        categoryTotals[categoryName] += Display(li.Subtotal / lineItemsTotal * subtotalUSD);
                    }
                }
                else
                {
                    // Line items sum to 0, allocate full transaction amount to "Other"
                    categoryTotals.TryAdd("Other", 0);
                    categoryTotals["Other"] += Display(subtotalUSD);
                }
            }
            else
            {
                categoryTotals.TryAdd("Other", 0);
                categoryTotals["Other"] += Display(p.EffectiveTotalUSD);
            }
        }

        return categoryTotals
            .Select(kvp => new ChartDataPoint { Label = kvp.Key, Value = (double)kvp.Value })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets total expenses value (in USD).
    /// </summary>
    public decimal GetTotalExpenses()
    {
        if (companyData?.Expenses == null)
            return 0;

        var (startDate, endDate) = GetDateRange();
        return ExpenseAggregator.SumExpensesUSD(companyData.Expenses, startDate, endDate);
    }

    #endregion

    #region Financial Charts

    /// <summary>
    /// Gets profit over time data (in USD for consistent calculations).
    /// </summary>
    public List<ChartDataPoint> GetProfitOverTime()
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // ProfitCalculator owns the formula (pre-tax revenue − expenses −
        // pre-tax refunds), keyed by the day each component happened. See
        // docs/Calculations.md §2 and §8.
        return ProfitCalculator.CalculateNetProfitByDayUSD(companyData, startDate, endDate)
            .OrderBy(kv => kv.Key)
            .Select(kv => new ChartDataPoint
            {
                Label = kv.Key.ToString("MMM dd"),
                Value = (double)kv.Value,
                Date = kv.Key
            })
            .ToList();
    }

    /// <summary>
    /// Gets revenue vs expenses comparison data.
    /// </summary>
    public List<ChartSeriesData> GetRevenueVsExpenses()
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // Switch to daily buckets when the filter window is narrow. Month
        // bucketing summed the WHOLE month even when the user filtered to
        // a single day or week, making "today only" show all of May, etc.
        // GetTimeBucket already handles the day/week/month switchover.
        var bucket = GetTimeBucket(startDate, endDate);

        if (bucket == TimeBucket.Day)
        {
            // Daily granularity: sum revenue and expenses per day inside
            // the actual filter window. Aligns with what the user expects
            // when filtering to a short range.
            var revByDay = companyData.Revenues
                .Where(s => s.Date >= startDate && s.Date <= endDate)
                .Where(RevenueAggregator.IsCollected)
                .GroupBy(s => s.Date.Date)
                .ToDictionary(g => g.Key, g => (double)g.Sum(s => s.EffectiveTotalUSD));
            // Subtract refunds cash-basis on the refund's day so the Revenue
            // series matches the standalone Revenue chart and stat card.
            foreach (var (day, refunded) in RefundAggregator.GroupRefundsByDayUSD(
                         companyData.Payments, startDate, endDate))
            {
                revByDay[day] = revByDay.GetValueOrDefault(day, 0) - (double)refunded;
            }
            var expByDay = companyData.Expenses
                .Where(p => p.Date >= startDate && p.Date <= endDate)
                .GroupBy(p => p.Date.Date)
                .ToDictionary(g => g.Key, g => (double)g.Sum(p => p.EffectiveTotalUSD));
            var dayKeys = revByDay.Keys.Union(expByDay.Keys).OrderBy(d => d).ToList();
            if (dayKeys.Count == 0) return [];

            var revDaily = dayKeys.Select(d => new ChartDataPoint
            {
                Label = d.ToString("MMM dd"),
                Value = revByDay.GetValueOrDefault(d, 0),
                Date = d
            }).ToList();
            var expDaily = dayKeys.Select(d => new ChartDataPoint
            {
                Label = d.ToString("MMM dd"),
                Value = expByDay.GetValueOrDefault(d, 0),
                Date = d
            }).ToList();
            return
            [
                new ChartSeriesData { Name = "Expenses", Color = AppColors.ExpenseRed, DataPoints = expDaily },
                new ChartSeriesData { Name = "Revenue",  Color = AppColors.Success,    DataPoints = revDaily }
            ];
        }

        var allMonths = GetMonthsBetween(startDate, endDate).ToList();

        // Filter to only months that have actual revenue or expense data
        // INSIDE the filter window (not the whole month).
        var monthsWithData = allMonths.Where(month =>
        {
            var clampedStart = new DateTime(month.Year, month.Month, 1) < startDate
                ? startDate
                : new DateTime(month.Year, month.Month, 1);
            var rawMonthEnd = new DateTime(month.Year, month.Month, 1).AddMonths(1).AddTicks(-1);
            var clampedEnd = rawMonthEnd > endDate ? endDate : rawMonthEnd;
            var hasRevenue = companyData.Revenues
                .Where(RevenueAggregator.IsCollected)
                .Any(s => s.Date >= clampedStart && s.Date <= clampedEnd);
            var hasExpenses = companyData.Expenses.Any(p => p.Date >= clampedStart && p.Date <= clampedEnd);
            return hasRevenue || hasExpenses;
        }).ToList();

        if (monthsWithData.Count == 0)
            return [];

        var revenueData = monthsWithData.Select(month =>
        {
            // Clamp month bounds to the filter window so partial months only
            // sum data inside the user's selection (e.g. "May 1 - May 11"
            // should not include May 12-31).
            var clampedStart = new DateTime(month.Year, month.Month, 1) < startDate
                ? startDate
                : new DateTime(month.Year, month.Month, 1);
            var rawMonthEnd = new DateTime(month.Year, month.Month, 1).AddMonths(1).AddTicks(-1);
            var clampedEnd = rawMonthEnd > endDate ? endDate : rawMonthEnd;

            var gross = (double)companyData.Revenues
                .Where(s => s.Date >= clampedStart && s.Date <= clampedEnd)
                .Where(RevenueAggregator.IsCollected)
                .Sum(s => s.EffectiveTotalUSD);
            // Subtract refunds within the same clamped window so the Revenue
            // series nets out refunds the same way other revenue surfaces do.
            var refunds = (double)RefundAggregator.GetRefundedInDateRangeUSD(
                companyData.Payments, clampedStart, clampedEnd);

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = gross - refunds,
                Date = month
            };
        }).ToList();

        var expenseData = monthsWithData.Select(month =>
        {
            var clampedStart = new DateTime(month.Year, month.Month, 1) < startDate
                ? startDate
                : new DateTime(month.Year, month.Month, 1);
            var rawMonthEnd = new DateTime(month.Year, month.Month, 1).AddMonths(1).AddTicks(-1);
            var clampedEnd = rawMonthEnd > endDate ? endDate : rawMonthEnd;

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = (double)ExpenseAggregator.SumExpensesUSD(
                    companyData.Expenses, clampedStart, clampedEnd),
                Date = month
            };
        }).ToList();

        return
        [
            new ChartSeriesData { Name = "Expenses", Color = AppColors.ExpenseRed, DataPoints = expenseData },
            new ChartSeriesData { Name = "Revenue", Color = AppColors.Success, DataPoints = revenueData }
        ];
    }

    /// <summary>
    /// Gets revenue vs expenses data grouped by day for granular display.
    /// </summary>
    public List<ChartSeriesData> GetRevenueVsExpensesDaily()
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var salesByDay = companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate)
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => s.Date.Date)
            .ToDictionary(g => g.Key, g => (double)g.Sum(s => s.EffectiveTotalUSD));
        // Subtract refunds cash-basis on the refund's day, as GetRevenueVsExpenses does.
        foreach (var (day, refunded) in RefundAggregator.GroupRefundsByDayUSD(
                     companyData.Payments, startDate, endDate))
        {
            salesByDay[day] = salesByDay.GetValueOrDefault(day, 0) - (double)refunded;
        }

        var purchasesByDay = companyData.Expenses
            .Where(p => p.Date >= startDate && p.Date <= endDate)
            .GroupBy(p => p.Date.Date)
            .ToDictionary(g => g.Key, g => (double)g.Sum(p => p.EffectiveTotalUSD));

        var allDays = salesByDay.Keys.Union(purchasesByDay.Keys).OrderBy(d => d).ToList();

        if (allDays.Count == 0)
            return [];

        var revenueData = allDays.Select(key => new ChartDataPoint
        {
            Label = key.ToString("MMM dd"),
            Value = salesByDay.GetValueOrDefault(key, 0),
            Date = key
        }).ToList();

        var expenseData = allDays.Select(key => new ChartDataPoint
        {
            Label = key.ToString("MMM dd"),
            Value = purchasesByDay.GetValueOrDefault(key, 0),
            Date = key
        }).ToList();

        return
        [
            new ChartSeriesData { Name = "Expenses", Color = AppColors.ExpenseRed, DataPoints = expenseData },
            new ChartSeriesData { Name = "Revenue", Color = AppColors.Success, DataPoints = revenueData }
        ];
    }

    /// <summary>
    /// Revenue vs Expenses already in the display currency: converts each DAY's value at that day's OWN
    /// rate (via <paramref name="toDisplay"/>) BEFORE bucketing to the report's granularity. A wide
    /// range must never convert a month total at the month-start date, whose exact-date rate is usually
    /// uncached, because that falls back to the raw USD figure. Mirrors the dashboard / analytics path
    /// (docs/Calculations.md §3a Phase 2).
    /// </summary>
    public List<ChartSeriesData> GetRevenueVsExpensesConverted(Func<decimal, DateTime, decimal> toDisplay)
    {
        var daily = GetRevenueVsExpensesDaily();
        if (daily.Count == 0)
            return daily;

        foreach (var series in daily)
            foreach (var point in series.DataPoints)
                if (point.Date.HasValue)
                    point.Value = (double)toDisplay((decimal)point.Value, point.Date.Value);

        var (startDate, endDate) = GetDateRange();
        var bucket = GetTimeBucket(startDate, endDate);
        return bucket == TimeBucket.Day ? daily : RebucketSeriesSum(daily, bucket);
    }

    #endregion

    #region Transaction Charts

    /// <summary>
    /// Gets average transaction value over time with separate series for revenue and expense transactions.
    /// </summary>
    public List<ChartSeriesData> GetAverageTransactionValueBySeries(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var allMonths = GetMonthsBetween(startDate, endDate).ToList();

        // Filter to only months with actual transaction data
        var monthsWithData = allMonths.Where(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);

            var hasRevenue = companyData.Revenues
                .Where(RevenueAggregator.IsCollected)
                .Any(s => s.Date >= monthStart && s.Date <= monthEnd);
            var hasExpenses = companyData.Expenses.Any(p => p.Date >= monthStart && p.Date <= monthEnd);

            return hasRevenue || hasExpenses;
        }).ToList();

        if (monthsWithData.Count == 0)
            return [];

        var revenueData = monthsWithData.Select(month =>
        {
            // Clamp month bounds to the filter window so a partial month only averages data inside
            // the user's selection (matches GetRevenueVsExpenses).
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            // Paid-only: average should reflect typical collected revenue,
            // not paddled by unpaid invoices that may never come in.
            var transactions = companyData.Revenues
                .Where(s => s.Date >= clampedStart && s.Date <= clampedEnd)
                .Where(RevenueAggregator.IsCollected)
                .Select(s => toDisplay != null ? toDisplay(s.EffectiveTotalUSD, s.Date) : s.EffectiveTotalUSD)
                .ToList();

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = transactions.Count > 0 ? (double)transactions.Average() : 0,
                Date = month
            };
        }).ToList();

        var expenseData = monthsWithData.Select(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            var transactions = companyData.Expenses
                .Where(p => p.Date >= clampedStart && p.Date <= clampedEnd)
                .Select(p => toDisplay != null ? toDisplay(p.EffectiveTotalUSD, p.Date) : p.EffectiveTotalUSD)
                .ToList();

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = transactions.Count > 0 ? (double)transactions.Average() : 0,
                Date = month
            };
        }).ToList();

        return
        [
            new ChartSeriesData { Name = "Expenses", Color = AppColors.ExpenseRed, DataPoints = expenseData },
            new ChartSeriesData { Name = "Revenue", Color = AppColors.Success, DataPoints = revenueData }
        ];
    }

    /// <summary>
    /// Gets transaction count over time with separate series for revenue and expense transactions.
    /// </summary>
    public List<ChartSeriesData> GetTransactionCountBySeries()
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var allMonths = GetMonthsBetween(startDate, endDate).ToList();

        // Filter to only months with actual transaction data
        var monthsWithData = allMonths.Where(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);

            var hasRevenue = companyData.Revenues
                .Where(RevenueAggregator.IsCollected)
                .Any(s => s.Date >= monthStart && s.Date <= monthEnd);
            var hasExpenses = companyData.Expenses.Any(p => p.Date >= monthStart && p.Date <= monthEnd);

            return hasRevenue || hasExpenses;
        }).ToList();

        if (monthsWithData.Count == 0)
            return [];

        var revenueData = monthsWithData.Select(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                // Match the paired "Average Transaction Value" chart, which
                // averages only collected revenue. Counting unpaid invoices
                // here would make Count * Avg disagree with Total Revenue.
                Value = companyData.Revenues
                    .Where(RevenueAggregator.IsCollected)
                    .Count(s => s.Date >= clampedStart && s.Date <= clampedEnd),
                Date = month
            };
        }).ToList();

        var expenseData = monthsWithData.Select(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = companyData.Expenses.Count(p => p.Date >= clampedStart && p.Date <= clampedEnd),
                Date = month
            };
        }).ToList();

        return
        [
            new ChartSeriesData { Name = "Expenses", Color = AppColors.ExpenseRed, DataPoints = expenseData },
            new ChartSeriesData { Name = "Revenue", Color = AppColors.Success, DataPoints = revenueData }
        ];
    }

    /// <summary>
    /// Gets average transaction value over time grouped by day with separate series for revenue and expense transactions.
    /// </summary>
    public List<ChartSeriesData> GetAverageTransactionValueDailyBySeries()
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var revenueValuesByDate = new Dictionary<DateTime, List<decimal>>();
        var expenseValuesByDate = new Dictionary<DateTime, List<decimal>>();

        // Only average collected revenue, unpaid invoices aren't actual
        // transactions in cash-basis terms.
        foreach (var revenue in companyData.Revenues
                     .Where(s => s.Date >= startDate && s.Date <= endDate)
                     .Where(RevenueAggregator.IsCollected))
        {
            var date = revenue.Date.Date;
            if (!revenueValuesByDate.ContainsKey(date))
                revenueValuesByDate[date] = [];
            revenueValuesByDate[date].Add(revenue.EffectiveTotalUSD);
        }

        foreach (var expense in companyData.Expenses.Where(p => p.Date >= startDate && p.Date <= endDate))
        {
            var date = expense.Date.Date;
            if (!expenseValuesByDate.ContainsKey(date))
                expenseValuesByDate[date] = [];
            expenseValuesByDate[date].Add(expense.EffectiveTotalUSD);
        }

        // Combine all dates
        var allDates = revenueValuesByDate.Keys.Union(expenseValuesByDate.Keys).OrderBy(d => d).ToList();

        if (allDates.Count == 0)
            return [];

        var revenueData = allDates.Select(date => new ChartDataPoint
        {
            Label = date.ToString("MMM dd"),
            Value = revenueValuesByDate.TryGetValue(date, out var values) && values.Count > 0 ? (double)values.Average() : 0,
            Date = date
        }).ToList();

        var expenseData = allDates.Select(date => new ChartDataPoint
        {
            Label = date.ToString("MMM dd"),
            Value = expenseValuesByDate.TryGetValue(date, out var values) && values.Count > 0 ? (double)values.Average() : 0,
            Date = date
        }).ToList();

        return
        [
            new ChartSeriesData { Name = "Expenses", Color = AppColors.ExpenseRed, DataPoints = expenseData },
            new ChartSeriesData { Name = "Revenue", Color = AppColors.Success, DataPoints = revenueData }
        ];
    }

    /// <summary>
    /// Gets transaction count over time grouped by day with separate series for revenue and expense transactions.
    /// </summary>
    public List<ChartSeriesData> GetTransactionCountDailyBySeries()
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var revenueCountsByDate = new Dictionary<DateTime, int>();
        var expenseCountsByDate = new Dictionary<DateTime, int>();

        // Match the paired daily Average chart, count only collected revenue.
        foreach (var revenue in companyData.Revenues
                     .Where(s => s.Date >= startDate && s.Date <= endDate)
                     .Where(RevenueAggregator.IsCollected))
        {
            var date = revenue.Date.Date;
            revenueCountsByDate[date] = revenueCountsByDate.GetValueOrDefault(date, 0) + 1;
        }

        foreach (var expense in companyData.Expenses.Where(p => p.Date >= startDate && p.Date <= endDate))
        {
            var date = expense.Date.Date;
            expenseCountsByDate[date] = expenseCountsByDate.GetValueOrDefault(date, 0) + 1;
        }

        // Combine all dates
        var allDates = revenueCountsByDate.Keys.Union(expenseCountsByDate.Keys).OrderBy(d => d).ToList();

        if (allDates.Count == 0)
            return [];

        var revenueData = allDates.Select(date => new ChartDataPoint
        {
            Label = date.ToString("MMM dd"),
            Value = revenueCountsByDate.GetValueOrDefault(date, 0),
            Date = date
        }).ToList();

        var expenseData = allDates.Select(date => new ChartDataPoint
        {
            Label = date.ToString("MMM dd"),
            Value = expenseCountsByDate.GetValueOrDefault(date, 0),
            Date = date
        }).ToList();

        return
        [
            new ChartSeriesData { Name = "Expenses", Color = AppColors.ExpenseRed, DataPoints = expenseData },
            new ChartSeriesData { Name = "Revenue", Color = AppColors.Success, DataPoints = revenueData }
        ];
    }

    #endregion

    #region Geographic Charts

    /// <summary>
    /// Gets sales by customer country.
    /// </summary>
    public List<ChartDataPoint> GetRevenueByCountryOfOrigin(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        return GetRevenueByCustomerCountry(toDisplay);
    }

    /// <summary>
    /// Gets sales grouped by customer country.
    /// Used for geographic distribution charts showing where customers are located.
    /// </summary>
    public List<ChartDataPoint> GetRevenueByCustomerCountry(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData?.Revenues == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // When a display converter is supplied, convert each transaction's USD amount at its OWN
        // date before grouping (Calculations.md §3a Phase 2). Default (null) keeps USD.
        decimal Display(decimal amountUSD, DateTime date) => toDisplay != null ? toDisplay(amountUSD, date) : amountUSD;

        return companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate)
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s =>
            {
                var customer = companyData.GetCustomer(s.CustomerId ?? "");
                return customer?.Address.Country ?? "Unknown";
            })
            .Select(g => new ChartDataPoint
            {
                Label = g.Key,
                Value = (double)g.Sum(s => Display(s.EffectiveTotalUSD, s.Date))
            })
            // Filter out "Unknown" entries with zero or negligible value
            .Where(p => p.Label != "Unknown" || p.Value > 0.01)
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets world map data (countries with sales totals) by customer country.
    /// Returns country names as keys.
    /// </summary>
    public Dictionary<string, double> GetWorldMapData(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData?.Revenues == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate)
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s =>
            {
                var customer = companyData.GetCustomer(s.CustomerId ?? "");
                return customer?.Address.Country;
            })
            .Where(g => g.Key != null)
            // Convert each revenue at its OWN date before summing per country
            // (docs/Calculations.md §3a Phase 2); null keeps USD.
            .ToDictionary(g => g.Key!, g => (double)g.Sum(s =>
                toDisplay != null ? toDisplay(s.EffectiveTotalUSD, s.Date) : s.EffectiveTotalUSD));
    }

    /// <summary>
    /// Gets world map data by supplier country from purchases.
    /// Returns country names as keys.
    /// </summary>
    public Dictionary<string, double> GetWorldMapDataBySupplier()
    {
        if (companyData?.Expenses == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.Expenses
            .Where(p => p.Date >= startDate && p.Date <= endDate)
            .GroupBy(p =>
            {
                var supplier = companyData.GetSupplier(p.SupplierId ?? "");
                return supplier?.Address.Country;
            })
            .Where(g => g.Key != null && !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key!, g => (double)g.Sum(p => p.EffectiveTotalUSD));
    }

    /// <summary>
    /// Gets purchases by supplier country.
    /// </summary>
    public List<ChartDataPoint> GetExpensesByCountryOfDestination(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData?.Expenses == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // When a display converter is supplied, convert each transaction's USD amount at its OWN
        // date before grouping (Calculations.md §3a Phase 2). Default (null) keeps USD.
        decimal Display(decimal amountUSD, DateTime date) => toDisplay != null ? toDisplay(amountUSD, date) : amountUSD;

        return companyData.Expenses
            .Where(p => p.Date >= startDate && p.Date <= endDate)
            .GroupBy(p =>
            {
                var supplier = companyData.GetSupplier(p.SupplierId ?? "");
                return supplier?.Address.Country ?? "Unknown";
            })
            .Select(g => new ChartDataPoint
            {
                Label = g.Key,
                Value = (double)g.Sum(p => Display(p.EffectiveTotalUSD, p.Date))
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets purchases by supplier company name.
    /// </summary>
    public List<ChartDataPoint> GetExpensesBySupplierCompany(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData?.Expenses == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // When a display converter is supplied, convert each transaction's USD amount at its OWN
        // date before grouping (Calculations.md §3a Phase 2). Default (null) keeps USD.
        decimal Display(decimal amountUSD, DateTime date) => toDisplay != null ? toDisplay(amountUSD, date) : amountUSD;

        return companyData.Expenses
            .Where(p => p.Date >= startDate && p.Date <= endDate)
            .GroupBy(p => companyData.GetSupplier(p.SupplierId ?? "")?.Name ?? "Unknown")
            .Where(g => g.Key != "Unknown")
            .Select(g => new ChartDataPoint
            {
                Label = g.Key,
                Value = (double)g.Sum(p => Display(p.EffectiveTotalUSD, p.Date))
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets sales by customer.
    /// </summary>
    public List<ChartDataPoint> GetRevenueByCompanyOfOrigin(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData?.Revenues == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // When a display converter is supplied, convert each transaction's USD amount at its OWN
        // date before grouping (Calculations.md §3a Phase 2). Default (null) keeps USD.
        decimal Display(decimal amountUSD, DateTime date) => toDisplay != null ? toDisplay(amountUSD, date) : amountUSD;

        return companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate)
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => companyData.GetCustomer(s.CustomerId ?? "")?.Name ?? "Unknown")
            .Select(g => new ChartDataPoint
            {
                Label = g.Key,
                Value = (double)g.Sum(s => Display(s.EffectiveTotalUSD, s.Date))
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets sales by customer company (destination companies).
    /// Only includes customers with CompanyName set.
    /// </summary>
    public List<ChartDataPoint> GetRevenueByCompanyOfDestination(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData?.Revenues == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // When a display converter is supplied, convert each transaction's USD amount at its OWN
        // date before grouping (Calculations.md §3a Phase 2). Default (null) keeps USD.
        decimal Display(decimal amountUSD, DateTime date) => toDisplay != null ? toDisplay(amountUSD, date) : amountUSD;

        return companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate && !string.IsNullOrEmpty(s.CustomerId))
            .Where(RevenueAggregator.IsCollected)
            .Select(s => new { Revenue = s, Customer = companyData.GetCustomer(s.CustomerId ?? "") })
            .Where(x => x.Customer != null && !string.IsNullOrEmpty(x.Customer.CompanyName))
            .GroupBy(x => x.Customer!.CompanyName)
            .Select(g => new ChartDataPoint
            {
                Label = g.Key!,
                Value = (double)g.Sum(x => Display(x.Revenue.EffectiveTotalUSD, x.Revenue.Date))
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets average shipping costs over time.
    /// </summary>
    public List<ChartDataPoint> GetAverageShippingCosts(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var allMonths = GetMonthsBetween(startDate, endDate).ToList();

        // Cash-basis: only count shipping on revenue rows that were actually
        // collected. Unpaid invoices shouldn't influence the shipping average,
        // see Calculations.md §2 Rule 2.
        var monthsWithData = allMonths.Where(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);

            var hasSales = companyData.Revenues
                .Where(RevenueAggregator.IsCollected)
                .Any(s => s.Date >= monthStart && s.Date <= monthEnd);
            var hasPurchases = companyData.Expenses.Any(p => p.Date >= monthStart && p.Date <= monthEnd);

            return hasSales || hasPurchases;
        }).ToList();

        return monthsWithData.Select(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            var shippingCosts = new List<decimal>();

            shippingCosts.AddRange(companyData.Revenues
                .Where(s => s.Date >= clampedStart && s.Date <= clampedEnd)
                .Where(RevenueAggregator.IsCollected)
                .Select(s => toDisplay != null ? toDisplay(s.EffectiveShippingCostUSD, s.Date) : s.EffectiveShippingCostUSD));

            shippingCosts.AddRange(companyData.Expenses
                .Where(p => p.Date >= clampedStart && p.Date <= clampedEnd)
                .Select(p => toDisplay != null ? toDisplay(p.EffectiveShippingCostUSD, p.Date) : p.EffectiveShippingCostUSD));

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = shippingCosts.Count > 0 ? (double)shippingCosts.Average() : 0,
                Date = month
            };
        }).ToList();
    }

    /// <summary>
    /// Gets average shipping costs over time grouped by day (in USD).
    /// </summary>
    public List<ChartDataPoint> GetAverageShippingCostsDaily()
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var shippingByDate = new Dictionary<DateTime, List<decimal>>();

        // Cash-basis: only count shipping on collected revenue rows.
        foreach (var revenue in companyData.Revenues
                     .Where(s => s.Date >= startDate && s.Date <= endDate)
                     .Where(RevenueAggregator.IsCollected))
        {
            var date = revenue.Date.Date;
            if (!shippingByDate.ContainsKey(date))
                shippingByDate[date] = [];
            shippingByDate[date].Add(revenue.EffectiveShippingCostUSD);
        }

        foreach (var purchase in companyData.Expenses.Where(p => p.Date >= startDate && p.Date <= endDate))
        {
            var date = purchase.Date.Date;
            if (!shippingByDate.ContainsKey(date))
                shippingByDate[date] = [];
            shippingByDate[date].Add(purchase.EffectiveShippingCostUSD);
        }

        return shippingByDate
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new ChartDataPoint
            {
                Label = kvp.Key.ToString("MMM dd"),
                Value = kvp.Value.Count > 0 ? (double)kvp.Value.Average() : 0,
                Date = kvp.Key
            })
            .ToList();
    }

    #endregion

    #region Accountant Charts

    public List<ChartDataPoint> GetTransactionsByAccountant()
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var accountantData = new Dictionary<string, int>();

        // Revenue by accountant
        foreach (var revenue in companyData.Revenues.Where(s => s.Date >= startDate && s.Date <= endDate))
        {
            var accountantName = companyData.GetAccountant(revenue.AccountantId ?? "")?.Name ?? "Unknown";
            accountantData.TryAdd(accountantName, 0);
            accountantData[accountantName] += 1;
        }

        // Expenses by accountant
        foreach (var expense in companyData.Expenses.Where(p => p.Date >= startDate && p.Date <= endDate))
        {
            var accountantName = companyData.GetAccountant(expense.AccountantId ?? "")?.Name ?? "Unknown";
            accountantData.TryAdd(accountantName, 0);
            accountantData[accountantName] += 1;
        }

        return accountantData
            .Select(kvp => new ChartDataPoint { Label = kvp.Key, Value = kvp.Value })
            .OrderByDescending(p => p.Value)
            .ToList();
    }

    #endregion

    #region Customer Charts

    public List<ChartDataPoint> GetTopCustomersByRevenue(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData?.Revenues == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // Convert each revenue at its OWN date before summing (display currency), matching the
        // other monetary distributions; falls back to raw USD when no converter is supplied.
        decimal Display(decimal amountUSD, DateTime date) => toDisplay != null ? toDisplay(amountUSD, date) : amountUSD;

        return companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate && !string.IsNullOrEmpty(s.CustomerId))
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => s.CustomerId)
            .Select(g =>
            {
                var customer = companyData.GetCustomer(g.Key ?? "");
                var customerName = customer?.Name ?? customer?.CompanyName ?? "Unknown";
                return new ChartDataPoint
                {
                    Label = customerName,
                    Value = (double)g.Sum(s => Display(s.EffectiveTotalUSD, s.Date))
                };
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets customer payment status breakdown (Paid, Pending, Overdue).
    /// Uses Sales.PaymentStatus to match ChartLoaderService behavior.
    /// </summary>
    public List<ChartDataPoint> GetCustomerPaymentStatus()
    {
        if (companyData?.Revenues == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var salesInRange = companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate)
            .ToList();

        if (salesInRange.Count == 0)
            return [];

        var paid = salesInRange.Count(RevenueAggregator.IsCollected);
        var pending = salesInRange.Count(s => s.PaymentStatus == RevenuePaymentStatus.Pending);
        var overdue = salesInRange.Count(s => s.PaymentStatus == RevenuePaymentStatus.Overdue);

        var result = new List<ChartDataPoint>();
        if (paid > 0) result.Add(new ChartDataPoint { Label = "Paid", Value = paid });
        if (pending > 0) result.Add(new ChartDataPoint { Label = "Pending", Value = pending });
        if (overdue > 0) result.Add(new ChartDataPoint { Label = "Overdue", Value = overdue });

        return result;
    }

    /// <summary>
    /// Gets customer growth over time (new customers acquired).
    /// </summary>
    public List<ChartDataPoint> GetCustomerGrowth()
    {
        if (companyData?.Customers == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // Group customers by their first transaction date (approximated by earliest revenue)
        var customerFirstTransaction = companyData.Revenues
            .Where(s => !string.IsNullOrEmpty(s.CustomerId))
            .GroupBy(s => s.CustomerId)
            .ToDictionary(g => g.Key!, g => g.Min(s => s.Date));

        return customerFirstTransaction
            .Where(kvp => kvp.Value >= startDate && kvp.Value <= endDate)
            .GroupBy(kvp => new DateTime(kvp.Value.Year, kvp.Value.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new ChartDataPoint
            {
                Label = g.Key.ToString("MMM yyyy"),
                Value = g.Count(),
                Date = g.Key
            })
            .ToList();
    }

    /// <summary>
    /// Gets customer lifetime value (average revenue per customer).
    /// </summary>
    public List<ChartDataPoint> GetCustomerLifetimeValue()
    {
        if (companyData?.Revenues == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // Lifetime value = collected revenue per customer. Unpaid invoices
        // would inflate every bucket and misclassify which segment a
        // customer falls into.
        var customerRevenue = companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate && !string.IsNullOrEmpty(s.CustomerId))
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => s.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                TotalRevenue = g.Sum(s => s.EffectiveTotalUSD)
            })
            .ToList();

        if (!customerRevenue.Any())
            return [];

        // Return distribution of customer lifetime values
        var ranges = new[]
        {
            (0m, 100m, "$0-100"),
            (100m, 500m, "$100-500"),
            (500m, 1000m, "$500-1K"),
            (1000m, 5000m, "$1K-5K"),
            (5000m, decimal.MaxValue, "$5K+")
        };

        return ranges
            .Select(r => new ChartDataPoint
            {
                Label = r.Item3,
                Value = customerRevenue.Count(c => c.TotalRevenue >= r.Item1 && c.TotalRevenue < r.Item2)
            })
            .Where(p => p.Value > 0)
            .ToList();
    }

    /// <summary>
    /// Gets active vs inactive customers count.
    /// </summary>
    public List<ChartDataPoint> GetActiveVsInactiveCustomers()
    {
        if (companyData?.Customers == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var activeCustomerIds = companyData.Revenues
            .Where(s => s.Date >= startDate && s.Date <= endDate && !string.IsNullOrEmpty(s.CustomerId))
            .Select(s => s.CustomerId)
            .Distinct()
            .ToHashSet();

        var activeCount = activeCustomerIds.Count;
        var inactiveCount = companyData.Customers.Count - activeCount;

        var result = new List<ChartDataPoint>();
        if (activeCount > 0) result.Add(new ChartDataPoint { Label = "Active", Value = activeCount });
        if (inactiveCount > 0) result.Add(new ChartDataPoint { Label = "Inactive", Value = inactiveCount });

        return result;
    }

    /// <summary>
    /// Gets rentals per customer distribution.
    /// </summary>
    public List<ChartDataPoint> GetRentalsPerCustomer()
    {
        if (companyData?.Rentals == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.Rentals
            .Where(r => r.StartDate >= startDate && r.StartDate <= endDate && !string.IsNullOrEmpty(r.CustomerId))
            .GroupBy(r => r.CustomerId)
            .Select(g =>
            {
                var customer = companyData.GetCustomer(g.Key);
                var customerName = customer?.Name ?? customer?.CompanyName ?? "Unknown";
                return new ChartDataPoint
                {
                    Label = customerName,
                    Value = g.Count()
                };
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    #endregion

    #region Returns Charts

    public List<ChartDataPoint> GetReturnsOverTime()
    {
        if (companyData?.Returns == null || !filters.IncludeReturns)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.Returns
            .Where(r => r.ReturnDate >= startDate && r.ReturnDate <= endDate)
            .GroupBy(r => r.ReturnDate.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ChartDataPoint
            {
                Label = g.Key.ToString("MMM dd"),
                Value = g.Count(),
                Date = g.Key
            })
            .ToList();
    }

    /// <summary>
    /// Gets returns by reason.
    /// </summary>
    public List<ChartDataPoint> GetReturnReasons()
    {
        if (companyData?.Returns == null || !filters.IncludeReturns)
            return [];

        var (startDate, endDate) = GetDateRange();

        // Returns have Items, each with a Reason - group by all item reasons
        return companyData.Returns
            .Where(r => r.ReturnDate >= startDate && r.ReturnDate <= endDate)
            .SelectMany(r => r.Items)
            .GroupBy(item => string.IsNullOrEmpty(item.Reason) ? "Unknown" : item.Reason)
            .Select(g => new ChartDataPoint
            {
                Label = g.Key,
                Value = g.Count()
            })
            .OrderByDescending(p => p.Value)
            .ToList();
    }

    /// <summary>
    /// Gets return financial impact (refund amounts).
    /// </summary>
    public List<ChartDataPoint> GetReturnFinancialImpact()
    {
        if (companyData?.Returns == null || !filters.IncludeReturns)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.Returns
            .Where(r => r.ReturnDate >= startDate && r.ReturnDate <= endDate)
            .GroupBy(r => new DateTime(r.ReturnDate.Year, r.ReturnDate.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new ChartDataPoint
            {
                Label = g.Key.ToString("MMM yyyy"),
                Value = (double)g.Sum(r => r.RefundAmount),
                Date = g.Key
            })
            .ToList();
    }

    /// <summary>
    /// Gets return financial impact (refund amounts) grouped by day. With <paramref name="toDisplay"/>,
    /// each amount is converted from its sale's currency at the return's date, and one whose rate
    /// is unavailable (null) counts as 0.
    /// </summary>
    public List<ChartDataPoint> GetReturnFinancialImpactDaily(Func<decimal, string, DateTime, decimal?>? toDisplay = null)
    {
        if (companyData?.Returns == null || !filters.IncludeReturns)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.Returns
            .Where(r => r.ReturnDate >= startDate && r.ReturnDate <= endDate)
            .GroupBy(r => r.ReturnDate.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ChartDataPoint
            {
                Label = g.Key.ToString("MMM dd"),
                Value = (double)g.Sum(r => toDisplay == null
                    ? r.RefundAmount
                    : toDisplay(r.RefundAmount, ReturnLossAmounts.CurrencyOf(companyData, r), r.ReturnDate) ?? 0m),
                Date = g.Key
            })
            .ToList();
    }

    public List<ChartDataPoint> GetReturnsByCategory()
    {
        if (companyData?.Returns == null || !filters.IncludeReturns)
            return [];

        var (startDate, endDate) = GetDateRange();

        // Returns have Items with ProductId - group by category of each returned item
        return companyData.Returns
            .Where(r => r.ReturnDate >= startDate && r.ReturnDate <= endDate)
            .SelectMany(r => r.Items)
            .GroupBy(item =>
            {
                var product = companyData.GetProduct(item.ProductId);
                var category = product != null ? companyData.GetCategory(product.CategoryId ?? "") : null;
                return category?.Name ?? "Unknown";
            })
            .Select(g => new ChartDataPoint
            {
                Label = g.Key,
                Value = g.Count()
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    public List<ChartDataPoint> GetReturnsByProduct()
    {
        if (companyData?.Returns == null || !filters.IncludeReturns)
            return [];

        var (startDate, endDate) = GetDateRange();

        // Returns have Items with ProductId - group by product name of each returned item
        return companyData.Returns
            .Where(r => r.ReturnDate >= startDate && r.ReturnDate <= endDate)
            .SelectMany(r => r.Items)
            .GroupBy(item => companyData.GetProduct(item.ProductId)?.Name ?? "Unknown")
            .Select(g => new ChartDataPoint
            {
                Label = g.Key,
                Value = g.Count()
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets returns comparison over time.
    /// Note: Current model only tracks revenue returns (returns from customers).
    /// </summary>
    public List<ChartSeriesData> GetExpenseVsRevenueReturns()
    {
        if (companyData?.Returns == null || !filters.IncludeReturns)
            return [];

        var (startDate, endDate) = GetDateRange();

        var allMonths = GetMonthsBetween(startDate, endDate).ToList();

        // Filter to only months with actual return data
        var monthsWithData = allMonths.Where(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;
            return companyData.Returns.Any(r => r.ReturnDate >= clampedStart && r.ReturnDate <= clampedEnd);
        }).ToList();

        if (monthsWithData.Count == 0)
            return [];

        // Current Return model represents returns from revenue (customer returns)
        var revenueReturns = monthsWithData.Select(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = companyData.Returns
                    .Count(r => r.ReturnDate >= clampedStart && r.ReturnDate <= clampedEnd),
                Date = month
            };
        }).ToList();

        // No expense returns in current model - return empty series
        var expenseReturns = monthsWithData.Select(month => new ChartDataPoint
        {
            Label = month.ToString("MMM yyyy"),
            Value = 0,
            Date = month
        }).ToList();

        return
        [
            new ChartSeriesData { Name = "Revenue Returns", Color = AppColors.ExpenseRed, DataPoints = revenueReturns },
            new ChartSeriesData { Name = "Expense Returns", Color = AppColors.PurpleDark, DataPoints = expenseReturns }
        ];
    }

    #endregion

    #region Losses Charts

    public List<ChartDataPoint> GetLossesOverTime()
    {
        if (companyData?.LostDamaged == null || !filters.IncludeLosses)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.LostDamaged
            .Where(l => l.DateDiscovered >= startDate && l.DateDiscovered <= endDate)
            .GroupBy(l => l.DateDiscovered.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ChartDataPoint
            {
                Label = g.Key.ToString("MMM dd"),
                Value = g.Count(),
                Date = g.Key
            })
            .ToList();
    }

    public List<ChartDataPoint> GetLossReasons()
    {
        if (companyData?.LostDamaged == null || !filters.IncludeLosses)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.LostDamaged
            .Where(l => l.DateDiscovered >= startDate && l.DateDiscovered <= endDate)
            .GroupBy(l => l.Reason.ToString())
            .Select(g => new ChartDataPoint
            {
                Label = g.Key,
                Value = g.Count()
            })
            .OrderByDescending(p => p.Value)
            .ToList();
    }

    public List<ChartDataPoint> GetLossFinancialImpact()
    {
        if (companyData?.LostDamaged == null || !filters.IncludeLosses)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.LostDamaged
            .Where(l => l.DateDiscovered >= startDate && l.DateDiscovered <= endDate)
            .GroupBy(l => new DateTime(l.DateDiscovered.Year, l.DateDiscovered.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new ChartDataPoint
            {
                Label = g.Key.ToString("MMM yyyy"),
                Value = (double)g.Sum(l => l.ValueLost),
                Date = g.Key
            })
            .ToList();
    }

    /// <summary>
    /// Gets loss financial impact grouped by day. With <paramref name="toDisplay"/>, each value is
    /// converted from its sale's or purchase's currency at the loss's date, and one whose rate is
    /// unavailable (null) counts as 0.
    /// </summary>
    public List<ChartDataPoint> GetLossFinancialImpactDaily(Func<decimal, string, DateTime, decimal?>? toDisplay = null)
    {
        if (companyData?.LostDamaged == null || !filters.IncludeLosses)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.LostDamaged
            .Where(l => l.DateDiscovered >= startDate && l.DateDiscovered <= endDate)
            .GroupBy(l => l.DateDiscovered.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ChartDataPoint
            {
                Label = g.Key.ToString("MMM dd"),
                Value = (double)g.Sum(l => toDisplay == null
                    ? l.ValueLost
                    : toDisplay(l.ValueLost, ReturnLossAmounts.CurrencyOf(companyData, l), l.DateDiscovered) ?? 0m),
                Date = g.Key
            })
            .ToList();
    }

    public List<ChartDataPoint> GetLossesByCategory()
    {
        if (companyData?.LostDamaged == null || !filters.IncludeLosses)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.LostDamaged
            .Where(l => l.DateDiscovered >= startDate && l.DateDiscovered <= endDate)
            .GroupBy(l =>
            {
                var product = companyData.GetProduct(l.ProductId);
                var category = product != null ? companyData.GetCategory(product.CategoryId ?? "") : null;
                return category?.Name ?? "Unknown";
            })
            .Select(g => new ChartDataPoint
            {
                Label = g.Key,
                Value = g.Count()
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    public List<ChartDataPoint> GetLossesByProduct()
    {
        if (companyData?.LostDamaged == null || !filters.IncludeLosses)
            return [];

        var (startDate, endDate) = GetDateRange();

        return companyData.LostDamaged
            .Where(l => l.DateDiscovered >= startDate && l.DateDiscovered <= endDate)
            .GroupBy(l => companyData.GetProduct(l.ProductId)?.Name ?? "Unknown")
            .Select(g => new ChartDataPoint
            {
                Label = g.Key,
                Value = g.Count()
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets losses by reason type over time.
    /// Note: Current model tracks inventory losses (all are expense-side). No revenue losses exist in the data model.
    /// </summary>
    public List<ChartSeriesData> GetExpenseVsRevenueLosses()
    {
        if (companyData?.LostDamaged == null || !filters.IncludeLosses)
            return [];

        var (startDate, endDate) = GetDateRange();

        var allMonths = GetMonthsBetween(startDate, endDate).ToList();

        // Filter to only months with actual loss data
        var monthsWithData = allMonths.Where(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;
            return companyData.LostDamaged.Any(l => l.DateDiscovered >= clampedStart && l.DateDiscovered <= clampedEnd);
        }).ToList();

        if (monthsWithData.Count == 0)
            return [];

        // All lost/damaged inventory are expense-side losses
        var expenseLosses = monthsWithData.Select(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = companyData.LostDamaged
                    .Count(l => l.DateDiscovered >= clampedStart && l.DateDiscovered <= clampedEnd),
                Date = month
            };
        }).ToList();

        // No revenue losses in current model - return empty series
        var revenueLosses = monthsWithData.Select(month => new ChartDataPoint
        {
            Label = month.ToString("MMM yyyy"),
            Value = 0,
            Date = month
        }).ToList();

        return
        [
            new ChartSeriesData { Name = "Expense Losses", Color = AppColors.Error, DataPoints = expenseLosses },
            new ChartSeriesData { Name = "Revenue Losses", Color = AppColors.PurpleDark, DataPoints = revenueLosses }
        ];
    }

    #endregion

    #region Tax Charts

    /// <summary>
    /// Gets tax collected (from revenues) and tax paid (from expenses) over time.
    /// Returns two series: "Tax Collected" and "Tax Paid".
    /// </summary>
    public List<ChartSeriesData> GetTaxCollectedVsPaid(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var allMonths = GetMonthsBetween(startDate, endDate).ToList();

        // Filter to only months with tax data
        var monthsWithData = allMonths.Where(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;
            return companyData.Revenues.Any(r => r.Date >= clampedStart && r.Date <= clampedEnd && (r.TaxAmountUSD > 0 || r.TaxAmount > 0)) ||
                   companyData.Expenses.Any(e => e.Date >= clampedStart && e.Date <= clampedEnd && (e.TaxAmountUSD > 0 || e.TaxAmount > 0));
        }).ToList();

        if (monthsWithData.Count == 0)
            return [];

        var taxCollected = monthsWithData.Select(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = (double)companyData.Revenues
                    .Where(r => r.Date >= clampedStart && r.Date <= clampedEnd)
                    .Sum(r => toDisplay != null ? toDisplay(r.EffectiveTaxAmountUSD, r.Date) : r.EffectiveTaxAmountUSD),
                Date = month
            };
        }).ToList();

        var taxPaid = monthsWithData.Select(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = (double)companyData.Expenses
                    .Where(e => e.Date >= clampedStart && e.Date <= clampedEnd)
                    .Sum(e => toDisplay != null ? toDisplay(e.EffectiveTaxAmountUSD, e.Date) : e.EffectiveTaxAmountUSD),
                Date = month
            };
        }).ToList();

        return
        [
            new ChartSeriesData { Name = "Tax Collected", Color = "#22C55E", DataPoints = taxCollected },
            new ChartSeriesData { Name = "Tax Paid", Color = "#EF4444", DataPoints = taxPaid }
        ];
    }

    /// <summary>
    /// Gets net tax liability (collected - paid) over time as daily data points.
    /// </summary>
    public List<ChartDataPoint> GetTaxLiabilityOverTime()
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        // Get all dates with any tax data
        var revenueDates = companyData.Revenues
            .Where(r => r.Date >= startDate && r.Date <= endDate && (r.TaxAmountUSD > 0 || r.TaxAmount > 0))
            .Select(r => r.Date.Date);
        var expenseDates = companyData.Expenses
            .Where(e => e.Date >= startDate && e.Date <= endDate && (e.TaxAmountUSD > 0 || e.TaxAmount > 0))
            .Select(e => e.Date.Date);

        var allDates = revenueDates.Concat(expenseDates).Distinct().OrderBy(d => d).ToList();

        if (allDates.Count == 0)
            return [];

        return allDates.Select(date =>
        {
            var collected = companyData.Revenues
                .Where(r => r.Date.Date == date)
                .Sum(r => r.EffectiveTaxAmountUSD);
            var paid = companyData.Expenses
                .Where(e => e.Date.Date == date)
                .Sum(e => e.EffectiveTaxAmountUSD);

            return new ChartDataPoint
            {
                Label = date.ToString("MMM dd"),
                Value = (double)(collected - paid),
                Date = date
            };
        }).ToList();
    }

    /// <summary>
    /// Gets tax amounts grouped by product category.
    /// </summary>
    public List<ChartDataPoint> GetTaxByCategory(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var allTransactions = new List<(decimal TaxUSD, string? CategoryId, DateTime Date)>();

        allTransactions.AddRange(companyData.Revenues
            .Where(r => r.Date >= startDate && r.Date <= endDate && (r.TaxAmountUSD > 0 || r.TaxAmount > 0))
            .Select(r =>
            {
                var productId = r.LineItems.FirstOrDefault()?.ProductId;
                var product = productId != null ? companyData.GetProduct(productId) : null;
                return (r.EffectiveTaxAmountUSD, product?.CategoryId, r.Date);
            }));

        allTransactions.AddRange(companyData.Expenses
            .Where(e => e.Date >= startDate && e.Date <= endDate && (e.TaxAmountUSD > 0 || e.TaxAmount > 0))
            .Select(e =>
            {
                var productId = e.LineItems.FirstOrDefault()?.ProductId;
                var product = productId != null ? companyData.GetProduct(productId) : null;
                return (e.EffectiveTaxAmountUSD, product?.CategoryId, e.Date);
            }));

        if (allTransactions.Count == 0)
            return [];

        // When a display converter is supplied, convert each transaction's tax USD at its OWN date
        // before grouping (Calculations.md §3a Phase 2). Default (null) keeps USD.
        decimal Display(decimal amountUSD, DateTime date) => toDisplay != null ? toDisplay(amountUSD, date) : amountUSD;

        return allTransactions
            .GroupBy(t => t.CategoryId ?? "Unknown")
            .Select(g =>
            {
                var categoryName = companyData.GetCategory(g.Key)?.Name ?? "Other";
                return new ChartDataPoint
                {
                    Label = categoryName,
                    Value = (double)g.Sum(t => Display(t.TaxUSD, t.Date))
                };
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets transaction count grouped by tax rate bracket, split by Revenue and Expense.
    /// Returns two series for stacked bar chart.
    /// </summary>
    public (List<ChartDataPoint> RevenueRates, List<ChartDataPoint> ExpenseRates, string[] Labels) GetTaxRateDistribution()
    {
        if (companyData == null)
            return ([], [], []);

        var (startDate, endDate) = GetDateRange();

        string GetBracket(decimal rate) => rate switch
        {
            0 => "0%",
            <= 5 => "1-5%",
            <= 10 => "6-10%",
            <= 15 => "11-15%",
            <= 20 => "16-20%",
            _ => "20%+"
        };

        var allBrackets = new[] { "0%", "1-5%", "6-10%", "11-15%", "16-20%", "20%+" };

        // Transaction.TaxRate is stored as percentage (e.g., 8 for 8%) by ViewModels
        var revenueRates = companyData.Revenues
            .Where(r => r.Date >= startDate && r.Date <= endDate)
            .Select(r => r.TaxRate)
            .ToList();

        var expenseRates = companyData.Expenses
            .Where(e => e.Date >= startDate && e.Date <= endDate)
            .Select(e => e.TaxRate)
            .ToList();

        if (revenueRates.Count == 0 && expenseRates.Count == 0)
            return ([], [], []);

        var revGrouped = revenueRates.GroupBy(GetBracket).ToDictionary(g => g.Key, g => (double)g.Count());
        var expGrouped = expenseRates.GroupBy(GetBracket).ToDictionary(g => g.Key, g => (double)g.Count());

        // Only include brackets that have data
        var usedBrackets = allBrackets.Where(b => revGrouped.ContainsKey(b) || expGrouped.ContainsKey(b)).ToArray();

        var revPoints = usedBrackets.Select(b => new ChartDataPoint
        {
            Label = b,
            Value = revGrouped.GetValueOrDefault(b, 0)
        }).ToList();

        var expPoints = usedBrackets.Select(b => new ChartDataPoint
        {
            Label = b,
            Value = expGrouped.GetValueOrDefault(b, 0)
        }).ToList();

        return (revPoints, expPoints, usedBrackets);
    }

    /// <summary>
    /// Gets tax amounts grouped by product.
    /// </summary>
    public List<ChartDataPoint> GetTaxByProduct(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var allTransactions = new List<(decimal TaxUSD, string? ProductId, DateTime Date)>();

        allTransactions.AddRange(companyData.Revenues
            .Where(r => r.Date >= startDate && r.Date <= endDate && (r.TaxAmountUSD > 0 || r.TaxAmount > 0))
            .Select(r => (r.EffectiveTaxAmountUSD, r.LineItems.FirstOrDefault()?.ProductId, r.Date)));

        allTransactions.AddRange(companyData.Expenses
            .Where(e => e.Date >= startDate && e.Date <= endDate && (e.TaxAmountUSD > 0 || e.TaxAmount > 0))
            .Select(e => (e.EffectiveTaxAmountUSD, e.LineItems.FirstOrDefault()?.ProductId, e.Date)));

        if (allTransactions.Count == 0)
            return [];

        // When a display converter is supplied, convert each transaction's tax USD at its OWN date
        // before grouping (Calculations.md §3a Phase 2). Default (null) keeps USD.
        decimal Display(decimal amountUSD, DateTime date) => toDisplay != null ? toDisplay(amountUSD, date) : amountUSD;

        return allTransactions
            .GroupBy(t => t.ProductId ?? "Unknown")
            .Select(g =>
            {
                var productName = companyData.GetProduct(g.Key)?.Name ?? "Other";
                return new ChartDataPoint
                {
                    Label = productName,
                    Value = (double)g.Sum(t => Display(t.TaxUSD, t.Date))
                };
            })
            .OrderByDescending(p => p.Value)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Gets expense tax vs revenue tax over time as two series.
    /// </summary>
    public List<ChartSeriesData> GetExpenseVsRevenueTax(Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        if (companyData == null)
            return [];

        var (startDate, endDate) = GetDateRange();

        var allMonths = GetMonthsBetween(startDate, endDate).ToList();

        var monthsWithData = allMonths.Where(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;
            return companyData.Revenues.Any(r => r.Date >= clampedStart && r.Date <= clampedEnd && (r.TaxAmountUSD > 0 || r.TaxAmount > 0)) ||
                   companyData.Expenses.Any(e => e.Date >= clampedStart && e.Date <= clampedEnd && (e.TaxAmountUSD > 0 || e.TaxAmount > 0));
        }).ToList();

        if (monthsWithData.Count == 0)
            return [];

        var revenueTax = monthsWithData.Select(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = (double)companyData.Revenues
                    .Where(r => r.Date >= clampedStart && r.Date <= clampedEnd)
                    .Sum(r => toDisplay != null ? toDisplay(r.EffectiveTaxAmountUSD, r.Date) : r.EffectiveTaxAmountUSD),
                Date = month
            };
        }).ToList();

        var expenseTax = monthsWithData.Select(month =>
        {
            var monthStart = new DateTime(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var clampedStart = monthStart < startDate ? startDate : monthStart;
            var clampedEnd = monthEnd > endDate ? endDate : monthEnd;

            return new ChartDataPoint
            {
                Label = month.ToString("MMM yyyy"),
                Value = (double)companyData.Expenses
                    .Where(e => e.Date >= clampedStart && e.Date <= clampedEnd)
                    .Sum(e => toDisplay != null ? toDisplay(e.EffectiveTaxAmountUSD, e.Date) : e.EffectiveTaxAmountUSD),
                Date = month
            };
        }).ToList();

        return
        [
            new ChartSeriesData { Name = "Revenue Tax", Color = "#22C55E", DataPoints = revenueTax },
            new ChartSeriesData { Name = "Expense Tax", Color = "#EF4444", DataPoints = expenseTax }
        ];
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Determines the appropriate time bucket size based on the span of the date range.
    /// Returns day/week/month to keep chart data points in a readable range.
    /// </summary>
    public enum TimeBucket { Day, Week, Month, Year }

    public static TimeBucket GetTimeBucket(DateTime startDate, DateTime endDate)
    {
        var days = (endDate - startDate).TotalDays;
        return days switch
        {
            < 90 => TimeBucket.Day,
            < 365 => TimeBucket.Week,
            _ => TimeBucket.Month
        };
    }

    /// <summary>
    /// Groups a date into the appropriate bucket key based on the chosen time bucket.
    /// </summary>
    public static DateTime GetBucketKey(DateTime date, TimeBucket bucket)
    {
        return bucket switch
        {
            TimeBucket.Day => date.Date,
            TimeBucket.Week => date.Date.AddDays(-(int)date.DayOfWeek),
            TimeBucket.Month => new DateTime(date.Year, date.Month, 1),
            TimeBucket.Year => new DateTime(date.Year, 1, 1),
            _ => date.Date
        };
    }

    /// <summary>
    /// Formats a bucket key as a label appropriate for the time bucket.
    /// </summary>
    public static string GetBucketLabel(DateTime bucketKey, TimeBucket bucket)
    {
        return bucket switch
        {
            TimeBucket.Day => bucketKey.ToString("MMM dd"),
            TimeBucket.Week => bucketKey.ToString("MMM dd"),
            TimeBucket.Month => bucketKey.ToString("MMM yyyy"),
            TimeBucket.Year => bucketKey.ToString("yyyy"),
            _ => bucketKey.ToString("MMM dd")
        };
    }

    /// <summary>
    /// Re-buckets a list of daily ChartDataPoints into the specified bucket size by summing values.
    /// </summary>
    public static List<ChartDataPoint> RebucketSum(List<ChartDataPoint> dailyPoints, TimeBucket bucket)
    {
        return dailyPoints
            .Where(p => p.Date.HasValue)
            .GroupBy(p => GetBucketKey(p.Date!.Value, bucket))
            .OrderBy(g => g.Key)
            .Select(g => new ChartDataPoint
            {
                Label = GetBucketLabel(g.Key, bucket),
                Value = g.Sum(p => p.Value),
                Date = g.Key
            })
            .ToList();
    }

    /// <summary>
    /// Re-buckets a list of daily ChartSeriesData into the specified bucket size by summing values.
    /// </summary>
    public static List<ChartSeriesData> RebucketSeriesSum(List<ChartSeriesData> dailySeries, TimeBucket bucket)
    {
        return dailySeries.Select(series => new ChartSeriesData
        {
            Name = series.Name,
            Color = series.Color,
            DataPoints = RebucketSum(series.DataPoints, bucket)
        }).ToList();
    }

    /// <summary>
    /// Gets the months between two dates.
    /// Handles extreme dates (DateTime.MinValue/MaxValue) by clamping to reasonable ranges.
    /// </summary>
    private static IEnumerable<DateTime> GetMonthsBetween(DateTime startDate, DateTime endDate)
    {
        // Clamp dates to avoid DateTime overflow when using MinValue/MaxValue
        var minSafeDate = new DateTime(1900, 1, 1);
        var maxSafeDate = new DateTime(2100, 12, 31);

        if (startDate < minSafeDate) startDate = minSafeDate;
        if (endDate > maxSafeDate) endDate = maxSafeDate;
        if (startDate > endDate) yield break;

        var current = new DateTime(startDate.Year, startDate.Month, 1);
        var end = new DateTime(endDate.Year, endDate.Month, 1);

        // Safety limit to prevent infinite loops (max 1200 months = 100 years)
        var maxIterations = 1200;
        var iterations = 0;

        while (current <= end && iterations < maxIterations)
        {
            yield return current;
            current = current.AddMonths(1);
            iterations++;
        }
    }

    /// <summary>
    /// Gets chart data for a specific chart type.
    /// Results are cached per chart type for the lifetime of this service instance.
    /// </summary>
    public object GetChartData(ChartDataType chartType, Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        // When a display converter is supplied (dashboard distribution path), bypass the cache
        // entirely: a USD result computed earlier (e.g. for the report) must not be served when a
        // converter is requested, and a converted result must never poison the USD cache.
        if (toDisplay != null)
            return ComputeChartData(chartType, toDisplay);

        if (_cache.TryGetValue(chartType, out var cached))
            return cached;

        var result = ComputeChartData(chartType);
        _cache[chartType] = result;
        return result;
    }

    /// <summary>
    /// Computes chart data for a specific chart type.
    /// </summary>
    private object ComputeChartData(ChartDataType chartType, Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        return chartType switch
        {
            // Revenue charts
            ChartDataType.TotalRevenue => GetRevenueOverTime(),
            ChartDataType.RevenueDistribution => GetRevenueDistribution(toDisplay),

            // Expense charts
            ChartDataType.TotalExpenses => GetExpensesOverTime(),
            ChartDataType.ExpensesDistribution => GetExpenseDistribution(toDisplay),

            // Financial charts
            ChartDataType.TotalProfits => GetProfitOverTime(),
            ChartDataType.RevenueVsExpenses => GetRevenueVsExpenses(),

            // Transaction charts
            ChartDataType.AverageTransactionValue => GetAverageTransactionValueBySeries(toDisplay),
            ChartDataType.TotalTransactions => GetTransactionCountBySeries(),
            ChartDataType.AverageShippingCosts => GetAverageShippingCosts(toDisplay),

            // Geographic charts
            ChartDataType.WorldMap => GetWorldMapData(toDisplay),
            ChartDataType.CountriesOfOrigin => GetRevenueByCountryOfOrigin(toDisplay),
            ChartDataType.CountriesOfDestination => GetExpensesByCountryOfDestination(toDisplay),
            ChartDataType.CompaniesOfOrigin => GetRevenueByCompanyOfOrigin(toDisplay),
            ChartDataType.CompaniesOfDestination => GetRevenueByCompanyOfDestination(toDisplay),

            // Accountant charts
            ChartDataType.AccountantsTransactions => GetTransactionsByAccountant(),

            // Customer charts
            ChartDataType.TopCustomersByRevenue => GetTopCustomersByRevenue(toDisplay),
            ChartDataType.CustomerPaymentStatus => GetCustomerPaymentStatus(),
            ChartDataType.CustomerGrowth => GetCustomerGrowth(),
            ChartDataType.CustomerLifetimeValue => GetCustomerLifetimeValue(),
            ChartDataType.ActiveVsInactiveCustomers => GetActiveVsInactiveCustomers(),
            ChartDataType.RentalsPerCustomer => GetRentalsPerCustomer(),

            // Returns charts
            ChartDataType.ReturnsOverTime => GetReturnsOverTime(),
            ChartDataType.ReturnReasons => GetReturnReasons(),
            ChartDataType.ReturnFinancialImpact => GetReturnFinancialImpact(),
            ChartDataType.ReturnsByCategory => GetReturnsByCategory(),
            ChartDataType.ReturnsByProduct => GetReturnsByProduct(),
            ChartDataType.ExpenseVsRevenueReturns => GetExpenseVsRevenueReturns(),

            // Losses charts
            ChartDataType.LossesOverTime => GetLossesOverTime(),
            ChartDataType.LossReasons => GetLossReasons(),
            ChartDataType.LossFinancialImpact => GetLossFinancialImpact(),
            ChartDataType.LossesByCategory => GetLossesByCategory(),
            ChartDataType.LossesByProduct => GetLossesByProduct(),
            ChartDataType.ExpenseVsRevenueLosses => GetExpenseVsRevenueLosses(),

            // Taxes charts
            ChartDataType.TaxCollectedVsPaid => GetTaxCollectedVsPaid(toDisplay),
            ChartDataType.TaxLiabilityTrend => GetTaxLiabilityOverTime(),
            ChartDataType.TaxByCategory => GetTaxByCategory(toDisplay),
            ChartDataType.TaxRateDistribution => GetTaxRateDistribution(),
            ChartDataType.TaxByProduct => GetTaxByProduct(toDisplay),
            ChartDataType.ExpenseVsRevenueTax => GetExpenseVsRevenueTax(toDisplay),

            _ => new List<ChartDataPoint>()
        };
    }

    #endregion
}
