#pragma warning disable CS0618 // LabelVisual is obsolete
using System.Collections.ObjectModel;
using ArgoBooks.Core;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Charts;
using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.VisualElements;
using SkiaSharp;

namespace ArgoBooks.ViewModels.Dashboard;

public partial class UnifiedChartWidgetViewModel : WidgetViewModelBase
{
    public ChartDataType ChartDataType { get; private set; }

    public override WidgetType WidgetType => WidgetType.Chart;

    public bool IsDistribution => ChartDataType.IsDistribution();

    public ChartLoaderService ChartLoaderService { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CartesianSeries))]
    [NotifyPropertyChangedFor(nameof(PieSeries))]
    private ObservableCollection<ISeries> _series = [];

    // Separate bindings so PieChart never sees LineSeries and vice versa
    public ObservableCollection<ISeries>? CartesianSeries => IsDistribution ? null : Series;
    public ObservableCollection<ISeries>? PieSeries => IsDistribution ? Series : null;

    [ObservableProperty]
    private Axis[] _xAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _yAxes = [new Axis()];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoData))]
    private bool _hasData;

    public bool HasNoData => !HasData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChartTitleVisual))]
    private string _chartTitle = "";

    public LabelVisual ChartTitleVisual => ChartLoaderService.CreateChartTitle(ChartTitle);

    [ObservableProperty]
    private string _emptyStateMessage = "No data available";

    [ObservableProperty]
    private string _chartStyle = "pie";

    public string[] ChartStyleOptions { get; } = ["pie", "donut"];

    public override bool HasConfig => IsDistribution;

    partial void OnChartStyleChanged(string value) => LoadData();

    public UnifiedChartWidgetViewModel(ChartDataType chartDataType)
    {
        ChartDataType = chartDataType;
        ChartTitle = chartDataType.GetDisplayName();
        EmptyStateMessage = $"No {chartDataType.GetChartCategory().ToLowerInvariant()} data available";
    }

    public override void Initialize(Dictionary<string, string> config)
    {
        ApplyConfig(config);
    }

    public override void ApplyConfig(Dictionary<string, string> config)
    {
        if (config.TryGetValue("ChartDataType", out var typeStr)
            && Enum.TryParse<ChartDataType>(typeStr, out var parsed))
        {
            ChartDataType = parsed;
            OnPropertyChanged(nameof(IsDistribution));
            OnPropertyChanged(nameof(HasConfig));
        }

        if (config.TryGetValue("ChartStyle", out var style))
            ChartStyle = style;
    }

    public override Dictionary<string, string> GetConfig()
    {
        var config = new Dictionary<string, string>
        {
            ["ChartDataType"] = ChartDataType.ToString()
        };
        if (IsDistribution)
            config["ChartStyle"] = ChartStyle;
        return config;
    }

    public override void LoadData()
    {
        var data = CompanyManager?.CompanyData;
        if (data == null) return;

        var chartSettings = ChartSettingsService.Instance;

        ChartLoaderService.UpdateThemeColors(ThemeService.Instance.IsDarkTheme);
        ChartLoaderService.SelectedChartStyle = chartSettings.SelectedChartType switch
        {
            "Line" => Services.ChartStyle.Line,
            "Column" => Services.ChartStyle.Column,
            "Step Line" => Services.ChartStyle.StepLine,
            "Area" => Services.ChartStyle.Area,
            "Scatter" => Services.ChartStyle.Scatter,
            _ => Services.ChartStyle.Line
        };

        var filters = new ReportFilters
        {
            StartDate = chartSettings.StartDate,
            EndDate = chartSettings.EndDate
        };

        ChartTitle = ChartDataType.GetDisplayName();

        // Total Profits uses the analytics-page loader so the dashboard widget
        // gets the same positive=green / negative=red bar split and computed title.
        if (ChartDataType == ChartDataType.TotalProfits)
        {
            LoadTotalProfitsChart(data, chartSettings.StartDate, chartSettings.EndDate);
            return;
        }

        // Revenue vs Expenses uses the same analytics-page loader, which converts each day's value at
        // that day's OWN rate before bucketing (Calculations.md §3a Phase 2). The generic multi-series
        // path below converts pre-bucketed monthly totals at the month-start date, whose rate is usually
        // uncached, so it fell back to showing the raw USD amount instead of the display currency.
        if (ChartDataType == ChartDataType.RevenueVsExpenses)
        {
            LoadRevenueVsExpensesComparisonChart(data, chartSettings.StartDate, chartSettings.EndDate);
            return;
        }

        // Return and loss amounts are in their sale's or purchase's own currency, not USD, so they take
        // the analytics-page loaders, which convert each one from that currency at its own date.
        if (ChartDataType is ChartDataType.ReturnFinancialImpact or ChartDataType.LossFinancialImpact)
        {
            LoadReturnOrLossImpactChart(data, chartSettings.StartDate, chartSettings.EndDate);
            return;
        }

        var service = new ReportChartDataService(data, filters);

        if (IsDistribution)
        {
            // Currency distribution pies must convert each transaction at its OWN date before
            // grouping into a slice (Calculations.md §3a Phase 2). Count-based distributions are
            // unaffected because GetDisplayAmount only scales monetary aggregates. The time-series
            // paths below intentionally stay in USD: CreateDateTimeSeries already converts per
            // bucket date, so passing a converter there would double-convert.
            var result = service.GetChartData(ChartDataType, CurrencyService.GetDisplayAmount);
            LoadDistributionChart(result);
        }
        else if (ChartDataType.IsMultiSeries())
        {
            var result = service.GetChartData(ChartDataType);
            LoadMultiSeriesChart(result);
        }
        else
        {
            var result = service.GetChartData(ChartDataType);
            LoadSingleSeriesChart(result);
        }
    }

    private void LoadTotalProfitsChart(CompanyData data, DateTime startDate, DateTime endDate)
    {
        var (series, _, dates, totalProfit) = ChartLoaderService.LoadProfitsOverviewChart(data, startDate, endDate);

        var localizedName = ChartDataType.TotalProfits.GetDisplayName().Translate();
        // totalProfit is already in the display currency, converted per-day at each day's OWN date
        // (Calculations.md §3a Phase 2), so the title matches the bars and needs no today's-rate step.
        ChartTitle = $"{localizedName}: {CurrencyService.Format(totalProfit)}";

        XAxes = ChartLoaderService.CreateDateXAxes(dates);
        YAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        Series = series;
        HasData = dates.Length > 0;
    }

    private void LoadRevenueVsExpensesComparisonChart(CompanyData data, DateTime startDate, DateTime endDate)
    {
        var (series, dates) = ChartLoaderService.LoadRevenueVsExpensesChart(data, startDate, endDate);
        XAxes = ChartLoaderService.CreateDateXAxes(dates);
        YAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        Series = series;
        HasData = dates.Length > 0;
    }

    private void LoadReturnOrLossImpactChart(CompanyData data, DateTime startDate, DateTime endDate)
    {
        var (series, dates) = ChartDataType == ChartDataType.ReturnFinancialImpact
            ? ChartLoaderService.LoadReturnFinancialImpactChart(data, startDate, endDate)
            : ChartLoaderService.LoadLossFinancialImpactChart(data, startDate, endDate);
        XAxes = ChartLoaderService.CreateDateXAxes(dates);
        YAxes = ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);
        Series = series;
        HasData = dates.Length > 0;
    }

    // Counts get a plain number axis, money the display currency's.
    private Axis[] CreateYAxes() => ChartDataType.IsCount()
        ? ChartLoaderService.CreateNumberYAxes()
        : ChartLoaderService.CreateCurrencyYAxes(CurrencyService.CurrentSymbol);

    private void LoadDistributionChart(object result)
    {
        if (result is not List<ChartDataPoint> points || points.Count == 0)
        {
            Series = [];
            HasData = false;
            return;
        }

        var isDonut = ChartStyle == "donut";
        var isCount = ChartDataType.IsCount();
        var series = new ObservableCollection<ISeries>();

        // Distribution points already arrive in the display currency: currency distributions are
        // converted per transaction at each transaction's OWN date inside the data service
        // (Calculations.md §3a Phase 2), and count-based distributions are raw counts that must NOT
        // be FX-converted. So use the point values directly here, no further conversion.
        var top = points.OrderByDescending(p => p.Value).Take(8).ToList();
        var displayValues = top.Select(p => p.Value).ToArray();
        for (int i = 0; i < top.Count; i++)
        {
            var point = top[i];
            var colorHex = AppColors.Palette[i % AppColors.Palette.Length];
            series.Add(new PieSeries<double>
            {
                Values = [Math.Round(displayValues[i], 2)],
                Name = TruncateLabel(point.Label),
                Fill = new SolidColorPaint(SKColor.Parse(colorHex)),
                InnerRadius = isDonut ? 50 : 0,
                Pushout = 0,
                // Values are already in display currency (or plain counts), so just format.
                ToolTipLabelFormatter = p => isCount
                    ? ((decimal)p.Coordinate.PrimaryValue).ToString("N0")
                    : CurrencyService.Format((decimal)p.Coordinate.PrimaryValue)
            });
        }

        Series = series;
        HasData = true;

        ChartLoaderService.StoreExportData(ChartDataType, new ChartExportData
        {
            ChartTitle = ChartTitle,
            ChartType = ChartType.Distribution,
            Labels = top.Select(p => p.Label).ToArray(),
            Values = displayValues.Select(v => Math.Round(v, 2)).ToArray(),
            SeriesName = ChartDataType.GetDisplayName()
        });
    }

    private void LoadMultiSeriesChart(object result)
    {
        if (result is not List<ChartSeriesData> seriesData || seriesData.Count == 0)
        {
            Series = [];
            HasData = false;
            return;
        }

        var allDates = seriesData
            .SelectMany(s => s.DataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value))
            .Distinct().OrderBy(d => d).ToArray();

        // Convert each DAILY point to display currency at its OWN date BEFORE pivoting onto the
        // aligned date axis (Calculations.md §3a Phase 2). The pivoted values are then already
        // display currency, so CreateDateTimeSeries must not convert again. Counts stay as they are.
        if (!ChartDataType.IsCount())
        {
            foreach (var sd in seriesData)
                foreach (var p in sd.DataPoints)
                    if (p.Date.HasValue)
                        p.Value = (double)CurrencyService.GetDisplayAmount((decimal)p.Value, p.Date.Value);
        }

        var series = new ObservableCollection<ISeries>();
        var seriesDisplayValues = new List<double[]>();
        for (int i = 0; i < seriesData.Count; i++)
        {
            var sd = seriesData[i];
            var displayValues = allDates.Select(date =>
                sd.DataPoints.FirstOrDefault(p => p.Date == date)?.Value ?? 0.0).ToArray();
            seriesDisplayValues.Add(displayValues);

            var colorHex = sd.Color;
            series.Add(ChartLoaderService.CreateDateTimeSeries(
                allDates, displayValues, sd.Name, SKColor.Parse(colorHex), convertFromUSD: false));
        }

        XAxes = ChartLoaderService.CreateDateXAxes(allDates);
        YAxes = CreateYAxes();
        Series = series;
        HasData = allDates.Length > 0;

        if (seriesData.Count > 0)
        {
            // Values are already display currency, so the export uses them directly.
            ChartLoaderService.StoreExportData(ChartDataType, new ChartExportData
            {
                ChartTitle = ChartTitle,
                ChartType = ChartDataType.GetChartExportType(),
                Labels = allDates.Select(d => d.ToString("yyyy-MM-dd")).ToArray(),
                Values = seriesDisplayValues[0],
                SeriesName = seriesData[0].Name,
                AdditionalSeries = seriesData
                    .Skip(1)
                    .Select((sd, idx) => (sd.Name, seriesDisplayValues[idx + 1]))
                    .ToList()
            });
        }
    }

    private void LoadSingleSeriesChart(object result)
    {
        if (result is not List<ChartDataPoint> points || points.Count == 0)
        {
            Series = [];
            HasData = false;
            return;
        }

        var dated = points.Where(p => p.Date.HasValue).ToList();

        // Convert each DAILY point to display currency at its OWN date BEFORE re-bucketing, so the
        // bucket sum is a sum of per-day-correct display values (Calculations.md §3a Phase 2).
        // Counts stay as they are.
        if (!ChartDataType.IsCount())
        {
            foreach (var p in dated)
                p.Value = (double)CurrencyService.GetDisplayAmount((decimal)p.Value, p.Date!.Value);
        }

        // Bucket daily data into weeks/months when the date range is wide so
        // column bars are a readable width instead of hairline-thin slivers.
        if (dated.Count >= 2)
        {
            var bucket = ReportChartDataService.GetTimeBucket(
                dated[0].Date!.Value, dated[^1].Date!.Value);
            if (bucket != ReportChartDataService.TimeBucket.Day)
                dated = ReportChartDataService.RebucketSum(dated, bucket);
        }

        var dates = dated.Select(p => p.Date!.Value).ToArray();
        // Values are already display currency (converted per day above), so don't convert again.
        var displayValues = dated.Select(p => p.Value).ToArray();

        var series = new ObservableCollection<ISeries>();
        series.Add(ChartLoaderService.CreateDateTimeSeries(
            dates, displayValues, ChartDataType.GetDisplayName(), SKColor.Parse(AppColors.Palette[0]),
            convertFromUSD: false));

        XAxes = ChartLoaderService.CreateDateXAxes(dates);
        YAxes = CreateYAxes();
        Series = series;
        HasData = dates.Length > 0;

        // Values are already display currency, so the export uses them directly (no further conversion).
        ChartLoaderService.StoreExportData(ChartDataType, new ChartExportData
        {
            ChartTitle = ChartTitle,
            ChartType = ChartDataType.GetChartExportType(),
            Labels = dated.Select(p => p.Label).ToArray(),
            Values = displayValues,
            SeriesName = ChartDataType.GetDisplayName()
        });
    }

    private static string TruncateLabel(string? label)
    {
        if (string.IsNullOrEmpty(label)) return "Unknown";
        return label.Length > 18 ? label[..17] + "\u2026" : label;
    }
}

internal static class ChartDataTypeExportExtensions
{
    internal static ChartType GetChartExportType(this ChartDataType type) => type switch
    {
        ChartDataType.TotalExpenses => ChartType.Expense,
        ChartDataType.TotalRevenue => ChartType.Revenue,
        ChartDataType.TotalProfits => ChartType.Profit,
        _ when type.IsDistribution() => ChartType.Distribution,
        _ when type.IsMultiSeries() => ChartType.Comparison,
        _ => ChartType.Revenue
    };
}
