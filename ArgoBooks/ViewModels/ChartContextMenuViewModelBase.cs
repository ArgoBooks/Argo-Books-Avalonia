using ArgoBooks.Core.Enums;
using ArgoBooks.Shared.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Base class for ViewModels that support chart context menu functionality.
/// Provides common properties and commands for showing/hiding the context menu
/// and handling chart actions like save, export, and zoom reset.
/// </summary>
public abstract partial class ChartContextMenuViewModelBase : ViewModelBase
{
    #region Chart Context Menu Properties

    [ObservableProperty]
    private bool _isChartContextMenuOpen;

    [ObservableProperty]
    private double _chartContextMenuX;

    [ObservableProperty]
    private double _chartContextMenuY;

    [ObservableProperty]
    private bool _showChartResetZoom = true;

    [ObservableProperty]
    private bool _showChartExportOptions = true;

    [ObservableProperty]
    private bool _showGoogleSheetsExport = true;

    /// <summary>
    /// Gets or sets the chart data type of the currently selected chart for context menu operations.
    /// </summary>
    [ObservableProperty]
    private ChartDataType? _selectedChartDataType;

    #endregion

    #region Chart Context Menu Methods

    /// <summary>
    /// Shows the chart context menu at the specified position.
    /// </summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="chartDataType">The ChartDataType from the control's Tag, if any.</param>
    /// <param name="isPieChart">True if the chart is a pie chart (hides reset zoom).</param>
    /// <param name="isGeoMap">True if the chart is a geo map (only shows save as image).</param>
    /// <param name="parentWidth">The width of the parent container for bounds checking.</param>
    /// <param name="parentHeight">The height of the parent container for bounds checking.</param>
    public void ShowChartContextMenu(double x, double y, ChartDataType? chartDataType = null, bool isPieChart = false, bool isGeoMap = false, double parentWidth = 0, double parentHeight = 0)
    {
        // Menu dimensions (approximate)
        const double menuWidth = 220;
        const double menuHeight = 180;

        // Adjust position to prevent overflow
        double adjustedX = x;
        double adjustedY = y;

        if (parentWidth > 0 && x + menuWidth > parentWidth)
        {
            adjustedX = Math.Max(0, parentWidth - menuWidth - 10);
        }

        if (parentHeight > 0 && y + menuHeight > parentHeight)
        {
            adjustedY = Math.Max(0, parentHeight - menuHeight - 10);
        }

        ChartContextMenuX = adjustedX;
        ChartContextMenuY = adjustedY;
        SelectedChartDataType = chartDataType;
        ShowChartResetZoom = !isPieChart && !isGeoMap;
        ShowChartExportOptions = true;
        ShowGoogleSheetsExport = !isGeoMap;
        IsChartContextMenuOpen = true;
    }

    /// <summary>
    /// Hides the chart context menu.
    /// </summary>
    [RelayCommand]
    private void HideChartContextMenu()
    {
        IsChartContextMenuOpen = false;
    }

    #endregion

    #region Chart Context Menu Commands

    /// <summary>
    /// Saves the chart as an image file.
    /// </summary>
    [RelayCommand]
    private void SaveChartAsImage()
    {
        IsChartContextMenuOpen = false;
        OnSaveChartAsImage();
    }

    /// <summary>
    /// Exports the chart data to Google Sheets.
    /// </summary>
    [RelayCommand]
    private void ExportToGoogleSheets()
    {
        IsChartContextMenuOpen = false;
        // Tracked here rather than in each override: every chart on every page routes
        // through this command, so one call covers them all and a new chart type gets
        // the tracking for free.
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ChartExportedToGoogleSheets);
        OnExportToGoogleSheets();
    }

    /// <summary>
    /// Exports the chart data to Microsoft Excel.
    /// </summary>
    [RelayCommand]
    private void ExportToExcel()
    {
        IsChartContextMenuOpen = false;
        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ChartExportedToExcel);
        OnExportToExcel();
    }

    /// <summary>
    /// Resets the zoom on the chart.
    /// </summary>
    [RelayCommand]
    private void ResetChartZoom()
    {
        IsChartContextMenuOpen = false;
        OnResetChartZoom();
    }

    #endregion

    #region Virtual Methods for Derived Classes

    /// <summary>
    /// Called when the user requests to save the chart as an image.
    /// Override in derived classes to implement specific behavior.
    /// </summary>
    protected virtual void OnSaveChartAsImage() { }

    /// <summary>
    /// Called when the user requests to export to Google Sheets.
    /// Override in derived classes to implement specific behavior.
    /// </summary>
    protected virtual void OnExportToGoogleSheets() { }

    /// <summary>
    /// Called when the user requests to export to Excel.
    /// Override in derived classes to implement specific behavior.
    /// </summary>
    protected virtual void OnExportToExcel() { }

    /// <summary>
    /// Called when the user requests to reset the chart zoom.
    /// Override in derived classes to implement specific behavior.
    /// </summary>
    protected virtual void OnResetChartZoom() { }

    #endregion
}
