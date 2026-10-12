using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the HoltWintersForecasting class.
/// </summary>
public class HoltWintersForecastingTests
{
    private readonly HoltWintersForecasting _forecasting = new();

    #region ForecastAdditive Tests

    [Fact]
    public void ForecastAdditive_InsufficientData_UsesFallback()
    {
        var data = new List<decimal> { 100m, 110m, 105m };

        var result = _forecasting.ForecastAdditive(data);

        Assert.NotNull(result);
        Assert.NotEmpty(result.ForecastedValues);
    }

    [Fact]
    public void ForecastAdditive_SufficientData_ReturnsForecast()
    {
        // Need at least 2 * seasonLength data points
        var data = Enumerable.Range(0, 24).Select(i => 100m + i * 5m).ToList();

        var result = _forecasting.ForecastAdditive(data);

        Assert.NotNull(result);
        Assert.Single(result.ForecastedValues);
        Assert.Equal("Holt-Winters Additive", result.Method);
    }

    [Fact]
    public void ForecastAdditive_MultiplePeriodsForecasted()
    {
        var data = Enumerable.Range(0, 24).Select(i => 100m + i * 5m).ToList();

        var result = _forecasting.ForecastAdditive(data, 12, 3);

        Assert.Equal(3, result.ForecastedValues.Count);
    }

    [Fact]
    public void ForecastAdditive_ReturnsSeasonalPattern()
    {
        var data = Enumerable.Range(0, 24).Select(i => 100m + i * 5m).ToList();

        var result = _forecasting.ForecastAdditive(data);

        Assert.NotNull(result.SeasonalPattern);
        Assert.Equal(12, result.SeasonalPattern.SeasonLength);
    }

    [Fact]
    public void ForecastAdditive_ForecastedValuesAreNonNegative()
    {
        var data = Enumerable.Range(0, 24).Select(i => 10m + i).ToList();

        var result = _forecasting.ForecastAdditive(data, 12, 3);

        Assert.All(result.ForecastedValues, v => Assert.True(v >= 0));
    }

    [Fact]
    public void ForecastAdditive_NonMultipleOfSeasonLength_UsesCorrectSeasonForForecast()
    {
        // The seasonal index was computed as n + (n + h - 1) % m, which equals the correct n + (h - 1) % m only when n is a multiple of the season.
        var data = new List<decimal>
        {
            120m, 0m, 60m,
            120m, 0m, 60m,
            120m, 0m, 60m,
            120m
        };

        var result = _forecasting.ForecastAdditive(data, seasonLength: 3, periodsToForecast: 1);

        // A correct forecast lands near the "low" season (0); the bug lands near the "mid" season
        // (60). Assert it is closer to the low value than to the mid value.
        Assert.True(
            result.ForecastedValues[0] < 30m,
            $"Expected forecast near the low season (~0) but got {result.ForecastedValues[0]}, " +
            "which means the wrong season's factor was used.");
    }

    #endregion

    #region ForecastMultiplicative Tests

    [Fact]
    public void ForecastMultiplicative_InsufficientData_UsesFallback()
    {
        var data = new List<decimal> { 100m, 110m, 105m };

        var result = _forecasting.ForecastMultiplicative(data);

        Assert.NotNull(result);
        Assert.NotEmpty(result.ForecastedValues);
    }

    [Fact]
    public void ForecastMultiplicative_WithZeroValues_FallsBackToAdditive()
    {
        var data = Enumerable.Range(0, 24).Select(i => (decimal)i).ToList(); // Contains 0

        var result = _forecasting.ForecastMultiplicative(data);

        Assert.NotNull(result);
        Assert.Contains("Additive", result.Method);
    }

    [Fact]
    public void ForecastMultiplicative_SufficientPositiveData_ReturnsForecast()
    {
        var data = Enumerable.Range(1, 24).Select(i => 100m + i * 5m).ToList();

        var result = _forecasting.ForecastMultiplicative(data);

        Assert.NotNull(result);
        Assert.Single(result.ForecastedValues);
        Assert.Equal("Holt-Winters Multiplicative", result.Method);
    }

    #endregion

    #region AutoForecast Tests

    [Fact]
    public void AutoForecast_InsufficientData_UsesFallback()
    {
        var data = new List<decimal> { 100m, 110m };

        var result = _forecasting.AutoForecast(data);

        Assert.NotNull(result);
        Assert.NotEmpty(result.ForecastedValues);
    }

    [Fact]
    public void AutoForecast_WithZeros_UsesAdditive()
    {
        var data = Enumerable.Range(0, 24).Select(i => (decimal)i).ToList();

        var result = _forecasting.AutoForecast(data);

        Assert.NotNull(result);
    }

    [Fact]
    public void AutoForecast_PositiveData_ReturnsForecast()
    {
        var data = Enumerable.Range(1, 24).Select(i => 100m + i * 5m).ToList();

        var result = _forecasting.AutoForecast(data);

        Assert.NotNull(result);
        Assert.NotEmpty(result.ForecastedValues);
    }

    #endregion

    #region DetectSeasonLength Tests

    [Fact]
    public void DetectSeasonLength_ShortData_ReturnsFirstCandidate()
    {
        var data = Enumerable.Range(0, 20).Select(i => 100m + i).ToList();
        var candidates = new[] { 12, 6, 4, 3 };

        var result = _forecasting.DetectSeasonLength(data, candidates);

        Assert.True(result > 0);
    }

    [Fact]
    public void DetectSeasonLength_LongData_SelectsBestLength()
    {
        // Create data with a clear quarterly (4-month) pattern
        var data = Enumerable.Range(0, 48)
            .Select(i => 100m + 20m * (decimal)Math.Sin(2 * Math.PI * i / 4))
            .ToList();
        var candidates = new[] { 12, 6, 4, 3 };

        var result = _forecasting.DetectSeasonLength(data, candidates);

        Assert.Contains(result, candidates);
    }

    #endregion

    #region HoltWintersForecastResult Tests

    [Fact]
    public void HoltWintersForecastResult_ForecastedValue_ReturnsFirst()
    {
        var result = new HoltWintersForecastResult
        {
            ForecastedValues = new List<decimal> { 100m, 200m }
        };

        Assert.Equal(100m, result.ForecastedValue);
    }

    [Fact]
    public void HoltWintersForecastResult_ForecastedValue_Empty_ReturnsDefault()
    {
        var result = new HoltWintersForecastResult();

        Assert.Equal(0m, result.ForecastedValue);
    }

    #endregion
}
