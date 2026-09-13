using System.Collections.ObjectModel;
using ArgoBooks.Core;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Insights;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using ArgoBooks.Controls;
using ArgoBooks.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Represents a single insight item for display in the UI.
/// </summary>
public partial class InsightItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _recommendation;

    [ObservableProperty]
    private IBrush _statusColor = Brushes.Gray;

    [ObservableProperty]
    private bool _isLastItem;

    public bool HasRecommendation => !string.IsNullOrEmpty(Recommendation);
}

/// <summary>
/// ViewModel for the Insights page displaying AI-powered business insights.
/// Uses the InsightsService for real statistical analysis of business data.
/// </summary>
public partial class InsightsPageViewModel : ViewModelBase, ICleanupViewModel
{
    #region Services

    private readonly IInsightsService _insightsService;
    private readonly IForecastAccuracyService _forecastAccuracyService;
    private readonly ILocalMLForecastingService _mlForecastingService;

    #endregion

    #region Backtest Loading State

    [ObservableProperty]
    private bool _isRunningBacktest;

    [ObservableProperty]
    private string _backtestProgressMessage = "Analyzing historical data...";

    [ObservableProperty]
    private int _backtestProgressCurrent;

    [ObservableProperty]
    private int _backtestProgressTotal;

    /// <summary>
    /// Shows the backtest progress as a percentage (0-100).
    /// </summary>
    public double BacktestProgressPercent =>
        BacktestProgressTotal > 0 ? (double)BacktestProgressCurrent / BacktestProgressTotal * 100 : 0;

    partial void OnBacktestProgressCurrentChanged(int value)
    {
        OnPropertyChanged(nameof(BacktestProgressPercent));
    }

    partial void OnBacktestProgressTotalChanged(int value)
    {
        OnPropertyChanged(nameof(BacktestProgressPercent));
    }

    #endregion

    #region Premium Teaser

    [ObservableProperty]
    private bool _hasPremium;

    /// <summary>
    /// Shows the teaser overlay when the user doesn't have premium.
    /// </summary>
    public bool ShowTeaser => !HasPremium && !ShowsSampleSnapshot;

    // The sample company's insights, worked out once from the sample as it opened. Held here rather
    // than recalculated from the open company, so edits, an import or a Save As can't feed it the
    // user's own figures: on the free plan that would be premium insights for nothing.
    private static InsightsData? _sampleInsights;
    private static ForecastData? _sampleForecast;
    private static string _sampleAnalysisPeriod = string.Empty;

    /// <summary>A free user seeing the sample company's frozen insights instead of the teaser.</summary>
    public bool ShowsSampleSnapshot => !HasPremium && _sampleInsights != null;

    /// <summary>A frozen snapshot can't answer a different period, so the picker is locked.</summary>
    public bool CanChangeInsightsPeriod => !ShowsSampleSnapshot;

    /// <summary>
    /// Works out the sample company's insights at the page's default periods. Call once, as the
    /// sample opens, before anything in it can have been changed.
    /// </summary>
    public static async Task CaptureSampleSnapshotAsync(Core.Data.CompanyData companyData, IInsightsService? service = null)
    {
        service ??= new InsightsService();

        const string insightsPeriod = "Last 3 Months";
        var insights = await service.GenerateInsightsAsync(companyData, AnalysisDateRange.FromPreset(insightsPeriod, companyData.GetEarliestDate()));

        var (forecastStart, forecastEnd) = DatePresetNames.GetDateRange(DatePresetNames.NextMonth);
        var forecast = await service.GenerateForecastAsync(companyData, AnalysisDateRange.Custom(forecastStart, forecastEnd));

        _sampleInsights = insights;
        _sampleForecast = forecast;
        _sampleAnalysisPeriod = $"Based on {insightsPeriod.ToLowerInvariant()} of data";
    }

    /// <summary>Drops the sample's insights. Only on closing the company, never on Save As.</summary>
    public static void ClearSampleSnapshot()
    {
        _sampleInsights = null;
        _sampleForecast = null;
        _sampleAnalysisPeriod = string.Empty;
    }

    /// <summary>For a page built before the snapshot finished, so it switches from the teaser.</summary>
    public void OnSampleSnapshotChanged()
    {
        OnPropertyChanged(nameof(ShowTeaser));
        OnPropertyChanged(nameof(ShowsSampleSnapshot));
        OnPropertyChanged(nameof(CanChangeInsightsPeriod));
        if (!HasPremium)
            ShowFreeContent();
    }

    private void ShowFreeContent()
    {
        if (ShowsSampleSnapshot)
            ApplySampleSnapshot();
        else
            PopulateSampleData();
    }

    private void ApplySampleSnapshot()
    {
        if (_sampleInsights is not { } insights)
            return;

        InsightsAnalysisPeriod = _sampleAnalysisPeriod;

        if (!insights.HasSufficientData)
        {
            HasInsufficientData = true;
            InsufficientDataMessage = insights.InsufficientDataMessage ?? "Insufficient data for analysis.";
            ClearInsights();
            return;
        }

        HasInsufficientData = false;
        ApplyInsights(insights);
        if (_sampleForecast != null)
            UpdateForecastDisplay(_sampleForecast);
    }

