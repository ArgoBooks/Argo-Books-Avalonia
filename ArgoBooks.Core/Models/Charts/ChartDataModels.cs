namespace ArgoBooks.Core.Models.Charts;

/// <summary>
/// Represents a single data point in a chart.
/// This is the shared data structure used by both LiveChartsCore (Dashboard/Analytics)
/// and SkiaSharp (Reports) rendering systems.
/// </summary>
public class ChartDataPoint
{
    /// <summary>
    /// The label for the X-axis (e.g., "Jan 2026", "Category A").
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// The numeric value for this data point.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Optional date associated with this data point.
    /// </summary>
    public DateTime? Date { get; set; }

    /// <summary>
    /// Optional color override for this specific data point (hex format).
    /// </summary>
    public string? Color { get; set; }
}

/// <summary>
/// Represents a series of data points for multi-series charts.
/// Used for charts like Revenue vs Expenses that have multiple data series.
/// </summary>
public class ChartSeriesData
{
    /// <summary>The revenue series' name, which the chart loader also looks the series up by.</summary>
    public const string RevenueName = "Revenue";

    /// <summary>The expenses series' name, which the chart loader also looks the series up by.</summary>
    public const string ExpensesName = "Expenses";

    /// <summary>
    /// The name of this series (e.g., "Revenue", "Expenses").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The color for this series in hex format (e.g., "#22C55E").
    /// </summary>
    public string Color { get; set; } = AppColors.Black;

    /// <summary>
    /// The data points in this series.
    /// </summary>
    public List<ChartDataPoint> DataPoints { get; set; } = [];
}
