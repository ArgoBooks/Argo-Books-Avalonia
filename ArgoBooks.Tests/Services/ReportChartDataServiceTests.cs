using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the ReportChartDataService class.
/// </summary>
public class ReportChartDataServiceTests
{
    private static ReportFilters CreateDefaultFilters() => new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31)
    };

    #region Revenue Chart Tests

    [Fact]
    public void GetRevenueOverTime_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetRevenueOverTime();

        Assert.Empty(result);
    }

    [Fact]
    public void GetRevenueOverTime_EmptyRevenues_ReturnsEmptyList()
    {
        var data = new CompanyData();
        var service = new ReportChartDataService(data, CreateDefaultFilters());

        var result = service.GetRevenueOverTime();

        Assert.Empty(result);
    }

    [Fact]
    public void GetRevenueOverTime_WithData_ReturnsGroupedByDate()
    {
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "R1",
            Date = new DateTime(2024, 6, 1),
            Subtotal = 100m,
            Total = 100m
        });
        data.Revenues.Add(new Revenue
        {
            Id = "R2",
            Date = new DateTime(2024, 6, 1),
            Subtotal = 200m,
            Total = 200m
        });
        var service = new ReportChartDataService(data, CreateDefaultFilters());

        var result = service.GetRevenueOverTime();

        Assert.Single(result); // Grouped by date
    }

    [Fact]
    public void GetRevenueDistribution_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetRevenueDistribution();

        Assert.Empty(result);
    }

    [Fact]
    public void GetTotalRevenue_NullCompanyData_ReturnsZero()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetTotalRevenue();

        Assert.Equal(0m, result);
    }

    #endregion

    #region Expense Chart Tests

    [Fact]
    public void GetExpensesOverTime_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetExpensesOverTime();

        Assert.Empty(result);
    }

    [Fact]
    public void GetExpenseDistribution_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetExpenseDistribution();

        Assert.Empty(result);
    }

    [Fact]
    public void GetTotalExpenses_NullCompanyData_ReturnsZero()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetTotalExpenses();

        Assert.Equal(0m, result);
    }

    #endregion

    #region Profit Chart Tests

    [Fact]
    public void GetProfitOverTime_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetProfitOverTime();

        Assert.Empty(result);
    }

    #endregion

    #region Combined Series Tests

    [Fact]
    public void GetRevenueVsExpenses_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetRevenueVsExpenses();

        Assert.Empty(result);
    }

    #endregion

    #region Customer Chart Tests

    [Fact]
    public void GetTopCustomersByRevenue_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetTopCustomersByRevenue();

        Assert.Empty(result);
    }

    [Fact]
    public void GetCustomerGrowth_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetCustomerGrowth();

        Assert.Empty(result);
    }

    #endregion

    #region Returns/Losses Chart Tests

    [Fact]
    public void GetReturnsOverTime_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetReturnsOverTime();

        Assert.Empty(result);
    }

    [Fact]
    public void GetLossesOverTime_NullCompanyData_ReturnsEmptyList()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetLossesOverTime();

        Assert.Empty(result);
    }

    #endregion

    #region Geography Chart Tests

    [Fact]
    public void GetWorldMapData_NullCompanyData_ReturnsEmptyDictionary()
    {
        var service = new ReportChartDataService(null, CreateDefaultFilters());

        var result = service.GetWorldMapData();

        Assert.Empty(result);
    }

    #endregion

    #region Partial-month clamping

    [Fact]
    public void GetAverageTransactionValueBySeries_ClampsPartialMonthToFilterWindow()
    {
        // A sale inside the filter window and another in the same calendar month but outside it.
        // GetRevenueVsExpenses clamps month bounds to the window; this method did not, so the
        // out-of-window sale leaks into the average. (Revenue defaults to PaymentStatus.Paid.)
        var data = new CompanyData();
        data.Revenues.Add(new Revenue { Id = "R1", Date = new DateTime(2024, 1, 10), Total = 100m, OriginalCurrency = "USD" });
        data.Revenues.Add(new Revenue { Id = "R2", Date = new DateTime(2024, 1, 25), Total = 1000m, OriginalCurrency = "USD" });

        var filters = new ReportFilters { StartDate = new DateTime(2024, 1, 5), EndDate = new DateTime(2024, 1, 15) };
        var series = new ReportChartDataService(data, filters).GetAverageTransactionValueBySeries();

        var revenue = series.First(s => s.Name == "Revenue");
        // Only the Jan 10 sale ($100) is within Jan 5-15; the Jan 25 sale must be excluded.
        Assert.Equal(100d, revenue.DataPoints.Single().Value);
    }

    private static double SumOf(List<Core.Models.Charts.ChartSeriesData> series, string name) =>
        series.Where(s => s.Name == name).SelectMany(s => s.DataPoints).Sum(p => p.Value);

    // A sale entered in the afternoon of a month's last day belongs to that month. The month buckets
    // ended at midnight that morning, so it was left out of every one of them.
    [Fact]
    public void MonthBuckets_IncludeTheWholeLastDayOfTheMonth()
    {
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "R1", Date = new DateTime(2024, 1, 31, 14, 30, 0), OriginalCurrency = "USD",
            Subtotal = 100m, TaxAmount = 8m, TaxAmountUSD = 8m, Total = 108m, TotalUSD = 108m
        });
        var service = new ReportChartDataService(data, CreateDefaultFilters());

        Assert.Equal(108d, SumOf(service.GetRevenueVsExpenses(), "Revenue"));
        Assert.Equal(1d, SumOf(service.GetTransactionCountBySeries(), "Revenue"));
        Assert.Equal(8d, SumOf(service.GetTaxCollectedVsPaid(), "Tax Collected"));
    }

    // Accounting reports count the whole end day, so a sale in its final second has to reach the
    // charts beside them too.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DateRange_IncludesTheFinalSecondOfTheEndDay(bool usePreset)
    {
        var lastDay = new DateTime(DateTime.Today.Year - 1, 12, 31);
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "R1", Date = lastDay.AddDays(1).AddMilliseconds(-500), OriginalCurrency = "USD",
            Subtotal = 100m, Total = 100m, TotalUSD = 100m
        });
        var filters = usePreset
            ? new ReportFilters { DatePresetName = DatePresetNames.LastYear }
            : new ReportFilters { DatePresetName = DatePresetNames.Custom, StartDate = lastDay.AddMonths(-1), EndDate = lastDay };

        Assert.Equal(100m, new ReportChartDataService(data, filters).GetTotalRevenue());
    }

    // A range that starts partway through a month counts only that month's tax from inside the range,
    // as the other month-bucketed charts do.
    [Fact]
    public void TaxCharts_ClampPartialMonthsToTheSelectedRange()
    {
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "R1", Date = new DateTime(2024, 8, 3), OriginalCurrency = "USD",
            Subtotal = 100m, TaxAmount = 10m, TaxAmountUSD = 10m, Total = 110m, TotalUSD = 110m
        });
        data.Revenues.Add(new Revenue
        {
            Id = "R2", Date = new DateTime(2024, 8, 20), OriginalCurrency = "USD",
            Subtotal = 50m, TaxAmount = 5m, TaxAmountUSD = 5m, Total = 55m, TotalUSD = 55m
        });
        var filters = new ReportFilters { StartDate = new DateTime(2024, 8, 12), EndDate = new DateTime(2024, 9, 10) };
        var service = new ReportChartDataService(data, filters);

        Assert.Equal(5d, SumOf(service.GetTaxCollectedVsPaid(), "Tax Collected"));
        Assert.Equal(5d, SumOf(service.GetExpenseVsRevenueTax(), "Revenue Tax"));
    }

    // The returns and losses comparisons count only what falls inside the range, and a month whose
    // only entries are outside it gets no bar.
    [Fact]
    public void ReturnAndLossComparisons_ClampPartialMonthsToTheSelectedRange()
    {
        var data = new CompanyData();
        data.Returns.Add(new Core.Models.Tracking.Return { Id = "RET-1", ReturnDate = new DateTime(2024, 8, 3) });
        data.Returns.Add(new Core.Models.Tracking.Return { Id = "RET-2", ReturnDate = new DateTime(2024, 8, 20) });
        data.Returns.Add(new Core.Models.Tracking.Return { Id = "RET-3", ReturnDate = new DateTime(2024, 9, 20) });
        data.LostDamaged.Add(new Core.Models.Tracking.LostDamaged { Id = "LOST-1", DateDiscovered = new DateTime(2024, 8, 3) });
        data.LostDamaged.Add(new Core.Models.Tracking.LostDamaged { Id = "LOST-2", DateDiscovered = new DateTime(2024, 8, 20) });
        data.LostDamaged.Add(new Core.Models.Tracking.LostDamaged { Id = "LOST-3", DateDiscovered = new DateTime(2024, 9, 20) });
        var filters = new ReportFilters
        {
            StartDate = new DateTime(2024, 8, 12), EndDate = new DateTime(2024, 9, 10),
            IncludeReturns = true, IncludeLosses = true
        };
        var service = new ReportChartDataService(data, filters);

        var returns = service.GetExpenseVsRevenueReturns();
        var losses = service.GetExpenseVsRevenueLosses();

        Assert.Equal(1d, SumOf(returns, "Revenue Returns"));
        Assert.Equal(1d, SumOf(losses, "Expense Losses"));
        Assert.Single(returns.First(s => s.Name == "Revenue Returns").DataPoints);
        Assert.Single(losses.First(s => s.Name == "Expense Losses").DataPoints);
    }

    // $500 paid Jan 5 and refunded Jan 20 nets to nothing, as it does on the Revenue card. The daily
    // series feeds the dashboard and Analytics chart and the non-USD report path.
    [Fact]
    public void RevenueVsExpensesDaily_TakesOffRefundsOnTheirOwnDay()
    {
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "R1", InvoiceId = "INV-1", Date = new DateTime(2024, 1, 5), OriginalCurrency = "USD",
            Subtotal = 500m, Total = 500m, TotalUSD = 500m
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-2", InvoiceId = "INV-1", Date = new DateTime(2024, 1, 20), OriginalCurrency = "USD",
            Amount = -500m, AmountUSD = -500m, IsRefund = true
        });
        var filters = new ReportFilters { StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 1, 31) };
        var service = new ReportChartDataService(data, filters);

        Assert.Equal(0d, SumOf(service.GetRevenueVsExpensesDaily(), "Revenue"));
        Assert.Equal(0d, SumOf(service.GetRevenueVsExpensesConverted((usd, _) => usd), "Revenue"));
    }

    #endregion
}
