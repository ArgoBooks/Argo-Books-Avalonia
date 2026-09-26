using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Insights;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for generating AI-style business insights using local statistical analysis.
/// </summary>
public interface IInsightsService
{
    /// <summary>
    /// Generates comprehensive business insights for the given date range.
    /// </summary>
    /// <param name="companyData">The company data to analyze.</param>
    /// <param name="dateRange">The date range for analysis.</param>
    /// <returns>Complete insights data including trends, anomalies, forecasts, and recommendations.</returns>
    Task<InsightsData> GenerateInsightsAsync(CompanyData companyData, AnalysisDateRange dateRange);

    /// <summary>
    /// Generates forecast data for the next period.
    /// </summary>
    /// <param name="companyData">The company data to analyze.</param>
    /// <param name="dateRange">The current date range (forecast will be for the next period).</param>
    /// <returns>Forecast data with confidence scores.</returns>
    Task<ForecastData> GenerateForecastAsync(CompanyData companyData, AnalysisDateRange dateRange);
}
