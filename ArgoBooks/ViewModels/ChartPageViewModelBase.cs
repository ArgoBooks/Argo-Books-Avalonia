using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;

namespace ArgoBooks.ViewModels;

/// <summary>
/// The date range, chart type and chart export the Dashboard and Analytics pages share. Both read
/// and write the one ChartSettingsService, so a change made on one page shows on the other.
/// </summary>
public abstract partial class ChartPageViewModelBase : ChartContextMenuViewModelBase
{
    protected CompanyManager? _companyManager;

    /// <summary>Reloads the page after a chart setting changes; a style-only change need not reload the data.</summary>
    protected abstract void ReloadForChartSettings(bool styleChangeOnly);

    /// <summary>The loader holding the given chart's data.</summary>
    protected virtual ChartLoaderService ChartLoaderFor(ChartDataType? chartDataType) => ChartLoaderService;

    /// <summary>
    /// Gets the shared chart settings service.
    /// </summary>
    protected ChartSettingsService ChartSettings => ChartSettingsService.Instance;

    protected bool _isLocalSettingChange;

    public ChartLoaderService ChartLoaderService { get; } = new();
    /// <summary>
    /// Available date range options.
    /// </summary>
    public ObservableCollection<string> DateRangeOptions { get; } = new(DatePresetNames.StandardDateRangeOptions);

