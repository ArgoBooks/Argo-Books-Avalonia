using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Models.Reports;

/// <summary>
/// Complete configuration for a report including layout, data filters, and settings.
/// </summary>
public class ReportConfiguration
{
    /// <summary>
    /// Unique identifier for the configuration.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Report title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "Untitled Report";

    /// <summary>
    /// Page size setting.
    /// </summary>
    [JsonPropertyName("pageSize")]
    public PageSize PageSize { get; set; } = PageSize.A4;

    /// <summary>
    /// Page orientation.
    /// </summary>
    [JsonPropertyName("pageOrientation")]
    public PageOrientation PageOrientation { get; set; } = PageOrientation.Landscape;

    /// <summary>
    /// Page margins in pixels.
    /// </summary>
    [JsonPropertyName("pageMargins")]
    public ReportMargins PageMargins { get; set; } = new();

    /// <summary>
    /// Background color for the report (hex format).
    /// </summary>
    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = AppColors.White;

    /// <summary>
    /// Number of pages in the report.
    /// </summary>
    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; } = 1;

    /// <summary>
    /// Whether to show page numbers.
    /// </summary>
    [JsonPropertyName("showPageNumbers")]
    public bool ShowPageNumbers { get; set; } = true;

    /// <summary>
    /// Whether to show header with title.
    /// </summary>
    [JsonPropertyName("showHeader")]
    public bool ShowHeader { get; set; } = true;

    /// <summary>
    /// Whether to show footer with date/time.
    /// </summary>
    [JsonPropertyName("showFooter")]
    public bool ShowFooter { get; set; } = true;

    /// <summary>
    /// Whether to show company details (logo, name, address, phone, email) in the report header.
    /// Standard for formal accounting documents like balance sheets, income statements, etc.
    /// </summary>
    [JsonPropertyName("showCompanyDetails")]
    public bool ShowCompanyDetails { get; set; }

    /// <summary>
    /// Font size for the page title in the header (in points).
    /// </summary>
    [JsonPropertyName("titleFontSize")]
    public double TitleFontSize { get; set; } = 18;

    /// <summary>
    /// Data filtering configuration.
    /// </summary>
    [JsonPropertyName("filters")]
    public ReportFilters Filters { get; set; } = new();

    /// <summary>
    /// List of all elements in the report.
    /// </summary>
    [JsonPropertyName("elements")]
    public List<ReportElementBase> Elements { get; set; } = [];

    /// <summary>
    /// Indicates whether the user has manually moved or resized any charts.
    /// </summary>
    [JsonPropertyName("hasManualChartLayout")]
    public bool HasManualChartLayout { get; set; }

    /// <summary>
    /// Current page number for display in footer (runtime only).
    /// </summary>
    [JsonIgnore]
    public int CurrentPageNumber { get; set; } = 1;

    /// <summary>
    /// Total page count for display in footer (runtime only).
    /// Set during rendering to enable "Page X of Y" display.
    /// </summary>
    [JsonIgnore]
    public int TotalPageCount { get; set; } = 1;

    /// <summary>
    /// Whether to use 24-hour time format in the report footer.
    /// This is a runtime setting that is not persisted.
    /// </summary>
    [JsonIgnore]
    public bool Use24HourFormat { get; set; }

    /// <summary>
    /// Full file path to the company logo image.
    /// Runtime-only, resolved from CompanyManager's temp directory.
    /// </summary>
    [JsonIgnore]
    public string? CompanyLogoPath { get; set; }

    /// <summary>
    /// Maximum number of pie chart slices before grouping into "Other".
    /// Runtime-only, sourced from global settings.
    /// </summary>
    [JsonIgnore]
    public int MaxPieSlices { get; set; } = 6;

    /// <summary>
    /// Gets elements sorted by Z-order (for rendering).
    /// </summary>
    public List<ReportElementBase> GetElementsByZOrder()
    {
        return Elements.OrderBy(e => e.ZOrder).ToList();
    }

    /// <summary>
    /// Gets elements for a specific page, sorted by Z-order (for rendering).
    /// </summary>
    public List<ReportElementBase> GetElementsByZOrderForPage(int pageNumber)
    {
        return Elements.Where(e => e.PageNumber == pageNumber).OrderBy(e => e.ZOrder).ToList();
    }

    /// <summary>
    /// Adds an element to the report.
    /// </summary>
    public void AddElement(ReportElementBase element)
    {
        element.ZOrder = Elements.Count != 0 ? Elements.Max(e => e.ZOrder) + 1 : 0;
        Elements.Add(element);
    }

    /// <summary>
    /// Removes an element from the report.
    /// </summary>
    public bool RemoveElement(string elementId)
    {
        var element = Elements.FirstOrDefault(e => e.Id == elementId);
        if (element != null)
        {
            Elements.Remove(element);
            return true;
        }
        return false;
    }

    public ReportElementBase? GetElementById(string elementId)
    {
        return Elements.FirstOrDefault(e => e.Id == elementId);
    }

    /// <summary>
    /// Gets all elements of a specific type.
    /// </summary>
    public List<T> GetElementsOfType<T>() where T : ReportElementBase
    {
        return Elements.OfType<T>().ToList();
    }

    /// <summary>
    /// Creates a deep clone of this configuration.
    /// </summary>
    public ReportConfiguration Clone()
    {
        return new ReportConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Title = Title,
            PageCount = PageCount,
            PageSize = PageSize,
            PageOrientation = PageOrientation,
            PageMargins = new ReportMargins
            {
                Left = PageMargins.Left,
                Top = PageMargins.Top,
                Right = PageMargins.Right,
                Bottom = PageMargins.Bottom
            },
            BackgroundColor = BackgroundColor,
            ShowPageNumbers = ShowPageNumbers,
            ShowHeader = ShowHeader,
            ShowFooter = ShowFooter,
            ShowCompanyDetails = ShowCompanyDetails,
            TitleFontSize = TitleFontSize,
            Filters = new ReportFilters
            {
                StartDate = Filters.StartDate,
                EndDate = Filters.EndDate,
                TransactionType = Filters.TransactionType,
                IncludeReturns = Filters.IncludeReturns,
                IncludeLosses = Filters.IncludeLosses,
                DatePresetName = Filters.DatePresetName,
                SelectedChartTypes = [.. Filters.SelectedChartTypes]
            },
            HasManualChartLayout = HasManualChartLayout,
            Use24HourFormat = Use24HourFormat,
            Elements = Elements.Select(e => e.Clone()).ToList()
        };
    }
}

