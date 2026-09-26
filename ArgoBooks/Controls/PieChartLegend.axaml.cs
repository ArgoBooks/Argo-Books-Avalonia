using ArgoBooks.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.ObjectModel;

namespace ArgoBooks.Controls;

/// <summary>
/// Represents an item in the pie chart legend.
/// </summary>
public class PieLegendItem
{
    /// <summary>
    /// The display label for the legend item.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// The value for this item.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// The percentage this item represents of the total.
    /// </summary>
    public double Percentage { get; set; }

    /// <summary>
    /// The color brush for the legend indicator.
    /// </summary>
    public IBrush? Color { get; set; }

    /// <summary>
    /// The hex color string.
    /// </summary>
    public string ColorHex { get; set; } = AppColors.Primary;

    /// <summary>
    /// Formatted percentage display string.
    /// </summary>
    public string FormattedPercentage => $"{Percentage:F1}%";
}

/// <summary>
/// A custom scrollable legend component for pie charts.
/// </summary>
public partial class PieChartLegend : UserControl
{
    #region Styled Properties

    public static readonly StyledProperty<ObservableCollection<PieLegendItem>?> ItemsProperty =
        AvaloniaProperty.Register<PieChartLegend, ObservableCollection<PieLegendItem>?>(nameof(Items));

    public static readonly StyledProperty<double> MaxHeightOverrideProperty =
        AvaloniaProperty.Register<PieChartLegend, double>(nameof(MaxHeightOverride), 200);

    public static readonly StyledProperty<bool> ShowPercentageProperty =
        AvaloniaProperty.Register<PieChartLegend, bool>(nameof(ShowPercentage), true);

    public static readonly StyledProperty<bool> ShowValueProperty =
        AvaloniaProperty.Register<PieChartLegend, bool>(nameof(ShowValue));

    public static readonly StyledProperty<double> LegendFontSizeProperty =
        AvaloniaProperty.Register<PieChartLegend, double>(nameof(LegendFontSize), 13);

    public static readonly StyledProperty<double> IndicatorSizeProperty =
        AvaloniaProperty.Register<PieChartLegend, double>(nameof(IndicatorSize), 12);

    public static readonly StyledProperty<CornerRadius> IndicatorCornerRadiusProperty =
        AvaloniaProperty.Register<PieChartLegend, CornerRadius>(nameof(IndicatorCornerRadius), new CornerRadius(6));

    #endregion

    #region Properties

    public ObservableCollection<PieLegendItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height for the legend scroll area.
    /// </summary>
    public double MaxHeightOverride
    {
        get => GetValue(MaxHeightOverrideProperty);
        set => SetValue(MaxHeightOverrideProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the percentage for each item.
    /// </summary>
    public bool ShowPercentage
    {
        get => GetValue(ShowPercentageProperty);
        set => SetValue(ShowPercentageProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the value for each item.
    /// </summary>
    public bool ShowValue
    {
        get => GetValue(ShowValueProperty);
        set => SetValue(ShowValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size for legend labels and percentages.
    /// </summary>
    public double LegendFontSize
    {
        get => GetValue(LegendFontSizeProperty);
        set => SetValue(LegendFontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the size of the color indicator circle.
    /// </summary>
    public double IndicatorSize
    {
        get => GetValue(IndicatorSizeProperty);
        set => SetValue(IndicatorSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the corner radius of the color indicator.
    /// </summary>
    public CornerRadius IndicatorCornerRadius
    {
        get => GetValue(IndicatorCornerRadiusProperty);
        set => SetValue(IndicatorCornerRadiusProperty, value);
    }

    #endregion

    /// <summary>
    /// The legend never takes more than this share of the row it shares with its pie, so a narrow
    /// card shrinks the legend (labels trim) instead of squeezing the pie down to a dot.
    /// </summary>
    private const double MaxShareOfRow = 0.4;

    private Control? _row;

    public PieChartLegend()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _row = Parent as Control;
        if (_row != null)
            _row.SizeChanged += OnRowSizeChanged;
        UpdateMaxWidth();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_row != null)
            _row.SizeChanged -= OnRowSizeChanged;
        _row = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            UpdateMaxWidth();
    }

    // MaxWidth rather than Width, so the Width a page or the expanded chart view asks for still
    // applies whenever the row has room for it.
    private void UpdateMaxWidth()
    {
        var rowWidth = _row?.Bounds.Width ?? 0;
        MaxWidth = rowWidth > 0 ? Math.Floor(rowWidth * MaxShareOfRow) : double.PositiveInfinity;
    }
}
