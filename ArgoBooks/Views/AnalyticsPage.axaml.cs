using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArgoBooks.Services;
using ArgoBooks.ViewModels;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsGeneratedCode;

namespace ArgoBooks.Views;

/// <summary>
/// Code-behind for the Analytics page.
/// </summary>
public partial class AnalyticsPage : UserControl
{
    private readonly PageChartInteractions _charts;

    /// <summary>
    /// Sets the clicked chart reference from an external source (e.g., ChartExpandOverlay).
    /// </summary>
    public void SetClickedChart(Control? chart, string name) => _charts.SetClickedChart(chart, name);

    public AnalyticsPage()
    {
        InitializeComponent();
        _charts = new PageChartInteractions(this);

        // Subscribe to ViewModel events when DataContext changes
        DataContextChanged += OnDataContextChanged;
    }

    private AnalyticsPageViewModel? _previousViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_previousViewModel != null)
        {
            _previousViewModel.SaveChartImageRequested -= _charts.OnSaveChartImageRequested;
            _previousViewModel.ExcelExportRequested -= _charts.OnExcelExportRequested;
            _previousViewModel = null;
        }

        if (DataContext is AnalyticsPageViewModel viewModel)
        {
            viewModel.SaveChartImageRequested += _charts.OnSaveChartImageRequested;
            viewModel.ExcelExportRequested += _charts.OnExcelExportRequested;
            _previousViewModel = viewModel;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // LiveCharts drops a redraw when the chart has painted no frame since its last one, as a chart on a hidden tab has not, so a chart that gets a size is redrawn until one goes through.
        foreach (var chart in this.GetVisualDescendants().OfType<SourceGenChart>())
        {
            chart.PropertyChanged -= OnChartPropertyChanged;
            chart.PropertyChanged += OnChartPropertyChanged;
            chart.CoreChart.UpdateStarted -= OnChartDrawn;
            chart.CoreChart.UpdateStarted += OnChartDrawn;
        }
    }

    private const int MaxRedrawTries = 20;

    /// <summary>Charts given a size that LiveCharts has not drawn at yet, with how many redraws each has been asked for.</summary>
    private readonly Dictionary<SourceGenChart, int> _chartsAwaitingDraw = new();

    private DispatcherTimer? _redrawTimer;

    private void OnChartPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not SourceGenChart chart || e.Property != Visual.BoundsProperty)
            return;

        if (chart.Bounds.Width <= 0 || chart.Bounds.Height <= 0)
        {
            _chartsAwaitingDraw.Remove(chart);
            return;
        }

        _chartsAwaitingDraw[chart] = 0;
        _redrawTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, RetryRedraws);
        _redrawTimer.Start();
    }

    /// <summary>Only a draw with a real size raises UpdateStarted, so it marks the chart done.</summary>
    private void OnChartDrawn(IChartView view)
    {
        if (view is SourceGenChart chart)
            _chartsAwaitingDraw.Remove(chart);
    }

    private void RetryRedraws(object? sender, EventArgs e)
    {
        foreach (var (chart, tries) in _chartsAwaitingDraw.ToList())
        {
            if (tries >= MaxRedrawTries)
            {
                _chartsAwaitingDraw.Remove(chart);
                continue;
            }

            _chartsAwaitingDraw[chart] = tries + 1;
            // Painting a frame is what lets LiveCharts accept the next redraw.
            chart.CoreCanvas.Invalidate();
            chart.CoreChart.Update(new ChartUpdateParams { Throttling = false });
        }

        if (_chartsAwaitingDraw.Count == 0)
            _redrawTimer?.Stop();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        foreach (var chart in this.GetVisualDescendants().OfType<SourceGenChart>())
        {
            chart.PropertyChanged -= OnChartPropertyChanged;
            chart.CoreChart.UpdateStarted -= OnChartDrawn;
        }
        _redrawTimer?.Stop();
        _chartsAwaitingDraw.Clear();

        if (_previousViewModel != null)
        {
            _previousViewModel.SaveChartImageRequested -= _charts.OnSaveChartImageRequested;
            _previousViewModel.ExcelExportRequested -= _charts.OnExcelExportRequested;
            _previousViewModel = null;
        }
    }

    private AnalyticsPageViewModel? ViewModel => DataContext as AnalyticsPageViewModel;

    /// <summary>
    /// Handles right-click on charts to show the context menu.
    /// </summary>
    private void OnChartPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(sender as Control).Properties;

        if (properties.IsRightButtonPressed)
        {
            if (DataContext is AnalyticsPageViewModel)
            {
                _charts.ShowContextMenu(sender as Control, e);
                e.Handled = true;
            }
        }
        else if (properties.IsLeftButtonPressed)
        {
            // Close context menu on left click
            if (DataContext is AnalyticsPageViewModel viewModel)
            {
                viewModel.HideChartContextMenuCommand.Execute(null);
            }

            // Set hand cursor when panning
            if (sender is CartesianChart chart)
            {
                chart.Cursor = new Cursor(StandardCursorType.Hand);
            }
        }
    }

    /// <summary>
    /// Restores the default cursor when pointer is released after panning.
    /// </summary>
    private void OnChartPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is CartesianChart chart)
        {
            chart.Cursor = Cursor.Default;
        }
    }
}
