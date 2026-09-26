#pragma warning disable CS0618 // LabelVisual is obsolete, DrawnLabelVisual is not API-compatible
using ArgoBooks.Controls;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.VisualElements;
using OfficeOpenXml.Drawing.Chart;

namespace ArgoBooks.Services;

/// <summary>
/// What every page of charts does with them: the right-click menu, saving a chart as an image,
/// exporting its data to Excel, and letting the mouse wheel scroll the page. The Dashboard and
/// Analytics pages each own one.
/// </summary>
public sealed class PageChartInteractions
{
    private readonly UserControl _page;
    private Control? _clickedChart;
    private string _clickedChartName = "Chart";

    public PageChartInteractions(UserControl page)
    {
        _page = page;
        page.PointerPressed += OnPagePointerPressed;

        // Tunnel with handledEventsToo, because LiveCharts handles both first: the wheel would zoom
        // instead of scrolling, and a right-click would start a selection box.
        page.AddHandler(InputElement.PointerWheelChangedEvent, OnChartPointerWheelChanged,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        page.AddHandler(InputElement.PointerPressedEvent, OnChartPointerPressedTunnel,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private ChartContextMenuViewModelBase? ViewModel => _page.DataContext as ChartContextMenuViewModelBase;

    /// <summary>
    /// Sets the chart the menu acts on from outside the page, such as the expanded chart overlay.
    /// </summary>
    public void SetClickedChart(Control? chart, string name)
    {
        _clickedChart = chart;
        _clickedChartName = name;
    }

    /// <summary>
    /// Opens the chart menu for a right-click on <paramref name="clicked"/>, a chart or a pie
    /// chart's legend, which stands for the pie beside it.
    /// </summary>
    public void ShowContextMenu(Control? clicked, PointerEventArgs e)
    {
        if (clicked == null || ViewModel is not { } viewModel)
            return;

        var legend = clicked as PieChartLegend ?? clicked.FindAncestorOfType<PieChartLegend>();
        if (legend != null)
        {
            if ((legend.Parent as Grid)?.Children.OfType<PieChart>().FirstOrDefault() is { } siblingPie)
                _clickedChart = siblingPie;
        }
        else
        {
            _clickedChart = FindChart(clicked);
        }

        _clickedChartName = GetChartTitle(legend ?? _clickedChart) ?? "Chart";

        var position = e.GetPosition(_page);
        viewModel.ShowChartContextMenu(position.X, position.Y,
            chartDataType: _clickedChart?.Tag as ChartDataType?,
            isPieChart: legend != null || _clickedChart is PieChart,
            isGeoMap: _clickedChart is GeoMap,
            parentWidth: _page.Bounds.Width, parentHeight: _page.Bounds.Height);
    }

    private static Control? FindChart(Control source) =>
        source as CartesianChart ?? source.FindAncestorOfType<CartesianChart>()
        ?? (Control?)(source as PieChart ?? source.FindAncestorOfType<PieChart>())
        ?? source as GeoMap ?? source.FindAncestorOfType<GeoMap>();

    private void OnChartPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control source || FindChart(source) == null
            || !e.GetCurrentPoint(_page).Properties.IsRightButtonPressed)
            return;

        ShowContextMenu(source, e);
        e.Handled = true;
    }

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { IsChartContextMenuOpen: true } viewModel)
            return;

        var contextMenu = _page.FindControl<ChartContextMenu>("ChartContextMenu");
        if (contextMenu == null)
            return;

