using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Services;

/// <summary>
/// Service for managing shared chart settings between Dashboard and Analytics pages.
/// Settings are persisted per-company to global settings and synchronized across pages.
/// </summary>
public partial class ChartSettingsService : ObservableObject
{
    private static readonly Lock Lock = new();

    private readonly IGlobalSettingsService? _globalSettingsService;
    private bool _isInitialized;
    private string? _currentCompanyPath;
    private bool _isLoading;

    /// <summary>
    /// Gets the singleton instance of the ChartSettingsService.
    /// </summary>
    public static ChartSettingsService Instance
    {
        get
        {
            if (field == null)
            {
                lock (Lock)
                {
                    field ??= new ChartSettingsService();
                }
            }
            return field;
        }
    }

    /// <summary>
    /// Available chart type options.
    /// </summary>
    public string[] ChartTypeOptions { get; } = ["Area", "Column", "Line", "Step Line", "Scatter"];

    /// <summary>
    /// Available date range options.
    /// </summary>
    public string[] DateRangeOptions { get; } = DatePresetNames.StandardDateRangeOptions;

    [ObservableProperty]
    private string _selectedChartType = "Area";

    [ObservableProperty]
    private string _selectedDateRange = DateRangePreset.ThisMonth.GetDisplayName();

    [ObservableProperty]
    private DateTime _startDate = new(DateTime.Now.Year, DateTime.Now.Month, 1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Now;

    [ObservableProperty]
    private bool _hasAppliedCustomRange;

    /// <summary>
    /// Gets the formatted text showing the applied custom date range.
    /// </summary>
    public string AppliedDateRangeText => HasAppliedCustomRange
        ? $"{StartDate:MMM d, yyyy} - {EndDate:MMM d, yyyy}"
        : string.Empty;

    /// <summary>
    /// Gets the formatted text showing the currently selected date range span.
    /// Always returns the date span regardless of selection type.
    /// </summary>
    public string DateRangeDisplayText => $"{StartDate:MMM d, yyyy} – {EndDate:MMM d, yyyy}";

    /// <summary>
    /// Gets the label for comparison period based on selected date range.
    /// </summary>
    public string ComparisonPeriodLabel =>
        DateRangePresetExtensions.ParseDateRange(SelectedDateRange)?.GetComparisonPeriodLabel() ?? "from last period";

    private ChartSettingsService()
    {
        // Try to get the global settings service from the app
        _globalSettingsService = App.SettingsService;
    }

    /// <summary>
    /// Initializes the service by loading settings from global settings.
    /// Called from ViewModel constructors; only runs once as a fallback
    /// if LoadForCompany hasn't been called yet.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        LoadFromGlobalSettings();
        _isInitialized = true;
    }

    /// <summary>
    /// Loads chart settings for a specific company. Resets to defaults first,
    /// then loads saved per-company preferences if they exist.
    /// Should be called when a company is opened.
    /// </summary>
    /// <param name="companyPath">File path of the company being opened.</param>
    public void LoadForCompany(string? companyPath)
    {
        _currentCompanyPath = companyPath;

        _isLoading = true;
        try
        {
            // Reset to defaults before loading company-specific settings
            SelectedChartType = "Area";
            SelectedDateRange = DateRangePreset.ThisMonth.GetDisplayName();
            HasAppliedCustomRange = false;
            UpdateDateRangeFromSelection();

            // Load company-specific settings (or keep defaults if none found)
            LoadFromGlobalSettings();
        }
        finally
        {
            _isLoading = false;
        }

        _isInitialized = true;

        // Notify all computed properties after load
        OnPropertyChanged(nameof(AppliedDateRangeText));
        OnPropertyChanged(nameof(ComparisonPeriodLabel));
        OnPropertyChanged(nameof(DateRangeDisplayText));

        // Notify subscribers so pages reload data with the restored settings
        DateRangeChanged?.Invoke(this, SelectedDateRange);
        ChartTypeChanged?.Invoke(this, SelectedChartType);
    }

