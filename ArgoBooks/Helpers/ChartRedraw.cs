using Avalonia;
using Avalonia.Threading;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsGeneratedCode;

namespace ArgoBooks.Helpers;

/// <summary>
/// Makes sure every LiveCharts chart in the app draws once it has a size.
///
/// <para>
/// LiveCharts drops a redraw when the chart has painted no frame since its last one, and never
/// retries it. A chart given its data before it has a size, such as one on a hidden tab or a
/// dashboard widget just created, uses that slot on a draw at 0x0, so the redraw at its real size
/// is often dropped on a slower machine and the chart stays blank. A chart that gets a size is
/// asked to paint a frame and redraw every 100 ms until LiveCharts raises UpdateStarted, which it
/// does only for a draw at a real size, and given up on after two seconds.
/// </para>
/// </summary>
internal static class ChartRedraw
{
    private const int MaxTries = 20;

    /// <summary>Charts given a size that LiveCharts has not drawn at yet, with how many redraws each has been asked for.</summary>
    private static readonly Dictionary<SourceGenChart, int> AwaitingDraw = new();

    private static DispatcherTimer? _timer;

    /// <summary>Watches every chart the app creates from now on. Called once at start-up.</summary>
    public static void Register() =>
        Visual.BoundsProperty.Changed.AddClassHandler<SourceGenChart>((chart, _) => OnBoundsChanged(chart));

    private static void OnBoundsChanged(SourceGenChart chart)
    {
        if (chart.Bounds.Width <= 0 || chart.Bounds.Height <= 0)
        {
            AwaitingDraw.Remove(chart);
            return;
        }

        chart.CoreChart.UpdateStarted -= OnChartDrawn;
        chart.CoreChart.UpdateStarted += OnChartDrawn;
        AwaitingDraw[chart] = 0;
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, RetryRedraws);
        _timer.Start();
    }

    private static void OnChartDrawn(IChartView view)
    {
        if (view is SourceGenChart chart)
            AwaitingDraw.Remove(chart);
    }

    private static void RetryRedraws(object? sender, EventArgs e)
    {
        foreach (var (chart, tries) in AwaitingDraw.ToList())
        {
            if (tries >= MaxTries)
            {
                AwaitingDraw.Remove(chart);
                continue;
            }

            AwaitingDraw[chart] = tries + 1;
            // Painting a frame is what lets LiveCharts accept the next redraw.
            chart.CoreCanvas.Invalidate();
            chart.CoreChart.Update(new ChartUpdateParams { Throttling = false });
        }

        if (AwaitingDraw.Count == 0)
            _timer?.Stop();
    }
}