/// <summary>
/// Page margins specification.
/// </summary>
public class ReportMargins
{
    [JsonPropertyName("left")]
    public double Left { get; set; } = 40;

    [JsonPropertyName("top")]
    public double Top { get; set; } = 40;

    [JsonPropertyName("right")]
    public double Right { get; set; } = 40;

    [JsonPropertyName("bottom")]
    public double Bottom { get; set; } = 40;

    public ReportMargins() { }

    public ReportMargins(double allSides)
    {
        Left = Top = Right = Bottom = allSides;
    }

    public ReportMargins(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }
}

/// <summary>
/// Data filtering options for report generation.
/// </summary>
public class ReportFilters
{
    /// <summary>
    /// Start date for data filtering.
    /// </summary>
    [JsonPropertyName("startDate")]
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// End date for data filtering.
    /// </summary>
    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Transaction types to include.
    /// </summary>
    [JsonPropertyName("transactionType")]
    public TransactionType TransactionType { get; set; } = TransactionType.Revenue;

    /// <summary>
    /// Selected chart types to include.
    /// </summary>
    [JsonPropertyName("selectedChartTypes")]
    public List<ChartDataType> SelectedChartTypes { get; set; } = [];

    /// <summary>
    /// Whether to include returned items.
    /// </summary>
    [JsonPropertyName("includeReturns")]
    public bool IncludeReturns { get; set; } = true;

    /// <summary>
    /// Whether to include lost items.
    /// </summary>
    [JsonPropertyName("includeLosses")]
    public bool IncludeLosses { get; set; } = true;

    /// <summary>
    /// The name of the date preset to use (e.g., "This month", "Last quarter").
    /// If null or empty, uses StartDate and EndDate.
    /// </summary>
    [JsonPropertyName("datePresetName")]
    public string? DatePresetName { get; set; }

