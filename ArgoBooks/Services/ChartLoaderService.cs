#pragma warning disable CS0618 // LabelVisual is obsolete, DrawnLabelVisual is not API-compatible
using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Core;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Charts;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.VisualElements;
using SkiaSharp;

namespace ArgoBooks.Services;

/// <summary>
/// Service for loading and configuring charts with consistent styling.
/// Designed to work with Google Sheets export, Microsoft Excel export, and report generator systems.
/// </summary>
public class ChartLoaderService
{
    // Chart text size matching WinForms version
    private const float AxisTextSize = 14f;

    // Series colors live in ArgoBooks.Core.ChartColors so the dashboard,
    // Analytics page, and PDF report renderer all share the same mapping.

    // Theme colors (will be updated based on current theme)
    private SKColor _textColor = SKColor.Parse(AppColors.TextDark);
    private SKColor _gridColor = SKColor.Parse(AppColors.ChartAxis);

    /// <summary>
    /// Convert an array of USD amounts to the user's display currency, converting each
    /// USD bucket value at its OWN date (docs/Calculations.md §3a Phase 2) instead of today's rate,
    /// so a non-USD chart isn't re-priced. <paramref name="dates"/> must align 1:1 with
    /// <paramref name="usdValues"/>. For a USD display currency this is a no-op.
    ///
    /// Chart aggregations are all USD-normalized (so multi-currency data rolls up consistently),
    /// but charts must render in the display currency or the bars / tooltips disagree with the
    /// stat cards. Apply this at the boundary right before passing values to LiveCharts.
    /// </summary>
    public static double[] ConvertUSDValuesToDisplay(double[] usdValues, DateTime[] dates)
    {
        if (usdValues.Length == 0) return usdValues;
        var result = new double[usdValues.Length];
        for (int i = 0; i < usdValues.Length; i++)
        {
            var date = i < dates.Length ? dates[i] : DateTime.Now;
            result[i] = (double)CurrencyService.GetDisplayAmount((decimal)usdValues[i], date);
        }
        return result;
    }

    /// <summary>
    /// Convert each daily point to the display currency at its OWN date so re-bucketing sums
    /// per-day-correct values (Calculations.md §3a Phase 2). Identity for a USD display currency.
    /// </summary>
    private static List<ChartDataPoint> ConvertDailyPointsToDisplay(List<ChartDataPoint> dailyPoints)
    {
        foreach (var p in dailyPoints)
            if (p.Date.HasValue)
                p.Value = (double)CurrencyService.GetDisplayAmount((decimal)p.Value, p.Date.Value);
        return dailyPoints;
    }

    // Maximum length for legend labels to prevent overflow
    private const int MaxLegendLabelLength = 18;

    /// <summary>
    /// Truncates a legend label to prevent pie chart legend overflow.
    /// </summary>
    private static string TruncateLegendLabel(string? label)
    {
        if (string.IsNullOrEmpty(label))
            return LanguageService.Instance.Translate("Unknown");

        return label.Length > MaxLegendLabelLength
            ? label[..(MaxLegendLabelLength - 1)] + "…"
            : label;
    }

    /// <summary>
    /// Creates a chart title visual element with consistent styling.
    /// </summary>
    /// <param name="text">The title text.</param>
    /// <returns>A configured LabelVisual for use as a chart title.</returns>
    public static LabelVisual CreateChartTitle(string text) =>
        CreateChartTitle(text, new Padding(15, 12));

    /// <summary>
    /// Creates a chart title with a custom padding. Pass a smaller vertical padding
    /// to let the title sit higher and give the plot area more room.
    /// </summary>
    public static LabelVisual CreateChartTitle(string text, Padding padding)
    {
        var isDarkTheme = ThemeService.Instance.IsDarkTheme;
        var textColor = isDarkTheme ? SKColor.Parse(AppColors.TextDark) : SKColor.Parse(AppColors.TextLight);

        // Handle titles that may already be translated or contain dynamic values
        // If the text contains a colon followed by a value (e.g., "Total profits: $7,246.51"),
        // only translate the label part before the colon
        var translatedText = TranslateChartTitle(text);

        return new LabelVisual
        {
            Text = translatedText,
            TextSize = 16,
            Padding = padding,
            Paint = new SolidColorPaint(textColor) { SKTypeface = PlatformTypefaces.Resolve(null, new SKFontStyle(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)) }
        };
    }

    /// <summary>
    /// Translates a chart title, handling titles with dynamic values.
    /// </summary>
    private static string TranslateChartTitle(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Check if the title contains a colon followed by a value (e.g., "Total profits: $7,246.51")
        var colonIndex = text.IndexOf(':');
        if (colonIndex > 0 && colonIndex < text.Length - 1)
        {
            var label = text[..colonIndex].Trim();
            var value = text[(colonIndex + 1)..].Trim();

            // Only translate the label part if the value looks like a number/currency
            if (value.Length > 0 && (char.IsDigit(value[0]) || value[0] == '$' || value[0] == '-' || value[0] == '+'))
            {
                var translatedLabel = LanguageService.Instance.Translate(label);
                return $"{translatedLabel}: {value}";
            }
        }

        // Standard translation for simple titles
        return LanguageService.Instance.Translate(text);
    }

    /// <summary>
    /// Gets or sets the current data for export functionality.
    /// This is used by Google Sheets and Excel exporters.
    /// </summary>
    private ChartExportData? CurrentExportData { get; set; }

    /// <summary>
    /// Gets or sets the pie chart data for export functionality.
    /// </summary>
    private ChartExportData? PieChartExportData { get; set; }

    /// <summary>
    /// Dictionary storing export data for each chart by its ChartDataType enum.
    /// </summary>
    private readonly Dictionary<ChartDataType, ChartExportData> _chartExportDataByType = new();

    /// <summary>
    /// Stores daily-resolution data for time-series charts to support dynamic re-bucketing on zoom.
    /// </summary>
    private readonly Dictionary<ChartDataType, List<ChartDataPoint>> _dailyDataByChart = new();

    /// <summary>
    /// Stores daily-resolution multi-series data for charts to support dynamic re-bucketing on zoom.
    /// </summary>
    private readonly Dictionary<ChartDataType, List<ChartSeriesData>> _dailySeriesDataByChart = new();

    /// <summary>
    /// Tracks the current bucket granularity for each chart to avoid unnecessary re-bucketing.
    /// </summary>
    private readonly Dictionary<ChartDataType, ReportChartDataService.TimeBucket> _currentBucketByChart = new();

    /// <summary>
    /// When set, zoom re-bucketing uses this fixed bucket instead of auto-detecting from the visible range.
    /// Null means "Auto" (the default behavior).
    /// </summary>
    private readonly Dictionary<ChartDataType, ReportChartDataService.TimeBucket?> _manualBucketOverride = new();

    /// <summary>
    /// Guard flag to prevent re-entrant zoom updates.
    /// </summary>
    private bool _isUpdatingFromZoom;

    /// <summary>
    /// Gets or sets the chart style for rendering series.
    /// </summary>
    public ChartStyle SelectedChartStyle { get; set; } = ChartStyle.Line;

    /// <summary>
    /// Creates a series for time-based data based on SelectedChartStyle.
    /// Uses categorical (index-based) positioning - dates will be evenly spaced.
    /// </summary>
    private ISeries CreateTimeSeries(double[] values, string name, SKColor color)
    {
        return SelectedChartStyle switch
        {
            ChartStyle.Line => new LineSeries<double>
            {
                Values = values,
                Name = name,
                Stroke = new SolidColorPaint(color, 2),
                Fill = null,
                GeometryStroke = new SolidColorPaint(color, 2),
                GeometryFill = new SolidColorPaint(color),
                GeometrySize = 6
            },
            ChartStyle.StepLine => new StepLineSeries<double>
            {
                Values = values,
                Name = name,
                Stroke = new SolidColorPaint(color, 2),
                Fill = null,
                GeometryStroke = new SolidColorPaint(color, 2),
                GeometryFill = new SolidColorPaint(color),
                GeometrySize = 6
            },
            ChartStyle.Area => new LineSeries<double>
            {
                Values = values,
                Name = name,
                Stroke = new SolidColorPaint(color, 2),
                Fill = new SolidColorPaint(color.WithAlpha(80)),
                GeometryStroke = new SolidColorPaint(color, 2),
                GeometryFill = new SolidColorPaint(color),
                GeometrySize = 6
            },
            ChartStyle.Scatter => new ScatterSeries<double>
            {
                Values = values,
                Name = name,
                Stroke = new SolidColorPaint(color, 2),
                Fill = new SolidColorPaint(color),
                GeometrySize = 10
            },
            _ => new ColumnSeries<double>
            {
                Values = values,
                Name = name,
                Fill = new SolidColorPaint(color),
                Stroke = null,
                MaxBarWidth = 100
            }
        };
    }

    /// <summary>
    /// Creates a series for date-based data with proportional spacing.
    /// Points are positioned based on actual date values (converted to OADate), not evenly spaced.
    /// </summary>
    public ISeries CreateDateTimeSeries(DateTime[] dates, double[] values, string name, SKColor color,
        bool convertFromUSD = true)
    {
        // Convert dates to OADate (days since Dec 30, 1899) for X coordinate
        // Use ObservablePoint which directly stores X,Y coordinates.
        // Most chart series carry currency aggregates that originate in USD.
        // Convert to display currency at this boundary so bars / tooltips
        // / axis labels all agree with stat cards. Callers passing counts
        // (returns, losses, transaction counts) pass convertFromUSD=false.
        if (convertFromUSD)
            values = ConvertUSDValuesToDisplay(values, dates);
        var points = dates.Zip(values, (d, v) => new ObservablePoint(d.ToOADate(), v)).ToArray();

        return SelectedChartStyle switch
        {
            ChartStyle.Line => new LineSeries<ObservablePoint>
            {
                Values = points,
                Name = name,
                Stroke = new SolidColorPaint(color, 2),
                Fill = null,
                GeometryStroke = new SolidColorPaint(color, 2),
                GeometryFill = new SolidColorPaint(color),
                GeometrySize = 6
            },
            ChartStyle.StepLine => new StepLineSeries<ObservablePoint>
            {
                Values = points,
                Name = name,
                Stroke = new SolidColorPaint(color, 2),
                Fill = null,
                GeometryStroke = new SolidColorPaint(color, 2),
                GeometryFill = new SolidColorPaint(color),
                GeometrySize = 6
            },
            ChartStyle.Area => new LineSeries<ObservablePoint>
            {
                Values = points,
                Name = name,
                Stroke = new SolidColorPaint(color, 2),
                Fill = new SolidColorPaint(color.WithAlpha(80)),
                GeometryStroke = new SolidColorPaint(color, 2),
                GeometryFill = new SolidColorPaint(color),
                GeometrySize = 6
            },
            ChartStyle.Scatter => new ScatterSeries<ObservablePoint>
            {
                Values = points,
                Name = name,
                Stroke = new SolidColorPaint(color, 2),
                Fill = new SolidColorPaint(color),
                GeometrySize = 10
            },
            _ => new ColumnSeries<ObservablePoint>
            {
                Values = points,
                Name = name,
                Fill = new SolidColorPaint(color),
                Stroke = null,
                MaxBarWidth = 100
            }
        };
    }

