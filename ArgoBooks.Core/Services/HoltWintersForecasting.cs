using ArgoBooks.Core.Models.Insights;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Holt-Winters triple exponential smoothing for time series forecasting.
/// Handles level, trend, and seasonal components.
/// </summary>
public class HoltWintersForecasting
{
    // Default smoothing parameters (used when optimization is not possible)
    private const double DefaultAlpha = 0.3; // Level smoothing
    private const double DefaultBeta = 0.1;  // Trend smoothing
    private const double DefaultGamma = 0.2; // Seasonal smoothing
    private const double DefaultPhi = 0.9;   // Trend dampening (1.0 = no dampening)

    /// <summary>
    /// Generates a forecast using the Holt-Winters additive method.
    /// Best for data where seasonal variations are roughly constant over time.
    /// </summary>
    /// <param name="data">Historical time series data.</param>
    /// <param name="seasonLength">Length of one seasonal cycle (e.g., 12 for monthly data with yearly seasonality).</param>
    /// <param name="periodsToForecast">Number of periods to forecast ahead.</param>
    /// <returns>Forecast result with values and seasonal pattern information.</returns>
    public HoltWintersForecastResult ForecastAdditive(
        List<decimal> data,
        int seasonLength = 12,
        int periodsToForecast = 1,
        double? alpha = null,
        double? beta = null,
        double? gamma = null,
        double? phi = null)
    {
        var result = new HoltWintersForecastResult();

        if (data.Count < seasonLength * 2)
        {
            return FallbackForecast(data, periodsToForecast, result);
        }

        var a = alpha ?? DefaultAlpha;
        var b = beta ?? DefaultBeta;
        var g = gamma ?? DefaultGamma;
        var p = phi ?? DefaultPhi;

        var values = data.Select(d => (double)d).ToList();
        var n = values.Count;

        var (level, trend, seasonals) = InitializeComponents(values, seasonLength);

        var smoothedLevel = new double[n];
        var smoothedTrend = new double[n];
        var smoothedSeasonals = new double[n + seasonLength];

        for (int i = 0; i < seasonLength; i++)
            smoothedSeasonals[i] = seasonals[i];

        smoothedLevel[0] = level;
        smoothedTrend[0] = trend;

        for (int t = 1; t < n; t++)
        {
            double prevSeasonal = smoothedSeasonals[t];

            smoothedLevel[t] = a * (values[t] - prevSeasonal) +
                               (1 - a) * (smoothedLevel[t - 1] + p * smoothedTrend[t - 1]);

            smoothedTrend[t] = b * (smoothedLevel[t] - smoothedLevel[t - 1]) +
                               (1 - b) * p * smoothedTrend[t - 1];

            smoothedSeasonals[t + seasonLength] = g * (values[t] - smoothedLevel[t]) +
                                                   (1 - g) * prevSeasonal;
        }

        var lastLevel = smoothedLevel[n - 1];
        var lastTrend = smoothedTrend[n - 1];

        var forecasts = new List<double>();
        for (int h = 1; h <= periodsToForecast; h++)
        {
            // The seasonal estimate for horizon h lives at smoothedSeasonals[n + (h - 1) % seasonLength]
            // (the current season block occupies indices [n, n + seasonLength), same as finalSeasonals
            // below).
            int seasonIndex = (h - 1) % seasonLength;
            double seasonalFactor = smoothedSeasonals[n + seasonIndex];
            // Damped trend: phi + phi^2 + ... + phi^h
            double dampedTrendSum = DampedTrendSum(p, h);
            double forecast = lastLevel + dampedTrendSum * lastTrend + seasonalFactor;
            forecasts.Add(Math.Max(0, forecast));
        }

        var finalSeasonals = new List<double>();
        for (int i = 0; i < seasonLength; i++)
            finalSeasonals.Add(smoothedSeasonals[n + i]);

        var seasonalVariance = finalSeasonals.Select(s => s * s).Average();
        var dataVariance = CalculateVariance(values);
        var seasonalStrength = dataVariance > 0 ? Math.Min(1, seasonalVariance / dataVariance) : 0;

        result.ForecastedValues = forecasts.Select(f => (decimal)f).ToList();
        result.SeasonalPattern = new SeasonalPattern
        {
            SeasonLength = seasonLength,
            SeasonalFactors = finalSeasonals,
            SeasonalStrength = seasonalStrength,
            Trend = lastTrend > 0.01 ? TrendDirection.Increasing :
                    lastTrend < -0.01 ? TrendDirection.Decreasing : TrendDirection.Stable,
            TrendSlope = lastTrend,
            Description = GenerateSeasonalDescription(finalSeasonals, seasonLength, seasonalStrength)
        };
        result.FinalLevel = (decimal)lastLevel;
        result.FinalTrend = (decimal)lastTrend;
        result.Method = "Holt-Winters Additive";

        return result;
    }

