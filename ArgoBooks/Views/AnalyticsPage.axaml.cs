#pragma warning disable CS0618 // LabelVisual is obsolete, DrawnLabelVisual is not API-compatible
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArgoBooks.Controls;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.ViewModels;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView.Avalonia;
using OfficeOpenXml.Drawing.Chart;
using LiveChartsCore.SkiaSharpView.VisualElements;

namespace ArgoBooks.Views;

/// <summary>
/// Code-behind for the Analytics page.
/// </summary>
public partial class AnalyticsPage : UserControl
{
    /// <summary>
    /// Stores a reference to the last clicked chart control for export operations.
    /// </summary>
    private Control? _clickedChart;

    /// <summary>
    /// Sets the clicked chart reference from an external source (e.g., ChartExpandOverlay).
    /// </summary>
    public void SetClickedChart(Control? chart, string name)
    {
        _clickedChart = chart;
        _clickedChartName = name;
    }

    public AnalyticsPage()
    {
        InitializeComponent();

        // Subscribe to ViewModel events when DataContext changes
        DataContextChanged += OnDataContextChanged;
    }

    private AnalyticsPageViewModel? _previousViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_previousViewModel != null)
        {
            _previousViewModel.SaveChartImageRequested -= OnSaveChartImageRequested;
            _previousViewModel.ExcelExportRequested -= OnExcelExportRequested;
            _previousViewModel = null;
        }

        if (DataContext is AnalyticsPageViewModel viewModel)
        {
            viewModel.SaveChartImageRequested += OnSaveChartImageRequested;
            viewModel.ExcelExportRequested += OnExcelExportRequested;
            _previousViewModel = viewModel;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Wire up page-level click handler to close context menu
        PointerPressed += OnPagePointerPressed;

        // Attach handler at page level with handledEventsToo to intercept events handled by LiveCharts
        AddHandler(
            PointerWheelChangedEvent,
            OnChartPointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        // Intercept right-click in tunneling phase to prevent LiveCharts selection box
        AddHandler(
            PointerPressedEvent,
            OnChartPointerPressedTunnel,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

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

    /// <summary>
    /// Intercepts right-click in tunneling phase to prevent LiveCharts from starting selection box.
    /// </summary>
    private void OnChartPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Control;
        var chart = source?.FindAncestorOfType<CartesianChart>() ?? source as CartesianChart;
        var pieChart = source?.FindAncestorOfType<PieChart>() ?? source as PieChart;
        var geoMap = source?.FindAncestorOfType<GeoMap>() ?? source as GeoMap;

        if ((chart != null || pieChart != null || geoMap != null) && e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // Show context menu and mark as handled to prevent LiveCharts selection box
            if (DataContext is AnalyticsPageViewModel viewModel)
            {
                var clickedControl = source;

                // For PieChartLegend, find the associated PieChart sibling
                var legend = source?.FindAncestorOfType<PieChartLegend>() ?? source as PieChartLegend;
                if (legend != null && legend.Parent is Grid parentGrid)
                {
                    foreach (var child in parentGrid.Children)
                    {
                        if (child is PieChart siblingChart)
                        {
                            _clickedChart = siblingChart;
                            clickedControl = siblingChart;
                            break;
                        }
                    }
                }
                else
                {
                    _clickedChart = (Control?)chart ?? (Control?)pieChart ?? geoMap;
                    clickedControl = _clickedChart;
                }

                _clickedChartName = GetChartTitle(clickedControl) ?? "Chart";
                var position = e.GetPosition(this);
                var isPieChart = pieChart != null || legend != null;
                var isGeoMap = geoMap != null;
                var chartDataType = _clickedChart?.Tag as ChartDataType?;

                viewModel.ShowChartContextMenu(position.X, position.Y, chartDataType: chartDataType, isPieChart: isPieChart, isGeoMap: isGeoMap,
                    parentWidth: Bounds.Width, parentHeight: Bounds.Height);
            }
            e.Handled = true;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        PointerPressed -= OnPagePointerPressed;

        foreach (var pie in this.GetVisualDescendants().OfType<PieChart>())
            pie.EffectiveViewportChanged -= OnPieChartViewportChanged;
        _renderedPieCharts.Clear();

        if (_previousViewModel != null)
        {
            _previousViewModel.SaveChartImageRequested -= OnSaveChartImageRequested;
            _previousViewModel.ExcelExportRequested -= OnExcelExportRequested;
            _previousViewModel = null;
        }
    }

    private AnalyticsPageViewModel? ViewModel => DataContext as AnalyticsPageViewModel;

    /// <summary>
    /// The name of the last clicked chart for file naming.
    /// </summary>
    private string _clickedChartName = "Chart";

    /// <summary>
    /// Handles right-click on charts to show the context menu.
    /// </summary>
    private void OnChartPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(sender as Control).Properties;

        if (properties.IsRightButtonPressed)
        {
            if (DataContext is AnalyticsPageViewModel viewModel)
            {
                var clickedControl = sender as Control;

                // For PieChartLegend, find the associated PieChart sibling
                if (clickedControl is PieChartLegend legend)
                {
                    // Navigate: PieChartLegend → Parent Grid → find PieChart sibling
                    if (legend.Parent is Grid parentGrid)
                    {
                        foreach (var child in parentGrid.Children)
                        {
                            if (child is PieChart siblingChart)
                            {
                                _clickedChart = siblingChart;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // Store reference to the clicked chart for later use in save/export operations
                    _clickedChart = clickedControl;
                }

                // Try to find the chart title from the parent container
                _clickedChartName = GetChartTitle(clickedControl) ?? "Chart";

                // Get position relative to this page (the Panel container) for proper menu placement
                var position = e.GetPosition(this);
                var isPieChart = sender is PieChart || sender is PieChartLegend;
                var isGeoMap = sender is GeoMap;
                var chartDataType = _clickedChart?.Tag as ChartDataType?;

                viewModel.ShowChartContextMenu(position.X, position.Y, chartDataType: chartDataType, isPieChart: isPieChart, isGeoMap: isGeoMap,
                    parentWidth: Bounds.Width, parentHeight: Bounds.Height);
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

    /// <summary>
    /// Attempts to find the chart title from the chart's Title property or from a sibling TextBlock.
    /// </summary>
    private static string? GetChartTitle(Control? control)
    {
        if (control == null) return null;

        // Get the title directly from LiveCharts chart controls
        // The Title property is a LabelVisual which has a Text property
        if (control is CartesianChart cartesianChart &&
            cartesianChart.Title is LabelVisual cartesianLabel &&
            !string.IsNullOrWhiteSpace(cartesianLabel.Text))
        {
            return cartesianLabel.Text;
        }

        if (control is PieChart pieChart &&
            pieChart.Title is LabelVisual pieLabel &&
            !string.IsNullOrWhiteSpace(pieLabel.Text))
        {
            return pieLabel.Text;
        }

        // For pie charts and legends without a Title property, look for a TextBlock title
        // in the parent container structure:
        // Grid (RowDefinitions="Auto,*")
        //   ├─ TextBlock Row="0" (the title)
        //   └─ Grid Row="1"
        //       ├─ PieChart/GeoMap Column="0"
        //       └─ PieChartLegend Column="1"
        if (control is PieChart or PieChartLegend or GeoMap)
        {
            // Navigate up: Control → Grid (Column container) → Grid (Row container)
            var columnGrid = control.Parent as Grid;
            var rowGrid = columnGrid?.Parent as Grid;

            if (rowGrid != null)
            {
                // Find the TextBlock in Row 0 (the title)
                foreach (var child in rowGrid.Children)
                {
                    if (child is TextBlock textBlock &&
                        Grid.GetRow(textBlock) == 0 &&
                        !string.IsNullOrWhiteSpace(textBlock.Text))
                    {
                        return textBlock.Text;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Handles the save chart as image request from the ViewModel.
    /// </summary>
    private async void OnSaveChartImageRequested(object? sender, SaveChartImageEventArgs e)
    {
        try
        {
            if (_clickedChart == null) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            await ChartImageExportService.SaveChartAsImageAsync(
                topLevel,
                _clickedChart,
                ChartImageExportService.CreateSafeFileName(_clickedChartName));
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Export, "OnSaveChartImageRequested");
        }
    }

    /// <summary>
    /// Handles the Excel export request from the ViewModel.
    /// </summary>
    private async void OnExcelExportRequested(object? sender, ExcelExportEventArgs e)
    {
        try
        {
        // Get the top-level window for the file picker
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        // Create safe filename from chart title
        var safeName = string.Join("_", e.ChartTitle.Split(Path.GetInvalidFileNameChars()));
        safeName = safeName.Replace(" ", "_");
        var suggestedFileName = $"{safeName}_{DateTime.Now:yyyy-MM-dd}";

        // Show save file dialog
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Chart to Excel",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel Workbook") { Patterns = ["*.xlsx"] }
            ]
        });

        if (file == null) return;

        try
        {
            var filePath = file.Path.LocalPath;

            // Map chart style to Excel chart type
            var excelChartType = e.ChartStyle switch
            {
                ChartStyle.Column => eChartType.ColumnClustered,
                ChartStyle.Area => eChartType.Area,
                ChartStyle.Scatter => eChartType.XYScatter,
                _ => eChartType.Line
            };

            // Export based on chart type
            if (e.IsRegionMap)
            {
                // Region Map chart (geographic heat map)
                await ChartExcelExportService.ExportRegionMapChartAsync(
                    filePath,
                    e.ChartTitle,
                    e.RegionMapData,
                    valueHeader: "Amount",
                    isCurrency: true);
            }
            else if (e.IsMultiSeries)
            {
                // Multi-series chart (e.g., Revenue vs Expenses)
                var seriesData = new Dictionary<string, double[]>
                {
                    { e.SeriesName, e.Values }
                };
                foreach (var (name, values) in e.AdditionalSeries)
                {
                    seriesData[name] = values;
                }

                await ChartExcelExportService.ExportMultiSeriesChartAsync(
                    filePath,
                    e.ChartTitle,
                    e.Labels,
                    seriesData,
                    labelHeader: "Date",
                    isCurrency: true,
                    excelChartType: excelChartType);
            }
            else if (e.IsDistribution)
            {
                // Distribution/Pie chart
                await ChartExcelExportService.ExportDistributionChartAsync(
                    filePath,
                    e.ChartTitle,
                    e.Labels,
                    e.Values,
                    categoryHeader: "Category",
                    valueHeader: e.SeriesName,
                    isCurrency: true);
            }
            else
            {
                // Single-series time chart
                var isCurrency = e.ChartType != ChartType.Comparison ||
                                 !e.SeriesName.Contains("Count", StringComparison.OrdinalIgnoreCase);

                await ChartExcelExportService.ExportChartAsync(
                    filePath,
                    e.ChartTitle,
                    e.Labels,
                    e.Values,
                    column1Header: "Date",
                    column2Header: e.SeriesName,
                    isCurrency: isCurrency,
                    excelChartType: excelChartType);
            }

        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Export, "Failed to export chart to Excel");
            var dialog = App.ConfirmationDialog;
            if (dialog != null)
            {
                await dialog.ShowAsync(new ConfirmationDialogOptions
                {
                    Title = "Export Failed".Translate(),
                    Message = "Failed to export the chart to Excel: {0}".TranslateFormat(ex.Message),
                    PrimaryButtonText = "OK".Translate(),
                    SecondaryButtonText = null,
                    CancelButtonText = null
                });
            }
        }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Export, "OnExcelExportRequested");
        }
    }

    /// <summary>
    /// Handles clicks on the page to close the context menu when clicking outside.
    /// </summary>
    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is AnalyticsPageViewModel { IsChartContextMenuOpen: true } viewModel)
        {
            var contextMenu = this.FindControl<ChartContextMenu>("ChartContextMenu");
            if (contextMenu != null)
            {
                var position = e.GetPosition(contextMenu);
                var bounds = contextMenu.Bounds;

                // If clicked outside the context menu, close it
                if (position.X < 0 || position.Y < 0 ||
                    position.X > bounds.Width || position.Y > bounds.Height)
                {
                    viewModel.HideChartContextMenuCommand.Execute(null);
                }
            }
        }
    }

    /// <summary>
    /// Handles pointer wheel events on charts to allow scroll passthrough to parent ScrollViewer.
    /// LiveCharts captures wheel events for zooming, so we intercept them and forward to the ScrollViewer.
    /// When CTRL or Shift is held, allow LiveCharts to handle zooming instead.
    /// </summary>
    private void OnChartPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Check if the event originated from a CartesianChart
        var source = e.Source as Control;
        var chart = source?.FindAncestorOfType<CartesianChart>() ?? source as CartesianChart;

        // Only intercept events that originate from a chart
        if (chart == null)
            return;

        // If CTRL or Shift is held, allow LiveCharts to handle zooming
        if (e.KeyModifiers.HasZoomModifier() || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return; // Don't intercept - let LiveCharts zoom
        }

        // Mark as handled to prevent LiveCharts from zooming when no modifier is held
        e.Handled = true;

        // Find the ScrollViewer and manually scroll it
        var scrollViewer = chart.FindAncestorOfType<ScrollViewer>();
        if (scrollViewer != null)
        {
            // Use ScrollViewer's built-in line scroll methods for natural scroll feel
            var linesToScroll = (int)Math.Round(e.Delta.Y * 3);
            for (int i = 0; i < Math.Abs(linesToScroll); i++)
            {
                if (linesToScroll > 0)
                    scrollViewer.LineUp();
                else
                    scrollViewer.LineDown();
            }
        }
    }
}