    /// <summary>
    /// Creates a date-time series colored by chart semantics: column bars split
    /// positive vs negative via <see cref="ChartColors.ForValue"/>; line/area/scatter
    /// use a single representative color from the same mapping.
    /// </summary>
    private IEnumerable<ISeries> CreateSignedValueDateTimeSeries(
        DateTime[] dates, double[] values, string name,
        ChartDataType chartType, string negativeSuffix,
        bool convertFromUSD = true)
        => CreateSignedValueDateTimeSeries(dates, values, name,
            positiveColor: ChartColors.ForValue(chartType, 1),
            negativeColor: ChartColors.ForValue(chartType, -1),
            negativeSuffix: negativeSuffix,
            lineColor: ChartColors.ForValue(chartType, 0),
            convertFromUSD: convertFromUSD);

    /// <summary>
    /// Creates a date-time series where positive and negative values get distinct colors
    /// (column charts split into two series; line/area/scatter use a single line color).
    /// </summary>
    private IEnumerable<ISeries> CreateSignedValueDateTimeSeries(
        DateTime[] dates, double[] values, string name,
        SKColor positiveColor, SKColor negativeColor,
        string negativeSuffix, SKColor lineColor,
        bool convertFromUSD = true)
    {
        // Values are USD-aggregated currency unless the caller already converted them. When
        // converting here, do it per bucket at its OWN date (docs/Calculations.md §3a Phase 2),
        // not today's rate. Callers whose values already come from per-day-converted, re-bucketed
        // data pass convertFromUSD:false so we don't double-convert.
        if (convertFromUSD)
            values = ConvertUSDValuesToDisplay(values, dates);

        var isColumnStyle = SelectedChartStyle != ChartStyle.Line &&
                           SelectedChartStyle != ChartStyle.StepLine &&
                           SelectedChartStyle != ChartStyle.Area &&
                           SelectedChartStyle != ChartStyle.Scatter;

        if (isColumnStyle)
        {
            var positivePoints = new List<ObservablePoint>();
            var negativePoints = new List<ObservablePoint>();

            for (int i = 0; i < dates.Length; i++)
            {
                var x = dates[i].ToOADate();
                var y = values[i];

                if (y >= 0)
                    positivePoints.Add(new ObservablePoint(x, y));
                else
                    negativePoints.Add(new ObservablePoint(x, y));
            }

            if (positivePoints.Count > 0)
            {
                yield return new ColumnSeries<ObservablePoint>
                {
                    Values = positivePoints,
                    Name = name,
                    Fill = new SolidColorPaint(positiveColor),
                    Stroke = null,
                    MaxBarWidth = 100,
                    IgnoresBarPosition = true
                };
            }

            if (negativePoints.Count > 0)
            {
                yield return new ColumnSeries<ObservablePoint>
                {
                    Values = negativePoints,
                    Name = $"{name} {negativeSuffix}",
                    Fill = new SolidColorPaint(negativeColor),
                    Stroke = null,
                    MaxBarWidth = 100,
                    IgnoresBarPosition = true
                };
            }

            yield break;
        }

        // Values were already converted to display currency above, so
        // skip the conversion inside CreateDateTimeSeries.
        yield return CreateDateTimeSeries(dates, values, name, lineColor, convertFromUSD: false);
    }

    /// <summary>
    /// Updates theme colors based on the current application theme.
    /// </summary>
    /// <param name="isDarkTheme">Whether the dark theme is active.</param>
    public void UpdateThemeColors(bool isDarkTheme)
    {
        if (isDarkTheme)
        {
            _textColor = SKColor.Parse(AppColors.TextDark);
            _gridColor = SKColor.Parse(AppColors.ChartAxis);
        }
        else
        {
            _textColor = SKColor.Parse(AppColors.TextLightAlt);
            _gridColor = SKColor.Parse(AppColors.ChartGrid);
        }
    }

    /// <summary>
    /// Creates X-axis configuration for a cartesian chart with categorical (evenly-spaced) labels.
    /// Use this for non-date categories like product names, months, etc.
    /// </summary>
    /// <param name="labels">The labels for the X-axis.</param>
    /// <returns>Configured X-axis array.</returns>
    public Axis[] CreateXAxes(IEnumerable<string>? labels = null)
    {
        var axis = new Axis
        {
            TextSize = AxisTextSize,
            LabelsPaint = new SolidColorPaint(_textColor),
            Labels = labels?.ToArray(),
            LabelsRotation = 0
        };

        return [axis];
    }

    /// <summary>
    /// Creates the date X-axis for a time-series chart. For 2+ points the axis is left to AUTO-FIT
    /// (null limits): pinning explicit MinLimit/MaxLimit made LiveCharts (with ZoomMode enabled)
    /// render a stale/wrong visible range on load that only "Reset Zoom" (which nulls the limits)
    /// corrected, and it never rescaled when data was added. A single point has no range to fit, so it
    /// keeps padded limits or the axis would be zero-width.
    /// </summary>
    public Axis[] CreateDateXAxes(DateTime[]? dates = null)
    {
        if (dates == null || dates.Length == 0)
        {
            return CreateXAxes();
        }

        var sortedOADates = dates.Select(d => d.ToOADate()).OrderBy(d => d).ToArray();
        var minDate = sortedOADates[0];
        var maxDate = sortedOADates[^1];

        // Calculate UnitWidth based on minimum gap between consecutive dates.
        // This tells LiveCharts how wide each data point "slot" is, so column bars
        // fill the space between points instead of defaulting to 1 day.
        double unitWidth = 1;
        if (sortedOADates.Length >= 2)
        {
            var minGap = double.MaxValue;
            for (var i = 1; i < sortedOADates.Length; i++)
            {
                var gap = sortedOADates[i] - sortedOADates[i - 1];
                if (gap > 0 && gap < minGap) minGap = gap;
            }
            if (minGap < double.MaxValue) unitWidth = minGap;
        }

        var axis = new Axis
        {
            TextSize = AxisTextSize,
            LabelsPaint = new SolidColorPaint(_textColor),
            LabelsRotation = 0,
            UnitWidth = unitWidth,
            MinStep = unitWidth,
            Labeler = value =>
            {
                if (value < -657434 || value > 2958466)
                {
                    return string.Empty;
                }
                try
                {
                    var date = DateTime.FromOADate(value);
                    return DateFormatService.Format(date);
                }
                catch
                {
                    return string.Empty;
                }
            }
        };

        // A single point has no min..max range for LiveCharts to auto-fit, so give it a padded
        // window; for 2+ points leave the limits null so the chart auto-fits and rescales on reload.
        if (sortedOADates.Length == 1)
        {
            axis.MinLimit = minDate - 0.5;
            axis.MaxLimit = maxDate + 0.5;
        }

        return [axis];
    }

    /// <summary>
    /// Creates Y-axis configuration for a cartesian chart with currency formatting.
    /// </summary>
    /// <param name="currencySymbol">The currency symbol to use (default: $).</param>
    /// <returns>Configured Y-axis array.</returns>
    public Axis[] CreateCurrencyYAxes(string currencySymbol = "$")
    {
        var axis = new Axis
        {
            TextSize = AxisTextSize,
            LabelsPaint = new SolidColorPaint(_textColor),
            SeparatorsPaint = new SolidColorPaint(_gridColor) { StrokeThickness = 1 },
            Labeler = value => $"{currencySymbol}{value:N2}"
        };

        return [axis];
    }

    /// <summary>
    /// Creates Y-axis configuration for a cartesian chart with number formatting.
    /// </summary>
    /// <returns>Configured Y-axis array.</returns>
    public Axis[] CreateNumberYAxes()
    {
        var axis = new Axis
        {
            TextSize = AxisTextSize,
            LabelsPaint = new SolidColorPaint(_textColor),
            SeparatorsPaint = new SolidColorPaint(_gridColor) { StrokeThickness = 1 },
            Labeler = value => value.ToString("N0")
        };

        return [axis];
    }

    #region Dynamic Zoom Re-bucketing

    /// <summary>
    /// Stores daily data and returns bucketed data for initial display.
    /// The daily data is preserved for dynamic re-bucketing when the user zooms.
    /// </summary>
    private List<ChartDataPoint> StoreDailyAndBucket(ChartDataType chartType, List<ChartDataPoint> dailyPoints)
    {
        // Convert each daily point to display currency at its OWN date BEFORE bucketing, so the
        // bucket sum is a sum of per-day-correct display values (Calculations.md §3a Phase 2).
        // The stored daily cache and returned bucketed points are therefore already display-currency.
        dailyPoints = ConvertDailyPointsToDisplay(dailyPoints);

        _dailyDataByChart[chartType] = dailyPoints;

        if (dailyPoints.Count < 2 || !dailyPoints[0].Date.HasValue || !dailyPoints[^1].Date.HasValue)
            return dailyPoints;

        var bucket = ReportChartDataService.GetTimeBucket(
            dailyPoints[0].Date!.Value, dailyPoints[^1].Date!.Value);
        _currentBucketByChart[chartType] = bucket;

        return bucket == ReportChartDataService.TimeBucket.Day
            ? dailyPoints
            : ReportChartDataService.RebucketSum(dailyPoints, bucket);
    }

    /// <summary>
    /// Stores daily multi-series data and returns bucketed data for initial display.
    /// </summary>
    private List<ChartSeriesData> StoreDailySeriesAndBucket(
        ChartDataType chartType, List<ChartSeriesData> dailySeries)
    {
        // Convert each series' daily points to display currency at each point's OWN date BEFORE
        // bucketing, so bucket sums are per-day-correct (Calculations.md §3a Phase 2). The stored
        // daily cache and returned bucketed series are therefore already display-currency.
        foreach (var s in dailySeries)
            s.DataPoints = ConvertDailyPointsToDisplay(s.DataPoints);

        _dailySeriesDataByChart[chartType] = dailySeries;

        var allDates = dailySeries
            .SelectMany(s => s.DataPoints)
            .Where(p => p.Date.HasValue)
            .Select(p => p.Date!.Value)
            .OrderBy(d => d)
            .ToList();

        if (allDates.Count < 2)
            return dailySeries;

        var bucket = ReportChartDataService.GetTimeBucket(allDates[0], allDates[^1]);
        _currentBucketByChart[chartType] = bucket;

        return bucket == ReportChartDataService.TimeBucket.Day
            ? dailySeries
            : ReportChartDataService.RebucketSeriesSum(dailySeries, bucket);
    }

    /// <summary>
    /// Handles zoom-based re-bucketing for a single-series time chart.
    /// Call this from axis PropertyChanged when MinLimit or MaxLimit changes.
    /// Returns the updated dates array (for axis reconfiguration) or null if no update was needed.
    /// </summary>
    public DateTime[]? HandleZoomRebucket(
        ChartDataType chartType,
        ObservableCollection<ISeries> series,
        Axis[] xAxes,
        double visibleMinOADate,
        double visibleMaxOADate)
    {
        if (_isUpdatingFromZoom)
            return null;

        if (!_dailyDataByChart.TryGetValue(chartType, out var dailyData) || dailyData.Count < 2)
            return null;

        DateTime visibleStart, visibleEnd;
        try
        {
            visibleStart = DateTime.FromOADate(visibleMinOADate);
            visibleEnd = DateTime.FromOADate(visibleMaxOADate);
        }
        catch
        {
            return null;
        }

        // Use manual override if set, otherwise auto-detect from visible range
        var newBucket = _manualBucketOverride.TryGetValue(chartType, out var manualBucket) && manualBucket.HasValue
            ? manualBucket.Value
            : ReportChartDataService.GetTimeBucket(visibleStart, visibleEnd);
        if (_currentBucketByChart.TryGetValue(chartType, out var currentBucket) && currentBucket == newBucket)
            return null;

        _currentBucketByChart[chartType] = newBucket;

        var bucketed = ReportChartDataService.RebucketSum(dailyData, newBucket);
        if (bucketed.Count == 0)
            return null;

        var dates = bucketed.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var values = bucketed.Select(p => p.Value).ToArray();
        var points = dates.Zip(values, (d, v) => new ObservablePoint(d.ToOADate(), v)).ToArray();

        _isUpdatingFromZoom = true;
        try
        {
            // Profit chart in column mode splits into positive/negative series
            if (chartType == ChartDataType.TotalProfits && series.Count > 1)
            {
                var posPoints = points.Where(p => p.Y is not null && p.Y >= 0).ToArray();
                var negPoints = points.Where(p => p.Y is not null && p.Y < 0).ToArray();
                SetSeriesValues(series[0], posPoints);
                SetSeriesValues(series[1], negPoints);
            }
            else if (series.Count > 0)
            {
                SetSeriesValues(series[0], points);
            }

            // Update axis UnitWidth for proper column sizing without resetting zoom
            if (xAxes.Length > 0)
                UpdateAxisUnitWidth(xAxes[0], dates);
        }
        finally
        {
            _isUpdatingFromZoom = false;
        }

        return dates;
    }

