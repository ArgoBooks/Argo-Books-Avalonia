using System.Collections.ObjectModel;
using ArgoBooks.Core;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Reports page with 3-step wizard navigation.
/// </summary>
public partial class ReportsPageViewModel : ViewModelBase, ICleanupViewModel
{
    #region Report Template Options for Step 1

    /// <summary>
    /// Template options for the Step 1 template grid.
    /// </summary>
    public ObservableCollection<ReportTemplateOption> ReportTemplateOptions { get; } =
    [
        new(ReportTemplateFactory.TemplateNames.MonthlyRevenue, "Revenue Overview", "Summarize monthly revenue data",
            Icons.RevenueChart, AppColors.MdBlue, AppColors.MdBlueLight),
        new(ReportTemplateFactory.TemplateNames.FinancialOverview, "Financial Overview", "Full financial breakdown",
            Icons.LineChart, AppColors.MdGreen, AppColors.MdGreenLight),
        new(ReportTemplateFactory.TemplateNames.PerformanceAnalysis, "Performance Analysis", "Business performance metrics",
            Icons.BarChart, AppColors.MdOrange, AppColors.MdOrangeLight),
        new(ReportTemplateFactory.TemplateNames.ReturnsAnalysis, "Returns Analysis", "Analyze product returns",
            Icons.Return, AppColors.MdRed, AppColors.MdRedLight),
        new(ReportTemplateFactory.TemplateNames.GeographicAnalysis, "Geographic Analysis", "Sales by region",
            Icons.MapPin, AppColors.MdPurple, AppColors.MdPurpleLight),
        new(ReportTemplateFactory.TemplateNames.LossesAnalysis, "Losses Analysis", "Analyze losses and damages",
            Icons.WarningCircle, AppColors.MdPink, AppColors.MdPinkLight),
        new(ReportTemplateFactory.TemplateNames.CustomerAnalysis, "Customer Analysis", "Analyze customer trends",
            Icons.Customers, AppColors.MdCyan, AppColors.MdCyanLight),
        new(ReportTemplateFactory.TemplateNames.ExpenseBreakdown, "Expense Breakdown", "Analyze spending patterns",
            Icons.DollarSign, AppColors.MdDeepOrange, AppColors.MdDeepOrangeLight),
        new(ReportTemplateFactory.TemplateNames.Custom, "Blank Template", "Start from scratch",
            Icons.Plus, AppColors.MdGray, AppColors.ReportBackground)
    ];

    /// <summary>
    /// Accounting template options for the Step 1 template grid (Accounting section).
    /// </summary>
    public ObservableCollection<ReportTemplateOption> AccountingTemplateOptions { get; } =
    [
        new(ReportTemplateFactory.TemplateNames.IncomeStatement, "Income Statement", "Shows if your business made or lost money",
            Icons.DocumentText, AppColors.FlatGreen, AppColors.FlatGreenLight),
        new(ReportTemplateFactory.TemplateNames.BalanceSheet, "Balance Sheet", "Snapshot of what you own and owe",
            Icons.DocumentStack, AppColors.FlatBlue, AppColors.FlatBlueLight),
        new(ReportTemplateFactory.TemplateNames.CashFlowStatement, "Cash Flow", "Where your cash came from and went",
            Icons.DollarSign, AppColors.FlatPurple, AppColors.FlatPurpleLight),
        new(ReportTemplateFactory.TemplateNames.GeneralLedger, "General Ledger", "Full list of every transaction",
            Icons.DocumentStackAdd, AppColors.ReportSlate, AppColors.FlatSlateLight),
        new(ReportTemplateFactory.TemplateNames.ARaging, "AR Aging", "Who owes you and for how long",
            Icons.DollarCircleAlt, AppColors.FlatTeal, AppColors.FlatTealLight),
        new(ReportTemplateFactory.TemplateNames.TaxSummary, "Tax Summary", "Tax you collected vs. tax you paid",
            Icons.BarChart, AppColors.FlatOrange, AppColors.FlatOrangeLight),
        new(ReportTemplateFactory.TemplateNames.ProductSales, "Sales by Product", "Units and revenue per product",
            Icons.Products, AppColors.FlatIndigo, AppColors.FlatIndigoLight),
        new(ReportTemplateFactory.TemplateNames.PayrollRemittance, "Payroll Remittance", "What you owe CRA for your payroll",
            Icons.Payments, AppColors.MdBrown, AppColors.MdBrownLight)
    ];

    #endregion

    #region Wizard Step Management

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private bool _step1Completed;

    [ObservableProperty]
    private bool _step2Completed;

    [ObservableProperty]
    private string _currentStepTitle = "Template & Settings";

    partial void OnCurrentStepChanged(int value)
    {
        CurrentStepTitle = value switch
        {
            1 => "Template & Settings",
            2 => "Layout Designer",
            3 => "Preview & Export",
            _ => "Template & Settings"
        };
    }

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;

    // Step indicator state properties
    public bool IsStep1Active => CurrentStep >= 1;
    public bool IsStep2Active => CurrentStep >= 2 || Step1Completed;
    public bool IsStep3Active => CurrentStep >= 3 || Step2Completed;
    public bool IsConnector1Active => CurrentStep > 1 || Step1Completed;
    public bool IsConnector2Active => CurrentStep > 2 || Step2Completed;

    // Show checkmark only when completed AND past that step (not currently on it)
    public bool IsStep1CompletedAndPast => Step1Completed && CurrentStep > 1;
    public bool IsStep2CompletedAndPast => Step2Completed && CurrentStep > 2;

    public bool CanGoBack => CurrentStep > 1;
    public bool CanGoNext => CurrentStep < 3;
    public bool IsOnFinalStep => CurrentStep == 3;

    /// <summary>
    /// Function to confirm discarding unsaved changes. Set by AppShellViewModel.
    /// Returns true if changes should be discarded (continue), false to cancel.
    /// </summary>
    public Func<Task<bool>>? ConfirmDiscardChangesAsync { get; set; }

    [RelayCommand]
    private async Task GoToPreviousStepAsync()
    {
        if (CurrentStep > 1)
        {
            // Check for unsaved changes when going back from step 2 (Layout Designer)
            if (CurrentStep == 2 && HasUnsavedChanges)
            {
                if (ConfirmDiscardChangesAsync != null)
                {
                    var shouldContinue = await ConfirmDiscardChangesAsync();
                    if (!shouldContinue)
                    {
                        return; // User cancelled, don't go back
                    }
                }
            }

            // Reset completion flags when going back
            if (CurrentStep == 3)
            {
                Step2Completed = false;
            }
            else if (CurrentStep == 2)
            {
                Step1Completed = false;
                // Preserve chart selections and date preset before reloading template
                // (will be restored in ApplyConfigurationToPageSettings after template loads)
                _chartTypesToPreserve = Configuration.Filters.SelectedChartTypes.ToList();
                _datePresetToPreserve = SelectedDatePreset;

                // Clear undo history and reload the template to discard layout changes
                UndoRedoManager.Clear();
                LoadTemplate(SelectedTemplateName);

                // Clear radio button selection for point-in-time reports where date range is disabled
                // (mirrors the logic in OnSelectedTemplateNameChanged, which doesn't fire here
                // because SelectedTemplateName didn't change)
                if (!IsDateRangeEnabled)
                {
                    foreach (var option in DatePresets)
                        option.IsSelected = false;
                }
            }

            CurrentStep--;
            NotifyStepChanged();
        }
    }

    /// <summary>
    /// Restores chart selections from a list of chart types.
    /// </summary>
    private void RestoreChartSelections(List<ChartDataType> chartTypes)
    {
        Configuration.Filters.SelectedChartTypes.Clear();
        Configuration.Filters.SelectedChartTypes.AddRange(chartTypes);

        foreach (var chart in AvailableCharts)
        {
            chart.IsSelected = chartTypes.Contains(chart.ChartType);
        }

        OnPropertyChanged(nameof(HasSelectedCharts));
    }

