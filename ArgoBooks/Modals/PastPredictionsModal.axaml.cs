using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.ViewModels;
using LiveChartsCore.SkiaSharpView.Avalonia;

namespace ArgoBooks.Modals;

public partial class PastPredictionsModal : UserControl
{
    private CartesianChart? _accuracyChart;
    private PastPredictionsModalViewModel? _subscribedVm;

    public PastPredictionsModal()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private PastPredictionsModalViewModel? ViewModel => DataContext as PastPredictionsModalViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old ViewModel
        if (_subscribedVm != null)
        {
            _subscribedVm.SaveChartImageRequested -= OnSaveChartImageRequested;
            _subscribedVm.GoogleSheetsExportRequested -= OnGoogleSheetsExportRequested;
            _subscribedVm.ExcelExportRequested -= OnExcelExportRequested;
            _subscribedVm = null;
        }

        // Subscribe to new ViewModel
        if (ViewModel != null)
        {
            _subscribedVm = ViewModel;
            ViewModel.SaveChartImageRequested += OnSaveChartImageRequested;
            ViewModel.GoogleSheetsExportRequested += OnGoogleSheetsExportRequested;
            ViewModel.ExcelExportRequested += OnExcelExportRequested;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Attach handler at control level to intercept scroll events
        AddHandler(
            PointerWheelChangedEvent,
            PageChartInteractions.OnChartPointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        // Intercept right-click in tunneling phase to prevent LiveCharts selection box
        AddHandler(
            PointerPressedEvent,
            OnChartPointerPressedTunnel,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    /// <summary>
    /// Intercepts right-click in tunneling phase to prevent LiveCharts from starting selection box.
    /// </summary>
    private void OnChartPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Control;
        var chart = source?.FindAncestorOfType<CartesianChart>() ?? source as CartesianChart;

        if (chart != null && e.GetCurrentPoint(this).Properties.IsRightButtonPressed && ViewModel != null)
        {
            _accuracyChart = chart;
            ViewModel.AccuracyChart = _accuracyChart;

            var position = e.GetPosition(this);
            ViewModel.ChartContextMenuX = position.X;
            ViewModel.ChartContextMenuY = position.Y;
            ViewModel.IsChartContextMenuOpen = true;

            e.Handled = true;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_subscribedVm != null)
        {
            _subscribedVm.SaveChartImageRequested -= OnSaveChartImageRequested;
            _subscribedVm.GoogleSheetsExportRequested -= OnGoogleSheetsExportRequested;
            _subscribedVm.ExcelExportRequested -= OnExcelExportRequested;
            _subscribedVm = null;
        }

        DataContextChanged -= OnDataContextChanged;
    }

    /// <summary>
    /// Handles right-click on the chart to show context menu.
    /// </summary>
    private void OnChartPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(sender as Control).Properties;

        if (properties.IsRightButtonPressed && ViewModel != null)
        {
            _accuracyChart = sender as CartesianChart;
            ViewModel.AccuracyChart = _accuracyChart;

            // Get click position relative to this control
            var position = e.GetPosition(this);
            ViewModel.ChartContextMenuX = position.X;
            ViewModel.ChartContextMenuY = position.Y;
            ViewModel.IsChartContextMenuOpen = true;

            e.Handled = true;
        }
        else if (properties.IsLeftButtonPressed)
        {
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
    /// Handles the save chart image request using the shared service.
    /// </summary>
    private async void OnSaveChartImageRequested(object? sender, EventArgs e)
    {
        try
        {
            if (_accuracyChart == null) return;
            if (ViewModel == null || !ViewModel.HasChartData) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            // Ensure the chart has been rendered with valid dimensions
            if (_accuracyChart.Bounds.Width <= 0 || _accuracyChart.Bounds.Height <= 0)
            {
                // Force a layout pass before export
                _accuracyChart.InvalidateMeasure();
                _accuracyChart.InvalidateArrange();
            }

            await ChartImageExportService.SaveChartAsImageAsync(
                topLevel,
                _accuracyChart,
                ChartImageExportService.CreateSafeFileName("Prediction Accuracy"));
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Export, "OnSaveChartImageRequested");
        }
    }

    /// <summary>
    /// Handles the Google Sheets export request.
    /// </summary>
    private async void OnGoogleSheetsExportRequested(object? sender, EventArgs e)
    {
        if (ViewModel == null) return;

        var (labels, revenueAccuracy, expensesAccuracy) = ViewModel.GetExportData();
        if (labels.Length == 0) return;

        try
        {
            // Ensure Google is authorized (auto-initiates OAuth if needed)
            var isAuthenticated = await GoogleCredentialsManager.EnsureAuthenticatedAsync();
            if (!isAuthenticated)
            {
                await App.ShowWarningDialogAsync(
                    "Export Failed",
                    "Google Sheets authorization was not completed. Please try again.");
                return;
            }

            // Prepare data for export
            var exportData = new List<List<object>>
            {
                new() { "Period", "Revenue Accuracy (%)", "Expenses Accuracy (%)" }
            };

            for (int i = 0; i < labels.Length; i++)
            {
                exportData.Add(new List<object>
                {
                    labels[i],
                    revenueAccuracy[i],
                    i < expensesAccuracy.Length ? expensesAccuracy[i] : 0
                });
            }

            var companyName = App.CompanyManager?.CompanyData?.Settings.Company.Name ?? "Company";
            var sheetsService = new GoogleSheetsService();
            var url = await sheetsService.ExportFormattedDataToGoogleSheetsAsync(
                exportData,
                "Prediction Accuracy Over Time",
                GoogleSheetsService.ChartType.Line,
                companyName);

            if (!string.IsNullOrEmpty(url))
            {
                // Open the spreadsheet in browser
                var launcher = TopLevel.GetTopLevel(this)?.Launcher;
                if (launcher != null)
                {
                    await launcher.LaunchUriAsync(new Uri(url));
                }
            }
        }
        catch (InvalidOperationException ex) when (ConnectivityMessage.IsConnectivityMessage(ex.Message))
        {
            await App.ShowConnectivityErrorAsync(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // Non-connectivity service error (e.g. the server rejected the request).
            await App.ShowErrorDialogAsync(
                "Export Failed",
                ex.Message);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Export, "Failed to export to Google Sheets");
            await App.ShowErrorDialogAsync(
                "Export Failed",
                $"Failed to export to Google Sheets: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the Excel export request.
    /// </summary>
    private async void OnExcelExportRequested(object? sender, EventArgs e)
    {
        if (ViewModel == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var (labels, revenueAccuracy, expensesAccuracy) = ViewModel.GetExportData();
        if (labels.Length == 0) return;

        var suggestedFileName = $"Prediction_Accuracy_{DateTime.Now:yyyy-MM-dd}";

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

            var seriesData = new Dictionary<string, double[]>
            {
                { "Revenue Accuracy (%)", revenueAccuracy },
                { "Expenses Accuracy (%)", expensesAccuracy }
            };

            await ChartExcelExportService.ExportMultiSeriesChartAsync(
                filePath,
                "Prediction Accuracy Over Time",
                labels,
                seriesData,
                labelHeader: "Period",
                isCurrency: false,
                excelChartType: OfficeOpenXml.Drawing.Chart.eChartType.Line);

        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Export, "Failed to export to Excel");
            await App.ShowErrorDialogAsync(
                "Export Failed",
                $"Failed to export to Excel: {ex.Message}");
        }
    }
}