    /// <summary>
    /// Handles zoom-based re-bucketing for a multi-series time chart.
    /// Returns the updated dates array or null if no update was needed.
    /// </summary>
    public DateTime[]? HandleZoomRebucketMultiSeries(
        ChartDataType chartType,
        ObservableCollection<ISeries> series,
        Axis[] xAxes,
        double visibleMinOADate,
        double visibleMaxOADate)
    {
        if (_isUpdatingFromZoom)
            return null;

        if (!_dailySeriesDataByChart.TryGetValue(chartType, out var dailySeries) || dailySeries.Count == 0)
            return null;

        DateTime visibleStart, visibleEnd;
        try
        {
            visibleStart = DateTime.FromOADate(visibleMinOADate);
            visibleEnd = DateTime.FromOADate(visibleMaxOADate);
        }
        catch
        {
            return null;
        }

        // Use manual override if set, otherwise auto-detect from visible range
        var newBucket = _manualBucketOverride.TryGetValue(chartType, out var manualBucket) && manualBucket.HasValue
            ? manualBucket.Value
            : ReportChartDataService.GetTimeBucket(visibleStart, visibleEnd);
        if (_currentBucketByChart.TryGetValue(chartType, out var currentBucket) && currentBucket == newBucket)
            return null;

        _currentBucketByChart[chartType] = newBucket;

        var rebucketed = ReportChartDataService.RebucketSeriesSum(dailySeries, newBucket);

        // Build aligned date set from all series
        var allDates = rebucketed
            .SelectMany(s => s.DataPoints)
            .Where(p => p.Date.HasValue)
            .Select(p => p.Date!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToArray();

        if (allDates.Length == 0)
            return null;

        _isUpdatingFromZoom = true;
        try
        {
            for (int i = 0; i < Math.Min(series.Count, rebucketed.Count); i++)
            {
                var seriesData = rebucketed[i];
                var dates = seriesData.DataPoints
                    .Where(p => p.Date.HasValue)
                    .Select(p => p.Date!.Value).ToArray();
                var values = seriesData.DataPoints.Select(p => p.Value).ToArray();
                var points = dates.Zip(values, (d, v) => new ObservablePoint(d.ToOADate(), v)).ToArray();
                SetSeriesValues(series[i], points);
            }

            if (xAxes.Length > 0)
                UpdateAxisUnitWidth(xAxes[0], allDates);
        }
        finally
        {
            _isUpdatingFromZoom = false;
        }

        return allDates;
    }

    /// <summary>
    /// Updates the Values property on a series regardless of its concrete type.
    /// </summary>
    private static void SetSeriesValues(ISeries series, ObservablePoint[] points)
    {
        switch (series)
        {
            case LineSeries<ObservablePoint> line:
                line.Values = points;
                break;
            case ColumnSeries<ObservablePoint> column:
                column.Values = points;
                break;
            case StepLineSeries<ObservablePoint> stepLine:
                stepLine.Values = points;
                break;
            case ScatterSeries<ObservablePoint> scatter:
                scatter.Values = points;
                break;
        }
    }

    /// <summary>
    /// Updates axis UnitWidth and MinStep based on new data dates without resetting zoom limits.
    /// </summary>
    private static void UpdateAxisUnitWidth(Axis axis, DateTime[] dates)
    {
        if (dates.Length < 2)
            return;

        var sortedOADates = dates.Select(d => d.ToOADate()).OrderBy(d => d).ToArray();
        var minGap = double.MaxValue;
        for (var i = 1; i < sortedOADates.Length; i++)
        {
            var gap = sortedOADates[i] - sortedOADates[i - 1];
            if (gap > 0 && gap < minGap) minGap = gap;
        }

        if (minGap < double.MaxValue)
        {
            axis.UnitWidth = minGap;
            axis.MinStep = minGap;
        }
    }

    /// <summary>
    /// Subscribes to an axis's property changes to detect zoom and trigger re-bucketing.
    /// Returns an Action that should be called to unsubscribe.
    /// </summary>
    public Action SubscribeToAxisZoom(
        Axis[] xAxes,
        ChartDataType chartType,
        ObservableCollection<ISeries> series,
        bool isMultiSeries = false)
    {
        if (xAxes.Length == 0)
            return () => { };

        var axis = xAxes[0];
        System.Timers.Timer? debounceTimer = null;

        void handler(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not ("MinLimit" or "MaxLimit"))
                return;

            if (_isUpdatingFromZoom)
                return;

            var min = axis.MinLimit;
            var max = axis.MaxLimit;
            if (min == null || max == null)
                return;

            // Debounce: MinLimit and MaxLimit often change together
            debounceTimer?.Stop();
            debounceTimer?.Dispose();
            debounceTimer = new System.Timers.Timer(150);
            debounceTimer.AutoReset = false;
            debounceTimer.Elapsed += (_, _) =>
            {
                debounceTimer.Dispose();
                debounceTimer = null;

                if (isMultiSeries)
                    HandleZoomRebucketMultiSeries(chartType, series, xAxes, min.Value, max.Value);
                else
                    HandleZoomRebucket(chartType, series, xAxes, min.Value, max.Value);
            };
            debounceTimer.Start();
        }

        axis.PropertyChanged += handler;
        return () =>
        {
            axis.PropertyChanged -= handler;
            debounceTimer?.Stop();
            debounceTimer?.Dispose();
        };
    }

    /// <summary>
    /// Sets a manual bucket override for a chart. When set, zoom re-bucketing uses this
    /// fixed bucket instead of auto-detecting from the visible range.
    /// Pass null for "Auto" (clears the override).
    /// </summary>
    public void SetManualBucketOverride(ChartDataType chartType, ReportChartDataService.TimeBucket? bucket)
    {
        if (bucket.HasValue)
            _manualBucketOverride[chartType] = bucket;
        else
            _manualBucketOverride.Remove(chartType);
    }

    /// <summary>
    /// Clears all manual bucket overrides.
    /// </summary>
    public void ClearManualBucketOverride(ChartDataType chartType)
    {
        _manualBucketOverride.Remove(chartType);
    }

    /// <summary>
    /// Applies a specific bucket granularity to a single-series chart, updating series values and axis.
    /// Returns the updated dates array, or null if no daily data is stored for this chart.
    /// </summary>
    public DateTime[]? ApplyBucket(
        ChartDataType chartType,
        ReportChartDataService.TimeBucket bucket,
        ObservableCollection<ISeries> series,
        Axis[] xAxes)
    {
        if (!_dailyDataByChart.TryGetValue(chartType, out var dailyData) || dailyData.Count < 2)
            return null;

        if (_currentBucketByChart.TryGetValue(chartType, out var currentBucket) && currentBucket == bucket)
            return null;

        _currentBucketByChart[chartType] = bucket;

        var bucketed = ReportChartDataService.RebucketSum(dailyData, bucket);
        if (bucketed.Count == 0)
            return null;

        var dates = bucketed.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var values = bucketed.Select(p => p.Value).ToArray();
        var points = dates.Zip(values, (d, v) => new ObservablePoint(d.ToOADate(), v)).ToArray();

        _isUpdatingFromZoom = true;
        try
        {
            if (chartType == ChartDataType.TotalProfits && series.Count > 1)
            {
                var posPoints = points.Where(p => p.Y is not null && p.Y >= 0).ToArray();
                var negPoints = points.Where(p => p.Y is not null && p.Y < 0).ToArray();
                SetSeriesValues(series[0], posPoints);
                SetSeriesValues(series[1], negPoints);
            }
            else if (series.Count > 0)
            {
                SetSeriesValues(series[0], points);
            }

            if (xAxes.Length > 0)
                UpdateAxisUnitWidth(xAxes[0], dates);
        }
        finally
        {
            _isUpdatingFromZoom = false;
        }

        return dates;
    }

