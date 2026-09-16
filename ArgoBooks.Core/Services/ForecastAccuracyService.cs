using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Insights;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for tracking and comparing forecast accuracy over time.
/// Stores historical forecasts and validates them against actual outcomes.
/// </summary>
public class ForecastAccuracyService : IForecastAccuracyService
{
    /// <summary>
    /// Increment this when the forecasting algorithm changes to trigger a full re-backtest.
    /// </summary>
    private const string CurrentBacktestVersion = "v3-gross-collected";
    /// <inheritdoc />
    public void SaveForecast(CompanyData companyData, ForecastData forecast, AnalysisDateRange forecastPeriod)
    {
        // Matched on days: a period saved by an older build ends at 23:59:59, not the day's last tick.
        var existingRecord = companyData.ForecastRecords.FirstOrDefault(r =>
            r.PeriodStartDate.Date == forecastPeriod.StartDate.Date &&
            r.PeriodEndDate.Date == forecastPeriod.EndDate.Date &&
            !r.IsValidated);

        if (existingRecord != null)
        {
            // Update existing unvalidated record
            existingRecord.PeriodStartDate = forecastPeriod.StartDate;
            existingRecord.PeriodEndDate = forecastPeriod.EndDate;
            existingRecord.ForecastedRevenue = forecast.ForecastedRevenue;
            existingRecord.ForecastedExpenses = forecast.ForecastedExpenses;
            existingRecord.ForecastedProfit = forecast.ForecastedProfit;
            existingRecord.ForecastedNewCustomers = forecast.ExpectedNewCustomers;
            existingRecord.ConfidenceScore = forecast.ConfidenceScore;
            existingRecord.ForecastDate = DateTime.Now;
        }
        else
        {
            var record = new ForecastAccuracyRecord
            {
                ForecastDate = DateTime.Now,
                PeriodStartDate = forecastPeriod.StartDate,
                PeriodEndDate = forecastPeriod.EndDate,
                ForecastedRevenue = forecast.ForecastedRevenue,
                ForecastedExpenses = forecast.ForecastedExpenses,
                ForecastedProfit = forecast.ForecastedProfit,
                ForecastedNewCustomers = forecast.ExpectedNewCustomers,
                ConfidenceScore = forecast.ConfidenceScore,
                ForecastMethod = forecast.ForecastMethod ?? "Combined",
                IsValidated = false
            };

            companyData.ForecastRecords.Add(record);
        }

        companyData.MarkAsModified();
    }

    /// <inheritdoc />
    public void ValidatePastForecasts(CompanyData companyData)
    {
        var today = DateTime.Today;
        var unvalidatedRecords = companyData.ForecastRecords
            .Where(r => !r.IsValidated && r.PeriodEndDate < today)
            .ToList();

        foreach (var record in unvalidatedRecords)
        {
            // Calculate actual values for the forecast period
            // Measure "actual" the same way InsightsService generates the forecast: gross
            // (EffectiveTotalUSD), and collected-only for revenue (RevenueAggregator.IsCollected).
            // Using pre-tax amounts or counting uncollected revenue made the accuracy score compare
            // two different yardsticks.
            var actualRevenue = RevenueAggregator.SumCollectedRevenueUSD(companyData.Revenues, record.PeriodStartDate, record.PeriodEndDate);
            var actualExpenses = ExpenseAggregator.SumExpensesUSD(companyData.Expenses, record.PeriodStartDate, record.PeriodEndDate);

            var actualProfit = actualRevenue - actualExpenses;

            // Calculate actual new customers (first purchase within the period)
            var firstPurchaseByCustomer = companyData.Revenues
                .GroupBy(s => s.CustomerId)
                .Select(g => new { CustomerId = g.Key, FirstPurchase = g.Min(s => s.Date) })
                .ToList();

            var actualNewCustomers = firstPurchaseByCustomer
                .Count(c => c.FirstPurchase >= record.PeriodStartDate && c.FirstPurchase <= record.PeriodEndDate);

            // Update the record with actual values
            record.ActualRevenue = actualRevenue;
            record.ActualExpenses = actualExpenses;
            record.ActualProfit = actualProfit;
            record.ActualNewCustomers = actualNewCustomers;
            record.IsValidated = true;
        }

        if (unvalidatedRecords.Any())
        {
            companyData.MarkAsModified();
        }
    }