    /// <summary>
    /// The date format to use for X-axis labels (e.g., "MMM d, yyyy", "MM/dd/yyyy").
    /// Defaults to "MMM yyyy" for month-year format on chart X-axes.
    /// </summary>
    [JsonPropertyName("dateFormat")]
    public string DateFormat { get; set; } = "MMM yyyy";

    /// <summary>
    /// Checks if the preset name indicates a custom range.
    /// </summary>
    public static bool IsCustomRange(string? presetName)
    {
        if (string.IsNullOrEmpty(presetName))
            return true;

        var lower = presetName.ToLowerInvariant();
        return lower is "custom" or "custom range";
    }

    /// <summary>
    /// Gets the date range based on filters, with consistent end-date normalization.
    /// Used by both chart and table data services to ensure identical date ranges.
    /// </summary>
    /// <param name="earliestDate">Optional earliest transaction date for "All Time". Defaults to today if null.</param>
    public (DateTime Start, DateTime End) GetDateRange(DateTime? earliestDate = null)
    {
        // For custom ranges, use the explicit start/end dates from filters
        if (string.IsNullOrEmpty(DatePresetName) || IsCustomRange(DatePresetName))
        {
            var start = StartDate?.Date ?? DateTime.MinValue;
            // Normalize end date to include the entire day (end of day)
            var end = EndDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.MaxValue;
            return (start, end);
        }

        // For preset names, calculate the date range
        return DatePresetNames.GetDateRange(DatePresetName, earliestDate);
    }
}

/// <summary>
/// Export settings for report output.
/// </summary>
public class ExportSettings
{
    /// <summary>
    /// Export format.
    /// </summary>
    [JsonPropertyName("format")]
    public ExportFormat Format { get; set; } = ExportFormat.PDF;

    /// <summary>
    /// Output file path.
    /// </summary>
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Quality setting (0-100).
    /// </summary>
    [JsonPropertyName("quality")]
    public int Quality { get; set; } = 95;

    /// <summary>
    /// Whether to open the file after export.
    /// </summary>
    [JsonPropertyName("openAfterExport")]
    public bool OpenAfterExport { get; set; } = true;

    /// <summary>
    /// Whether to include metadata in the export.
    /// </summary>
    [JsonPropertyName("includeMetadata")]
    public bool IncludeMetadata { get; set; } = true;
}

/// <summary>
/// Page size dimensions helper.
/// </summary>
public static class PageDimensions
{
    /// <summary>
    /// Header height in pixels (matches LayoutContext in ReportTemplateFactory).
    /// </summary>
    public const int HeaderHeight = 80;

    /// <summary>
    /// Header height in pixels when company details are shown.
    /// Accommodates logo, company name, address, and report title.
    /// </summary>
    public const int CompanyDetailsHeaderHeight = 90;

    /// <summary>
    /// Footer height in pixels (matches LayoutContext in ReportTemplateFactory).
    /// </summary>
    public const int FooterHeight = 50;

    /// <summary>
    /// Margin from page edges in pixels.
    /// </summary>
    public const int Margin = 40;

    /// <summary>
    /// Render scale for high-quality export.
    /// </summary>
    public const float RenderScale = 3f;

    /// <summary>
    /// Gets the effective header height based on whether company details are shown.
    /// </summary>
    public static int GetHeaderHeight(bool showCompanyDetails) =>
        showCompanyDetails ? CompanyDetailsHeaderHeight : HeaderHeight;

    /// <summary>
    /// Gets the dimensions for a page size in pixels (96 DPI).
    /// </summary>
    public static (int Width, int Height) GetDimensions(PageSize pageSize, PageOrientation orientation)
    {
        var (width, height) = pageSize switch
        {
            PageSize.A4 => (794, 1123),      // 210 × 297 mm
            PageSize.Letter => (816, 1056),  // 8.5 × 11 inches
            PageSize.Legal => (816, 1344),   // 8.5 × 14 inches
            PageSize.Tabloid => (1122, 1632),// 11 × 17 inches
            PageSize.A3 => (1123, 1587),     // 297 × 420 mm
            _ => (794, 1123)                 // Default to A4
        };

        // Swap dimensions for landscape
        if (orientation == PageOrientation.Landscape)
        {
            return (height, width);
        }

        return (width, height);
    }
}

