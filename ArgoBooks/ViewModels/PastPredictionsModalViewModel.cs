#pragma warning disable CS0618 // LabelVisual is obsolete. DrawnLabelVisual is not API-compatible
using System.Collections.ObjectModel;
using ArgoBooks.Core;
using ArgoBooks.Core.Models.Insights;
using ArgoBooks.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.VisualElements;
using SkiaSharp;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Represents a single past prediction record for display in the modal.
/// </summary>
public partial class PastPredictionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _periodLabel = string.Empty;

    [ObservableProperty]
    private string _forecastedRevenue = string.Empty;

    [ObservableProperty]
    private string _actualRevenue = string.Empty;

    [ObservableProperty]
    private string _revenueAccuracy = string.Empty;

    [ObservableProperty]
    private string _forecastedExpenses = string.Empty;

    [ObservableProperty]
    private string _actualExpenses = string.Empty;

    [ObservableProperty]
    private string _expensesAccuracy = string.Empty;

    [ObservableProperty]
    private string _confidenceScore = string.Empty;

    [ObservableProperty]
    private string _forecastMethod = string.Empty;

    [ObservableProperty]
    private bool _isBacktested;

    [ObservableProperty]
    private bool _isValidated;

    [ObservableProperty]
    private Color _accuracyColor = Color.Parse(AppColors.GrayMedium);

    [ObservableProperty]
    private Color _expensesAccuracyColor = Color.Parse(AppColors.GrayMedium);

    [ObservableProperty]
    private string _typeLabel = "Live";

    [ObservableProperty]
    private Color _typeBadgeColor = Color.Parse(AppColors.Primary);

    /// <summary>
    /// Creates a view model from a ForecastAccuracyRecord.
    /// </summary>
    public static PastPredictionItemViewModel FromRecord(ForecastAccuracyRecord record)
    {
        var isBacktested = record.ForecastDate.Date == record.PeriodStartDate.Date;

        var revenueAccuracy = record.RevenueAccuracyPercent;
        var expensesAccuracy = record.ExpensesAccuracyPercent;

        // Determine color based on accuracy
        var accuracyColor = revenueAccuracy switch
        {
            >= 90 => AppColors.Success,
            >= 80 => AppColors.Primary,
            >= 70 => AppColors.Warning,
            _ => AppColors.ExpenseRed
        };

        var expAccuracyColor = expensesAccuracy switch
        {
            >= 90 => AppColors.Success,
            >= 80 => AppColors.Primary,
            >= 70 => AppColors.Warning,
            _ => AppColors.ExpenseRed
        };

        return new PastPredictionItemViewModel
        {
            PeriodLabel = $"{record.PeriodStartDate:MMM yyyy}",
            // Stored in USD, so shown as the forecast cards are: in the display currency at today's rate.
            ForecastedRevenue = InsightsPageViewModel.FormatForecastAmount(record.ForecastedRevenue),
            ActualRevenue = record.ActualRevenue.HasValue ? InsightsPageViewModel.FormatForecastAmount(record.ActualRevenue.Value) : "—",
            RevenueAccuracy = revenueAccuracy.HasValue ? $"{revenueAccuracy.Value:F0}%" : "—",
            ForecastedExpenses = InsightsPageViewModel.FormatForecastAmount(record.ForecastedExpenses),
            ActualExpenses = record.ActualExpenses.HasValue ? InsightsPageViewModel.FormatForecastAmount(record.ActualExpenses.Value) : "—",
            ExpensesAccuracy = expensesAccuracy.HasValue ? $"{expensesAccuracy.Value:F0}%" : "—",
            ConfidenceScore = $"{record.ConfidenceScore:F0}%",
            ForecastMethod = record.ForecastMethod,
            IsBacktested = isBacktested,
            IsValidated = record.IsValidated,
            AccuracyColor = Color.Parse(revenueAccuracy.HasValue ? accuracyColor : AppColors.GrayMedium),
            ExpensesAccuracyColor = Color.Parse(expensesAccuracy.HasValue ? expAccuracyColor : AppColors.GrayMedium),
            TypeLabel = isBacktested ? "Backtest" : "Live",
            TypeBadgeColor = Color.Parse(isBacktested ? AppColors.Violet : AppColors.Primary)
        };
    }
}

