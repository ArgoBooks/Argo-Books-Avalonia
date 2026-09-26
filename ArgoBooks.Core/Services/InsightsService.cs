using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Insights;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for generating business insights using local statistical analysis and ML.
/// Uses ML.NET SSA and Holt-Winters for forecasting, with accuracy tracking.
/// </summary>
public class InsightsService(
    ILocalMLForecastingService mlForecastingService,
    IForecastAccuracyService forecastAccuracyService,
    Func<DateTime>? today = null)
    : IInsightsService
{
    // Minimum data requirements
    private const int MinimumTransactionsForInsights = 5;
    private const int MinimumMonthsForForecasting = 2;

    public InsightsService() : this(new LocalMLForecastingService(), new ForecastAccuracyService())
    {
    }

    private readonly Func<DateTime> _today = today ?? (() => DateTime.Today);

    // Anomaly detection thresholds
    private const double ZScoreThreshold = 2.0; // Standard deviations for anomaly
    private const decimal SignificantChangePercent = 15.0m; // % change to flag as significant

    #region Display Currency

    // The currency the displayed insight amounts are shown in, resolved once per analysis run at the
    // top of each worker (AnalyzeTrends/DetectAnomalies/GenerateRecommendations). "USD" means show the
    // stored USD figures unchanged. Analytics (percentages, z-scores, forecasting) always run in USD;
    // only the amounts that appear in a sentence are converted, via ToDisplay/SumDisplay below.
    private string _displayCode = "USD";

    /// <summary>
    /// Resolves the single currency insight amounts are shown in, mirroring the report rule
    /// (see AccountingReportDataService.ResolveDisplayCode): the company currency only when an
    /// exact-date USD-&gt;currency rate is available for every date we might convert; otherwise "USD"
    /// for the whole run, so a sentence never mixes currencies or shows a partial value.
    /// </summary>
    private void ResolveDisplayCode(CompanyData companyData) =>
        // The same dates reports check, and that opening a company and changing its currency preload.
        // Insights converts nothing dated after today, and a future date never has a rate, so one
        // future-dated invoice or purchase order would otherwise switch the whole run to USD.
        _displayCode = DisplayCurrency.Resolve(
            companyData.Settings.Localization.Currency,
            DisplayCurrency.ReportDates(companyData, null).Where(d => d.Date <= DateTime.Today));

    private decimal ToDisplay(decimal amountUSD, DateTime date) => DisplayCurrency.FromUSD(amountUSD, _displayCode, date);

    /// <summary>
    /// Sums per-item USD amounts after converting EACH at that item's OWN date, per the totals
    /// rule in docs/Calculations.md Rule 3a (never convert a pre-summed total at one date).
    /// Identical to summing the USD amounts directly for a USD run.
    /// </summary>
    private decimal SumDisplay<T>(IEnumerable<T> items, Func<T, decimal> amountUSD, Func<T, DateTime> date)
    {
        if (string.Equals(_displayCode, "USD", StringComparison.OrdinalIgnoreCase))
            return items.Sum(amountUSD);

        decimal total = 0m;
        foreach (var item in items)
            total += ToDisplay(amountUSD(item), date(item));
        return total;
    }

    #endregion

    /// <inheritdoc />
    public async Task<InsightsData> GenerateInsightsAsync(CompanyData companyData, AnalysisDateRange dateRange)
    {
        return await Task.Run(() =>
        {
            var insights = new InsightsData
            {
                GeneratedAt = DateTime.UtcNow
            };

            // Check for sufficient data
            var dataCheck = CheckDataSufficiency(companyData);
            if (!dataCheck.HasSufficientData)
            {
                insights.HasSufficientData = false;
                insights.InsufficientDataMessage = dataCheck.Message;
                return insights;
            }

            insights.HasSufficientData = true;

            // Generate all insight types
            insights.RevenueTrends = AnalyzeTrends(companyData, dateRange);
            insights.Anomalies = DetectAnomalies(companyData, dateRange);
            insights.Forecasts = GenerateForecastInsights(companyData, dateRange);
            insights.Recommendations = GenerateRecommendations(companyData, dateRange);
            insights.Forecast = GenerateForecast(companyData, dateRange);

            // Calculate summary
            insights.Summary = new InsightsSummary
            {
                TotalInsights = insights.RevenueTrends.Count + insights.Anomalies.Count +
                               insights.Forecasts.Count + insights.Recommendations.Count,
                TrendsDetected = insights.RevenueTrends.Count,
                AnomaliesDetected = insights.Anomalies.Count,
                Opportunities = insights.Recommendations.Count,
                MonthsOfData = dataCheck.MonthsOfData
            };

            return insights;
        });
    }

    /// <inheritdoc />
    public async Task<ForecastData> GenerateForecastAsync(CompanyData companyData, AnalysisDateRange dateRange)
    {
        return await Task.Run(() => GenerateForecast(companyData, dateRange));
    }

    #region Data Sufficiency Check

    private (bool HasSufficientData, string? Message, int MonthsOfData) CheckDataSufficiency(
        CompanyData companyData)
    {
        // For forecasting, we need historical data - not data from the future date range
        // Use all historical data (up to today) for sufficiency checks
        var today = DateTime.Today;
        var sales = companyData.Revenues.Where(s => s.Date <= today).ToList();
        var purchases = companyData.Expenses.Where(p => p.Date <= today).ToList();

        var totalTransactions = sales.Count + purchases.Count;

        if (totalTransactions < MinimumTransactionsForInsights)
        {
            return (false, $"Need at least {MinimumTransactionsForInsights} transactions for meaningful insights. Currently have {totalTransactions}.", 0);
        }

        // Calculate months of data
        var allDates = sales.Select(s => s.Date)
            .Concat(purchases.Select(p => p.Date))
            .ToList();

        if (!allDates.Any())
        {
            return (false, "No historical transaction data available.", 0);
        }

        var minDate = allDates.Min();
        var maxDate = allDates.Max();
        var monthsOfData = ((maxDate.Year - minDate.Year) * 12) + maxDate.Month - minDate.Month + 1;

        return (true, null, monthsOfData);
    }

    #endregion

    #region Trend Analysis

    internal List<InsightItem> AnalyzeTrends(CompanyData companyData, AnalysisDateRange dateRange)
    {
        var insights = new List<InsightItem>();
        ResolveDisplayCode(companyData);

        // For forecasting, use historical periods for trend analysis
        // Map future date ranges to equivalent historical periods
        var (currentPeriod, previousPeriod) = GetHistoricalAnalysisPeriods(dateRange);

        // Trend / anomaly analysis is cash-basis to match the dashboard:
        // unpaid invoices aren't real "sales activity" yet.
        var currentSales = companyData.Revenues
            .Where(s => s.Date >= currentPeriod.StartDate && s.Date <= currentPeriod.EndDate)
            .Where(RevenueAggregator.IsCollected)
            .ToList();

        var previousSales = companyData.Revenues
            .Where(s => s.Date >= previousPeriod.StartDate && s.Date <= previousPeriod.EndDate)
            .Where(RevenueAggregator.IsCollected)
            .ToList();

        var currentPurchases = companyData.Expenses
            .Where(p => p.Date >= currentPeriod.StartDate && p.Date <= currentPeriod.EndDate)
            .ToList();

        var previousPurchases = companyData.Expenses
            .Where(p => p.Date >= previousPeriod.StartDate && p.Date <= previousPeriod.EndDate)
            .ToList();

        // Revenue trend: display the gross figure the user recognises from
        // the Revenue stat card (Total, not pre-tax).
        var currentRevenue = currentSales.Sum(s => s.EffectiveTotalUSD);
        var previousRevenue = previousSales.Sum(s => s.EffectiveTotalUSD);

        if (previousRevenue > 0)
        {
            var revenueChange = CalculatePercentChange(previousRevenue, currentRevenue);

            if (Math.Abs(revenueChange) >= SignificantChangePercent)
            {
                var isGrowth = revenueChange > 0;
                // Convert each sale at its own date for the displayed figures; the percentage above
                // stays USD-based (currency-invariant).
                var previousRevenueDisplay = SumDisplay(previousSales, s => s.EffectiveTotalUSD, s => s.Date);
                var currentRevenueDisplay = SumDisplay(currentSales, s => s.EffectiveTotalUSD, s => s.Date);
                insights.Add(new InsightItem
                {
                    Title = isGrowth ? "Revenue Growth Detected" : "Revenue Decline Detected",
                    Description = $"Your revenue has {(isGrowth ? "increased" : "decreased")} by {Math.Abs(revenueChange):F1}% compared to the previous period ({FormatCurrency(previousRevenueDisplay)} → {FormatCurrency(currentRevenueDisplay)}).",
                    Recommendation = isGrowth
                        ? "Consider analyzing which products or services drove this growth to replicate success."
                        : "Review recent changes that may have impacted revenue and consider promotional strategies.",
                    Severity = isGrowth ? InsightSeverity.Success : InsightSeverity.Warning,
                    Category = InsightCategory.RevenueTrend,
                    MetricValue = currentRevenue,
                    PercentageChange = revenueChange
                });
            }
        }

        // Expense trend: display gross (Total), matching the Expenses stat card.
        var currentExpenses = currentPurchases.Sum(p => p.EffectiveTotalUSD);
        var previousExpenses = previousPurchases.Sum(p => p.EffectiveTotalUSD);

        if (previousExpenses > 0)
        {
            var expenseChange = CalculatePercentChange(previousExpenses, currentExpenses);

            if (Math.Abs(expenseChange) >= SignificantChangePercent)
            {
                var isIncrease = expenseChange > 0;
                var previousExpensesDisplay = SumDisplay(previousPurchases, p => p.EffectiveTotalUSD, p => p.Date);
                var currentExpensesDisplay = SumDisplay(currentPurchases, p => p.EffectiveTotalUSD, p => p.Date);
                insights.Add(new InsightItem
                {
                    Title = isIncrease ? "Expense Increase Detected" : "Expense Reduction Achieved",
                    Description = $"Your expenses have {(isIncrease ? "increased" : "decreased")} by {Math.Abs(expenseChange):F1}% compared to the previous period ({FormatCurrency(previousExpensesDisplay)} → {FormatCurrency(currentExpensesDisplay)}).",
                    Recommendation = isIncrease
                        ? "Review expense categories to identify areas where costs can be optimized."
                        : "Good job on cost management! Document what strategies worked for future reference.",
                    Severity = isIncrease ? InsightSeverity.Warning : InsightSeverity.Success,
                    Category = InsightCategory.ExpenseTrend,
                    MetricValue = currentExpenses,
                    PercentageChange = expenseChange
                });
            }
        }

        // Day-of-week pattern analysis
        var dayOfWeekInsight = AnalyzeDayOfWeekPattern(currentSales);
        if (dayOfWeekInsight != null)
        {
            insights.Add(dayOfWeekInsight);
        }

        // Seasonal pattern (if we have enough months)
        var seasonalInsight = AnalyzeSeasonalPattern(companyData.Revenues);
        if (seasonalInsight != null)
        {
            insights.Add(seasonalInsight);
        }

        var volumeTrendInsight = AnalyzeTransactionVolumeTrend(currentSales.Count, previousSales.Count);
        if (volumeTrendInsight != null)
        {
            insights.Add(volumeTrendInsight);
        }

        return insights;
    }

    private InsightItem? AnalyzeDayOfWeekPattern(List<Revenue> sales)
    {
        if (sales.Count < 14) return null;

        var salesByDay = sales
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => s.Date.DayOfWeek)
            .Select(g => new
            {
                Day = g.Key,
                Total = g.Sum(s => s.EffectiveTotalUSD),
                DisplayTotal = SumDisplay(g, s => s.EffectiveTotalUSD, s => s.Date),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Total)
            .ToList();

        if (!salesByDay.Any()) return null;

        var bestDay = salesByDay.First();
        var averageDaily = salesByDay.Average(x => x.Total);

        if (bestDay.Total > averageDaily * 1.3m) // 30% above average
        {
            var percentAbove = ((bestDay.Total / averageDaily) - 1) * 100;
            // Displayed amounts in the display currency; the percentage stays USD-based.
            var averageDailyDisplay = salesByDay.Average(x => x.DisplayTotal);
            return new InsightItem
            {
                Title = $"{bestDay.Day} Sales Performance",
                Description = $"{bestDay.Day}s generate {percentAbove:F0}% more revenue than average daily sales ({FormatCurrency(bestDay.DisplayTotal)} vs {FormatCurrency(averageDailyDisplay)} average).",
                Recommendation = $"Consider running promotions or increasing staffing on {bestDay.Day}s to maximize this opportunity.",
                Severity = InsightSeverity.Info,
                Category = InsightCategory.RevenueTrend,
                PercentageChange = percentAbove
            };
        }

        return null;
    }

    private InsightItem? AnalyzeSeasonalPattern(List<Revenue> allSales)
    {
        // Get monthly revenue data for ML analysis. Cash-basis + gross so it
        // matches the Revenue stat card / Revenue chart the user sees.
        var monthlyData = allSales
            .Where(s => s.Date >= DateTime.UtcNow.AddMonths(-12))
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => new { s.Date.Year, s.Date.Month })
            .OrderBy(g => g.Key.Year)
            .ThenBy(g => g.Key.Month)
            .Select(g => g.Sum(s => s.EffectiveTotalUSD))
            .ToList();

        if (monthlyData.Count < 6) return null;

        var seasonalPattern = mlForecastingService.DetectSeasonality(monthlyData);

        if (seasonalPattern.SeasonalStrength > 0.2) // Significant seasonal pattern detected
        {
            var cycleDescription = seasonalPattern.SeasonLength switch
            {
                12 => "yearly",
                6 => "semi-annual",
                4 => "quarterly",
                3 => "quarterly",
                _ => $"{seasonalPattern.SeasonLength}-month"
            };

            var strengthDescription = seasonalPattern.SeasonalStrength switch
            {
                >= 0.7 => "strong",
                >= 0.4 => "moderate",
                _ => "mild"
            };

            return new InsightItem
            {
                Title = "ML-Detected Seasonal Pattern",
                Description = $"Machine learning analysis detected a {strengthDescription} {cycleDescription} seasonal pattern ({seasonalPattern.SeasonalStrength:P0} strength). {seasonalPattern.Description}",
                Recommendation = "Use this pattern to optimize inventory timing, staffing schedules, and marketing campaigns.",
                Severity = InsightSeverity.Info,
                Category = InsightCategory.RevenueTrend,
                PercentageChange = (decimal)(seasonalPattern.SeasonalStrength * 100)
            };
        }

        // Fallback to simple analysis if no strong ML pattern detected
        var monthlyByMonth = allSales
            .Where(s => s.Date >= DateTime.UtcNow.AddMonths(-12))
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => s.Date.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(s => s.EffectiveTotalUSD) })
            .OrderByDescending(x => x.Total)
            .ToList();

        if (monthlyByMonth.Count < 6) return null;

        var bestMonth = monthlyByMonth.First();
        var averageMonthly = monthlyByMonth.Average(x => x.Total);

        if (bestMonth.Total > averageMonthly * 1.25m)
        {
            var monthName = new DateTime(2024, bestMonth.Month, 1).ToString("MMMM");
            var percentAbove = ((bestMonth.Total / averageMonthly) - 1) * 100;

            return new InsightItem
            {
                Title = "Seasonal Pattern Identified",
                Description = $"Historical data shows {monthName} generates {percentAbove:F0}% more revenue than average months.",
                Recommendation = $"Plan inventory and marketing campaigns ahead of {monthName} to capitalize on this seasonal trend.",
                Severity = InsightSeverity.Info,
                Category = InsightCategory.RevenueTrend,
                PercentageChange = percentAbove
            };
        }

        return null;
    }

    private InsightItem? AnalyzeTransactionVolumeTrend(int currentCount, int previousCount)
    {
        if (previousCount == 0) return null;

        var volumeChange = CalculatePercentChange(previousCount, currentCount);

        if (Math.Abs(volumeChange) >= 20) // 20% change in volume
        {
            var isIncrease = volumeChange > 0;
            return new InsightItem
            {
                Title = isIncrease ? "Transaction Volume Increasing" : "Transaction Volume Declining",
                Description = $"Number of transactions has {(isIncrease ? "increased" : "decreased")} by {Math.Abs(volumeChange):F0}% ({previousCount} → {currentCount} transactions).",
                Recommendation = isIncrease
                    ? "Ensure operational capacity can handle increased demand."
                    : "Consider outreach campaigns to re-engage customers.",
                Severity = isIncrease ? InsightSeverity.Success : InsightSeverity.Warning,
                Category = InsightCategory.RevenueTrend,
                PercentageChange = volumeChange
            };
        }

        return null;
    }

    #endregion

    #region Anomaly Detection

    private List<InsightItem> DetectAnomalies(CompanyData companyData, AnalysisDateRange dateRange)
    {
        var anomalies = new List<InsightItem>();
        ResolveDisplayCode(companyData);

        // Use historical date range for anomaly detection
        var historicalRange = GetHistoricalDateRange(dateRange);

        // Expense spike detection
        var expenseAnomaly = DetectExpenseAnomalies(companyData, historicalRange);
        if (expenseAnomaly != null)
        {
            anomalies.Add(expenseAnomaly);
        }

        var returnAnomaly = DetectReturnRateAnomaly(companyData, historicalRange);
        if (returnAnomaly != null)
        {
            anomalies.Add(returnAnomaly);
        }

        // Revenue anomaly (unusual drop or spike)
        var revenueAnomalies = DetectRevenueAnomalies(companyData, historicalRange);
        anomalies.AddRange(revenueAnomalies);

        // Large single transaction anomaly
        var largeTransactionAnomaly = DetectLargeTransactionAnomaly(companyData, historicalRange);
        if (largeTransactionAnomaly != null)
        {
            anomalies.Add(largeTransactionAnomaly);
        }

        return anomalies;
    }

    private InsightItem? DetectExpenseAnomalies(CompanyData companyData, AnalysisDateRange dateRange)
    {
        // Get weekly expense data for the past 12 weeks (gross, to match the
        // Expenses stat card the user sees).
        var twelveWeeksAgo = dateRange.EndDate.AddDays(-84);
        var weeklyGroups = companyData.Expenses
            .Where(p => p.Date >= twelveWeeksAgo && p.Date <= dateRange.EndDate)
            .GroupBy(p => GetWeekNumber(p.Date))
            .ToList();
        var weeklyExpenses = weeklyGroups.Select(g => g.Sum(p => p.EffectiveTotalUSD)).ToList();

        if (weeklyExpenses.Count < 4) return null;

        var currentWeekList = companyData.Expenses
            .Where(p => p.Date >= dateRange.EndDate.AddDays(-7) && p.Date <= dateRange.EndDate)
            .ToList();
        var currentWeekExpenses = currentWeekList.Sum(p => p.EffectiveTotalUSD);

        var stats = CalculateStatistics(weeklyExpenses.Select(x => (double)x).ToList());

        if (stats.StandardDeviation > 0)
        {
            var zScore = ((double)currentWeekExpenses - stats.Mean) / stats.StandardDeviation;

            if (zScore > ZScoreThreshold)
            {
                var percentAbove = ((currentWeekExpenses / (decimal)stats.Mean) - 1) * 100;
                // Both figures convert each expense at its own (cached) date: this week's total, and
                // the typical-week average taken over the same weekly buckets in display currency.
                var currentWeekDisplay = SumDisplay(currentWeekList, p => p.EffectiveTotalUSD, p => p.Date);
                var meanDisplay = weeklyGroups.Average(g => SumDisplay(g, p => p.EffectiveTotalUSD, p => p.Date));
                return new InsightItem
                {
                    Title = "Unusual Expense Spike Detected",
                    Description = $"This week's expenses ({FormatCurrency(currentWeekDisplay)}) are {percentAbove:F0}% above your typical weekly average ({FormatCurrency(meanDisplay)}).",
                    Recommendation = "Review recent expense entries for any errors, unexpected costs, or one-time purchases.",
                    Severity = InsightSeverity.Warning,
                    Category = InsightCategory.Anomaly,
                    MetricValue = currentWeekExpenses,
                    PercentageChange = percentAbove
                };
            }
        }

        return null;
    }

    private InsightItem? DetectReturnRateAnomaly(CompanyData companyData, AnalysisDateRange dateRange)
    {
        var currentReturns = companyData.Returns
            .Where(r => r.ReturnDate >= dateRange.StartDate && r.ReturnDate <= dateRange.EndDate)
            .ToList();

        var currentSales = companyData.Revenues
            .Where(s => s.Date >= dateRange.StartDate && s.Date <= dateRange.EndDate)
            .ToList();

        if (currentSales.Count < 10) return null;

        var currentReturnRate = currentSales.Count > 0
            ? (decimal)currentReturns.Count / currentSales.Count * 100
            : 0;

        // Get historical return rate (past 6 months)
        var sixMonthsAgo = dateRange.StartDate.AddMonths(-6);
        var historicalReturns = companyData.Returns
            .Count(r => r.ReturnDate >= sixMonthsAgo && r.ReturnDate < dateRange.StartDate);

        var historicalSales = companyData.Revenues
            .Count(s => s.Date >= sixMonthsAgo && s.Date < dateRange.StartDate);

        if (historicalSales < 10) return null;

        var historicalReturnRate = (decimal)historicalReturns / historicalSales * 100;

        if (currentReturnRate > historicalReturnRate + 3) // 3 percentage points higher
        {
            // Find most common return category
            var returnsByProduct = currentReturns
                .SelectMany(r => r.Items)
                .GroupBy(i => i.ProductId)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            var productInfo = returnsByProduct != null
                ? companyData.GetProduct(returnsByProduct.Key)
                : null;

            var categoryNote = productInfo != null
                ? $" Most returns are for: {productInfo.Name}."
                : "";

            return new InsightItem
            {
                Title = "Return Rate Above Normal",
                Description = $"Current return rate is {currentReturnRate:F1}% compared to historical average of {historicalReturnRate:F1}%.{categoryNote}",
                Recommendation = "Investigate product quality, description accuracy, or shipping issues for affected items.",
                Severity = InsightSeverity.Warning,
                Category = InsightCategory.Anomaly,
                MetricValue = currentReturnRate,
                PercentageChange = currentReturnRate - historicalReturnRate
            };
        }

        return null;
    }

    private List<InsightItem> DetectRevenueAnomalies(CompanyData companyData, AnalysisDateRange dateRange)
    {
        var anomalies = new List<InsightItem>();

        // Daily revenue analysis for short periods, weekly for longer
        var periodDays = dateRange.DayCount;
        var groupByWeek = periodDays > 30;

        var historicalStart = dateRange.StartDate.AddDays(-periodDays * 3); // 3x the period for baseline

        // Include the year in the day bucket so a baseline that spans a year boundary
        // doesn't collapse Jan 5 2024 and Jan 5 2025 into the same statistical bucket.
        // Use gross + IsCollected so anomaly detection matches dashboard signal.
        var historicalData = companyData.Revenues
            .Where(s => s.Date >= historicalStart && s.Date < dateRange.StartDate)
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => groupByWeek ? GetWeekNumber(s.Date) : s.Date.Year * 1000 + s.Date.DayOfYear)
            .Select(g => g.Sum(s => s.EffectiveTotalUSD))
            .ToList();

        if (historicalData.Count < 5) return anomalies;

        var stats = CalculateStatistics(historicalData.Select(x => (double)x).ToList());

        // Check current period data points
        var currentData = companyData.Revenues
            .Where(s => s.Date >= dateRange.StartDate && s.Date <= dateRange.EndDate)
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => groupByWeek ? GetWeekNumber(s.Date) : s.Date.Year * 1000 + s.Date.DayOfYear)
            .Select(g => new
            {
                Period = g.Key,
                Total = g.Sum(s => s.EffectiveTotalUSD),
                DisplayTotal = SumDisplay(g, s => s.EffectiveTotalUSD, s => s.Date)
            })
            .ToList();

        foreach (var period in currentData)
        {
            if (stats.StandardDeviation > 0)
            {
                var zScore = ((double)period.Total - stats.Mean) / stats.StandardDeviation;

                if (zScore < -ZScoreThreshold) // Unusually low
                {
                    var percentBelow = (1 - (period.Total / (decimal)stats.Mean)) * 100;
                    anomalies.Add(new InsightItem
                    {
                        Title = "Unusual Revenue Drop",
                        Description = $"Revenue for a recent period ({FormatCurrency(period.DisplayTotal)}) was {percentBelow:F0}% below typical levels.",
                        Recommendation = "Check for any operational issues, competitor activity, or external factors that may have affected sales.",
                        Severity = InsightSeverity.Critical,
                        Category = InsightCategory.Anomaly,
                        MetricValue = period.Total,
                        PercentageChange = -percentBelow
                    });
                    break; // Only report one
                }
            }
        }

        return anomalies;
    }

    private InsightItem? DetectLargeTransactionAnomaly(CompanyData companyData, AnalysisDateRange dateRange)
    {
        // Cash-basis transaction-size analysis: only count collected rows,
        // and use gross (Total) so the figure shown matches the Revenue page.
        var currentSales = companyData.Revenues
            .Where(s => s.Date >= dateRange.StartDate && s.Date <= dateRange.EndDate)
            .Where(RevenueAggregator.IsCollected)
            .ToList();

        if (currentSales.Count < 5) return null;

        var stats = CalculateStatistics(currentSales.Select(s => (double)s.EffectiveTotalUSD).ToList());

        var largestRevenue = currentSales.MaxBy(s => s.EffectiveTotalUSD)!;

        if (stats.StandardDeviation > 0)
        {
            var zScore = ((double)largestRevenue.EffectiveTotalUSD - stats.Mean) / stats.StandardDeviation;

            if (zScore > 3.0) // Very unusual
            {
                var customer = companyData.GetCustomer(largestRevenue.CustomerId ?? "");
                var customerName = customer?.Name ?? "a customer";

                // Both convert at real (cached) sale dates: the largest sale at its own date, and the
                // typical-size mean as the average of the sales converted at their own dates.
                var largestDisplay = ToDisplay(largestRevenue.EffectiveTotalUSD, largestRevenue.Date);
                var meanDisplay = currentSales.Average(s => ToDisplay(s.EffectiveTotalUSD, s.Date));
                return new InsightItem
                {
                    Title = "Unusually Large Transaction",
                    Description = $"A revenue transaction of {FormatCurrency(largestDisplay)} to {customerName} on {largestRevenue.Date:MMM d} is significantly larger than your typical transaction size ({FormatCurrency(meanDisplay)}).",
                    Recommendation = "Verify this transaction is correct and consider nurturing this high-value customer relationship.",
                    Severity = InsightSeverity.Info,
                    Category = InsightCategory.Anomaly,
                    MetricValue = largestRevenue.EffectiveTotalUSD
                };
            }
        }

        return null;
    }

    #endregion

    #region Forecasting

    private ForecastData GenerateForecast(CompanyData companyData, AnalysisDateRange dateRange)
    {
        var forecast = new ForecastData();

        // Calculate the forecast period multiplier based on the date range
        var periodMonths = GetForecastPeriodMonths(dateRange);
        forecast.PeriodMonths = periodMonths;

        // Get monthly data for forecasting. Revenue uses gross (Total) +
        // paid-only so the forecasted figure matches the Revenue stat card.
        // Expenses use gross (Total) to match the Expenses stat card. Note:
        // forecast.ForecastedProfit (computed as forecastedRevenue −
        // forecastedExpenses below) is an approximation of net profit,
        // strictly Net Profit uses pre-tax revenue per Calculations.md §2,
        // but exposing two parallel revenue forecasts adds little signal.
        var monthlyRevenue = GetMonthlyTotals(
            companyData.Revenues.Where(RevenueAggregator.IsCollected).ToList(),
            s => s.EffectiveTotalUSD);
        var monthlyExpenses = GetMonthlyTotals(companyData.Expenses, p => p.EffectiveTotalUSD);

        forecast.DataMonthsUsed = Math.Max(monthlyRevenue.Count, monthlyExpenses.Count);

        // Validate past forecasts and get accuracy data
        var accuracyData = forecastAccuracyService.GetAccuracyData(companyData);
        forecast.ValidatedForecastCount = accuracyData.ValidatedForecastCount;

        if (accuracyData.ValidatedForecastCount > 0)
        {
            forecast.HistoricalAccuracyPercent = accuracyData.AverageRevenueAccuracy;
            forecast.AccuracyDescription = accuracyData.AccuracyDescription;
        }

        // Get method accuracy for adaptive weighting
        var methodAccuracy = forecastAccuracyService.GetMethodAccuracies(companyData);

        // Revenue forecast using ML methods with adaptive weighting
        if (monthlyRevenue.Count >= MinimumMonthsForForecasting)
        {
            var periodsToForecast = Math.Max(1, (int)Math.Ceiling(periodMonths));
            var revenueForecast = mlForecastingService.GenerateEnhancedForecast(
                monthlyRevenue, periodsToForecast, ForecastMethod.Auto, methodAccuracy);

            // Scale by period months for partial months
            var scaleFactor = periodMonths / periodsToForecast;
            forecast.ForecastedRevenue = Math.Max(0, revenueForecast.ForecastedValues.Sum() * scaleFactor);
            forecast.ForecastedRevenueLower = Math.Max(0, revenueForecast.LowerBounds.Sum() * scaleFactor);
            forecast.ForecastedRevenueUpper = revenueForecast.UpperBounds.Sum() * scaleFactor;

            // Store seasonal pattern info. Require a full year of history (matching DetectSeasonality's
            // MinimumDataPointsForHoltWinters) before surfacing a seasonal narrative: with fewer points
            // Holt-Winters fits a 2-3 month "season" to month-to-month noise, so the Forecast card would
            // claim a pattern the trend-insight path (correctly) stays silent about.
            if (monthlyRevenue.Count >= 12 && revenueForecast.SeasonalPattern.SeasonalStrength > 0.1)
            {
                forecast.SeasonalInfo = new SeasonalPatternInfo
                {
                    HasSeasonalPattern = true,
                    Strength = revenueForecast.SeasonalPattern.SeasonalStrength,
                    Description = revenueForecast.SeasonalPattern.Description,
                    TrendDirection = revenueForecast.SeasonalPattern.Trend.ToString()
                };
            }

            forecast.ForecastMethod = revenueForecast.MethodUsed;

            // Calculate growth compared to historical average
            var historicalRevenue = GetScaledHistoricalValue(monthlyRevenue, periodMonths);
            if (historicalRevenue > 0)
            {
                forecast.RevenueGrowthPercent = CalculatePercentChange(historicalRevenue, forecast.ForecastedRevenue);
                forecast.RevenueGrowthPercentLower = CalculatePercentChange(historicalRevenue, forecast.ForecastedRevenueLower);
                forecast.RevenueGrowthPercentUpper = CalculatePercentChange(historicalRevenue, forecast.ForecastedRevenueUpper);
            }
        }

        // Expense forecast using ML methods with adaptive weighting
        if (monthlyExpenses.Count >= MinimumMonthsForForecasting)
        {
            var periodsToForecast = Math.Max(1, (int)Math.Ceiling(periodMonths));
            var expenseForecast = mlForecastingService.GenerateEnhancedForecast(
                monthlyExpenses, periodsToForecast, ForecastMethod.Auto, methodAccuracy);

            var scaleFactor = periodMonths / periodsToForecast;
            forecast.ForecastedExpenses = Math.Max(0, expenseForecast.ForecastedValues.Sum() * scaleFactor);
            forecast.ForecastedExpensesLower = Math.Max(0, expenseForecast.LowerBounds.Sum() * scaleFactor);
            forecast.ForecastedExpensesUpper = expenseForecast.UpperBounds.Sum() * scaleFactor;

            var historicalExpenses = GetScaledHistoricalValue(monthlyExpenses, periodMonths);
            if (historicalExpenses > 0)
            {
                forecast.ExpenseGrowthPercent = CalculatePercentChange(historicalExpenses, forecast.ForecastedExpenses);
                forecast.ExpenseGrowthPercentLower = CalculatePercentChange(historicalExpenses, forecast.ForecastedExpensesLower);
                forecast.ExpenseGrowthPercentUpper = CalculatePercentChange(historicalExpenses, forecast.ForecastedExpensesUpper);
            }
        }

        // Calculate profit forecast (point and bounds, conservative profit pairs low revenue with high expenses)
        forecast.ForecastedProfit = forecast.ForecastedRevenue - forecast.ForecastedExpenses;
        forecast.ForecastedProfitLower = forecast.ForecastedRevenueLower - forecast.ForecastedExpensesUpper;
        forecast.ForecastedProfitUpper = forecast.ForecastedRevenueUpper - forecast.ForecastedExpensesLower;

        // Calculate profit growth
        var historicalProfit = GetScaledHistoricalValue(monthlyRevenue, periodMonths) -
                               GetScaledHistoricalValue(monthlyExpenses, periodMonths);
        if (historicalProfit != 0)
        {
            forecast.ProfitGrowthPercent = CalculatePercentChange(historicalProfit, forecast.ForecastedProfit);
            forecast.ProfitGrowthPercentLower = CalculatePercentChange(historicalProfit, forecast.ForecastedProfitLower);
            forecast.ProfitGrowthPercentUpper = CalculatePercentChange(historicalProfit, forecast.ForecastedProfitUpper);
        }

        // Customer growth forecast using ML
        var monthlyNewCustomers = GetMonthlyNewCustomers(companyData);
        if (monthlyNewCustomers.Count >= MinimumMonthsForForecasting)
        {
            var periodsToForecast = Math.Max(1, (int)Math.Ceiling(periodMonths));
            var customerData = monthlyNewCustomers.Select(x => (decimal)x).ToList();
            var customerForecast = mlForecastingService.GenerateEnhancedForecast(
                customerData, periodsToForecast, ForecastMethod.HoltWinters);

            var scaleFactor = periodMonths / periodsToForecast;
            forecast.ExpectedNewCustomers = Math.Max(0, (int)Math.Round(customerForecast.ForecastedValues.Sum() * scaleFactor));
            forecast.ExpectedNewCustomersLower = Math.Max(0, (int)Math.Round(customerForecast.LowerBounds.Sum() * scaleFactor));
            forecast.ExpectedNewCustomersUpper = Math.Max(0, (int)Math.Round(customerForecast.UpperBounds.Sum() * scaleFactor));

            var historicalCustomers = GetScaledHistoricalValue(customerData, periodMonths);
            if (historicalCustomers > 0)
            {
                forecast.CustomerGrowthPercent = CalculatePercentChange(historicalCustomers, forecast.ExpectedNewCustomers);
                forecast.CustomerGrowthPercentLower = CalculatePercentChange(historicalCustomers, forecast.ExpectedNewCustomersLower);
                forecast.CustomerGrowthPercentUpper = CalculatePercentChange(historicalCustomers, forecast.ExpectedNewCustomersUpper);
            }
        }

        // Calculate confidence score using ML service
        forecast.ConfidenceScore = mlForecastingService.CalculateConfidenceScore(
            monthlyRevenue,
            forecast.SeasonalInfo != null ? new SeasonalPattern
            {
                SeasonalStrength = forecast.SeasonalInfo.Strength
            } : null,
            forecast.HistoricalAccuracyPercent
        );

        forecast.ConfidenceLevel = forecast.ConfidenceScore switch
        {
            >= 80 => "High",
            >= 50 => "Medium",
            _ => "Low"
        };

        // Save this forecast for future accuracy tracking
        var forecastPeriod = CalculateForecastPeriod(dateRange);
        forecastAccuracyService.SaveForecast(companyData, forecast, forecastPeriod);

        return forecast;
    }

    /// <summary>
    /// Calculates the date range for the forecast period (the future period being predicted).
    /// </summary>
    private static AnalysisDateRange CalculateForecastPeriod(AnalysisDateRange currentRange)
    {
        var today = DateTime.Today;

        // A future range, as the Insights page passes ("Next Month"), is itself the period forecast.
        if (currentRange.StartDate > today)
            return AnalysisDateRange.Custom(currentRange.StartDate, currentRange.EndDate);

        // A historical range forecasts the period after it
        var periodDays = currentRange.DayCount;

        // Start from today (or end of current range if it's in the past)
        var forecastStart = currentRange.EndDate < today
            ? today
            : currentRange.EndDate.AddDays(1);

        var forecastEnd = forecastStart.AddDays(periodDays - 1);

        return AnalysisDateRange.Custom(forecastStart, forecastEnd);
    }

    /// <summary>
    /// Gets a scaled historical value for comparison with a forecast period.
    /// Uses monthly average scaled by the period months for consistent comparison.
    /// </summary>
    private static decimal GetScaledHistoricalValue(List<decimal> monthlyData, decimal periodMonths)
    {
        if (monthlyData.Count == 0) return 0;

        // Always use average-based scaling for consistent comparison
        // This ensures fair comparison regardless of how many months of data we have
        var monthlyAverage = monthlyData.Average();
        return monthlyAverage * periodMonths;
    }

    /// <summary>
    /// Determines how many months the forecast period covers based on the date range.
    /// Returns a decimal for precise day-based scaling.
    /// </summary>
    private static decimal GetForecastPeriodMonths(AnalysisDateRange dateRange)
    {
        var periodDays = dateRange.DayCount;
        // Use 30.44 days per month (average days in a month)
        return periodDays / 30.44m;
    }

    private List<InsightItem> GenerateForecastInsights(CompanyData companyData, AnalysisDateRange dateRange)
    {
        var insights = new List<InsightItem>();

        // Use historical data range for forecast insights
        var historicalRange = GetHistoricalDateRange(dateRange);

        // Inventory depletion alert
        var inventoryAlerts = CheckInventoryDepletion(companyData, historicalRange);
        if (inventoryAlerts.Count > 0)
        {
            insights.Add(new InsightItem
            {
                Title = "Inventory Depletion Alert",
                Description = $"At current sales velocity, {inventoryAlerts.Count} {(inventoryAlerts.Count == 1 ? "product" : "products")} will reach reorder point within 2 weeks.",
                Recommendation = "Review and place orders for low-stock items: " + string.Join(", ", inventoryAlerts.Take(3)),
                Severity = InsightSeverity.Warning,
                Category = InsightCategory.Inventory,
                MetricValue = inventoryAlerts.Count
            });
        }

        return insights;
    }

    private List<string> CheckInventoryDepletion(CompanyData companyData, AnalysisDateRange dateRange)
    {
        var alerts = new List<string>();

        // Calculate average daily sales per product over the last 30 days
        var thirtyDaysAgo = dateRange.EndDate.AddDays(-30);
        var recentSales = companyData.Revenues
            .Where(s => s.Date >= thirtyDaysAgo && s.Date <= dateRange.EndDate)
            .SelectMany(s => s.LineItems)
            .GroupBy(li => li.ProductId)
            .Select(g => new { ProductId = g.Key, DailyVelocity = g.Sum(li => li.Quantity) / 30m })
            .ToList();

        // First inventory row per product, computed once, so the velocity loop below doesn't
        // rescan all Inventory for each product.
        var inventoryByProductId = companyData.Inventory
            .Where(i => !string.IsNullOrEmpty(i.ProductId))
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var item in recentSales.Where(x => x.DailyVelocity > 0))
        {
            var inventory = !string.IsNullOrEmpty(item.ProductId)
                && inventoryByProductId.TryGetValue(item.ProductId, out var inv) ? inv : null;
            if (inventory != null && inventory.InStock > 0)
            {
                var daysUntilEmpty = inventory.InStock / item.DailyVelocity;
                if (daysUntilEmpty <= 14) // 2 weeks or less
                {
                    var product = companyData.GetProduct(item.ProductId ?? "");
                    if (product != null)
                    {
                        alerts.Add(product.Name);
                    }
                }
            }
        }

        return alerts;
    }

    #endregion

    #region Recommendations

    internal List<InsightItem> GenerateRecommendations(CompanyData companyData, AnalysisDateRange dateRange)
    {
        var recommendations = new List<InsightItem>();
        ResolveDisplayCode(companyData);

        // Use historical date range for recommendations
        var historicalRange = GetHistoricalDateRange(dateRange);

        // Top performing product
        var topProductRec = AnalyzeTopProducts(companyData, historicalRange);
        if (topProductRec != null)
        {
            recommendations.Add(topProductRec);
        }

        var inactiveCustomerRec = AnalyzeInactiveCustomers(companyData, historicalRange);
        if (inactiveCustomerRec != null)
        {
            recommendations.Add(inactiveCustomerRec);
        }

        var overdueRec = AnalyzeOverdueInvoices(companyData);
        if (overdueRec != null)
        {
            recommendations.Add(overdueRec);
        }

        var supplierRec = AnalyzeSupplierOptimization(companyData, historicalRange);
        if (supplierRec != null)
        {
            recommendations.Add(supplierRec);
        }

        // Customer concentration risk
        var concentrationRec = AnalyzeCustomerConcentration(companyData, historicalRange);
        if (concentrationRec != null)
        {
            recommendations.Add(concentrationRec);
        }

        return recommendations;
    }

    private InsightItem? AnalyzeTopProducts(CompanyData companyData, AnalysisDateRange dateRange)
    {
        // Use USD-converted amounts by applying each transaction's conversion ratio to its line items.
        // RevenueDisplay accumulates the same allocation converted to the display currency at each
        // transaction's own date, so the shown figure is display-currency without disturbing the
        // USD-based margin ordering.
        var productSalesData = new Dictionary<string, (decimal Revenue, decimal RevenueDisplay, decimal Cost, decimal Quantity)>();

        // Cash-basis: only collected revenue contributes to top-product analysis.
        foreach (var s in companyData.Revenues
                     .Where(s => s.Date >= dateRange.StartDate && s.Date <= dateRange.EndDate)
                     .Where(RevenueAggregator.IsCollected))
        {
            if (s.LineItems.Count == 0) continue;
            var lineItemsTotal = s.LineItems.Sum(li => li.Subtotal);
            // Weight by each line's pre-tax subtotal, but distribute the GROSS (tax-inclusive) USD
            // total, matching docs/Calculations.md §13 and ProductSalesService so per-product revenue
            // agrees across the Insights, Analytics, and Report surfaces.
            var grossUSD = s.EffectiveTotalUSD;

            foreach (var li in s.LineItems)
            {
                var revenueUSD = lineItemsTotal != 0
                    ? Math.Round(li.Subtotal / lineItemsTotal * grossUSD, 2)
                    : 0;
                var revenueDisplay = ToDisplay(revenueUSD, s.Date);
                var costProduct = companyData.GetProduct(li.ProductId ?? "");
                var costUSD = costProduct == null ? 0 : li.Quantity * InventoryStockService.CostPriceUSD(companyData, costProduct, s.Date);

                var pid = li.ProductId ?? "";
                if (!productSalesData.ContainsKey(pid))
                    productSalesData[pid] = (0, 0, 0, 0);
                var current = productSalesData[pid];
                productSalesData[pid] = (current.Revenue + revenueUSD, current.RevenueDisplay + revenueDisplay, current.Cost + costUSD, current.Quantity + li.Quantity);
            }
        }

        var productSales = productSalesData
            .Select(kvp => new { ProductId = kvp.Key, kvp.Value.Revenue, kvp.Value.RevenueDisplay, kvp.Value.Cost,
                kvp.Value.Quantity })
            .ToList();

        if (!productSales.Any()) return null;

        var topProduct = productSales
            .Where(p => p.Cost > 0 && p.Revenue > 0)
            .Select(p => new { p.ProductId, p.Revenue, p.RevenueDisplay, Margin = (p.Revenue - p.Cost) / p.Revenue * 100 })
            .OrderByDescending(p => p.Margin)
            .FirstOrDefault();

        if (topProduct == null) return null;

        var product = companyData.GetProduct(topProduct.ProductId ?? "");
        if (product == null) return null;

        return new InsightItem
        {
            Title = "Top Performing Product",
            Description = $"\"{product.Name}\" has the highest profit margin at {topProduct.Margin:F0}%. Revenue this period: {FormatCurrency(topProduct.RevenueDisplay)}.",
            Recommendation = "Consider featuring this product more prominently in marketing or bundling it with other items.",
            Severity = InsightSeverity.Info,
            Category = InsightCategory.Product,
            MetricValue = topProduct.Revenue,
            PercentageChange = topProduct.Margin
        };
    }

    private InsightItem? AnalyzeInactiveCustomers(CompanyData companyData, AnalysisDateRange dateRange)
    {
        var inactivityThreshold = 60; // days

        // Group Revenues by customer once, capturing both the last purchase date and the
        // purchase count, so the "previously active" check below is a filter on this list
        // instead of re-scanning all Revenues per inactive customer.
        var purchasesByCustomer = companyData.Revenues
            .GroupBy(s => s.CustomerId)
            .Select(g => new { LastPurchase = g.Max(s => s.Date), PurchaseCount = g.Count() })
            .ToList();

        // Only flag customers who were previously active (at least 2 purchases in the past)
        // and haven't purchased within the inactivity threshold.
        var previouslyActiveCount = purchasesByCustomer.Count(c =>
            (dateRange.EndDate - c.LastPurchase).TotalDays > inactivityThreshold && c.PurchaseCount >= 2);

        if (previouslyActiveCount == 0) return null;

        return new InsightItem
        {
            Title = "Customer Retention Opportunity",
            Description = $"{previouslyActiveCount} previously active {(previouslyActiveCount == 1 ? "customer hasn't" : "customers haven't")} made a purchase in over {inactivityThreshold} days.",
            Recommendation = "Consider sending re-engagement emails, special offers, or conducting a satisfaction survey.",
            Severity = InsightSeverity.Info,
            Category = InsightCategory.Customer,
            MetricValue = previouslyActiveCount
        };
    }

    private InsightItem? AnalyzeOverdueInvoices(CompanyData companyData)
    {
        var overdueInvoices = companyData.Invoices
            .Where(i => i.IsOverdue)
            .OrderByDescending(i => (DateTime.Today - i.DueDate).TotalDays)
            .ToList();

        if (!overdueInvoices.Any()) return null;

        var totalOverdue = overdueInvoices.Sum(i => i.EffectiveBalanceUSD);
        // At least 1: IsOverdue means the due date is before today.
        var oldestDaysOverdue = (DateTime.Today - overdueInvoices.First().DueDate.Date).Days;

        // Each balance at its invoice's issue date, as the Overdue Invoices card converts it.
        var totalOverdueDisplay = SumDisplay(overdueInvoices, i => i.EffectiveBalanceUSD, i => i.IssueDate);

        return new InsightItem
        {
            Title = "Payment Collection Needed",
            Description = $"{overdueInvoices.Count} {(overdueInvoices.Count == 1 ? "invoice" : "invoices")} totaling {FormatCurrency(totalOverdueDisplay)} {(overdueInvoices.Count == 1 ? "is" : "are")} overdue. Oldest is {oldestDaysOverdue} days past due.",
            Recommendation = "Send payment reminders and follow up with these customers to improve cash flow.",
            Severity = oldestDaysOverdue > 30 ? InsightSeverity.Warning : InsightSeverity.Info,
            Category = InsightCategory.Payment,
            MetricValue = totalOverdue
        };
    }

    private InsightItem? AnalyzeSupplierOptimization(CompanyData companyData, AnalysisDateRange dateRange)
    {
        // Analyze purchase patterns by supplier, gross (Total) to match the
        // Expenses stat card and Expenses Over Time chart.
        var supplierPurchases = companyData.Expenses
            .Where(p => p.Date >= dateRange.StartDate && p.Date <= dateRange.EndDate)
            .GroupBy(p => p.SupplierId)
            .Select(g => new
            {
                SupplierId = g.Key,
                TotalSpent = g.Sum(p => p.EffectiveTotalUSD),
                TotalSpentDisplay = SumDisplay(g, p => p.EffectiveTotalUSD, p => p.Date),
                Count = g.Count(),
                AvgPerPurchase = g.Average(p => p.EffectiveTotalUSD)
            })
            .OrderByDescending(s => s.TotalSpent)
            .ToList();

        if (supplierPurchases.Count < 2) return null;

        var topSupplier = supplierPurchases.First();
        var supplier = companyData.GetSupplier(topSupplier.SupplierId ?? "");

        if (supplier == null) return null;

        var concentrationPercent = supplierPurchases.Sum(s => s.TotalSpent) > 0
            ? topSupplier.TotalSpent / supplierPurchases.Sum(s => s.TotalSpent) * 100
            : 0;

        if (concentrationPercent > 60) // More than 60% with one supplier
        {
            return new InsightItem
            {
                Title = "Supplier Concentration Risk",
                Description = $"{concentrationPercent:F0}% of your purchases ({FormatCurrency(topSupplier.TotalSpentDisplay)}) are from {supplier.Name}.",
                Recommendation = "Consider diversifying suppliers to reduce risk and potentially negotiate better terms.",
                Severity = InsightSeverity.Info,
                Category = InsightCategory.Recommendation,
                PercentageChange = concentrationPercent
            };
        }

        return null;
    }

    private InsightItem? AnalyzeCustomerConcentration(CompanyData companyData, AnalysisDateRange dateRange)
    {
        var customerRevenue = companyData.Revenues
            .Where(s => s.Date >= dateRange.StartDate && s.Date <= dateRange.EndDate)
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => s.CustomerId)
            .Select(g => new { CustomerId = g.Key, Revenue = g.Sum(s => s.EffectiveTotalUSD) })
            .OrderByDescending(c => c.Revenue)
            .ToList();

        if (customerRevenue.Count < 3) return null;

        var totalRevenue = customerRevenue.Sum(c => c.Revenue);
        if (totalRevenue == 0) return null;

        var topCustomer = customerRevenue.First();
        var concentrationPercent = topCustomer.Revenue / totalRevenue * 100;

        if (concentrationPercent > 40) // More than 40% from one customer
        {
            var customer = companyData.GetCustomer(topCustomer.CustomerId ?? "");
            var customerName = customer?.Name ?? "your top customer";

            return new InsightItem
            {
                Title = "Revenue Concentration Risk",
                Description = $"{concentrationPercent:F0}% of revenue comes from {customerName}. This creates business risk if that relationship changes.",
                Recommendation = "Work on diversifying your customer base through acquisition and marketing efforts.",
                Severity = InsightSeverity.Warning,
                Category = InsightCategory.Customer,
                PercentageChange = concentrationPercent
            };
        }

        return null;
    }

    #endregion

    #region Helper Methods

    private List<decimal> GetMonthlyTotals<T>(List<T> transactions, Func<T, decimal> amountSelector)
        where T : Transaction
    {
        var cutoff = DateTime.UtcNow.AddMonths(-24);
        var monthlyAmounts = transactions
            .Where(t => t.Date >= cutoff)
            .GroupBy(t => new DateTime(t.Date.Year, t.Date.Month, 1))
            .ToDictionary(g => g.Key, g => g.Sum(amountSelector));

        if (monthlyAmounts.Count == 0)
            return new List<decimal>();

        var startMonth = monthlyAmounts.Keys.Min();
        var endMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        return InterpolateMonthlyGaps(monthlyAmounts, startMonth, endMonth);
    }

    /// <summary>
    /// Builds a continuous monthly series, interpolating gaps instead of zero-filling.
    /// Interior gaps are linearly interpolated; leading/trailing gaps use nearest known value.
    /// </summary>
    internal static List<decimal> InterpolateMonthlyGaps(Dictionary<DateTime, decimal> knownValues, DateTime startMonth, DateTime endMonth)
    {
        // First pass: collect months with null for gaps
        var values = new List<decimal?>();
        var current = startMonth;
        while (current <= endMonth)
        {
            values.Add(knownValues.TryGetValue(current, out var amount) ? amount : null);
            current = current.AddMonths(1);
        }

        // Second pass: interpolate gaps
        var result = new List<decimal>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i].HasValue)
            {
                result.Add(values[i]!.Value);
                continue;
            }

            // Find previous known value
            decimal? prevVal = null;
            int prevIdx = -1;
            for (int j = i - 1; j >= 0; j--)
            {
                if (values[j].HasValue) { prevVal = values[j]!.Value; prevIdx = j; break; }
            }

            // Find next known value
            decimal? nextVal = null;
            int nextIdx = -1;
            for (int j = i + 1; j < values.Count; j++)
            {
                if (values[j].HasValue) { nextVal = values[j]!.Value; nextIdx = j; break; }
            }

            if (prevVal.HasValue && nextVal.HasValue)
            {
                // Interior gap: linear interpolation
                var fraction = (decimal)(i - prevIdx) / (nextIdx - prevIdx);
                result.Add(prevVal.Value + fraction * (nextVal.Value - prevVal.Value));
            }
            else if (prevVal.HasValue)
            {
                // Trailing gap: carry forward
                result.Add(prevVal.Value);
            }
            else if (nextVal.HasValue)
            {
                // Leading gap: carry backward
                result.Add(nextVal.Value);
            }
            else
            {
                result.Add(0m); // No known values at all (shouldn't happen)
            }
        }

        return result;
    }

    private List<int> GetMonthlyNewCustomers(CompanyData companyData)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-24);
        var firstPurchaseByCustomer = companyData.Revenues
            .GroupBy(s => s.CustomerId)
            .Select(g => new { CustomerId = g.Key, FirstPurchase = g.Min(s => s.Date) })
            .ToList();

        var monthlyCounts = firstPurchaseByCustomer
            .Where(c => c.FirstPurchase >= cutoff)
            .GroupBy(c => new DateTime(c.FirstPurchase.Year, c.FirstPurchase.Month, 1))
            .ToDictionary(g => g.Key, g => g.Count());

        if (monthlyCounts.Count == 0)
            return new List<int>();

        // Build a continuous monthly series with zero-fill for missing months
        var startMonth = monthlyCounts.Keys.Min();
        var endMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        var result = new List<int>();
        var current = startMonth;
        while (current <= endMonth)
        {
            result.Add(monthlyCounts.TryGetValue(current, out var count) ? count : 0);
            current = current.AddMonths(1);
        }
        return result;
    }

    private static decimal CalculatePercentChange(decimal oldValue, decimal newValue)
    {
        if (oldValue == 0) return newValue > 0 ? 100 : 0;
        var change = (newValue - oldValue) / Math.Abs(oldValue) * 100;
        // Cap at ±999% to prevent absurd values from display issues
        return Math.Max(-999, Math.Min(999, change));
    }

    private static (double Mean, double StandardDeviation) CalculateStatistics(List<double> values)
    {
        if (!values.Any()) return (0, 0);

        var mean = values.Average();
        var variance = values.Select(x => Math.Pow(x - mean, 2)).Average();
        var stdDev = Math.Sqrt(variance);

        return (mean, stdDev);
    }

    private static int GetWeekNumber(DateTime date)
    {
        // ISO 8601 weeks are always a full 7 days. GetYear is the ISO week-year so the
        // key stays consistent for dates that ISO-belong to an adjacent calendar year.
        return System.Globalization.ISOWeek.GetYear(date) * 100 + System.Globalization.ISOWeek.GetWeekOfYear(date);
    }

    private string FormatCurrency(decimal amount)
    {
        // The amount is already in _displayCode (converted at production via ToDisplay/SumDisplay).
        // Whole units (N0) to keep the narrative style these sentences have always used, with the
        // display currency's own symbol, invariantly so the output doesn't depend on the machine locale.
        return CurrencyInfo.GetByCode(_displayCode).Symbol
             + amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The current and previous periods a trend compares, by the same rule as the dashboard, read
    /// from the range's preset name. A future range ("Next Month") maps to this month, quarter or
    /// year so far first, so a part-period is never measured against a whole one.
    /// </summary>
    private (AnalysisDateRange Current, AnalysisDateRange Previous) GetHistoricalAnalysisPeriods(AnalysisDateRange dateRange)
    {
        var today = _today();
        (DateRangePreset? Preset, AnalysisDateRange Range) current = dateRange.StartDate > today
            ? GetPeriodSoFar(dateRange, today)
            : (DateRangePresetExtensions.ParseDateRange(dateRange.PresetName), dateRange);

        var (previousStart, previousEnd) = ComparisonPeriod.For(current.Preset, current.Range.StartDate, current.Range.EndDate);
        return (current.Range, AnalysisDateRange.Custom(previousStart, previousEnd));
    }

    /// <summary>
    /// Gets the historical date range to use for analysis when given a future forecast period.
    /// </summary>
    private AnalysisDateRange GetHistoricalDateRange(AnalysisDateRange dateRange)
    {
        var today = _today();
        return dateRange.StartDate > today ? GetPeriodSoFar(dateRange, today).Range : dateRange;
    }

    /// <summary>
    /// This month, quarter or year, whichever is nearest the future range's length, up to the end of
    /// today, since transactions carry a time of day.
    /// </summary>
    private static (DateRangePreset Preset, AnalysisDateRange Range) GetPeriodSoFar(AnalysisDateRange futureRange, DateTime today)
    {
        var periodMonths = Math.Max(1, (int)Math.Round(futureRange.DayCount / 30.0));
        var (preset, start) = periodMonths switch
        {
            <= 1 => (DateRangePreset.ThisMonth, new DateTime(today.Year, today.Month, 1)),
            <= 3 => (DateRangePreset.ThisQuarter, new DateTime(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1)),
            _ => (DateRangePreset.ThisYear, new DateTime(today.Year, 1, 1))
        };
        return (preset, AnalysisDateRange.Custom(start, today.Date.AddDays(1).AddTicks(-1)));
    }

    #endregion
}
