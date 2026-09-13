using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.ViewModels.Dashboard;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Dashboard page.
/// Provides an overview of key business metrics, recent transactions, and quick actions.
/// Widget data loading is delegated to <see cref="DashboardLayoutViewModel"/>.
/// </summary>
public partial class DashboardPageViewModel : ChartContextMenuViewModelBase, ICleanupViewModel
{
    [ObservableProperty]
    private bool _hasPremium;

    #region Date Range

    /// <summary>
    /// Gets the shared chart settings service.
    /// </summary>
    private ChartSettingsService ChartSettings => ChartSettingsService.Instance;

    // Flag to prevent duplicate data loading when changing settings locally
    private bool _isLocalSettingChange;

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
                        LoadDashboardData();
                    }
                }
                finally
                {
                    _isLocalSettingChange = false;
                }
            }
        }
    }

    // Stores the previous selection before opening custom range modal
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
    /// Gets or sets the start date as DateTimeOffset for DatePicker binding.
    /// </summary>
    public DateTimeOffset? StartDateOffset
    {
        get => new DateTimeOffset(StartDate);
        set
        {
            if (value.HasValue)
            {
                StartDate = value.Value.DateTime;
                LoadDashboardData();
            }
        }
    }

    /// <summary>
    /// Gets or sets the end date as DateTimeOffset for DatePicker binding.
    /// </summary>
    public DateTimeOffset? EndDateOffset
    {
        get => new DateTimeOffset(EndDate);
        set
        {
            if (value.HasValue)
            {
                EndDate = value.Value.DateTime;
                LoadDashboardData();
            }
        }
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
    private void OpenCustomDateRangeModal()
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
                // Inclusive end-of-day so transactions stored later in the
                // user's day (or with a UTC timestamp ahead of local time)
                // aren't filtered out of stat-card aggregations.
                EndDate = end.Date.AddDays(1).AddTicks(-1);
                HasAppliedCustomRange = true;
                OnPropertyChanged(nameof(AppliedDateRangeText));
                OnPropertyChanged(nameof(DateRangeDisplayText));
                LoadDashboardData();
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

    #endregion

    #region Chart Type

    /// <summary>
    /// Available chart type options for the selector.
    /// </summary>
    public string[] ChartTypeOptions { get; } = ["Area", "Column", "Line", "Step Line", "Scatter"];

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
                    LoadDashboardData();
                }
                finally
                {
                    _isLocalSettingChange = false;
                }
            }
        }
    }

    #endregion

    #region Welcome Subtitle

    /// <summary>
    /// Gets the welcome subtitle text, which changes based on whether this is a sample company.
    /// </summary>
    public string WelcomeSubtitle => _companyManager?.IsSampleCompany == true
        ? "You're exploring TechFlow Solutions - a sample company. Feel free to experiment!".Translate()
        : "Welcome back! Here is an overview of your business.".Translate();

    /// <summary>
    /// Whether the current company is a sample company (dashboard changes won't persist).
    /// </summary>
    public bool IsSampleCompany => _companyManager?.IsSampleCompany == true;

    /// <summary>
    /// Re-reads the two properties above. Both are computed live but only a notification makes
    /// a binding look again, and Save As turns the sample company into the user's own without
    /// closing anything or navigating, so the page on screen would otherwise keep warning about
    /// a sample company that is no longer open.
    /// </summary>
    public void RefreshSampleCompanyState()
    {
        OnPropertyChanged(nameof(WelcomeSubtitle));
        OnPropertyChanged(nameof(IsSampleCompany));
    }

    #endregion

    #region Empty State Date Range Detection

    /// <summary>
    /// True when financial data (revenues or expenses) exists in all time but the current
    /// date range filter is excluding it. Used to show "no data in selected date range" messages.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDateRangeBanner))]
    private bool _showFinancialDateRangeMessage;

    /// <summary>
    /// Whether to actually show the "no data in the selected date range" banner: only when there
    /// genuinely is no data in range AND the dashboard isn't being edited (the banner is noise and
    /// gets in the way while arranging widgets).
    /// </summary>
    public bool ShowDateRangeBanner => ShowFinancialDateRangeMessage && !LayoutViewModel.IsEditMode;

    #endregion

    #region Company Data Reference

    private CompanyManager? _companyManager;

    #endregion

    #region Widget Layout

    /// <summary>
    /// Gets the dashboard layout view model that manages all widgets.
    /// </summary>
    public DashboardLayoutViewModel LayoutViewModel { get; } = new();

    #endregion

    #region Constructor

    public DashboardPageViewModel()
    {
        // Wire up checklist navigation for SetupChecklist widgets
        // (handled when widgets fire NavigationRequested)

        // Initialize the shared chart settings service
        ChartSettings.Initialize();

        // Subscribe to chart settings changes from other pages
        ChartSettings.ChartTypeChanged += OnChartSettingsChartTypeChanged;
        ChartSettings.DateRangeChanged += OnChartSettingsDateRangeChanged;

        // Subscribe to theme changes to reload charts with new colors
        ThemeService.Instance.ThemeChanged += OnThemeChanged;

        // Subscribe to date format changes to refresh charts and transaction dates
        DateFormatService.DateFormatChanged += OnDateFormatChanged;

        // Subscribe to currency changes to refresh all monetary displays
        CurrencyService.CurrencyChanged += OnCurrencyChanged;

        // Hide the date-range banner while editing the dashboard (re-evaluate when edit mode toggles).
        LayoutViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardLayoutViewModel.IsEditMode))
                OnPropertyChanged(nameof(ShowDateRangeBanner));
        };

        // Re-evaluate the source-survey banner whenever the user answers,
        // dismisses, or the tutorial state changes.
        TutorialService.Instance.SourceSurveyVisibilityChanged += OnSourceSurveyVisibilityChanged;
        TutorialService.Instance.TutorialStateChanged += OnTutorialStateChanged;
        RefreshSourceSurveyBanner();
    }

    #region Source Survey Banner

    [ObservableProperty]
    private bool _showSourceSurveyBanner;

    public void RefreshSourceSurveyBanner()
    {
        var tutorial = TutorialService.Instance;
        // Someone working through the checklist is asked when they finish it; this banner is
        // for everyone the checklist will never reach.
        ShowSourceSurveyBanner =
            (tutorial.HasSkippedTutorial || tutorial.IsSetupChecklistDismissed) &&
            tutorial.ShouldShowSourceSurvey();
    }

    private void OnSourceSurveyVisibilityChanged(object? sender, bool shown)
    {
        // After the overlay opens/closes, recompute banner visibility.
        // Once the user answers or dismisses, ShouldShowSourceSurvey() returns false.
        RefreshSourceSurveyBanner();
    }

    // Named handler (not a lambda) so Cleanup can unsubscribe it; an anonymous delegate would leave
    // this VM rooted on TutorialService for the rest of the app's lifetime.
    private void OnTutorialStateChanged(object? sender, EventArgs e) => RefreshSourceSurveyBanner();

    [RelayCommand]
    private void OpenSourceSurvey()
    {
        TutorialService.Instance.RequestShowSourceSurvey();
    }

    [RelayCommand]
    private void DismissSourceSurveyBanner()
    {
        TutorialService.Instance.MarkSourceSurveyDismissed();
        ShowSourceSurveyBanner = false;
    }

    #endregion

    private void OnThemeChanged(object? sender, ThemeMode e)
    {
        LoadDashboardData();
    }

    private void OnDateFormatChanged(object? sender, EventArgs e)
    {
        LoadDashboardData();
    }

    private void OnCurrencyChanged(object? sender, EventArgs e)
    {
        LoadDashboardData();
    }

    private void OnChartSettingsChartTypeChanged(object? sender, string chartType)
    {
        // Only reload if the change came from another page
        if (!_isLocalSettingChange)
        {
            OnPropertyChanged(nameof(SelectedChartType));
            LoadDashboardData();
        }
    }

    private void OnChartSettingsDateRangeChanged(object? sender, string dateRange)
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
            LoadDashboardData();
        }
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Initializes the ViewModel with the company manager.
    /// </summary>
    public void Initialize(CompanyManager companyManager)
    {
        _companyManager = companyManager;

        // Initialize the widget layout system
        LayoutViewModel.Initialize(companyManager);

        // Load data through the full flow (includes CorrectRentalStatuses)
        LoadDashboardData();

        // Mark dashboard as explored when initialized
        TutorialService.Instance.CompleteChecklistItem(TutorialService.ChecklistItems.ExploreDashboard);

        // Notify properties that depend on company manager
        RefreshSampleCompanyState();

        // Subscribe to data change events
        _companyManager.CompanyDataChanged += OnCompanyDataChanged;

        // Subscribe to language changes to refresh translated chart titles
        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Cleans up event subscriptions.
    /// </summary>
    public void Cleanup()
    {
        if (_companyManager != null)
        {
            _companyManager.CompanyDataChanged -= OnCompanyDataChanged;
        }

        // Unsubscribe from chart settings events
        ChartSettings.ChartTypeChanged -= OnChartSettingsChartTypeChanged;
        ChartSettings.DateRangeChanged -= OnChartSettingsDateRangeChanged;

        // Unsubscribe from theme, date format, and currency changes
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
        DateFormatService.DateFormatChanged -= OnDateFormatChanged;
        CurrencyService.CurrencyChanged -= OnCurrencyChanged;

        // Unsubscribe from language changes
        LanguageService.Instance.LanguageChanged -= OnLanguageChanged;

        // Unsubscribe from tutorial / source-survey events
        TutorialService.Instance.SourceSurveyVisibilityChanged -= OnSourceSurveyVisibilityChanged;
        TutorialService.Instance.TutorialStateChanged -= OnTutorialStateChanged;

        // Cleanup widget layout
        LayoutViewModel.Cleanup();
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        // Refresh all widget data for translation changes
        LoadDashboardData();

        // Refresh welcome subtitle for translation
        OnPropertyChanged(nameof(WelcomeSubtitle));

        // Force ComboBox to re-render items with new translations
        // by refreshing the collection (items don't change, but triggers re-render)
        var currentSelection = SelectedDateRange;
        DateRangeOptions.Clear();
        foreach (var option in DatePresetNames.StandardDateRangeOptions)
        {
            DateRangeOptions.Add(option);
        }
        // Restore selection without triggering data reload
        _isLocalSettingChange = true;
        try
        {
            ChartSettings.SelectedDateRange = currentSelection;
            OnPropertyChanged(nameof(SelectedDateRange));
        }
        finally
        {
            _isLocalSettingChange = false;
        }
    }

    private void OnCompanyDataChanged(object? sender, EventArgs e)
    {
        // When "All Time" is selected, recalculate the date range to include any new data
        if (SelectedDateRange == DateRangePreset.AllTime.GetDisplayName())
        {
            ChartSettings.UpdateDateRangeFromSelection();
        }

        LoadDashboardData();
    }

    /// <summary>
    /// Loads all dashboard data from the company data.
    /// </summary>
    public void LoadDashboardData()
    {
        var data = _companyManager?.CompanyData;
        if (data == null) return;

        // Correct rental statuses before displaying
        CorrectRentalStatuses(data);

        // Show date range message only when data exists but the current range has no matching records
        var isFiltered = SelectedDateRange != DateRangePreset.AllTime.GetDisplayName();
        var hasAnyData = data.Revenues.Count > 0 || data.Expenses.Count > 0;
        var hasDataInRange = hasAnyData && (
            data.Revenues.Any(s => s.Date >= StartDate && s.Date <= EndDate) ||
            data.Expenses.Any(p => p.Date >= StartDate && p.Date <= EndDate));
        ShowFinancialDateRangeMessage = isFiltered && hasAnyData && !hasDataInRange;

        // Delegate all widget data loading to the layout VM
        LayoutViewModel.LoadAllWidgetData();
    }

    /// <summary>
    /// Corrects rental statuses based on due dates.
    /// </summary>
    private static void CorrectRentalStatuses(CompanyData data)
    {
        // Mark active rentals as overdue if past due
        foreach (var rental in data.Rentals.Where(r => r.Status == RentalStatus.Active))
        {
            if (rental.IsOverdue)
            {
                rental.Status = RentalStatus.Overdue;
            }
        }

        // Reset incorrectly marked overdue rentals back to active if due date is in the future
        foreach (var rental in data.Rentals.Where(r => r.Status == RentalStatus.Overdue))
        {
            if (DateTime.Today <= rental.DueDate.Date)
            {
                rental.Status = RentalStatus.Active;
            }
        }
    }

    #endregion

    #region Chart Context Menu Overrides

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
        var loader = GetChartLoaderForType(SelectedChartDataType);
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
            ArgoBooks.Core.Services.GoogleSheetsService.ChartType chartType;
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
                var browserOpened = GoogleSheetsService.OpenInBrowser(url);

                GoogleSheetsExportStatusChanged?.Invoke(this, new GoogleSheetsExportEventArgs
                {
                    IsSuccess = true,
                    SpreadsheetUrl = url
                });

                if (!browserOpened)
                {
                    var dialog = App.ConfirmationDialog;
                    if (dialog != null)
                    {
                        await dialog.ShowAsync(new ConfirmationDialogOptions
                        {
                            Title = "Browser Error".Translate(),
                            Message = "The spreadsheet was created but could not open in your browser. You can access it at:\n\n{0}".TranslateFormat(url),
                            PrimaryButtonText = "OK".Translate(),
                            SecondaryButtonText = null,
                            CancelButtonText = null
                        });
                    }
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

    /// <summary>
    /// Event raised when exporting to Excel is requested.
    /// The View should subscribe to this and handle the file save dialog.
    /// </summary>
    public event EventHandler<ExcelExportEventArgs>? ExcelExportRequested;

    /// <inheritdoc />
    protected override void OnExportToExcel()
    {
        var loader = GetChartLoaderForType(SelectedChartDataType);
        var exportData = loader.GetExportDataForChart(SelectedChartDataType);
        if (exportData == null || exportData.Labels.Length == 0)
        {
            // No data to export
            return;
        }

        var chartTitle = SelectedChartDataType?.GetDisplayName() ?? exportData.ChartTitle;

        // Raise event for View to handle file save dialog
        ExcelExportRequested?.Invoke(this, new ExcelExportEventArgs
        {
            ChartTitle = chartTitle,
            Labels = exportData.Labels,
            Values = exportData.Values,
            SeriesName = exportData.SeriesName,
            ChartType = exportData.ChartType,
            ChartStyle = loader.SelectedChartStyle,
            AdditionalSeries = exportData.AdditionalSeries
        });
    }

    /// <inheritdoc />
    protected override void OnResetChartZoom()
    {
        if (SelectedChartDataType is not { } chartType) return;

        foreach (var row in LayoutViewModel.Rows)
        {
            foreach (var widget in row.Widgets)
            {
                if (widget.WidgetViewModel is UnifiedChartWidgetViewModel chartVm
                    && chartVm.ChartDataType == chartType)
                {
                    ChartLoaderService.ResetZoom(chartVm.XAxes, chartVm.YAxes);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Gets the chart loader service. Falls back to a shared instance for non-widget operations.
    /// </summary>
    public ChartLoaderService ChartLoaderService { get; } = new();

    /// <summary>
    /// Finds the ChartLoaderService for the widget matching the given chart type,
    /// falling back to the dashboard-level instance.
    /// </summary>
    private ChartLoaderService GetChartLoaderForType(ChartDataType? chartDataType)
    {
        if (chartDataType is not { } type) return ChartLoaderService;

        foreach (var row in LayoutViewModel.Rows)
        {
            foreach (var widget in row.Widgets)
            {
                if (widget.WidgetViewModel is UnifiedChartWidgetViewModel chartVm
                    && chartVm.ChartDataType == type)
                {
                    return chartVm.ChartLoaderService;
                }
            }
        }

        return ChartLoaderService;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Checks if there is sufficient data coverage for the prior comparison period.
    /// Returns false if the earliest transaction date is after the prior period start,
    /// which would make the comparison misleading.
    /// </summary>
    internal static bool HasSufficientPriorData(CompanyData data, DateTime prevStartDate)
    {
        var earliestRevenue = data.Revenues.Count > 0 ? data.Revenues.Min(r => r.Date) : DateTime.MaxValue;
        var earliestExpense = data.Expenses.Count > 0 ? data.Expenses.Min(e => e.Date) : DateTime.MaxValue;
        var earliestDate = earliestRevenue < earliestExpense ? earliestRevenue : earliestExpense;

        // If no data at all, no meaningful comparison
        if (earliestDate == DateTime.MaxValue)
            return false;

        return earliestDate <= prevStartDate;
    }

    internal static double? CalculatePercentageChange(decimal previous, decimal current)
    {
        // Return null when there's no previous period data to compare against
        if (previous == 0)
        {
            return null;
        }
        return (double)((current - previous) / previous * 100);
    }

    internal static string? FormatPercentageChange(double? change)
    {
        if (!change.HasValue)
        {
            return null;
        }
        // Use absolute value since the arrow indicates direction
        return $"{Math.Abs(change.Value):F1}%";
    }

    #endregion
}

#region View Models for Lists

/// <summary>
/// Represents a recent transaction item for display.
/// </summary>
public class RecentTransactionItem
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public decimal AmountValue { get; set; }
    public DateTime Date { get; set; }
    public string DateFormatted { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusVariant { get; set; } = "neutral";
    public bool IsIncome { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    // Helper properties for status variant styling
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public bool IsStatusSuccess => StatusVariant == "success";
    public bool IsStatusWarning => StatusVariant == "warning";
    public bool IsStatusError => StatusVariant == "error";
    public bool IsStatusInfo => StatusVariant == "info";
    public bool IsStatusNeutral => StatusVariant == "neutral" || string.IsNullOrEmpty(StatusVariant);
}

/// <summary>
/// Represents an active rental item for display.
/// </summary>
public class ActiveRentalItem
{
    public string Id { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public string StartDateFormatted { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string DueDateFormatted { get; set; } = string.Empty;
    public string RateAmount { get; set; } = string.Empty;
    public string RateType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusVariant { get; set; } = "success";
    public int DaysRemaining { get; set; }
    public bool IsOverdue { get; set; }

    public string DaysRemainingText => IsOverdue
        ? $"{Math.Abs(DaysRemaining)} days overdue"
        : DaysRemaining == 0
            ? "Due today"
            : $"{DaysRemaining} days left";
}

/// <summary>
/// Event arguments for Google Sheets export status changes.
/// </summary>
public class GoogleSheetsExportEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets whether an export is currently in progress.
    /// </summary>
    public bool IsExporting { get; set; }

    /// <summary>
    /// Gets or sets whether the export completed successfully.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Gets or sets the URL of the created spreadsheet.
    /// </summary>
    public string? SpreadsheetUrl { get; set; }

    /// <summary>
    /// Gets or sets the error message if the export failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the cancellation token source for cancelling the export.
    /// </summary>
    public CancellationTokenSource? CancellationTokenSource { get; set; }
}

/// <summary>
/// Event arguments for saving a chart as an image.
/// </summary>
public class SaveChartImageEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the identifier of the chart to save.
    /// </summary>
    public string ChartId { get; set; } = string.Empty;
}

/// <summary>
/// Event arguments for Excel export requests.
/// </summary>
public class ExcelExportEventArgs : EventArgs
{
    public string ChartTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the labels (categories/dates) for the chart data.
    /// </summary>
    public string[] Labels { get; set; } = [];

    /// <summary>
    /// Gets or sets the primary series values.
    /// </summary>
    public double[] Values { get; set; } = [];

    /// <summary>
    /// Gets or sets the name of the primary series.
    /// </summary>
    public string SeriesName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the chart type for export formatting.
    /// </summary>
    public ChartType ChartType { get; set; }

    /// <summary>
    /// Gets or sets additional series for multi-series charts.
    /// </summary>
    public List<(string Name, double[] Values)> AdditionalSeries { get; set; } = [];

    /// <summary>
    /// Gets or sets the visual chart style (Line, Column, Area, etc.).
    /// </summary>
    public ChartStyle ChartStyle { get; set; } = ChartStyle.Line;

    /// <summary>
    /// Returns true if this is a multi-series chart.
    /// </summary>
    public bool IsMultiSeries => AdditionalSeries.Count > 0;

    /// <summary>
    /// Returns true if this is a distribution/pie chart.
    /// </summary>
    public bool IsDistribution => ChartType == ChartType.Distribution;

    /// <summary>
    /// Returns true if this is a region map chart (geographic heat map).
    /// </summary>
    public bool IsRegionMap => RegionMapData.Count > 0;

    /// <summary>
    /// Gets or sets the region map data (country display name to value).
    /// </summary>
    public Dictionary<string, double> RegionMapData { get; set; } = [];
}

/// <summary>
/// Navigation parameter for navigating to a specific transaction.
/// </summary>
public class TransactionNavigationParameter(string transactionId)
{
    /// <summary>
    /// Gets the transaction ID to highlight.
    /// </summary>
    public string TransactionId { get; } = transactionId;
}

#endregion
