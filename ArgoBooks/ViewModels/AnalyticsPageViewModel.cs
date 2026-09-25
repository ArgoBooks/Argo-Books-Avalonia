#pragma warning disable CS0618 // LabelVisual is obsolete. DrawnLabelVisual is not API-compatible
using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Helpers;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Geo;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.SkiaSharpView.VisualElements;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Analytics page.
/// Handles tab selection, date range filtering, chart type toggling, and chart data loading.
/// </summary>
public partial class AnalyticsPageViewModel : ChartContextMenuViewModelBase, ICleanupViewModel
{
    #region Services

    public ChartLoaderService ChartLoaderService { get; } = new();
    private CompanyManager? _companyManager;

    /// <summary>
    /// Gets the shared chart settings service.
    /// </summary>
    private ChartSettingsService ChartSettingsShared => ChartSettingsService.Instance;

    // Flag to prevent duplicate data loading when changing settings locally
    private bool _isLocalSettingChange;

    #endregion

    #region Tab Selection

    [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsDashboardTabSelected => SelectedTabIndex == 0;

    public bool IsGeographicTabSelected => SelectedTabIndex == 2;

    public bool IsOperationalTabSelected => SelectedTabIndex == 3;

    public bool IsPerformanceTabSelected => SelectedTabIndex == 4;

    public bool IsCustomersTabSelected => SelectedTabIndex == 5;

    public bool IsTaxesTabSelected => SelectedTabIndex == 6;

    public bool IsReturnsTabSelected => SelectedTabIndex == 7;

    public bool IsLossesTabSelected => SelectedTabIndex == 8;

    public bool IsRefundsTabSelected => SelectedTabIndex == 9;

    public bool IsProductsTabSelected => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsDashboardTabSelected));
        OnPropertyChanged(nameof(IsGeographicTabSelected));
        OnPropertyChanged(nameof(IsOperationalTabSelected));
        OnPropertyChanged(nameof(IsPerformanceTabSelected));
        OnPropertyChanged(nameof(IsCustomersTabSelected));
        OnPropertyChanged(nameof(IsReturnsTabSelected));
        OnPropertyChanged(nameof(IsLossesTabSelected));
        OnPropertyChanged(nameof(IsTaxesTabSelected));
        OnPropertyChanged(nameof(IsRefundsTabSelected));
        OnPropertyChanged(nameof(IsProductsTabSelected));

        if (IsRefundsTabSelected) RefreshRefundMetrics();

        // Make sure a product is selected when arriving on the Products tab so
        // the detail chart is populated even if the first load raced the tab.
        if (IsProductsTabSelected && SelectedProduct == null)
            SelectedProduct = Products.FirstOrDefault();
    }

    #endregion

    #region Products Tab

    /// <summary>
    /// All products sold in the period, ordered by revenue (highest first).
    /// Backs the product picker; the first entry is the default selection.
    /// </summary>
    public BatchObservableCollection<ProductSalesRow> Products { get; } = [];

    [ObservableProperty]
    private string _totalProductRevenue = string.Empty;

    [ObservableProperty]
    private string _totalProductUnits = string.Empty;

    [ObservableProperty]
    private string _avgProductSalePrice = string.Empty;

    [ObservableProperty]
    private string _productsSoldCount = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectPreviousProductCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectNextProductCommand))]
    private ProductSalesRow? _selectedProduct;

    // Per-product detail trend chart.
    [ObservableProperty]
    private ObservableCollection<ISeries> _productRevenueTrendSeries = [];

    [ObservableProperty]
    private Axis[] _productRevenueTrendXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _productRevenueTrendYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasProductTrendData;

    // Tight vertical padding keeps the title near the top of the card so the plot
    // gets as much height as possible (the card already names the product above).
    public LabelVisual ProductRevenueTrendTitle =>
        ChartLoaderService.CreateChartTitle(
            ChartDataType.ProductRevenueTrend.GetDisplayName(),
            new LiveChartsCore.Drawing.Padding(15, 2));

    /// <summary>
    /// Aggregates per-product sales for the current date range (cash-basis,
    /// matching the other analytics tabs) and refreshes the picker list + KPI cards.
    /// </summary>
    private void LoadProductSales(CompanyData data)
    {
        // Convert each sale at its OWN date during aggregation (Calculations.md §3a Phase 2), so
        // the per-product and total figures aren't re-priced at a single date. The resulting
        // amounts are already in the display currency.
        var rows = ProductSalesService.GetProductSales(data, StartDate, EndDate, cashBasis: true, CurrencyService.GetDisplayAmount)
            .Select(d => new ProductSalesRow(d))
            .OrderByDescending(r => r.RevenueUSD)
            .ToList();

        var totalRevenue = rows.Sum(r => r.RevenueUSD);
        var totalUnits = rows.Sum(r => r.UnitsSold);
        var avgPrice = totalUnits > 0 ? totalRevenue / totalUnits : 0;

        TotalProductRevenue = CurrencyService.Format(totalRevenue);
        TotalProductUnits = totalUnits.ToString("0.##");
        AvgProductSalePrice = CurrencyService.Format(avgPrice);
        ProductsSoldCount = rows.Count.ToString();

        Products.ReplaceAll(rows);

        // Keep the current selection if its product still exists; otherwise
        // auto-select the highest-revenue product so the chart is never empty.
        var previouslySelectedId = SelectedProduct?.ProductId;
        SelectedProduct =
            (previouslySelectedId != null ? rows.FirstOrDefault(r => r.ProductId == previouslySelectedId) : null)
            ?? rows.FirstOrDefault();
    }

    partial void OnSelectedProductChanged(ProductSalesRow? value)
    {
        ReloadProductRevenueTrend();
    }

    /// <summary>
    /// (Re)builds the selected product's revenue-trend series. Called when the
    /// selection changes and whenever the chart style changes (via LoadAllCharts),
    /// since the series geometry depends on the chosen chart type.
    /// </summary>
    private void ReloadProductRevenueTrend()
    {
        var value = SelectedProduct;
        var data = _companyManager?.CompanyData;
        if (value == null || data == null)
        {
            ProductRevenueTrendSeries = [];
            HasProductTrendData = false;
            return;
        }

        var (series, dates) = ChartLoaderService.LoadProductRevenueTrendChart(
            data, value.ProductId, StartDate, EndDate);
        ProductRevenueTrendSeries = series;
        ProductRevenueTrendXAxes = ChartLoaderService.CreateDateXAxes(dates);
        ProductRevenueTrendYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasProductTrendData = series.Count > 0;
    }

    /// <summary>Steps the picker to the previous product in the list.</summary>
    [RelayCommand(CanExecute = nameof(CanSelectPreviousProduct))]
    private void SelectPreviousProduct()
    {
        var index = SelectedProduct != null ? Products.IndexOf(SelectedProduct) : -1;
        if (index > 0)
            SelectedProduct = Products[index - 1];
    }

    private bool CanSelectPreviousProduct() =>
        SelectedProduct != null && Products.IndexOf(SelectedProduct) > 0;

    /// <summary>Steps the picker to the next product in the list.</summary>
    [RelayCommand(CanExecute = nameof(CanSelectNextProduct))]
    private void SelectNextProduct()
    {
        var index = SelectedProduct != null ? Products.IndexOf(SelectedProduct) : -1;
        if (index >= 0 && index < Products.Count - 1)
            SelectedProduct = Products[index + 1];
    }

    private bool CanSelectNextProduct() =>
        SelectedProduct != null && Products.IndexOf(SelectedProduct) < Products.Count - 1;

    #endregion

    #region Date Range

    /// <summary>
    /// Available date range options.
    /// </summary>
    public ObservableCollection<string> DateRangeOptions { get; } = new(DatePresetNames.StandardDateRangeOptions);

    /// <summary>
    /// Gets or sets the selected date range (delegates to shared service).
    /// </summary>
    public string SelectedDateRange
    {
        get => ChartSettingsShared.SelectedDateRange;
        set
        {
            if (ChartSettingsShared.SelectedDateRange != value)
            {
                var oldValue = ChartSettingsShared.SelectedDateRange;
                _isLocalSettingChange = true;
                try
                {
                    ChartSettingsShared.SelectedDateRange = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsCustomDateRange));
                    OnPropertyChanged(nameof(ComparisonPeriodLabel));
                    OnPropertyChanged(nameof(DateRangeDisplayText));

                    if (value == DateRangePreset.CustomRange.GetDisplayName())
                    {
                        OpenCustomDateRangeModal();
                    }
                    else if (oldValue != value)
                    {
                        // Explicitly notify even if value unchanged (service may have set it first)
                        ChartSettingsShared.HasAppliedCustomRange = false;
                        OnPropertyChanged(nameof(HasAppliedCustomRange));
                        OnPropertyChanged(nameof(AppliedDateRangeText));
                        LoadAllCharts();
                    }
                }
                finally
                {
                    _isLocalSettingChange = false;
                }
            }
        }
    }

    // Stores the previous selection before opening custom range modal
    private string _previousDateRange = DateRangePreset.ThisMonth.GetDisplayName();

    /// <summary>
    /// Gets or sets the start date (delegates to shared service).
    /// </summary>
    public DateTime StartDate
    {
        get => ChartSettingsShared.StartDate;
        set
        {
            if (ChartSettingsShared.StartDate != value)
            {
                ChartSettingsShared.StartDate = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the end date (delegates to shared service).
    /// </summary>
    public DateTime EndDate
    {
        get => ChartSettingsShared.EndDate;
        set
        {
            if (ChartSettingsShared.EndDate != value)
            {
                ChartSettingsShared.EndDate = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether a custom date range has been applied (delegates to shared service).
    /// </summary>
    public bool HasAppliedCustomRange
    {
        get => ChartSettingsShared.HasAppliedCustomRange;
        set
        {
            if (ChartSettingsShared.HasAppliedCustomRange != value)
            {
                ChartSettingsShared.HasAppliedCustomRange = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AppliedDateRangeText));
            }
        }
    }

    /// <summary>
    /// Gets the formatted text showing the applied custom date range.
    /// </summary>
    public string AppliedDateRangeText => ChartSettingsShared.AppliedDateRangeText;

    /// <summary>
    /// Gets the formatted text showing the currently selected date range span.
    /// </summary>
    public string DateRangeDisplayText => ChartSettingsShared.DateRangeDisplayText;

    /// <summary>
    /// Gets or sets the start date as DateTimeOffset for DatePicker binding.
    /// </summary>
    public DateTimeOffset? StartDateOffset
    {
        get => new DateTimeOffset(StartDate);
        set
        {
            if (value.HasValue)
            {
                StartDate = value.Value.DateTime;
                LoadAllCharts();
            }
        }
    }

    /// <summary>
    /// Gets or sets the end date as DateTimeOffset for DatePicker binding.
    /// </summary>
    public DateTimeOffset? EndDateOffset
    {
        get => new DateTimeOffset(EndDate);
        set
        {
            if (value.HasValue)
            {
                EndDate = value.Value.DateTime;
                LoadAllCharts();
            }
        }
    }

    /// <summary>
    /// Gets whether the custom date range option is selected.
    /// </summary>
    public bool IsCustomDateRange => SelectedDateRange == DateRangePreset.CustomRange.GetDisplayName();

    /// <summary>
    /// Gets the label for comparison period based on selected date range (delegates to shared service).
    /// </summary>
    public string ComparisonPeriodLabel => ChartSettingsShared.ComparisonPeriodLabel;

    /// <summary>
    /// Opens the custom date range modal.
    /// </summary>
    private void OpenCustomDateRangeModal()
    {
        // Store the previous selection before opening the modal
        // so we can restore it if the user cancels
        if (SelectedDateRange != DateRangePreset.CustomRange.GetDisplayName())
        {
            _previousDateRange = SelectedDateRange;
        }

        var earliestDate = _companyManager?.CompanyData?.GetEarliestDate() ?? StartDate;
        var modalStartDate = HasAppliedCustomRange ? StartDate : earliestDate;

        App.CustomDateRangeModal?.Open(modalStartDate, EndDate,
            onApply: (start, end) =>
            {
                StartDate = start.Date;
                // Inclusive end-of-day so transactions stored later in the
                // user's day (or with a UTC timestamp ahead of local time)
                // aren't filtered out of stat-card aggregations.
                EndDate = end.Date.AddDays(1).AddTicks(-1);
                HasAppliedCustomRange = true;
                OnPropertyChanged(nameof(AppliedDateRangeText));
                OnPropertyChanged(nameof(DateRangeDisplayText));
                LoadAllCharts();
            },
            onCancel: () =>
            {
                // If no custom range was previously applied, revert to the previous selection
                if (!HasAppliedCustomRange)
                {
                    ChartSettingsShared.SelectedDateRange = _previousDateRange;
                    OnPropertyChanged(nameof(SelectedDateRange));
                    OnPropertyChanged(nameof(DateRangeDisplayText));
                }
            });
    }

    #endregion

    #region Dashboard Tab Statistics

    [ObservableProperty]
    private string _totalPurchases = "$0.00";

    [ObservableProperty]
    private double? _purchasesChangeValue;

    [ObservableProperty]
    private string? _purchasesChangeText;

    [ObservableProperty]
    private string _totalRevenue = "$0.00";

    [ObservableProperty]
    private double? _revenueChangeValue;

    [ObservableProperty]
    private string? _revenueChangeText;

    [ObservableProperty]
    private string _netProfit = "$0.00";

    [ObservableProperty]
    private double? _profitChangeValue;

    [ObservableProperty]
    private string? _profitChangeText;

    [ObservableProperty]
    private string _profitMargin = "0.0%";

    [ObservableProperty]
    private double? _profitMarginChangeValue;

    [ObservableProperty]
    private string? _profitMarginChangeText;

    #endregion

    #region Operational Tab Statistics

    [ObservableProperty]
    private string _activeAccountants = "0";

    [ObservableProperty]
    private string _transactionsProcessed = "0";

    [ObservableProperty]
    private double? _transactionsChangeValue;

    [ObservableProperty]
    private string? _transactionsChangeText;

    [ObservableProperty]
    private string _avgProcessingTime = "N/A";

    [ObservableProperty]
    private double? _processingTimeChangeValue;

    [ObservableProperty]
    private string? _processingTimeChangeText;

    [ObservableProperty]
    private string _accuracyRate = "N/A";

    [ObservableProperty]
    private double? _accuracyChangeValue;

    [ObservableProperty]
    private string? _accuracyChangeText;

    #endregion

    #region Performance Tab Statistics

    [ObservableProperty]
    private string _revenueGrowth = "0.0%";

    [ObservableProperty]
    private double? _revenueGrowthChangeValue;

    [ObservableProperty]
    private string? _revenueGrowthChangeText;

    [ObservableProperty]
    private string _totalTransactions = "0";

    [ObservableProperty]
    private double? _totalTransactionsChangeValue;

    [ObservableProperty]
    private string? _totalTransactionsChangeText;

    [ObservableProperty]
    private string _avgTransactionValue = "$0.00";

    [ObservableProperty]
    private double? _avgTransactionChangeValue;

    [ObservableProperty]
    private string? _avgTransactionChangeText;

    [ObservableProperty]
    private string _avgShippingCost = "$0.00";

    [ObservableProperty]
    private double? _avgShippingChangeValue;

    [ObservableProperty]
    private string? _avgShippingChangeText;

    #endregion

    #region Customers Tab Statistics

    [ObservableProperty]
    private string _totalCustomers = "0";

    [ObservableProperty]
    private double? _customersChangeValue;

    [ObservableProperty]
    private string? _customersChangeText;

    [ObservableProperty]
    private string _newCustomers = "0";

    [ObservableProperty]
    private double? _newCustomersChangeValue;

    [ObservableProperty]
    private string? _newCustomersChangeText;

    [ObservableProperty]
    private string _retentionRate = "N/A";

    [ObservableProperty]
    private double? _retentionChangeValue;

    [ObservableProperty]
    private string? _retentionChangeText;

    [ObservableProperty]
    private string _avgCustomerValue = "$0.00";

    [ObservableProperty]
    private double? _avgCustomerValueChangeValue;

    [ObservableProperty]
    private string? _avgCustomerValueChangeText;

    #endregion

    #region Returns Tab Statistics

    [ObservableProperty]
    private string _totalReturns = "0";

    [ObservableProperty]
    private double? _returnsChangeValue;

    [ObservableProperty]
    private string? _returnsChangeText;

    [ObservableProperty]
    private string _returnRate = "0.0%";

    [ObservableProperty]
    private double? _returnRateChangeValue;

    [ObservableProperty]
    private string? _returnRateChangeText;

    [ObservableProperty]
    private string _returnsFinancialImpact = "$0.00";

    [ObservableProperty]
    private double? _returnsImpactChangeValue;

    [ObservableProperty]
    private string? _returnsImpactChangeText;

    [ObservableProperty]
    private string _avgResolutionTime = "N/A";

    [ObservableProperty]
    private double? _resolutionTimeChangeValue;

    [ObservableProperty]
    private string? _resolutionTimeChangeText;

    #endregion

    #region Losses Tab Statistics

    [ObservableProperty]
    private string _totalLosses = "0";

    [ObservableProperty]
    private double? _lossesChangeValue;

    [ObservableProperty]
    private string? _lossesChangeText;

    [ObservableProperty]
    private string _lossRate = "0.0%";

    [ObservableProperty]
    private double? _lossRateChangeValue;

    [ObservableProperty]
    private string? _lossRateChangeText;

    [ObservableProperty]
    private string _lossesFinancialImpact = "$0.00";

    [ObservableProperty]
    private double? _lossesImpactChangeValue;

    [ObservableProperty]
    private string? _lossesImpactChangeText;

    [ObservableProperty]
    private string _insuranceClaims = "0";

    [ObservableProperty]
    private double? _insuranceClaimsChangeValue;

    [ObservableProperty]
    private string? _insuranceClaimsChangeText;

    #endregion

    #region Taxes Tab Statistics

    [ObservableProperty]
    private string _totalTaxCollected = "$0.00";

    [ObservableProperty]
    private double? _taxCollectedChangeValue;

    [ObservableProperty]
    private string? _taxCollectedChangeText;

    [ObservableProperty]
    private string _totalTaxPaid = "$0.00";

    [ObservableProperty]
    private double? _taxPaidChangeValue;

    [ObservableProperty]
    private string? _taxPaidChangeText;

    [ObservableProperty]
    private string _netTaxLiability = "$0.00";

    [ObservableProperty]
    private double? _taxLiabilityChangeValue;

    [ObservableProperty]
    private string? _taxLiabilityChangeText;

    [ObservableProperty]
    private string _effectiveTaxRate = "0.0%";

    [ObservableProperty]
    private double? _effectiveTaxRateChangeValue;

    [ObservableProperty]
    private string? _effectiveTaxRateChangeText;

    #endregion

    #region Chart Type Toggle

    /// <summary>
    /// Available chart type options for the selector.
    /// </summary>
    public string[] ChartTypeOptions { get; } = ["Area", "Column", "Line", "Step Line", "Scatter"];

    /// <summary>
    /// Gets or sets the selected chart type (delegates to shared service).
    /// </summary>
    public string SelectedChartType
    {
        get => ChartSettingsShared.SelectedChartType;
        set
        {
            if (ChartSettingsShared.SelectedChartType != value)
            {
                _isLocalSettingChange = true;
                try
                {
                    ChartSettingsShared.SelectedChartType = value;
                    OnPropertyChanged();
                    LoadAllCharts(styleChangeOnly: true);
                }
                finally
                {
                    _isLocalSettingChange = false;
                }
            }
        }
    }

    #endregion

    #region Map Mode Toggle

    [ObservableProperty]
    private bool _isMapModeOrigin = true;

    public bool IsMapModeDestination
    {
        get => !IsMapModeOrigin;
        set
        {
            if (value != !IsMapModeOrigin)
            {
                IsMapModeOrigin = !value;
            }
        }
    }

    partial void OnIsMapModeOriginChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMapModeDestination));
        // No need to reload - we have two separate GeoMaps that show/hide based on mode
    }

    #endregion

    #region Dashboard Charts - Expenses/Purchases

    [ObservableProperty]
    private ObservableCollection<ISeries> _expensesTrendsSeries = [];

    [ObservableProperty]
    private Axis[] _expensesTrendsXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _expensesTrendsYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasExpensesTrendsData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _expensesDistributionSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _expensesDistributionLegend = [];

    [ObservableProperty]
    private bool _hasExpensesDistributionData;

    #endregion

    #region Dashboard Charts - Revenue/Sales

    [ObservableProperty]
    private ObservableCollection<ISeries> _revenueTrendsSeries = [];

    [ObservableProperty]
    private Axis[] _revenueTrendsXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _revenueTrendsYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasRevenueTrendsData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _revenueDistributionSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _revenueDistributionLegend = [];

    [ObservableProperty]
    private bool _hasRevenueDistributionData;

    #endregion

    #region Dashboard Charts - Profits

    [ObservableProperty]
    private ObservableCollection<ISeries> _profitTrendsSeries = [];

    [ObservableProperty]
    private Axis[] _profitTrendsXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _profitTrendsYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasProfitTrendsData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _revenueVsExpensesSeries = [];

    [ObservableProperty]
    private Axis[] _revenueVsExpensesXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _revenueVsExpensesYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasRevenueVsExpensesData;

    #endregion

    #region Geographic Charts

    [ObservableProperty]
    private ObservableCollection<ISeries> _countriesOfOriginSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _countriesOfOriginLegend = [];

    [ObservableProperty]
    private bool _hasCountriesOfOriginData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _companiesOfOriginSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _companiesOfOriginLegend = [];

    [ObservableProperty]
    private bool _hasCompaniesOfOriginData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _topCustomersSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _topCustomersLegend = [];

    [ObservableProperty]
    private bool _hasTopCustomersData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _countriesOfDestinationSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _countriesOfDestinationLegend = [];

    [ObservableProperty]
    private bool _hasCountriesOfDestinationData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _companiesOfDestinationSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _companiesOfDestinationLegend = [];

    [ObservableProperty]
    private bool _hasCompaniesOfDestinationData;

    [ObservableProperty]
    private ObservableCollection<IGeoSeries> _originGeoMapSeries = [];

    [ObservableProperty]
    private bool _hasOriginMapData;

    [ObservableProperty]
    private ObservableCollection<IGeoSeries> _destinationGeoMapSeries = [];

    [ObservableProperty]
    private bool _hasDestinationMapData;

    #endregion

    #region Operational Charts

    [ObservableProperty]
    private ObservableCollection<ISeries> _avgTransactionValueSeries = [];

    [ObservableProperty]
    private Axis[] _avgTransactionValueXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _avgTransactionValueYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasAvgTransactionValueData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _totalTransactionsSeries = [];

    [ObservableProperty]
    private Axis[] _totalTransactionsXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _totalTransactionsYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasTotalTransactionsData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _avgShippingCostsSeries = [];

    [ObservableProperty]
    private Axis[] _avgShippingCostsXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _avgShippingCostsYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasAvgShippingCostsData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _accountantsTransactionsSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _accountantsTransactionsLegend = [];

    [ObservableProperty]
    private bool _hasAccountantsTransactionsData;

    #endregion

    #region Performance Charts

    [ObservableProperty]
    private ObservableCollection<ISeries> _customerGrowthSeries = [];

    [ObservableProperty]
    private Axis[] _customerGrowthXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _customerGrowthYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasCustomerGrowthData;

    #endregion

    #region Returns Charts

    [ObservableProperty]
    private ObservableCollection<ISeries> _returnsOverTimeSeries = [];

    [ObservableProperty]
    private Axis[] _returnsOverTimeXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _returnsOverTimeYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasReturnsOverTimeData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _returnReasonsSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _returnReasonsLegend = [];

    [ObservableProperty]
    private bool _hasReturnReasonsData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _returnsByCategorySeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _returnsByCategoryLegend = [];

    [ObservableProperty]
    private bool _hasReturnsByCategoryData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _returnFinancialImpactSeries = [];

    [ObservableProperty]
    private Axis[] _returnFinancialImpactXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _returnFinancialImpactYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasReturnFinancialImpactData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _returnsByProductSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _returnsByProductLegend = [];

    [ObservableProperty]
    private bool _hasReturnsByProductData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _expenseVsRevenueReturnsSeries = [];

    [ObservableProperty]
    private Axis[] _expenseVsRevenueReturnsXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _expenseVsRevenueReturnsYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasExpenseVsRevenueReturnsData;

    #endregion

    #region Customer Charts

    [ObservableProperty]
    private ObservableCollection<ISeries> _customerPaymentStatusSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _customerPaymentStatusLegend = [];

    [ObservableProperty]
    private bool _hasCustomerPaymentStatusData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _activeInactiveCustomersSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _activeInactiveCustomersLegend = [];

    [ObservableProperty]
    private bool _hasActiveInactiveCustomersData;

    #endregion

    #region Losses Charts

    [ObservableProperty]
    private ObservableCollection<ISeries> _lossesOverTimeSeries = [];

    [ObservableProperty]
    private Axis[] _lossesOverTimeXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _lossesOverTimeYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasLossesOverTimeData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _lossFinancialImpactSeries = [];

    [ObservableProperty]
    private Axis[] _lossFinancialImpactXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _lossFinancialImpactYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasLossFinancialImpactData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _lossReasonsSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _lossReasonsLegend = [];

    [ObservableProperty]
    private bool _hasLossReasonsData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _lossesByProductSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _lossesByProductLegend = [];

    [ObservableProperty]
    private bool _hasLossesByProductData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _lossesByCategorySeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _lossesByCategoryLegend = [];

    [ObservableProperty]
    private bool _hasLossesByCategoryData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _expenseVsRevenueLossesSeries = [];

    [ObservableProperty]
    private Axis[] _expenseVsRevenueLossesXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _expenseVsRevenueLossesYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasExpenseVsRevenueLossesData;

    #endregion

    #region Taxes Charts

    [ObservableProperty]
    private ObservableCollection<ISeries> _taxCollectedVsPaidSeries = [];

    [ObservableProperty]
    private Axis[] _taxCollectedVsPaidXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _taxCollectedVsPaidYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasTaxCollectedVsPaidData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _taxLiabilityTrendSeries = [];

    [ObservableProperty]
    private Axis[] _taxLiabilityTrendXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _taxLiabilityTrendYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasTaxLiabilityTrendData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _taxByCategorySeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _taxByCategoryLegend = [];

    [ObservableProperty]
    private bool _hasTaxByCategoryData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _taxRateDistributionSeries = [];

    [ObservableProperty]
    private Axis[] _taxRateDistributionXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _taxRateDistributionYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasTaxRateDistributionData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _taxByProductSeries = [];

    [ObservableProperty]
    private ObservableCollection<PieLegendItem> _taxByProductLegend = [];

    [ObservableProperty]
    private bool _hasTaxByProductData;

    [ObservableProperty]
    private ObservableCollection<ISeries> _expenseVsRevenueTaxSeries = [];

    [ObservableProperty]
    private Axis[] _expenseVsRevenueTaxXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _expenseVsRevenueTaxYAxes = [new Axis()];

    [ObservableProperty]
    private bool _hasExpenseVsRevenueTaxData;

    #endregion

    #region Empty State Date Range Detection

    /// <summary>
    /// True when revenue data exists in all time but the current date range filter is excluding it.
    /// </summary>
    [ObservableProperty]
    private bool _showRevenueDateRangeMessage;

    /// <summary>
    /// True when expense data exists in all time but the current date range filter is excluding it.
    /// </summary>
    [ObservableProperty]
    private bool _showExpenseDateRangeMessage;

    /// <summary>
    /// True when financial data (revenue or expenses) exists in all time but the current date range filter is excluding it.
    /// </summary>
    [ObservableProperty]
    private bool _showFinancialDateRangeMessage;

    /// <summary>
    /// True when the company has no income or expenses at all, so every chart is empty.
    /// </summary>
    [ObservableProperty]
    private bool _hasNoTransactions;

    /// <summary>
    /// True when return data exists in all time but the current date range filter is excluding it.
    /// </summary>
    [ObservableProperty]
    private bool _showReturnDateRangeMessage;

    /// <summary>
    /// True when loss data exists in all time but the current date range filter is excluding it.
    /// </summary>
    [ObservableProperty]
    private bool _showLossDateRangeMessage;

    /// <summary>
    /// True when rental data exists in all time but the current date range filter is excluding it.
    /// </summary>
    [ObservableProperty]
    private bool _showRentalDateRangeMessage;

    /// <summary>
    /// True when tax data exists in all time but the current date range filter is excluding it.
    /// </summary>
    [ObservableProperty]
    private bool _showTaxDateRangeMessage;

    #endregion

    #region Chart Titles

    // Dashboard Tab Chart Titles
    private string _profitOverTimeTitleText = ChartDataType.TotalProfits.GetDisplayName();
    public LabelVisual ProfitOverTimeTitle => ChartLoaderService.CreateChartTitle(_profitOverTimeTitleText);
    public LabelVisual RevenueVsExpensesTitle => ChartLoaderService.CreateChartTitle(ChartDataType.RevenueVsExpenses.GetDisplayName());
    public LabelVisual RevenueTrendsTitle => ChartLoaderService.CreateChartTitle(ChartDataType.TotalRevenue.GetDisplayName());
    public LabelVisual RevenueDistributionTitle => ChartLoaderService.CreateChartTitle(ChartDataType.RevenueDistribution.GetDisplayName());
    public LabelVisual ExpenseTrendsTitle => ChartLoaderService.CreateChartTitle(ChartDataType.TotalExpenses.GetDisplayName());
    public LabelVisual ExpenseDistributionTitle => ChartLoaderService.CreateChartTitle(ChartDataType.ExpensesDistribution.GetDisplayName());

    // Geographic Tab Chart Titles
    public LabelVisual CountriesOfOriginTitle => ChartLoaderService.CreateChartTitle(ChartDataType.CountriesOfOrigin.GetDisplayName());
    public LabelVisual CompaniesOfOriginTitle => ChartLoaderService.CreateChartTitle(ChartDataType.CompaniesOfOrigin.GetDisplayName());
    public LabelVisual CountriesOfDestinationTitle => ChartLoaderService.CreateChartTitle(ChartDataType.CountriesOfDestination.GetDisplayName());
    public LabelVisual CompaniesOfDestinationTitle => ChartLoaderService.CreateChartTitle(ChartDataType.CompaniesOfDestination.GetDisplayName());
    public LabelVisual WorldMapOverviewTitle => ChartLoaderService.CreateChartTitle(ChartDataType.WorldMap.GetDisplayName());

    // Operational Tab Chart Titles
    public LabelVisual TransactionsByAccountantTitle => ChartLoaderService.CreateChartTitle(ChartDataType.AccountantsTransactions.GetDisplayName());
    public LabelVisual WorkloadDistributionTitle => ChartLoaderService.CreateChartTitle(ChartDataType.TotalTransactions.GetDisplayName());

    // Performance Tab Chart Titles
    public LabelVisual AverageTransactionValueTitle => ChartLoaderService.CreateChartTitle(ChartDataType.AverageTransactionValue.GetDisplayName());
    public LabelVisual TotalTransactionsTitle => ChartLoaderService.CreateChartTitle(ChartDataType.TotalTransactions.GetDisplayName());
    public LabelVisual AverageShippingCostsTitle => ChartLoaderService.CreateChartTitle(ChartDataType.AverageShippingCosts.GetDisplayName());

    // Customers Tab Chart Titles
    public LabelVisual TopCustomersByRevenueTitle => ChartLoaderService.CreateChartTitle(ChartDataType.TopCustomersByRevenue.GetDisplayName());
    public LabelVisual CustomerPaymentStatusTitle => ChartLoaderService.CreateChartTitle(ChartDataType.CustomerPaymentStatus.GetDisplayName());
    public LabelVisual CustomerGrowthTitle => ChartLoaderService.CreateChartTitle(ChartDataType.CustomerGrowth.GetDisplayName());
    public LabelVisual CustomerLifetimeValueTitle => ChartLoaderService.CreateChartTitle(ChartDataType.CustomerLifetimeValue.GetDisplayName());
    public LabelVisual ActiveVsInactiveCustomersTitle => ChartLoaderService.CreateChartTitle(ChartDataType.ActiveVsInactiveCustomers.GetDisplayName());
    public LabelVisual RentalsPerCustomerTitle => ChartLoaderService.CreateChartTitle(ChartDataType.RentalsPerCustomer.GetDisplayName());

    // Returns Tab Chart Titles
    public LabelVisual ReturnsOverTimeTitle => ChartLoaderService.CreateChartTitle(ChartDataType.ReturnsOverTime.GetDisplayName());
    public LabelVisual ReturnReasonsTitle => ChartLoaderService.CreateChartTitle(ChartDataType.ReturnReasons.GetDisplayName());
    public LabelVisual FinancialImpactOfReturnsTitle => ChartLoaderService.CreateChartTitle(ChartDataType.ReturnFinancialImpact.GetDisplayName());
    public LabelVisual ReturnsByCategoryTitle => ChartLoaderService.CreateChartTitle(ChartDataType.ReturnsByCategory.GetDisplayName());
    public LabelVisual ReturnsByProductTitle => ChartLoaderService.CreateChartTitle(ChartDataType.ReturnsByProduct.GetDisplayName());
    public LabelVisual ExpenseVsRevenueReturnsTitle => ChartLoaderService.CreateChartTitle(ChartDataType.ExpenseVsRevenueReturns.GetDisplayName());

    // Losses Tab Chart Titles
    public LabelVisual LossesOverTimeTitle => ChartLoaderService.CreateChartTitle(ChartDataType.LossesOverTime.GetDisplayName());
    public LabelVisual LossReasonsTitle => ChartLoaderService.CreateChartTitle(ChartDataType.LossReasons.GetDisplayName());
    public LabelVisual FinancialImpactOfLossesTitle => ChartLoaderService.CreateChartTitle(ChartDataType.LossFinancialImpact.GetDisplayName());
    public LabelVisual LossesByCategoryTitle => ChartLoaderService.CreateChartTitle(ChartDataType.LossesByCategory.GetDisplayName());
    public LabelVisual LossesByProductTitle => ChartLoaderService.CreateChartTitle(ChartDataType.LossesByProduct.GetDisplayName());
    public LabelVisual ExpenseVsRevenueLossesTitle => ChartLoaderService.CreateChartTitle(ChartDataType.ExpenseVsRevenueLosses.GetDisplayName());

    // Taxes Tab Chart Titles
    public LabelVisual TaxCollectedVsPaidTitle => ChartLoaderService.CreateChartTitle(ChartDataType.TaxCollectedVsPaid.GetDisplayName());
    public LabelVisual TaxLiabilityTrendTitle => ChartLoaderService.CreateChartTitle(ChartDataType.TaxLiabilityTrend.GetDisplayName());
    public LabelVisual TaxByCategoryTitle => ChartLoaderService.CreateChartTitle(ChartDataType.TaxByCategory.GetDisplayName());
    public LabelVisual TaxRateDistributionTitle => ChartLoaderService.CreateChartTitle(ChartDataType.TaxRateDistribution.GetDisplayName());
    public LabelVisual TaxByProductTitle => ChartLoaderService.CreateChartTitle(ChartDataType.TaxByProduct.GetDisplayName());
    public LabelVisual ExpenseVsRevenueTaxTitle => ChartLoaderService.CreateChartTitle(ChartDataType.ExpenseVsRevenueTax.GetDisplayName());

    /// <summary>
    /// Chart title property names for batch notification.
    /// </summary>
    private static readonly string[] ChartTitlePropertyNames =
    [
        nameof(ProfitOverTimeTitle), nameof(RevenueVsExpensesTitle), nameof(RevenueTrendsTitle),
        nameof(RevenueDistributionTitle), nameof(ExpenseTrendsTitle), nameof(ExpenseDistributionTitle),
        nameof(CountriesOfOriginTitle), nameof(CompaniesOfOriginTitle), nameof(CountriesOfDestinationTitle),
        nameof(CompaniesOfDestinationTitle), nameof(WorldMapOverviewTitle), nameof(TransactionsByAccountantTitle),
        nameof(WorkloadDistributionTitle), nameof(AverageTransactionValueTitle),
        nameof(TotalTransactionsTitle), nameof(AverageShippingCostsTitle),
        nameof(TopCustomersByRevenueTitle), nameof(CustomerPaymentStatusTitle), nameof(CustomerGrowthTitle),
        nameof(CustomerLifetimeValueTitle), nameof(ActiveVsInactiveCustomersTitle), nameof(RentalsPerCustomerTitle),
        nameof(ReturnsOverTimeTitle), nameof(ReturnReasonsTitle), nameof(FinancialImpactOfReturnsTitle),
        nameof(ReturnsByCategoryTitle), nameof(ReturnsByProductTitle), nameof(ExpenseVsRevenueReturnsTitle),
        nameof(LossesOverTimeTitle), nameof(LossReasonsTitle), nameof(FinancialImpactOfLossesTitle),
        nameof(LossesByCategoryTitle), nameof(LossesByProductTitle), nameof(ExpenseVsRevenueLossesTitle),
        nameof(TaxCollectedVsPaidTitle), nameof(TaxLiabilityTrendTitle), nameof(TaxByCategoryTitle),
        nameof(TaxRateDistributionTitle), nameof(TaxByProductTitle), nameof(ExpenseVsRevenueTaxTitle),
        nameof(ProductRevenueTrendTitle)
    ];

    /// <summary>
    /// Notifies all chart title properties changed (for theme updates).
    /// </summary>
    private void NotifyAllChartTitlesChanged()
    {
        foreach (var propertyName in ChartTitlePropertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Default constructor.
    /// </summary>
    public AnalyticsPageViewModel()
    {
        // Initialize the shared chart settings service
        ChartSettingsShared.Initialize();

        // Subscribe to chart settings changes from other pages
        ChartSettingsShared.ChartTypeChanged += OnChartSettingsChartTypeChanged;
        ChartSettingsShared.DateRangeChanged += OnChartSettingsDateRangeChanged;

        // Subscribe to theme changes to reload charts with new colors
        ThemeService.Instance.ThemeChanged += OnThemeChanged;

        // Subscribe to date format changes to refresh charts
        DateFormatService.DateFormatChanged += OnDateFormatChanged;

        // Subscribe to max pie slices changes to refresh pie charts
        ChartSettingsService.MaxPieSlicesChanged += OnMaxPieSlicesChanged;

        // Subscribe to currency changes to refresh all monetary displays
        CurrencyService.CurrencyChanged += OnCurrencyChanged;
    }

    private void OnThemeChanged(object? sender, ThemeMode e)
    {
        LoadAllCharts();
        NotifyAllChartTitlesChanged();
    }

    private void OnDateFormatChanged(object? sender, EventArgs e)
    {
        LoadAllCharts();
    }

    private void OnMaxPieSlicesChanged(object? sender, EventArgs e)
    {
        LoadAllCharts();
    }

    private void OnCurrencyChanged(object? sender, EventArgs e)
    {
        LoadAllCharts();
    }

    private void OnChartSettingsChartTypeChanged(object? sender, string chartType)
    {
        // Only reload if the change came from another page
        if (!_isLocalSettingChange)
        {
            OnPropertyChanged(nameof(SelectedChartType));
            LoadAllCharts();
        }
    }

    private void OnChartSettingsDateRangeChanged(object? sender, string dateRange)
    {
        // Only reload if the change came from another page
        if (!_isLocalSettingChange)
        {
            OnPropertyChanged(nameof(SelectedDateRange));
            OnPropertyChanged(nameof(StartDate));
            OnPropertyChanged(nameof(EndDate));
            OnPropertyChanged(nameof(HasAppliedCustomRange));
            OnPropertyChanged(nameof(AppliedDateRangeText));
            OnPropertyChanged(nameof(DateRangeDisplayText));
            OnPropertyChanged(nameof(IsCustomDateRange));
            OnPropertyChanged(nameof(ComparisonPeriodLabel));
            LoadAllCharts();
        }
    }

    #endregion

    #region Chart Context Menu Overrides

    /// <summary>
    /// Event raised when a chart image should be saved.
    /// The View should subscribe to this and handle the actual save dialog.
    /// </summary>
    public event EventHandler<SaveChartImageEventArgs>? SaveChartImageRequested;

    /// <inheritdoc />
    protected override void OnSaveChartAsImage()
    {
        SaveChartImageRequested?.Invoke(this, new SaveChartImageEventArgs { ChartId = SelectedChartDataType?.GetDisplayName() ?? "" });
    }

    /// <summary>
    /// Event raised when exporting to Google Sheets starts or completes.
    /// </summary>
    public event EventHandler<GoogleSheetsExportEventArgs>? GoogleSheetsExportStatusChanged;

    /// <inheritdoc />
    protected override void OnExportToGoogleSheets()
    {
        // Fire and forget the async operation
        _ = ExportToGoogleSheetsAsync();
    }

    /// <summary>
    /// Exports the chart data to Google Sheets asynchronously.
    /// </summary>
    private async Task ExportToGoogleSheetsAsync()
    {
        var exportData = ChartLoaderService.GetGoogleSheetsExportData(SelectedChartDataType);
        if (exportData.Count == 0)
        {
            GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
            {
                IsSuccess = false,
                ErrorMessage = "No chart data to export."
            });
            return;
        }

        // Notify that export is starting (with cancellation support)
        var cts = new CancellationTokenSource();
        GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
        {
            IsExporting = true,
            CancellationTokenSource = cts
        });

        try
        {
            // Ensure Google is authorized (auto-initiates OAuth if needed)
            var isAuthenticated = await GoogleCredentialsManager.EnsureAuthenticatedAsync(cts.Token);
            if (!isAuthenticated)
            {
                GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
                {
                    IsSuccess = false,
                    ErrorMessage = "Google Sheets authorization was not completed. Please try again."
                });
                return;
            }

            var companyName = App.CompanyManager?.CurrentCompanyName ?? "Argo Books";
            var chartExportData = ChartLoaderService.GetExportDataForChart(SelectedChartDataType);
            var chartTitle = SelectedChartDataType?.GetDisplayName() ?? chartExportData?.ChartTitle ?? "Chart";

            // Use Pie chart type for distribution charts, match chart style for time-based charts
            GoogleSheetsService.ChartType chartType;
            if (chartExportData?.ChartType == ChartType.Distribution)
            {
                chartType = GoogleSheetsService.ChartType.Pie;
            }
            else
            {
                chartType = ChartLoaderService.SelectedChartStyle switch
                {
                    ChartStyle.Line => GoogleSheetsService.ChartType.Line,
                    ChartStyle.Area => GoogleSheetsService.ChartType.Area,
                    ChartStyle.StepLine => GoogleSheetsService.ChartType.StepLine,
                    ChartStyle.Scatter => GoogleSheetsService.ChartType.Scatter,
                    _ => GoogleSheetsService.ChartType.Column
                };
            }

            cts.Token.ThrowIfCancellationRequested();

            var googleSheetsService = new GoogleSheetsService(App.ErrorLogger);
            var url = await googleSheetsService.ExportFormattedDataToGoogleSheetsAsync(
                exportData,
                chartTitle,
                chartType,
                companyName,
                cts.Token
            );

            if (!string.IsNullOrEmpty(url))
            {
                // Open the spreadsheet in the browser
                var browserOpened = GoogleSheetsService.OpenInBrowser(url);

                GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
                {
                    IsSuccess = true,
                    SpreadsheetUrl = url
                });

                if (!browserOpened)
                {
                    var dialog = App.ConfirmationDialog;
                    if (dialog != null)
                    {
                        await dialog.ShowAsync(new ConfirmationDialogOptions
                        {
                            Title = "Browser Error".Translate(),
                            Message = "The spreadsheet was created but could not open in your browser. You can access it at:\n\n{0}".TranslateFormat(url),
                            PrimaryButtonText = "OK".Translate(),
                            SecondaryButtonText = null,
                            CancelButtonText = null
                        });
                    }
                }
            }
            else
            {
                GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
                {
                    IsSuccess = false,
                    ErrorMessage = "Failed to create spreadsheet."
                });
            }
        }
        catch (OperationCanceledException)
        {
            GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
            {
                IsSuccess = false,
                ErrorMessage = null // Cancelled by user, no error message needed
            });
        }
        catch (InvalidOperationException ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Google Sheets export failed - invalid operation");
            GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            });
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Google Sheets export failed");
            GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
            {
                IsSuccess = false,
                ErrorMessage = $"Failed to export to Google Sheets: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Event raised when exporting to Excel is requested.
    /// The View should subscribe to this and handle the file save dialog.
    /// </summary>
    public event EventHandler<ExcelExportEventArgs>? ExcelExportRequested;

    /// <inheritdoc />
    protected override void OnExportToExcel()
    {
        // Handle GeoMap (Region Map) export
        if (SelectedChartDataType == ChartDataType.WorldMap)
        {
            var data = _companyManager?.CompanyData;
            if (data == null) return;

            var isoData = IsMapModeOrigin
                ? ChartLoaderService.LoadWorldMapDataBySupplier(data, StartDate, EndDate)
                : ChartLoaderService.LoadWorldMapData(data, StartDate, EndDate);

            if (isoData.Count == 0) return;

            var displayData = CountryCodeMapping.ConvertGeoMapDataForExport(isoData);
            var mapTitle = IsMapModeOrigin ? "Countries of Origin" : "Countries of Destination";

            ExcelExportRequested?.Invoke(this, new ExcelExportEventArgs
            {
                ChartTitle = mapTitle,
                RegionMapData = displayData
            });
            return;
        }

        var exportData = ChartLoaderService.GetExportDataForChart(SelectedChartDataType);
        if (exportData == null || exportData.Labels.Length == 0)
        {
            // No data to export
            return;
        }

        var chartTitle = SelectedChartDataType?.GetDisplayName() ?? exportData.ChartTitle;

        // Raise event for View to handle file save dialog
        ExcelExportRequested?.Invoke(this, new ExcelExportEventArgs
        {
            ChartTitle = chartTitle,
            Labels = exportData.Labels,
            Values = exportData.Values,
            SeriesName = exportData.SeriesName,
            ChartType = exportData.ChartType,
            ChartStyle = ChartLoaderService.SelectedChartStyle,
            AdditionalSeries = exportData.AdditionalSeries
        });
    }

    /// <inheritdoc />
    protected override void OnResetChartZoom()
    {
        if (SelectedChartDataType is not { } chartType)
            return;

        // Map ChartDataType to the corresponding axes and reset zoom
        switch (chartType)
        {
            case ChartDataType.TotalProfits:
                ChartLoaderService.ResetZoom(ProfitTrendsXAxes, ProfitTrendsYAxes);
                break;
            case ChartDataType.RevenueVsExpenses:
                ChartLoaderService.ResetZoom(RevenueVsExpensesXAxes, RevenueVsExpensesYAxes);
                break;
            case ChartDataType.TotalRevenue:
                ChartLoaderService.ResetZoom(RevenueTrendsXAxes, RevenueTrendsYAxes);
                break;
            case ChartDataType.TotalExpenses:
                ChartLoaderService.ResetZoom(ExpensesTrendsXAxes, ExpensesTrendsYAxes);
                break;
            case ChartDataType.TotalTransactions:
                ChartLoaderService.ResetZoom(TotalTransactionsXAxes, TotalTransactionsYAxes);
                break;
            case ChartDataType.AverageTransactionValue:
                ChartLoaderService.ResetZoom(AvgTransactionValueXAxes, AvgTransactionValueYAxes);
                break;
            case ChartDataType.AverageShippingCosts:
                ChartLoaderService.ResetZoom(AvgShippingCostsXAxes, AvgShippingCostsYAxes);
                break;
            case ChartDataType.CustomerGrowth:
                ChartLoaderService.ResetZoom(CustomerGrowthXAxes, CustomerGrowthYAxes);
                break;
            case ChartDataType.CustomerLifetimeValue:
                ChartLoaderService.ResetZoom(AvgTransactionValueXAxes, AvgTransactionValueYAxes);
                break;
            case ChartDataType.RentalsPerCustomer:
                ChartLoaderService.ResetZoom(TotalTransactionsXAxes, TotalTransactionsYAxes);
                break;
            case ChartDataType.TaxCollectedVsPaid:
                ChartLoaderService.ResetZoom(TaxCollectedVsPaidXAxes, TaxCollectedVsPaidYAxes);
                break;
            case ChartDataType.TaxRateDistribution:
                ChartLoaderService.ResetZoom(TaxRateDistributionXAxes, TaxRateDistributionYAxes);
                break;
            case ChartDataType.TaxLiabilityTrend:
                ChartLoaderService.ResetZoom(TaxLiabilityTrendXAxes, TaxLiabilityTrendYAxes);
                break;
            case ChartDataType.ExpenseVsRevenueTax:
                ChartLoaderService.ResetZoom(ExpenseVsRevenueTaxXAxes, ExpenseVsRevenueTaxYAxes);
                break;
            case ChartDataType.ReturnsOverTime:
                ChartLoaderService.ResetZoom(ReturnsOverTimeXAxes, ReturnsOverTimeYAxes);
                break;
            case ChartDataType.ReturnFinancialImpact:
                ChartLoaderService.ResetZoom(ReturnFinancialImpactXAxes, ReturnFinancialImpactYAxes);
                break;
            case ChartDataType.ExpenseVsRevenueReturns:
                ChartLoaderService.ResetZoom(ExpenseVsRevenueReturnsXAxes, ExpenseVsRevenueReturnsYAxes);
                break;
            case ChartDataType.LossesOverTime:
                ChartLoaderService.ResetZoom(LossesOverTimeXAxes, LossesOverTimeYAxes);
                break;
            case ChartDataType.LossFinancialImpact:
                ChartLoaderService.ResetZoom(LossFinancialImpactXAxes, LossFinancialImpactYAxes);
                break;
            case ChartDataType.ExpenseVsRevenueLosses:
                ChartLoaderService.ResetZoom(ExpenseVsRevenueLossesXAxes, ExpenseVsRevenueLossesYAxes);
                break;
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the ViewModel with the company manager.
    /// </summary>
    public void Initialize(CompanyManager companyManager)
    {
        _companyManager = companyManager;
        LoadAllCharts();

        // Subscribe to data change events
        _companyManager.CompanyDataChanged += OnCompanyDataChanged;
    }

    /// <summary>
    /// Cleans up event subscriptions.
    /// </summary>
    public void Cleanup()
    {
        if (_companyManager != null)
        {
            _companyManager.CompanyDataChanged -= OnCompanyDataChanged;
        }

        // Unsubscribe from chart settings events
        ChartSettingsShared.ChartTypeChanged -= OnChartSettingsChartTypeChanged;
        ChartSettingsShared.DateRangeChanged -= OnChartSettingsDateRangeChanged;

        // Unsubscribe from theme, date format, pie slices, and currency changes
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
        DateFormatService.DateFormatChanged -= OnDateFormatChanged;
        ChartSettingsService.MaxPieSlicesChanged -= OnMaxPieSlicesChanged;
        CurrencyService.CurrencyChanged -= OnCurrencyChanged;

        // Zoom handlers are managed by ChartExpandOverlay (fullscreen only)
    }

    private void OnCompanyDataChanged(object? sender, EventArgs e)
    {
        LoadAllCharts();
    }

    [RelayCommand]
    private void AddExpense()
    {
        App.NavigationService?.NavigateTo("Expenses");
        App.ExpenseModalsViewModel?.OpenAddModal();
    }

    #endregion

    #region Chart Loading

    /// <summary>
    /// Loads all chart data.
    /// </summary>
    public void LoadAllCharts(bool styleChangeOnly = false)
    {
        var data = _companyManager?.CompanyData;
        if (data == null) return;

        // Update theme colors and chart style
        ChartLoaderService.UpdateThemeColors(ThemeService.Instance.IsDarkTheme);
        ChartLoaderService.SelectedChartStyle = SelectedChartType switch
        {
            "Line" => ChartStyle.Line,
            "Column" => ChartStyle.Column,
            "Step Line" => ChartStyle.StepLine,
            "Area" => ChartStyle.Area,
            "Scatter" => ChartStyle.Scatter,
            _ => ChartStyle.Line
        };

        if (!styleChangeOnly)
        {
            // Determine if a date range filter is active and data exists beyond it
            var isFiltered = SelectedDateRange != DateRangePreset.AllTime.GetDisplayName();
            ShowRevenueDateRangeMessage = isFiltered && data.Revenues.Count > 0;
            ShowExpenseDateRangeMessage = isFiltered && data.Expenses.Count > 0;
            ShowFinancialDateRangeMessage = isFiltered && (data.Revenues.Count > 0 || data.Expenses.Count > 0);
            ShowReturnDateRangeMessage = isFiltered && data.Returns.Count > 0;
            ShowLossDateRangeMessage = isFiltered && data.LostDamaged.Count > 0;
            ShowRentalDateRangeMessage = isFiltered && data.Rentals.Count > 0;
            ShowTaxDateRangeMessage = isFiltered && (data.Revenues.Any(r => r.TaxAmount > 0 || r.TaxAmountUSD > 0) ||
                                                      data.Expenses.Any(e => e.TaxAmount > 0 || e.TaxAmountUSD > 0));
            HasNoTransactions = data.Expenses.Count == 0 && data.Revenues.Count == 0;

            // Load statistics for stat cards
            LoadAllStatistics(data);
        }

        // Dashboard charts (cartesian)
        LoadExpensesTrendsChart(data);
        LoadRevenueTrendsChart(data);
        LoadProfitTrendsChart(data);
        LoadRevenueVsExpensesChart(data);

        // Operational charts (cartesian)
        LoadAvgTransactionValueChart(data);
        LoadTotalTransactionsChart(data);
        LoadAvgShippingCostsChart(data);

        // Performance charts (cartesian)
        LoadCustomerGrowthChart(data);

        // Returns charts (cartesian)
        LoadReturnsOverTimeChart(data);
        LoadReturnFinancialImpactChart(data);
        LoadExpenseVsRevenueReturnsChart(data);

        // Losses charts (cartesian)
        LoadLossesOverTimeChart(data);
        LoadLossFinancialImpactChart(data);
        LoadExpenseVsRevenueLossesChart(data);

        // Taxes charts (cartesian)
        LoadTaxCollectedVsPaidChart(data);
        LoadTaxLiabilityTrendChart(data);
        LoadTaxRateDistributionChart(data);
        LoadExpenseVsRevenueTaxChart(data);

        // Products detail chart (cartesian) reacts to chart-style changes too. On a data/filter
        // change LoadProductSales (below) reassigns SelectedProduct and reloads it, so only the
        // style-only path needs an explicit reload here (avoids reloading the trend twice).
        if (styleChangeOnly)
            ReloadProductRevenueTrend();

        // Pie charts and geo map are style-independent, only reload on data/filter changes
        if (!styleChangeOnly)
        {
            // Dashboard pie charts
            LoadExpensesDistributionChart(data);
            LoadRevenueDistributionChart(data);

            // Geographic charts
            LoadCountriesOfOriginChart(data);
            LoadCompaniesOfOriginChart(data);
            LoadCountriesOfDestinationChart(data);
            LoadCompaniesOfDestinationChart(data);
            LoadGeoMapChart();

            // Operational pie chart
            LoadAccountantsTransactionsChart(data);

            // Customer pie charts
            LoadTopCustomersChart(data);
            LoadCustomerPaymentStatusChart(data);
            LoadActiveInactiveCustomersChart(data);

            // Returns pie charts
            LoadReturnReasonsChart(data);
            LoadReturnsByCategoryChart(data);
            LoadReturnsByProductChart(data);

            // Losses pie charts
            LoadLossReasonsChart(data);
            LoadLossesByProductChart(data);
            LoadLossesByCategoryChart(data);

            // Taxes pie charts
            LoadTaxByCategoryChart(data);
            LoadTaxByProductChart(data);

            // Products tab
            LoadProductSales(data);
        }
    }

    private void LoadExpensesTrendsChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadExpensesOverviewChart(data, StartDate, EndDate);
        ExpensesTrendsSeries = series;
        ExpensesTrendsXAxes = ChartLoaderService.CreateDateXAxes(dates);
        ExpensesTrendsYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasExpensesTrendsData = series.Count > 0;

    }

    private void LoadExpensesDistributionChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadExpenseDistributionChart(data, StartDate, EndDate);
        ExpensesDistributionSeries = series;
        ExpensesDistributionLegend = legend;
        HasExpensesDistributionData = series.Count > 0;
    }

    private void LoadRevenueTrendsChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadRevenueOverviewChart(data, StartDate, EndDate);
        RevenueTrendsSeries = series;
        RevenueTrendsXAxes = ChartLoaderService.CreateDateXAxes(dates);
        RevenueTrendsYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasRevenueTrendsData = series.Count > 0;

    }

    private void LoadRevenueDistributionChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadRevenueDistributionChart(data, StartDate, EndDate);
        RevenueDistributionSeries = series;
        RevenueDistributionLegend = legend;
        HasRevenueDistributionData = series.Count > 0;
    }

    private void LoadProfitTrendsChart(CompanyData data)
    {
        var (series, _, dates, totalProfit) = ChartLoaderService.LoadProfitsOverviewChart(data, StartDate, EndDate);
        ProfitTrendsSeries = series;
        ProfitTrendsXAxes = ChartLoaderService.CreateDateXAxes(dates);
        ProfitTrendsYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasProfitTrendsData = series.Count > 0;

        // totalProfit is already in the display currency, converted per-day at each day's OWN date
        // (Calculations.md §3a Phase 2), so the title matches the bars and needs no today's-rate step.
        _profitOverTimeTitleText = $"Total profits: {CurrencyService.Format(totalProfit)}";
        OnPropertyChanged(nameof(ProfitOverTimeTitle));
    }

    private void LoadRevenueVsExpensesChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadRevenueVsExpensesChart(data, StartDate, EndDate);
        RevenueVsExpensesSeries = series;
        RevenueVsExpensesXAxes = ChartLoaderService.CreateDateXAxes(dates);
        RevenueVsExpensesYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasRevenueVsExpensesData = series.Count > 0;

    }

    private void LoadCountriesOfOriginChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadCountriesOfOriginChart(data, StartDate, EndDate);
        CountriesOfOriginSeries = series;
        CountriesOfOriginLegend = legend;
        HasCountriesOfOriginData = series.Count > 0;
    }

    private void LoadCompaniesOfOriginChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadCompaniesOfOriginChart(data, StartDate, EndDate);
        CompaniesOfOriginSeries = series;
        CompaniesOfOriginLegend = legend;
        HasCompaniesOfOriginData = series.Count > 0;
    }

    private void LoadTopCustomersChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadTopCustomersChart(data, StartDate, EndDate);
        TopCustomersSeries = series;
        TopCustomersLegend = legend;
        HasTopCustomersData = series.Count > 0;
    }

    private void LoadCountriesOfDestinationChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadCountriesOfDestinationChart(data, StartDate, EndDate);
        CountriesOfDestinationSeries = series;
        CountriesOfDestinationLegend = legend;
        HasCountriesOfDestinationData = series.Count > 0;
    }

    private void LoadCompaniesOfDestinationChart(CompanyData data)
    {
        // For companies of destination, use sales by customer company (where products are shipped to)
        var (series, legend) = ChartLoaderService.LoadCompaniesOfDestinationChart(data, StartDate, EndDate);
        CompaniesOfDestinationSeries = series;
        CompaniesOfDestinationLegend = legend;
        HasCompaniesOfDestinationData = series.Count > 0;
    }

    private void LoadGeoMapChart()
    {
        var data = _companyManager?.CompanyData;
        if (data == null) return;

        // Load Origin map (supplier countries from purchases)
        var originData = ChartLoaderService.LoadWorldMapDataBySupplier(data, StartDate, EndDate);
        HasOriginMapData = originData.Count > 0;

        var originSeries = new ObservableCollection<IGeoSeries>();
        if (HasOriginMapData)
        {
            var lands = originData
                .Where(kvp => kvp.Value > 0)
                .Select(kvp => new HeatLand { Name = kvp.Key, Value = kvp.Value })
                .ToArray();
            originSeries.Add(new HeatLandSeries { Lands = lands });
        }
        OriginGeoMapSeries = originSeries;

        // Load Destination map (customer countries from sales)
        var destinationData = ChartLoaderService.LoadWorldMapData(data, StartDate, EndDate);
        HasDestinationMapData = destinationData.Count > 0;

        var destinationSeries = new ObservableCollection<IGeoSeries>();
        if (HasDestinationMapData)
        {
            var lands = destinationData
                .Where(kvp => kvp.Value > 0)
                .Select(kvp => new HeatLand { Name = kvp.Key, Value = kvp.Value })
                .ToArray();
            destinationSeries.Add(new HeatLandSeries { Lands = lands });
        }
        DestinationGeoMapSeries = destinationSeries;
    }

    private void LoadAvgTransactionValueChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadAverageTransactionValueChart(data, StartDate, EndDate);
        AvgTransactionValueSeries = series;
        AvgTransactionValueXAxes = ChartLoaderService.CreateDateXAxes(dates);
        AvgTransactionValueYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasAvgTransactionValueData = series.Count > 0;
    }

    private void LoadTotalTransactionsChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadTotalTransactionsChart(data, StartDate, EndDate);
        TotalTransactionsSeries = series;
        TotalTransactionsXAxes = ChartLoaderService.CreateDateXAxes(dates);
        TotalTransactionsYAxes = ChartLoaderService.CreateNumberYAxes();
        HasTotalTransactionsData = series.Count > 0;
    }

    private void LoadAvgShippingCostsChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadAverageShippingCostsChart(data, StartDate, EndDate);
        AvgShippingCostsSeries = series;
        AvgShippingCostsXAxes = ChartLoaderService.CreateDateXAxes(dates);
        AvgShippingCostsYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasAvgShippingCostsData = series.Count > 0;
    }

    private void LoadAccountantsTransactionsChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadAccountantsTransactionsChart(data, StartDate, EndDate);
        AccountantsTransactionsSeries = series;
        AccountantsTransactionsLegend = legend;
        HasAccountantsTransactionsData = series.Count > 0;
    }

    private void LoadCustomerGrowthChart(CompanyData data)
    {
        var (series, labels) = ChartLoaderService.LoadCustomerGrowthChart(data, StartDate, EndDate, SelectedDateRange);
        CustomerGrowthSeries = series;
        CustomerGrowthXAxes = ChartLoaderService.CreateXAxes(labels);
        CustomerGrowthYAxes = ChartLoaderService.CreateNumberYAxes();
        HasCustomerGrowthData = series.Count > 0;
    }

    private void LoadReturnsOverTimeChart(CompanyData data)
    {
        var (series, _, dates) = ChartLoaderService.LoadReturnsOverTimeChart(data, StartDate, EndDate);
        ReturnsOverTimeSeries = series;
        ReturnsOverTimeXAxes = ChartLoaderService.CreateDateXAxes(dates);
        ReturnsOverTimeYAxes = ChartLoaderService.CreateNumberYAxes();
        HasReturnsOverTimeData = series.Count > 0;
    }

    private void LoadReturnReasonsChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadReturnReasonsChart(data, StartDate, EndDate);
        ReturnReasonsSeries = series;
        ReturnReasonsLegend = legend;
        HasReturnReasonsData = series.Count > 0;
    }

    private void LoadReturnsByCategoryChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadReturnsByCategoryChart(data, StartDate, EndDate);
        ReturnsByCategorySeries = series;
        ReturnsByCategoryLegend = legend;
        HasReturnsByCategoryData = series.Count > 0;
    }

    private void LoadReturnFinancialImpactChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadReturnFinancialImpactChart(data, StartDate, EndDate);
        ReturnFinancialImpactSeries = series;
        ReturnFinancialImpactXAxes = ChartLoaderService.CreateDateXAxes(dates);
        ReturnFinancialImpactYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasReturnFinancialImpactData = series.Count > 0;
    }

    private void LoadLossesOverTimeChart(CompanyData data)
    {
        var (series, _, dates) = ChartLoaderService.LoadLossesOverTimeChart(data, StartDate, EndDate);
        LossesOverTimeSeries = series;
        LossesOverTimeXAxes = ChartLoaderService.CreateDateXAxes(dates);
        LossesOverTimeYAxes = ChartLoaderService.CreateNumberYAxes();
        HasLossesOverTimeData = series.Count > 0;
    }

    private void LoadLossFinancialImpactChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadLossFinancialImpactChart(data, StartDate, EndDate);
        LossFinancialImpactSeries = series;
        LossFinancialImpactXAxes = ChartLoaderService.CreateDateXAxes(dates);
        LossFinancialImpactYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasLossFinancialImpactData = series.Count > 0;
    }

    private void LoadCustomerPaymentStatusChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadCustomerPaymentStatusChart(data, StartDate, EndDate);
        CustomerPaymentStatusSeries = series;
        CustomerPaymentStatusLegend = legend;
        HasCustomerPaymentStatusData = series.Count > 0;
    }

    private void LoadActiveInactiveCustomersChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadActiveInactiveCustomersChart(data, StartDate, EndDate);
        ActiveInactiveCustomersSeries = series;
        ActiveInactiveCustomersLegend = legend;
        HasActiveInactiveCustomersData = series.Count > 0;
    }

    private void LoadLossReasonsChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadLossReasonsChart(data, StartDate, EndDate);
        LossReasonsSeries = series;
        LossReasonsLegend = legend;
        HasLossReasonsData = series.Count > 0;
    }

    private void LoadLossesByProductChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadLossesByProductChart(data, StartDate, EndDate);
        LossesByProductSeries = series;
        LossesByProductLegend = legend;
        HasLossesByProductData = series.Count > 0;
    }

    private void LoadReturnsByProductChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadReturnsByProductChart(data, StartDate, EndDate);
        ReturnsByProductSeries = series;
        ReturnsByProductLegend = legend;
        HasReturnsByProductData = series.Count > 0;
    }

    private void LoadExpenseVsRevenueReturnsChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadExpenseVsRevenueReturnsChart(data, StartDate, EndDate);
        ExpenseVsRevenueReturnsSeries = series;
        ExpenseVsRevenueReturnsXAxes = ChartLoaderService.CreateDateXAxes(dates);
        ExpenseVsRevenueReturnsYAxes = ChartLoaderService.CreateNumberYAxes();
        HasExpenseVsRevenueReturnsData = series.Count > 0;
    }

    private void LoadLossesByCategoryChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadLossesByCategoryChart(data, StartDate, EndDate);
        LossesByCategorySeries = series;
        LossesByCategoryLegend = legend;
        HasLossesByCategoryData = series.Count > 0;
    }

    private void LoadExpenseVsRevenueLossesChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadExpenseVsRevenueLossesChart(data, StartDate, EndDate);
        ExpenseVsRevenueLossesSeries = series;
        ExpenseVsRevenueLossesXAxes = ChartLoaderService.CreateDateXAxes(dates);
        ExpenseVsRevenueLossesYAxes = ChartLoaderService.CreateNumberYAxes();
        HasExpenseVsRevenueLossesData = series.Count > 0;
    }

    private void LoadTaxCollectedVsPaidChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadTaxCollectedVsPaidChart(data, StartDate, EndDate);
        TaxCollectedVsPaidSeries = series;
        TaxCollectedVsPaidXAxes = ChartLoaderService.CreateDateXAxes(dates);
        TaxCollectedVsPaidYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasTaxCollectedVsPaidData = series.Count > 0;
    }

    private void LoadTaxLiabilityTrendChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadTaxLiabilityTrendChart(data, StartDate, EndDate);
        TaxLiabilityTrendSeries = series;
        TaxLiabilityTrendXAxes = ChartLoaderService.CreateDateXAxes(dates);
        TaxLiabilityTrendYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasTaxLiabilityTrendData = series.Count > 0;
    }

    private void LoadTaxByCategoryChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadTaxByCategoryChart(data, StartDate, EndDate);
        TaxByCategorySeries = series;
        TaxByCategoryLegend = legend;
        HasTaxByCategoryData = series.Count > 0;
    }

    private void LoadTaxRateDistributionChart(CompanyData data)
    {
        var (series, xAxes, yAxes) = ChartLoaderService.LoadTaxRateDistributionChart(data, StartDate, EndDate);
        TaxRateDistributionSeries = series;
        TaxRateDistributionXAxes = xAxes;
        TaxRateDistributionYAxes = yAxes;
        HasTaxRateDistributionData = series.Count > 0;
    }

    private void LoadTaxByProductChart(CompanyData data)
    {
        var (series, legend) = ChartLoaderService.LoadTaxByProductChart(data, StartDate, EndDate);
        TaxByProductSeries = series;
        TaxByProductLegend = legend;
        HasTaxByProductData = series.Count > 0;
    }

    private void LoadExpenseVsRevenueTaxChart(CompanyData data)
    {
        var (series, dates) = ChartLoaderService.LoadExpenseVsRevenueTaxChart(data, StartDate, EndDate);
        ExpenseVsRevenueTaxSeries = series;
        ExpenseVsRevenueTaxXAxes = ChartLoaderService.CreateDateXAxes(dates);
        ExpenseVsRevenueTaxYAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        HasExpenseVsRevenueTaxData = series.Count > 0;
    }

    #endregion

    #region Statistics Loading

    /// <summary>
    /// Loads all statistics for stat cards across all tabs.
    /// </summary>
    private void LoadAllStatistics(CompanyData data)
    {
        LoadDashboardStatistics(data);
        LoadOperationalStatistics(data);
        LoadPerformanceStatistics(data);
        LoadCustomerStatistics(data);
        LoadReturnsStatistics(data);
        LoadLossesStatistics(data);
        LoadTaxesStatistics(data);
    }

    private (DateTime Start, DateTime End) ComparisonRange() =>
        ComparisonPeriod.For(DateRangePresetExtensions.ParseDateRange(SelectedDateRange), StartDate, EndDate);

    private void LoadDashboardStatistics(CompanyData data)
    {
        // Revenue stat uses gross-of-tax (Total) per Calculations.md §2 Rule 1,
        // refunds subtracted at full amount. Profit/margin use pre-tax revenue
        // and refunds subtracted at pre-tax portion (handled in ProfitCalculator).
        var totalPurchasesUSD = ExpenseAggregator.SumExpensesUSD(data.Expenses, StartDate, EndDate);
        var grossRevenueUSD = RevenueAggregator.SumCollectedRevenueUSD(data.Revenues, StartDate, EndDate);
        var refundsUSD = RefundAggregator.GetRefundedInDateRangeUSD(data.Payments, StartDate, EndDate);
        var totalRevenueUSD = grossRevenueUSD - refundsUSD;
        var totalRevenuePreTaxUSD = RevenueAggregator.SumCollectedRevenuePreTaxUSD(data.Revenues, StartDate, EndDate);
        var netProfitUSD = ProfitCalculator.CalculateNetProfitUSD(data, StartDate, EndDate);
        var margin = totalRevenuePreTaxUSD > 0 ? (netProfitUSD / totalRevenuePreTaxUSD) * 100 : 0;

        // Calculate previous period for comparison (guard against overflow for very large date ranges like "All Time")
        var (prevStartDate, prevEndDate) = ComparisonRange();

        var prevPurchasesUSD = ExpenseAggregator.SumExpensesUSD(data.Expenses, prevStartDate, prevEndDate);
        var prevGrossRevenueUSD = RevenueAggregator.SumCollectedRevenueUSD(data.Revenues, prevStartDate, prevEndDate);
        var prevRefundsUSD = RefundAggregator.GetRefundedInDateRangeUSD(data.Payments, prevStartDate, prevEndDate);
        var prevSalesUSD = prevGrossRevenueUSD - prevRefundsUSD;
        var prevSalesPreTaxUSD = RevenueAggregator.SumCollectedRevenuePreTaxUSD(data.Revenues, prevStartDate, prevEndDate);
        var prevNetProfit = ProfitCalculator.CalculateNetProfitUSD(data, prevStartDate, prevEndDate);
        var prevMargin = prevSalesPreTaxUSD > 0 ? (prevNetProfit / prevSalesPreTaxUSD) * 100 : 0;

        // Check if there's any previous period data to compare against
        var hasPrevPeriodData = prevPurchasesUSD > 0 || prevSalesUSD > 0;

        // Calculate change percentages (only meaningful if there's previous data)
        var purchasesChange = prevPurchasesUSD > 0 ? ((totalPurchasesUSD - prevPurchasesUSD) / prevPurchasesUSD) * 100 : 0;
        var revenueChange = prevSalesUSD > 0 ? ((totalRevenueUSD - prevSalesUSD) / prevSalesUSD) * 100 : 0;
        var profitChange = prevNetProfit != 0 ? ((netProfitUSD - prevNetProfit) / Math.Abs(prevNetProfit)) * 100 : 0;
        var marginChange = margin - prevMargin;

        // Update properties (convert each transaction at its OWN date per Calculations.md §3a).
        // The same figures as the dashboard cards, showing Pending while a rate is missing.
        TotalPurchases = CurrencyService.FormatSumDisplayFromUSD(
            data.Expenses.Where(e => e.Date >= StartDate && e.Date <= EndDate),
            e => e.Total, e => e.OriginalCurrency, e => e.TotalUSD, e => e.Date);
        PurchasesChangeValue = hasPrevPeriodData && prevPurchasesUSD > 0 ? (double)purchasesChange : null;
        PurchasesChangeText = hasPrevPeriodData && prevPurchasesUSD > 0 ? $"{Math.Abs(purchasesChange):F1}%" : null;

        TotalRevenue = CurrencyService.FormatTotalOrPending(convert =>
            RevenueAggregator.SumCollectedRevenueDisplay(data.Revenues, StartDate, EndDate, convert)
            - RefundAggregator.GetRefundedInDateRangeDisplay(data.Payments, StartDate, EndDate, convert));
        RevenueChangeValue = hasPrevPeriodData && prevSalesUSD > 0 ? (double)revenueChange : null;
        RevenueChangeText = hasPrevPeriodData && prevSalesUSD > 0 ? $"{Math.Abs(revenueChange):F1}%" : null;

        NetProfit = CurrencyService.FormatTotalOrPending(convert =>
            ProfitCalculator.CalculateNetProfitDisplay(data, StartDate, EndDate, convert));
        ProfitChangeValue = hasPrevPeriodData && prevNetProfit != 0 ? (double)profitChange : null;
        ProfitChangeText = hasPrevPeriodData && prevNetProfit != 0 ? $"{Math.Abs(profitChange):F1}%" : null;

        ProfitMargin = $"{margin:F1}%";
        ProfitMarginChangeValue = hasPrevPeriodData && prevSalesUSD > 0 ? (double)marginChange : null;
        ProfitMarginChangeText = hasPrevPeriodData && prevSalesUSD > 0 ? $"{Math.Abs(marginChange):F1}%" : null;
    }

    private void LoadOperationalStatistics(CompanyData data)
    {
        // Count all accountants (no Status property on Accountant)
        var accountantsCount = data.Accountants.Count;
        ActiveAccountants = accountantsCount.ToString();

        // Transactions processed in the date range
        var purchases = data.Expenses.Where(p => p.Date >= StartDate && p.Date <= EndDate).ToList();
        var sales = data.Revenues.Where(s => s.Date >= StartDate && s.Date <= EndDate).ToList();
        var totalTransactions = purchases.Count + sales.Count;

        // Calculate previous period for comparison
        var (prevStartDate, prevEndDate) = ComparisonRange();

        var prevPurchasesCount = data.Expenses.Count(p => p.Date >= prevStartDate && p.Date <= prevEndDate);
        var prevSalesCount = data.Revenues.Count(s => s.Date >= prevStartDate && s.Date <= prevEndDate);
        var prevTransactions = prevPurchasesCount + prevSalesCount;

        var hasPrevPeriodData = prevTransactions > 0;
        var transactionsChange = prevTransactions > 0 ? ((double)(totalTransactions - prevTransactions) / prevTransactions) * 100 : 0;

        TransactionsProcessed = totalTransactions.ToString("N0");
        TransactionsChangeValue = hasPrevPeriodData ? transactionsChange : null;
        TransactionsChangeText = hasPrevPeriodData ? $"{(transactionsChange >= 0 ? "+" : "")}{transactionsChange:F1}%" : null;

        // Processing time and accuracy rate are not tracked in the data model
        AvgProcessingTime = "N/A";
        ProcessingTimeChangeValue = null;
        ProcessingTimeChangeText = null;

        AccuracyRate = "N/A";
        AccuracyChangeValue = null;
        AccuracyChangeText = null;
    }

    private void LoadPerformanceStatistics(CompanyData data)
    {
        // Filter transactions by date range; revenues are paid-only so
        // unpaid invoices don't pad avg-transaction-value / growth stats.
        var sales = data.Revenues
            .Where(s => s.Date >= StartDate && s.Date <= EndDate)
            .Where(RevenueAggregator.IsCollected)
            .ToList();
        var purchases = data.Expenses.Where(p => p.Date >= StartDate && p.Date <= EndDate).ToList();

        var totalTransactionsCount = sales.Count + purchases.Count;
        var allTransactionValues = sales.Select(s => s.EffectiveTotalUSD).Concat(purchases.Select(p => p.EffectiveTotalUSD)).ToList();
        var avgTransactionValue = allTransactionValues.Count > 0 ? allTransactionValues.Average() : 0;

        // Average display value: convert each transaction at its OWN date (Calculations.md §3a),
        // sum, then divide by the same count used above.
        var salesComplete = CurrencyService.TrySumDisplayFromUSD(sales, s => s.Total, s => s.OriginalCurrency, s => s.TotalUSD, s => s.Date, out var salesSumDisplay);
        var purchasesComplete = CurrencyService.TrySumDisplayFromUSD(purchases, p => p.Total, p => p.OriginalCurrency, p => p.TotalUSD, p => p.Date, out var purchasesSumDisplay);
        var transactionsComplete = salesComplete && purchasesComplete;
        var transactionsValueDisplay = salesSumDisplay + purchasesSumDisplay;
        var avgTransactionValueDisplay = totalTransactionsCount > 0 ? transactionsValueDisplay / totalTransactionsCount : 0;

        // Shipping on collected sales and on expenses, as the Average Shipping Costs chart counts it,
        // each converted at its own date.
        var avgShipping = totalTransactionsCount > 0
            ? (sales.Sum(s => s.EffectiveShippingCostUSD) + purchases.Sum(p => p.EffectiveShippingCostUSD)) / totalTransactionsCount
            : 0;
        var salesShippingComplete = CurrencyService.TrySumDisplayFromUSD(sales, s => s.ShippingCost, s => s.OriginalCurrency, s => s.EffectiveShippingCostUSD, s => s.Date, out var salesShippingDisplay);
        var purchasesShippingComplete = CurrencyService.TrySumDisplayFromUSD(purchases, p => p.ShippingCost, p => p.OriginalCurrency, p => p.EffectiveShippingCostUSD, p => p.Date, out var purchasesShippingDisplay);
        var avgShippingDisplay = totalTransactionsCount > 0 ? (salesShippingDisplay + purchasesShippingDisplay) / totalTransactionsCount : 0;

        // Calculate previous period for comparison
        var (prevStartDate, prevEndDate) = ComparisonRange();

        var prevSales = data.Revenues
            .Where(s => s.Date >= prevStartDate && s.Date <= prevEndDate)
            .Where(RevenueAggregator.IsCollected)
            .ToList();
        var prevPurchases = data.Expenses.Where(p => p.Date >= prevStartDate && p.Date <= prevEndDate).ToList();

        var prevTotalTransactionsCount = prevSales.Count + prevPurchases.Count;
        var prevAllTransactionValues = prevSales.Select(s => s.EffectiveTotalUSD).Concat(prevPurchases.Select(p => p.EffectiveTotalUSD)).ToList();
        var prevAvgTransactionValue = prevAllTransactionValues.Count > 0 ? prevAllTransactionValues.Average() : 0;
        var prevAvgShipping = prevTotalTransactionsCount > 0
            ? (prevSales.Sum(s => s.EffectiveShippingCostUSD) + prevPurchases.Sum(p => p.EffectiveShippingCostUSD)) / prevTotalTransactionsCount
            : 0;

        // Check if there's any previous period data
        var hasPrevPeriodData = prevTotalTransactionsCount > 0;

        // Revenue growth (period over period), net of refunds on their own dates as the Revenue card counts it
        var currentRevenueTotal = RevenueAggregator.SumCollectedRevenueUSD(data.Revenues, StartDate, EndDate)
            - RefundAggregator.GetRefundedInDateRangeUSD(data.Payments, StartDate, EndDate);
        var prevRevenueTotal = RevenueAggregator.SumCollectedRevenueUSD(data.Revenues, prevStartDate, prevEndDate)
            - RefundAggregator.GetRefundedInDateRangeUSD(data.Payments, prevStartDate, prevEndDate);
        var revenueGrowthValue = prevRevenueTotal > 0 ? ((currentRevenueTotal - prevRevenueTotal) / prevRevenueTotal) * 100 : 0;

        var transactionsChange = prevTotalTransactionsCount > 0 ? ((double)(totalTransactionsCount - prevTotalTransactionsCount) / prevTotalTransactionsCount) * 100 : 0;
        var avgTransactionChange = prevAvgTransactionValue > 0 ? ((avgTransactionValue - prevAvgTransactionValue) / prevAvgTransactionValue) * 100 : 0;
        var shippingChange = prevAvgShipping > 0 ? ((avgShipping - prevAvgShipping) / prevAvgShipping) * 100 : 0;

        RevenueGrowth = $"{revenueGrowthValue:F1}%";
        RevenueGrowthChangeValue = hasPrevPeriodData && prevRevenueTotal > 0 ? (double)revenueGrowthValue : null;
        RevenueGrowthChangeText = hasPrevPeriodData && prevRevenueTotal > 0 ? $"{(revenueGrowthValue >= 0 ? "+" : "")}{revenueGrowthValue:F1}%" : null;

        TotalTransactions = totalTransactionsCount.ToString("N0");
        TotalTransactionsChangeValue = hasPrevPeriodData ? transactionsChange : null;
        TotalTransactionsChangeText = hasPrevPeriodData ? $"{(transactionsChange >= 0 ? "+" : "")}{transactionsChange:F1}%" : null;

        AvgTransactionValue = transactionsComplete ? CurrencyService.Format(avgTransactionValueDisplay) : CurrencyService.PendingMarker;
        AvgTransactionChangeValue = hasPrevPeriodData && prevAvgTransactionValue > 0 ? (double)avgTransactionChange : null;
        AvgTransactionChangeText = hasPrevPeriodData && prevAvgTransactionValue > 0 ? $"{(avgTransactionChange >= 0 ? "+" : "")}{avgTransactionChange:F1}%" : null;

        AvgShippingCost = salesShippingComplete && purchasesShippingComplete
            ? CurrencyService.Format(avgShippingDisplay)
            : CurrencyService.PendingMarker;
        AvgShippingChangeValue = hasPrevPeriodData && prevAvgShipping > 0 ? (double)shippingChange : null;
        AvgShippingChangeText = hasPrevPeriodData && prevAvgShipping > 0 ? $"{(shippingChange >= 0 ? "+" : "")}{shippingChange:F1}%" : null;
    }

    private void LoadCustomerStatistics(CompanyData data)
    {
        var totalCustomersCount = data.Customers.Count;
        TotalCustomers = totalCustomersCount.ToString("N0");

        // New customers (created within date range)
        var newCustomersCount = data.Customers.Count(c => c.CreatedAt >= StartDate && c.CreatedAt <= EndDate);
        NewCustomers = newCustomersCount.ToString("N0");

        // Calculate previous period for comparison
        var (prevStartDate, prevEndDate) = ComparisonRange();

        // Previous new customers
        var prevNewCustomers = data.Customers.Count(c => c.CreatedAt >= prevStartDate && c.CreatedAt <= prevEndDate);
        var hasPrevNewCustomers = prevNewCustomers > 0;
        var newCustomersChange = prevNewCustomers > 0 ? ((double)(newCustomersCount - prevNewCustomers) / prevNewCustomers) * 100 : 0;

        CustomersChangeValue = null; // Total customers doesn't have a meaningful change calculation
        CustomersChangeText = null;

        NewCustomersChangeValue = hasPrevNewCustomers ? newCustomersChange : null;
        NewCustomersChangeText = hasPrevNewCustomers ? $"{(newCustomersChange >= 0 ? "+" : "")}{newCustomersChange:F1}%" : null;

        // Retention rate and avg customer value are complex calculations
        // For now, calculate avg customer value based on revenue per customer
        var sales = data.Revenues
            .Where(s => s.Date >= StartDate && s.Date <= EndDate)
            .Where(RevenueAggregator.IsCollected)
            .ToList();
        var customerIds = sales.Select(s => s.CustomerId).Distinct().ToList();
        // Convert each sale at its OWN date (Calculations.md §3a), then divide by the
        // same distinct-customer count.
        var custSalesComplete = CurrencyService.TrySumDisplayFromUSD(sales, s => s.Total, s => s.OriginalCurrency, s => s.TotalUSD, s => s.Date, out var salesValueDisplay);
        var avgValueDisplay = customerIds.Count > 0 ? salesValueDisplay / customerIds.Count : 0;

        RetentionRate = "N/A";
        RetentionChangeValue = null;
        RetentionChangeText = null;

        AvgCustomerValue = custSalesComplete ? CurrencyService.Format(avgValueDisplay) : CurrencyService.PendingMarker;
        AvgCustomerValueChangeValue = null;
        AvgCustomerValueChangeText = null;
    }

    private void LoadReturnsStatistics(CompanyData data)
    {
        // Filter returns by date range
        var returns = data.Returns.Where(r => r.ReturnDate >= StartDate && r.ReturnDate <= EndDate).ToList();

        var totalReturnsCount = returns.Count;
        // Refund amounts are in their sale's own currency, so each converts from it at the return's date.
        var impactComplete = ReturnLossAmounts.TrySumDisplay(returns, r => r.RefundAmount,
            r => ReturnLossAmounts.CurrencyOf(data, r), r => r.ReturnDate,
            CurrencyService.GetDisplayAmountFromNative, out var financialImpact);

        // Calculate return rate (returns / total sales transactions)
        var salesTransactions = data.Revenues.Count(s => s.Date >= StartDate && s.Date <= EndDate);
        var returnRate = salesTransactions > 0 ? ((double)totalReturnsCount / salesTransactions) * 100 : 0;

        // Calculate previous period for comparison
        var (prevStartDate, prevEndDate) = ComparisonRange();

        var prevReturns = data.Returns.Where(r => r.ReturnDate >= prevStartDate && r.ReturnDate <= prevEndDate).ToList();
        var prevReturnsCount = prevReturns.Count;
        ReturnLossAmounts.TrySumDisplay(prevReturns, r => r.RefundAmount,
            r => ReturnLossAmounts.CurrencyOf(data, r), r => r.ReturnDate,
            CurrencyService.GetDisplayAmountFromNative, out var prevFinancialImpact);
        var prevSalesTransactions = data.Revenues.Count(s => s.Date >= prevStartDate && s.Date <= prevEndDate);
        var prevReturnRate = prevSalesTransactions > 0 ? ((double)prevReturnsCount / prevSalesTransactions) * 100 : 0;

        // Check if there's any previous period data
        var hasPrevPeriodData = prevReturnsCount > 0 || prevSalesTransactions > 0;

        var returnsChange = prevReturnsCount > 0 ? ((double)(totalReturnsCount - prevReturnsCount) / prevReturnsCount) * 100 : 0;
        var returnRateChange = returnRate - prevReturnRate;
        var impactChange = prevFinancialImpact > 0 ? ((financialImpact - prevFinancialImpact) / prevFinancialImpact) * 100 : 0;

        TotalReturns = totalReturnsCount.ToString("N0");
        ReturnsChangeValue = hasPrevPeriodData && prevReturnsCount > 0 ? returnsChange : null;
        ReturnsChangeText = hasPrevPeriodData && prevReturnsCount > 0 ? $"{(returnsChange >= 0 ? "+" : "")}{returnsChange:F1}%" : null;

        ReturnRate = $"{returnRate:F1}%";
        ReturnRateChangeValue = hasPrevPeriodData && prevSalesTransactions > 0 ? returnRateChange : null;
        ReturnRateChangeText = hasPrevPeriodData && prevSalesTransactions > 0 ? $"{(returnRateChange >= 0 ? "+" : "")}{returnRateChange:F1}%" : null;

        ReturnsFinancialImpact = impactComplete ? CurrencyService.Format(financialImpact) : CurrencyService.PendingMarker;
        ReturnsImpactChangeValue = hasPrevPeriodData && prevFinancialImpact > 0 ? (double)impactChange : null;
        ReturnsImpactChangeText = hasPrevPeriodData && prevFinancialImpact > 0 ? $"{(impactChange >= 0 ? "+" : "")}{impactChange:F1}%" : null;

        // Resolution time is not tracked in the data model
        AvgResolutionTime = "N/A";
        ResolutionTimeChangeValue = null;
        ResolutionTimeChangeText = null;
    }

    private void LoadLossesStatistics(CompanyData data)
    {
        // Filter losses by date range
        var losses = data.LostDamaged.Where(l => l.DateDiscovered >= StartDate && l.DateDiscovered <= EndDate).ToList();

        var totalLossesCount = losses.Count;
        // Loss values are in their sale's or purchase's own currency, so each converts from it at the loss's date.
        var impactComplete = ReturnLossAmounts.TrySumDisplay(losses, l => l.ValueLost,
            l => ReturnLossAmounts.CurrencyOf(data, l), l => l.DateDiscovered,
            CurrencyService.GetDisplayAmountFromNative, out var financialImpact);

        // Calculate loss rate (losses / total transactions)
        var totalTransactions = data.Revenues.Count(s => s.Date >= StartDate && s.Date <= EndDate) +
                               data.Expenses.Count(p => p.Date >= StartDate && p.Date <= EndDate);
        var lossRate = totalTransactions > 0 ? ((double)totalLossesCount / totalTransactions) * 100 : 0;

        // Count losses with insurance claims filed
        var insuranceClaimsCount = losses.Count(l => l.InsuranceClaim);

        // Calculate previous period for comparison
        var (prevStartDate, prevEndDate) = ComparisonRange();

        var prevLosses = data.LostDamaged.Where(l => l.DateDiscovered >= prevStartDate && l.DateDiscovered <= prevEndDate).ToList();
        var prevLossesCount = prevLosses.Count;
        ReturnLossAmounts.TrySumDisplay(prevLosses, l => l.ValueLost,
            l => ReturnLossAmounts.CurrencyOf(data, l), l => l.DateDiscovered,
            CurrencyService.GetDisplayAmountFromNative, out var prevFinancialImpact);
        var prevTotalTransactions = data.Revenues.Count(s => s.Date >= prevStartDate && s.Date <= prevEndDate) +
                                    data.Expenses.Count(p => p.Date >= prevStartDate && p.Date <= prevEndDate);
        var prevLossRate = prevTotalTransactions > 0 ? ((double)prevLossesCount / prevTotalTransactions) * 100 : 0;
        var prevInsuranceClaimsCount = prevLosses.Count(l => l.InsuranceClaim);

        // Check if there's any previous period data
        var hasPrevPeriodData = prevLossesCount > 0 || prevTotalTransactions > 0;

        var lossesChange = prevLossesCount > 0 ? ((double)(totalLossesCount - prevLossesCount) / prevLossesCount) * 100 : 0;
        var lossRateChange = lossRate - prevLossRate;
        var impactChange = prevFinancialImpact > 0 ? ((financialImpact - prevFinancialImpact) / prevFinancialImpact) * 100 : 0;
        var insuranceChange = prevInsuranceClaimsCount > 0 ? ((double)(insuranceClaimsCount - prevInsuranceClaimsCount) / prevInsuranceClaimsCount) * 100 : 0;

        TotalLosses = totalLossesCount.ToString("N0");
        LossesChangeValue = hasPrevPeriodData && prevLossesCount > 0 ? lossesChange : null;
        LossesChangeText = hasPrevPeriodData && prevLossesCount > 0 ? $"{(lossesChange >= 0 ? "+" : "")}{lossesChange:F1}%" : null;

        LossRate = $"{lossRate:F1}%";
        LossRateChangeValue = hasPrevPeriodData && prevTotalTransactions > 0 ? lossRateChange : null;
        LossRateChangeText = hasPrevPeriodData && prevTotalTransactions > 0 ? $"{(lossRateChange >= 0 ? "+" : "")}{lossRateChange:F1}%" : null;

        LossesFinancialImpact = impactComplete ? CurrencyService.Format(financialImpact) : CurrencyService.PendingMarker;
        LossesImpactChangeValue = hasPrevPeriodData && prevFinancialImpact > 0 ? (double)impactChange : null;
        LossesImpactChangeText = hasPrevPeriodData && prevFinancialImpact > 0 ? $"{(impactChange >= 0 ? "+" : "")}{impactChange:F1}%" : null;

        InsuranceClaims = insuranceClaimsCount.ToString("N0");
        InsuranceClaimsChangeValue = hasPrevPeriodData && prevInsuranceClaimsCount > 0 ? insuranceChange : null;
        InsuranceClaimsChangeText = hasPrevPeriodData && prevInsuranceClaimsCount > 0 ? $"{(insuranceChange >= 0 ? "+" : "")}{insuranceChange:F1}%" : null;
    }

    private void LoadTaxesStatistics(CompanyData data)
    {
        // Paid sales only (Rule 2), and the tax a refund handed back comes off tax collected on the
        // refund's date, as in the Tax Summary report (docs/Calculations.md §8).
        var invoicesById = ProfitCalculator.BuildInvoiceLookup(data.Invoices);
        decimal RefundTaxUSD(Payment p) => RefundAggregator.TaxPortionUSD(p, invoicesById);

        var revenues = data.Revenues.Where(r => r.Date >= StartDate && r.Date <= EndDate).Where(RevenueAggregator.IsCollected).ToList();
        var expenses = data.Expenses.Where(e => e.Date >= StartDate && e.Date <= EndDate).ToList();
        var refunds = data.Payments.Where(p => p.IsRefund && p.Date >= StartDate && p.Date <= EndDate).ToList();

        // EffectiveTaxAmountUSD, not a hand-rolled "USD if we have it, native otherwise".
        // docs/Calculations.md §3 forbids summing native fields into a USD total. The Effective
        // property derives the missing figure
        // from the row's own Total/TotalUSD ratio and yields 0 when there is nothing to derive
        // it from, so a rate that never arrived reads as nothing rather than as dollars.
        var grossTaxCollectedUSD = revenues.Sum(r => r.EffectiveTaxAmountUSD);
        var taxCollectedUSD = grossTaxCollectedUSD - refunds.Sum(RefundTaxUSD);
        var taxPaidUSD = expenses.Sum(e => e.EffectiveTaxAmountUSD);
        var netLiability = taxCollectedUSD - taxPaidUSD;

        // Calculate effective tax rate (weighted average across all transactions)
        var totalPreTax = revenues.Sum(r => r.EffectiveSubtotalUSD) + expenses.Sum(e => e.EffectiveSubtotalUSD);
        var totalTax = grossTaxCollectedUSD + taxPaidUSD;
        var effectiveRate = totalPreTax > 0 ? (totalTax / totalPreTax) * 100 : 0;

        // Calculate previous period for comparison
        var (prevStartDate, prevEndDate) = ComparisonRange();

        var prevRevenues = data.Revenues.Where(r => r.Date >= prevStartDate && r.Date <= prevEndDate).Where(RevenueAggregator.IsCollected).ToList();
        var prevExpenses = data.Expenses.Where(e => e.Date >= prevStartDate && e.Date <= prevEndDate).ToList();
        var prevRefunds = data.Payments.Where(p => p.IsRefund && p.Date >= prevStartDate && p.Date <= prevEndDate);

        var prevGrossTaxCollected = prevRevenues.Sum(r => r.EffectiveTaxAmountUSD);
        var prevTaxCollected = prevGrossTaxCollected - prevRefunds.Sum(RefundTaxUSD);
        var prevTaxPaid = prevExpenses.Sum(e => e.EffectiveTaxAmountUSD);
        var prevNetLiability = prevTaxCollected - prevTaxPaid;
        var prevTotalPreTax = prevRevenues.Sum(r => r.EffectiveSubtotalUSD) + prevExpenses.Sum(e => e.EffectiveSubtotalUSD);
        var prevTotalTax = prevGrossTaxCollected + prevTaxPaid;
        var prevEffectiveRate = prevTotalPreTax > 0 ? (prevTotalTax / prevTotalPreTax) * 100 : 0;

        var hasPrevPeriodData = prevTaxCollected > 0 || prevTaxPaid > 0;

        var collectedChange = prevTaxCollected > 0 ? ((taxCollectedUSD - prevTaxCollected) / prevTaxCollected) * 100 : 0;
        var paidChange = prevTaxPaid > 0 ? ((taxPaidUSD - prevTaxPaid) / prevTaxPaid) * 100 : 0;
        var liabilityChange = prevNetLiability != 0 ? ((netLiability - prevNetLiability) / Math.Abs(prevNetLiability)) * 100 : 0;
        var rateChange = effectiveRate - prevEffectiveRate;

        // Convert each transaction's tax at its OWN date (Calculations.md §3a). The per-row
        // USD selector mirrors taxCollectedUSD/taxPaidUSD above so USD display is identity.
        var grossCollectedComplete = CurrencyService.TrySumDisplayFromUSD(
            revenues, r => r.TaxAmount, r => r.OriginalCurrency, r => r.EffectiveTaxAmountUSD, r => r.Date, out var grossTaxCollectedDisplay);
        // Refund tax is only known in USD, so it always converts from USD at the refund's date.
        var refundedComplete = CurrencyService.TrySumDisplayFromUSD(
            refunds, RefundTaxUSD, _ => "USD", RefundTaxUSD, p => p.Date, out var taxRefundedDisplay);
        var collectedComplete = grossCollectedComplete && refundedComplete;
        var taxCollectedDisplay = grossTaxCollectedDisplay - taxRefundedDisplay;
        var paidComplete = CurrencyService.TrySumDisplayFromUSD(
            expenses, e => e.TaxAmount, e => e.OriginalCurrency, e => e.EffectiveTaxAmountUSD, e => e.Date, out var taxPaidDisplay);
        var netLiabilityDisplay = taxCollectedDisplay - taxPaidDisplay;

        // Show Pending while any component is still awaiting its exact-date rate.
        TotalTaxCollected = collectedComplete ? CurrencyService.Format(taxCollectedDisplay) : CurrencyService.PendingMarker;
        TaxCollectedChangeValue = hasPrevPeriodData && prevTaxCollected > 0 ? (double)collectedChange : null;
        TaxCollectedChangeText = hasPrevPeriodData && prevTaxCollected > 0 ? $"{Math.Abs(collectedChange):F1}%" : null;

        TotalTaxPaid = paidComplete ? CurrencyService.Format(taxPaidDisplay) : CurrencyService.PendingMarker;
        TaxPaidChangeValue = hasPrevPeriodData && prevTaxPaid > 0 ? (double)paidChange : null;
        TaxPaidChangeText = hasPrevPeriodData && prevTaxPaid > 0 ? $"{Math.Abs(paidChange):F1}%" : null;

        NetTaxLiability = collectedComplete && paidComplete ? CurrencyService.Format(netLiabilityDisplay) : CurrencyService.PendingMarker;
        TaxLiabilityChangeValue = hasPrevPeriodData && prevNetLiability != 0 ? (double)liabilityChange : null;
        TaxLiabilityChangeText = hasPrevPeriodData && prevNetLiability != 0 ? $"{Math.Abs(liabilityChange):F1}%" : null;

        EffectiveTaxRate = $"{effectiveRate:F1}%";
        EffectiveTaxRateChangeValue = hasPrevPeriodData && prevTotalPreTax > 0 ? (double)rateChange : null;
        EffectiveTaxRateChangeText = hasPrevPeriodData && prevTotalPreTax > 0 ? $"{Math.Abs(rateChange):F1}%" : null;
    }

    #endregion

    #region Customer Activity Info Modal

    [ObservableProperty]
    private bool _isCustomerActivityInfoOpen;

    [RelayCommand]
    private void ShowCustomerActivityInfo()
    {
        IsCustomerActivityInfoOpen = true;
    }

    [RelayCommand]
    private void CloseCustomerActivityInfo()
    {
        IsCustomerActivityInfoOpen = false;
    }

    #endregion

    #region Refunds Tab

    [ObservableProperty] private string _refundsTotal = "0.00";
    [ObservableProperty] private string _refundsRate = "0.0%";
    [ObservableProperty] private string _refundsAvgLatency = "—";
    [ObservableProperty] private ObservableCollection<RefundsRow> _refundsTopCustomers = new();
    [ObservableProperty] private ObservableCollection<RefundsRow> _refundsTopProducts = new();
    [ObservableProperty] private ObservableCollection<RefundsRow> _refundsTopReasons = new();
    [ObservableProperty] private ObservableCollection<RefundsRow> _refundsChannelBreakdown = new();
    [ObservableProperty] private ObservableCollection<RefundsMonthBucket> _refundsMonthlyTotals = new();
    [ObservableProperty] private bool _hasAnyRefunds;

    /// <summary>
    /// Refresh the Refunds tab metrics from the company's Payment list.
    /// Called automatically when the user switches to the Refunds tab.
    /// </summary>
    public void RefreshRefundMetrics()
    {
        var company = App.CompanyManager?.CompanyData;
        if (company == null) return;

        var since = DateTime.Today.AddDays(-90);

        // RefundAnalyticsService converts each refund at its OWN date (Calculations.md §3a)
        // when passed CurrencyService.GetDisplayAmount, returning display-currency amounts
        // that we format directly with CurrencyService.Format (no second conversion).
        var toDisplay = (Func<decimal, DateTime, decimal>)CurrencyService.GetDisplayAmount;
        var total = RefundAnalyticsService.TotalRefundedDisplay(company, since, toDisplay);
        var rateDecimal = RefundAnalyticsService.RefundRate(company, since);
        var avgLatency = RefundAnalyticsService.AverageRefundLatencyDays(company, since);

        RefundsTotal = CurrencyService.Format(total);
        RefundsRate = (rateDecimal * 100).ToString("F1") + "%";
        RefundsAvgLatency = avgLatency > 0 ? $"{avgLatency:F1} days" : "—";
        HasAnyRefunds = total > 0;

        RefundsTopCustomers.Clear();
        foreach (var c in RefundAnalyticsService.TopRefundedCustomers(company, since, 10, toDisplay))
            RefundsTopCustomers.Add(new RefundsRow(c.CustomerName, CurrencyService.Format(c.AmountUSD), $"{c.Count} refund{(c.Count == 1 ? "" : "s")}"));

        RefundsTopProducts.Clear();
        foreach (var p in RefundAnalyticsService.TopRefundedProducts(company, since, 10, toDisplay))
            RefundsTopProducts.Add(new RefundsRow(p.ProductLabel, CurrencyService.Format(p.AmountUSD), null));

        RefundsTopReasons.Clear();
        foreach (var r in RefundAnalyticsService.TopReasons(company, since, 5, toDisplay))
            RefundsTopReasons.Add(new RefundsRow(r.Reason, CurrencyService.Format(r.TotalAmountUSD), $"{r.Count}"));

        RefundsChannelBreakdown.Clear();
        foreach (var (channel, amount) in RefundAnalyticsService.ChannelBreakdown(company, since, toDisplay)
                     .OrderByDescending(kv => kv.Value))
            RefundsChannelBreakdown.Add(new RefundsRow(channel, CurrencyService.Format(amount), null));

        // Each refund is converted at its OWN date before monthly bucketing (Calculations.md §3a).
        RefundsMonthlyTotals.Clear();
        foreach (var m in RefundAnalyticsService.MonthlyTotals(company, 12, toDisplay))
            RefundsMonthlyTotals.Add(new RefundsMonthBucket(m.Month.ToString("MMM yyyy"), m.AmountUSD));
    }

    #endregion
}

/// <summary>Generic display row for the Refunds tab tables.</summary>
public record RefundsRow(string Label, string Amount, string? Detail);

/// <summary>Monthly bucket for the Refunds-over-time chart.</summary>
public record RefundsMonthBucket(string MonthLabel, decimal Amount);