    private void ApplyInsights(InsightsData insights)
    {
        TotalInsights = insights.Summary.TotalInsights.ToString();
        TrendsDetected = insights.Summary.TrendsDetected.ToString();
        AnomaliesDetected = insights.Summary.AnomaliesDetected.ToString();
        Opportunities = insights.Summary.Opportunities.ToString();

        // Update insight collections (these don't change with date range)
        UpdateInsightCollection(RevenueTrends, insights.RevenueTrends);
        UpdateInsightCollection(Anomalies, insights.Anomalies);
        UpdateInsightCollection(Forecasts, insights.Forecasts);
        UpdateInsightCollection(Recommendations, insights.Recommendations);
    }

    partial void OnHasPremiumChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTeaser));
        OnPropertyChanged(nameof(ShowsSampleSnapshot));
        OnPropertyChanged(nameof(CanChangeInsightsPeriod));
        if (value)
        {
            // Switched to premium, load real data
            _ = RefreshInsightsAsync();
        }
        else
        {
            ShowFreeContent();
        }
    }

    [RelayCommand]
    private void Upgrade()
    {
        App.OpenUpgradeModal("insights");
    }

    /// <summary>
    /// Populates the page with realistic-looking sample data for the teaser.
    /// </summary>
    private void PopulateSampleData()
    {
        HasInsufficientData = false;

        TotalInsights = "12";
        TrendsDetected = "5";
        AnomaliesDetected = "2";
        Opportunities = "3";

        // Build a synthetic ForecastData for the Conservative/Baseline/Optimistic options
        var sampleForecast = new ForecastData
        {
            ForecastedRevenue = 24800m,
            ForecastedRevenueLower = 21400m,
            ForecastedRevenueUpper = 28200m,
            RevenueGrowthPercent = 12.4m,
            RevenueGrowthPercentLower = -3.0m,
            RevenueGrowthPercentUpper = 27.8m,

            ForecastedExpenses = 18200m,
            ForecastedExpensesLower = 17200m,
            ForecastedExpensesUpper = 19000m,
            ExpenseGrowthPercent = 3.1m,
            ExpenseGrowthPercentLower = -2.5m,
            ExpenseGrowthPercentUpper = 7.7m,

            ForecastedProfit = 6600m,
            ForecastedProfitLower = 2400m,
            ForecastedProfitUpper = 11000m,
            ProfitGrowthPercent = 28.7m,
            ProfitGrowthPercentLower = -53.2m,
            ProfitGrowthPercentUpper = 114.5m,

            ExpectedNewCustomers = 47,
            ExpectedNewCustomersLower = 39,
            ExpectedNewCustomersUpper = 56,
            CustomerGrowthPercent = 8.2m,
            CustomerGrowthPercentLower = -10.3m,
            CustomerGrowthPercentUpper = 28.7m
        };
        _latestForecast = sampleForecast;
        ApplyForecastToCards(sampleForecast, convertFromUsd: false);

        PredictionConfidence = "82% High";
        DataMonthsNote = "Based on 14 months of data using Linear Regression";
        ForecastMethod = "Linear Regression";
        RevenueRange = $"Range: {CurrencyService.CurrentSymbol}21,400 - {CurrencyService.CurrentSymbol}28,200";
        ExpensesRange = $"Range: {CurrencyService.CurrentSymbol}17,200 - {CurrencyService.CurrentSymbol}19,000";
        HasAccuracyData = true;
        HistoricalAccuracy = "79%";
        AccuracyDescription = string.Empty;
        ValidatedForecastsNote = "Based on 8 validated forecasts";
        HasSeasonalPattern = true;
        SeasonalPatternDescription = "Revenue peaks in Q4 with 18% above average";
        TrendDirection = "Up";

        RevenueTrends.Clear();
        RevenueTrends.Add(new InsightItemViewModel
        {
            Title = "Consistent Monthly Growth",
            Description = "Revenue has increased by an average of 8.3% month-over-month for the past quarter.",
            Recommendation = "Consider increasing inventory to meet growing demand.",
            StatusColor = new SolidColorBrush(Color.Parse(AppColors.Success)),
            IsLastItem = false
        });
        RevenueTrends.Add(new InsightItemViewModel
        {
            Title = "Top Category Performance",
            Description = "Services revenue outpacing product sales by 2.1x this quarter.",
            StatusColor = new SolidColorBrush(Color.Parse(AppColors.Primary)),
            IsLastItem = true
        });

        Anomalies.Clear();
        Anomalies.Add(new InsightItemViewModel
        {
            Title = "Unusual Expense Spike",
            Description = "Office supplies spending was 340% above the 3-month average in the last period.",
            Recommendation = "Review recent purchases for potential billing errors.",
            StatusColor = new SolidColorBrush(Color.Parse(AppColors.Warning)),
            IsLastItem = true
        });

        Recommendations.Clear();
        Recommendations.Add(new InsightItemViewModel
        {
            Title = "Optimize Payment Terms",
            Description = "32% of invoices are paid late. Offering early payment discounts could improve cash flow.",
            Recommendation = "Consider a 2% discount for payments within 10 days.",
            StatusColor = new SolidColorBrush(Color.Parse(AppColors.Primary)),
            IsLastItem = false
        });
        Recommendations.Add(new InsightItemViewModel
        {
            Title = "Reduce Supplier Costs",
            Description = "3 suppliers have increased prices by over 10% this year. Alternatives may be available.",
            StatusColor = new SolidColorBrush(Color.Parse(AppColors.Violet)),
            IsLastItem = true
        });

        OnPropertyChanged(nameof(HasNoRevenueTrends));
        OnPropertyChanged(nameof(HasNoAnomalies));
        OnPropertyChanged(nameof(HasNoForecasts));
        OnPropertyChanged(nameof(HasNoRecommendations));
    }

    #endregion

    #region Statistics

    [ObservableProperty]
    private string _totalInsights = "0";

    [ObservableProperty]
    private string _trendsDetected = "0";

    [ObservableProperty]
    private string _anomaliesDetected = "0";

    [ObservableProperty]
    private string _opportunities = "0";

    [ObservableProperty]
    private string _lastUpdated = "Never";

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _hasInsufficientData;

    [ObservableProperty]
    private string _insufficientDataMessage = string.Empty;

    #endregion

    #region Date Range

    /// <summary>
    /// Available date range options for insights (future-focused).
    /// </summary>
    public ObservableCollection<string> DateRangeOptions { get; } = new(DatePresetNames.FutureDateRangeOptions);

    [ObservableProperty]
    private int _selectedDateRangeIndex;

    [ObservableProperty]
    private DateTime _startDate;

    [ObservableProperty]
    private DateTime _endDate;

    /// <summary>
    /// Most recent forecast result, retained so we can re-render cards when the scenario mode changes
    /// without re-running the ML pipeline.
    /// </summary>
    private ForecastData? _latestForecast;

    [ObservableProperty]
    private int _selectedScenarioModeIndex = (int)ForecastScenario.Baseline;

    [ObservableProperty]
    private bool _scenarioToggleEnabled;

    [ObservableProperty]
    private string _scenarioOutlookHint = string.Empty;

    partial void OnSelectedScenarioModeIndexChanged(int value)
    {
        if (_latestForecast != null)
        {
            ApplyForecastToCards(_latestForecast);
        }
        else
        {
            UpdateScenarioOutlookHint();
        }
    }

    private void UpdateScenarioOutlookHint()
    {
        ScenarioOutlookHint = (ForecastScenario)SelectedScenarioModeIndex switch
        {
            ForecastScenario.Conservative => "Conservative outlook: revenue, profit, and customers at the low end of the range; expenses at the high end.",
            ForecastScenario.Optimistic => "Optimistic outlook: revenue, profit, and customers at the high end of the range; expenses at the low end.",
            _ => "Baseline outlook: the middle of the forecast range, between conservative and optimistic."
        };
    }

    /// <summary>
    /// Gets or sets the currently selected date range string.
    /// </summary>
    public string SelectedDateRange
    {
        get => DateRangeOptions.Count > SelectedDateRangeIndex
            ? DateRangeOptions[SelectedDateRangeIndex]
            : "Next Month";
        set
        {
            if (string.IsNullOrEmpty(value)) return;

            var index = DateRangeOptions.IndexOf(value);
            if (index >= 0 && index != SelectedDateRangeIndex)
            {
                SelectedDateRangeIndex = index;
            }
        }
    }

    partial void OnSelectedDateRangeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedDateRange));
        UpdateDateRangeFromSelection();

        // Only refresh the forecast cards, not the insights
        _ = RefreshForecastAsync();
    }

    /// <summary>
    /// Updates the start and end dates based on the selected future date range option.
    /// </summary>
    private void UpdateDateRangeFromSelection()
    {
        var (start, end) = DatePresetNames.GetDateRange(SelectedDateRange switch
        {
            "Next Month" => DatePresetNames.NextMonth,
            "Next Quarter" => DatePresetNames.NextQuarter,
            "Next Year" => DatePresetNames.NextYear,
            "Next 30 Days" => DatePresetNames.NextMonthToDate,
            "Next 90 Days" => DatePresetNames.NextQuarterToDate,
            "Next 365 Days" => DatePresetNames.NextYearToDate,
            _ => DatePresetNames.NextMonth
        });

        StartDate = start;
        EndDate = end;

        // Update forecast date range label to show exact dates
        ForecastDateRangeLabel = $"{start:MMM d, yyyy} - {end:MMM d, yyyy}";

        // Update forecast card labels based on selected period
        switch (SelectedDateRange)
        {
            case "Next Quarter":
                RevenueLabel = "Next Quarter Revenue";
                ExpensesLabel = "Next Quarter Expenses";
                ProfitLabel = "Projected Quarterly Profit";
                CustomersLabel = "Expected New Customers (Quarter)";
                ComparisonLabel = "vs quarterly avg";
                break;
            case "Next Year":
                RevenueLabel = "Next Year Revenue";
                ExpensesLabel = "Next Year Expenses";
                ProfitLabel = "Projected Annual Profit";
                CustomersLabel = "Expected New Customers (Year)";
                ComparisonLabel = "vs annual avg";
                break;
            case "Next 30 Days":
                RevenueLabel = "Next 30 Days Revenue";
                ExpensesLabel = "Next 30 Days Expenses";
                ProfitLabel = "Projected Profit (30 Days)";
                CustomersLabel = "Expected New Customers (30 Days)";
                ComparisonLabel = "vs 30-day avg";
                break;
            case "Next 90 Days":
                RevenueLabel = "Next 90 Days Revenue";
                ExpensesLabel = "Next 90 Days Expenses";
                ProfitLabel = "Projected Profit (90 Days)";
                CustomersLabel = "Expected New Customers (90 Days)";
                ComparisonLabel = "vs 90-day avg";
                break;
            case "Next 365 Days":
                RevenueLabel = "Next 365 Days Revenue";
                ExpensesLabel = "Next 365 Days Expenses";
                ProfitLabel = "Projected Annual Profit";
                CustomersLabel = "Expected New Customers (Year)";
                ComparisonLabel = "vs annual avg";
                break;
            default: // Next Month
                RevenueLabel = "Next Month Revenue";
                ExpensesLabel = "Next Month Expenses";
                ProfitLabel = "Projected Profit";
                CustomersLabel = "Expected New Customers";
                ComparisonLabel = "vs monthly avg";
                break;
        }
    }

    #endregion

    #region Forecasted Growth

    [ObservableProperty]
    private string _forecastedRevenue = "$0";

    [ObservableProperty]
    private double _revenueGrowthValue;

    [ObservableProperty]
    private string _revenueGrowth = "0%";

    [ObservableProperty]
    private string _forecastedExpenses = "$0";

    [ObservableProperty]
    private double _expenseGrowthValue;

    [ObservableProperty]
    private string _expenseGrowth = "0%";

    [ObservableProperty]
    private string _forecastedProfit = "$0";

    /// <summary>Green for a projected profit, red for a projected loss.</summary>
    [ObservableProperty]
    private StatCardColor _profitValueColor = StatCardColor.Success;

    [ObservableProperty]
    private double _profitGrowthValue;

    [ObservableProperty]
    private string _profitGrowth = "0%";

    [ObservableProperty]
    private string _forecastedCustomers = "0";

    [ObservableProperty]
    private double _customerGrowthValue;

    [ObservableProperty]
    private string _customerGrowth = "0%";

    [ObservableProperty]
    private string _predictionConfidence = "-- --";

    [ObservableProperty]
    private string _dataMonthsNote = "Insufficient data for forecasting";

    // Dynamic labels based on selected forecast period
    [ObservableProperty]
    private string _revenueLabel = "Next Month Revenue";

    [ObservableProperty]
    private string _expensesLabel = "Next Month Expenses";

    [ObservableProperty]
    private string _profitLabel = "Projected Profit";

    [ObservableProperty]
    private string _customersLabel = "Expected New Customers";

    /// <summary>
    /// Dynamic comparison label that shows what period the forecast is compared against.
    /// </summary>
    [ObservableProperty]
    private string _comparisonLabel = "vs monthly avg";

    /// <summary>
    /// Shows the exact date range being forecasted.
    /// </summary>
    [ObservableProperty]
    private string _forecastDateRangeLabel = string.Empty;

    /// <summary>
    /// The forecasting method used (e.g., "Combined (SSA + Holt-Winters)").
    /// </summary>
    [ObservableProperty]
    private string _forecastMethod = string.Empty;

    /// <summary>
    /// Revenue forecast range (lower - upper bounds).
    /// </summary>
    [ObservableProperty]
    private string _revenueRange = string.Empty;

    /// <summary>
    /// Expenses forecast range (lower - upper bounds).
    /// </summary>
    [ObservableProperty]
    private string _expensesRange = string.Empty;

    /// <summary>
    /// Whether historical accuracy data is available.
    /// </summary>
    [ObservableProperty]
    private bool _hasAccuracyData;

    /// <summary>
    /// Historical forecast accuracy percentage.
    /// </summary>
    [ObservableProperty]
    private string _historicalAccuracy = string.Empty;

    /// <summary>
    /// Description of the forecast accuracy.
    /// </summary>
    [ObservableProperty]
    private string _accuracyDescription = string.Empty;

    /// <summary>
    /// Number of validated forecasts used for accuracy calculation.
    /// </summary>
    [ObservableProperty]
    private string _validatedForecastsNote = string.Empty;

    /// <summary>
    /// Whether a seasonal pattern was detected.
    /// </summary>
    [ObservableProperty]
    private bool _hasSeasonalPattern;

    /// <summary>
    /// Description of the detected seasonal pattern.
    /// </summary>
    [ObservableProperty]
    private string _seasonalPatternDescription = string.Empty;

    /// <summary>
    /// The detected trend direction.
    /// </summary>
    [ObservableProperty]
    private string _trendDirection = string.Empty;

    #endregion

    #region Info Modal

    /// <summary>
    /// Opens the prediction methodology info modal.
    /// </summary>
    [RelayCommand]
    private void ShowPredictionInfo()
    {
        App.PredictionInfoModalViewModel?.OpenCommand.Execute(null);
    }

    /// <summary>
    /// Opens the past predictions modal.
    /// </summary>
    [RelayCommand]
    private void ShowPastPredictions()
    {
        App.PastPredictionsModalViewModel?.OpenCommand.Execute(null);
    }

    #endregion

    #region Insights Period

    /// <summary>
    /// Available date range options for historical insights analysis.
    /// </summary>
    public ObservableCollection<string> InsightsDateRangeOptions { get; } = new(DatePresetNames.InsightsHistoricalDateRangeOptions);

    [ObservableProperty]
    private int _selectedInsightsDateRangeIndex = 1; // Default to "Last 3 Months"

    /// <summary>
    /// Gets or sets the currently selected insights date range string.
    /// </summary>
    public string SelectedInsightsDateRange
    {
        get => InsightsDateRangeOptions.Count > SelectedInsightsDateRangeIndex
            ? InsightsDateRangeOptions[SelectedInsightsDateRangeIndex]
            : "Last 3 Months";
        set
        {
            if (string.IsNullOrEmpty(value)) return;

            var index = InsightsDateRangeOptions.IndexOf(value);
            if (index >= 0 && index != SelectedInsightsDateRangeIndex)
            {
                SelectedInsightsDateRangeIndex = index;
            }
        }
    }

    partial void OnSelectedInsightsDateRangeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedInsightsDateRange));
        _ = RefreshInsightsAsync();
    }

    /// <summary>
    /// Description of the analysis period used for insights.
    /// </summary>
    [ObservableProperty]
    private string _insightsAnalysisPeriod = "Based on last 3 months of data";

    #endregion

    #region Insight Collections

    public ObservableCollection<InsightItemViewModel> RevenueTrends { get; } = [];
    public ObservableCollection<InsightItemViewModel> Anomalies { get; } = [];
    public ObservableCollection<InsightItemViewModel> Forecasts { get; } = [];
    public ObservableCollection<InsightItemViewModel> Recommendations { get; } = [];

    /// <summary>
    /// Indicates if the Revenue Trends collection is empty.
    /// </summary>
    public bool HasNoRevenueTrends => RevenueTrends.Count == 0;

    /// <summary>
    /// Indicates if the Anomalies collection is empty.
    /// </summary>
    public bool HasNoAnomalies => Anomalies.Count == 0;

    /// <summary>
    /// Indicates if the Forecasts collection is empty.
    /// </summary>
    public bool HasNoForecasts => Forecasts.Count == 0;

    /// <summary>
    /// Indicates if the Recommendations collection is empty.
    /// </summary>
    public bool HasNoRecommendations => Recommendations.Count == 0;

    #endregion

    /// <summary>
    /// Creates a new InsightsPageViewModel with the InsightsService.
    /// </summary>
    public InsightsPageViewModel()
    {
        // Instantiate services directly
        _insightsService = new InsightsService();
        _forecastAccuracyService = new ForecastAccuracyService();
        _mlForecastingService = new LocalMLForecastingService();

        App.PlanStatusChanged += OnPlanStatusChanged;
        CurrencyService.CurrencyChanged += OnCurrencyChanged;
        UpdateDateRangeFromSelection();

        if (HasPremium)
            _ = RefreshInsightsAsync();
        else
            ShowFreeContent();
    }

    /// <summary>
    /// Constructor for dependency injection.
    /// </summary>
    public InsightsPageViewModel(
        IInsightsService insightsService,
        IForecastAccuracyService? forecastAccuracyService = null,
        ILocalMLForecastingService? mlForecastingService = null)
    {
        _insightsService = insightsService;
        _forecastAccuracyService = forecastAccuracyService ?? new ForecastAccuracyService();
        _mlForecastingService = mlForecastingService ?? new LocalMLForecastingService();

        App.PlanStatusChanged += OnPlanStatusChanged;
        CurrencyService.CurrencyChanged += OnCurrencyChanged;
        UpdateDateRangeFromSelection();

        if (HasPremium)
            _ = RefreshInsightsAsync();
        else
            ShowFreeContent();
    }

    private void OnPlanStatusChanged(object? sender, PlanStatusChangedEventArgs e)
    {
        HasPremium = e.HasPremium;
    }

    // The displayed amounts are baked into the generated insight strings and forecast cards, so a
    // currency switch needs a full regenerate (like the other money pages), not just a rebind.
    private void OnCurrencyChanged(object? sender, EventArgs e)
    {
        if (HasPremium)
            _ = RefreshInsightsAsync();
        else
            ShowFreeContent();
    }

    /// <summary>
    /// Unsubscribes from the app-level events so this page VM can be garbage collected when the
    /// company is switched, instead of leaking and reacting forever.
    /// </summary>
    public void Cleanup()
    {
        App.PlanStatusChanged -= OnPlanStatusChanged;
        CurrencyService.CurrencyChanged -= OnCurrencyChanged;
    }

    /// <summary>
    /// Refreshes only the forecast cards based on the selected date range.
    /// </summary>
    private async Task RefreshForecastAsync()
    {
        if (!HasPremium) return;

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
        {
            return;
        }

        try
        {
            // Create date range for forecast using the future date preset
            var dateRange = AnalysisDateRange.Custom(StartDate, EndDate);

            // Generate forecast only
            var forecast = await _insightsService.GenerateForecastAsync(companyData, dateRange);

            // Warm today's rate so a non-USD company's forecast cards convert (they're "as of now"
            // projections, converted at today's rate) instead of briefly showing "Pending".
            await CurrencyService.TryWarmTodayRateAsync();

            UpdateForecastDisplay(forecast);
        }
        catch (Exception ex)
        {
            DataMonthsNote = $"Error refreshing forecast: {ex.Message}";
        }
    }

    /// <summary>
    /// Refreshes the insights data using the InsightsService.
    /// Insights use a standard historical period, not affected by date range selection.
    /// </summary>
    [RelayCommand]
    private async Task RefreshInsightsAsync()
    {
        if (IsRefreshing || !HasPremium) return;

        var companyData = App.CompanyManager?.CompanyData;
        var companySettings = App.CompanyManager?.CurrentCompanySettings;
        if (companyData == null || companySettings == null)
        {
            HasInsufficientData = true;
            InsufficientDataMessage = "No company data loaded. Please open or create a company file.";
            return;
        }

        IsRefreshing = true;
        HasInsufficientData = false;

        try
        {
            // Ensure today's rate is cached: a couple of "as of now" figures (the overdue-invoice
            // total, the forecast cards) convert at today's rate, and it isn't in the transaction-date
            // set that a currency switch preloads.
            await CurrencyService.TryWarmTodayRateAsync();

            // Check if we should run backtesting first
            await RunBacktestIfNeededAsync(companyData, companySettings);

            // Use the selected historical date range for insights
            var insightsDateRange = AnalysisDateRange.FromPreset(SelectedInsightsDateRange, companyData.GetEarliestDate());

            // Update the analysis period description
            InsightsAnalysisPeriod = SelectedInsightsDateRange == DateRangePreset.AllTime.GetDisplayName()
                ? "Based on all available data"
                : $"Based on {SelectedInsightsDateRange.ToLowerInvariant()} of data";

            // Generate insights using the service with historical data
            var insights = await _insightsService.GenerateInsightsAsync(companyData, insightsDateRange);

            if (!insights.HasSufficientData)
            {
                HasInsufficientData = true;
                InsufficientDataMessage = insights.InsufficientDataMessage ?? "Insufficient data for analysis.";
                ClearInsights();
                return;
            }

            ApplyInsights(insights);

            // Now update forecast based on selected date range
            var forecastDateRange = AnalysisDateRange.Custom(StartDate, EndDate);
            var forecast = await _insightsService.GenerateForecastAsync(companyData, forecastDateRange);
            await CurrencyService.TryWarmTodayRateAsync();
            UpdateForecastDisplay(forecast);

            LastUpdated = TimeZoneService.FormatTime(DateTime.Now);
        }
        catch (Exception ex)
        {
            HasInsufficientData = true;
            InsufficientDataMessage = $"Error generating insights: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Runs backtesting if needed to build historical accuracy data.
    /// Shows a loading screen during the process.
    /// </summary>
    private async Task RunBacktestIfNeededAsync(Core.Data.CompanyData companyData, CompanySettings settings)
    {
        if (!_forecastAccuracyService.ShouldRunBacktest(companyData, settings))
        {
            return;
        }

        // Show loading screen
        IsRunningBacktest = true;
        BacktestProgressMessage = "Analyzing historical data...";
        BacktestProgressCurrent = 0;
        BacktestProgressTotal = 0;

        try
        {
            // Create progress reporter
            var progress = new Progress<(int current, int total, string message)>(report =>
            {
                BacktestProgressCurrent = report.current;
                BacktestProgressTotal = report.total;
                BacktestProgressMessage = report.message;
            });

            var recordsCreated = await _forecastAccuracyService.RunBacktestAsync(
                companyData,
                settings,
                _mlForecastingService,
                minTrainingMonths: 3,
                progress: progress);

            if (recordsCreated > 0)
            {
                // Mark data as modified so it gets saved
                companyData.MarkAsModified();
                settings.ChangesMade = true;
            }
        }
        finally
        {
            IsRunningBacktest = false;
        }
    }

    /// <summary>
    /// Updates the forecast display with the generated data.
    /// </summary>
    private void UpdateForecastDisplay(ForecastData forecast)
    {
        _latestForecast = forecast;
        ApplyForecastToCards(forecast);

        PredictionConfidence = $"{forecast.ConfidenceScore:F0}% {forecast.ConfidenceLevel}";

        // Data months and method info
        var methodInfo = !string.IsNullOrEmpty(forecast.ForecastMethod)
            ? $" using {forecast.ForecastMethod}"
            : "";
        DataMonthsNote = forecast.DataMonthsUsed > 0
            ? $"Based on {forecast.DataMonthsUsed} months of data{methodInfo}"
            : "Insufficient data for forecasting";

        ForecastMethod = forecast.ForecastMethod ?? string.Empty;

        // Confidence bounds/ranges (always show the full envelope, independent of selected scenario)
        if (forecast.ForecastedRevenueLower > 0 || forecast.ForecastedRevenueUpper > 0)
        {
            RevenueRange = $"Range: {FormatForecastAmount(forecast.ForecastedRevenueLower)} - {FormatForecastAmount(forecast.ForecastedRevenueUpper)}";
        }
        else
        {
            RevenueRange = string.Empty;
        }

        if (forecast.ForecastedExpensesLower > 0 || forecast.ForecastedExpensesUpper > 0)
        {
            ExpensesRange = $"Range: {FormatForecastAmount(forecast.ForecastedExpensesLower)} - {FormatForecastAmount(forecast.ForecastedExpensesUpper)}";
        }
        else
        {
            ExpensesRange = string.Empty;
        }

        // Historical accuracy data
        HasAccuracyData = forecast.ValidatedForecastCount > 0;
        if (HasAccuracyData)
        {
            HistoricalAccuracy = forecast.HistoricalAccuracyPercent.HasValue
                ? $"{forecast.HistoricalAccuracyPercent.Value:F0}%"
                : "--";
            AccuracyDescription = forecast.AccuracyDescription ?? string.Empty;
            ValidatedForecastsNote = $"Based on {forecast.ValidatedForecastCount} validated {(forecast.ValidatedForecastCount == 1 ? "forecast" : "forecasts")}";
        }
        else
        {
            HistoricalAccuracy = string.Empty;
            AccuracyDescription = "No validated forecasts yet. Accuracy will be calculated after the current forecast period ends.";
            ValidatedForecastsNote = string.Empty;
        }

        HasSeasonalPattern = forecast.SeasonalInfo?.HasSeasonalPattern ?? false;
        if (HasSeasonalPattern && forecast.SeasonalInfo != null)
        {
            SeasonalPatternDescription = forecast.SeasonalInfo.Description;
            TrendDirection = forecast.SeasonalInfo.TrendDirection;
        }
        else
        {
            SeasonalPatternDescription = string.Empty;
            TrendDirection = string.Empty;
        }
    }

    /// <summary>
    /// Formats a USD forecast amount in the display currency as whole units (cents would be false
    /// precision on a projection). Forecasts are "as of now", so they convert at today's rate (warmed
    /// by the caller); shows the pending marker if that rate isn't cached yet.
    /// </summary>
    internal static string FormatForecastAmount(decimal amountUSD) =>
        CurrencyService.TryGetDisplayFromUSD(amountUSD, DateTime.Today, out var amount)
            ? CurrencyService.CurrentSymbol + amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            : CurrencyService.PendingMarker;

    /// <summary>
    /// Renders the four forecast cards (revenue, expenses, profit, customers) using the
    /// currently selected scenario mode. Conservative shows lower bounds for revenue/profit/customers
    /// and the upper bound for expenses (since high expenses are the conservative case); Optimistic
    /// is the inverse; Baseline shows the point forecast.
    /// </summary>
    private void ApplyForecastToCards(ForecastData forecast, bool convertFromUsd = true)
    {
        // Real forecast values are USD projections, so convert them to the display currency at today's
        // rate ("as of now"; warmed by the caller). The sample teaser passes convertFromUsd: false
        // because its numbers are illustrative, already in the display currency: format the symbol only,
        // never convert or show "Pending".
        string Card(decimal amount) => convertFromUsd
            ? FormatForecastAmount(amount)
            : CurrencyService.CurrentSymbol + amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        ScenarioToggleEnabled = forecast.ForecastedRevenueUpper > forecast.ForecastedRevenueLower
                             || forecast.ForecastedExpensesUpper > forecast.ForecastedExpensesLower
                             || forecast.ExpectedNewCustomersUpper > forecast.ExpectedNewCustomersLower;

        // If bounds aren't meaningful, snap selection back to Baseline so the tab/hint match the cards.
        // The property setter re-enters this method via OnSelectedScenarioModeIndexChanged, but the second
        // pass is a no-op (Index already Baseline) so it terminates after one extra render.
        if (SelectedScenarioModeIndex != (int)ForecastScenario.Baseline && !ScenarioToggleEnabled)
        {
            SelectedScenarioModeIndex = (int)ForecastScenario.Baseline;
        }

        var scenario = (ForecastScenario)SelectedScenarioModeIndex;

        var (revenue, revenueGrowth) = scenario switch
        {
            ForecastScenario.Conservative => (forecast.ForecastedRevenueLower, forecast.RevenueGrowthPercentLower),
            ForecastScenario.Optimistic => (forecast.ForecastedRevenueUpper, forecast.RevenueGrowthPercentUpper),
            _ => (forecast.ForecastedRevenue, forecast.RevenueGrowthPercent)
        };
        ForecastedRevenue = Card(revenue);
        RevenueGrowthValue = (double)revenueGrowth;
        RevenueGrowth = $"{Math.Abs(revenueGrowth):F1}%";

        // Expenses flip: conservative = high expenses (upper bound), optimistic = low expenses.
        var (expenses, expenseGrowth) = scenario switch
        {
            ForecastScenario.Conservative => (forecast.ForecastedExpensesUpper, forecast.ExpenseGrowthPercentUpper),
            ForecastScenario.Optimistic => (forecast.ForecastedExpensesLower, forecast.ExpenseGrowthPercentLower),
            _ => (forecast.ForecastedExpenses, forecast.ExpenseGrowthPercent)
        };
        ForecastedExpenses = Card(expenses);
        ExpenseGrowthValue = -(double)expenseGrowth;
        ExpenseGrowth = $"{Math.Abs(expenseGrowth):F1}%";

        var (profit, profitGrowth) = scenario switch
        {
            ForecastScenario.Conservative => (forecast.ForecastedProfitLower, forecast.ProfitGrowthPercentLower),
            ForecastScenario.Optimistic => (forecast.ForecastedProfitUpper, forecast.ProfitGrowthPercentUpper),
            _ => (forecast.ForecastedProfit, forecast.ProfitGrowthPercent)
        };
        ForecastedProfit = Card(profit);
        // Red for a projected loss, green for a projected profit.
        ProfitValueColor = profit < 0 ? StatCardColor.Danger : StatCardColor.Success;
        ProfitGrowthValue = (double)profitGrowth;
        ProfitGrowth = $"{Math.Abs(profitGrowth):F1}%";

        var (customers, customerGrowth) = scenario switch
        {
            ForecastScenario.Conservative => (forecast.ExpectedNewCustomersLower, forecast.CustomerGrowthPercentLower),
            ForecastScenario.Optimistic => (forecast.ExpectedNewCustomersUpper, forecast.CustomerGrowthPercentUpper),
            _ => (forecast.ExpectedNewCustomers, forecast.CustomerGrowthPercent)
        };
        ForecastedCustomers = customers.ToString();
        CustomerGrowthValue = (double)customerGrowth;
        CustomerGrowth = $"{Math.Abs(customerGrowth):F1}%";

        UpdateScenarioOutlookHint();
    }

    /// <summary>
    /// Updates an insight collection with new items.
    /// </summary>
    private void UpdateInsightCollection(ObservableCollection<InsightItemViewModel> collection, List<InsightItem> items)
    {
        collection.Clear();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            collection.Add(new InsightItemViewModel
            {
                Title = item.Title,
                Description = item.Description,
                Recommendation = item.Recommendation,
                StatusColor = GetStatusColor(item.Severity),
                IsLastItem = i == items.Count - 1
            });
        }

        // Notify empty state properties
        OnPropertyChanged(nameof(HasNoRevenueTrends));
        OnPropertyChanged(nameof(HasNoAnomalies));
        OnPropertyChanged(nameof(HasNoForecasts));
        OnPropertyChanged(nameof(HasNoRecommendations));
    }

    /// <summary>
    /// Gets the color brush for an insight severity level.
    /// </summary>
    private static IBrush GetStatusColor(InsightSeverity severity)
    {
        return severity switch
        {
            InsightSeverity.Success => new SolidColorBrush(Color.Parse(AppColors.Success)),  // Green
            InsightSeverity.Info => new SolidColorBrush(Color.Parse(AppColors.Primary)),     // Blue
            InsightSeverity.Warning => new SolidColorBrush(Color.Parse(AppColors.Warning)),  // Orange
            InsightSeverity.Critical => new SolidColorBrush(Color.Parse(AppColors.ExpenseRed)), // Red
            _ => new SolidColorBrush(Color.Parse(AppColors.Violet))                          // Purple (default)
        };
    }

    /// <summary>
    /// Clears all insight collections when data is insufficient.
    /// </summary>
    private void ClearInsights()
    {
        TotalInsights = "0";
        TrendsDetected = "0";
        AnomaliesDetected = "0";
        Opportunities = "0";

        _latestForecast = null;
        ScenarioToggleEnabled = false;
        ScenarioOutlookHint = string.Empty;

        ForecastedRevenue = "$0";
        RevenueGrowthValue = 0;
        RevenueGrowth = "0%";
        ForecastedExpenses = "$0";
        ExpenseGrowthValue = 0;
        ExpenseGrowth = "0%";
        ForecastedProfit = "$0";
        ProfitValueColor = StatCardColor.Success;
        ProfitGrowthValue = 0;
        ProfitGrowth = "0%";
        ForecastedCustomers = "0";
        CustomerGrowthValue = 0;
        CustomerGrowth = "0%";
        PredictionConfidence = "-- --";
        DataMonthsNote = "Insufficient data for forecasting";

        // Clear new ML/accuracy properties
        ForecastMethod = string.Empty;
        RevenueRange = string.Empty;
        ExpensesRange = string.Empty;
        HasAccuracyData = false;
        HistoricalAccuracy = string.Empty;
        AccuracyDescription = string.Empty;
        ValidatedForecastsNote = string.Empty;
        HasSeasonalPattern = false;
        SeasonalPatternDescription = string.Empty;
        TrendDirection = string.Empty;

        RevenueTrends.Clear();
        Anomalies.Clear();
        Forecasts.Clear();
        Recommendations.Clear();

        // Notify empty state properties
        OnPropertyChanged(nameof(HasNoRevenueTrends));
        OnPropertyChanged(nameof(HasNoAnomalies));
        OnPropertyChanged(nameof(HasNoForecasts));
        OnPropertyChanged(nameof(HasNoRecommendations));
    }
}