    /// <summary>
    /// Applies a specific bucket granularity to a multi-series chart, updating all series values and axis.
    /// Returns the updated dates array, or null if no daily data is stored for this chart.
    /// </summary>
    public DateTime[]? ApplyBucketMultiSeries(
        ChartDataType chartType,
        ReportChartDataService.TimeBucket bucket,
        ObservableCollection<ISeries> series,
        Axis[] xAxes)
    {
        if (!_dailySeriesDataByChart.TryGetValue(chartType, out var dailySeries) || dailySeries.Count == 0)
            return null;

        if (_currentBucketByChart.TryGetValue(chartType, out var currentBucket) && currentBucket == bucket)
            return null;

        _currentBucketByChart[chartType] = bucket;

        var rebucketed = ReportChartDataService.RebucketSeriesSum(dailySeries, bucket);

        var allDates = rebucketed
            .SelectMany(s => s.DataPoints)
            .Where(p => p.Date.HasValue)
            .Select(p => p.Date!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToArray();

        if (allDates.Length == 0)
            return null;

        _isUpdatingFromZoom = true;
        try
        {
            for (int i = 0; i < Math.Min(series.Count, rebucketed.Count); i++)
            {
                var seriesData = rebucketed[i];
                var dates = seriesData.DataPoints
                    .Where(p => p.Date.HasValue)
                    .Select(p => p.Date!.Value).ToArray();
                var values = seriesData.DataPoints.Select(p => p.Value).ToArray();
                var points = dates.Zip(values, (d, v) => new ObservablePoint(d.ToOADate(), v)).ToArray();
                SetSeriesValues(series[i], points);
            }

            if (xAxes.Length > 0)
                UpdateAxisUnitWidth(xAxes[0], allDates);
        }
        finally
        {
            _isUpdatingFromZoom = false;
        }

        return allDates;
    }

    /// <summary>
    /// Returns whether daily data is stored for the given chart type (single-series).
    /// </summary>
    public bool HasDailyData(ChartDataType chartType) => _dailyDataByChart.ContainsKey(chartType);

    /// <summary>
    /// Returns whether daily multi-series data is stored for the given chart type.
    /// </summary>
    public bool HasDailySeriesData(ChartDataType chartType) => _dailySeriesDataByChart.ContainsKey(chartType);

    /// <summary>
    /// Gets the current auto-detected bucket for a chart based on its full date range.
    /// </summary>
    public ReportChartDataService.TimeBucket? GetCurrentBucket(ChartDataType chartType)
    {
        return _currentBucketByChart.TryGetValue(chartType, out var bucket) ? bucket : null;
    }

    /// <summary>
    /// Returns the default bucket for a chart based on its full daily data date range.
    /// This is the bucket that would be auto-selected on initial load.
    /// </summary>
    public ReportChartDataService.TimeBucket? GetDefaultBucket(ChartDataType chartType)
    {
        if (_dailyDataByChart.TryGetValue(chartType, out var dailyData) && dailyData.Count >= 2
            && dailyData[0].Date.HasValue && dailyData[^1].Date.HasValue)
        {
            return ReportChartDataService.GetTimeBucket(dailyData[0].Date!.Value, dailyData[^1].Date!.Value);
        }

        if (_dailySeriesDataByChart.TryGetValue(chartType, out var seriesData))
        {
            var allDates = seriesData
                .SelectMany(s => s.DataPoints)
                .Where(p => p.Date.HasValue)
                .Select(p => p.Date!.Value)
                .OrderBy(d => d)
                .ToList();
            if (allDates.Count >= 2)
                return ReportChartDataService.GetTimeBucket(allDates[0], allDates[^1]);
        }

        return null;
    }

    /// <summary>
    /// Restores a chart to a specific bucket granularity. Used when closing the fullscreen modal
    /// to return the chart to its pre-fullscreen state without affecting the normal view.
    /// </summary>
    public void RestoreBucket(
        ChartDataType chartType,
        ReportChartDataService.TimeBucket bucket,
        ObservableCollection<ISeries> series,
        Axis[] xAxes,
        bool isMultiSeries)
    {
        // Force the current bucket to a different value so ApplyBucket doesn't short-circuit
        _currentBucketByChart.Remove(chartType);

        if (isMultiSeries)
            ApplyBucketMultiSeries(chartType, bucket, series, xAxes);
        else
            ApplyBucket(chartType, bucket, series, xAxes);
    }

    #endregion

    #region Shared Data Model Conversion

    /// <summary>
    /// Creates a ReportFilters instance from date parameters.
    /// This enables using ReportChartDataService for data fetching.
    /// </summary>
    /// <param name="startDate">Optional start date.</param>
    /// <param name="endDate">Optional end date.</param>
    /// <param name="defaultDaysBack">Default number of days to look back if no start date specified.</param>
    /// <returns>A configured ReportFilters instance.</returns>
    private static ReportFilters CreateFilters(DateTime? startDate, DateTime? endDate, int defaultDaysBack = 30)
    {
        var end = endDate ?? DateTime.Now;
        var start = startDate ?? end.AddDays(-defaultDaysBack);
        return new ReportFilters
        {
            StartDate = start,
            EndDate = end,
            IncludeReturns = true,
            IncludeLosses = true
        };
    }

    /// <summary>
    /// Creates a ReportFilters instance with month-based default range.
    /// </summary>
    private static ReportFilters CreateFiltersMonths(DateTime? startDate, DateTime? endDate, int defaultMonthsBack = 6)
    {
        var end = endDate ?? DateTime.Now;
        var start = startDate ?? end.AddMonths(-defaultMonthsBack);
        return new ReportFilters
        {
            StartDate = start,
            EndDate = end,
            IncludeReturns = true,
            IncludeLosses = true
        };
    }

    #endregion

    /// <summary>
    /// Loads expenses overview chart data as a column series.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    /// <param name="companyData">The company data to load from.</param>
    /// <param name="startDate">Optional start date filter.</param>
    /// <param name="endDate">Optional end date filter.</param>
    /// <returns>A tuple containing the series collection and dates for proportional spacing.</returns>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadExpensesOverviewChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var dailyPoints = dataService.GetExpensesOverTime();

        if (dailyPoints.Count == 0)
        {
            StoreExportData(ChartDataType.TotalExpenses, new ChartExportData
            {
                ChartTitle = ChartDataType.TotalExpenses.GetDisplayName().Translate(),
                ChartType = ChartType.Expense,
                Labels = [],
                Values = [],
                SeriesName = "Expenses"
            });
            return (series, dates);
        }

        var dataPoints = StoreDailyAndBucket(ChartDataType.TotalExpenses, dailyPoints);
        var labels = dataPoints.Select(p => p.Label).ToArray();
        dates = dataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var values = dataPoints.Select(p => p.Value).ToArray();

        // Values already come from StoreDailyAndBucket (per-day-converted then re-bucketed),
        // so they are already display-currency. Don't convert again (Calculations.md §3a Phase 2).
        series.Add(CreateDateTimeSeries(dates, values, "Expenses", ChartColors.Expense, convertFromUSD: false));

        StoreExportData(ChartDataType.TotalExpenses, new ChartExportData
        {
            ChartTitle = ChartDataType.TotalExpenses.GetDisplayName().Translate(),
            ChartType = ChartType.Expense,
            Labels = labels,
            Values = values,
            SeriesName = "Expenses",
        });

        return (series, dates);
    }

    /// <summary>
    /// Loads revenue overview chart data as a column series.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadRevenueOverviewChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var dailyPoints = dataService.GetRevenueOverTime();

        if (dailyPoints.Count == 0)
        {
            StoreExportData(ChartDataType.TotalRevenue, new ChartExportData
            {
                ChartTitle = ChartDataType.TotalRevenue.GetDisplayName().Translate(),
                ChartType = ChartType.Revenue,
                Labels = [],
                Values = [],
                SeriesName = "Revenue"
            });
            return (series, dates);
        }

        var dataPoints = StoreDailyAndBucket(ChartDataType.TotalRevenue, dailyPoints);
        var labels = dataPoints.Select(p => p.Label).ToArray();
        dates = dataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var values = dataPoints.Select(p => p.Value).ToArray();

        // Values already come from StoreDailyAndBucket (per-day-converted then re-bucketed),
        // so they are already display-currency. Don't convert again (Calculations.md §3a Phase 2).
        series.Add(CreateDateTimeSeries(dates, values, "Revenue", ChartColors.Revenue, convertFromUSD: false));

        StoreExportData(ChartDataType.TotalRevenue, new ChartExportData
        {
            ChartTitle = ChartDataType.TotalRevenue.GetDisplayName().Translate(),
            ChartType = ChartType.Revenue,
            Labels = labels,
            Values = values,
            SeriesName = "Revenue",
        });