    /// <summary>
    /// Loads chart settings from global settings.
    /// Uses per-company settings if a company path is set and has saved preferences,
    /// otherwise uses defaults.
    /// </summary>
    private void LoadFromGlobalSettings()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings == null) return;

        // Try to load company-specific settings first
        CompanyChartPreferences? chartSettings = null;
        if (!string.IsNullOrEmpty(_currentCompanyPath) &&
            settings.Ui.CompanyChartSettings.TryGetValue(_currentCompanyPath, out var companyChart))
        {
            chartSettings = companyChart;
        }

        // If no company-specific settings found, keep current defaults
        if (chartSettings == null) return;

        if (!string.IsNullOrEmpty(chartSettings.ChartType) &&
            ChartTypeOptions.Contains(chartSettings.ChartType))
        {
            SelectedChartType = chartSettings.ChartType;
        }

        if (!string.IsNullOrEmpty(chartSettings.DateRange))
        {
            if (chartSettings.DateRange == DateRangePreset.CustomRange.GetDisplayName() &&
                chartSettings.CustomStartDate.HasValue &&
                chartSettings.CustomEndDate.HasValue)
            {
                SelectedDateRange = chartSettings.DateRange;
                StartDate = chartSettings.CustomStartDate.Value;
                EndDate = chartSettings.CustomEndDate.Value;
                HasAppliedCustomRange = true;
            }
            else if (DateRangeOptions.Contains(chartSettings.DateRange))
            {
                SelectedDateRange = chartSettings.DateRange;
                UpdateDateRangeFromSelection();
            }
        }

        // Notify computed property changed
        OnPropertyChanged(nameof(AppliedDateRangeText));
    }

    /// <summary>
    /// Saves chart settings to global settings, keyed by current company path.
    /// </summary>
    private void SaveToGlobalSettings()
    {
        if (_isLoading) return;

        var settings = _globalSettingsService?.GetSettings();
        if (settings == null) return;

        var chartData = new CompanyChartPreferences
        {
            ChartType = SelectedChartType,
            DateRange = SelectedDateRange
        };

        if (HasAppliedCustomRange && SelectedDateRange == DateRangePreset.CustomRange.GetDisplayName())
        {
            chartData.CustomStartDate = StartDate;
            chartData.CustomEndDate = EndDate;
        }

        // Save to company-specific settings if we have a company path
        if (!string.IsNullOrEmpty(_currentCompanyPath))
        {
            settings.Ui.CompanyChartSettings[_currentCompanyPath] = chartData;
        }

        _globalSettingsService?.SaveSettings(settings);
    }

    partial void OnSelectedChartTypeChanged(string value)
    {
        SaveToGlobalSettings();
        ChartTypeChanged?.Invoke(this, value);
    }

    partial void OnSelectedDateRangeChanged(string value)
    {
        OnPropertyChanged(nameof(AppliedDateRangeText));
        OnPropertyChanged(nameof(ComparisonPeriodLabel));

        if (value != DateRangePreset.CustomRange.GetDisplayName())
        {
            HasAppliedCustomRange = false;
            UpdateDateRangeFromSelection();
        }

        SaveToGlobalSettings();
        DateRangeChanged?.Invoke(this, value);
    }

    partial void OnStartDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(AppliedDateRangeText));
        OnPropertyChanged(nameof(DateRangeDisplayText));
    }

    partial void OnEndDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(AppliedDateRangeText));
        OnPropertyChanged(nameof(DateRangeDisplayText));
    }

    partial void OnHasAppliedCustomRangeChanged(bool value)
    {
        OnPropertyChanged(nameof(AppliedDateRangeText));
    }

    /// <summary>
    /// Updates the start and end dates based on the selected date range option.
    /// </summary>
    public void UpdateDateRangeFromSelection()
    {
        var now = DateTime.Now;
        // End-of-today, used as the inclusive upper bound for any "current"
        // range. Without this, EndDate is the moment the dashboard refreshed
        // (e.g., 11:43 AM) and transactions stored later in the day,
        // including rows saved with DateTime.UtcNow on a behind-UTC clock,
        // get filtered out of stat cards even though the chart's own
        // end-of-day normalization includes them.
        var endOfToday = now.Date.AddDays(1).AddTicks(-1);

        var preset = DateRangePresetExtensions.ParseDateRange(SelectedDateRange);
        switch (preset)
        {
            case DateRangePreset.ThisMonth:
                StartDate = new DateTime(now.Year, now.Month, 1);
                EndDate = endOfToday;
                break;

            case DateRangePreset.LastMonth:
                var lastMonth = now.AddMonths(-1);
                StartDate = new DateTime(lastMonth.Year, lastMonth.Month, 1);
                EndDate = new DateTime(lastMonth.Year, lastMonth.Month,
                    DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month)).AddDays(1).AddTicks(-1);
                break;

            case DateRangePreset.Last30Days:
                StartDate = now.AddDays(-29).Date;
                EndDate = endOfToday;
                break;

            case DateRangePreset.Last100Days:
                StartDate = now.AddDays(-99).Date;
                EndDate = endOfToday;
                break;

            case DateRangePreset.Last365Days:
                StartDate = now.AddDays(-364).Date;
                EndDate = endOfToday;
                break;

            case DateRangePreset.ThisQuarter:
                var quarterStart = new DateTime(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1);
                StartDate = quarterStart;
                EndDate = endOfToday;
                break;

            case DateRangePreset.LastQuarter:
                var lastQuarterEnd = new DateTime(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1).AddDays(-1);
                var lastQuarterStart = lastQuarterEnd.AddMonths(-2);
                lastQuarterStart = new DateTime(lastQuarterStart.Year, lastQuarterStart.Month, 1);
                StartDate = lastQuarterStart;
                EndDate = lastQuarterEnd.Date.AddDays(1).AddTicks(-1);
                break;

            case DateRangePreset.ThisYear:
                StartDate = new DateTime(now.Year, 1, 1);
                EndDate = endOfToday;
                break;

            case DateRangePreset.LastYear:
                StartDate = new DateTime(now.Year - 1, 1, 1);
                EndDate = new DateTime(now.Year - 1, 12, 31).AddDays(1).AddTicks(-1);
                break;

            case DateRangePreset.AllTime:
                StartDate = App.CompanyManager?.CompanyData?.GetEarliestDate() ?? DateTime.Today;
                EndDate = endOfToday;
                break;

            case DateRangePreset.CustomRange:
                // Keep current values
                break;
        }
    }

    /// <summary>
    /// Event raised when the chart type changes.
    /// </summary>
    public event EventHandler<string>? ChartTypeChanged;

    /// <summary>
    /// Event raised when the date range changes.
    /// </summary>
    public event EventHandler<string>? DateRangeChanged;

    /// <summary>
    /// Static event raised when max pie slices setting changes.
    /// </summary>
    public static event EventHandler? MaxPieSlicesChanged;

    /// <summary>
    /// Notifies that the max pie slices setting has changed.
    /// </summary>
    public static void NotifyMaxPieSlicesChanged()
    {
        MaxPieSlicesChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the current max pie slices setting from global settings.
    /// </summary>
    public static int GetMaxPieSlices()
    {
        var settings = App.SettingsService?.GlobalSettings;
        return settings?.Ui.Chart.MaxPieSlices ?? 6;
    }
}