    /// <inheritdoc />
    public ForecastAccuracyData GetAccuracyData(CompanyData companyData)
    {
        // First, validate any past forecasts that haven't been validated yet
        ValidatePastForecasts(companyData);

        var accuracyData = new ForecastAccuracyData
        {
            HistoricalRecords = companyData.ForecastRecords
                .OrderByDescending(r => r.PeriodStartDate)
                .ToList()
        };

        accuracyData.CalculateStatistics();

        return accuracyData;
    }

    /// <inheritdoc />
    public (double RevenueAccuracy, double ExpenseAccuracy)? GetRecentAccuracy(CompanyData companyData, int recentCount = 6)
    {
        var validatedRecords = companyData.ForecastRecords
            .Where(r => r.IsValidated)
            .OrderByDescending(r => r.PeriodEndDate)
            .Take(recentCount)
            .ToList();

        if (validatedRecords.Count == 0)
            return null;

        var revenueAccuracies = validatedRecords
            .Where(r => r.RevenueAccuracyPercent.HasValue)
            .Select(r => r.RevenueAccuracyPercent!.Value)
            .ToList();

        var expenseAccuracies = validatedRecords
            .Where(r => r.ExpensesAccuracyPercent.HasValue)
            .Select(r => r.ExpensesAccuracyPercent!.Value)
            .ToList();

        if (!revenueAccuracies.Any() && !expenseAccuracies.Any())
            return null;

        return (
            revenueAccuracies.Any() ? revenueAccuracies.Average() : 0,
            expenseAccuracies.Any() ? expenseAccuracies.Average() : 0
        );
    }

    /// <inheritdoc />
    public string GetAccuracySummary(CompanyData companyData)
    {
        var recentAccuracy = GetRecentAccuracy(companyData);
        if (!recentAccuracy.HasValue)
        {
            return "No validated forecasts yet. Check back after the current forecast period ends.";
        }

        var validatedCount = companyData.ForecastRecords.Count(r => r.IsValidated);
        var avgAccuracy = (recentAccuracy.Value.RevenueAccuracy + recentAccuracy.Value.ExpenseAccuracy) / 2;
        var errorMargin = 100 - avgAccuracy;

        return $"Based on {validatedCount} validated {(validatedCount == 1 ? "forecast" : "forecasts")}, predictions were within ±{errorMargin:F0}% of actual values on average.";
    }

    /// <inheritdoc />
    public void CleanupOldRecords(CompanyData companyData, int maxRecords = 24)
    {
        // Keep only the most recent records, validated ones get priority
        var toKeep = companyData.ForecastRecords
            .OrderByDescending(r => r.IsValidated ? 1 : 0)
            .ThenByDescending(r => r.PeriodStartDate)
            .Take(maxRecords)
            .ToList();

        if (companyData.ForecastRecords.Count > toKeep.Count)
        {
            companyData.ForecastRecords.Clear();
            companyData.ForecastRecords.AddRange(toKeep);
            companyData.MarkAsModified();
        }
    }

    /// <inheritdoc />
    public bool ShouldRunBacktest(CompanyData companyData, CompanySettings settings, int minMonths = 4)
    {
        // Get all months with transaction data
        var allDates = companyData.Revenues.Select(s => s.Date)
            .Concat(companyData.Expenses.Select(p => p.Date))
            .ToList();

        if (allDates.Count == 0)
            return false;

        var distinctMonths = allDates
            .Select(d => new DateTime(d.Year, d.Month, 1))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // Need at least minMonths of data
        if (distinctMonths.Count < minMonths)
            return false;

        // Get the newest month with data
        var newestMonth = distinctMonths.Last();
        var newestMonthStr = newestMonth.ToString("yyyy-MM");

        // If never backtested, run backtest
        if (string.IsNullOrEmpty(settings.LastBacktestedMonth))
            return true;

        // If newest data is newer than last backtest, run incremental backtest
        return string.Compare(newestMonthStr, settings.LastBacktestedMonth, StringComparison.Ordinal) > 0;
    }