    /// <summary>
    /// Generates a forecast using the Holt-Winters multiplicative method.
    /// Best for data where seasonal variations scale with the level.
    /// </summary>
    public HoltWintersForecastResult ForecastMultiplicative(
        List<decimal> data,
        int seasonLength = 12,
        int periodsToForecast = 1,
        double? alpha = null,
        double? beta = null,
        double? gamma = null,
        double? phi = null)
    {
        var result = new HoltWintersForecastResult();

        if (data.Count < seasonLength * 2)
        {
            return FallbackForecast(data, periodsToForecast, result);
        }

        var values = data.Select(d => (double)d).ToList();

        if (values.Any(v => v <= 0))
        {
            return ForecastAdditive(data, seasonLength, periodsToForecast, alpha, beta, gamma, phi);
        }

        var a = alpha ?? DefaultAlpha;
        var b = beta ?? DefaultBeta;
        var g = gamma ?? DefaultGamma;
        var p = phi ?? DefaultPhi;
        var n = values.Count;

        var (level, trend, seasonals) = InitializeComponentsMultiplicative(values, seasonLength);

        var smoothedLevel = new double[n];
        var smoothedTrend = new double[n];
        var smoothedSeasonals = new double[n + seasonLength];

        for (int i = 0; i < seasonLength; i++)
            smoothedSeasonals[i] = seasonals[i];

        smoothedLevel[0] = level;
        smoothedTrend[0] = trend;

        for (int t = 1; t < n; t++)
        {
            double prevSeasonal = smoothedSeasonals[t];
            if (Math.Abs(prevSeasonal) < 0.0001) prevSeasonal = 0.0001;

            smoothedLevel[t] = a * (values[t] / prevSeasonal) +
                               (1 - a) * (smoothedLevel[t - 1] + p * smoothedTrend[t - 1]);

            smoothedTrend[t] = b * (smoothedLevel[t] - smoothedLevel[t - 1]) +
                               (1 - b) * p * smoothedTrend[t - 1];

            var levelForSeasonal = Math.Abs(smoothedLevel[t]) < 0.0001 ? 0.0001 : smoothedLevel[t];
            smoothedSeasonals[t + seasonLength] = g * (values[t] / levelForSeasonal) +
                                                   (1 - g) * prevSeasonal;
        }

        var lastLevel = smoothedLevel[n - 1];
        var lastTrend = smoothedTrend[n - 1];

        var forecasts = new List<double>();
        for (int h = 1; h <= periodsToForecast; h++)
        {
            // See ForecastAdditive: the seasonal estimate for horizon h is at
            // smoothedSeasonals[n + (h - 1) % seasonLength], not n + (n + h - 1) % seasonLength.
            int seasonIndex = (h - 1) % seasonLength;
            double seasonalFactor = smoothedSeasonals[n + seasonIndex];
            double dampedTrendSum = DampedTrendSum(p, h);
            double forecast = (lastLevel + dampedTrendSum * lastTrend) * seasonalFactor;
            forecasts.Add(Math.Max(0, forecast));
        }

        var finalSeasonals = new List<double>();
        for (int i = 0; i < seasonLength; i++)
            finalSeasonals.Add(smoothedSeasonals[n + i]);

        var seasonalDeviation = finalSeasonals.Select(s => Math.Abs(s - 1)).Average();
        var seasonalStrength = Math.Min(1, seasonalDeviation * 5);

        result.ForecastedValues = forecasts.Select(f => (decimal)f).ToList();
        result.SeasonalPattern = new SeasonalPattern
        {
            SeasonLength = seasonLength,
            SeasonalFactors = finalSeasonals,
            SeasonalStrength = seasonalStrength,
            Trend = lastTrend > 0.01 ? TrendDirection.Increasing :
                    lastTrend < -0.01 ? TrendDirection.Decreasing : TrendDirection.Stable,
            TrendSlope = lastTrend,
            Description = GenerateSeasonalDescription(finalSeasonals, seasonLength, seasonalStrength)
        };
        result.FinalLevel = (decimal)lastLevel;
        result.FinalTrend = (decimal)lastTrend;
        result.Method = "Holt-Winters Multiplicative";

        return result;
    }

