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

        // A pie handed its series while its tab is collapsed draws nothing, and the size arriving
        // later does not make LiveCharts look again, so each one is redrawn once it has a size.
        foreach (var pie in this.GetVisualDescendants().OfType<PieChart>())
        {
            pie.PropertyChanged -= OnPieChartPropertyChanged;
            pie.PropertyChanged += OnPieChartPropertyChanged;
        }
    }

    /// <summary>Pies already redrawn since they last had a size, so each gets one redraw.</summary>
    private readonly HashSet<PieChart> _renderedPieCharts = new();

    /// <summary>Redraws a pie once it has a size. Losing the size arms it for the next visit.</summary>
    private void OnPieChartPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not PieChart pie || e.Property != Visual.BoundsProperty)
            return;

        if (pie.Bounds.Width <= 0 || pie.Bounds.Height <= 0)
        {
            _renderedPieCharts.Remove(pie);
            return;
        }

        // Posted, so the redraw reads the size it has now rather than the one it is about to get.
        if (_renderedPieCharts.Add(pie))
            Dispatcher.UIThread.Post(() => pie.CoreChart.Update(new ChartUpdateParams()),
                DispatcherPriority.Background);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        foreach (var pie in this.GetVisualDescendants().OfType<PieChart>())
            pie.PropertyChanged -= OnPieChartPropertyChanged;
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
