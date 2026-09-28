using Avalonia;
using Avalonia.Controls;
using LiveChartsCore.Measure;

namespace ArgoBooks.Controls.Dashboard.Widgets;

public partial class ChartWidget : UserControl
{
    /// <summary>
    /// Below this width a legend beside the pie leaves the pie too small to read, so it goes underneath.
    /// </summary>
    private const double LegendBelowThreshold = 400;

    public ChartWidget()
    {
        InitializeComponent();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var position = finalSize.Width > 0 && finalSize.Width < LegendBelowThreshold
            ? LegendPosition.Bottom
            : LegendPosition.Right;

        if (DistributionChart.LegendPosition != position)
            DistributionChart.LegendPosition = position;

        return base.ArrangeOverride(finalSize);
    }
}