    /// <inheritdoc />
    public async Task<int> RunBacktestAsync(
        CompanyData companyData,
        CompanySettings settings,
        ILocalMLForecastingService mlService,
        int minTrainingMonths = 3,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        return await Task.Run(() =>
        {
            // Check if algorithm version changed, if so, clear old backtests and re-run
            if (settings.BacktestVersion != CurrentBacktestVersion)
            {
                companyData.ForecastRecords.RemoveAll(r => r.IsValidated && r.ForecastDate.Date == r.PeriodStartDate.Date);
                settings.LastBacktestedMonth = null;
                settings.BacktestVersion = CurrentBacktestVersion;
                companyData.MarkAsModified();
            }

            // Get monthly revenue and expense aggregates
            var monthlyData = GetMonthlyAggregates(companyData);

            if (monthlyData.Count < minTrainingMonths + 1)
            {
                progress?.Report((0, 0, "Insufficient data for backtesting"));
                return 0;
            }

            // Determine starting point for backtest
            var existingPeriods = companyData.ForecastRecords
                .Where(r => r.IsValidated)
                .Select(r => r.PeriodStartDate.ToString("yyyy-MM"))
                .ToHashSet();

            // Only backtest months that have fully completed (exclude current and future months)
            var currentMonthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var monthsToTest = monthlyData
                .Skip(minTrainingMonths)
                .Where(m => m.Month < currentMonthStart)
                .Where(m => !existingPeriods.Contains(m.Month.ToString("yyyy-MM")))
                .ToList();

            if (!monthsToTest.Any())
            {
                progress?.Report((0, 0, "All periods already backtested"));
                settings.LastBacktestedMonth = monthlyData.Last().Month.ToString("yyyy-MM");
                return 0;
            }

            var total = monthsToTest.Count;
            var recordsCreated = 0;

            for (int i = 0; i < monthsToTest.Count; i++)
            {
                var targetMonth = monthsToTest[i];
                var targetIndex = monthlyData.IndexOf(targetMonth);

                progress?.Report((i + 1, total, $"Analyzing {targetMonth.Month:MMMM yyyy}..."));

                // Get training data (up to 24 months before target, matching live forecast window)
                var trainingRevenue = monthlyData
                    .Take(targetIndex)
                    .TakeLast(24)
                    .Select(m => m.Revenue)
                    .ToList();

                var trainingExpenses = monthlyData
                    .Take(targetIndex)
                    .TakeLast(24)
                    .Select(m => m.Expenses)
                    .ToList();

                if (trainingRevenue.Count < minTrainingMonths)
                    continue;

                // Generate forecast using ML service
                var revenueForecast = mlService.GenerateEnhancedForecast(trainingRevenue);
                var expenseForecast = mlService.GenerateEnhancedForecast(trainingExpenses);

                var forecastedRevenue = revenueForecast.ForecastedValue;
                var forecastedExpenses = expenseForecast.ForecastedValue;
                var forecastedProfit = forecastedRevenue - forecastedExpenses;

                // Create validated record with both forecast and actual
                var record = new ForecastAccuracyRecord
                {
                    ForecastDate = targetMonth.Month, // Simulated forecast date
                    PeriodStartDate = targetMonth.Month,
                    PeriodEndDate = targetMonth.Month.AddMonths(1).AddDays(-1),
                    ForecastedRevenue = forecastedRevenue,
                    ForecastedExpenses = forecastedExpenses,
                    ForecastedProfit = forecastedProfit,
                    ForecastedNewCustomers = 0, // Not tracked in backtest
                    ConfidenceScore = (revenueForecast.ConfidenceScore + expenseForecast.ConfidenceScore) / 2,
                    ForecastMethod = revenueForecast.MethodUsed,
                    ActualRevenue = targetMonth.Revenue,
                    ActualExpenses = targetMonth.Expenses,
                    ActualProfit = targetMonth.Revenue - targetMonth.Expenses,
                    ActualNewCustomers = 0,
                    IsValidated = true
                };

                companyData.ForecastRecords.Add(record);
                recordsCreated++;
            }

            settings.LastBacktestedMonth = monthlyData.Last().Month.ToString("yyyy-MM");

            if (recordsCreated > 0)
            {
                companyData.MarkAsModified();
            }

            progress?.Report((total, total, $"Completed! {recordsCreated} {(recordsCreated == 1 ? "forecast" : "forecasts")} validated."));
            return recordsCreated;
        });
    }