    /// <summary>
    /// Automatically selects the best method based on data characteristics.
    /// </summary>
    public HoltWintersForecastResult AutoForecast(
        List<decimal> data,
        int seasonLength = 12,
        int periodsToForecast = 1)
    {
        if (data.Count < seasonLength)
        {
            return FallbackForecast(data, periodsToForecast, new HoltWintersForecastResult());
        }

        // Replace exact zeros (from gap-filling) with a small value so multiplicative can run
        var adjustedData = data.Select(d =>
        {
            if (d <= 0)
            {
                var median = data.Where(v => v > 0).OrderBy(v => v).ToList();
                var medianVal = median.Count > 0 ? median[median.Count / 2] : 1m;
                return medianVal * 0.01m;
            }
            return d;
        }).ToList();

        // Optimize parameters via grid search
        var (bestAlpha, bestBeta, bestGamma, bestPhi, useMultiplicative) =
            OptimizeParameters(adjustedData, seasonLength);

        if (useMultiplicative)
        {
            return ForecastMultiplicative(adjustedData, seasonLength, periodsToForecast,
                bestAlpha, bestBeta, bestGamma, bestPhi);
        }

        return ForecastAdditive(adjustedData, seasonLength, periodsToForecast,
            bestAlpha, bestBeta, bestGamma, bestPhi);
    }

    /// <summary>
    /// Detects the optimal season length from data.
    /// </summary>
    public int DetectSeasonLength(List<decimal> data, int[] candidateLengths)
    {
        if (data.Count < 24)
        {
            var candidate = candidateLengths.FirstOrDefault(l => l <= data.Count / 2);
            return candidate > 0 ? candidate : candidateLengths[^1];
        }

        var values = data.Select(d => (double)d).ToArray();
        var bestLength = candidateLengths[0];
        var bestAutocorrelation = double.MinValue;

        foreach (var length in candidateLengths.Where(l => l <= values.Length / 2))
        {
            var autocorr = CalculateAutocorrelation(values, length);
            if (autocorr > bestAutocorrelation)
            {
                bestAutocorrelation = autocorr;
                bestLength = length;
            }
        }

        return bestLength;
    }

    #region Private Methods

    /// <summary>
    /// Computes the damped trend sum: phi + phi^2 + ... + phi^h
    /// </summary>
    private static double DampedTrendSum(double phi, int h)
    {
        if (Math.Abs(phi - 1.0) < 0.001) return h; // No dampening
        double sum = 0;
        double phiPow = 1;
        for (int i = 0; i < h; i++)
        {
            phiPow *= phi;
            sum += phiPow;
        }
        return sum;
    }

