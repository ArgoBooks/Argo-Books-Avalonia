using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArgoBooks.Services;
using ArgoBooks.ViewModels;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView.Avalonia;

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

        // LiveCharts pie charts that load while their tab is collapsed (every tab except
        // Dashboard is IsVisible=false at startup) sometimes never paint their first frame
        // when the tab is later shown, leaving a blank circle next to a populated legend.
        // Watch each pie's effective viewport and force a redraw the moment it actually
        // becomes visible, which fills in the slices reliably.
        foreach (var pie in this.GetVisualDescendants().OfType<PieChart>())
        {
            pie.EffectiveViewportChanged -= OnPieChartViewportChanged;
            pie.EffectiveViewportChanged += OnPieChartViewportChanged;
        }
    }

    /// <summary>
    /// Pie charts that have already been forced to redraw since becoming visible, so the
    /// kick fires once per collapsed-to-visible transition rather than on every scroll.
    /// </summary>
    private readonly HashSet<PieChart> _renderedPieCharts = new();

    /// <summary>
    /// Forces a LiveCharts pie chart to redraw when it transitions from collapsed (empty
    /// viewport) to visible, working around blank pies after a tab switch.
    /// </summary>
    private void OnPieChartViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (sender is not PieChart pie)
            return;

        var isVisible = e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Height > 0;
        if (!isVisible)
        {
            // Left the tab (or scrolled out of view): allow the kick to fire again next time.
            _renderedPieCharts.Remove(pie);
            return;
        }

        // Only kick once per visible transition, and after layout settles, so the chart
        // has a real size before LiveCharts recomputes its geometry.
        if (_renderedPieCharts.Add(pie))
            Dispatcher.UIThread.Post(() => pie.CoreChart.Update(new ChartUpdateParams()),
                DispatcherPriority.Background);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        foreach (var pie in this.GetVisualDescendants().OfType<PieChart>())
            pie.EffectiveViewportChanged -= OnPieChartViewportChanged;
        _renderedPieCharts.Clear();

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