/// <summary>
/// Date preset names for common date ranges.
/// </summary>
public static class DatePresetNames
{
    public const string Today = "Today";
    public const string Yesterday = "Yesterday";
    public const string Last7Days = "Last 7 Days";
    public const string Last30Days = "Last 30 Days";
    public const string Last100Days = "Last 100 Days";
    public const string Last365Days = "Last 365 Days";
    public const string ThisWeek = "This Week";
    public const string LastWeek = "Last Week";
    public const string ThisMonth = "This Month";
    public const string LastMonth = "Last Month";
    public const string ThisQuarter = "This Quarter";
    public const string LastQuarter = "Last Quarter";
    public const string YearToDate = "This Year";
    public const string LastYear = "Last Year";
    public const string AllTime = "All Time";
    public const string Custom = "Custom";

    // Future date range presets (for insights/forecasting)
    public const string NextMonth = "Next Month";
    public const string NextQuarter = "Next Quarter";
    public const string NextYear = "Next Year";
    public const string NextMonthToDate = "Next Month to Date";
    public const string NextQuarterToDate = "Next Quarter to Date";
    public const string NextYearToDate = "Next Year to Date";

    /// <summary>
    /// Gets the date range for a preset name.
    /// Handles case-insensitive matching to support both constant values and UI display values.
    /// </summary>
    /// <param name="presetName">The preset name (e.g., "This Month", "All Time").</param>
    /// <param name="earliestDate">Optional earliest transaction date for "All Time". Defaults to today if null.</param>
    public static (DateTime Start, DateTime End) GetDateRange(string presetName, DateTime? earliestDate = null)
    {
        var now = DateTime.Now;
        var today = now.Date;

        // Normalize to lowercase for case-insensitive comparison
        var presetLower = presetName.ToLowerInvariant();

        // This week, month, quarter and year run to the end of today, as on the dashboard (Calculations.md §12).
        return presetLower switch
        {
            "today" => (today, today.AddDays(1).AddTicks(-1)),
            "yesterday" => (today.AddDays(-1), today.AddTicks(-1)),
            "last 7 days" => (today.AddDays(-6), today.AddDays(1).AddTicks(-1)),
            "last 30 days" => (today.AddDays(-29), today.AddDays(1).AddTicks(-1)),
            "last 100 days" => (today.AddDays(-99), today.AddDays(1).AddTicks(-1)),
            "last 365 days" => (today.AddDays(-364), today.AddDays(1).AddTicks(-1)),
            "this week" => (today.AddDays(-(int)today.DayOfWeek), today.AddDays(1).AddTicks(-1)),
            "last week" => (today.AddDays(-(int)today.DayOfWeek - 7), today.AddDays(-(int)today.DayOfWeek).AddTicks(-1)),
            "this month" => (new DateTime(now.Year, now.Month, 1), today.AddDays(1).AddTicks(-1)),
            "last month" => (new DateTime(now.Year, now.Month, 1).AddMonths(-1), new DateTime(now.Year, now.Month, 1).AddTicks(-1)),
            "last 3 months" => (today.AddMonths(-3), today.AddDays(1).AddTicks(-1)),
            "last 6 months" => (today.AddMonths(-6), today.AddDays(1).AddTicks(-1)),
            "this quarter" => GetThisQuarterRange(now),
            "last quarter" => GetLastQuarterRange(now),
            "this year" => (new DateTime(now.Year, 1, 1), today.AddDays(1).AddTicks(-1)),
            "last year" => (new DateTime(now.Year - 1, 1, 1), new DateTime(now.Year, 1, 1).AddTicks(-1)),
            "all time" => (earliestDate ?? today, today.AddDays(1).AddTicks(-1)),

            // Future date presets for forecasting
            "next month" => GetNextMonthRange(now),
            "next quarter" => GetNextQuarterRange(now),
            "next year" => GetNextYearRange(now),
            "next 30 days" => GetNextMonthToDateRange(now),
            "next 90 days" => GetNextQuarterToDateRange(now),
            "next 365 days" => GetNextYearToDateRange(now),
            // Also handle the constant form (e.g., "Next month to date")
            "next month to date" => GetNextMonthToDateRange(now),
            "next quarter to date" => GetNextQuarterToDateRange(now),
            "next year to date" => GetNextYearToDateRange(now),

            _ => (today.AddDays(-29), today.AddDays(1).AddTicks(-1)) // Default to last 30 days
        };
    }