    /// <summary>
    /// Grid search over alpha, beta, gamma, phi to minimize one-step-ahead MSE.
    /// Also determines whether additive or multiplicative is better.
    /// </summary>
    private (double alpha, double beta, double gamma, double phi, bool useMultiplicative)
        OptimizeParameters(List<decimal> data, int seasonLength)
    {
        if (data.Count < seasonLength * 2)
        {
            return (DefaultAlpha, DefaultBeta, DefaultGamma, DefaultPhi, false);
        }

        var values = data.Select(d => (double)d).ToList();
        var canMultiplicative = values.All(v => v > 0);

        var alphaValues = new[] { 0.1, 0.2, 0.3, 0.5, 0.7 };
        var betaValues = new[] { 0.01, 0.05, 0.1, 0.2 };
        var gammaValues = new[] { 0.05, 0.1, 0.2, 0.3 };
        var phiValues = new[] { 0.8, 0.9, 0.95, 1.0 };

        double bestMse = double.MaxValue;
        double bestA = DefaultAlpha, bestB = DefaultBeta, bestG = DefaultGamma, bestP = DefaultPhi;
        bool bestIsMultiplicative = false;

        foreach (var a in alphaValues)
        foreach (var b in betaValues)
        foreach (var g in gammaValues)
        foreach (var p in phiValues)
        {
            // Test additive
            var additiveMse = ComputeOneStepMSE(values, seasonLength, a, b, g, p, multiplicative: false);
            if (additiveMse < bestMse)
            {
                bestMse = additiveMse;
                bestA = a; bestB = b; bestG = g; bestP = p;
                bestIsMultiplicative = false;
            }

            // Test multiplicative if possible
            if (canMultiplicative)
            {
                var multMse = ComputeOneStepMSE(values, seasonLength, a, b, g, p, multiplicative: true);
                if (multMse < bestMse)
                {
                    bestMse = multMse;
                    bestA = a; bestB = b; bestG = g; bestP = p;
                    bestIsMultiplicative = true;
                }
            }
        }

        return (bestA, bestB, bestG, bestP, bestIsMultiplicative);
    }

    /// <summary>
    /// Compute one-step-ahead MSE for a given set of parameters.
    /// Uses the second half of the data for validation.
    /// </summary>
    private double ComputeOneStepMSE(
        List<double> values, int seasonLength,
        double alpha, double beta, double gamma, double phi,
        bool multiplicative)
    {
        var n = values.Count;
        if (n < seasonLength * 2) return double.MaxValue;

        try
        {
            double level, trend;
            double[] seasonals;

            if (multiplicative)
            {
                (level, trend, seasonals) = InitializeComponentsMultiplicative(values, seasonLength);
            }
            else
            {
                (level, trend, seasonals) = InitializeComponents(values, seasonLength);
            }

            var smoothedLevel = new double[n];
            var smoothedTrend = new double[n];
            var smoothedSeasonals = new double[n + seasonLength];

            for (int i = 0; i < seasonLength; i++)
                smoothedSeasonals[i] = seasonals[i];

            smoothedLevel[0] = level;
            smoothedTrend[0] = trend;

            double sumSquaredError = 0;
            int errorCount = 0;
            // Start computing errors from seasonLength onward (after initialization)
            var errorStart = seasonLength;

            for (int t = 1; t < n; t++)
            {
                double prevSeasonal = smoothedSeasonals[t];

                // One-step-ahead forecast from t-1
                if (t >= errorStart)
                {
                    double predicted;
                    if (multiplicative)
                    {
                        predicted = (smoothedLevel[t - 1] + phi * smoothedTrend[t - 1]) * prevSeasonal;
                    }
                    else
                    {
                        predicted = smoothedLevel[t - 1] + phi * smoothedTrend[t - 1] + prevSeasonal;
                    }
                    var error = values[t] - predicted;
                    sumSquaredError += error * error;
                    errorCount++;
                }

                // Update components
                if (multiplicative)
                {
                    if (Math.Abs(prevSeasonal) < 0.0001) prevSeasonal = 0.0001;
                    smoothedLevel[t] = alpha * (values[t] / prevSeasonal) +
                                       (1 - alpha) * (smoothedLevel[t - 1] + phi * smoothedTrend[t - 1]);
                    smoothedTrend[t] = beta * (smoothedLevel[t] - smoothedLevel[t - 1]) +
                                       (1 - beta) * phi * smoothedTrend[t - 1];
                    var lvl = Math.Abs(smoothedLevel[t]) < 0.0001 ? 0.0001 : smoothedLevel[t];
                    smoothedSeasonals[t + seasonLength] = gamma * (values[t] / lvl) +
                                                           (1 - gamma) * prevSeasonal;
                }
                else
                {
                    smoothedLevel[t] = alpha * (values[t] - prevSeasonal) +
                                       (1 - alpha) * (smoothedLevel[t - 1] + phi * smoothedTrend[t - 1]);
                    smoothedTrend[t] = beta * (smoothedLevel[t] - smoothedLevel[t - 1]) +
                                       (1 - beta) * phi * smoothedTrend[t - 1];
                    smoothedSeasonals[t + seasonLength] = gamma * (values[t] - smoothedLevel[t]) +
                                                           (1 - gamma) * prevSeasonal;
                }
            }

            return errorCount > 0 ? sumSquaredError / errorCount : double.MaxValue;
        }
        catch
        {
            return double.MaxValue;
        }
    }

