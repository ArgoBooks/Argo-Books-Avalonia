using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Insights;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Insights is premium, but the sample company shows its insights to everyone, like payroll. They
/// are worked out once, from the sample as it opened, and never again: otherwise someone on the free
/// plan could open the sample, put their own figures in (or Save As), and read insights on them.
/// </summary>
public class InsightsSampleSnapshotTests : ModalViewModelTestBase
{
    private sealed class CountingInsightsService : IInsightsService
    {
        public int InsightsCalls { get; private set; }
        private int ForecastCalls { get; set; }

        public Task<InsightsData> GenerateInsightsAsync(CompanyData companyData, AnalysisDateRange dateRange)
        {
            InsightsCalls++;
            return Task.FromResult(new InsightsData
            {
                HasSufficientData = true,
                Summary = new InsightsSummary { TotalInsights = InsightsCalls },
                RevenueTrends = [new InsightItem { Title = $"Insight {InsightsCalls}" }]
            });
        }

        public Task<ForecastData> GenerateForecastAsync(CompanyData companyData, AnalysisDateRange dateRange)
        {
            ForecastCalls++;
            return Task.FromResult(new ForecastData());
        }

        public Task<List<InsightItem>> DetectAnomaliesAsync(CompanyData companyData, AnalysisDateRange dateRange) => Task.FromResult(new List<InsightItem>());
        public Task<List<InsightItem>> AnalyzeTrendsAsync(CompanyData companyData, AnalysisDateRange dateRange) => Task.FromResult(new List<InsightItem>());
        public Task<List<InsightItem>> GenerateRecommendationsAsync(CompanyData companyData, AnalysisDateRange dateRange) => Task.FromResult(new List<InsightItem>());
    }

    [Fact]
    public async Task AFreeUserInTheSample_SeesTheSamplesInsights_NotTheTeaser()
    {
        InsightsPageViewModel.ClearSampleSnapshot();
        try
        {
            var service = new CountingInsightsService();
            await InsightsPageViewModel.CaptureSampleSnapshotAsync(Company, service);

            var vm = new InsightsPageViewModel(service);

            Assert.False(vm.ShowTeaser);
            Assert.Equal("Insight 1", Assert.Single(vm.RevenueTrends).Title);
        }
        finally
        {
            InsightsPageViewModel.ClearSampleSnapshot();
        }
    }

    [Fact]
    public async Task TheSamplesInsights_AreNeverWorkedOutAgain()
    {
        InsightsPageViewModel.ClearSampleSnapshot();
        try
        {
            var service = new CountingInsightsService();
            await InsightsPageViewModel.CaptureSampleSnapshotAsync(Company, service);
            var vm = new InsightsPageViewModel(service);

            // The user's own figures go in, and everything that would normally recalculate is tried.
            Company.Revenues.Add(new Revenue { Id = "REV-OWN", Date = DateTime.Today, Total = 5000m });
            vm.SelectedInsightsDateRangeIndex = 0;
            await vm.RefreshInsightsCommand.ExecuteAsync(null);
            vm.HasPremium = false;

            Assert.Equal(1, service.InsightsCalls);
            Assert.Equal("Insight 1", Assert.Single(vm.RevenueTrends).Title);
            Assert.False(vm.ShowTeaser);
        }
        finally
        {
            InsightsPageViewModel.ClearSampleSnapshot();
        }
    }

    [Fact]
    public async Task ClosingTheSample_PutsTheTeaserBack()
    {
        InsightsPageViewModel.ClearSampleSnapshot();
        var service = new CountingInsightsService();
        await InsightsPageViewModel.CaptureSampleSnapshotAsync(Company, service);

        InsightsPageViewModel.ClearSampleSnapshot();
        var vm = new InsightsPageViewModel(service);

        Assert.True(vm.ShowTeaser);
    }
}