    private void NotifyStepChanged()
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep1Active));
        OnPropertyChanged(nameof(IsStep2Active));
        OnPropertyChanged(nameof(IsStep3Active));
        OnPropertyChanged(nameof(IsConnector1Active));
        OnPropertyChanged(nameof(IsConnector2Active));
        OnPropertyChanged(nameof(IsStep1CompletedAndPast));
        OnPropertyChanged(nameof(IsStep2CompletedAndPast));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsOnFinalStep));
    }

    [RelayCommand]
    private void GoToNextStep()
    {
        if (CurrentStep < 3)
        {
            if (CurrentStep == 1)
            {
                Step1Completed = true;
                ApplyFiltersToConfiguration();
            }
            else if (CurrentStep == 2)
            {
                Step2Completed = true;
                GeneratePreview();
            }

            CurrentStep++;
            NotifyStepChanged();
        }
    }

    #endregion

    #region Step 1 - Template & Settings

    [ObservableProperty]
    private int _step1TabIndex;

    [ObservableProperty]
    private int _tablePropertiesTabIndex;

    public bool IsTemplatesTabSelected => Step1TabIndex == 0;
    public bool IsChartsTabSelected => Step1TabIndex == 1;

    partial void OnStep1TabIndexChanged(int value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsTemplatesTabSelected));
        OnPropertyChanged(nameof(IsChartsTabSelected));

        // When switching to the Custom (charts) tab, reset template-specific state
        // so Balance Sheet's disabled date range doesn't carry over, and the report
        // name reflects that this is now a custom chart report.
        if (IsChartsTabSelected)
        {
            IsDateRangeEnabled = true;
            ReportName = ReportTemplateFactory.TemplateNames.Custom;
            Configuration.Title = ReportTemplateFactory.TemplateNames.Custom;
            PageOrientation = PageOrientation.Landscape;
            Configuration.PageOrientation = PageOrientation.Landscape;
        }
    }

    [RelayCommand]
    private void SetStep1Tab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out var index))
        {
            Step1TabIndex = index;
        }
    }

    [RelayCommand]
    private void SetTablePropertiesTab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out var index))
        {
            TablePropertiesTabIndex = index;
        }
    }

    [ObservableProperty]
    private string _selectedTemplateName = ReportTemplateFactory.TemplateNames.Custom;

    [ObservableProperty]
    private string _reportName = "Untitled Report";

    partial void OnReportNameChanged(string value)
    {
        // Keep configuration title in sync with the report name
        Configuration.Title = value;

        // Keep export file path in sync with report name so switching templates
        // doesn't overwrite a previously exported file.
        if (!string.IsNullOrEmpty(ExportFilePath))
        {
            var dir = Path.GetDirectoryName(ExportFilePath) ?? "";
            var ext = Path.GetExtension(ExportFilePath);
            var newName = string.IsNullOrWhiteSpace(value) ? "Report" : value;
            ExportFilePath = Path.Combine(dir, $"{newName}{ext}");
        }

        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Chart types to preserve during template reload (when going back from step 2).
    /// </summary>
    private List<ChartDataType>? _chartTypesToPreserve;

    /// <summary>
    /// Date preset to preserve during template reload (when going back from step 2).
    /// </summary>
    private string? _datePresetToPreserve;

    [ObservableProperty]
    private string _selectedDatePreset = DatePresetNames.ThisMonth;

    [ObservableProperty]
    private DateTimeOffset? _customStartDate;

    [ObservableProperty]
    private DateTimeOffset? _customEndDate;

    [ObservableProperty]
    private bool _isCustomDateRange;

    [ObservableProperty]
    private bool _isDateRangeEnabled = true;

    [ObservableProperty]
    private TransactionType _selectedTransactionType = TransactionType.Revenue;

    public ObservableCollection<CustomTemplateOption> CustomTemplateNames { get; } = [];
    public ObservableCollection<DatePresetOption> DatePresets { get; } = [];

    // Chart selection - all charts in one list, use IsSelected property
    public ObservableCollection<ChartOption> AvailableCharts { get; } = [];

    // Chart categories for grouped display
    public ObservableCollection<ChartCategoryGroup> ChartCategories { get; } = [];

    public bool HasSelectedCharts => AvailableCharts.Any(c => c.IsSelected);

    partial void OnSelectedTemplateNameChanged(string value)
    {
        IsDateRangeEnabled = value != ReportTemplateFactory.TemplateNames.BalanceSheet;
        LoadTemplate(value);

        // Clear radio button selection for point-in-time reports where date range is disabled
        if (!IsDateRangeEnabled)
        {
            foreach (var option in DatePresets)
                option.IsSelected = false;
        }
    }

    partial void OnSelectedDatePresetChanged(string value)
    {
        IsCustomDateRange = value == DatePresetNames.Custom;
        OnPropertyChanged(nameof(SelectedDatePresetOption));

        // Step 1 hands the range over on Next; in the designer the report already exists, so the
        // range has to reach it now. Going through PageSettingsDatePreset records it for undo.
        if (CurrentStep == 2)
        {
            ApplyDateRangeToConfiguration();
            PageSettingsDatePreset = value;
        }
    }

    partial void OnCustomStartDateChanged(DateTimeOffset? value) => ApplyDesignerCustomDates();

    partial void OnCustomEndDateChanged(DateTimeOffset? value) => ApplyDesignerCustomDates();

    private void ApplyDesignerCustomDates()
    {
        if (CurrentStep != 2 || !IsCustomDateRange)
            return;

        ApplyDateRangeToConfiguration();
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets or sets the selected date preset option for ComboBox binding in the properties panel.
    /// </summary>
    public DatePresetOption? SelectedDatePresetOption
    {
        get => DatePresets.FirstOrDefault(p => p.IsSelected);
        set
        {
            if (value != null)
                SelectDatePreset(value);
        }
    }

    [RelayCommand]
    private void ToggleChart(ChartOption chart)
    {
        chart.IsSelected = !chart.IsSelected;
        OnPropertyChanged(nameof(HasSelectedCharts));
    }

    [RelayCommand]
    private void ToggleSelectAllInCategory(ChartCategoryGroup category)
    {
        category.ToggleSelectAll();
        OnPropertyChanged(nameof(HasSelectedCharts));
    }

    [RelayCommand]
    private void SelectTemplate(string? templateName)
    {
        if (!string.IsNullOrEmpty(templateName))
        {
            SelectedTemplateName = templateName;

            // Update IsSelected on all built-in template options
            foreach (var template in ReportTemplateOptions)
            {
                template.IsSelected = template.TemplateName == templateName;
            }

            // Update IsSelected on all accounting template options
            foreach (var template in AccountingTemplateOptions)
            {
                template.IsSelected = template.TemplateName == templateName;
            }

            // Update IsSelected on all custom template options
            foreach (var customTemplate in CustomTemplateNames)
            {
                customTemplate.IsSelected = customTemplate.Name == templateName;
            }
        }
    }

    [RelayCommand]
    private async Task OpenCustomTemplateAsync(string? templateName)
    {
        if (string.IsNullOrEmpty(templateName)) return;

        // Load the custom template
        SelectedTemplateName = templateName;

        // Update IsSelected on all built-in template options
        foreach (var template in ReportTemplateOptions)
        {
            template.IsSelected = template.TemplateName == templateName;
        }

        // Update IsSelected on all accounting template options
        foreach (var template in AccountingTemplateOptions)
        {
            template.IsSelected = template.TemplateName == templateName;
        }

        // Update IsSelected on all custom template options
        foreach (var customTemplate in CustomTemplateNames)
        {
            customTemplate.IsSelected = customTemplate.Name == templateName;
        }

        // Wait for template to load
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);

        ReportName = templateName;
        Configuration.Title = templateName;

        // Notify UI of configuration change (needed because ReportConfiguration doesn't implement INPC)
        OnPropertyChanged(nameof(Configuration));

        // Clear any unsaved changes indicator
        UndoRedoManager.Clear();

        // Go directly to step 2 (Layout Designer)
        Step1Completed = true;
        ApplyFiltersToConfiguration();
        CurrentStep = 2;
        NotifyStepChanged();
    }

    [RelayCommand]
    private void SelectDatePreset(DatePresetOption? preset)
    {
        if (preset != null)
        {
            SelectedDatePreset = preset.Name;

            // Update IsSelected on all date preset options
            foreach (var option in DatePresets)
            {
                option.IsSelected = option.Name == preset.Name;
            }
        }
    }

    #endregion

    #region Step 2 - Layout Designer

    [ObservableProperty]
    private ReportConfiguration _configuration = new();

    [ObservableProperty]
    private ReportElementBase? _selectedElement;

    /// <summary>
    /// Event raised when an element's properties change and the canvas needs to refresh.
    /// </summary>
    public event EventHandler<ReportElementBase>? ElementPropertyChanged;
    public event EventHandler? PageSettingsRefreshRequested;
    public event EventHandler? TemplateLoaded;
    public event EventHandler? PreviewFitToWindowRequested;
    public event EventHandler? CanvasRefreshRequested;

    partial void OnSelectedElementChanged(ReportElementBase? oldValue, ReportElementBase? newValue)
    {
        // Unsubscribe from old element
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnElementPropertyChanged;
            oldValue.PropertyChanging -= OnElementPropertyChanging;
        }

        // Subscribe to new element
        if (newValue != null)
        {
            newValue.PropertyChanged += OnElementPropertyChanged;
            newValue.PropertyChanging += OnElementPropertyChanging;
        }

        OnPropertyChanged(nameof(SelectedChartElement));
        OnPropertyChanged(nameof(SelectedChartDataTypeOption));
        OnPropertyChanged(nameof(SelectedChartStyleOption));
        OnPropertyChanged(nameof(SelectedLabelElement));
        OnPropertyChanged(nameof(SelectedImageElement));
        OnPropertyChanged(nameof(SelectedImageFileName));
        OnPropertyChanged(nameof(HasSelectedImage));
        OnPropertyChanged(nameof(SelectedTableElement));
        OnPropertyChanged(nameof(IsSelectedTableMaxRowsUnlimited));
        OnPropertyChanged(nameof(SelectedDateRangeElement));
        OnPropertyChanged(nameof(SelectedSummaryElement));
        OnPropertyChanged(nameof(IsChartSelected));
        OnPropertyChanged(nameof(IsLabelSelected));
        OnPropertyChanged(nameof(IsImageSelected));
        OnPropertyChanged(nameof(IsTableSelected));
        OnPropertyChanged(nameof(IsDateRangeSelected));
        OnPropertyChanged(nameof(IsSummarySelected));
        OnPropertyChanged(nameof(SelectedAccountingTableElement));
        OnPropertyChanged(nameof(IsAccountingTableSelected));
        OnPropertyChanged(nameof(ShowSectionHeaderColors));
        OnPropertyChanged(nameof(ShowTotalColors));
        OnPropertyChanged(nameof(IsDistributionChartSelected));
    }

    private void OnElementPropertyChanging(object? sender, ElementPropertyChangingEventArgs e)
    {
        if (sender is ReportElementBase element)
        {
            // Skip ZOrder and Bounds - these are tracked through other mechanisms
            if (e.PropertyName is "ZOrder" or "Bounds")
                return;

            // Skip no-op changes (can happen during binding round-trips, e.g. when
            // NumericUpDown writes back the same value it just received)
            if (Equals(e.OldValue, e.NewValue))
                return;

            // For position/size changes, create a coalescing move/resize action.
            // Rapid changes (e.g., scrolling spinner controls) will be merged into
            // a single undo entry by the undo manager's coalescing logic.
            // During canvas drag/resize, SuppressRecording is set so these are skipped.
            if (e.PropertyName is "X" or "Y" or "Width" or "Height")
            {
                // PropertyChanging fires before the change, so element still has old values
                var oldBounds = (element.X, element.Y, element.Width, element.Height);
                var newBounds = oldBounds;
                switch (e.PropertyName)
                {
                    case "X": newBounds.Item1 = (double)e.NewValue!; break;
                    case "Y": newBounds.Item2 = (double)e.NewValue!; break;
                    case "Width": newBounds.Item3 = (double)e.NewValue!; break;
                    case "Height": newBounds.Item4 = (double)e.NewValue!; break;
                }

                var isResize = e.PropertyName is "Width" or "Height";
                UndoRedoManager.RecordAction(new MoveResizeElementAction(
                    Configuration, element.Id, oldBounds, newBounds, isResize));
                return;
            }

            UndoRedoManager.RecordAction(new ElementPropertyChangeAction(
                Configuration,
                element.Id,
                element.DisplayName,
                e.PropertyName,
                e.OldValue,
                e.NewValue));
        }
    }

    private void OnElementPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is ReportElementBase element)
        {
            // Raise event to notify view to refresh element content
            ElementPropertyChanged?.Invoke(this, element);

            // When the accounting table report type changes, update color property visibility
            if (e.PropertyName == nameof(AccountingTableReportElement.ReportType))
            {
                OnPropertyChanged(nameof(ShowSectionHeaderColors));
                OnPropertyChanged(nameof(ShowTotalColors));
            }
        }
    }

    // Static empty instances to prevent null-traversal binding errors when no element is selected.
    // These are never added to Configuration, so PropertyChanging subscriptions won't fire on them.
    private static readonly ChartReportElement EmptyChart = new();
    private static readonly LabelReportElement EmptyLabel = new();
    private static readonly ImageReportElement EmptyImage = new();
    private static readonly TableReportElement EmptyTable = new();
    private static readonly DateRangeReportElement EmptyDateRange = new();
    private static readonly SummaryReportElement EmptySummary = new();
    private static readonly AccountingTableReportElement EmptyAccountingTable = new();

    // Typed accessors for element-specific properties (never null to avoid binding errors)
    public ChartReportElement SelectedChartElement => (SelectedElement as ChartReportElement) ?? EmptyChart;
    public LabelReportElement SelectedLabelElement => (SelectedElement as LabelReportElement) ?? EmptyLabel;
    public ImageReportElement SelectedImageElement => (SelectedElement as ImageReportElement) ?? EmptyImage;
    public string SelectedImageFileName => string.IsNullOrEmpty(SelectedImageElement.ImagePath)
        ? string.Empty
        : Path.GetFileName(SelectedImageElement.ImagePath);
    public bool HasSelectedImage => !string.IsNullOrEmpty(SelectedImageElement.ImagePath);
    public TableReportElement SelectedTableElement => (SelectedElement as TableReportElement) ?? EmptyTable;

    public bool IsSelectedTableMaxRowsUnlimited
    {
        get => SelectedTableElement.MaxRows == 0;
        set
        {
            if (SelectedElement is TableReportElement table)
                table.MaxRows = value ? 0 : 10;
            OnPropertyChanged();
        }
    }

    public DateRangeReportElement SelectedDateRangeElement => (SelectedElement as DateRangeReportElement) ?? EmptyDateRange;
    public SummaryReportElement SelectedSummaryElement => (SelectedElement as SummaryReportElement) ?? EmptySummary;
    public AccountingTableReportElement SelectedAccountingTableElement => (SelectedElement as AccountingTableReportElement) ?? EmptyAccountingTable;

    /// <summary>
    /// Whether the selected accounting table's report type produces section header rows.
    /// </summary>
    public bool ShowSectionHeaderColors => SelectedAccountingTableElement.ReportType
        is not AccountingReportType.AccountsReceivableAging;

    /// <summary>
    /// Whether the selected accounting table's report type produces total/grand total rows.
    /// </summary>
    public bool ShowTotalColors => SelectedAccountingTableElement.ReportType
        is AccountingReportType.BalanceSheet or AccountingReportType.AccountsReceivableAging;

    /// <summary>
    /// Gets or sets the selected chart style as a ChartStyleOption for the ComboBox binding.
    /// Converts between ChartStyleOption and ReportChartStyle.
    /// </summary>
    public ChartStyleOption? SelectedChartStyleOption
    {
        get => SelectedChartElement is { } chart
            ? ChartStyleOptions.FirstOrDefault(o => o.Value == chart.ChartStyle)
            : null;
        set
        {
            if (value != null && SelectedChartElement is { } chart)
            {
                chart.ChartStyle = value.Value;
                OnPropertyChanged();
            }
        }
    }

    // Type checking properties for conditional visibility
    public bool IsChartSelected => SelectedElement is ChartReportElement;
    public bool IsLabelSelected => SelectedElement is LabelReportElement;
    public bool IsImageSelected => SelectedElement is ImageReportElement;
    public bool IsTableSelected => SelectedElement is TableReportElement;
    public bool IsDateRangeSelected => SelectedElement is DateRangeReportElement;
    public bool IsSummarySelected => SelectedElement is SummaryReportElement;
    public bool IsAccountingTableSelected => SelectedElement is AccountingTableReportElement;

    /// <summary>
    /// Whether the selected chart is a distribution/pie chart that benefits from legend display.
    /// </summary>
    public bool IsDistributionChartSelected => SelectedChartElement.ChartType is
        ChartDataType.WorldMap or
        ChartDataType.RevenueDistribution or
        ChartDataType.ExpensesDistribution or
        ChartDataType.ReturnReasons or
        ChartDataType.LossReasons or
        ChartDataType.ReturnsByCategory or
        ChartDataType.LossesByCategory or
        ChartDataType.ReturnsByProduct or
        ChartDataType.LossesByProduct or
        ChartDataType.CountriesOfOrigin or
        ChartDataType.CountriesOfDestination or
        ChartDataType.CompaniesOfOrigin or
        ChartDataType.CompaniesOfDestination or
        ChartDataType.AccountantsTransactions or
        ChartDataType.TopCustomersByRevenue or
        ChartDataType.CustomerPaymentStatus or
        ChartDataType.ActiveVsInactiveCustomers;

    [ObservableProperty]
    private bool _showGrid = true;

    [ObservableProperty]
    private int _gridSize = 10;

    [ObservableProperty]
    private bool _snapToGrid = true;

    [ObservableProperty]
    private double _canvasWidth = 1123;

    [ObservableProperty]
    private double _canvasHeight = 794;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private bool _isElementPanelExpanded = true;

    #region Page Management

    [ObservableProperty]
    private int _currentDesignerPage = 1;

    /// <summary>
    /// Callback to get the viewport center position from the canvas (page number, local X, local Y).
    /// Set by the View code-behind when the canvas is available.
    /// </summary>
    public Func<(int PageNumber, double LocalX, double LocalY)>? GetViewportCenter { get; set; }

    public string CurrentDesignerPageDisplay =>
        $"Page {CurrentDesignerPage} of {Configuration.PageCount}";

    public bool CanDeletePage => Configuration.PageCount > 1;

    public bool CanGoToPreviousPage => CurrentDesignerPage > 1;

    public bool CanGoToNextPage => CurrentDesignerPage < Configuration.PageCount;

    partial void OnCurrentDesignerPageChanged(int value)
    {
        _ = value;
        OnPropertyChanged(nameof(CurrentDesignerPageDisplay));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
    }

    [RelayCommand]
    private void NextDesignerPage()
    {
        if (CurrentDesignerPage < Configuration.PageCount)
        {
            CurrentDesignerPage++;
        }
    }

    [RelayCommand]
    private void PreviousDesignerPage()
    {
        if (CurrentDesignerPage > 1)
        {
            CurrentDesignerPage--;
        }
    }

    [RelayCommand]
    private void AddPage()
    {
        UndoRedoManager.RecordAction(new AddPageAction(Configuration));
        Configuration.PageCount++;
        CurrentDesignerPage = Configuration.PageCount;
        OnPropertyChanged(nameof(CurrentDesignerPageDisplay));
        OnPropertyChanged(nameof(CanDeletePage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        CanvasRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void DeletePage()
    {
        if (Configuration.PageCount <= 1) return;

        var pageToDelete = CurrentDesignerPage;
        var elementsOnPage = Configuration.Elements
            .Where(e => e.PageNumber == pageToDelete).ToList();

        // Record undo action before making changes
        UndoRedoManager.RecordAction(new DeletePageAction(Configuration, pageToDelete, elementsOnPage));

        foreach (var element in elementsOnPage)
        {
            Configuration.Elements.Remove(element);
        }

        // Renumber elements on higher pages
        foreach (var element in Configuration.Elements.Where(e => e.PageNumber > pageToDelete))
        {
            element.PageNumber--;
        }

        Configuration.PageCount--;

        // Navigate to a valid page
        if (CurrentDesignerPage > Configuration.PageCount)
        {
            CurrentDesignerPage = Configuration.PageCount;
        }
        else
        {
            // Force refresh even if page number didn't change
            OnPropertyChanged(nameof(CurrentDesignerPageDisplay));
        }

        SelectedElement = null;
        OnPropertyChanged(nameof(CanDeletePage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        CanvasRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Context Menu

    [ObservableProperty]
    private bool _isContextMenuOpen;

    [ObservableProperty]
    private double _contextMenuX;

    [ObservableProperty]
    private double _contextMenuY;

    /// <summary>
    /// Shows the context menu at the specified position.
    /// </summary>
    public void ShowContextMenu(double x, double y)
    {
        ContextMenuX = x;
        ContextMenuY = y;
        IsContextMenuOpen = true;
    }

    [RelayCommand]
    private void HideContextMenu()
    {
        IsContextMenuOpen = false;
    }

    [RelayCommand]
    private void ContextMenuBringToFront()
    {
        BringToFront();
        HideContextMenu();
    }

    [RelayCommand]
    private void ContextMenuSendToBack()
    {
        SendToBack();
        HideContextMenu();
    }

    [RelayCommand]
    private void ContextMenuDuplicate()
    {
        DuplicateSelectedElements();
        HideContextMenu();
    }

    [RelayCommand]
    private void ContextMenuDelete()
    {
        DeleteSelectedElements();
        HideContextMenu();
    }

    #endregion

    [ObservableProperty]
    private bool _isSaveTemplateOpen;

    [ObservableProperty]
    private bool _showSaveConfirmation;

    [ObservableProperty]
    private bool _showNoChangesMessage;

    public ReportUndoRedoManager UndoRedoManager { get; } = new();

    /// <summary>
    /// Gets whether the report has unsaved changes (changes since last save).
    /// </summary>
    public bool HasUnsavedChanges => UndoRedoManager.HasUnsavedChanges;

    /// <summary>
    /// ViewModel for the undo/redo button group control.
    /// </summary>
    public UndoRedoButtonGroupViewModel UndoRedoViewModel { get; }

    [ObservableProperty]
    private bool _isUndoDropdownOpen;

    [ObservableProperty]
    private bool _isRedoDropdownOpen;

    [RelayCommand]
    private void UndoToIndex(int index)
    {
        for (int i = 0; i <= index; i++)
        {
            UndoRedoManager.Undo();
        }
        IsUndoDropdownOpen = false;
        OnPropertyChanged(nameof(Configuration));
    }

    [RelayCommand]
    private void RedoToIndex(int index)
    {
        for (int i = 0; i <= index; i++)
        {
            UndoRedoManager.Redo();
        }
        IsRedoDropdownOpen = false;
        OnPropertyChanged(nameof(Configuration));
    }

    public ObservableCollection<ReportElementBase> SelectedElements { get; } = [];

    // Properties for toolbar button enabling
    public bool HasSelectedElements => SelectedElements.Count > 0;
    public bool HasMultipleSelectedElements => SelectedElements.Count > 1;

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedElements));
        OnPropertyChanged(nameof(HasMultipleSelectedElements));
    }

    [RelayCommand]
    private void SelectElement(ReportElementBase? element)
    {
        SelectedElements.Clear();
        if (element != null)
        {
            SelectedElements.Add(element);
            SelectedElement = element;
        }
        else
        {
            SelectedElement = null;
        }
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SelectedElements.Clear();
        SelectedElement = null;
        NotifySelectionChanged();
    }

    /// <summary>
    /// Syncs selection from the canvas control.
    /// </summary>
    public void SyncSelection(List<ReportElementBase> selectedElements)
    {
        SelectedElements.Clear();
        foreach (var element in selectedElements)
        {
            SelectedElements.Add(element);
        }
        SelectedElement = selectedElements.FirstOrDefault();
        NotifySelectionChanged();
    }

    partial void OnConfigurationChanged(ReportConfiguration value)
    {
        _ = value;
        UpdateCanvasDimensions();
    }

    private void UpdateCanvasDimensions()
    {
        var (width, height) = PageDimensions.GetDimensions(Configuration.PageSize, Configuration.PageOrientation);
        CanvasWidth = width;
        CanvasHeight = height;
    }

    [RelayCommand]
    private void AddElement(ReportElementType elementType)
    {
        // Get element dimensions first so we can center it
        var (width, height) = elementType switch
        {
            ReportElementType.Chart => (300.0, 200.0),
            ReportElementType.Table => (400.0, 200.0),
            ReportElementType.Label => (200.0, 40.0),
            ReportElementType.Image => (150.0, 150.0),
            ReportElementType.DateRange => (200.0, 30.0),
            ReportElementType.Summary => (200.0, 120.0),
            ReportElementType.AccountingTable => (500.0, 600.0),
            _ => (200.0, 40.0)
        };

        // Place at the center of the user's current viewport
        var viewportCenter = GetViewportCenter?.Invoke();
        int pageNumber;
        double centerX, centerY;

        if (viewportCenter.HasValue)
        {
            pageNumber = viewportCenter.Value.PageNumber;
            centerX = viewportCenter.Value.LocalX;
            centerY = viewportCenter.Value.LocalY;
        }
        else
        {
            // Fallback: top-left of content area on current page
            pageNumber = CurrentDesignerPage;
            var margins = Configuration.PageMargins;
            var headerHeight = Configuration.ShowHeader
                ? PageDimensions.GetHeaderHeight(Configuration.ShowCompanyDetails)
                : 0;
            centerX = margins.Left + width / 2;
            centerY = headerHeight + margins.Top + height / 2;
        }

        // Center the element on the viewport center point
        var elementX = centerX - width / 2;
        var elementY = centerY - height / 2;

        // Clamp to content bounds
        var (pageWidth, pageHeight) = PageDimensions.GetDimensions(Configuration.PageSize, Configuration.PageOrientation);
        var ml = Configuration.PageMargins.Left;
        var mr = Configuration.PageMargins.Right;
        var mt = Configuration.PageMargins.Top;
        var mb = Configuration.PageMargins.Bottom;
        var hh = Configuration.ShowHeader ? PageDimensions.GetHeaderHeight(Configuration.ShowCompanyDetails) : 0;
        var fh = Configuration.ShowFooter ? PageDimensions.FooterHeight : 0;
        var contentLeft = ml;
        var contentTop = hh + mt;
        var contentRight = pageWidth - mr;
        var contentBottom = pageHeight - fh - mb;

        elementX = Math.Max(contentLeft, Math.Min(elementX, contentRight - width));
        elementY = Math.Max(contentTop, Math.Min(elementY, contentBottom - height));

        ReportElementBase element = elementType switch
        {
            ReportElementType.Chart => new ChartReportElement { X = elementX, Y = elementY, Width = width, Height = height },
            ReportElementType.Table => new TableReportElement { X = elementX, Y = elementY, Width = width, Height = height },
            ReportElementType.Label => new LabelReportElement { X = elementX, Y = elementY, Width = width, Height = height },
            ReportElementType.Image => new ImageReportElement { X = elementX, Y = elementY, Width = width, Height = height },
            ReportElementType.DateRange => new DateRangeReportElement { X = elementX, Y = elementY, Width = width, Height = height },
            ReportElementType.Summary => new SummaryReportElement { X = elementX, Y = elementY, Width = width, Height = height },
            ReportElementType.AccountingTable => new AccountingTableReportElement { X = elementX, Y = elementY, Width = width, Height = height },
            _ => new LabelReportElement()
        };

        element.PageNumber = pageNumber;
        Configuration.AddElement(element);
        UndoRedoManager.RecordAction(new AddElementAction(Configuration, element));

        // Suppress recording while setting up selection, binding updates from the
        // properties panel can write back rounded values (e.g. the NumericUpDown
        // integer display format) which would create a spurious "Move element" action.
        UndoRedoManager.SuppressRecording = true;
        try
        {
            SelectedElement = element;
            SelectedElements.Clear();
            SelectedElements.Add(element);
            NotifySelectionChanged();
            OnPropertyChanged(nameof(Configuration));
        }
        finally
        {
            UndoRedoManager.SuppressRecording = false;
        }
    }

    [RelayCommand]
    private void DeleteSelectedElements()
    {
        foreach (var element in SelectedElements.ToList())
        {
            UndoRedoManager.RecordAction(new RemoveElementAction(Configuration, element));
            Configuration.RemoveElement(element.Id);
        }
        SelectedElements.Clear();
        SelectedElement = null;
        NotifySelectionChanged();
        OnPropertyChanged(nameof(Configuration));
    }

    [RelayCommand]
    private void DuplicateSelectedElements()
    {
        if (SelectedElements.Count == 0) return;

        var newElements = new List<ReportElementBase>();
        const double offset = 20; // Offset for duplicated elements

        foreach (var element in SelectedElements.ToList())
        {
            var clone = element.Clone();
            clone.X += offset;
            clone.Y += offset;

            // Clamp position to stay within canvas bounds
            clone.X = Math.Max(0, Math.Min(clone.X, CanvasWidth - clone.Width));
            clone.Y = Math.Max(0, Math.Min(clone.Y, CanvasHeight - clone.Height));

            Configuration.AddElement(clone);
            UndoRedoManager.RecordAction(new AddElementAction(Configuration, clone));
            newElements.Add(clone);
        }

        // Select the duplicated elements, suppress recording to avoid spurious
        // undo entries from binding write-backs during selection setup.
        UndoRedoManager.SuppressRecording = true;
        try
        {
            SelectedElements.Clear();
            foreach (var element in newElements)
            {
                SelectedElements.Add(element);
            }
            SelectedElement = newElements.FirstOrDefault();
            NotifySelectionChanged();
            OnPropertyChanged(nameof(Configuration));
        }
        finally
        {
            UndoRedoManager.SuppressRecording = false;
        }
    }

    [RelayCommand]
    private void Undo()
    {
        if (UndoRedoManager.CanUndo)
        {
            UndoRedoManager.Undo();
            OnPropertyChanged(nameof(Configuration));
        }
    }

    [RelayCommand]
    private void Redo()
    {
        if (UndoRedoManager.CanRedo)
        {
            UndoRedoManager.Redo();
            OnPropertyChanged(nameof(Configuration));
        }
    }

    [RelayCommand]
    private void BringToFront()
    {
        if (SelectedElement == null) return;

        var oldZOrders = Configuration.Elements.ToDictionary(e => e.Id, e => e.ZOrder);
        var maxZ = Configuration.Elements.Max(e => e.ZOrder);
        SelectedElement.ZOrder = maxZ + 1;
        var newZOrders = Configuration.Elements.ToDictionary(e => e.Id, e => e.ZOrder);

        UndoRedoManager.RecordAction(new ZOrderChangeAction(Configuration, oldZOrders, newZOrders, "Bring to Front".Translate()));
        OnPropertyChanged(nameof(Configuration));
    }

    [RelayCommand]
    private void SendToBack()
    {
        if (SelectedElement == null) return;

        var oldZOrders = Configuration.Elements.ToDictionary(e => e.Id, e => e.ZOrder);
        var minZ = Configuration.Elements.Min(e => e.ZOrder);

        foreach (var element in Configuration.Elements)
        {
            if (element.Id != SelectedElement.Id)
                element.ZOrder++;
        }
        SelectedElement.ZOrder = minZ;

        var newZOrders = Configuration.Elements.ToDictionary(e => e.Id, e => e.ZOrder);
        UndoRedoManager.RecordAction(new ZOrderChangeAction(Configuration, oldZOrders, newZOrders, "Send to Back".Translate()));
        OnPropertyChanged(nameof(Configuration));
    }

    [RelayCommand]
    private void AlignElements(ElementAlignment alignment)
    {
        if (SelectedElements.Count == 0) return;

        var oldBounds = SelectedElements.ToDictionary(e => e.Id, e => e.BoundsWithPage);

        if (SelectedElements.Count == 1)
        {
            // Single element: align to canvas
            var element = SelectedElements[0];
            switch (alignment)
            {
                case ElementAlignment.Left:
                    element.X = 0;
                    break;
                case ElementAlignment.Right:
                    element.X = CanvasWidth - element.Width;
                    break;
                case ElementAlignment.Top:
                    element.Y = 0;
                    break;
                case ElementAlignment.Bottom:
                    element.Y = CanvasHeight - element.Height;
                    break;
                case ElementAlignment.CenterH:
                    element.X = (CanvasWidth - element.Width) / 2;
                    break;
                case ElementAlignment.CenterV:
                    element.Y = (CanvasHeight - element.Height) / 2;
                    break;
            }
        }
        else
        {
            // Multiple elements: align to first selected element
            var reference = SelectedElements[0];
            foreach (var element in SelectedElements.Skip(1))
            {
                switch (alignment)
                {
                    case ElementAlignment.Left:
                        element.X = reference.X;
                        break;
                    case ElementAlignment.Right:
                        element.X = reference.X + reference.Width - element.Width;
                        break;
                    case ElementAlignment.Top:
                        element.Y = reference.Y;
                        break;
                    case ElementAlignment.Bottom:
                        element.Y = reference.Y + reference.Height - element.Height;
                        break;
                    case ElementAlignment.CenterH:
                        element.X = reference.X + (reference.Width - element.Width) / 2;
                        break;
                    case ElementAlignment.CenterV:
                        element.Y = reference.Y + (reference.Height - element.Height) / 2;
                        break;
                }
            }
        }

        var newBounds = SelectedElements.ToDictionary(e => e.Id, e => e.BoundsWithPage);
        UndoRedoManager.RecordAction(new BatchMoveResizeAction(Configuration, oldBounds, newBounds, "Align {0}".TranslateFormat(alignment)));
        OnPropertyChanged(nameof(Configuration));
    }

    [RelayCommand]
    private void DistributeElements(DistributeDirection direction)
    {
        if (SelectedElements.Count < 3) return;

        var oldBounds = SelectedElements.ToDictionary(e => e.Id, e => e.BoundsWithPage);

        var sorted = direction == DistributeDirection.Horizontal
            ? SelectedElements.OrderBy(e => e.X).ToList()
            : SelectedElements.OrderBy(e => e.Y).ToList();

        if (direction == DistributeDirection.Horizontal)
        {
            var totalWidth = sorted.Sum(e => e.Width);
            var minX = sorted.First().X;
            var maxX = sorted.Last().X + sorted.Last().Width;
            var spacing = (maxX - minX - totalWidth) / (sorted.Count - 1);

            var currentX = minX;
            foreach (var element in sorted)
            {
                element.X = currentX;
                currentX += element.Width + spacing;
            }
        }
        else
        {
            var totalHeight = sorted.Sum(e => e.Height);
            var minY = sorted.First().Y;
            var maxY = sorted.Last().Y + sorted.Last().Height;
            var spacing = (maxY - minY - totalHeight) / (sorted.Count - 1);

            var currentY = minY;
            foreach (var element in sorted)
            {
                element.Y = currentY;
                currentY += element.Height + spacing;
            }
        }

        var newBounds = SelectedElements.ToDictionary(e => e.Id, e => e.BoundsWithPage);
        UndoRedoManager.RecordAction(new BatchMoveResizeAction(Configuration, oldBounds, newBounds, "Distribute {0}".TranslateFormat(direction.ToString())));
        OnPropertyChanged(nameof(Configuration));
    }

    [RelayCommand]
    private void MatchSize(MatchSizeMode mode)
    {
        if (SelectedElements.Count < 2) return;

        var oldBounds = SelectedElements.ToDictionary(e => e.Id, e => e.BoundsWithPage);
        var reference = SelectedElements[0];

        foreach (var element in SelectedElements.Skip(1))
        {
            switch (mode)
            {
                case MatchSizeMode.Width:
                    element.Width = reference.Width;
                    break;
                case MatchSizeMode.Height:
                    element.Height = reference.Height;
                    break;
                case MatchSizeMode.Both:
                    element.Width = reference.Width;
                    element.Height = reference.Height;
                    break;
            }
        }

        var newBounds = SelectedElements.ToDictionary(e => e.Id, e => e.BoundsWithPage);
        UndoRedoManager.RecordAction(new BatchMoveResizeAction(Configuration, oldBounds, newBounds, "Match {0}".TranslateFormat(mode.ToString())));
        OnPropertyChanged(nameof(Configuration));
    }

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(Controls.Reports.SkiaReportDesignCanvas.MaxZoom, ZoomLevel + 0.1);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(Controls.Reports.SkiaReportDesignCanvas.MinZoom, ZoomLevel - 0.1);
    }

    [RelayCommand]
    private void ZoomFit()
    {
        ZoomLevel = 1.0;
    }

    [RelayCommand]
    private async Task BrowseImagePathAsync()
    {
        if (SelectedElement is not ImageReportElement) return;

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider != null)
        {
            var filters = new[]
            {
                new FilePickerFileType("Image files") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"
                    ]
                },
                new FilePickerFileType("All files") { Patterns = ["*.*"] }
            };

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Image",
                AllowMultiple = false,
                FileTypeFilter = filters
            });

            if (result.Count > 0)
            {
                SelectedImageElement.ImagePath = result[0].Path.LocalPath;
                OnPropertyChanged(nameof(SelectedImageElement));
                OnPropertyChanged(nameof(SelectedImageFileName));
                OnPropertyChanged(nameof(HasSelectedImage));
                OnPropertyChanged(nameof(Configuration));
            }
        }
    }

    [RelayCommand]
    private void RemoveImage()
    {
        if (SelectedElement is not ImageReportElement) return;
        SelectedImageElement.ImagePath = string.Empty;
        OnPropertyChanged(nameof(SelectedImageElement));
        OnPropertyChanged(nameof(SelectedImageFileName));
        OnPropertyChanged(nameof(HasSelectedImage));
        OnPropertyChanged(nameof(Configuration));
    }

    /// <summary>
    /// Returns true if we are editing an existing custom template (not a built-in template).
    /// </summary>
    private bool IsEditingCustomTemplate =>
        !string.IsNullOrEmpty(SelectedTemplateName) &&
        !ReportTemplateFactory.IsBuiltInTemplate(SelectedTemplateName);

    /// <summary>
    /// Saves the current configuration to the current custom template.
    /// </summary>
    private async Task<bool> SaveToCurrentTemplateAsync()
    {
        var success = await _templateStorage.SaveTemplateAsync(Configuration, SelectedTemplateName);
        if (success)
        {
            LoadCustomTemplates();
            // Mark save point so asterisk disappears
            UndoRedoManager.MarkSaved();
            // Show save confirmation message
            ShowSaveConfirmation = true;
            await Task.Delay(2000);
            ShowSaveConfirmation = false;
        }
        else
        {
            var dialog = App.ConfirmationDialog;
            if (dialog != null)
            {
                await dialog.ShowAsync(new ConfirmationDialogOptions
                {
                    Title = "Save Failed".Translate(),
                    Message = "Failed to save the template. Please check that you have write permissions to the templates folder.".Translate(),
                    PrimaryButtonText = "OK".Translate(),
                    SecondaryButtonText = null,
                    CancelButtonText = null
                });
            }
        }
        return success;
    }

    [RelayCommand]
    private async Task OpenSaveTemplate()
    {
        // If editing an existing custom template, save directly without showing modal
        if (IsEditingCustomTemplate)
        {
            if (!HasUnsavedChanges)
            {
                ShowNoChangesMessage = true;
                await Task.Delay(2000);
                ShowNoChangesMessage = false;
                return;
            }

            await SaveToCurrentTemplateAsync();
            return;
        }

        IsSaveTemplateOpen = true;
    }

    [RelayCommand]
    private void CloseSaveTemplate()
    {
        IsSaveTemplateOpen = false;
        _saveTemplateCompletionSource?.TrySetResult(false);
    }

    private TaskCompletionSource<bool>? _saveTemplateCompletionSource;

    /// <summary>
    /// Opens the save template modal and waits for it to close.
    /// Returns true if template was saved successfully, false if cancelled.
    /// </summary>
    public async Task<bool> OpenSaveTemplateAndWaitAsync()
    {
        // If editing an existing custom template, save directly without showing modal
        if (IsEditingCustomTemplate)
        {
            return await SaveToCurrentTemplateAsync();
        }

        _saveTemplateCompletionSource = new TaskCompletionSource<bool>();
        IsSaveTemplateOpen = true;
        return await _saveTemplateCompletionSource.Task;
    }

    #endregion

    #region Step 3 - Preview & Export

    public ObservableCollection<Bitmap> PreviewPageImages { get; } = new();

    [ObservableProperty]
    private ExportFormat _selectedExportFormat = ExportFormat.PDF;

    [ObservableProperty]
    private int _exportQuality = 95;

    [ObservableProperty]
    private string _exportFilePath = string.Empty;

    [ObservableProperty]
    private string _exportFileName = "Report";

    partial void OnExportFilePathChanged(string value)
    {
        // Sync file name from file path (without triggering a loop)
        var nameFromPath = Path.GetFileNameWithoutExtension(value);
        if (!string.IsNullOrEmpty(nameFromPath) && nameFromPath != _exportFileName)
        {
            _exportFileName = nameFromPath;
            OnPropertyChanged(nameof(ExportFileName));
        }
    }

    partial void OnExportFileNameChanged(string value)
    {
        // Sync file path from file name (without triggering a loop)
        if (!string.IsNullOrEmpty(ExportFilePath))
        {
            var dir = Path.GetDirectoryName(ExportFilePath) ?? "";
            var ext = Path.GetExtension(ExportFilePath);
            var newName = string.IsNullOrWhiteSpace(value) ? "Report" : value;
            var newPath = Path.Combine(dir, $"{newName}{ext}");
            if (newPath != _exportFilePath)
            {
                _exportFilePath = newPath;
                OnPropertyChanged(nameof(ExportFilePath));
            }
        }
    }

    [ObservableProperty]
    private bool _openAfterExport = true;

    [ObservableProperty]
    private bool _includeMetadata = true;

    [ObservableProperty]
    private double _previewZoom = 1.0;

    [ObservableProperty]
    private double _previewDisplayWidth = 612;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string? _exportMessage;

    private void GeneratePreview()
    {
        try
        {
            var companyData = App.CompanyManager?.CompanyData;
            var (width, height) = PageDimensions.GetDimensions(Configuration.PageSize, Configuration.PageOrientation);

            // Store display width (original page width for zoom calculations)
            PreviewDisplayWidth = width;

            // Render at 2x resolution for sharper zoom, but display at original size
            const int resolutionMultiplier = 2;
            Configuration.Use24HourFormat = TimeZoneService.Is24HourFormat;
            Configuration.CompanyLogoPath = App.CompanyManager?.CurrentCompanyLogoPath;
            Configuration.MaxPieSlices = ChartSettingsService.GetMaxPieSlices();
            using var renderer = new ReportRenderer(Configuration, companyData, 1f, LanguageServiceTranslationProvider.Instance, App.ErrorLogger);

            // Dispose previous page bitmaps
            foreach (var bmp in PreviewPageImages)
                bmp.Dispose();
            PreviewPageImages.Clear();

            // Compute continuation plan for overflow handling
            renderer.ComputeContinuationPlan();
            var continuationPlan = renderer.GetContinuationPlan();
            var effectivePages = continuationPlan?.Pages ?? [];
            var pageCount = Math.Max(1, effectivePages.Count > 0 ? effectivePages.Count : Configuration.PageCount);

            if (effectivePages.Count > 0)
            {
                foreach (var effectivePage in effectivePages)
                {
                    using var skBitmap = renderer.CreateEffectivePagePreview(effectivePage, width * resolutionMultiplier, height * resolutionMultiplier);
                    var bitmap = ConvertToBitmap(skBitmap);
                    if (bitmap != null)
                        PreviewPageImages.Add(bitmap);
                }
            }
            else
            {
                for (int page = 1; page <= pageCount; page++)
                {
                    using var skBitmap = renderer.CreatePagePreview(page, width * resolutionMultiplier, height * resolutionMultiplier);
                    var bitmap = ConvertToBitmap(skBitmap);
                    if (bitmap != null)
                        PreviewPageImages.Add(bitmap);
                }
            }

            // Request fit-to-window after preview is generated
            PreviewFitToWindowRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.Unknown, "Failed to generate report preview");
            foreach (var bmp in PreviewPageImages)
                bmp.Dispose();
            PreviewPageImages.Clear();
        }
    }

    [RelayCommand]
    private void PreviewZoomIn()
    {
        PreviewZoom = Math.Min(Controls.Reports.SkiaReportDesignCanvas.MaxZoom, PreviewZoom + Controls.Reports.SkiaReportDesignCanvas.ZoomStep);
    }

    [RelayCommand]
    private void PreviewZoomOut()
    {
        PreviewZoom = Math.Max(Controls.Reports.SkiaReportDesignCanvas.MinZoom, PreviewZoom - Controls.Reports.SkiaReportDesignCanvas.ZoomStep);
    }

    [RelayCommand]
    private void PreviewZoomFit()
    {
        PreviewZoom = 1.0;
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportFilePath))
        {
            ExportMessage = "Please specify a file path.";
            return;
        }

        IsExporting = true;
        ExportMessage = null;

        try
        {
            var companyData = App.CompanyManager?.CompanyData;
            Configuration.Use24HourFormat = TimeZoneService.Is24HourFormat;
            Configuration.CompanyLogoPath = App.CompanyManager?.CurrentCompanyLogoPath;
            Configuration.MaxPieSlices = ChartSettingsService.GetMaxPieSlices();
            using var renderer = new ReportRenderer(Configuration, companyData, PageDimensions.RenderScale, LanguageServiceTranslationProvider.Instance, App.ErrorLogger);

            bool success;
            if (SelectedExportFormat == ExportFormat.PDF)
            {
                // PDF handles multi-page internally
                success = await renderer.ExportToPdfAsync(ExportFilePath);
            }
            else
            {
                // Compute continuation plan for multi-page support
                renderer.ComputeContinuationPlan();
                var plan = renderer.GetContinuationPlan();
                var effectivePageCount = renderer.EffectivePageCount;

                if (effectivePageCount > 1)
                {
                    // Multi-page image export: export each page as a separate file
                    var dir = Path.GetDirectoryName(ExportFilePath) ?? "";
                    var baseName = Path.GetFileNameWithoutExtension(ExportFilePath);
                    var ext = Path.GetExtension(ExportFilePath);
                    success = true;

                    if (plan?.Pages.Count > 0)
                    {
                        foreach (var effectivePage in plan.Pages)
                        {
                            var pageFilePath = Path.Combine(dir, $"{baseName}_p{effectivePage.EffectivePageNumber}{ext}");
                            var pageSuccess = await renderer.ExportEffectivePageToImageAsync(pageFilePath, effectivePage, SelectedExportFormat, ExportQuality);
                            if (!pageSuccess) success = false;
                        }
                    }
                    else
                    {
                        for (int page = 1; page <= Configuration.PageCount; page++)
                        {
                            var pageFilePath = Path.Combine(dir, $"{baseName}_p{page}{ext}");
                            var pageSuccess = await renderer.ExportPageToImageAsync(pageFilePath, page, SelectedExportFormat, ExportQuality);
                            if (!pageSuccess) success = false;
                        }
                    }
                }
                else
                {
                    success = await renderer.ExportToImageAsync(ExportFilePath, SelectedExportFormat, ExportQuality);
                }
            }

            if (success)
            {
                _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.ReportGenerated);
                ExportMessage = "Export completed successfully!";

                // Auto-dismiss success message after 5 seconds
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(5000);
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (ExportMessage == "Export completed successfully!")
                                ExportMessage = string.Empty;
                        });
                    }
                    catch (Exception ex)
                    {
                        App.ErrorLogger?.LogError(ex, ErrorCategory.Unknown, "Failed to auto-dismiss export message");
                    }
                });

                // Save export settings for next time
                await SaveExportSettingsAsync();

                if (OpenAfterExport && File.Exists(ExportFilePath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = ExportFilePath,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Ignore if we can't open the file
                    }
                }
            }
            else
            {
                ExportMessage = "Export failed. Please check the file path and try again.";
            }
        }
        catch (Exception ex)
        {
            ExportMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void SelectExportFormat(ExportFormat format)
    {
        SelectedExportFormat = format;
        // Update file extension if a path is already set
        if (!string.IsNullOrEmpty(ExportFilePath))
        {
            var dir = Path.GetDirectoryName(ExportFilePath) ?? "";
            var name = Path.GetFileNameWithoutExtension(ExportFilePath);
            var newExt = format switch
            {
                ExportFormat.PDF => ".pdf",
                ExportFormat.PNG => ".png",
                ExportFormat.JPEG => ".jpg",
                _ => ".pdf"
            };
            ExportFilePath = Path.Combine(dir, $"{name}{newExt}");
        }
    }

    [RelayCommand]
    private async Task BrowseExportPathAsync()
    {
        // Use last export directory, or default to Desktop
        var lastDir = App.SettingsService?.GlobalSettings.ReportExport.LastExportDirectory;
        var defaultPath = !string.IsNullOrEmpty(lastDir) && Directory.Exists(lastDir)
            ? lastDir
            : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider != null)
        {
            var startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(defaultPath);
            var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Export Folder",
                SuggestedStartLocation = startFolder,
                AllowMultiple = false
            });

            if (result is { Count: > 0 })
            {
                var selectedDir = result[0].Path.LocalPath;
                var fileName = string.IsNullOrWhiteSpace(ExportFileName) ? "Report" : ExportFileName;
                var extension = SelectedExportFormat switch
                {
                    ExportFormat.PDF => ".pdf",
                    ExportFormat.PNG => ".png",
                    ExportFormat.JPEG => ".jpg",
                    _ => ".pdf"
                };
                ExportFilePath = Path.Combine(selectedDir, $"{fileName}{extension}");
            }
        }
    }

    private async Task SaveExportSettingsAsync()
    {
        if (App.SettingsService?.GlobalSettings.ReportExport is { } settings)
        {
            settings.LastExportDirectory = Path.GetDirectoryName(ExportFilePath);
            settings.OpenAfterExport = OpenAfterExport;
            settings.IncludeMetadata = IncludeMetadata;
            await App.SettingsService.SaveGlobalSettingsAsync();
        }
    }

    #endregion

    #region Page Settings Properties

    [ObservableProperty]
    private PageSize _pageSize = PageSize.A4;

    [ObservableProperty]
    private PageOrientation _pageOrientation = PageOrientation.Landscape;

    [ObservableProperty]
    private double _marginTop = 40;

    [ObservableProperty]
    private double _marginRight = 40;

    [ObservableProperty]
    private double _marginBottom = 40;

    [ObservableProperty]
    private double _marginLeft = 40;

    [ObservableProperty]
    private bool _showHeader = true;

    [ObservableProperty]
    private bool _showFooter = true;

    [ObservableProperty]
    private bool _showPageNumbers = true;

    [ObservableProperty]
    private bool _showCompanyDetails;

    [ObservableProperty]
    private string _backgroundColor = AppColors.White;

    [ObservableProperty]
    private double _titleFontSize = 18;

    [ObservableProperty]
    private string _pageSettingsDatePreset = DatePresetNames.ThisMonth;

    public ObservableCollection<PageSize> PageSizes { get; } =
        new(Enum.GetValues<PageSize>());

    public ObservableCollection<PageOrientation> Orientations { get; } =
        new(Enum.GetValues<PageOrientation>());

    // Element property enum collections - uses GetDisplayName() extension method for consistent naming
    // Wrapped with Loc.Tr() for translation support
    public ObservableCollection<ChartDataTypeOption> ChartTypeOptions { get; } =
        new(Enum.GetValues<ChartDataType>().Select(t => new ChartDataTypeOption(t, Loc.Tr(t.GetDisplayName()))));

    /// <summary>
    /// Gets or sets the selected chart data type option, syncing with SelectedChartElement.ChartType.
    /// </summary>
    public ChartDataTypeOption? SelectedChartDataTypeOption
    {
        get => SelectedElement is ChartReportElement
            ? ChartTypeOptions.FirstOrDefault(o => o.Value == SelectedChartElement.ChartType)
            : null;
        set
        {
            if (SelectedElement is ChartReportElement && value != null)
            {
                SelectedChartElement.ChartType = value.Value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDistributionChartSelected));
            }
        }
    }

    public ObservableCollection<ChartStyleOption> ChartStyleOptions { get; } =
    [
        new(ReportChartStyle.Bar, "Column"),
        new(ReportChartStyle.Line, "Line"),
        new(ReportChartStyle.StepLine, "Step Line"),
        new(ReportChartStyle.Area, "Area"),
        new(ReportChartStyle.Scatter, "Scatter")
    ];

    public ObservableCollection<ImageScaleMode> ImageScaleModes { get; } =
        new(Enum.GetValues<ImageScaleMode>());

    public ObservableCollection<AccountingReportType> AccountingReportTypes { get; } =
        new(Enum.GetValues<AccountingReportType>());

    public ObservableCollection<TableSortOrder> TableSortOrders { get; } =
        new(Enum.GetValues<TableSortOrder>());

    public ObservableCollection<TransactionType> TransactionTypes { get; } =
        new(Enum.GetValues<TransactionType>());

    public ObservableCollection<HorizontalTextAlignment> HorizontalAlignments { get; } =
        new(Enum.GetValues<HorizontalTextAlignment>());

    public ObservableCollection<VerticalTextAlignment> VerticalAlignments { get; } =
        new(Enum.GetValues<VerticalTextAlignment>());

    public ObservableCollection<string> FontFamilies { get; } =
        ["Segoe UI", "Arial", "Times New Roman", "Calibri", "Courier New", "Georgia", "Verdana", "Trebuchet MS", "DejaVu Sans", "Liberation Sans", "Noto Sans"];

    public ObservableCollection<string> DateFormats { get; } =
        ["MM/dd/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "MMMM d, yyyy", "MMM d, yyyy", "d MMMM yyyy", "d MMM yyyy"];

    // --- Page settings undo/redo helpers ---

    /// <summary>
    /// Captures the current page settings as a snapshot for undo/redo.
    /// </summary>
    private PageSettingsSnapshot CapturePageSettingsSnapshot() => new(
        PageSize, PageOrientation,
        MarginTop, MarginRight, MarginBottom, MarginLeft,
        ShowHeader, ShowFooter, ShowPageNumbers, ShowCompanyDetails,
        BackgroundColor, TitleFontSize, PageSettingsDatePreset);

    /// <summary>
    /// Records a page settings change for undo/redo using coalescing.
    /// Call BEFORE applying the change to Configuration (pass the pre-change snapshot).
    /// </summary>
    private void RecordPageSettingsChange(PageSettingsSnapshot before, PageSettingsSnapshot after)
    {
        if (before != after)
        {
            UndoRedoManager.RecordAction(new PageSettingsChangeAction(
                Configuration, before, after, ApplyPageSettingsSnapshot));
        }
    }

    // Partial methods to update Configuration immediately when page settings change
    partial void OnPageSizeChanged(PageSize oldValue, PageSize newValue)
    {
        var before = CapturePageSettingsSnapshot() with { PageSize = oldValue };
        Configuration.PageSize = newValue;
        UpdateCanvasDimensions();
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnPageOrientationChanged(PageOrientation oldValue, PageOrientation newValue)
    {
        var before = CapturePageSettingsSnapshot() with { PageOrientation = oldValue };
        Configuration.PageOrientation = newValue;
        UpdateCanvasDimensions();
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnMarginTopChanged(double oldValue, double newValue)
    {
        var before = CapturePageSettingsSnapshot() with { MarginTop = oldValue };
        Configuration.PageMargins = new ReportMargins(MarginLeft, newValue, MarginRight, MarginBottom);
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnMarginRightChanged(double oldValue, double newValue)
    {
        var before = CapturePageSettingsSnapshot() with { MarginRight = oldValue };
        Configuration.PageMargins = new ReportMargins(MarginLeft, MarginTop, newValue, MarginBottom);
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnMarginBottomChanged(double oldValue, double newValue)
    {
        var before = CapturePageSettingsSnapshot() with { MarginBottom = oldValue };
        Configuration.PageMargins = new ReportMargins(MarginLeft, MarginTop, MarginRight, newValue);
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnMarginLeftChanged(double oldValue, double newValue)
    {
        var before = CapturePageSettingsSnapshot() with { MarginLeft = oldValue };
        Configuration.PageMargins = new ReportMargins(newValue, MarginTop, MarginRight, MarginBottom);
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnShowHeaderChanged(bool oldValue, bool newValue)
    {
        var before = CapturePageSettingsSnapshot() with { ShowHeader = oldValue };
        Configuration.ShowHeader = newValue;
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnShowFooterChanged(bool oldValue, bool newValue)
    {
        var before = CapturePageSettingsSnapshot() with { ShowFooter = oldValue };
        Configuration.ShowFooter = newValue;
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnShowPageNumbersChanged(bool oldValue, bool newValue)
    {
        var before = CapturePageSettingsSnapshot() with { ShowPageNumbers = oldValue };
        Configuration.ShowPageNumbers = newValue;
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnShowCompanyDetailsChanged(bool oldValue, bool newValue)
    {
        var before = CapturePageSettingsSnapshot() with { ShowCompanyDetails = oldValue };
        Configuration.ShowCompanyDetails = newValue;
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnBackgroundColorChanged(string? oldValue, string newValue)
    {
        var before = CapturePageSettingsSnapshot() with { BackgroundColor = oldValue! };
        Configuration.BackgroundColor = newValue;
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnTitleFontSizeChanged(double oldValue, double newValue)
    {
        var before = CapturePageSettingsSnapshot() with { TitleFontSize = oldValue };
        Configuration.TitleFontSize = newValue;
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    partial void OnPageSettingsDatePresetChanged(string? oldValue, string newValue)
    {
        var before = CapturePageSettingsSnapshot() with { DatePreset = oldValue! };
        Configuration.Filters.DatePresetName = newValue;

        // Sync the step-1 SelectedDatePreset so everything stays consistent
        SelectedDatePreset = newValue;
        foreach (var option in DatePresets)
        {
            option.IsSelected = option.Name == newValue;
        }

        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        RecordPageSettingsChange(before, CapturePageSettingsSnapshot());
    }

    private void ApplyPageSettingsSnapshot(PageSettingsSnapshot s)
    {
        PageSize = s.PageSize;
        PageOrientation = s.PageOrientation;
        MarginTop = s.MarginTop;
        MarginRight = s.MarginRight;
        MarginBottom = s.MarginBottom;
        MarginLeft = s.MarginLeft;
        ShowHeader = s.ShowHeader;
        ShowFooter = s.ShowFooter;
        ShowPageNumbers = s.ShowPageNumbers;
        ShowCompanyDetails = s.ShowCompanyDetails;
        BackgroundColor = s.BackgroundColor;
        TitleFontSize = s.TitleFontSize;
        PageSettingsDatePreset = s.DatePreset;

        SelectedDatePreset = s.DatePreset;
        foreach (var option in DatePresets)
        {
            option.IsSelected = option.Name == s.DatePreset;
        }

        UpdateCanvasDimensions();
        PageSettingsRefreshRequested?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(Configuration));
    }

    #endregion

    #region Save Template Properties

    [ObservableProperty]
    private string _saveTemplateName = string.Empty;

    [ObservableProperty]
    private string? _saveTemplateMessage;

    [RelayCommand]
    private async Task SaveTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(SaveTemplateName))
        {
            SaveTemplateMessage = "Please enter a template name.";
            return;
        }

        var storage = new ReportTemplateStorage();

        if (storage.TemplateExists(SaveTemplateName))
        {
            SaveTemplateMessage = "A template with this name already exists.";
            return;
        }

        var success = await storage.SaveTemplateAsync(Configuration, SaveTemplateName);

        if (success)
        {
            // Close modal immediately
            IsSaveTemplateOpen = false;
            SaveTemplateMessage = null;

            // Update SelectedTemplateName to the new template name so subsequent saves
            // will save to this template directly (without showing the save modal)
            SelectedTemplateName = SaveTemplateName;

            // Refresh custom templates list
            LoadCustomTemplates();

            // Mark save point so asterisk disappears
            UndoRedoManager.MarkSaved();

            // Show the "Saved" overlay notification
            ShowSaveConfirmation = true;
            await Task.Delay(2000);
            ShowSaveConfirmation = false;

            // Signal successful save to any waiting callers
            _saveTemplateCompletionSource?.TrySetResult(true);
        }
        else
        {
            SaveTemplateMessage = "Failed to save template. Please try again.";
        }
    }

    #endregion

    #region Delete Template Properties

    [ObservableProperty]
    private bool _isDeleteTemplateOpen;

    [ObservableProperty]
    private string _templateToDelete = string.Empty;

    [RelayCommand]
    private void OpenDeleteTemplate(string templateName)
    {
        TemplateToDelete = templateName;
        IsDeleteTemplateOpen = true;
    }

    [RelayCommand]
    private void CloseDeleteTemplate()
    {
        IsDeleteTemplateOpen = false;
        TemplateToDelete = string.Empty;
    }

    [RelayCommand]
    private void ConfirmDeleteTemplate()
    {
        if (string.IsNullOrEmpty(TemplateToDelete)) return;

        var success = _templateStorage.DeleteTemplate(TemplateToDelete);
        if (success)
        {
            // If we're deleting the currently selected template, clear selection state
            if (SelectedTemplateName == TemplateToDelete)
            {
                SelectedTemplateName = ReportTemplateFactory.TemplateNames.Custom;

                // Clear date range selection since the selected template was deleted
                foreach (var option in DatePresets)
                    option.IsSelected = false;
                SelectedDatePreset = string.Empty;
                IsCustomDateRange = false;
                CustomStartDate = null;
                CustomEndDate = null;
            }

            // Refresh custom templates list
            LoadCustomTemplates();
        }

        IsDeleteTemplateOpen = false;
        TemplateToDelete = string.Empty;
    }

    #endregion

    #region Rename Template

    [ObservableProperty]
    private bool _isRenameTemplateOpen;

    [ObservableProperty]
    private string _templateToRename = string.Empty;

    [ObservableProperty]
    private string _renameTemplateNewName = string.Empty;

    [RelayCommand]
    private void OpenRenameTemplate(string templateName)
    {
        TemplateToRename = templateName;
        RenameTemplateNewName = templateName;
        IsRenameTemplateOpen = true;
    }

    [RelayCommand]
    private void CloseRenameTemplate()
    {
        IsRenameTemplateOpen = false;
        TemplateToRename = string.Empty;
        RenameTemplateNewName = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmRenameTemplateAsync()
    {
        if (string.IsNullOrEmpty(TemplateToRename) || string.IsNullOrEmpty(RenameTemplateNewName)) return;
        if (TemplateToRename == RenameTemplateNewName)
        {
            CloseRenameTemplate();
            return;
        }

        var success = await _templateStorage.RenameTemplateAsync(TemplateToRename, RenameTemplateNewName);
        if (success)
        {
            // If we're renaming the currently selected template, update the selection
            if (SelectedTemplateName == TemplateToRename)
            {
                SelectedTemplateName = RenameTemplateNewName;
            }

            // Refresh custom templates list
            LoadCustomTemplates();
        }

        IsRenameTemplateOpen = false;
        TemplateToRename = string.Empty;
        RenameTemplateNewName = string.Empty;
    }

    #endregion

    #region Constructor & Initialization

    private readonly ReportTemplateStorage _templateStorage;

    public ReportsPageViewModel() : this(new ReportTemplateStorage())
    {
    }

    /// <summary>
    /// Takes the template store, so tests can keep it away from the user's saved templates.
    /// </summary>
    internal ReportsPageViewModel(ReportTemplateStorage templateStorage)
    {
        _templateStorage = templateStorage;
        UndoRedoViewModel = new UndoRedoButtonGroupViewModel(UndoRedoManager);
        UndoRedoViewModel.ActionPerformed += (_, _) => OnPropertyChanged(nameof(Configuration));

        // Load element panel and grid state from settings
        var uiSettings = App.SettingsService?.GlobalSettings.Ui;
        if (uiSettings != null)
        {
            _isElementPanelExpanded = !uiSettings.ReportsElementPanelCollapsed;
            _showGrid = uiSettings.ReportsShowGrid;
        }

        InitializeCollections();
        LoadTemplate(SelectedTemplateName);
        InitializeExportSettings();

        // Subscribe to language changes to refresh preview with updated translations
        LanguageService.Instance.LanguageChanged += OnLanguageChanged;

        // Subscribe to currency changes to regenerate any open report
        CurrencyService.CurrencyChanged += OnCurrencyChanged;
    }

    /// <summary>
    /// Called when the language changes. Refreshes the preview to update translations.
    /// </summary>
    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        // Notify property changes for any translated properties
        OnPropertyChanged(nameof(CurrentStepTitle));

        // Force ItemsControls to re-render with new translations by notifying collection changes
        OnPropertyChanged(nameof(ReportTemplateOptions));
        OnPropertyChanged(nameof(AccountingTemplateOptions));
        OnPropertyChanged(nameof(DatePresets));

        // Refresh the preview and canvas to show translated content
        GeneratePreview();
        CanvasRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called when the currency changes. Regenerates the preview and refreshes the canvas
    /// so any currently open report reflects the new currency.
    /// </summary>
    private void OnCurrencyChanged(object? sender, EventArgs e)
    {
        GeneratePreview();
        CanvasRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Refreshes the report design canvas (e.g. after company logo changes).
    /// </summary>
    public void RefreshCanvas()
    {
        CanvasRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Cleans up event subscriptions when the view model is no longer needed.
    /// </summary>
    public void Cleanup()
    {
        LanguageService.Instance.LanguageChanged -= OnLanguageChanged;
        CurrencyService.CurrencyChanged -= OnCurrencyChanged;

        foreach (var bmp in PreviewPageImages)
            bmp.Dispose();
        PreviewPageImages.Clear();
    }

    /// <summary>
    /// Saves the element panel state when it changes.
    /// </summary>
    partial void OnIsElementPanelExpandedChanged(bool value)
    {
        var settings = App.SettingsService?.GlobalSettings;
        if (settings != null)
        {
            settings.Ui.ReportsElementPanelCollapsed = !value;
            _ = App.SettingsService?.SaveGlobalSettingsAsync();
        }
    }

    /// <summary>
    /// Saves the grid visibility state when it changes.
    /// </summary>
    partial void OnShowGridChanged(bool value)
    {
        var settings = App.SettingsService?.GlobalSettings;
        if (settings != null)
        {
            settings.Ui.ReportsShowGrid = value;
            _ = App.SettingsService?.SaveGlobalSettingsAsync();
        }
    }

    private void InitializeExportSettings()
    {
        // Load settings from persisted storage
        var settings = App.SettingsService?.GlobalSettings.ReportExport;
        if (settings != null)
        {
            OpenAfterExport = settings.OpenAfterExport;
            IncludeMetadata = settings.IncludeMetadata;
        }

        // Set default export path to Desktop
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var extension = SelectedExportFormat switch
        {
            ExportFormat.PDF => ".pdf",
            ExportFormat.PNG => ".png",
            ExportFormat.JPEG => ".jpg",
            _ => ".pdf"
        };
        var defaultName = string.IsNullOrWhiteSpace(ReportName) ? "Report" : ReportName;
        ExportFilePath = Path.Combine(desktopPath, $"{defaultName}{extension}");
    }

    private void InitializeCollections()
    {
        LoadCustomTemplates();

        foreach (var preset in DatePresetNames.GetAllPresets())
        {
            DatePresets.Add(new DatePresetOption(preset));
        }

        // Load available charts
        InitializeChartOptions();
    }

    private void LoadCustomTemplates()
    {
        CustomTemplateNames.Clear();
        var customNames = _templateStorage.GetSavedTemplateNames();
        foreach (var name in customNames)
        {
            CustomTemplateNames.Add(new CustomTemplateOption(name));
        }
    }

    private void InitializeChartOptions()
    {
        AvailableCharts.Clear();
        ChartCategories.Clear();

        // Define category colors and icons
        const string revenueColor = AppColors.MdBlue;           // Blue
        const string revenueLight = AppColors.MdBlueLight;
        const string expenseColor = AppColors.MdRed;            // Red
        const string expenseLight = AppColors.MdRedLight;
        const string financialColor = AppColors.MdGreen;        // Green
        const string financialLight = AppColors.MdGreenLight;
        const string transactionColor = AppColors.MdOrange;     // Orange
        const string transactionLight = AppColors.MdOrangeLight;
        const string geographicColor = AppColors.MdPurple;      // Purple
        const string geographicLight = AppColors.MdPurpleLight;
        const string accountantColor = AppColors.MdCyan;        // Cyan
        const string accountantLight = AppColors.MdCyanLight;
        const string returnColor = AppColors.MdPink;            // Pink
        const string returnLight = AppColors.MdPinkLight;
        const string lossColor = AppColors.MdBrown;             // Brown
        const string lossLight = AppColors.MdBrownLight;
        const string customerColor = AppColors.MdIndigo;        // Indigo
        const string customerLight = AppColors.MdIndigoLight;

        // Chart type icons: reference centralized Icons class
        const string lineChartIcon = Icons.LineChart;
        const string barChartIcon = Icons.RevenueChart;
        const string pieChartIcon = "M11 2v20c-5.07-.5-9-4.79-9-10s3.93-9.5 9-10zm2.03 0v8.99H22c-.47-4.74-4.24-8.52-8.97-8.99zm0 11.01V22c4.74-.47 8.5-4.25 8.97-8.99h-8.97z";
        const string trendUpIcon = Icons.TrendUp;
        const string compareIcon = "M9.5 3H4C2.9 3 2 3.9 2 5v14c0 1.1.9 2 2 2h5.5V3zm5 0v18H20c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2h-5.5z";
        const string growthIcon = "M7.5 21H2V9h5.5v12zm7.25-18h-5.5v18h5.5V3zM22 11h-5.5v10H22V11z";
        const string transactionIcon = "M20 4H4c-1.11 0-1.99.89-1.99 2L2 18c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V6c0-1.11-.89-2-2-2zm0 14H4v-6h16v6zm0-10H4V6h16v2z";
        const string shippingIcon = "M20 8h-3V4H3c-1.1 0-2 .9-2 2v11h2c0 1.66 1.34 3 3 3s3-1.34 3-3h6c0 1.66 1.34 3 3 3s3-1.34 3-3h2v-5l-3-4zM6 18.5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm13.5-9l1.96 2.5H17V9.5h2.5zm-1.5 9c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5z";
        const string globeIcon = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z";
        const string locationIcon = Icons.MapPin;
        const string buildingIcon = "M12 7V3H2v18h20V7H12zM6 19H4v-2h2v2zm0-4H4v-2h2v2zm0-4H4V9h2v2zm0-4H4V5h2v2zm4 12H8v-2h2v2zm0-4H8v-2h2v2zm0-4H8V9h2v2zm0-4H8V5h2v2zm10 12h-8v-2h2v-2h-2v-2h2v-2h-2V9h8v10zm-2-8h-2v2h2v-2zm0 4h-2v2h2v-2z";
        const string personIcon = "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z";
        const string returnIcon = Icons.Return;
        const string reasonIcon = Icons.WarningCircle;
        const string impactIcon = Icons.DollarSign;
        const string categoryIcon = "M3 3v8h8V3H3zm6 6H5V5h4v4zm-6 4v8h8v-8H3zm6 6H5v-4h4v4zm4-16v8h8V3h-8zm6 6h-4V5h4v4zm-6 4v8h8v-8h-8zm6 6h-4v-4h4v4z";
        const string productIcon = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5V5h14v14zm-7-7l-4 5h8z";
        const string vsIcon = "M12 3C6.5 3 2 6.58 2 11c0 2.13 1.02 4.05 2.67 5.45v4.05l3.58-2.03c1.2.39 2.48.53 3.75.53 5.5 0 10-3.58 10-8s-4.5-8-10-8z";
        const string lossIcon = Icons.CircleCheck;
        const string groupIcon = Icons.Customers;
        const string paymentIcon = "M20 4H4c-1.11 0-1.99.89-1.99 2L2 18c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V6c0-1.11-.89-2-2-2zm0 14H4v-6h16v6zm0-10H4V6h16v2z";
        const string heartIcon = "M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z";
        const string rentalIcon = Icons.Bookmark;

        var revenueCharts = new ObservableCollection<ChartOption>
        {
            new(ChartDataType.TotalRevenue, ChartDataType.TotalRevenue.GetDisplayName(), "Revenue over time", "Revenue", lineChartIcon, revenueColor, revenueLight),
            new(ChartDataType.RevenueDistribution, ChartDataType.RevenueDistribution.GetDisplayName(), "Revenue by category", "Revenue", pieChartIcon, revenueColor, revenueLight)
        };

        var expenseCharts = new ObservableCollection<ChartOption>
        {
            new(ChartDataType.TotalExpenses, ChartDataType.TotalExpenses.GetDisplayName(), "Expenses over time", "Expenses", lineChartIcon, expenseColor, expenseLight),
            new(ChartDataType.ExpensesDistribution, ChartDataType.ExpensesDistribution.GetDisplayName(), "Expenses by category", "Expenses", pieChartIcon, expenseColor, expenseLight)
        };

        var financialCharts = new ObservableCollection<ChartOption>
        {
            new(ChartDataType.TotalProfits, ChartDataType.TotalProfits.GetDisplayName(), "Profit over time", "Financial", trendUpIcon, financialColor, financialLight),
            new(ChartDataType.RevenueVsExpenses, ChartDataType.RevenueVsExpenses.GetDisplayName(), "Compare revenue and costs", "Financial", compareIcon, financialColor, financialLight)
        };

        var transactionCharts = new ObservableCollection<ChartOption>
        {
            new(ChartDataType.AverageTransactionValue, ChartDataType.AverageTransactionValue.GetDisplayName(), "Average transaction amounts", "Transactions", transactionIcon, transactionColor, transactionLight),
            new(ChartDataType.TotalTransactions, ChartDataType.TotalTransactions.GetDisplayName(), "Transaction volume", "Transactions", barChartIcon, transactionColor, transactionLight),
            new(ChartDataType.AverageShippingCosts, ChartDataType.AverageShippingCosts.GetDisplayName(), "Mean shipping cost per period", "Transactions", shippingIcon, transactionColor, transactionLight)
        };

        var geographicCharts = new ObservableCollection<ChartOption>
        {
            new(ChartDataType.WorldMap, ChartDataType.WorldMap.GetDisplayName(), "Sales distribution on a world map", "Geographic", globeIcon, geographicColor, geographicLight),
            new(ChartDataType.CountriesOfOrigin, ChartDataType.CountriesOfOrigin.GetDisplayName(), "Sales by origin country", "Geographic", locationIcon, geographicColor, geographicLight),
            new(ChartDataType.CountriesOfDestination, ChartDataType.CountriesOfDestination.GetDisplayName(), "Sales by destination country", "Geographic", locationIcon, geographicColor, geographicLight),
            new(ChartDataType.CompaniesOfOrigin, ChartDataType.CompaniesOfOrigin.GetDisplayName(), "Sales by supplier company", "Geographic", buildingIcon, geographicColor, geographicLight),
            new(ChartDataType.CompaniesOfDestination, ChartDataType.CompaniesOfDestination.GetDisplayName(), "Sales by destination company", "Geographic", buildingIcon, geographicColor, geographicLight)
        };

        var accountantCharts = new ObservableCollection<ChartOption>
        {
            new(ChartDataType.AccountantsTransactions, ChartDataType.AccountantsTransactions.GetDisplayName(), "Volume of transactions per accountant", "Personnel", personIcon, accountantColor, accountantLight)
        };

        var customerCharts = new ObservableCollection<ChartOption>
        {
            new(ChartDataType.TopCustomersByRevenue, ChartDataType.TopCustomersByRevenue.GetDisplayName(), "Highest revenue customers", "Customers", groupIcon, customerColor, customerLight),
            new(ChartDataType.CustomerPaymentStatus, ChartDataType.CustomerPaymentStatus.GetDisplayName(), "Payment status breakdown", "Customers", paymentIcon, customerColor, customerLight),
            new(ChartDataType.CustomerGrowth, ChartDataType.CustomerGrowth.GetDisplayName(), "Customer acquisition trends", "Customers", growthIcon, customerColor, customerLight),
            new(ChartDataType.CustomerLifetimeValue, ChartDataType.CustomerLifetimeValue.GetDisplayName(), "Average customer value", "Customers", heartIcon, customerColor, customerLight),
            new(ChartDataType.ActiveVsInactiveCustomers, ChartDataType.ActiveVsInactiveCustomers.GetDisplayName(), "Customer activity status", "Customers", groupIcon, customerColor, customerLight),
            new(ChartDataType.RentalsPerCustomer, ChartDataType.RentalsPerCustomer.GetDisplayName(), "Rental frequency by customer", "Customers", rentalIcon, customerColor, customerLight)
        };

        var returnCharts = new ObservableCollection<ChartOption>
        {
            new(ChartDataType.ReturnsOverTime, ChartDataType.ReturnsOverTime.GetDisplayName(), "Return trends", "Returns", returnIcon, returnColor, returnLight),
            new(ChartDataType.ReturnReasons, ChartDataType.ReturnReasons.GetDisplayName(), "Why items are returned", "Returns", reasonIcon, returnColor, returnLight),
            new(ChartDataType.ReturnFinancialImpact, ChartDataType.ReturnFinancialImpact.GetDisplayName(), "Total cost of returns", "Returns", impactIcon, returnColor, returnLight),
            new(ChartDataType.ReturnsByCategory, ChartDataType.ReturnsByCategory.GetDisplayName(), "Return volume grouped by category", "Returns", categoryIcon, returnColor, returnLight),
            new(ChartDataType.ReturnsByProduct, ChartDataType.ReturnsByProduct.GetDisplayName(), "Return volume grouped by product", "Returns", productIcon, returnColor, returnLight),
            new(ChartDataType.ExpenseVsRevenueReturns, ChartDataType.ExpenseVsRevenueReturns.GetDisplayName(), "Compare expense and revenue returns", "Returns", vsIcon, returnColor, returnLight)
        };

        var lossCharts = new ObservableCollection<ChartOption>
        {
            new(ChartDataType.LossesOverTime, ChartDataType.LossesOverTime.GetDisplayName(), "Loss trends", "Losses", lossIcon, lossColor, lossLight),
            new(ChartDataType.LossReasons, ChartDataType.LossReasons.GetDisplayName(), "Why items are lost", "Losses", reasonIcon, lossColor, lossLight),
            new(ChartDataType.LossFinancialImpact, ChartDataType.LossFinancialImpact.GetDisplayName(), "Total cost of losses", "Losses", impactIcon, lossColor, lossLight),
            new(ChartDataType.LossesByCategory, ChartDataType.LossesByCategory.GetDisplayName(), "Loss volume grouped by category", "Losses", categoryIcon, lossColor, lossLight),
            new(ChartDataType.LossesByProduct, ChartDataType.LossesByProduct.GetDisplayName(), "Loss volume grouped by product", "Losses", productIcon, lossColor, lossLight),
            new(ChartDataType.ExpenseVsRevenueLosses, ChartDataType.ExpenseVsRevenueLosses.GetDisplayName(), "Compare expense and revenue losses", "Losses", vsIcon, lossColor, lossLight)
        };

        // Add all charts to AvailableCharts (flat list for backward compatibility)
        foreach (var chart in revenueCharts) AvailableCharts.Add(chart);
        foreach (var chart in expenseCharts) AvailableCharts.Add(chart);
        foreach (var chart in financialCharts) AvailableCharts.Add(chart);
        foreach (var chart in transactionCharts) AvailableCharts.Add(chart);
        foreach (var chart in geographicCharts) AvailableCharts.Add(chart);
        foreach (var chart in accountantCharts) AvailableCharts.Add(chart);
        foreach (var chart in customerCharts) AvailableCharts.Add(chart);
        foreach (var chart in returnCharts) AvailableCharts.Add(chart);
        foreach (var chart in lossCharts) AvailableCharts.Add(chart);

        // Create category groups for grouped display
        ChartCategories.Add(new ChartCategoryGroup("Revenue", revenueCharts, () => OnPropertyChanged(nameof(HasSelectedCharts))));
        ChartCategories.Add(new ChartCategoryGroup("Expenses", expenseCharts, () => OnPropertyChanged(nameof(HasSelectedCharts))));
        ChartCategories.Add(new ChartCategoryGroup("Financial", financialCharts, () => OnPropertyChanged(nameof(HasSelectedCharts))));
        ChartCategories.Add(new ChartCategoryGroup("Transactions", transactionCharts, () => OnPropertyChanged(nameof(HasSelectedCharts))));
        ChartCategories.Add(new ChartCategoryGroup("Geographic", geographicCharts, () => OnPropertyChanged(nameof(HasSelectedCharts))));
        ChartCategories.Add(new ChartCategoryGroup("Personnel", accountantCharts, () => OnPropertyChanged(nameof(HasSelectedCharts))));
        ChartCategories.Add(new ChartCategoryGroup("Customers", customerCharts, () => OnPropertyChanged(nameof(HasSelectedCharts))));
        ChartCategories.Add(new ChartCategoryGroup("Returns", returnCharts, () => OnPropertyChanged(nameof(HasSelectedCharts))));
        ChartCategories.Add(new ChartCategoryGroup("Losses", lossCharts, () => OnPropertyChanged(nameof(HasSelectedCharts))));
    }

    private void LoadTemplate(string templateName)
    {
        if (ReportTemplateFactory.IsBuiltInTemplate(templateName))
        {
            Configuration = ReportTemplateFactory.CreateFromTemplate(templateName);
            UndoRedoManager.SuppressRecording = true;
            try { ApplyConfigurationToPageSettings(); }
            finally { UndoRedoManager.SuppressRecording = false; }
        }
        else
        {
            // Try to load custom template asynchronously
            Task.Run(async () =>
            {
                var config = await _templateStorage.LoadTemplateAsync(templateName);
                // Update configuration and page settings on UI thread
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (config == null)
                    {
                        // Show error message if template couldn't be loaded
                        var dialog = App.ConfirmationDialog;
                        if (dialog != null)
                        {
                            await dialog.ShowAsync(new ConfirmationDialogOptions
                            {
                                Title = "Load Failed".Translate(),
                                Message = "Failed to load the template '{0}'. The file may be corrupted or missing.".TranslateFormat(templateName),
                                PrimaryButtonText = "OK".Translate(),
                                SecondaryButtonText = null,
                                CancelButtonText = null
                            });
                        }
                    }
                    Configuration = config ?? new ReportConfiguration();
                    UndoRedoManager.SuppressRecording = true;
                    try { ApplyConfigurationToPageSettings(); }
                    finally { UndoRedoManager.SuppressRecording = false; }
                    // Fire event to refresh canvas after template loads
                    TemplateLoaded?.Invoke(this, EventArgs.Empty);
                });
            });
        }
    }

    /// <summary>
    /// Applies the current configuration settings to the page setting properties.
    /// </summary>
    private void ApplyConfigurationToPageSettings()
    {
        // Update report name from configuration
        if (!string.IsNullOrEmpty(Configuration.Title))
        {
            ReportName = Configuration.Title;
        }

        // Update page settings from configuration
        PageSize = Configuration.PageSize;
        PageOrientation = Configuration.PageOrientation;
        MarginTop = Configuration.PageMargins.Top;
        MarginRight = Configuration.PageMargins.Right;
        MarginBottom = Configuration.PageMargins.Bottom;
        MarginLeft = Configuration.PageMargins.Left;
        ShowHeader = Configuration.ShowHeader;
        ShowFooter = Configuration.ShowFooter;
        ShowPageNumbers = Configuration.ShowPageNumbers;
        ShowCompanyDetails = Configuration.ShowCompanyDetails;
        BackgroundColor = Configuration.BackgroundColor;
        TitleFontSize = Configuration.TitleFontSize;

        // Check if we need to restore a preserved date preset (when going back from step 2)
        if (_datePresetToPreserve != null)
        {
            SelectedDatePreset = _datePresetToPreserve;
            PageSettingsDatePreset = _datePresetToPreserve;

            // Update IsSelected on all date preset options
            foreach (var option in DatePresets)
            {
                option.IsSelected = option.Name == _datePresetToPreserve;
            }

            _datePresetToPreserve = null;
        }
        else if (IsDateRangeEnabled &&
                 (string.IsNullOrEmpty(SelectedDatePreset) || !DatePresets.Any(o => o.IsSelected)))
        {
            // Only set a date preset if the user hasn't already selected one.
            // Default to "Last 30 days" when no selection exists.
            var defaultPreset = DatePresetNames.Last30Days;
            SelectedDatePreset = defaultPreset;
            PageSettingsDatePreset = defaultPreset;

            foreach (var option in DatePresets)
            {
                option.IsSelected = option.Name == defaultPreset;
            }
        }

        SelectedTransactionType = Configuration.Filters.TransactionType;

        // Check if we need to restore preserved chart selections (when going back from step 2)
        if (_chartTypesToPreserve != null)
        {
            RestoreChartSelections(_chartTypesToPreserve);
            _chartTypesToPreserve = null;
        }
        else
        {
            // Update selected charts - reset all and mark selected ones from configuration
            foreach (var chart in AvailableCharts)
            {
                chart.IsSelected = Configuration.Filters.SelectedChartTypes.Contains(chart.ChartType);
            }
        }

        UpdateCanvasDimensions();
        UndoRedoManager.Clear();
        OnPropertyChanged(nameof(Configuration));

        // Notify view to fit canvas to window
        TemplateLoaded?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDateRangeToConfiguration()
    {
        Configuration.Filters.DatePresetName = SelectedDatePreset;

        if (IsCustomDateRange)
        {
            Configuration.Filters.StartDate = CustomStartDate?.DateTime;
            Configuration.Filters.EndDate = CustomEndDate?.DateTime;
        }
        else if (!string.IsNullOrEmpty(SelectedDatePreset))
        {
            var (start, end) = DatePresetNames.GetDateRange(SelectedDatePreset, App.CompanyManager?.CompanyData?.GetEarliestDate());
            Configuration.Filters.StartDate = start;
            Configuration.Filters.EndDate = end;
        }
    }

    private void ApplyFiltersToConfiguration()
    {
        // Custom (charts) tab always uses landscape orientation
        if (IsChartsTabSelected)
        {
            PageOrientation = PageOrientation.Landscape;
            Configuration.PageOrientation = PageOrientation.Landscape;
        }

        Configuration.Title = ReportName;
        Configuration.Filters.TransactionType = SelectedTransactionType;

        // Set date format from DateFormatService for consistent X-axis labeling
        Configuration.Filters.DateFormat = DateFormatService.GetCurrentDotNetFormat();

        ApplyDateRangeToConfiguration();

        // Page-settings undo snapshots carry the preset. Left at the template's, undoing any page
        // setting in the designer would switch the report back to that range.
        UndoRedoManager.SuppressRecording = true;
        try { PageSettingsDatePreset = SelectedDatePreset; }
        finally { UndoRedoManager.SuppressRecording = false; }

        Configuration.Filters.SelectedChartTypes.Clear();
        foreach (var chart in AvailableCharts.Where(c => c.IsSelected))
        {
            Configuration.Filters.SelectedChartTypes.Add(chart.ChartType);
        }

        // Sync chart elements with selection (add new, remove deselected)
        SyncChartElementsWithSelection();

        // Notify view to sync canvas with new elements
        OnPropertyChanged(nameof(Configuration));
    }

    /// <summary>
    /// Syncs ChartReportElement objects with the selected chart types.
    /// Creates elements for newly selected charts, removes elements for deselected charts,
    /// and rearranges all chart elements in a grid layout.
    /// </summary>
    private void SyncChartElementsWithSelection()
    {
        // When user is on the Custom (charts) tab, remove all non-chart elements
        // that may have been added by a previously selected template (summary cards,
        // accounting tables, labels, images, etc.). This mode is chart-only.
        if (IsChartsTabSelected)
        {
            foreach (var element in Configuration.Elements
                         .Where(e => e is not ChartReportElement && e is not DateRangeReportElement)
                         .ToList())
            {
                Configuration.RemoveElement(element.Id);
            }
        }

        var selectedChartTypes = Configuration.Filters.SelectedChartTypes.ToHashSet();

        var existingChartElements = Configuration.Elements
            .OfType<ChartReportElement>()
            .ToList();

        // Remove chart elements for deselected charts
        foreach (var chartElement in existingChartElements)
        {
            if (!selectedChartTypes.Contains(chartElement.ChartType))
            {
                Configuration.RemoveElement(chartElement.Id);
            }
        }

        // Get chart types that already have elements (after removal)
        var existingChartTypes = Configuration.Elements
            .OfType<ChartReportElement>()
            .Select(e => e.ChartType)
            .ToHashSet();

        // Add new chart elements for selected charts that don't have elements yet
        foreach (var chartType in Configuration.Filters.SelectedChartTypes)
        {
            if (!existingChartTypes.Contains(chartType))
            {
                Configuration.AddElement(new ChartReportElement
                {
                    ChartType = chartType,
                    X = 0,
                    Y = 0,
                    Width = 200,
                    Height = 150
                });
            }
        }

        // Rearrange all chart elements in a grid layout
        RearrangeChartElements();
    }

    /// <summary>
    /// Rearranges all chart elements in a grid layout.
    /// Only rearranges if HasManualChartLayout is false (no manual positioning has been done).
    /// </summary>
    private void RearrangeChartElements()
    {
        // Skip rearranging if user has manually positioned elements
        if (Configuration.HasManualChartLayout)
            return;

        var chartElements = Configuration.Elements
            .OfType<ChartReportElement>()
            .ToList();

        if (chartElements.Count == 0)
            return;

        // Use LayoutContext for consistent positioning that accounts for
        // top margin, date range space, and footer/bottom margin
        var context = new TemplateLayoutHelper.LayoutContext(Configuration);
        const double spacing = 10;

        // Determine grid dimensions based on number of charts
        var chartCount = chartElements.Count;
        int columns = chartCount <= 2 ? chartCount : (chartCount <= 4 ? 2 : 3);
        int rows = (int)Math.Ceiling((double)chartCount / columns);

        var cellWidth = (context.ContentWidth - (spacing * (columns - 1))) / columns;
        var cellHeight = (context.ContentHeight - (spacing * (rows - 1))) / rows;

        // Cap cell height so 1-2 charts don't stretch to fill the full page.
        // Use the same height as a 2-row layout (like 4 charts in a 2×2 grid).
        var maxCellHeight = (context.ContentHeight - spacing) / 2;
        cellHeight = Math.Min(cellHeight, maxCellHeight);

        // Position each chart element in the grid below date range area
        for (int i = 0; i < chartElements.Count; i++)
        {
            int row = i / columns;
            int col = i % columns;

            chartElements[i].X = context.Margin + (col * (cellWidth + spacing));
            chartElements[i].Y = context.ContentTop + (row * (cellHeight + spacing));
            chartElements[i].Width = cellWidth;
            chartElements[i].Height = cellHeight;
        }
    }

    private static Bitmap? ConvertToBitmap(SKBitmap skBitmap)
    {
        try
        {
            using var image = SKImage.FromBitmap(skBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Position = 0;
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    #endregion
}

/// <summary>
/// Represents a chart option for selection with category and icon information.
/// </summary>
public partial class ChartOption(
    ChartDataType chartType,
    string name,
    string description,
    string category,
    string iconData,
    string iconForeground,
    string iconBackground)
    : ObservableObject
{
    public ChartDataType ChartType { get; } = chartType;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string Category { get; } = category;
    public string IconData { get; } = iconData;
    public string IconForeground { get; } = iconForeground;
    public string IconBackground { get; } = iconBackground;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Represents a category group of chart options with "Select All" functionality.
/// </summary>
public class ChartCategoryGroup : ObservableObject
{
    public ChartCategoryGroup(string name, ObservableCollection<ChartOption> charts, Action onSelectionChanged)
    {
        Name = name;
        Charts = charts;
        var onSelectionChanged1 = onSelectionChanged;

        // Subscribe to each chart's selection changes
        foreach (var chart in Charts)
        {
            chart.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChartOption.IsSelected))
                {
                    OnPropertyChanged(nameof(IsAllSelected));
                    OnPropertyChanged(nameof(IsSomeSelected));
                    onSelectionChanged1.Invoke();
                }
            };
        }
    }

    public string Name { get; }
    public ObservableCollection<ChartOption> Charts { get; }

    /// <summary>
    /// Gets whether all charts in this category are selected.
    /// </summary>
    public bool IsAllSelected => Charts.All(c => c.IsSelected);

    /// <summary>
    /// Gets whether some (but not all) charts in this category are selected.
    /// </summary>
    public bool IsSomeSelected => Charts.Any(c => c.IsSelected) && !IsAllSelected;

    /// <summary>
    /// Toggles selection of all charts in this category.
    /// </summary>
    public void ToggleSelectAll()
    {
        var newState = !IsAllSelected;
        foreach (var chart in Charts)
        {
            chart.IsSelected = newState;
        }
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(IsSomeSelected));
    }
}

/// <summary>
/// Represents a date preset option for selection.
/// </summary>
public partial class DatePresetOption(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Represents a report template option for the Step 1 template grid.
/// </summary>
public partial class ReportTemplateOption(
    string templateName,
    string displayName,
    string description,
    string iconData,
    string iconForeground,
    string iconBackground)
    : ObservableObject
{
    public string TemplateName { get; } = templateName;
    public string DisplayName { get; } = displayName;
    public string Description { get; } = description;
    public string IconData { get; } = iconData;
    public string IconForeground { get; } = iconForeground;
    public string IconBackground { get; } = iconBackground;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Represents a custom template option for the Step 1 template grid.
/// </summary>
public partial class CustomTemplateOption(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Represents a chart style option with a display name.
/// </summary>
public record ChartStyleOption(ReportChartStyle Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Represents a chart data type option with a display name.
/// </summary>
public record ChartDataTypeOption(ChartDataType Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}