        return (series, dates);
    }

    /// <summary>
    /// Loads profits overview chart data as a column series.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, string[] Labels, DateTime[] Dates, decimal TotalProfit) LoadProfitsOverviewChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var labels = Array.Empty<string>();
        var dates = Array.Empty<DateTime>();
        decimal totalProfit = 0;

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var dailyPoints = dataService.GetProfitOverTime();

        if (dailyPoints.Count == 0)
        {
            StoreExportData(ChartDataType.TotalProfits, new ChartExportData
            {
                ChartTitle = ChartDataType.TotalProfits.GetDisplayName().Translate(),
                ChartType = ChartType.Profit,
                Labels = [],
                Values = [],
                SeriesName = "Profit"
            });
            return (series, labels, dates, totalProfit);
        }

        // Total profit for the chart title, in the DISPLAY currency. Convert each day at its OWN
        // date (Calculations.md §3a Phase 2) so the title matches the per-bucket bars and isn't
        // re-priced at today's rate. Computed from daily data before bucketing for exact dates.
        totalProfit = dailyPoints.Sum(p =>
            CurrencyService.GetDisplayAmount((decimal)p.Value, p.Date ?? DateTime.Now));

        var dataPoints = StoreDailyAndBucket(ChartDataType.TotalProfits, dailyPoints);
        labels = dataPoints.Select(p => p.Label).ToArray();
        dates = dataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var values = dataPoints.Select(p => p.Value).ToArray();

        // Values already come from StoreDailyAndBucket (per-day-converted then re-bucketed),
        // so they are already display-currency. Don't convert again (Calculations.md §3a Phase 2).
        foreach (var s in CreateSignedValueDateTimeSeries(dates, values, "Profit",
                     ChartDataType.TotalProfits, "(Loss)", convertFromUSD: false))
        {
            series.Add(s);
        }

        StoreExportData(ChartDataType.TotalProfits, new ChartExportData
        {
            ChartTitle = ChartDataType.TotalProfits.GetDisplayName().Translate(),
            ChartType = ChartType.Profit,
            Labels = labels,
            Values = values,
            SeriesName = "Profit",
        });

        return (series, labels, dates, totalProfit);
    }

    /// <summary>
    /// Builds the per-product revenue-over-time series for the Analytics Products
    /// detail panel. Cash-basis (paid-only) to match the rest of the analytics
    /// surfaces. Values are USD and converted to display currency at the series
    /// boundary. See docs/Calculations.md §13.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadProductRevenueTrendChart(
        CompanyData? companyData, string productId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        if (companyData == null || string.IsNullOrEmpty(productId))
            return (series, Array.Empty<DateTime>());

        var dailyPoints = ProductSalesService.GetProductRevenueByDayUSD(
            companyData, productId,
            startDate ?? DateTime.MinValue,
            endDate ?? DateTime.MaxValue,
            cashBasis: true);

        if (dailyPoints.Count == 0)
            return (series, Array.Empty<DateTime>());

        var ordered = dailyPoints.OrderBy(p => p.Key).ToList();
        var dates = ordered.Select(p => p.Key).ToArray();
        var values = ordered.Select(p => (double)p.Value).ToArray();

        series.Add(CreateDateTimeSeries(dates, values, "Revenue", ChartColors.Revenue));

        return (series, dates);
    }

    /// <summary>
    /// Loads expenses vs revenue comparison chart as a multi-series column chart.
    /// Uses ReportChartDataService for data fetching with daily granularity.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadRevenueVsExpensesChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var dailySeriesData = dataService.GetRevenueVsExpensesDaily();

        if (dailySeriesData.Count < 2)
        {
            _chartExportDataByType[ChartDataType.RevenueVsExpenses] = new ChartExportData
            {
                ChartTitle = ChartDataType.RevenueVsExpenses.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Expenses"
            };
            return (series, dates);
        }

        // Filter out zero-data points from daily data before storing
        var dailyRevenue = dailySeriesData.FirstOrDefault(s => s.Name == "Revenue");
        var dailyExpense = dailySeriesData.FirstOrDefault(s => s.Name == "Expenses");

        if (dailyRevenue == null || dailyExpense == null || dailyRevenue.DataPoints.Count == 0)
        {
            _chartExportDataByType[ChartDataType.RevenueVsExpenses] = new ChartExportData
            {
                ChartTitle = ChartDataType.RevenueVsExpenses.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Expenses"
            };
            return (series, dates);
        }

        // Filter to only include days with data (at least one non-zero value). A refund-only day is
        // negative and must stay, or the refund never comes off the chart.
        var datesWithData = new HashSet<DateTime>();
        foreach (var dp in dailyRevenue.DataPoints.Concat(dailyExpense.DataPoints))
        {
            if (dp.Date.HasValue && dp.Value != 0)
                datesWithData.Add(dp.Date.Value);
        }

        if (datesWithData.Count == 0)
        {
            _chartExportDataByType[ChartDataType.RevenueVsExpenses] = new ChartExportData
            {
                ChartTitle = ChartDataType.RevenueVsExpenses.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Expenses"
            };
            return (series, dates);
        }

        // Filter daily data to only dates with data
        var filteredDailySeries = new List<ChartSeriesData>
        {
            new() { Name = "Expenses", Color = dailyExpense.Color,
                DataPoints = dailyExpense.DataPoints.Where(p => p.Date.HasValue && datesWithData.Contains(p.Date.Value)).ToList() },
            new() { Name = "Revenue", Color = dailyRevenue.Color,
                DataPoints = dailyRevenue.DataPoints.Where(p => p.Date.HasValue && datesWithData.Contains(p.Date.Value)).ToList() }
        };

        var bucketedSeries = StoreDailySeriesAndBucket(ChartDataType.RevenueVsExpenses, filteredDailySeries);
        var revenueData = bucketedSeries.FirstOrDefault(s => s.Name == "Revenue");
        var expenseData = bucketedSeries.FirstOrDefault(s => s.Name == "Expenses");

        if (revenueData == null || expenseData == null)
            return (series, dates);

        var labels = revenueData.DataPoints.Select(p => p.Label).ToArray();
        dates = revenueData.DataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var revenueValues = revenueData.DataPoints.Select(p => p.Value).ToArray();
        var expenseValues = expenseData.DataPoints.Select(p => p.Value).ToArray();

        // Add series (expenses first, then revenue for consistency)
        if (dates.Length > 0)
        {
            // Values already come from StoreDailySeriesAndBucket (per-day-converted then re-bucketed),
            // so they are already display-currency. Don't convert again (Calculations.md §3a Phase 2).
            series.Add(CreateDateTimeSeries(dates, expenseValues, "Expenses", ChartColors.Expense, convertFromUSD: false));
            series.Add(CreateDateTimeSeries(dates, revenueValues, "Revenue", ChartColors.Revenue, convertFromUSD: false));
        }

        // Store export data for multi-series chart
        _chartExportDataByType[ChartDataType.RevenueVsExpenses] = new ChartExportData
        {
            ChartTitle = ChartDataType.RevenueVsExpenses.GetDisplayName().Translate(),
            ChartType = ChartType.Comparison,
            Labels = labels,
            Values = expenseValues,
            SeriesName = "Expenses",
            AdditionalSeries = [("Revenue", revenueValues)]
        };

        return (series, dates);
    }

    /// <summary>
    /// Loads revenue distribution by category as a pie chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadRevenueDistributionChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        // Convert each transaction at its OWN date during aggregation (docs/Calculations.md §3a
        // Phase 2), so a category total spanning many dates isn't re-priced at one rate. Values
        // are already in display currency, so the pie helper must not convert again.
        var dataPoints = dataService.GetRevenueDistribution(CurrencyService.GetDisplayAmount).ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        var (series, legend) = CreatePieSeriesWithLegend(dataPoints);

        // Store export data for Google Sheets/Excel export
        var exportData = new ChartExportData
        {
            ChartTitle = ChartDataType.RevenueDistribution.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Amount"        };
        _chartExportDataByType[ChartDataType.RevenueDistribution] = exportData;

        return (series, legend);
    }

    /// <summary>
    /// Loads expense distribution by category as a pie chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadExpenseDistributionChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        // Per-transaction conversion at each row's OWN date (docs/Calculations.md §3a Phase 2);
        // values come back in display currency, so the pie helper must not convert again.
        var dataPoints = dataService.GetExpenseDistribution(CurrencyService.GetDisplayAmount).ToList();

        if (dataPoints.Count == 0)
        {
            PieChartExportData = null;
            return ([], []);
        }

        var (series, legend) = CreatePieSeriesWithLegend(dataPoints);

        // Store export data for Google Sheets/Excel export
        PieChartExportData = new ChartExportData
        {
            ChartTitle = ChartDataType.ExpensesDistribution.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Amount"        };

        // Store by type for chart-specific retrieval
        _chartExportDataByType[ChartDataType.ExpensesDistribution] = PieChartExportData;

        return (series, legend);
    }

    /// <summary>
    /// Loads growth rates chart data with dynamic granularity based on date range.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    /// <param name="companyData">The company data containing customer information.</param>
    /// <param name="startDate">Optional start date for filtering.</param>
    /// <param name="endDate">Optional end date for filtering.</param>
    /// <param name="datePresetName">Optional date preset name (e.g., "This Month", "Last Quarter").</param>
    public (ObservableCollection<ISeries> Series, string[] Labels) LoadCustomerGrowthChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? datePresetName = null)
    {
        var series = new ObservableCollection<ISeries>();
        var labels = Array.Empty<string>();

        var filters = CreateFiltersMonths(startDate, endDate, 12);
        filters.DatePresetName = datePresetName;
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetCustomerGrowth();

        if (dataPoints.Count == 0)
            return (series, labels);

        labels = dataPoints.Select(p => p.Label).ToArray();
        var values = dataPoints.Select(p => p.Value).ToArray();

        // Only add series if there's actual data
        if (values.Any(v => v != 0))
        {
            series.Add(CreateTimeSeries(values, "New Customers", ChartColors.Neutral));
        }

        // Store export data
        _chartExportDataByType[ChartDataType.CustomerGrowth] = new ChartExportData
        {
            ChartTitle = ChartDataType.CustomerGrowth.GetDisplayName().Translate(),
            ChartType = ChartType.Comparison,
            Labels = labels,
            Values = values,
            SeriesName = "New Customers"
        };

        return (series, labels);
    }

    /// <summary>
    /// Loads average transaction value chart with daily granularity.
    /// Shows separate series for revenue and expense transactions.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadAverageTransactionValueChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var seriesData = dataService.GetAverageTransactionValueDailyBySeries();

        if (seriesData.Count == 0)
            return (series, dates);

        // Get dates from the first series (both series have the same dates)
        var revenueSeriesData = seriesData.FirstOrDefault(s => s.Name == "Revenue");
        var expenseSeriesData = seriesData.FirstOrDefault(s => s.Name == "Expenses");

        if (revenueSeriesData?.DataPoints == null || revenueSeriesData.DataPoints.Count == 0)
            return (series, dates);

        dates = revenueSeriesData.DataPoints
            .Where(p => p.Date.HasValue)
            .Select(p => p.Date!.Value)
            .ToArray();

        if (dates.Length > 0)
        {
            // Revenue series (green)
            var revenueValues = revenueSeriesData.DataPoints.Select(p => p.Value).ToArray();
            series.Add(CreateDateTimeSeries(dates, revenueValues, "Revenue", ChartColors.Revenue));

            // Expense series (red)
            if (expenseSeriesData?.DataPoints != null)
            {
                var expenseValues = expenseSeriesData.DataPoints.Select(p => p.Value).ToArray();
                series.Add(CreateDateTimeSeries(dates, expenseValues, "Expenses", ChartColors.Expense));
            }
        }

        // Store export data
        var labels = revenueSeriesData.DataPoints.Select(p => p.Label).ToArray();
        _chartExportDataByType[ChartDataType.AverageTransactionValue] = new ChartExportData
        {
            ChartTitle = ChartDataType.AverageTransactionValue.GetDisplayName().Translate(),
            ChartType = ChartType.Comparison,
            Labels = labels,
            Values = revenueSeriesData.DataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Revenue"
        };

        return (series, dates);
    }

    /// <summary>
    /// Loads total transactions count chart with daily granularity.
    /// Shows separate series for revenue and expense transactions.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadTotalTransactionsChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var seriesData = dataService.GetTransactionCountDailyBySeries();

        if (seriesData.Count == 0)
            return (series, dates);

        // Get dates from the first series (both series have the same dates)
        var revenueSeriesData = seriesData.FirstOrDefault(s => s.Name == "Revenue");
        var expenseSeriesData = seriesData.FirstOrDefault(s => s.Name == "Expenses");

        if (revenueSeriesData?.DataPoints == null || revenueSeriesData.DataPoints.Count == 0)
            return (series, dates);

        dates = revenueSeriesData.DataPoints
            .Where(p => p.Date.HasValue)
            .Select(p => p.Date!.Value)
            .ToArray();

        if (dates.Length > 0)
        {
            // Transaction counts, not amounts, skip USD→display conversion.
            var revenueValues = revenueSeriesData.DataPoints.Select(p => p.Value).ToArray();
            series.Add(CreateDateTimeSeries(dates, revenueValues, "Revenue", ChartColors.Revenue, convertFromUSD: false));

            if (expenseSeriesData?.DataPoints != null)
            {
                var expenseValues = expenseSeriesData.DataPoints.Select(p => p.Value).ToArray();
                series.Add(CreateDateTimeSeries(dates, expenseValues, "Expenses", ChartColors.Expense, convertFromUSD: false));
            }
        }

        // Store export data (combined for backwards compatibility)
        var labels = revenueSeriesData.DataPoints.Select(p => p.Label).ToArray();
        var totalValues = revenueSeriesData.DataPoints
            .Zip(expenseSeriesData?.DataPoints ?? [], (r, e) => r.Value + e.Value)
            .ToArray();

        _chartExportDataByType[ChartDataType.TotalTransactions] = new ChartExportData
        {
            ChartTitle = ChartDataType.TotalTransactions.GetDisplayName().Translate(),
            ChartType = ChartType.Comparison,
            Labels = labels,
            Values = totalValues,
            SeriesName = "Transactions"
        };

        return (series, dates);
    }

    /// <summary>
    /// Loads average shipping costs chart with daily granularity.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadAverageShippingCostsChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetAverageShippingCostsDaily();

        // Filter to only days with data (non-zero values)
        var filteredData = dataPoints.Where(p => p.Value > 0).ToList();

        if (filteredData.Count == 0)
            return (series, dates);

        var labels = filteredData.Select(p => p.Label).ToArray();
        dates = filteredData.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var avgShipping = filteredData.Select(p => p.Value).ToArray();

        if (dates.Length > 0)
        {
            series.Add(CreateDateTimeSeries(dates, avgShipping, "Avg Shipping", ChartColors.Expense));
        }

        // Store export data
        _chartExportDataByType[ChartDataType.AverageShippingCosts] = new ChartExportData
        {
            ChartTitle = ChartDataType.AverageShippingCosts.GetDisplayName().Translate(),
            ChartType = ChartType.Comparison,
            Labels = labels,
            Values = avgShipping,
            SeriesName = "Avg Shipping"        };

        return (series, dates);
    }

    /// <summary>
    /// Loads countries of origin (supplier countries from purchases) as a pie chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadCountriesOfOriginChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        // Per-transaction conversion at each row's OWN date (docs/Calculations.md §3a Phase 2);
        // values come back in display currency, so the pie helper must not convert again.
        var dataPoints = dataService.GetExpensesByCountryOfDestination(CurrencyService.GetDisplayAmount).ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        var (series, legend) = CreatePieSeriesWithLegend(dataPoints);

        // Store export data
        _chartExportDataByType[ChartDataType.CountriesOfOrigin] = new ChartExportData
        {
            ChartTitle = ChartDataType.CountriesOfOrigin.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Amount"        };

        return (series, legend);
    }

    /// <summary>
    /// Loads countries of destination (sales by customer country) as a pie chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadCountriesOfDestinationChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        // Use sales with customer country lookup - destination is where products are shipped to (customer location)
        // Per-transaction conversion at each row's OWN date (docs/Calculations.md §3a Phase 2);
        // values come back in display currency, so the pie helper must not convert again.
        var dataPoints = dataService.GetRevenueByCustomerCountry(CurrencyService.GetDisplayAmount).ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        var (series, legend) = CreatePieSeriesWithLegend(dataPoints);

        // Store export data
        _chartExportDataByType[ChartDataType.CountriesOfDestination] = new ChartExportData
        {
            ChartTitle = ChartDataType.CountriesOfDestination.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Amount"        };

        return (series, legend);
    }

    /// <summary>
    /// Loads companies of origin (supplier companies from purchases) as a pie chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadCompaniesOfOriginChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        // Per-transaction conversion at each row's OWN date (docs/Calculations.md §3a Phase 2);
        // values come back in display currency, so the pie helper must not convert again.
        var dataPoints = dataService.GetExpensesBySupplierCompany(CurrencyService.GetDisplayAmount).ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        var (series, legend) = CreatePieSeriesWithLegend(dataPoints);

        // Store export data
        _chartExportDataByType[ChartDataType.CompaniesOfOrigin] = new ChartExportData
        {
            ChartTitle = ChartDataType.CompaniesOfOrigin.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Amount"        };

        return (series, legend);
    }

    /// <summary>
    /// Loads companies of destination (customer companies from sales) as a pie chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadCompaniesOfDestinationChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        // Per-transaction conversion at each row's OWN date (docs/Calculations.md §3a Phase 2);
        // values come back in display currency, so the pie helper must not convert again.
        var dataPoints = dataService.GetRevenueByCompanyOfDestination(CurrencyService.GetDisplayAmount).ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        var (series, legend) = CreatePieSeriesWithLegend(dataPoints);

        // Store export data
        _chartExportDataByType[ChartDataType.CompaniesOfDestination] = new ChartExportData
        {
            ChartTitle = ChartDataType.CompaniesOfDestination.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Amount"        };

        return (series, legend);
    }

    /// <summary>
    /// Loads accountants transactions as a pie chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadAccountantsTransactionsChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetTransactionsByAccountant().ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        // Values are transaction COUNTS per accountant, not currency amounts, so they must not be
        // FX-converted (the export SeriesName is "Count").
        var (series, legend) = CreatePieSeriesWithLegend(dataPoints, isCurrency: false);

        // Store export data (used for "Transactions by Accountant" and "Companies of Destination")
        var exportData = new ChartExportData
        {
            ChartTitle = ChartDataType.AccountantsTransactions.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Count"        };
        _chartExportDataByType[ChartDataType.AccountantsTransactions] = exportData;

        return (series, legend);
    }

    /// <summary>
    /// Loads customer payment status chart (Paid vs Pending vs Overdue).
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadCustomerPaymentStatusChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetCustomerPaymentStatus().ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        // Values are counts of revenues per status, not amounts.
        var (series, legend) = CreatePieSeriesWithLegend(dataPoints, isCurrency: false);

        // Store export data
        _chartExportDataByType[ChartDataType.CustomerPaymentStatus] = new ChartExportData
        {
            ChartTitle = ChartDataType.CustomerPaymentStatus.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Count"        };

        return (series, legend);
    }

    /// <summary>
    /// Loads active vs inactive customers chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadActiveInactiveCustomersChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate, defaultDaysBack: 90); // 90 days for activity
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetActiveVsInactiveCustomers().ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        // Customer counts, not amounts.
        var (series, legend) = CreatePieSeriesWithLegend(dataPoints, isCurrency: false);

        // Store export data
        _chartExportDataByType[ChartDataType.ActiveVsInactiveCustomers] = new ChartExportData
        {
            ChartTitle = ChartDataType.ActiveVsInactiveCustomers.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Count"        };

        return (series, legend);
    }

    /// <summary>
    /// Loads loss reasons chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadLossReasonsChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        filters.IncludeLosses = true;
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetLossReasons().ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        // Loss counts grouped by reason, not amounts.
        var (series, legend) = CreatePieSeriesWithLegend(dataPoints, isCurrency: false);

        // Store export data
        _chartExportDataByType[ChartDataType.LossReasons] = new ChartExportData
        {
            ChartTitle = ChartDataType.LossReasons.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Count"        };

        return (series, legend);
    }

    /// <summary>
    /// Loads losses by product chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadLossesByProductChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        filters.IncludeLosses = true;
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetLossesByProduct().ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        // Loss counts per product, not amounts.
        var (series, legend) = CreatePieSeriesWithLegend(dataPoints, isCurrency: false);

        // Store export data
        _chartExportDataByType[ChartDataType.LossesByProduct] = new ChartExportData
        {
            ChartTitle = ChartDataType.LossesByProduct.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Amount"        };

        return (series, legend);
    }

    /// <summary>
    /// Loads returns over time chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, string[] Labels, DateTime[] Dates) LoadReturnsOverTimeChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var labels = Array.Empty<string>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        filters.IncludeReturns = true;
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetReturnsOverTime();

        if (dataPoints.Count == 0)
            return (series, labels, dates);

        labels = dataPoints.Select(p => p.Label).ToArray();
        dates = dataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var values = dataPoints.Select(p => p.Value).ToArray();

        // Returns count, not amount, skip USD→display conversion.
        series.Add(CreateDateTimeSeries(dates, values, "Returns", ChartColors.Expense, convertFromUSD: false));

        // Store export data
        _chartExportDataByType[ChartDataType.ReturnsOverTime] = new ChartExportData
        {
            ChartTitle = ChartDataType.ReturnsOverTime.GetDisplayName().Translate(),
            ChartType = ChartType.Expense,
            Labels = labels,
            Values = values,
            SeriesName = "Returns"        };

        return (series, labels, dates);
    }

    /// <summary>
    /// Loads return reasons as a pie chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadReturnReasonsChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        filters.IncludeReturns = true;
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetReturnReasons().ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        // Return counts grouped by reason, not amounts.
        var (series, legend) = CreatePieSeriesWithLegend(dataPoints, isCurrency: false);

        // Store export data
        var exportData = new ChartExportData
        {
            ChartTitle = ChartDataType.ReturnReasons.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Count"        };
        _chartExportDataByType[ChartDataType.ReturnReasons] = exportData;

        return (series, legend);
    }

    /// <summary>
    /// Loads returns by category as a pie chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadReturnsByCategoryChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        filters.IncludeReturns = true;
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetReturnsByCategory().ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        // Return counts grouped by category, not amounts.
        var (series, legend) = CreatePieSeriesWithLegend(dataPoints, isCurrency: false);

        // Store export data
        _chartExportDataByType[ChartDataType.ReturnsByCategory] = new ChartExportData
        {
            ChartTitle = ChartDataType.ReturnsByCategory.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Count"
        };

        return (series, legend);
    }

    /// <summary>
    /// Loads return financial impact chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadReturnFinancialImpactChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        filters.IncludeReturns = true;
        var dataService = new ReportChartDataService(companyData, filters);

        // Refund amounts are in their sale's own currency, not USD, so the data service converts each
        // from that currency at its own date and the series must not convert again.
        var dataPoints = dataService.GetReturnFinancialImpactDaily(CurrencyService.GetDisplayAmountFromNative);

        // Filter to only days with data (non-zero values)
        var filteredData = dataPoints.Where(p => p.Value > 0).ToList();

        if (filteredData.Count == 0)
            return (series, dates);

        var labels = filteredData.Select(p => p.Label).ToArray();
        dates = filteredData.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var impactValues = filteredData.Select(p => p.Value).ToArray();

        if (dates.Length > 0)
        {
            series.Add(CreateDateTimeSeries(dates, impactValues, "Refunds", ChartColors.Expense, convertFromUSD: false));
        }

        // Store export data
        _chartExportDataByType[ChartDataType.ReturnFinancialImpact] = new ChartExportData
        {
            ChartTitle = ChartDataType.ReturnFinancialImpact.GetDisplayName().Translate(),
            ChartType = ChartType.Expense,
            Labels = labels,
            Values = impactValues,
            SeriesName = "Refunds"        };

        return (series, dates);
    }

    /// <summary>
    /// Loads losses over time chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, string[] Labels, DateTime[] Dates) LoadLossesOverTimeChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var labels = Array.Empty<string>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        filters.IncludeLosses = true;
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetLossesOverTime();

        if (dataPoints.Count == 0)
            return (series, labels, dates);

        labels = dataPoints.Select(p => p.Label).ToArray();
        dates = dataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var values = dataPoints.Select(p => p.Value).ToArray();

        // Losses count, not amount, skip USD→display conversion.
        series.Add(CreateDateTimeSeries(dates, values, "Losses", ChartColors.Expense, convertFromUSD: false));

        // Store export data
        _chartExportDataByType[ChartDataType.LossesOverTime] = new ChartExportData
        {
            ChartTitle = ChartDataType.LossesOverTime.GetDisplayName().Translate(),
            ChartType = ChartType.Expense,
            Labels = labels,
            Values = values,
            SeriesName = "Losses"        };

        return (series, labels, dates);
    }

    /// <summary>
    /// Loads loss financial impact chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadLossFinancialImpactChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        filters.IncludeLosses = true;
        var dataService = new ReportChartDataService(companyData, filters);

        // Loss values are in their sale's or purchase's own currency, not USD, so the data service
        // converts each from that currency at its own date and the series must not convert again.
        var dataPoints = dataService.GetLossFinancialImpactDaily(CurrencyService.GetDisplayAmountFromNative);

        // Filter to only days with data (non-zero values)
        var filteredData = dataPoints.Where(p => p.Value > 0).ToList();

        if (filteredData.Count == 0)
            return (series, dates);

        var labels = filteredData.Select(p => p.Label).ToArray();
        dates = filteredData.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var impactValues = filteredData.Select(p => p.Value).ToArray();

        if (dates.Length > 0)
        {
            series.Add(CreateDateTimeSeries(dates, impactValues, "Value Lost", ChartColors.Expense, convertFromUSD: false));
        }

        // Store export data
        _chartExportDataByType[ChartDataType.LossFinancialImpact] = new ChartExportData
        {
            ChartTitle = ChartDataType.LossFinancialImpact.GetDisplayName().Translate(),
            ChartType = ChartType.Expense,
            Labels = labels,
            Values = impactValues,
            SeriesName = "Value Lost"        };

        return (series, dates);
    }

    /// <summary>
    /// Loads returns by product chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadReturnsByProductChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        filters.IncludeReturns = true;
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetReturnsByProduct().ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        // Return counts per product, not amounts.
        var (series, legend) = CreatePieSeriesWithLegend(dataPoints, isCurrency: false);

        // Store export data
        _chartExportDataByType[ChartDataType.ReturnsByProduct] = new ChartExportData
        {
            ChartTitle = ChartDataType.ReturnsByProduct.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Count"
        };

        return (series, legend);
    }

    /// <summary>
    /// Loads losses by category chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadLossesByCategoryChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        filters.IncludeLosses = true;
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetLossesByCategory().ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        // Loss counts per category, not amounts.
        var (series, legend) = CreatePieSeriesWithLegend(dataPoints, isCurrency: false);

        // Store export data
        _chartExportDataByType[ChartDataType.LossesByCategory] = new ChartExportData
        {
            ChartTitle = ChartDataType.LossesByCategory.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Count"
        };

        return (series, legend);
    }

    /// <summary>
    /// Loads expense vs revenue returns chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadExpenseVsRevenueReturnsChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        filters.IncludeReturns = true;
        var dataService = new ReportChartDataService(companyData, filters);

        var seriesData = dataService.GetExpenseVsRevenueReturns();

        if (seriesData.Count == 0)
        {
            _chartExportDataByType[ChartDataType.ExpenseVsRevenueReturns] = new ChartExportData
            {
                ChartTitle = ChartDataType.ExpenseVsRevenueReturns.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Revenue Returns"
            };
            return (series, dates);
        }

        var revenueReturns = seriesData.FirstOrDefault(s => s.Name == "Revenue Returns");
        var expenseReturns = seriesData.FirstOrDefault(s => s.Name == "Expense Returns");

        if (revenueReturns == null || revenueReturns.DataPoints.Count == 0)
        {
            _chartExportDataByType[ChartDataType.ExpenseVsRevenueReturns] = new ChartExportData
            {
                ChartTitle = ChartDataType.ExpenseVsRevenueReturns.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Revenue Returns"
            };
            return (series, dates);
        }

        var labels = revenueReturns.DataPoints.Select(p => p.Label).ToArray();
        dates = revenueReturns.DataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var revenueReturnValues = revenueReturns.DataPoints.Select(p => p.Value).ToArray();
        var expenseReturnValues = expenseReturns?.DataPoints.Select(p => p.Value).ToArray() ?? [];

        if (dates.Length > 0)
        {
            // Return counts, not amounts, skip USD→display conversion.
            series.Add(CreateDateTimeSeries(dates, revenueReturnValues, "Revenue Returns", ChartColors.Expense, convertFromUSD: false));
            if (expenseReturnValues.Length > 0)
            {
                series.Add(CreateDateTimeSeries(dates, expenseReturnValues, "Expense Returns", SKColor.Parse(AppColors.PurpleDark), convertFromUSD: false));
            }
        }

        // Store export data
        _chartExportDataByType[ChartDataType.ExpenseVsRevenueReturns] = new ChartExportData
        {
            ChartTitle = ChartDataType.ExpenseVsRevenueReturns.GetDisplayName().Translate(),
            ChartType = ChartType.Comparison,
            Labels = labels,
            Values = revenueReturnValues,
            SeriesName = "Revenue Returns",
            AdditionalSeries = expenseReturnValues.Length > 0 ? [("Expense Returns", expenseReturnValues)] : []
        };

        return (series, dates);
    }

    /// <summary>
    /// Loads expense vs revenue losses chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadExpenseVsRevenueLossesChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        filters.IncludeLosses = true;
        var dataService = new ReportChartDataService(companyData, filters);

        var seriesData = dataService.GetExpenseVsRevenueLosses();

        if (seriesData.Count == 0)
        {
            _chartExportDataByType[ChartDataType.ExpenseVsRevenueLosses] = new ChartExportData
            {
                ChartTitle = ChartDataType.ExpenseVsRevenueLosses.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Expense Losses"
            };
            return (series, dates);
        }

        var expenseLosses = seriesData.FirstOrDefault(s => s.Name == "Expense Losses");
        var revenueLosses = seriesData.FirstOrDefault(s => s.Name == "Revenue Losses");

        if (expenseLosses == null || expenseLosses.DataPoints.Count == 0)
        {
            _chartExportDataByType[ChartDataType.ExpenseVsRevenueLosses] = new ChartExportData
            {
                ChartTitle = ChartDataType.ExpenseVsRevenueLosses.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Expense Losses"
            };
            return (series, dates);
        }

        var labels = expenseLosses.DataPoints.Select(p => p.Label).ToArray();
        dates = expenseLosses.DataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var expenseLossValues = expenseLosses.DataPoints.Select(p => p.Value).ToArray();
        var revenueLossValues = revenueLosses?.DataPoints.Select(p => p.Value).ToArray() ?? [];

        if (dates.Length > 0)
        {
            // Loss counts, not amounts, skip USD→display conversion.
            series.Add(CreateDateTimeSeries(dates, expenseLossValues, "Expense Losses", ChartColors.Expense, convertFromUSD: false));
            if (revenueLossValues.Length > 0)
            {
                series.Add(CreateDateTimeSeries(dates, revenueLossValues, "Revenue Losses", SKColor.Parse(AppColors.PurpleDark), convertFromUSD: false));
            }
        }

        // Store export data
        _chartExportDataByType[ChartDataType.ExpenseVsRevenueLosses] = new ChartExportData
        {
            ChartTitle = ChartDataType.ExpenseVsRevenueLosses.GetDisplayName().Translate(),
            ChartType = ChartType.Comparison,
            Labels = labels,
            Values = expenseLossValues,
            SeriesName = "Expense Losses",
            AdditionalSeries = revenueLossValues.Length > 0 ? [("Revenue Losses", revenueLossValues)] : []
        };

        return (series, dates);
    }

    /// <summary>
    /// Loads tax collected vs tax paid chart with two series over time.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadTaxCollectedVsPaidChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var seriesData = dataService.GetTaxCollectedVsPaid();

        if (seriesData.Count == 0)
        {
            _chartExportDataByType[ChartDataType.TaxCollectedVsPaid] = new ChartExportData
            {
                ChartTitle = ChartDataType.TaxCollectedVsPaid.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Tax Collected"
            };
            return (series, dates);
        }

        var taxCollected = seriesData.FirstOrDefault(s => s.Name == "Tax Collected");
        var taxPaid = seriesData.FirstOrDefault(s => s.Name == "Tax Paid");

        if (taxCollected == null || taxCollected.DataPoints.Count == 0)
        {
            _chartExportDataByType[ChartDataType.TaxCollectedVsPaid] = new ChartExportData
            {
                ChartTitle = ChartDataType.TaxCollectedVsPaid.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Tax Collected"
            };
            return (series, dates);
        }

        var labels = taxCollected.DataPoints.Select(p => p.Label).ToArray();
        dates = taxCollected.DataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var collectedValues = taxCollected.DataPoints.Select(p => p.Value).ToArray();
        var paidValues = taxPaid?.DataPoints.Select(p => p.Value).ToArray() ?? [];

        if (dates.Length > 0)
        {
            series.Add(CreateDateTimeSeries(dates, collectedValues, "Tax Collected", ChartColors.Revenue));
            if (paidValues.Length > 0)
            {
                series.Add(CreateDateTimeSeries(dates, paidValues, "Tax Paid", ChartColors.Expense));
            }
        }

        _chartExportDataByType[ChartDataType.TaxCollectedVsPaid] = new ChartExportData
        {
            ChartTitle = ChartDataType.TaxCollectedVsPaid.GetDisplayName().Translate(),
            ChartType = ChartType.Comparison,
            Labels = labels,
            Values = collectedValues,
            SeriesName = "Tax Collected",
            AdditionalSeries = paidValues.Length > 0 ? [("Tax Paid", paidValues)] : []
        };

        return (series, dates);
    }

    /// <summary>
    /// Loads net tax liability trend chart.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadTaxLiabilityTrendChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var dataPoints = dataService.GetTaxLiabilityOverTime();

        if (dataPoints.Count == 0)
            return (series, dates);

        var labels = dataPoints.Select(p => p.Label).ToArray();
        dates = dataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var values = dataPoints.Select(p => p.Value).ToArray();

        foreach (var s in CreateSignedValueDateTimeSeries(dates, values, "Net Tax Liability",
                     ChartDataType.TaxLiabilityTrend, "(Refund)"))
        {
            series.Add(s);
        }

        _chartExportDataByType[ChartDataType.TaxLiabilityTrend] = new ChartExportData
        {
            ChartTitle = ChartDataType.TaxLiabilityTrend.GetDisplayName().Translate(),
            ChartType = ChartType.Revenue,
            Labels = labels,
            Values = values,
            SeriesName = "Net Tax Liability"
        };

        return (series, dates);
    }

    /// <summary>
    /// Loads tax by category pie chart.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadTaxByCategoryChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        // Per-transaction conversion at each row's OWN date (docs/Calculations.md §3a Phase 2);
        // values come back in display currency, so the pie helper must not convert again.
        var dataPoints = dataService.GetTaxByCategory(CurrencyService.GetDisplayAmount).ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        var (series, legend) = CreatePieSeriesWithLegend(dataPoints);

        _chartExportDataByType[ChartDataType.TaxByCategory] = new ChartExportData
        {
            ChartTitle = ChartDataType.TaxByCategory.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Amount"
        };

        return (series, legend);
    }

    /// <summary>
    /// Loads tax rate distribution as a stacked bar chart with Revenue vs Expense series.
    /// </summary>
    public (ObservableCollection<ISeries> Series, Axis[] XAxes, Axis[] YAxes) LoadTaxRateDistributionChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var (revenueRates, expenseRates, labels) = dataService.GetTaxRateDistribution();

        if (labels.Length == 0)
        {
            _chartExportDataByType[ChartDataType.TaxRateDistribution] = new ChartExportData
            {
                ChartTitle = ChartDataType.TaxRateDistribution.GetDisplayName().Translate(),
                ChartType = ChartType.Distribution,
                Labels = [],
                Values = [],
                SeriesName = "Revenue"
            };
            return (series, CreateXAxes(), CreateNumberYAxes());
        }

        var revenueValues = revenueRates.Select(p => p.Value).ToArray();
        var expenseValues = expenseRates.Select(p => p.Value).ToArray();

        series.Add(new StackedColumnSeries<double>
        {
            Values = revenueValues,
            Name = "Revenue",
            Fill = new SolidColorPaint(ChartColors.Revenue),
            Stroke = null,
            MaxBarWidth = 40
        });

        series.Add(new StackedColumnSeries<double>
        {
            Values = expenseValues,
            Name = "Expense",
            Fill = new SolidColorPaint(ChartColors.Expense),
            Stroke = null,
            MaxBarWidth = 40
        });

        var xAxes = new Axis[]
        {
            new()
            {
                Labels = labels,
                TextSize = AxisTextSize,
                LabelsPaint = new SolidColorPaint(_textColor),
                SeparatorsPaint = null
            }
        };

        var yAxes = CreateNumberYAxes();

        _chartExportDataByType[ChartDataType.TaxRateDistribution] = new ChartExportData
        {
            ChartTitle = ChartDataType.TaxRateDistribution.GetDisplayName().Translate(),
            ChartType = ChartType.Comparison,
            Labels = labels,
            Values = revenueValues,
            SeriesName = "Revenue",
            AdditionalSeries = [("Expense", expenseValues)]
        };

        return (series, xAxes, yAxes);
    }

    /// <summary>
    /// Loads tax by product pie chart.
    /// </summary>
    public (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> Legend) LoadTaxByProductChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        // Per-transaction conversion at each row's OWN date (docs/Calculations.md §3a Phase 2);
        // values come back in display currency, so the pie helper must not convert again.
        var dataPoints = dataService.GetTaxByProduct(CurrencyService.GetDisplayAmount).ToList();

        if (dataPoints.Count == 0)
            return ([], []);

        var (series, legend) = CreatePieSeriesWithLegend(dataPoints);

        _chartExportDataByType[ChartDataType.TaxByProduct] = new ChartExportData
        {
            ChartTitle = ChartDataType.TaxByProduct.GetDisplayName().Translate(),
            ChartType = ChartType.Distribution,
            Labels = dataPoints.Select(p => p.Label).ToArray(),
            Values = dataPoints.Select(p => p.Value).ToArray(),
            SeriesName = "Amount"
        };

        return (series, legend);
    }

    /// <summary>
    /// Loads expense vs revenue tax chart with two series over time.
    /// </summary>
    public (ObservableCollection<ISeries> Series, DateTime[] Dates) LoadExpenseVsRevenueTaxChart(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var series = new ObservableCollection<ISeries>();
        var dates = Array.Empty<DateTime>();

        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var seriesData = dataService.GetExpenseVsRevenueTax();

        if (seriesData.Count == 0)
        {
            _chartExportDataByType[ChartDataType.ExpenseVsRevenueTax] = new ChartExportData
            {
                ChartTitle = ChartDataType.ExpenseVsRevenueTax.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Revenue Tax"
            };
            return (series, dates);
        }

        var revenueTax = seriesData.FirstOrDefault(s => s.Name == "Revenue Tax");
        var expenseTax = seriesData.FirstOrDefault(s => s.Name == "Expense Tax");

        if (revenueTax == null || revenueTax.DataPoints.Count == 0)
        {
            _chartExportDataByType[ChartDataType.ExpenseVsRevenueTax] = new ChartExportData
            {
                ChartTitle = ChartDataType.ExpenseVsRevenueTax.GetDisplayName().Translate(),
                ChartType = ChartType.Comparison,
                Labels = [],
                Values = [],
                SeriesName = "Revenue Tax"
            };
            return (series, dates);
        }

        var labels = revenueTax.DataPoints.Select(p => p.Label).ToArray();
        dates = revenueTax.DataPoints.Where(p => p.Date.HasValue).Select(p => p.Date!.Value).ToArray();
        var revenueValues = revenueTax.DataPoints.Select(p => p.Value).ToArray();
        var expenseValues = expenseTax?.DataPoints.Select(p => p.Value).ToArray() ?? [];

        if (dates.Length > 0)
        {
            series.Add(CreateDateTimeSeries(dates, revenueValues, "Revenue Tax", ChartColors.Revenue));
            if (expenseValues.Length > 0)
            {
                series.Add(CreateDateTimeSeries(dates, expenseValues, "Expense Tax", ChartColors.Expense));
            }
        }

        _chartExportDataByType[ChartDataType.ExpenseVsRevenueTax] = new ChartExportData
        {
            ChartTitle = ChartDataType.ExpenseVsRevenueTax.GetDisplayName().Translate(),
            ChartType = ChartType.Comparison,
            Labels = labels,
            Values = revenueValues,
            SeriesName = "Revenue Tax",
            AdditionalSeries = expenseValues.Length > 0 ? [("Expense Tax", expenseValues)] : []
        };

        return (series, dates);
    }

    /// <summary>
    /// Loads world map data for GeoMap chart.
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public Dictionary<string, double> LoadWorldMapData(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var countryData = dataService.GetWorldMapData();

        // Convert country names to ISO codes for GeoMap
        return countryData
            .Select(kvp => (CountryCodeMapping.GetIsoCode(kvp.Key), kvp.Value))
            .Where(x => !string.IsNullOrEmpty(x.Item1))
            .GroupBy(x => x.Item1)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Item2));
    }

    /// <summary>
    /// Loads world map data by supplier country for GeoMap chart (destination mode).
    /// Uses ReportChartDataService for data fetching.
    /// </summary>
    public Dictionary<string, double> LoadWorldMapDataBySupplier(
        CompanyData? companyData,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var filters = CreateFilters(startDate, endDate);
        var dataService = new ReportChartDataService(companyData, filters);

        var countryData = dataService.GetWorldMapDataBySupplier();

        // Convert country names to ISO codes for GeoMap
        return countryData
            .Select(kvp => (CountryCodeMapping.GetIsoCode(kvp.Key), kvp.Value))
            .Where(x => !string.IsNullOrEmpty(x.Item1))
            .GroupBy(x => x.Item1)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Item2));
    }

    /// <summary>
    /// Stores export data for a chart, making it available for later retrieval by ChartDataType.
    /// </summary>
    internal void StoreExportData(ChartDataType chartDataType, ChartExportData data)
    {
        CurrentExportData = data;
        _chartExportDataByType[chartDataType] = data;
    }

    /// <summary>
    /// Gets the export data for a specific chart by its ChartDataType.
    /// </summary>
    /// <param name="chartDataType">The ChartDataType of the chart.</param>
    /// <returns>The chart export data, or null if not found.</returns>
    public ChartExportData? GetExportDataForChart(ChartDataType? chartDataType)
    {
        if (chartDataType is not { } type)
            return CurrentExportData;

        if (_chartExportDataByType.TryGetValue(type, out var data))
            return data;

        return CurrentExportData;
    }

    /// <summary>
    /// Gets the data for exporting to Google Sheets.
    /// Does not include total row as it would appear as a category in the chart.
    /// </summary>
    /// <param name="chartDataType">The ChartDataType of the chart to export.</param>
    /// <returns>Export data formatted for Google Sheets.</returns>
    public List<List<object>> GetGoogleSheetsExportData(ChartDataType? chartDataType = null)
    {
        var exportData = GetExportDataForChart(chartDataType);

        if (exportData == null)
            return [];

        // Use "Category" for pie charts, "Date" or "Month" for time-based charts
        var labelHeader = exportData.ChartType switch
        {
            ChartType.Distribution => "Category",
            ChartType.Comparison => "Month",
            _ => "Date"
        };

        // Build header row with all series names
        var headerRow = new List<object> { labelHeader, exportData.SeriesName };
        foreach (var (name, _) in exportData.AdditionalSeries)
        {
            headerRow.Add(name);
        }

        var data = new List<List<object>> { headerRow };

        // Data rows (no total - it would show up as a category in the chart)
        for (int i = 0; i < exportData.Labels.Length; i++)
        {
            var row = new List<object>
            {
                exportData.Labels[i],
                exportData.Values[i]
            };

            foreach (var (_, values) in exportData.AdditionalSeries)
            {
                row.Add(i < values.Length ? values[i] : 0.0);
            }

            data.Add(row);
        }

        return data;
    }

    /// <summary>
    /// Resets the zoom on a cartesian chart by clearing axis limits.
    /// </summary>
    /// <param name="xAxes">The X-axes to reset.</param>
    /// <param name="yAxes">The Y-axes to reset.</param>
    public static void ResetZoom(IEnumerable<Axis>? xAxes, IEnumerable<Axis>? yAxes)
    {
        if (xAxes != null)
        {
            foreach (var axis in xAxes)
            {
                axis.MinLimit = null;
                axis.MaxLimit = null;
            }
        }

        if (yAxes != null)
        {
            foreach (var axis in yAxes)
            {
                axis.MinLimit = null;
                axis.MaxLimit = null;
            }
        }
    }

    /// <summary>
    /// Gets a color hex string for a series by index.
    /// </summary>
    /// <param name="index">The series index.</param>
    /// <returns>A hex color string for the series.</returns>
    private static string GetColorHexForIndex(int index)
    {
        return AppColors.Palette[index % AppColors.Palette.Length];
    }

    /// <summary>
    /// Creates pie chart series and legend items with "Other" grouping based on MaxPieSlices setting.
    /// </summary>
    /// <param name="dataPoints">The source data points.</param>
    /// <param name="isCurrency">When true (the default), tooltips are formatted as money in the
    /// display currency; pass false for count-based pies (return reasons, customer counts, etc.).</param>
    /// <returns>A tuple containing the series collection and legend items.</returns>
    private static (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> LegendItems) CreatePieSeriesWithLegend(
        List<ChartDataPoint> dataPoints,
        bool isCurrency = true)
    {
        var maxSlices = ChartSettingsService.GetMaxPieSlices();
        return CreatePieSeriesWithLegend(dataPoints, maxSlices, isCurrency);
    }

    /// <summary>
    /// Creates pie chart series and legend items with "Other" grouping.
    /// Values are used as-is: currency pies are pre-converted per-date in the
    /// data layer, and count-based pies (return reasons, customer counts, etc.)
    /// are never converted.
    /// </summary>
    /// <param name="dataPoints">The source data points.</param>
    /// <param name="maxSlices">Maximum number of slices before grouping into "Other".</param>
    /// <param name="isCurrency">When true, tooltips show money in the display currency; false shows
    /// a plain count.</param>
    /// <returns>A tuple containing the series collection and legend items.</returns>
    private static (ObservableCollection<ISeries> Series, ObservableCollection<PieLegendItem> LegendItems) CreatePieSeriesWithLegend(
        List<ChartDataPoint> dataPoints,
        int maxSlices,
        bool isCurrency = true)
    {
        var series = new ObservableCollection<ISeries>();
        var legendItems = new ObservableCollection<PieLegendItem>();

        if (dataPoints.Count == 0)
            return (series, legendItems);

        // Values are already in the correct units (display currency pre-converted
        // per-date in the data layer, or plain counts), so use them as-is. Currency pies show the
        // currency symbol and decimals in tooltips; count pies show a whole number.
        decimal Convert(double v) => (decimal)v;

        string FormatTooltip(double v) => isCurrency
            ? CurrencyService.Format((decimal)v)
            : ((decimal)v).ToString("N0");

        // Sort by value descending
        var sortedPoints = dataPoints.OrderByDescending(p => p.Value).ToList();
        var total = sortedPoints.Sum(p => p.Value);

        // Take top items and group the rest into "Other"
        var topItems = sortedPoints.Take(maxSlices - 1).ToList();
        var otherItems = sortedPoints.Skip(maxSlices - 1).ToList();

        // If there's only one "other" item, just show it directly instead of grouping
        if (otherItems.Count == 1)
        {
            topItems.Add(otherItems[0]);
            otherItems.Clear();
        }

        // Create series and legend items for top items
        for (int i = 0; i < topItems.Count; i++)
        {
            var item = topItems[i];
            var colorHex = GetColorHexForIndex(i);
            var percentage = total > 0 ? (item.Value / total) * 100 : 0;
            var displayValue = Math.Round((double)Convert(item.Value), 2);

            series.Add(new PieSeries<double>
            {
                Values = [displayValue],
                Name = TruncateLegendLabel(item.Label),
                Fill = new SolidColorPaint(SKColor.Parse(colorHex)),
                Pushout = 0,
                ToolTipLabelFormatter = point => FormatTooltip(point.Coordinate.PrimaryValue)
            });

            legendItems.Add(new PieLegendItem
            {
                Label = item.Label,
                Value = displayValue,
                Percentage = percentage,
                ColorHex = colorHex
            });
        }

        // Create "Other" category if needed
        if (otherItems.Count > 0)
        {
            var otherTotalUSD = otherItems.Sum(p => p.Value);
            var otherValue = Math.Round((double)Convert(otherTotalUSD), 2);
            var otherPercentage = total > 0 ? (otherTotalUSD / total) * 100 : 0;
            var otherColorHex = AppColors.Gray;

            series.Add(new PieSeries<double>
            {
                Values = [otherValue],
                Name = LanguageService.Instance.Translate("Other"),
                Fill = new SolidColorPaint(SKColor.Parse(otherColorHex)),
                Pushout = 0,
                ToolTipLabelFormatter = point => FormatTooltip(point.Coordinate.PrimaryValue)
            });

            var itemsText = LanguageService.Instance.Translate("items");
            legendItems.Add(new PieLegendItem
            {
                Label = $"{LanguageService.Instance.Translate("Other")} ({otherItems.Count} {itemsText})",
                Value = otherValue,
                Percentage = otherPercentage,
                ColorHex = otherColorHex
            });
        }

        return (series, legendItems);
    }

}

/// <summary>
/// Data structure for chart export functionality.
/// </summary>
public class ChartExportData
{
    public string ChartTitle { get; init; } = string.Empty;
    public ChartType ChartType { get; init; }
    public string[] Labels { get; init; } = [];
    public double[] Values { get; init; } = [];
    public string SeriesName { get; init; } = string.Empty;

    /// <summary>
    /// Additional series for multi-series charts (e.g., Revenue vs Expenses).
    /// Each entry contains a series name and its values.
    /// </summary>
    public List<(string Name, double[] Values)> AdditionalSeries { get; init; } = [];

    /// <summary>
    /// Returns true if this chart has multiple series.
    /// </summary>
    public bool IsMultiSeries => AdditionalSeries.Count > 0;
}

/// <summary>
/// Chart type enumeration for export handling.
/// </summary>
public enum ChartType
{
    Revenue,
    Expense,
    Profit,
    Distribution,
    Comparison
}

/// <summary>
/// Visual chart style for rendering series.
/// </summary>
public enum ChartStyle
{
    Line,
    Column,
    StepLine,
    Area,
    Scatter
}