    /// <summary>
    /// Gets monthly aggregates of revenue and expenses.
    /// </summary>
    private List<MonthlyAggregate> GetMonthlyAggregates(CompanyData companyData)
    {
        // Same basis as the live forecast and ValidatePastForecasts: gross collected revenue and
        // gross expenses, so backtests and live forecasts are scored against one yardstick.
        var salesByMonth = companyData.Revenues
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => new DateTime(s.Date.Year, s.Date.Month, 1))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.EffectiveTotalUSD));

        // Aggregate purchases by month
        var purchasesByMonth = companyData.Expenses
            .GroupBy(p => new DateTime(p.Date.Year, p.Date.Month, 1))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.EffectiveTotalUSD));

        // Get the full range of months with data
        var allDataMonths = salesByMonth.Keys.Concat(purchasesByMonth.Keys).ToList();
        if (allDataMonths.Count == 0)
            return new List<MonthlyAggregate>();

        var startMonth = allDataMonths.Min();
        var endMonth = allDataMonths.Max();

        // Interpolate gaps instead of zero-filling to avoid false seasonal patterns
        var interpolatedRevenue = InsightsService.InterpolateMonthlyGaps(salesByMonth, startMonth, endMonth);
        var interpolatedExpenses = InsightsService.InterpolateMonthlyGaps(purchasesByMonth, startMonth, endMonth);

        var result = new List<MonthlyAggregate>();
        var current = startMonth;
        int i = 0;
        while (current <= endMonth)
        {
            result.Add(new MonthlyAggregate
            {
                Month = current,
                Revenue = interpolatedRevenue[i],
                Expenses = interpolatedExpenses[i]
            });
            current = current.AddMonths(1);
            i++;
        }
        return result;
    }

    /// <summary>
    /// Represents monthly aggregated data for backtesting.
    /// </summary>
    private class MonthlyAggregate
    {
        public DateTime Month { get; set; }
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
    }

    /// <inheritdoc />
    public MethodAccuracyData GetMethodAccuracies(CompanyData companyData)
    {
        var result = new MethodAccuracyData();

        var validatedRecords = companyData.ForecastRecords
            .Where(r => r.IsValidated && !string.IsNullOrEmpty(r.ForecastMethod))
            .ToList();

        if (!validatedRecords.Any())
        {
            return result;
        }

        // Group by method and calculate accuracy for each
        var ssaRecords = validatedRecords
            .Where(r => r.ForecastMethod.Contains("SSA") && !r.ForecastMethod.Contains("Combined"))
            .ToList();

        var hwRecords = validatedRecords
            .Where(r => r.ForecastMethod.Contains("Holt") && !r.ForecastMethod.Contains("Combined"))
            .ToList();

        var combinedRecords = validatedRecords
            .Where(r => r.ForecastMethod.Contains("Combined"))
            .ToList();

        // Calculate accuracy for each method
        if (ssaRecords.Any())
        {
            result.SSACount = ssaRecords.Count;
            result.SSAAccuracy = ssaRecords
                .Select(CalculateRecordAccuracy)
                .Average();
        }

        if (hwRecords.Any())
        {
            result.HoltWintersCount = hwRecords.Count;
            result.HoltWintersAccuracy = hwRecords
                .Select(CalculateRecordAccuracy)
                .Average();
        }

        if (combinedRecords.Any())
        {
            result.CombinedCount = combinedRecords.Count;
            result.CombinedAccuracy = combinedRecords
                .Select(CalculateRecordAccuracy)
                .Average();
        }

        // Calculate overall accuracy
        var allAccuracies = validatedRecords
            .Select(CalculateRecordAccuracy)
            .ToList();
        result.OverallAccuracy = allAccuracies.Average();

        // Calculate adaptive weights based on accuracy
        // Only if we have data for both SSA and Holt-Winters methods
        if (result.SSACount > 0 && result.HoltWintersCount > 0)
        {
            // Use softmax-style weighting: better accuracy = exponentially higher weight
            var ssaScore = Math.Pow(result.SSAAccuracy / 100, 2);
            var hwScore = Math.Pow(result.HoltWintersAccuracy / 100, 2);
            var totalScore = ssaScore + hwScore;

            if (totalScore > 0)
            {
                result.SSAWeight = ssaScore / totalScore;
                result.HoltWintersWeight = hwScore / totalScore;
            }
        }
        else if (result.SSACount > 0)
        {
            // Only SSA data - weight it higher if accurate
            result.SSAWeight = Math.Min(0.7, result.SSAAccuracy / 100);
            result.HoltWintersWeight = 1 - result.SSAWeight;
        }
        else if (result.HoltWintersCount > 0)
        {
            // Only HW data - weight it higher if accurate
            result.HoltWintersWeight = Math.Min(0.7, result.HoltWintersAccuracy / 100);
            result.SSAWeight = 1 - result.HoltWintersWeight;
        }

        return result;
    }

    /// <summary>
    /// Calculates the accuracy percentage for a single forecast record.
    /// </summary>
    private double CalculateRecordAccuracy(ForecastAccuracyRecord record)
    {
        if (!record.ActualRevenue.HasValue) return 0;

        // Use SMAPE-based accuracy: handles small actuals better than MAPE
        var revDenominator = (Math.Abs(record.ForecastedRevenue) + Math.Abs(record.ActualRevenue.Value)) / 2m;
        var revenueAccuracy = revDenominator == 0 ? 100.0
            : Math.Max(0, 100 * (1 - (double)(Math.Abs(record.ForecastedRevenue - record.ActualRevenue.Value) / revDenominator)));

        // Also consider expense accuracy if available
        if (record.ActualExpenses.HasValue)
        {
            var expDenominator = (Math.Abs(record.ForecastedExpenses) + Math.Abs(record.ActualExpenses.Value)) / 2m;
            var expenseAccuracy = expDenominator == 0 ? 100.0
                : Math.Max(0, 100 * (1 - (double)(Math.Abs(record.ForecastedExpenses - record.ActualExpenses.Value) / expDenominator)));
            return (revenueAccuracy + expenseAccuracy) / 2;
        }

        return revenueAccuracy;
    }
}