    private (double level, double trend, double[] seasonals) InitializeComponents(
        List<double> values, int seasonLength)
    {
        // Initial level: average of first season
        var firstSeasonAvg = values.Take(seasonLength).Average();

        // Initial trend: average change between first two seasons
        var secondSeasonAvg = values.Skip(seasonLength).Take(seasonLength).Average();
        var initialTrend = (secondSeasonAvg - firstSeasonAvg) / seasonLength;

        // Initial seasonal factors (additive): deviation from first season average
        var seasonals = new double[seasonLength];
        for (int i = 0; i < seasonLength; i++)
        {
            seasonals[i] = values[i] - firstSeasonAvg;
        }

        return (firstSeasonAvg, initialTrend, seasonals);
    }

    private (double level, double trend, double[] seasonals) InitializeComponentsMultiplicative(
        List<double> values, int seasonLength)
    {
        // Initial level: average of first season
        var firstSeasonAvg = values.Take(seasonLength).Average();
        if (firstSeasonAvg <= 0) firstSeasonAvg = 0.0001;

        // Initial trend: average change between first two seasons
        var secondSeasonAvg = values.Skip(seasonLength).Take(seasonLength).Average();
        var initialTrend = (secondSeasonAvg - firstSeasonAvg) / seasonLength;

        // Initial seasonal factors (multiplicative): ratio to first season average
        var seasonals = new double[seasonLength];
        for (int i = 0; i < seasonLength; i++)
        {
            seasonals[i] = values[i] / firstSeasonAvg;
            if (seasonals[i] <= 0) seasonals[i] = 0.0001;
        }

        return (firstSeasonAvg, initialTrend, seasonals);
    }

    private HoltWintersForecastResult FallbackForecast(
        List<decimal> data, int periodsToForecast, HoltWintersForecastResult result)
    {
        if (data.Count == 0)
        {
            result.ForecastedValues = Enumerable.Repeat(0m, periodsToForecast).ToList();
            result.Method = "No Data";
            return result;
        }

        var values = data.Select(d => (double)d).ToList();

        // Use weighted moving average (more recent months weighted higher)
        var windowSize = Math.Min(6, values.Count);
        var recentValues = values.TakeLast(windowSize).ToList();
        double totalWeight = 0;
        double weightedSum = 0;
        for (int i = 0; i < recentValues.Count; i++)
        {
            var weight = i + 1.0; // Linear increasing weights (older=1, newest=windowSize)
            weightedSum += recentValues[i] * weight;
            totalWeight += weight;
        }
        var weightedAvg = weightedSum / totalWeight;

        // Robust trend using Theil-Sen median slope estimator (resistant to outliers)
        double medianTrend = 0;
        if (values.Count > 1)
        {
            var slopes = new List<double>();
            for (int i = 0; i < values.Count; i++)
                for (int j = i + 1; j < values.Count; j++)
                    slopes.Add((values[j] - values[i]) / (j - i));
            slopes.Sort();
            medianTrend = slopes[slopes.Count / 2];

            // Dampen trend to prevent runaway extrapolation
            medianTrend *= 0.5;
        }

        var maxVal = values.Max() * 2.0;
        var forecasts = new List<decimal>();
        for (int h = 1; h <= periodsToForecast; h++)
        {
            var forecast = weightedAvg + h * medianTrend;
            forecasts.Add((decimal)Math.Max(0, Math.Min(maxVal, forecast)));
        }

        result.ForecastedValues = forecasts;
        result.FinalLevel = (decimal)weightedAvg;
        result.FinalTrend = (decimal)medianTrend;
        result.SeasonalPattern = new SeasonalPattern
        {
            SeasonalStrength = 0,
            Description = "Insufficient data for seasonal analysis."
        };
        result.Method = "Weighted Moving Average";

        return result;
    }

    private double CalculateVariance(List<double> values) => values.Variance();