/// <summary>
/// ViewModel for the Past Predictions modal that shows historical forecast accuracy.
/// </summary>
public partial class PastPredictionsModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _overallAccuracy = "—";

    [ObservableProperty]
    private string _accuracyTrend = "Stable";

    [ObservableProperty]
    private string _accuracyTrendIcon = "→";

    [ObservableProperty]
    private Color _accuracyTrendColor = Color.Parse(AppColors.GrayMedium);

    [ObservableProperty]
    private string _validatedCount = "0";

    [ObservableProperty]
    private string _backtestCount = "0";

    [ObservableProperty]
    private bool _hasPredictions;

    [ObservableProperty]
    private string _noPredictionsMessage = "No past predictions yet. Predictions will appear here after forecast periods have completed.";

    [ObservableProperty]
    private bool _hasChartData;

    /// <summary>
    /// Gets the chart title visual using the shared ChartLoaderService styling.
    /// </summary>
    public LabelVisual AccuracyChartTitle => ChartLoaderService.CreateChartTitle("Accuracy Over Time");

    [ObservableProperty]
    private ObservableCollection<ISeries> _accuracyChartSeries = [];

    [ObservableProperty]
    private Axis[] _accuracyChartXAxes = [new Axis()];

    [ObservableProperty]
    private Axis[] _accuracyChartYAxes = [new Axis()];

    // Chart context menu properties
    [ObservableProperty]
    private bool _isChartContextMenuOpen;

    [ObservableProperty]
    private double _chartContextMenuX;

    [ObservableProperty]
    private double _chartContextMenuY;

    /// <summary>
    /// Reference to the chart for reset zoom functionality.
    /// </summary>
    public CartesianChart? AccuracyChart { get; set; }

    /// <summary>
    /// Collection of past predictions for display.
    /// </summary>
    public ObservableCollection<PastPredictionItemViewModel> Predictions { get; } = [];

    /// <summary>
    /// Opens the modal and loads data.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        LoadPredictions();
        IsOpen = true;
    }

    /// <summary>
    /// Closes the modal.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Closes the chart context menu.
    /// </summary>
    [RelayCommand]
    private void CloseChartContextMenu()
    {
        IsChartContextMenuOpen = false;
    }

    /// <summary>
    /// Event raised when save chart image is requested. View handles the actual save.
    /// </summary>
    public event EventHandler? SaveChartImageRequested;

    /// <summary>
    /// Requests saving the chart as an image.
    /// </summary>
    [RelayCommand]
    private void SaveChartAsImage()
    {
        IsChartContextMenuOpen = false;
        SaveChartImageRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resets the chart zoom to default view.
    /// </summary>
    [RelayCommand]
    private void ResetChartZoom()
    {
        IsChartContextMenuOpen = false;

        if (AccuracyChart?.CoreChart == null || !HasChartData)
            return;

        // Reload the chart data to reset zoom
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData != null)
        {
            var records = companyData.ForecastRecords
                .Where(r => r.IsValidated)
                .ToList();
            LoadAccuracyChart(records);
        }
    }

    /// <summary>
    /// Event raised when Google Sheets export is requested. View handles the actual export.
    /// </summary>
    public event EventHandler? GoogleSheetsExportRequested;

    /// <summary>
    /// Exports chart data to Google Sheets.
    /// </summary>
    [RelayCommand]
    private void ExportToGoogleSheets()
    {
        IsChartContextMenuOpen = false;
        GoogleSheetsExportRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Event raised when Excel export is requested. View handles the actual export.
    /// </summary>
    public event EventHandler? ExcelExportRequested;

    /// <summary>
    /// Exports chart data to Excel.
    /// </summary>
    [RelayCommand]
    private void ExportToExcel()
    {
        IsChartContextMenuOpen = false;
        ExcelExportRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the chart data for export.
    /// </summary>
    public (string[] Labels, double[] RevenueAccuracy, double[] ExpensesAccuracy) GetExportData()
    {
        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null)
            return ([], [], []);

        var records = companyData.ForecastRecords
            .Where(r => r.IsValidated && r.RevenueAccuracyPercent.HasValue)
            .OrderBy(r => r.PeriodStartDate)
            .ToList();

        var labels = records.Select(r => r.PeriodStartDate.ToString("MMM yyyy")).ToArray();
        var revenueAccuracy = records.Select(r => r.RevenueAccuracyPercent!.Value).ToArray();
        var expensesAccuracy = records
            .Select(r => r.ExpensesAccuracyPercent ?? 0)
            .ToArray();

        return (labels, revenueAccuracy, expensesAccuracy);
    }

    /// <summary>
    /// Loads prediction data from the company data.
    /// </summary>
    private void LoadPredictions()
    {
        Predictions.Clear();

        var companyData = App.CompanyManager?.CompanyData;
        if (companyData == null || companyData.ForecastRecords.Count == 0)
        {
            HasPredictions = false;
            return;
        }

        // Get all records, ordered by period date descending
        var records = companyData.ForecastRecords
            .OrderByDescending(r => r.PeriodStartDate)
            .ToList();

        // Calculate statistics (only include fully completed periods)
        var completedCutoff = DateTime.Today;
        var validatedRecords = records.Where(r => r.IsValidated && r.PeriodEndDate < completedCutoff).ToList();
        var backtestRecords = records.Where(r => r.ForecastDate.Date == r.PeriodStartDate.Date && r.IsValidated && r.PeriodEndDate < completedCutoff).ToList();
        var realPredictions = validatedRecords.Except(backtestRecords).ToList();

        ValidatedCount = realPredictions.Count.ToString();
        BacktestCount = backtestRecords.Count.ToString();

        // Calculate overall accuracy
        if (validatedRecords.Count > 0)
        {
            var accuracies = validatedRecords
                .Where(r => r.RevenueAccuracyPercent.HasValue)
                .Select(r => r.RevenueAccuracyPercent!.Value)
                .ToList();

            if (accuracies.Count > 0)
            {
                var avgAccuracy = accuracies.Average();
                OverallAccuracy = $"{avgAccuracy:F0}%";

                // Determine trend if we have enough data
                if (accuracies.Count >= 4)
                {
                    var half = accuracies.Count / 2;
                    var firstHalf = accuracies.Skip(half).Take(half).Average(); // Older predictions
                    var secondHalf = accuracies.Take(half).Average(); // Newer predictions

                    if (secondHalf > firstHalf + 5)
                    {
                        AccuracyTrend = "Improving";
                        AccuracyTrendIcon = "↑";
                        AccuracyTrendColor = Color.Parse(AppColors.Success);
                    }
                    else if (secondHalf < firstHalf - 5)
                    {
                        AccuracyTrend = "Declining";
                        AccuracyTrendIcon = "↓";
                        AccuracyTrendColor = Color.Parse(AppColors.ExpenseRed);
                    }
                    else
                    {
                        AccuracyTrend = "Stable";
                        AccuracyTrendIcon = "→";
                        AccuracyTrendColor = Color.Parse(AppColors.GrayMedium);
                    }
                }
            }
        }
        else
        {
            OverallAccuracy = "—";
            AccuracyTrend = "Stable";
            AccuracyTrendIcon = "→";
            AccuracyTrendColor = Color.Parse(AppColors.GrayMedium);
        }

        // Add records to the collection (only show completed periods)
        var today = DateTime.Today;
        foreach (var record in records)
        {
            if (record.IsValidated && record.PeriodEndDate < today)
            {
                Predictions.Add(PastPredictionItemViewModel.FromRecord(record));
            }
        }

        HasPredictions = Predictions.Count > 0;

        if (!HasPredictions)
        {
            NoPredictionsMessage = "No validated predictions yet. Predictions will appear here after forecast periods have completed and can be compared to actual results.";
        }

        LoadAccuracyChart(records.Where(r => r.IsValidated && r.PeriodEndDate < today).ToList());
    }

    /// <summary>
    /// Loads the accuracy over time chart data.
    /// </summary>
    private void LoadAccuracyChart(List<ForecastAccuracyRecord> validatedRecords)
    {
        // Need at least 2 data points for a meaningful chart
        if (validatedRecords.Count < 2)
        {
            HasChartData = false;
            AccuracyChartSeries = [];
            return;
        }

        // Sort by period date ascending for chronological chart
        var orderedRecords = validatedRecords
            .Where(r => r.RevenueAccuracyPercent.HasValue)
            .OrderBy(r => r.PeriodStartDate)
            .ToList();

        if (orderedRecords.Count < 2)
        {
            HasChartData = false;
            AccuracyChartSeries = [];
            return;
        }

        // Get theme colors
        var isDarkTheme = ThemeService.Instance.IsDarkTheme;
        var textColor = isDarkTheme ? SKColor.Parse(AppColors.TextDark) : SKColor.Parse(AppColors.TextLight);
        var gridColor = isDarkTheme ? SKColor.Parse(AppColors.ChartAxis) : SKColor.Parse(AppColors.ChartGrid);

        // Create data points for revenue accuracy
        var revenuePoints = orderedRecords
            .Select(r => new ObservablePoint(r.PeriodStartDate.ToOADate(), r.RevenueAccuracyPercent!.Value))
            .ToArray();

        // Create data points for expenses accuracy (if available)
        var expensePoints = orderedRecords
            .Where(r => r.ExpensesAccuracyPercent.HasValue)
            .Select(r => new ObservablePoint(r.PeriodStartDate.ToOADate(), r.ExpensesAccuracyPercent!.Value))
            .ToArray();

        var series = new ObservableCollection<ISeries>();

        // Revenue accuracy series (blue)
        var revenueColor = SKColor.Parse(AppColors.Primary);
        series.Add(new LineSeries<ObservablePoint>
        {
            Values = revenuePoints,
            Name = "Revenue Accuracy",
            Stroke = new SolidColorPaint(revenueColor, 2),
            Fill = new SolidColorPaint(revenueColor.WithAlpha(40)),
            GeometryStroke = new SolidColorPaint(revenueColor, 2),
            GeometryFill = new SolidColorPaint(revenueColor),
            GeometrySize = 8,
            LineSmoothness = 0.3
        });

        // Expenses accuracy series (purple) - only if we have data
        if (expensePoints.Length >= 2)
        {
            var expenseColor = SKColor.Parse(AppColors.Violet);
            series.Add(new LineSeries<ObservablePoint>
            {
                Values = expensePoints,
                Name = "Expenses Accuracy",
                Stroke = new SolidColorPaint(expenseColor, 2),
                Fill = new SolidColorPaint(expenseColor.WithAlpha(40)),
                GeometryStroke = new SolidColorPaint(expenseColor, 2),
                GeometryFill = new SolidColorPaint(expenseColor),
                GeometrySize = 8,
                LineSmoothness = 0.3
            });
        }

        AccuracyChartSeries = series;

        // Configure X-axis with date labels
        var dates = orderedRecords.Select(r => r.PeriodStartDate).ToArray();
        var minDate = dates.Min().ToOADate();
        var maxDate = dates.Max().ToOADate();
        var padding = Math.Max(5, (maxDate - minDate) * 0.05);

        AccuracyChartXAxes =
        [
            new Axis
            {
                TextSize = 12,
                LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = Core.Services.PlatformTypefaces.Default },
                MinLimit = minDate - padding,
                MaxLimit = maxDate + padding,
                Labeler = value =>
                {
                    if (value < -657434 || value > 2958466)
                        return string.Empty;
                    try
                    {
                        var date = DateTime.FromOADate(value);
                        return date.ToString("MMM yy");
                    }
                    catch
                    {
                        return string.Empty;
                    }
                },
                MinStep = 30 // Minimum step of ~1 month
            }
        ];

        // Configure Y-axis for percentage (0-100%)
        AccuracyChartYAxes =
        [
            new Axis
            {
                TextSize = 12,
                LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = Core.Services.PlatformTypefaces.Default },
                SeparatorsPaint = new SolidColorPaint(gridColor) { StrokeThickness = 1 },
                MinLimit = 0,
                MaxLimit = 105, // Slightly above 100 for visual padding
                Labeler = value => $"{value:F0}%"
            }
        ];

        HasChartData = true;
    }
}