/// <summary>
/// Interface for forecast accuracy tracking service.
/// </summary>
public interface IForecastAccuracyService
{
    /// <summary>
    /// Saves a forecast for later comparison with actual values.
    /// </summary>
    /// <param name="companyData">The company data to store the forecast in.</param>
    /// <param name="forecast">The forecast data to save.</param>
    /// <param name="forecastPeriod">The date range the forecast covers.</param>
    void SaveForecast(CompanyData companyData, ForecastData forecast, AnalysisDateRange forecastPeriod);

    /// <summary>
    /// Validates past forecasts by comparing them to actual values.
    /// Called automatically when generating new insights.
    /// </summary>
    /// <param name="companyData">The company data containing forecasts and actuals.</param>
    void ValidatePastForecasts(CompanyData companyData);

    /// <summary>
    /// Gets the complete forecast accuracy data including historical records and statistics.
    /// </summary>
    /// <param name="companyData">The company data to analyze.</param>
    /// <returns>Complete accuracy data with statistics.</returns>
    ForecastAccuracyData GetAccuracyData(CompanyData companyData);

    /// <summary>
    /// Gets recent forecast accuracy as a tuple.
    /// </summary>
    /// <param name="companyData">The company data to analyze.</param>
    /// <param name="recentCount">Number of recent forecasts to include.</param>
    /// <returns>Tuple of (RevenueAccuracy, ExpenseAccuracy) or null if no data.</returns>
    (double RevenueAccuracy, double ExpenseAccuracy)? GetRecentAccuracy(CompanyData companyData, int recentCount = 6);