    private static (DateTime Start, DateTime End) GetThisQuarterRange(DateTime now)
    {
        int quarter = (now.Month - 1) / 3;
        var start = new DateTime(now.Year, quarter * 3 + 1, 1);
        var end = now.Date.AddDays(1).AddTicks(-1);
        return (start, end);
    }

    private static (DateTime Start, DateTime End) GetLastQuarterRange(DateTime now)
    {
        int quarter = (now.Month - 1) / 3;
        var thisQuarterStart = new DateTime(now.Year, quarter * 3 + 1, 1);
        var start = thisQuarterStart.AddMonths(-3);
        var end = thisQuarterStart.AddTicks(-1);
        return (start, end);
    }

    private static (DateTime Start, DateTime End) GetNextMonthRange(DateTime now)
    {
        var nextMonth = now.AddMonths(1);
        var start = new DateTime(nextMonth.Year, nextMonth.Month, 1);
        var end = start.AddMonths(1).AddTicks(-1);
        return (start, end);
    }

    private static (DateTime Start, DateTime End) GetNextQuarterRange(DateTime now)
    {
        int quarter = (now.Month - 1) / 3;
        var thisQuarterStart = new DateTime(now.Year, quarter * 3 + 1, 1);
        var nextQuarterStart = thisQuarterStart.AddMonths(3);
        var end = nextQuarterStart.AddMonths(3).AddTicks(-1);
        return (nextQuarterStart, end);
    }

    private static (DateTime Start, DateTime End) GetNextYearRange(DateTime now)
    {
        var start = new DateTime(now.Year + 1, 1, 1);
        var end = new DateTime(now.Year + 2, 1, 1).AddTicks(-1);
        return (start, end);
    }

    private static (DateTime Start, DateTime End) GetNextMonthToDateRange(DateTime now)
    {
        // From tomorrow to 30 days from now
        var start = now.Date.AddDays(1);
        var end = now.Date.AddDays(30).AddDays(1).AddTicks(-1);
        return (start, end);
    }

    private static (DateTime Start, DateTime End) GetNextQuarterToDateRange(DateTime now)
    {
        // From tomorrow to 90 days from now
        var start = now.Date.AddDays(1);
        var end = now.Date.AddDays(90).AddDays(1).AddTicks(-1);
        return (start, end);
    }

    private static (DateTime Start, DateTime End) GetNextYearToDateRange(DateTime now)
    {
        // From tomorrow to 365 days from now
        var start = now.Date.AddDays(1);
        var end = now.Date.AddDays(365).AddDays(1).AddTicks(-1);
        return (start, end);
    }

    /// <summary>
    /// Gets all available preset names.
    /// </summary>
    public static string[] GetAllPresets()
    {
        return [Today, Yesterday, Last7Days, Last30Days, ThisWeek, LastWeek,
                ThisMonth, LastMonth, ThisQuarter, LastQuarter, YearToDate, LastYear, AllTime, Custom];
    }

    /// <summary>
    /// Standard date range options for UI dropdowns.
    /// </summary>
    public static readonly string[] StandardDateRangeOptions = DateRangePresetExtensions.GetStandardOptions();

    /// <summary>
    /// Future date range options for insights/forecasting UI dropdowns.
    /// </summary>
    public static readonly string[] FutureDateRangeOptions =
    [
        "Next Month",
        "Next Quarter",
        "Next Year",
        "Next 30 Days",
        "Next 90 Days",
        "Next 365 Days"
    ];

    /// <summary>
    /// Historical date range options for insights analysis UI dropdowns.
    /// </summary>
    public static readonly string[] InsightsHistoricalDateRangeOptions =
    [
        "Last Month",
        "Last 3 Months",
        "Last 6 Months",
        "Last Year",
        "All Time"
    ];
}