    private double CalculateAutocorrelation(double[] values, int lag)
    {
        var n = values.Length;
        if (lag >= n) return 0;

        var mean = values.Average();
        var variance = values.Select(v => (v - mean) * (v - mean)).Sum();

        if (variance == 0) return 0;

        double autocovariance = 0;
        for (int i = 0; i < n - lag; i++)
        {
            autocovariance += (values[i] - mean) * (values[i + lag] - mean);
        }

        return autocovariance / variance;
    }

    private string GenerateSeasonalDescription(List<double> seasonals, int seasonLength, double strength)
    {
        if (strength < 0.1)
        {
            return "No significant seasonal pattern detected.";
        }

        // Find peak and trough seasons
        var maxIndex = seasonals.IndexOf(seasonals.Max());
        var minIndex = seasonals.IndexOf(seasonals.Min());

        string peakPeriod, troughPeriod;
        string cycleDescription;

        if (seasonLength == 12)
        {
            var months = new[] { "January", "February", "March", "April", "May", "June",
                                 "July", "August", "September", "October", "November", "December" };
            peakPeriod = months[maxIndex % 12];
            troughPeriod = months[minIndex % 12];
            cycleDescription = "yearly";
        }
        else if (seasonLength == 6)
        {
            var biAnnual = new[] { "Jan-Feb", "Mar-Apr", "May-Jun", "Jul-Aug", "Sep-Oct", "Nov-Dec" };
            peakPeriod = biAnnual[maxIndex % 6];
            troughPeriod = biAnnual[minIndex % 6];
            cycleDescription = "bi-monthly";
        }
        else if (seasonLength == 4)
        {
            var quarters = new[] { "Q1 (Jan-Mar)", "Q2 (Apr-Jun)", "Q3 (Jul-Sep)", "Q4 (Oct-Dec)" };
            peakPeriod = quarters[maxIndex % 4];
            troughPeriod = quarters[minIndex % 4];
            cycleDescription = "quarterly";
        }
        else if (seasonLength == 3)
        {
            // For 3-period cycles, describe as ordinal within cycle
            peakPeriod = maxIndex == 0 ? "beginning" : maxIndex == 1 ? "middle" : "end";
            troughPeriod = minIndex == 0 ? "beginning" : minIndex == 1 ? "middle" : "end";
            cycleDescription = "3-month";
        }
        else if (seasonLength == 2)
        {
            peakPeriod = maxIndex == 0 ? "first month" : "second month";
            troughPeriod = minIndex == 0 ? "first month" : "second month";
            cycleDescription = "alternating";
        }
        else
        {
            peakPeriod = $"period {maxIndex + 1}";
            troughPeriod = $"period {minIndex + 1}";
            cycleDescription = $"{seasonLength}-period";
        }

        var strengthDesc = strength > 0.5 ? "strong" : strength > 0.25 ? "moderate" : "mild";

        return $"A {strengthDesc} {cycleDescription} pattern detected. Peak at {peakPeriod} of cycle, lowest at {troughPeriod}.";
    }

    #endregion
}

/// <summary>
/// Result of a Holt-Winters forecast operation.
/// </summary>
public class HoltWintersForecastResult
{
    /// <summary>
    /// The forecasted values for future periods.
    /// </summary>
    public List<decimal> ForecastedValues { get; set; } = [];

    /// <summary>
    /// The detected seasonal pattern.
    /// </summary>
    public SeasonalPattern SeasonalPattern { get; set; } = new();

    /// <summary>
    /// The final level component.
    /// </summary>
    public decimal FinalLevel { get; set; }

    /// <summary>
    /// The final trend component.
    /// </summary>
    public decimal FinalTrend { get; set; }

    /// <summary>
    /// The method used for forecasting.
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Gets the first forecasted value (for single-period forecasts).
    /// </summary>
    public decimal ForecastedValue => ForecastedValues.FirstOrDefault();
}

/// <summary>
/// Extension methods for statistical calculations.
/// </summary>
internal static class StatisticsExtensions
{
    public static double Variance(this IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count < 2) return 0;
        var mean = list.Average();
        return list.Select(v => (v - mean) * (v - mean)).Sum() / (list.Count - 1);
    }

    public static double StandardDeviation(this IEnumerable<double> values)
    {
        return Math.Sqrt(values.Variance());
    }
}