    /// <summary>
    /// Gets or sets the selected date range (delegates to shared service).
    /// </summary>
    public string SelectedDateRange
    {
        get => ChartSettings.SelectedDateRange;
        set
        {
            if (ChartSettings.SelectedDateRange != value)
            {
                var oldValue = ChartSettings.SelectedDateRange;
                _isLocalSettingChange = true;
                try
                {
                    ChartSettings.SelectedDateRange = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsCustomDateRange));
                    OnPropertyChanged(nameof(ComparisonPeriodLabel));
                    OnPropertyChanged(nameof(DateRangeDisplayText));

                    if (value == DateRangePreset.CustomRange.GetDisplayName())
                    {
                        OpenCustomDateRangeModal();
                    }
                    else if (oldValue != value)
                    {
                        // Explicitly notify even if value unchanged (service may have set it first)
                        ChartSettings.HasAppliedCustomRange = false;
                        OnPropertyChanged(nameof(HasAppliedCustomRange));
                        OnPropertyChanged(nameof(AppliedDateRangeText));
                        ReloadForChartSettings(styleChangeOnly: false);
                    }
                }
                finally
                {
                    _isLocalSettingChange = false;
                }
            }
        }
    }

    private string _previousDateRange = DateRangePreset.ThisMonth.GetDisplayName();

    /// <summary>
    /// Gets or sets the start date (delegates to shared service).
    /// </summary>
    public DateTime StartDate
    {
        get => ChartSettings.StartDate;
        set
        {
            if (ChartSettings.StartDate != value)
            {
                ChartSettings.StartDate = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the end date (delegates to shared service).
    /// </summary>
    public DateTime EndDate
    {
        get => ChartSettings.EndDate;
        set
        {
            if (ChartSettings.EndDate != value)
            {
                ChartSettings.EndDate = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether a custom date range has been applied (delegates to shared service).
    /// </summary>
    public bool HasAppliedCustomRange
    {
        get => ChartSettings.HasAppliedCustomRange;
        set
        {
            if (ChartSettings.HasAppliedCustomRange != value)
            {
                ChartSettings.HasAppliedCustomRange = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AppliedDateRangeText));
            }
        }
    }

    /// <summary>
    /// Gets the formatted text showing the applied custom date range.
    /// </summary>
    public string AppliedDateRangeText => ChartSettings.AppliedDateRangeText;

    /// <summary>
    /// Gets the formatted text showing the currently selected date range span.
    /// </summary>
    public string DateRangeDisplayText => ChartSettings.DateRangeDisplayText;

    /// <summary>
    /// Works out the selected preset's dates again before a reload, since they go stale past midnight
    /// and All Time starts at the earliest transaction, which moves when transactions are added.
    /// </summary>
    protected void RefreshPresetDates()
    {
        ChartSettings.UpdateDateRangeFromSelection();
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(EndDate));
        OnPropertyChanged(nameof(DateRangeDisplayText));
    }

    /// <summary>
    /// Gets whether the custom date range option is selected.
    /// </summary>
    public bool IsCustomDateRange => SelectedDateRange == DateRangePreset.CustomRange.GetDisplayName();

    /// <summary>
    /// Gets the label for comparison period based on selected date range (delegates to shared service).
    /// </summary>
    public string ComparisonPeriodLabel => ChartSettings.ComparisonPeriodLabel;

    /// <summary>
    /// Opens the custom date range modal.
    /// </summary>
    protected void OpenCustomDateRangeModal()
    {
        // Store the previous selection before opening the modal
        // so we can restore it if the user cancels
        if (SelectedDateRange != DateRangePreset.CustomRange.GetDisplayName())
        {
            _previousDateRange = SelectedDateRange;
        }

        var earliestDate = _companyManager?.CompanyData?.GetEarliestDate() ?? StartDate;
        var modalStartDate = HasAppliedCustomRange ? StartDate : earliestDate;

        App.CustomDateRangeModal?.Open(modalStartDate, EndDate,
            onApply: (start, end) =>
            {
                StartDate = start.Date;
                // Inclusive end-of-day so transactions stored later in the user's day (or with a UTC timestamp ahead of local time) aren't filtered out of stat-card aggregations.
                EndDate = end.Date.AddDays(1).AddTicks(-1);
                HasAppliedCustomRange = true;
                OnPropertyChanged(nameof(AppliedDateRangeText));
                OnPropertyChanged(nameof(DateRangeDisplayText));
                ReloadForChartSettings(styleChangeOnly: false);
            },
            onCancel: () =>
            {
                // If no custom range was previously applied, revert to the previous selection
                if (!HasAppliedCustomRange)
                {
                    ChartSettings.SelectedDateRange = _previousDateRange;
                    OnPropertyChanged(nameof(SelectedDateRange));
                    OnPropertyChanged(nameof(DateRangeDisplayText));
                }
            });
    }

    /// <summary>
    /// Available chart type options for the selector.
    /// </summary>
    public string[] ChartTypeOptions => ChartSettings.ChartTypeOptions;

    /// <summary>
    /// Gets or sets the selected chart type (delegates to shared service).
    /// </summary>
    public string SelectedChartType
    {
        get => ChartSettings.SelectedChartType;
        set
        {
            if (ChartSettings.SelectedChartType != value)
            {
                _isLocalSettingChange = true;
                try
                {
                    ChartSettings.SelectedChartType = value;
                    OnPropertyChanged();
                    ReloadForChartSettings(styleChangeOnly: true);
                }
                finally
                {
                    _isLocalSettingChange = false;
                }
            }
        }
    }

    protected void OnChartSettingsChartTypeChanged(object? sender, string chartType)
    {
        // Only reload if the change came from another page
        if (!_isLocalSettingChange)
        {
            OnPropertyChanged(nameof(SelectedChartType));
            ReloadForChartSettings(styleChangeOnly: false);
        }
    }

    protected void OnChartSettingsDateRangeChanged(object? sender, string dateRange)
    {
        // Only reload if the change came from another page
        if (!_isLocalSettingChange)
        {
            OnPropertyChanged(nameof(SelectedDateRange));
            OnPropertyChanged(nameof(StartDate));
            OnPropertyChanged(nameof(EndDate));
            OnPropertyChanged(nameof(HasAppliedCustomRange));
            OnPropertyChanged(nameof(AppliedDateRangeText));
            OnPropertyChanged(nameof(DateRangeDisplayText));
            OnPropertyChanged(nameof(IsCustomDateRange));
            OnPropertyChanged(nameof(ComparisonPeriodLabel));
            ReloadForChartSettings(styleChangeOnly: false);
        }
    }

    /// <summary>
    /// Event raised when a chart image should be saved.
    /// The View should subscribe to this and handle the actual save dialog.
    /// </summary>
    public event EventHandler<SaveChartImageEventArgs>? SaveChartImageRequested;

    /// <inheritdoc />
    protected override void OnSaveChartAsImage()
    {
        SaveChartImageRequested?.Invoke(this, new SaveChartImageEventArgs { ChartId = SelectedChartDataType?.GetDisplayName() ?? "" });
    }

    /// <summary>
    /// Event raised when exporting to Google Sheets starts or completes.
    /// </summary>
    public event EventHandler<GoogleSheetsExportEventArgs>? GoogleSheetsExportStatusChanged;

    /// <inheritdoc />
    protected override void OnExportToGoogleSheets()
    {
        // Fire and forget the async operation
        _ = ExportToGoogleSheetsAsync();
    }

    /// <summary>
    /// Exports the chart data to Google Sheets asynchronously.
    /// </summary>
    private async Task ExportToGoogleSheetsAsync()
    {
        var loader = ChartLoaderFor(SelectedChartDataType);
        var exportData = loader.GetGoogleSheetsExportData(SelectedChartDataType);
        if (exportData.Count == 0)
        {
            GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
            {
                IsSuccess = false,
                ErrorMessage = "No chart data to export."
            });
            return;
        }

        // Notify that export is starting (with cancellation support)
        var cts = new CancellationTokenSource();
        GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
        {
            IsExporting = true,
            CancellationTokenSource = cts
        });

        try
        {
            // Ensure Google is authorized (auto-initiates OAuth if needed)
            var isAuthenticated = await GoogleCredentialsManager.EnsureAuthenticatedAsync(cts.Token);
            if (!isAuthenticated)
            {
                GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
                {
                    IsSuccess = false,
                    ErrorMessage = "Google Sheets authorization was not completed. Please try again."
                });
                return;
            }

            var companyName = App.CompanyManager?.CurrentCompanyName ?? "Argo Books";
            var chartExportData = loader.GetExportDataForChart(SelectedChartDataType);
            var chartTitle = SelectedChartDataType?.GetDisplayName() ?? chartExportData?.ChartTitle ?? "Chart";

            // Use Pie chart type for distribution charts, match chart style for time-based charts
            GoogleSheetsService.ChartType chartType;
            if (chartExportData?.ChartType == ChartType.Distribution)
            {
                chartType = GoogleSheetsService.ChartType.Pie;
            }
            else
            {
                chartType = loader.SelectedChartStyle switch
                {
                    ChartStyle.Line => GoogleSheetsService.ChartType.Line,
                    ChartStyle.Area => GoogleSheetsService.ChartType.Area,
                    ChartStyle.StepLine => GoogleSheetsService.ChartType.StepLine,
                    ChartStyle.Scatter => GoogleSheetsService.ChartType.Scatter,
                    _ => GoogleSheetsService.ChartType.Column
                };
            }

            cts.Token.ThrowIfCancellationRequested();

            var googleSheetsService = new GoogleSheetsService(App.ErrorLogger);
            var url = await googleSheetsService.ExportFormattedDataToGoogleSheetsAsync(
                exportData,
                chartTitle,
                chartType,
                companyName,
                cts.Token
            );

            if (!string.IsNullOrEmpty(url))
            {
                // Open the spreadsheet in the browser
                var browserOpened = Core.Platform.UrlHelper.SafeOpenUrl(url);

                GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
                {
                    IsSuccess = true,
                    SpreadsheetUrl = url
                });

                if (!browserOpened)
                {
                    await App.ShowWarningDialogAsync(
                        "Browser Error".Translate(),
                        "The spreadsheet was created but could not open in your browser. You can access it at:\n\n{0}".TranslateFormat(url));
                }
            }
            else
            {
                GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
                {
                    IsSuccess = false,
                    ErrorMessage = "Failed to create spreadsheet."
                });
            }
        }
        catch (OperationCanceledException)
        {
            GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
            {
                IsSuccess = false,
                ErrorMessage = null // Cancelled by user, no error message needed
            });
        }
        catch (InvalidOperationException ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Google Sheets export failed - invalid operation");
            GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            });
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Api, "Google Sheets export failed");
            GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
            {
                IsSuccess = false,
                ErrorMessage = $"Failed to export to Google Sheets: {ex.Message}"
            });
        }
    }
}