    /// <summary>
    /// Gets a human-readable summary of forecast accuracy.
    /// </summary>
    /// <param name="companyData">The company data to analyze.</param>
    /// <returns>A description of forecast accuracy performance.</returns>
    string GetAccuracySummary(CompanyData companyData);

    /// <summary>
    /// Removes old forecast records beyond the maximum limit.
    /// </summary>
    /// <param name="companyData">The company data to clean up.</param>
    /// <param name="maxRecords">Maximum number of records to keep.</param>
    void CleanupOldRecords(CompanyData companyData, int maxRecords = 24);

    /// <summary>
    /// Determines if a backtest should be run based on available data and previous runs.
    /// </summary>
    /// <param name="companyData">The company data to check.</param>
    /// <param name="settings">The company settings with backtest tracking.</param>
    /// <param name="minMonths">Minimum months of data required.</param>
    /// <returns>True if backtest should be run.</returns>
    bool ShouldRunBacktest(CompanyData companyData, CompanySettings settings, int minMonths = 4);

    /// <summary>
    /// Runs walk-forward validation (backtesting) on historical data.
    /// Creates validated forecast records by testing predictions against known outcomes.
    /// </summary>
    /// <param name="companyData">The company data to backtest.</param>
    /// <param name="settings">The company settings to update with backtest status.</param>
    /// <param name="mlService">The ML forecasting service to use.</param>
    /// <param name="minTrainingMonths">Minimum months of training data before making predictions.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <returns>Number of forecast records created.</returns>
    Task<int> RunBacktestAsync(
        CompanyData companyData,
        CompanySettings settings,
        ILocalMLForecastingService mlService,
        int minTrainingMonths = 3,
        IProgress<(int current, int total, string message)>? progress = null);

    /// <summary>
    /// Calculates accuracy statistics for each forecasting method based on historical performance.
    /// Used to adaptively weight methods in combined forecasts.
    /// </summary>
    /// <param name="companyData">The company data to analyze.</param>
    /// <returns>Method accuracy data with weights for adaptive forecasting.</returns>
    MethodAccuracyData GetMethodAccuracies(CompanyData companyData);
}

/// <summary>
/// Contains accuracy statistics for different forecasting methods.
/// Used to adaptively weight methods based on historical performance.
/// </summary>
public class MethodAccuracyData
{
    /// <summary>
    /// Average accuracy of SSA forecasts (0-100).
    /// </summary>
    public double SSAAccuracy { get; set; }

    /// <summary>
    /// Average accuracy of Holt-Winters forecasts (0-100).
    /// </summary>
    public double HoltWintersAccuracy { get; set; }

    /// <summary>
    /// Average accuracy of Combined method forecasts (0-100).
    /// </summary>
    public double CombinedAccuracy { get; set; }

    /// <summary>
    /// Number of validated SSA forecasts.
    /// </summary>
    public int SSACount { get; set; }

    /// <summary>
    /// Number of validated Holt-Winters forecasts.
    /// </summary>
    public int HoltWintersCount { get; set; }

    /// <summary>
    /// Number of validated Combined forecasts.
    /// </summary>
    public int CombinedCount { get; set; }

    /// <summary>
    /// Calculated weight for SSA in combined forecasts (0-1).
    /// Higher accuracy = higher weight.
    /// </summary>
    public double SSAWeight { get; set; } = 0.5;

    /// <summary>
    /// Calculated weight for Holt-Winters in combined forecasts (0-1).
    /// </summary>
    public double HoltWintersWeight { get; set; } = 0.5;

    /// <summary>
    /// Overall historical accuracy across all methods.
    /// </summary>
    public double OverallAccuracy { get; set; }

    /// <summary>
    /// Whether there's enough data to use adaptive weights.
    /// </summary>
    public bool HasSufficientData => SSACount + HoltWintersCount + CombinedCount >= 3;

    /// <summary>
    /// Description of the adaptive weighting for display.
    /// </summary>
    public string WeightingDescription => HasSufficientData
        ? $"Methods weighted by historical accuracy: SSA {SSAWeight:P0}, Holt-Winters {HoltWintersWeight:P0}"
        : "Default equal weighting (insufficient historical data)";
}
