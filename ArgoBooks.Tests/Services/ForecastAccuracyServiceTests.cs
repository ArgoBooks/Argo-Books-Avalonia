using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Insights;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the ForecastAccuracyService class.
/// </summary>
public class ForecastAccuracyServiceTests
{
    private readonly ForecastAccuracyService _service = new();

    #region SaveForecast Tests

    [Fact]
    public void SaveForecast_NewForecast_StoresRecord()
    {
        var companyData = new CompanyData();
        var forecast = new ForecastData
        {
            ForecastedRevenue = 10000m,
            ForecastedExpenses = 5000m,
            ForecastedProfit = 5000m,
            ExpectedNewCustomers = 10,
            ConfidenceScore = 75.0
        };
        var period = new AnalysisDateRange
        {
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 1, 31)
        };

        _service.SaveForecast(companyData, forecast, period);

        Assert.Single(companyData.ForecastRecords);
        var record = companyData.ForecastRecords[0];
        Assert.Equal(10000m, record.ForecastedRevenue);
        Assert.Equal(5000m, record.ForecastedExpenses);
        Assert.Equal(5000m, record.ForecastedProfit);
        Assert.Equal(10, record.ForecastedNewCustomers);
        Assert.Equal(75.0, record.ConfidenceScore);
        Assert.False(record.IsValidated);
    }

    [Fact]
    public void SaveForecast_DuplicatePeriod_UpdatesExistingRecord()
    {
        var companyData = new CompanyData();
        var period = new AnalysisDateRange
        {
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 1, 31)
        };

        var forecast1 = new ForecastData
        {
            ForecastedRevenue = 10000m,
            ForecastedExpenses = 5000m,
            ForecastedProfit = 5000m,
            ConfidenceScore = 70.0
        };

        var forecast2 = new ForecastData
        {
            ForecastedRevenue = 12000m,
            ForecastedExpenses = 6000m,
            ForecastedProfit = 6000m,
            ConfidenceScore = 80.0
        };

        _service.SaveForecast(companyData, forecast1, period);
        _service.SaveForecast(companyData, forecast2, period);

        Assert.Single(companyData.ForecastRecords);
        Assert.Equal(12000m, companyData.ForecastRecords[0].ForecastedRevenue);
        Assert.Equal(80.0, companyData.ForecastRecords[0].ConfidenceScore);
    }

    /// <summary>
    /// A forecast saved by an older build ends at 23:59:59, and the same month now ends at the day's
    /// last tick. It is still the same period, so saving must update it rather than add a second
    /// record that Past Predictions and the accuracy average would count twice.
    /// </summary>
    [Fact]
    public void SaveForecast_SamePeriodEndingAtADifferentTimeOfDay_UpdatesExistingRecord()
    {
        var companyData = new CompanyData();
        _service.SaveForecast(companyData, new ForecastData { ForecastedRevenue = 10000m },
            new AnalysisDateRange { StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 1, 31, 23, 59, 59) });

        _service.SaveForecast(companyData, new ForecastData { ForecastedRevenue = 12000m },
            new AnalysisDateRange { StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 2, 1).AddTicks(-1) });

        Assert.Single(companyData.ForecastRecords);
        Assert.Equal(12000m, companyData.ForecastRecords[0].ForecastedRevenue);
    }

    [Fact]
    public void SaveForecast_MultiplePeriods_StoresMultipleRecords()
    {
        var companyData = new CompanyData();
        var forecast = new ForecastData
        {
            ForecastedRevenue = 10000m,
            ForecastedExpenses = 5000m,
            ForecastedProfit = 5000m,
            ConfidenceScore = 75.0
        };

        var period1 = new AnalysisDateRange
        {
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 1, 31)
        };

        var period2 = new AnalysisDateRange
        {
            StartDate = new DateTime(2025, 2, 1),
            EndDate = new DateTime(2025, 2, 28)
        };

        _service.SaveForecast(companyData, forecast, period1);
        _service.SaveForecast(companyData, forecast, period2);

        Assert.Equal(2, companyData.ForecastRecords.Count);
    }

    [Fact]
    public void SaveForecast_MarksDataAsModified()
    {
        var companyData = new CompanyData();
        companyData.MarkAsSaved();
        var forecast = new ForecastData { ForecastedRevenue = 1000m };
        var period = new AnalysisDateRange
        {
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 1, 31)
        };

        _service.SaveForecast(companyData, forecast, period);

        Assert.True(companyData.ChangesMade);
    }

    #endregion

    #region GetAccuracyData Tests

    [Fact]
    public void GetAccuracyData_NoRecords_ReturnsEmptyData()
    {
        var companyData = new CompanyData();

        var result = _service.GetAccuracyData(companyData);

        Assert.NotNull(result);
        Assert.Empty(result.HistoricalRecords);
        Assert.Equal(0, result.ValidatedForecastCount);
    }

    [Fact]
    public void GetAccuracyData_WithUnvalidatedRecords_ReturnsAllRecords()
    {
        var companyData = new CompanyData();
        companyData.ForecastRecords.Add(new ForecastAccuracyRecord
        {
            PeriodStartDate = DateTime.Today.AddMonths(1),
            PeriodEndDate = DateTime.Today.AddMonths(2),
            ForecastedRevenue = 10000m,
            IsValidated = false
        });

        var result = _service.GetAccuracyData(companyData);

        Assert.Single(result.HistoricalRecords);
        Assert.Equal(0, result.ValidatedForecastCount);
    }

    [Fact]
    public void GetAccuracyData_ReturnsRecordsOrderedByDate()
    {
        var companyData = new CompanyData();
        companyData.ForecastRecords.Add(new ForecastAccuracyRecord
        {
            PeriodStartDate = new DateTime(2025, 1, 1),
            PeriodEndDate = new DateTime(2025, 7, 1),
            ForecastedRevenue = 5000m,
            IsValidated = false
        });
        companyData.ForecastRecords.Add(new ForecastAccuracyRecord
        {
            PeriodStartDate = new DateTime(2025, 3, 1),
            PeriodEndDate = new DateTime(2025, 7, 1),
            ForecastedRevenue = 15000m,
            IsValidated = false
        });

        var result = _service.GetAccuracyData(companyData);

        Assert.Equal(2, result.HistoricalRecords.Count);
        Assert.True(result.HistoricalRecords[0].PeriodStartDate >= result.HistoricalRecords[1].PeriodStartDate);
    }

    #endregion

    #region ShouldRunBacktest Tests

    [Fact]
    public void ShouldRunBacktest_NoRecords_ReturnsFalse()
    {
        var companyData = new CompanyData();
        var settings = new CompanySettings();

        var result = _service.ShouldRunBacktest(companyData, settings);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRunBacktest_InsufficientMonths_ReturnsFalse()
    {
        var companyData = new CompanyData();
        // Add only 2 months of data (minimum is 4)
        companyData.Revenues.Add(new Revenue
        {
            Date = new DateTime(2025, 1, 15),
            Total = 1000m
        });
        companyData.Revenues.Add(new Revenue
        {
            Date = new DateTime(2025, 2, 15),
            Total = 2000m
        });
        var settings = new CompanySettings();

        var result = _service.ShouldRunBacktest(companyData, settings);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRunBacktest_SufficientDataNeverBacktested_ReturnsTrue()
    {
        var companyData = new CompanyData();
        // Add 5 months of data
        for (int i = 0; i < 5; i++)
        {
            companyData.Revenues.Add(new Revenue
            {
                Date = new DateTime(2025, 1 + i, 15),
                Total = 1000m * (i + 1)
            });
        }
        var settings = new CompanySettings { LastBacktestedMonth = null };

        var result = _service.ShouldRunBacktest(companyData, settings);

        Assert.True(result);
    }

    #endregion

    #region GetMethodAccuracies Tests

    [Fact]
    public void GetMethodAccuracies_NoData_ReturnsEmptyResult()
    {
        var companyData = new CompanyData();

        var result = _service.GetMethodAccuracies(companyData);

        Assert.NotNull(result);
        Assert.Equal(0, result.SSACount);
        Assert.Equal(0, result.HoltWintersCount);
        Assert.Equal(0, result.CombinedCount);
        Assert.False(result.HasSufficientData);
    }

    [Fact]
    public void GetMethodAccuracies_NoValidatedRecords_ReturnsEmptyResult()
    {
        var companyData = new CompanyData();
        companyData.ForecastRecords.Add(new ForecastAccuracyRecord
        {
            ForecastedRevenue = 10000m,
            IsValidated = false,
            ForecastMethod = "SSA"
        });

        var result = _service.GetMethodAccuracies(companyData);

        Assert.Equal(0, result.SSACount);
    }

    [Fact]
    public void GetMethodAccuracies_DefaultWeights_AreFiftyFifty()
    {
        var result = new MethodAccuracyData();

        Assert.Equal(0.5, result.SSAWeight);
        Assert.Equal(0.5, result.HoltWintersWeight);
    }

    #endregion

    #region ValidatePastForecasts Tests

    [Fact]
    public void ValidatePastForecasts_MeasuresActualRevenueAsGrossCollected()
    {
        // Forecasts are generated from gross (tax-inclusive), collected-only revenue
        // (InsightsService uses RevenueAggregator.IsCollected + EffectiveTotalUSD). The validation
        // step must measure "actual" the same way, otherwise the accuracy score compares two
        // different yardsticks. It used pre-tax amounts and counted uncollected revenue too.
        var companyData = new CompanyData();

        var period = new AnalysisDateRange
        {
            StartDate = new DateTime(2024, 6, 1),
            EndDate = new DateTime(2024, 6, 30)
        };
        _service.SaveForecast(
            companyData,
            new ForecastData { ForecastedRevenue = 110m, ForecastedExpenses = 0m, ForecastedProfit = 110m },
            period);

        // Collected sale: gross 110 (pre-tax 100 + 10 tax). This is the only revenue the forecast
        // basis would count, and it counts the gross 110.
        companyData.Revenues.Add(new Revenue
        {
            Id = "REV-PAID",
            Date = new DateTime(2024, 6, 15),
            OriginalCurrency = "USD",
            Total = 110m,
            TaxAmount = 10m,
            PaymentStatus = RevenuePaymentStatus.Paid
        });
        // Uncollected sale: must be excluded from "actual" because the forecast excluded it.
        companyData.Revenues.Add(new Revenue
        {
            Id = "REV-UNPAID",
            Date = new DateTime(2024, 6, 20),
            OriginalCurrency = "USD",
            Total = 200m,
            TaxAmount = 0m,
            PaymentStatus = RevenuePaymentStatus.Partial
        });

        _service.ValidatePastForecasts(companyData);

        var record = companyData.ForecastRecords[0];
        Assert.True(record.IsValidated);
        Assert.Equal(110m, record.ActualRevenue);
    }

    #endregion

    #region RunBacktestAsync Tests

    // Backtests are scored and weighted alongside live forecasts, so they have to use the same
    // yardstick: gross collected revenue and gross expenses.
    [Fact]
    public async Task RunBacktestAsync_MeasuresGrossCollectedRevenueAndGrossExpenses()
    {
        var companyData = new CompanyData();
        for (var month = 1; month <= 5; month++)
        {
            var date = new DateTime(2024, month, 15);
            companyData.Revenues.Add(new Revenue
            {
                Id = $"REV-{month}", Date = date, OriginalCurrency = "USD",
                Total = 110m, TaxAmount = 10m, PaymentStatus = RevenuePaymentStatus.Paid
            });
            companyData.Revenues.Add(new Revenue
            {
                Id = $"REV-UNPAID-{month}", Date = date, OriginalCurrency = "USD",
                Total = 500m, PaymentStatus = RevenuePaymentStatus.Unpaid
            });
            companyData.Expenses.Add(new Expense
            {
                Id = $"EXP-{month}", Date = date, OriginalCurrency = "USD", Total = 55m, TaxAmount = 5m
            });
        }

        await _service.RunBacktestAsync(companyData, new CompanySettings(), new LocalMLForecastingService());

        Assert.NotEmpty(companyData.ForecastRecords);
        Assert.All(companyData.ForecastRecords, r =>
        {
            Assert.Equal(110m, r.ActualRevenue);
            Assert.Equal(55m, r.ActualExpenses);
        });
    }

    #endregion
}
