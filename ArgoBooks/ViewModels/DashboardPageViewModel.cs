using System.Collections.ObjectModel;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;
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
public partial class DashboardPageViewModel : ChartPageViewModelBase, ICleanupViewModel
{
    protected override void ReloadForChartSettings(bool styleChangeOnly) => LoadDashboardData();

    protected override ChartLoaderService ChartLoaderFor(ChartDataType? chartDataType) => GetChartLoaderForType(chartDataType);

    [ObservableProperty]
    private bool _hasPremium;

    #region Date Range

    // Flag to prevent duplicate data loading when changing settings locally
    // Stores the previous selection before opening custom range modal
    #endregion

    #region Chart Type

    #endregion

    #region Welcome Subtitle

    /// <summary>
    /// Gets the welcome subtitle text, which changes based on whether this is a sample company.
    /// </summary>
    public string WelcomeSubtitle => _companyManager?.IsSampleCompany == true
        ? "You're exploring a sample company. Feel free to experiment!".Translate()
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

    [ObservableProperty]
    private string _sourceSurveyBannerMessage = string.Empty;

    public void RefreshSourceSurveyBanner()
    {
        var tutorial = TutorialService.Instance;
        // Someone working through the checklist is asked when they finish it; this banner is
        // for everyone the checklist will never reach.
        ShowSourceSurveyBanner =
            (tutorial.HasSkippedTutorial || tutorial.IsSetupChecklistDismissed) &&
            tutorial.ShouldShowSourceSurvey();

        // The banner names whichever question is still open. Someone whose source is already
        // known, or who answered it in an earlier version, only has the second one left.
        SourceSurveyBannerMessage = tutorial.ShouldAskSurveySource()
            ? "Quick question: where did you hear about Argo Books?".Translate()
            : "Quick question: what do you mainly want to use Argo Books for?".Translate();

        // Never both at once, and the survey wins, so this has to follow it.
        RefreshUpdateEmailBanner();
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

    #endregion

    #region Update Email Banner

    [ObservableProperty]
    private bool _showUpdateEmailBanner;

    /// <summary>
    /// Offers update emails once the person has actually used the app, and never alongside the
    /// source survey, which is the one question worth more than this. Held back until there is
    /// some data because asking on an empty dashboard is asking a stranger.
    /// </summary>
    public void RefreshUpdateEmailBanner()
    {
        var settings = App.SettingsService?.GlobalSettings?.UpdateEmail;
        if (settings == null || settings.Dismissed || settings.Submitted || ShowSourceSurveyBanner)
        {
            ShowUpdateEmailBanner = false;
        }
        else
        {
            var data = _companyManager?.CompanyData;
            var hasUsedTheApp = data != null &&
                (data.Expenses.Count > 0 || data.Revenues.Count > 0 || data.Invoices.Count > 0);

            ShowUpdateEmailBanner = hasUsedTheApp && _companyManager?.IsSampleCompany != true;
        }

        // One banner at a time, and this is the last in line, so it follows the other two.
        RefreshReviewBanner();
    }

    [RelayCommand]
    private void OpenUpdateEmailSettings()
    {
        var modal = App.UpdateEmailModalViewModel;
        if (modal == null) return;

        modal.Subscribed -= OnUpdateEmailSubscribed;
        modal.Subscribed += OnUpdateEmailSubscribed;
        modal.Open();
    }

    private void OnUpdateEmailSubscribed(object? sender, EventArgs e)
    {
        ShowUpdateEmailBanner = false;
        if (sender is UpdateEmailModalViewModel modal)
            modal.Subscribed -= OnUpdateEmailSubscribed;
    }

    [RelayCommand]
    private void DismissUpdateEmailBanner()
    {
        var service = App.SettingsService;
        if (service != null)
        {
            service.GlobalSettings.UpdateEmail.Dismissed = true;
            _ = service.SaveGlobalSettingsAsync();
        }
        ShowUpdateEmailBanner = false;
    }

    #endregion

    #region Review Banner

    [ObservableProperty]
    private bool _showReviewBanner;

    /// <summary>
    /// Asks for a review once someone has had the app a while and is plainly using it, and
    /// never beside the survey or the update email offer. See
    /// <see cref="SurveyPrompts.ShouldAskForReview"/>.
    /// </summary>
    public void RefreshReviewBanner()
    {
        var settings = App.SettingsService?.GlobalSettings;
        var data = _companyManager?.CompanyData;
        if (settings == null || data == null || ShowSourceSurveyBanner || ShowUpdateEmailBanner)
        {
            ShowReviewBanner = false;
            return;
        }

        ShowReviewBanner = SurveyPrompts.ShouldAskForReview(
            settings.ReviewPrompt,
            settings.Tutorial.FirstLaunchDate,
            data.Expenses.Count + data.Revenues.Count + data.Invoices.Count,
            _companyManager?.IsSampleCompany == true,
            DateTime.UtcNow);
    }

    [RelayCommand]
    private void OpenReviewPage()
    {
        UrlHelper.SafeOpenUrl($"{ApiConfig.BaseUrl}/review/?source=app-review");
        CloseReviewBanner(s => s.Opened = true);
    }

    [RelayCommand]
    private void DismissReviewBanner() => CloseReviewBanner(s => s.Dismissed = true);

    private void CloseReviewBanner(Action<ReviewPromptSettings> record)
    {
        var service = App.SettingsService;
        if (service != null)
        {
            record(service.GlobalSettings.ReviewPrompt);
            _ = service.SaveGlobalSettingsAsync();
        }
        ShowReviewBanner = false;
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

    #endregion

    #region Data Loading

    /// <summary>
    /// Initializes the ViewModel with the company manager.
    /// </summary>
    public void Initialize(CompanyManager companyManager)
    {
        // The page factory reuses this view model and calls Initialize on every visit, so
        // subscribing without removing first would reload the dashboard once per past visit.
        if (_companyManager != null)
            _companyManager.CompanyDataChanged -= OnCompanyDataChanged;
        _companyManager = companyManager;

        // Initialize the widget layout system
        LayoutViewModel.Initialize(companyManager);

        // Load data through the full flow (includes CorrectRentalStatuses)
        LoadDashboardData();

        // Mark dashboard as explored when initialized
        TutorialService.Instance.CompleteChecklistItem(TutorialService.ChecklistItems.ExploreDashboard);

        // Notify properties that depend on company manager
        RefreshSampleCompanyState();

        _companyManager.CompanyDataChanged -= OnCompanyDataChanged;
        _companyManager.CompanyDataChanged += OnCompanyDataChanged;

        // Subscribe to language changes to refresh translated chart titles
        LanguageService.Instance.LanguageChanged -= OnLanguageChanged;
        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Whether the dashboard is the page on screen. Changes made while it isn't skip the reload:
    /// the page factory calls <see cref="Initialize"/> on every visit, which reloads it anyway.
    /// </summary>
    private static bool IsOnScreen =>
        App.NavigationService is not { } navigation || navigation.CurrentPageName == PageNames.Dashboard;

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

        if (App.UpdateEmailModalViewModel is { } updateEmailModal)
            updateEmailModal.Subscribed -= OnUpdateEmailSubscribed;

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
        LoadDashboardData();
    }

    /// <summary>
    /// Loads all dashboard data from the company data.
    /// </summary>
    public void LoadDashboardData()
    {
        var data = _companyManager?.CompanyData;
        if (data == null || !IsOnScreen) return;

        RefreshPresetDates();

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