        var position = e.GetPosition(contextMenu);
        var bounds = contextMenu.Bounds;
        if (position.X < 0 || position.Y < 0 || position.X > bounds.Width || position.Y > bounds.Height)
            viewModel.HideChartContextMenuCommand.Execute(null);
    }

    /// <summary>
    /// Scrolls the page when the wheel turns over a chart, which LiveCharts would otherwise use to
    /// zoom. Holding Ctrl (Cmd on a Mac) or Shift lets the chart zoom.
    /// </summary>
    public static void OnChartPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var source = e.Source as Control;
        var chart = source?.FindAncestorOfType<CartesianChart>() ?? source as CartesianChart;
        if (chart == null || e.KeyModifiers.HasZoomModifier() || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        e.Handled = true;

        var scrollViewer = chart.FindAncestorOfType<ScrollViewer>();
        if (scrollViewer == null)
            return;

        var linesToScroll = (int)Math.Round(e.Delta.Y * 3);
        for (int i = 0; i < Math.Abs(linesToScroll); i++)
        {
            if (linesToScroll > 0)
                scrollViewer.LineUp();
            else
                scrollViewer.LineDown();
        }
    }

    /// <summary>
    /// The chart's title: its own Title, or for a pie chart or map without one, the TextBlock in
    /// the first row of the grid around it (Grid rows > Grid columns > chart and legend).
    /// </summary>
    public static string? GetChartTitle(Control? control)
    {
        if (control is CartesianChart { Title: LabelVisual cartesianLabel } &&
            !string.IsNullOrWhiteSpace(cartesianLabel.Text))
            return cartesianLabel.Text;

        if (control is PieChart { Title: LabelVisual pieLabel } &&
            !string.IsNullOrWhiteSpace(pieLabel.Text))
            return pieLabel.Text;

        if (control is PieChart or PieChartLegend or GeoMap &&
            (control.Parent as Grid)?.Parent is Grid rowGrid)
        {
            return rowGrid.Children
                .OfType<TextBlock>()
                .FirstOrDefault(t => Grid.GetRow(t) == 0 && !string.IsNullOrWhiteSpace(t.Text))
                ?.Text;
        }

        return null;
    }

    public async void OnSaveChartImageRequested(object? sender, SaveChartImageEventArgs e)
    {
        try
        {
            if (_clickedChart == null || TopLevel.GetTopLevel(_page) is not { } topLevel)
                return;

            await ChartImageExportService.SaveChartAsImageAsync(
                topLevel, _clickedChart, ChartImageExportService.CreateSafeFileName(_clickedChartName));
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Export, "OnSaveChartImageRequested");
        }
    }

    public async void OnExcelExportRequested(object? sender, ExcelExportEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(_page) is not { } topLevel)
                return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Chart to Excel",
                SuggestedFileName = ChartImageExportService.CreateSafeFileName(e.ChartTitle),
                DefaultExtension = "xlsx",
                FileTypeChoices =
                [
                    new FilePickerFileType("Excel Workbook") { Patterns = ["*.xlsx"] }
                ]
            });

            if (file == null) return;

            try
            {
                await ExportToExcelAsync(file.Path.LocalPath, e);
            }
            catch (Exception ex)
            {
                App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Export, "Failed to export chart to Excel");
                await App.ShowErrorDialogAsync(
                    "Export Failed".Translate(),
                    "Failed to export the chart to Excel: {0}".TranslateFormat(ex.Message));
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Export, "OnExcelExportRequested");
        }
    }

    private static async Task ExportToExcelAsync(string filePath, ExcelExportEventArgs e)
    {
        var excelChartType = e.ChartStyle switch
        {
            ChartStyle.Column => eChartType.ColumnClustered,
            ChartStyle.Area => eChartType.Area,
            ChartStyle.Scatter => eChartType.XYScatter,
            _ => eChartType.Line
        };

        if (e.IsRegionMap)
        {
            await ChartExcelExportService.ExportRegionMapChartAsync(
                filePath, e.ChartTitle, e.RegionMapData, valueHeader: "Amount", isCurrency: true);
        }
        else if (e.IsMultiSeries)
        {
            var seriesData = new Dictionary<string, double[]> { { e.SeriesName, e.Values } };
            foreach (var (name, values) in e.AdditionalSeries)
                seriesData[name] = values;

            await ChartExcelExportService.ExportMultiSeriesChartAsync(
                filePath, e.ChartTitle, e.Labels, seriesData,
                labelHeader: "Date", isCurrency: true, excelChartType: excelChartType);
        }
        else if (e.IsDistribution)
        {
            await ChartExcelExportService.ExportDistributionChartAsync(
                filePath, e.ChartTitle, e.Labels, e.Values,
                categoryHeader: "Category", valueHeader: e.SeriesName, isCurrency: true);
        }
        else
        {
            var isCurrency = e.ChartType != ChartType.Comparison ||
                             !e.SeriesName.Contains("Count", StringComparison.OrdinalIgnoreCase);

            await ChartExcelExportService.ExportChartAsync(
                filePath, e.ChartTitle, e.Labels, e.Values,
                column1Header: "Date", column2Header: e.SeriesName,
                isCurrency: isCurrency, excelChartType: excelChartType);
        }
    }
}
