using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Charts;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Telemetry;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.SkiaSharpView.SKCharts;
using SkiaSharp;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Renders reports to images and PDF files using SkiaSharp.
/// </summary>
public class ReportRenderer : IDisposable
{
    private readonly CompanyData? _companyData;
    private readonly ReportConfiguration _config;
    private readonly float _renderScale;
    private readonly ReportChartDataService? _chartDataService;
    private readonly ITranslationProvider _translationProvider;
    private readonly IErrorLogger? _errorLogger;
    private readonly string _currencyCode;
    private readonly string _currencySymbol;

    // Cached paints
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _headerPaint;
    private readonly SKPaint _borderPaint;

    // Fonts
    private readonly SKTypeface _defaultTypeface;
    private readonly SKTypeface _boldTypeface;
    private readonly SKFont _defaultFont;
    private readonly SKFont _headerFont;

    // Axes/grid stay local; series colors come from ArgoBooks.Core.ChartColors
    // so dashboard, Analytics page, and PDF reports share the same mapping.
    private static readonly SKColor ChartAxisColor = SKColor.Parse(AppColors.ChartAxis);
    private static readonly SKColor ChartGridColor = SKColor.Parse(AppColors.ChartGrid);

    /// <summary>
    /// Determines if a chart type should display currency formatting on the Y-axis.
    /// </summary>
    private static bool ShouldShowCurrency(ChartDataType chartType)
    {
        return chartType switch
        {
            // Count-based charts (no currency)
            ChartDataType.TotalTransactions => false,
            ChartDataType.CustomerGrowth => false,
            ChartDataType.ActiveVsInactiveCustomers => false,
            ChartDataType.RentalsPerCustomer => false,
            ChartDataType.AccountantsTransactions => false,
            ChartDataType.CountriesOfOrigin => false,
            ChartDataType.CountriesOfDestination => false,
            ChartDataType.CompaniesOfOrigin => false,
            ChartDataType.CompaniesOfDestination => false,
            // All other charts show currency
            _ => true
        };
    }

    /// <summary>
    /// Formats a Y-axis value based on whether the chart shows currency.
    /// </summary>
    private string FormatYAxisValue(double value, ChartDataType chartType)
    {
        return ShouldShowCurrency(chartType) ? $"{_currencySymbol}{value:N0}" : $"{value:N0}";
    }

    /// <summary>
    /// Converts a USD amount to the current display currency using cached exchange rates.
    /// Returns the original amount if the display currency is USD or no rate is available.
    /// </summary>
    private double ConvertFromUSD(double amountUSD, DateTime? date = null)
    {
        if (string.Equals(_currencyCode, "USD", StringComparison.OrdinalIgnoreCase))
            return amountUSD;

        var converted = ExchangeRateService.Instance?.ConvertFromUSD(
            (decimal)amountUSD, _currencyCode, date ?? DateTime.Today);
        return converted.HasValue ? (double)converted.Value : amountUSD;
    }

    /// <summary>
    /// A single USD amount converted to the report's display currency at its own date
    /// (docs/Calculations.md §3/§3a). Aggregate <c>Effective*USD</c> through this; never sum the
    /// native <c>Total</c> field, which silently mixes dollars and euros.
    /// </summary>
    private decimal ToDisplayCurrency(decimal amountUSD, DateTime date)
        => (decimal)ConvertFromUSD((double)amountUSD, date);

    private string PendingText => Tr("Pending");

    /// <summary>
    /// An amount recorded in <paramref name="currency"/> rather than USD (a return's refund, a loss's
    /// value), formatted in the display currency at its own date, or pending when that date's rate is
    /// unavailable. See docs/Calculations.md §10, Returns and Losses.
    /// </summary>
    private string FormatRecordedAmount(decimal amount, string currency, DateTime date)
    {
        if (string.Equals(currency, _currencyCode, StringComparison.OrdinalIgnoreCase))
            return FormatCurrency(amount);

        var rates = ExchangeRateService.Instance;
        if (rates == null)
            return FormatCurrency(amount);

        return rates.TryConvertToUsdBase(amount, currency, date, out var usd)
               && rates.TryConvertFromUSD(usd, _currencyCode, date, out var converted)
            ? FormatCurrency(converted)
            : PendingText;
    }

    public ReportRenderer(ReportConfiguration config, CompanyData? companyData, float renderScale = 1f, ITranslationProvider? translationProvider = null, IErrorLogger? errorLogger = null)
    {
        _config = config;
        _companyData = companyData;
        _renderScale = renderScale;
        _translationProvider = translationProvider ?? DefaultTranslationProvider.Instance;
        _errorLogger = errorLogger;

        // Resolve currency code and symbol from company settings
        _currencyCode = companyData?.Settings.Localization.Currency ?? "USD";
        _currencySymbol = CurrencyInfo.GetSymbol(_currencyCode);

        // Initialize chart data service for rendering actual charts
        if (companyData != null)
        {
            _chartDataService = new ReportChartDataService(companyData, config.Filters);
        }

        _defaultTypeface = PlatformTypefaces.Default;
        _boldTypeface = PlatformTypefaces.Bold;

        _defaultFont = new SKFont(_defaultTypeface, 12 * renderScale);
        _headerFont = new SKFont(_boldTypeface, (float)config.TitleFontSize * renderScale);

        _backgroundPaint = new SKPaint
        {
            Color = ParseColor(_config.BackgroundColor),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        _textPaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };

        _headerPaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };

        _borderPaint = new SKPaint
        {
            Color = SKColors.Gray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * renderScale,
            IsAntialias = true
        };
    }

    /// <summary>
    /// Renders the report to a bitmap.
    /// </summary>
    private SKBitmap RenderToBitmap()
    {
        var (width, height) = PageDimensions.GetDimensions(_config.PageSize, _config.PageOrientation);
        var scaledWidth = (int)(width * _renderScale);
        var scaledHeight = (int)(height * _renderScale);

        var bitmap = new SKBitmap(scaledWidth, scaledHeight);
        using var canvas = new SKCanvas(bitmap);

        RenderToCanvas(canvas, scaledWidth, scaledHeight);

        return bitmap;
    }

    /// <summary>
    /// Renders the report to a canvas.
    /// </summary>
    private void RenderToCanvas(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(_backgroundPaint.Color);

        // Render header
        if (_config.ShowHeader)
        {
            RenderHeader(canvas, width);
        }

        // Render elements sorted by Z-order
        foreach (var element in _config.GetElementsByZOrder())
        {
            if (!element.IsVisible)
                continue;

            RenderElement(canvas, element);
        }

        // Render footer
        if (_config.ShowFooter)
        {
            RenderFooter(canvas, width, height);
        }
    }

    /// <summary>
    /// Renders only the elements to a canvas (for design mode).
    /// Does not clear the canvas or render header/footer.
    /// </summary>
    public void RenderElementsToCanvas(SKCanvas canvas)
    {
        // Render elements sorted by Z-order
        foreach (var element in _config.GetElementsByZOrder())
        {
            if (!element.IsVisible)
                continue;

            RenderElement(canvas, element);
        }
    }

    /// <summary>
    /// Renders only the elements for a specific page to a canvas (for design mode).
    /// Does not clear the canvas or render header/footer.
    /// </summary>
    public void RenderElementsToCanvas(SKCanvas canvas, int pageNumber)
    {
        foreach (var element in _config.GetElementsByZOrderForPage(pageNumber))
        {
            if (!element.IsVisible)
                continue;

            RenderElement(canvas, element);
        }
    }

    /// <summary>
    /// Renders a specific page to a canvas, including header, elements, and footer.
    /// </summary>
    public void RenderPageToCanvas(SKCanvas canvas, int pageNumber, int width, int height)
    {
        canvas.Clear(_backgroundPaint.Color);

        if (_config.ShowHeader)
        {
            RenderHeader(canvas, width);
        }

        foreach (var element in _config.GetElementsByZOrderForPage(pageNumber))
        {
            if (!element.IsVisible)
                continue;

            RenderElement(canvas, element);
        }

        if (_config.ShowFooter)
        {
            RenderFooter(canvas, width, height);
        }
    }

    /// <summary>
    /// Renders a specific page to a bitmap.
    /// </summary>
    public SKBitmap RenderPageToBitmap(int pageNumber)
    {
        var (width, height) = PageDimensions.GetDimensions(_config.PageSize, _config.PageOrientation);
        var scaledWidth = (int)(width * _renderScale);
        var scaledHeight = (int)(height * _renderScale);

        _config.CurrentPageNumber = pageNumber;
        _config.TotalPageCount = _config.PageCount;

        var bitmap = new SKBitmap(scaledWidth, scaledHeight);
        using var canvas = new SKCanvas(bitmap);

        RenderPageToCanvas(canvas, pageNumber, scaledWidth, scaledHeight);

        return bitmap;
    }

    /// <summary>
    /// Creates a preview bitmap for a specific page at the specified scale.
    /// </summary>
    public SKBitmap CreatePagePreview(int pageNumber, int maxWidth, int maxHeight)
    {
        var (width, height) = PageDimensions.GetDimensions(_config.PageSize, _config.PageOrientation);

        var scaleX = (float)maxWidth / width;
        var scaleY = (float)maxHeight / height;
        var scale = Math.Min(scaleX, scaleY);

        var previewWidth = (int)(width * scale);
        var previewHeight = (int)(height * scale);

        _config.CurrentPageNumber = pageNumber;
        _config.TotalPageCount = _config.PageCount;

        var bitmap = new SKBitmap(previewWidth, previewHeight);
        using var canvas = new SKCanvas(bitmap);

        canvas.Scale(scale, scale);
        RenderPageToCanvas(canvas, pageNumber, width, height);

        return bitmap;
    }

    /// <summary>
    /// Exports the report to an image file.
    /// </summary>
    public async Task<bool> ExportPageToImageAsync(string filePath, int pageNumber, ExportFormat format, int quality = 95)
    {
        try
        {
            using var bitmap = RenderPageToBitmap(pageNumber);
            using var image = SKImage.FromBitmap(bitmap);

            var formatType = format switch
            {
                ExportFormat.PNG => SKEncodedImageFormat.Png,
                ExportFormat.JPEG => SKEncodedImageFormat.Jpeg,
                _ => SKEncodedImageFormat.Png
            };

            using var data = image.Encode(formatType, quality);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(filePath);
            data.SaveTo(stream);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Exports an effective page (including continuation pages) to an image file.
    /// </summary>
    public async Task<bool> ExportEffectivePageToImageAsync(string filePath, EffectivePage page, ExportFormat format, int quality = 95)
    {
        try
        {
            using var bitmap = RenderEffectivePageToBitmap(page);
            using var image = SKImage.FromBitmap(bitmap);

            var formatType = format switch
            {
                ExportFormat.PNG => SKEncodedImageFormat.Png,
                ExportFormat.JPEG => SKEncodedImageFormat.Jpeg,
                _ => SKEncodedImageFormat.Png
            };

            using var data = image.Encode(formatType, quality);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(filePath);
            data.SaveTo(stream);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ExportToImageAsync(string filePath, ExportFormat format, int quality = 95)
    {
        try
        {
            using var bitmap = RenderToBitmap();
            using var image = SKImage.FromBitmap(bitmap);

            var formatType = format switch
            {
                ExportFormat.PNG => SKEncodedImageFormat.Png,
                ExportFormat.JPEG => SKEncodedImageFormat.Jpeg,
                _ => SKEncodedImageFormat.Png
            };

            using var data = image.Encode(formatType, quality);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(filePath);
            data.SaveTo(stream);

            return true;
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to export report to image");
            return false;
        }
    }

    /// <summary>
    /// Exports the report to a PDF file.
    /// </summary>
    public async Task<bool> ExportToPdfAsync(string filePath)
    {
        try
        {
            var (width, height) = PageDimensions.GetDimensions(_config.PageSize, _config.PageOrientation);
            var scaledWidth = (int)(width * _renderScale);
            var scaledHeight = (int)(height * _renderScale);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Compute continuation plan for overflow handling
            if (_continuationPlan == null)
                ComputeContinuationPlan();

            _config.TotalPageCount = EffectivePageCount;

            await using var stream = File.Create(filePath);
            using var document = SKDocument.CreatePdf(stream);

            foreach (var effectivePage in _continuationPlan!.Pages)
            {
                _config.CurrentPageNumber = effectivePage.EffectivePageNumber;
                using var canvas = document.BeginPage(scaledWidth, scaledHeight);
                RenderEffectivePageToCanvas(canvas, effectivePage, scaledWidth, scaledHeight);
                document.EndPage();
            }

            document.Close();

            return true;
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to export report to PDF");
            return false;
        }
    }

    #region Page Continuation Planning

    private PageContinuationPlan? _continuationPlan;

    /// <summary>
    /// How many accounting table rows this renderer has drawn, for tests that check a paged table
    /// loses no rows.
    /// </summary>
    internal int AccountingRowsDrawn { get; private set; }

    /// <summary>
    /// The effective number of pages after accounting for continuation pages.
    /// </summary>
    public int EffectivePageCount => _continuationPlan?.TotalPageCount ?? _config.PageCount;

    /// <summary>
    /// Gets the continuation plan (must call ComputeContinuationPlan first).
    /// </summary>
    public PageContinuationPlan? GetContinuationPlan() => _continuationPlan;

    /// <summary>
    /// Computes the height of a single accounting row based on its type.
    /// Single source of truth used by both the planner and the renderer.
    /// </summary>
    internal static float GetAccountingRowHeight(AccountingRowType rowType, float dataRowHeight, float headerRowHeight)
    {
        return rowType switch
        {
            AccountingRowType.SectionHeader => headerRowHeight,
            AccountingRowType.DataRow => dataRowHeight,
            AccountingRowType.SubtotalRow => dataRowHeight,
            AccountingRowType.TotalRow => dataRowHeight * 1.1f,
            AccountingRowType.GrandTotalRow => dataRowHeight * 1.3f,
            AccountingRowType.SeparatorLine => dataRowHeight * 0.5f,
            AccountingRowType.BlankRow => dataRowHeight * 0.5f,
            _ => dataRowHeight
        };
    }

    /// <summary>
    /// Computes the height consumed by the accounting table header area on the first page
    /// (title bar + subtitle + column headers).
    /// </summary>
    internal static float GetAccountingTableFirstPageHeaderHeight(
        AccountingTableReportElement element, AccountingTableData tableData, float renderScale)
    {
        var headerRowHeight = (float)element.HeaderRowHeight * renderScale;
        var dataRowHeight = (float)element.DataRowHeight * renderScale;

        var height = headerRowHeight * 1.4f; // Title bar
        if (!string.IsNullOrEmpty(tableData.Subtitle))
            height += dataRowHeight * 1.2f; // Subtitle
        if (tableData.ColumnHeaders.Count > 0)
            height += headerRowHeight; // Column headers
        return height;
    }

    /// <summary>
    /// Computes the height consumed by the continuation page header area
    /// ("(Continued)" indicator + column headers).
    /// </summary>
    internal static float GetAccountingTableContinuationHeaderHeight(
        AccountingTableReportElement element, AccountingTableData tableData, float renderScale)
    {
        var headerRowHeight = (float)element.HeaderRowHeight * renderScale;
        var dataRowHeight = (float)element.DataRowHeight * renderScale;

        var height = dataRowHeight * 0.8f; // "(Continued)" indicator
        if (tableData.ColumnHeaders.Count > 0)
            height += headerRowHeight; // Column headers
        return height;
    }

    /// <summary>
    /// Top and bottom of the content area on a continuation page. The planner and the renderer both
    /// use this, so the rows planned for a page are the rows that fit when it is drawn.
    /// </summary>
    private (float Top, float Bottom) GetContinuationContentBounds(float scaledPageHeight)
    {
        var headerHeight = _config.ShowHeader ? PageDimensions.GetHeaderHeight(_config.ShowCompanyDetails) : 0;
        return ((headerHeight + (float)_config.PageMargins.Top) * _renderScale,
            scaledPageHeight - (PageDimensions.FooterHeight + (float)_config.PageMargins.Bottom) * _renderScale);
    }

    /// <summary>
    /// Computes the continuation plan for all accounting tables that may overflow.
    /// Must be called before rendering begins.
    /// </summary>
    public void ComputeContinuationPlan()
    {
        var plan = new PageContinuationPlan();
        var (_, pageHeight) = PageDimensions.GetDimensions(_config.PageSize, _config.PageOrientation);

        // Build effective pages: start with all template pages
        // Then insert continuation pages after each template page as needed
        for (int templatePage = 1; templatePage <= _config.PageCount; templatePage++)
        {
            // Add the original template page
            plan.Pages.Add(new EffectivePage
            {
                SourcePageNumber = templatePage,
                ContinuationElementId = null,
                StartRowIndex = 0,
                RowCount = 0,
                EffectivePageNumber = 0 // will be assigned later
            });

            // Check each accounting table on this page for overflow
            var accountingElements = _config.Elements
                .OfType<AccountingTableReportElement>()
                .Where(e => e.PageNumber == templatePage && e.IsVisible)
                .OrderBy(e => e.ZOrder)
                .ToList();

            foreach (var element in accountingElements)
            {
                var dataService = new AccountingReportDataService(_companyData, _config.Filters);
                var tableData = dataService.GetReportData(element.ReportType);
                plan.CachedTableData[element.Id] = tableData;

                if (tableData.Rows.Count == 0)
                    continue;

                var dataRowHeight = (float)element.DataRowHeight * _renderScale;
                var headerRowHeight = (float)element.HeaderRowHeight * _renderScale;

                // Compute available height on the first page (element's own rect)
                var rect = GetScaledRect(element);
                var firstPageAvailable = rect.Height
                    - GetAccountingTableFirstPageHeaderHeight(element, tableData, _renderScale);

                // Walk rows to find how many fit on page 1
                float accumulatedHeight = 0;
                int firstPageRowCount = 0;
                int dataRowIndex = 0;

                foreach (var row in tableData.Rows)
                {
                    var rowHeight = GetAccountingRowHeight(row.RowType, dataRowHeight, headerRowHeight);
                    if (accumulatedHeight + rowHeight > firstPageAvailable)
                        break;
                    accumulatedHeight += rowHeight;
                    firstPageRowCount++;
                    if (row.RowType == AccountingRowType.DataRow)
                        dataRowIndex++;
                }

                // If all rows fit, no continuation needed
                if (firstPageRowCount >= tableData.Rows.Count)
                    continue;

                plan.FirstPageRowCounts[element.Id] = firstPageRowCount;

                // Compute continuation pages
                // Available height on continuation pages: full content area
                var (continuationTop, continuationBottom) = GetContinuationContentBounds(pageHeight * _renderScale);
                var continuationAvailable = continuationBottom - continuationTop
                    - GetAccountingTableContinuationHeaderHeight(element, tableData, _renderScale);

                // Reserve space for footnote on last page
                var footnoteHeight = !string.IsNullOrEmpty(tableData.Footnote)
                    ? dataRowHeight * 1.3f
                    : 0f;

                int currentRowIndex = firstPageRowCount;
                while (currentRowIndex < tableData.Rows.Count)
                {
                    // Determine if this might be the last page
                    // Try to fit remaining rows
                    float pageAccumulated = 0;
                    int pageRowCount = 0;
                    int pageDataRowStart = dataRowIndex;

                    // Compute how many rows fit
                    for (int i = currentRowIndex; i < tableData.Rows.Count; i++)
                    {
                        var rowHeight = GetAccountingRowHeight(tableData.Rows[i].RowType, dataRowHeight, headerRowHeight);
                        if (pageAccumulated + rowHeight > continuationAvailable)
                            break;
                        pageAccumulated += rowHeight;
                        pageRowCount++;
                        if (tableData.Rows[i].RowType == AccountingRowType.DataRow)
                            dataRowIndex++;
                    }

                    bool isLast = currentRowIndex + pageRowCount >= tableData.Rows.Count;

                    // If this is the last page and footnote would overflow, reduce rows
                    if (isLast && footnoteHeight > 0 && pageAccumulated + footnoteHeight > continuationAvailable)
                    {
                        // Recalculate with footnote space reserved
                        var availableWithFootnote = continuationAvailable - footnoteHeight;
                        pageAccumulated = 0;
                        pageRowCount = 0;
                        dataRowIndex = pageDataRowStart;

                        for (int i = currentRowIndex; i < tableData.Rows.Count; i++)
                        {
                            var rowHeight = GetAccountingRowHeight(tableData.Rows[i].RowType, dataRowHeight, headerRowHeight);
                            if (pageAccumulated + rowHeight > availableWithFootnote)
                                break;
                            pageAccumulated += rowHeight;
                            pageRowCount++;
                            if (tableData.Rows[i].RowType == AccountingRowType.DataRow)
                                dataRowIndex++;
                        }
                    }

                    // Safety: always progress at least 1 row to prevent infinite loop
                    if (pageRowCount == 0)
                    {
                        pageRowCount = 1;
                        if (tableData.Rows[currentRowIndex].RowType == AccountingRowType.DataRow)
                            dataRowIndex++;
                    }

                    plan.Pages.Add(new EffectivePage
                    {
                        SourcePageNumber = templatePage,
                        ContinuationElementId = element.Id,
                        StartRowIndex = currentRowIndex,
                        RowCount = pageRowCount,
                        DataRowStartIndex = pageDataRowStart,
                        IsLastContinuationPage = currentRowIndex + pageRowCount >= tableData.Rows.Count,
                        EffectivePageNumber = 0 // assigned below
                    });

                    currentRowIndex += pageRowCount;
                }
            }

            // Check each normal table on this page for overflow
            var tableElements = _config.Elements
                .OfType<TableReportElement>()
                .Where(e => e.PageNumber == templatePage && e.IsVisible)
                .OrderBy(e => e.ZOrder)
                .ToList();

            foreach (var table in tableElements)
            {
                var columns = GetVisibleColumns(table);
                var normalTableData = GetTableData(table, columns);

                if (normalTableData.Count == 0)
                    continue;

                plan.CachedNormalTableData[table.Id] = normalTableData;
                plan.CachedNormalTableColumns[table.Id] = columns;

                var rect = GetScaledRect(table);
                var headerRowHeight = table.HeaderRowHeight * _renderScale;
                var dataRowHeight = table.DataRowHeight * _renderScale;
                var titleRowHeight = headerRowHeight;

                // Calculate available height for data rows on page 1
                var availableOnPage1 = rect.Height;
                if (table.ShowTitle)
                    availableOnPage1 -= titleRowHeight;
                if (table.ShowHeaders)
                    availableOnPage1 -= headerRowHeight;
                if (table.ShowTotalsRow)
                    availableOnPage1 -= headerRowHeight;

                // Apply MaxRows limit if set
                var maxRowsSetting = table.MaxRows > 0 ? table.MaxRows : int.MaxValue;
                var totalDataRows = Math.Min(normalTableData.Count, maxRowsSetting);

                // Walk rows to find how many fit on page 1
                var firstPageRowCount = Math.Max(0, (int)Math.Floor(availableOnPage1 / dataRowHeight));
                firstPageRowCount = Math.Min(firstPageRowCount, totalDataRows);

                // If all rows fit, no continuation needed
                if (firstPageRowCount >= totalDataRows)
                    continue;

                plan.FirstPageRowCounts[table.Id] = firstPageRowCount;

                // Compute continuation pages
                var (continuationTop, continuationBottom) = GetContinuationContentBounds(pageHeight * _renderScale);

                // Continuation header: "(Continued)" indicator + optional column headers
                var continuationHeaderHeight = dataRowHeight * 0.8f; // "(Continued)" indicator
                if (table.ShowHeaders)
                    continuationHeaderHeight += headerRowHeight;

                var continuationAvailable = continuationBottom - continuationTop - continuationHeaderHeight;

                // Reserve space for totals row on last page
                var totalsHeight = table.ShowTotalsRow ? headerRowHeight : 0f;

                var currentRowIndex = firstPageRowCount;
                while (currentRowIndex < totalDataRows)
                {
                    var rowsPerPage = Math.Max(0, (int)Math.Floor(continuationAvailable / dataRowHeight));
                    var remaining = totalDataRows - currentRowIndex;
                    var isLast = remaining <= rowsPerPage;

                    // If last page needs totals row, recalculate rows that fit
                    if (isLast && totalsHeight > 0)
                    {
                        var availableWithTotals = continuationAvailable - totalsHeight;
                        var rowsWithTotals = Math.Max(0, (int)Math.Floor(availableWithTotals / dataRowHeight));
                        if (remaining > rowsWithTotals)
                        {
                            // Not enough room for all remaining + totals, use reduced count
                            rowsPerPage = rowsWithTotals;
                        }
                    }

                    var pageRowCount = Math.Min(remaining, Math.Max(1, rowsPerPage));
                    isLast = currentRowIndex + pageRowCount >= totalDataRows;

                    plan.Pages.Add(new EffectivePage
                    {
                        SourcePageNumber = templatePage,
                        ContinuationElementId = table.Id,
                        StartRowIndex = currentRowIndex,
                        RowCount = pageRowCount,
                        DataRowStartIndex = currentRowIndex, // For alternating row color continuity
                        IsLastContinuationPage = isLast,
                        EffectivePageNumber = 0 // assigned below
                    });

                    currentRowIndex += pageRowCount;
                }
            }
        }

        // Assign effective page numbers
        for (int i = 0; i < plan.Pages.Count; i++)
        {
            plan.Pages[i].EffectivePageNumber = i + 1;
        }

        _continuationPlan = plan;
    }

    /// <summary>
    /// Renders a single effective page (original or continuation) to a canvas.
    /// </summary>
    public void RenderEffectivePageToCanvas(SKCanvas canvas, EffectivePage page, int width, int height, bool skipBackground = false)
    {
        // Use DrawRect instead of Clear so stacked pages aren't wiped out
        if (!skipBackground)
            canvas.DrawRect(0, 0, width, height, _backgroundPaint);

        if (_config.ShowHeader)
        {
            RenderHeader(canvas, width);
        }

        if (page.IsContinuationPage)
        {
            // Continuation page: render only the overflowing table
            var accountingElement = _config.Elements
                .OfType<AccountingTableReportElement>()
                .FirstOrDefault(e => e.Id == page.ContinuationElementId);

            if (accountingElement != null && _continuationPlan?.CachedTableData.TryGetValue(accountingElement.Id, out var tableData) == true)
            {
                // Position the table using the element's X/Width from page 1, with full vertical content area
                var elementRect = GetScaledRect(accountingElement);
                var (contentTop, contentBottom) = GetContinuationContentBounds(height);
                var overrideRect = new SKRect(elementRect.Left, contentTop, elementRect.Right, contentBottom);

                RenderAccountingTableSlice(canvas, accountingElement, tableData,
                    page.StartRowIndex, page.RowCount, page.DataRowStartIndex,
                    showTitle: false, showSubtitle: false,
                    showFootnote: page.IsLastContinuationPage,
                    showContinuedIndicator: true,
                    overrideRect: overrideRect);
            }
            else
            {
                // Check for a normal table continuation
                var normalTable = _config.Elements
                    .OfType<TableReportElement>()
                    .FirstOrDefault(e => e.Id == page.ContinuationElementId);

                if (normalTable != null
                    && _continuationPlan?.CachedNormalTableData.TryGetValue(normalTable.Id, out var normalData) == true
                    && _continuationPlan?.CachedNormalTableColumns.TryGetValue(normalTable.Id, out var normalColumns) == true)
                {
                    var elementRect = GetScaledRect(normalTable);
                    var (contentTop, contentBottom) = GetContinuationContentBounds(height);
                    var overrideRect = new SKRect(elementRect.Left, contentTop, elementRect.Right, contentBottom);

                    RenderTableSlice(canvas, normalTable, normalData, normalColumns,
                        page.StartRowIndex, page.RowCount, page.DataRowStartIndex,
                        showTitle: false,
                        showHeaders: normalTable.ShowHeaders,
                        showTotalsRow: page.IsLastContinuationPage && normalTable.ShowTotalsRow,
                        showContinuedIndicator: true,
                        overrideRect: overrideRect);
                }
            }
        }
        else
        {
            // Original template page: render all elements
            foreach (var element in _config.GetElementsByZOrderForPage(page.SourcePageNumber))
            {
                if (!element.IsVisible)
                    continue;

                if (element is AccountingTableReportElement accounting
                    && _continuationPlan?.FirstPageRowCounts.TryGetValue(accounting.Id, out var firstPageRowCount) == true
                    && _continuationPlan?.CachedTableData.TryGetValue(accounting.Id, out var tableData) == true)
                {
                    // This accounting table overflows, render only the first page's rows
                    RenderAccountingTableSlice(canvas, accounting, tableData,
                        startRowIndex: 0, rowCount: firstPageRowCount, dataRowStartIndex: 0,
                        showTitle: true, showSubtitle: true,
                        showFootnote: false,
                        showContinuedIndicator: false);
                }
                else if (element is AccountingTableReportElement accounting2
                    && _continuationPlan?.CachedTableData.TryGetValue(accounting2.Id, out var cachedData) == true)
                {
                    // This accounting table doesn't overflow but we have cached data, use it
                    RenderAccountingTableSlice(canvas, accounting2, cachedData,
                        startRowIndex: 0, rowCount: cachedData.Rows.Count, dataRowStartIndex: 0,
                        showTitle: true, showSubtitle: true,
                        showFootnote: true,
                        showContinuedIndicator: false);
                }
                else if (element is TableReportElement normalTable
                    && _continuationPlan?.FirstPageRowCounts.TryGetValue(normalTable.Id, out var normalFirstPageRowCount) == true
                    && _continuationPlan?.CachedNormalTableData.TryGetValue(normalTable.Id, out var normalData) == true
                    && _continuationPlan?.CachedNormalTableColumns.TryGetValue(normalTable.Id, out var normalColumns) == true)
                {
                    // This normal table overflows, render only the first page's rows
                    RenderTableSlice(canvas, normalTable, normalData, normalColumns,
                        startRowIndex: 0, rowCount: normalFirstPageRowCount, dataRowStartIndex: 0,
                        showTitle: normalTable.ShowTitle,
                        showHeaders: normalTable.ShowHeaders,
                        showTotalsRow: false,
                        showContinuedIndicator: false);
                }
                else
                {
                    RenderElement(canvas, element);
                }
            }
        }

        if (_config.ShowFooter)
        {
            RenderFooter(canvas, width, height);
        }
    }

    /// <summary>
    /// Renders an effective page to a bitmap.
    /// </summary>
    public SKBitmap RenderEffectivePageToBitmap(EffectivePage page)
    {
        var (width, height) = PageDimensions.GetDimensions(_config.PageSize, _config.PageOrientation);
        var scaledWidth = (int)(width * _renderScale);
        var scaledHeight = (int)(height * _renderScale);

        _config.CurrentPageNumber = page.EffectivePageNumber;
        _config.TotalPageCount = EffectivePageCount;

        var bitmap = new SKBitmap(scaledWidth, scaledHeight);
        using var canvas = new SKCanvas(bitmap);

        RenderEffectivePageToCanvas(canvas, page, scaledWidth, scaledHeight);

        return bitmap;
    }

    /// <summary>
    /// Creates a preview bitmap for an effective page at the specified scale.
    /// </summary>
    public SKBitmap CreateEffectivePagePreview(EffectivePage page, int maxWidth, int maxHeight)
    {
        var (width, height) = PageDimensions.GetDimensions(_config.PageSize, _config.PageOrientation);

        var scaleX = (float)maxWidth / width;
        var scaleY = (float)maxHeight / height;
        var scale = Math.Min(scaleX, scaleY);

        var previewWidth = (int)(width * scale);
        var previewHeight = (int)(height * scale);

        _config.CurrentPageNumber = page.EffectivePageNumber;
        _config.TotalPageCount = EffectivePageCount;

        var bitmap = new SKBitmap(previewWidth, previewHeight);
        using var canvas = new SKCanvas(bitmap);

        canvas.Scale(scale, scale);
        RenderEffectivePageToCanvas(canvas, page, width, height);

        return bitmap;
    }

    #endregion

    #region Element Rendering

    private void RenderElement(SKCanvas canvas, ReportElementBase element)
    {
        switch (element)
        {
            case ChartReportElement chart:
                RenderChart(canvas, chart);
                break;
            case TableReportElement table:
                RenderTable(canvas, table);
                break;
            case LabelReportElement label:
                RenderLabel(canvas, label);
                break;
            case ImageReportElement image:
                RenderImage(canvas, image);
                break;
            case DateRangeReportElement dateRange:
                RenderDateRange(canvas, dateRange);
                break;
            case SummaryReportElement summary:
                RenderSummary(canvas, summary);
                break;
            case AccountingTableReportElement accounting:
                RenderAccountingTable(canvas, accounting);
                break;
        }
    }

    private void RenderChart(SKCanvas canvas, ChartReportElement chart)
    {
        var rect = GetScaledRect(chart);

        // Draw background for chart using the element's background color
        var bgPaint = new SKPaint
        {
            Color = ParseColor(chart.BackgroundColor),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRect(rect, bgPaint);

        // Draw border if configured
        if (chart.BorderThickness > 0)
        {
            var borderPaint = new SKPaint
            {
                Color = ParseColor(chart.BorderColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = chart.BorderThickness * _renderScale,
                IsAntialias = true
            };
            canvas.DrawRect(rect, borderPaint);
        }

        // Draw chart title
        if (chart.ShowTitle)
        {
            var chartTitleTypeface = PlatformTypefaces.Resolve(chart.TitleFontFamily, SKFontStyle.Bold);
            using var titleFont = new SKFont(chartTitleTypeface, (float)chart.TitleFontSize * _renderScale);
            using var titlePaint = new SKPaint();
            titlePaint.Color = SKColors.Black;
            titlePaint.IsAntialias = true;

            var title = GetChartTitle(chart.ChartType);
            canvas.DrawText(title, rect.MidX, rect.Top + 20 * _renderScale, SKTextAlign.Center, titleFont, titlePaint);
        }

        // Define chart area with title offset
        var topPadding = (chart.ShowTitle ? 35 : 15) * _renderScale;

        // For bar/line charts, add padding for Y-axis and X-axis labels
        var barChartArea = new SKRect(
            rect.Left + 60 * _renderScale,   // Space for Y-axis labels
            rect.Top + topPadding,
            rect.Right - 15 * _renderScale,  // Right padding
            rect.Bottom - 40 * _renderScale  // Space for X-axis labels
        );

        // For pie charts and no-data, use minimal padding
        var pieChartArea = new SKRect(
            rect.Left + 15 * _renderScale,
            rect.Top + topPadding,
            rect.Right - 15 * _renderScale,
            rect.Bottom - 15 * _renderScale
        );

        // Handle different chart types
        if (IsGeoMapChart(chart.ChartType))
        {
            RenderGeoMap(canvas, pieChartArea);
            return;
        }

        if (IsMultiSeriesChart(chart.ChartType))
        {
            var seriesData = GetMultiSeriesData(chart.ChartType);
            if (seriesData == null || seriesData.Count == 0)
            {
                DrawNoDataPlaceholder(canvas, pieChartArea);
                return;
            }

            // Choose rendering based on chart style
            if (chart.ChartStyle == ReportChartStyle.Line ||
                chart.ChartStyle == ReportChartStyle.StepLine ||
                chart.ChartStyle == ReportChartStyle.Area ||
                chart.ChartStyle == ReportChartStyle.Scatter)
            {
                RenderMultiSeriesLineChart(canvas, barChartArea, seriesData, chart);
            }
            else
            {
                RenderMultiSeriesBarChart(canvas, barChartArea, seriesData, chart);
            }
            return;
        }

        // Get single-series chart data
        var chartData = GetChartDataPoints(chart.ChartType);

        if (chartData == null || chartData.Count == 0)
        {
            DrawNoDataPlaceholder(canvas, pieChartArea);
            return;
        }

        // Render the appropriate chart type
        if (IsDistributionChart(chart.ChartType))
        {
            RenderPieChart(canvas, pieChartArea, chartData, chart);
        }
        else
        {
            // Choose rendering based on chart style
            if (chart.ChartStyle == ReportChartStyle.Line ||
                chart.ChartStyle == ReportChartStyle.StepLine ||
                chart.ChartStyle == ReportChartStyle.Area ||
                chart.ChartStyle == ReportChartStyle.Scatter)
            {
                RenderLineChart(canvas, barChartArea, chartData, chart);
            }
            else
            {
                RenderBarChart(canvas, barChartArea, chartData, chart);
            }
        }
    }

    /// <summary>
    /// Draws a placeholder when no data is available.
    /// </summary>
    private void DrawNoDataPlaceholder(SKCanvas canvas, SKRect chartArea)
    {
        // Just show text, no background rectangle
        using var noDataFont = new SKFont(_defaultTypeface, 12 * _renderScale);
        using var noDataPaint = new SKPaint();
        noDataPaint.Color = SKColors.Gray;
        noDataPaint.IsAntialias = true;
        canvas.DrawText(Tr("No data available"), chartArea.MidX, chartArea.MidY, SKTextAlign.Center, noDataFont, noDataPaint);
    }

    /// <summary>
    /// Checks if the chart type is a GeoMap.
    /// </summary>
    private static bool IsGeoMapChart(ChartDataType chartType)
    {
        return chartType == ChartDataType.WorldMap;
    }

    /// <summary>
    /// Checks if the chart type requires multi-series rendering.
    /// </summary>
    private static bool IsMultiSeriesChart(ChartDataType chartType)
    {
        return chartType is ChartDataType.RevenueVsExpenses
            or ChartDataType.ExpenseVsRevenueReturns
            or ChartDataType.ExpenseVsRevenueLosses
            or ChartDataType.TotalTransactions
            or ChartDataType.AverageTransactionValue;
    }

    /// <summary>
    /// Checks if the chart type should be rendered as a pie chart.
    /// </summary>
    private static bool IsDistributionChart(ChartDataType chartType)
    {
        return chartType is ChartDataType.RevenueDistribution
            or ChartDataType.ExpensesDistribution
            or ChartDataType.ReturnReasons
            or ChartDataType.LossReasons
            or ChartDataType.ReturnsByCategory
            or ChartDataType.LossesByCategory
            or ChartDataType.ReturnsByProduct
            or ChartDataType.LossesByProduct
            or ChartDataType.CountriesOfOrigin
            or ChartDataType.CountriesOfDestination
            or ChartDataType.CompaniesOfOrigin
            or ChartDataType.CompaniesOfDestination
            or ChartDataType.AccountantsTransactions
            or ChartDataType.TopCustomersByRevenue
            or ChartDataType.CustomerPaymentStatus
            or ChartDataType.ActiveVsInactiveCustomers;
    }

    /// <summary>
    /// Gets chart data points for a specific chart type.
    /// </summary>
    /// <summary>
    /// Chart types whose data method converts each transaction at its OWN date when handed a converter.
    /// For these, a non-USD report passes the converter and does NOT re-convert the bucket afterwards,
    /// so a month bucket is never converted at the month-start date (whose rate is usually uncached,
    /// which falls back to the raw USD figure). Calculations.md Rule 3a Phase 2.
    /// </summary>
    private static bool ConvertsPerTransaction(ChartDataType chartType) => chartType is
        ChartDataType.AverageTransactionValue
        or ChartDataType.AverageShippingCosts
        or ChartDataType.TaxCollectedVsPaid
        or ChartDataType.ExpenseVsRevenueTax;

    private List<ChartDataPoint>? GetChartDataPoints(ChartDataType chartType)
    {
        if (_chartDataService == null)
            return null;

        if (ConvertsPerTransaction(chartType)
            && !string.Equals(_currencyCode, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return _chartDataService.GetChartData(
                chartType, (usd, date) => (decimal)ConvertFromUSD((double)usd, date)) as List<ChartDataPoint>;
        }

        var data = _chartDataService.GetChartData(chartType);

        if (data is List<ChartDataPoint> dataPoints)
        {
            // Chart data is computed in USD, convert to display currency for monetary charts
            if (ShouldShowCurrency(chartType))
            {
                foreach (var point in dataPoints)
                    point.Value = ConvertFromUSD(point.Value, point.Date);
            }
            return dataPoints;
        }

        return null;
    }

    /// <summary>
    /// Gets multi-series chart data.
    /// </summary>
    private List<ChartSeriesData>? GetMultiSeriesData(ChartDataType chartType)
    {
        if (_chartDataService == null)
            return null;

        // Revenue vs Expenses for a non-USD display currency: convert each day's value at that day's
        // OWN rate before bucketing, so a wide range doesn't convert a month total at the month-start
        // date (whose rate is usually uncached) and fall back to showing the raw USD figure. Matches
        // the dashboard / analytics path (Calculations.md Rule 3a Phase 2).
        if (chartType == ChartDataType.RevenueVsExpenses
            && !string.Equals(_currencyCode, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return _chartDataService.GetRevenueVsExpensesConverted(
                (usd, date) => (decimal)ConvertFromUSD((double)usd, date));
        }

        // Other monthly monetary multi-series charts convert each transaction at its own date inside
        // the data service (same reason as above), so pass the converter and don't re-convert here.
        if (ConvertsPerTransaction(chartType)
            && !string.Equals(_currencyCode, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return _chartDataService.GetChartData(
                chartType, (usd, date) => (decimal)ConvertFromUSD((double)usd, date)) as List<ChartSeriesData>;
        }

        var data = _chartDataService.GetChartData(chartType);

        if (data is List<ChartSeriesData> seriesData)
        {
            // Chart data is computed in USD, convert to display currency for monetary charts
            if (ShouldShowCurrency(chartType))
            {
                foreach (var series in seriesData)
                    foreach (var point in series.DataPoints)
                        point.Value = ConvertFromUSD(point.Value, point.Date);
            }
            return seriesData;
        }

        return null;
    }

    /// <summary>
    /// Gets world map data for GeoMap chart.
    /// </summary>
    private Dictionary<string, double>? GetWorldMapData()
    {
        if (_chartDataService == null)
            return null;

        // Convert each country's revenue at each transaction's OWN date during aggregation
        // (docs/Calculations.md §3a Phase 2) instead of converting the country total at today's
        // rate. Null for a USD report (identity).
        var converter = string.Equals(_currencyCode, "USD", StringComparison.OrdinalIgnoreCase)
            ? (Func<decimal, DateTime, decimal>?)null
            : (usd, date) => (decimal)ConvertFromUSD((double)usd, date);

        var data = _chartDataService.GetChartData(ChartDataType.WorldMap, converter);

        // Values are already in the display currency (or USD); do not re-convert.
        return data as Dictionary<string, double>;
    }

    /// <summary>
    /// Renders a bar chart using SkiaSharp.
    /// </summary>
    private void RenderBarChart(SKCanvas canvas, SKRect chartArea, List<ChartDataPoint> dataPoints, ChartReportElement chart)
    {
        if (dataPoints.Count == 0) return;

        // Get typeface for chart labels
        var chartTypeface = PlatformTypefaces.Resolve(chart.FontFamily);

        // Calculate value range
        var maxValue = dataPoints.Max(p => p.Value);
        var minValue = dataPoints.Min(p => p.Value);

        // Ensure we have a sensible range
        if (maxValue == 0 && minValue == 0) maxValue = 1;

        // Determine if we have negative values
        var hasNegatives = minValue < 0;

        // 20% headroom above the max so the tallest point isn't flush with the top edge.
        // The floor stays pinned at 0 unless the data goes negative, in which case the
        // bottom gets the same 20%.
        var paddedMaxValue = maxValue > 0 ? maxValue * 1.2 : maxValue;
        var paddedMinValue = hasNegatives ? minValue * 1.2 : 0;

        // Calculate the total range the chart needs to display
        var totalRange = paddedMaxValue - paddedMinValue;
        if (totalRange == 0) totalRange = 1;

        // Calculate baseline position (where value = 0)
        var baselineY = hasNegatives
            ? chartArea.Top + chartArea.Height * (float)(paddedMaxValue / totalRange)
            : chartArea.Bottom;

        // Draw grid lines
        using var gridPaint = new SKPaint();
        gridPaint.Color = ChartGridColor;
        gridPaint.Style = SKPaintStyle.Stroke;
        gridPaint.StrokeWidth = 1 * _renderScale;
        gridPaint.IsAntialias = true;

        var gridLineCount = 5;
        for (int i = 0; i <= gridLineCount; i++)
        {
            var y = chartArea.Top + (chartArea.Height * i / gridLineCount);
            canvas.DrawLine(chartArea.Left, y, chartArea.Right, y, gridPaint);

            // Draw Y-axis value labels using the full padded range
            var value = paddedMaxValue - totalRange * i / gridLineCount;
            using var yLabelFont = new SKFont(chartTypeface, (float)chart.AxisFontSize * _renderScale);
            using var yLabelPaint = new SKPaint();
            yLabelPaint.Color = ChartAxisColor;
            yLabelPaint.IsAntialias = true;

            canvas.DrawText(FormatYAxisValue(value, chart.ChartType), chartArea.Left - 5 * _renderScale, y + 4 * _renderScale, SKTextAlign.Right, yLabelFont, yLabelPaint);
        }

        // Draw Y-axis
        using var axisPaint = new SKPaint();
        axisPaint.Color = ChartAxisColor;
        axisPaint.Style = SKPaintStyle.Stroke;
        axisPaint.StrokeWidth = 1 * _renderScale;
        axisPaint.IsAntialias = true;
        canvas.DrawLine(chartArea.Left, chartArea.Top, chartArea.Left, chartArea.Bottom, axisPaint);

        // Draw X-axis (baseline)
        canvas.DrawLine(chartArea.Left, baselineY, chartArea.Right, baselineY, axisPaint);

        // Calculate bar dimensions - spread evenly across full chart width
        var barCount = dataPoints.Count;
        var categoryWidth = chartArea.Width / barCount;
        var maxBarWidth = 50 * _renderScale;
        var barWidth = Math.Min(maxBarWidth, categoryWidth * 0.7f);

        // Per-bar color: profit/tax-liability charts pick green or red based on
        // the bar's sign; flat-category charts use the same color throughout.
        using var barPaint = new SKPaint();
        barPaint.Style = SKPaintStyle.Fill;
        barPaint.IsAntialias = true;

        // Draw bars - centered within each category slot
        for (int i = 0; i < dataPoints.Count; i++)
        {
            var point = dataPoints[i];
            var categoryCenterX = chartArea.Left + (i * categoryWidth) + (categoryWidth / 2);
            var x = categoryCenterX - (barWidth / 2);

            // Calculate bar height based on value relative to total range
            var barHeight = chartArea.Height * (float)(Math.Abs(point.Value) / totalRange);

            // Handle positive vs negative values
            var barRect = point.Value >= 0
                ? new SKRect(x, baselineY - barHeight, x + barWidth, baselineY)
                : new SKRect(x, baselineY, x + barWidth, baselineY + barHeight);

            barPaint.Color = ChartColors.ForValue(chart.ChartType, point.Value);
            canvas.DrawRect(barRect, barPaint);
        }

        // Draw X-axis labels - dynamically based on date range and available width
        using var xLabelFont = new SKFont(chartTypeface, (float)chart.AxisFontSize * _renderScale);
        using var xLabelPaint = new SKPaint();
        xLabelPaint.Color = ChartAxisColor;
        xLabelPaint.IsAntialias = true;

        var sampleLabel = FormatXAxisLabel(dataPoints[0]);
        var measuredLabelWidth = xLabelFont.MeasureText(sampleLabel, xLabelPaint);
        var labelIndices = GetXAxisLabelIndices(dataPoints, chartArea.Width, measuredLabelWidth, _renderScale);
        var labelY = chartArea.Bottom + 18 * _renderScale;

        DrawXAxisLabelsWithOverflowProtection(canvas, labelY, labelIndices,
            i => (FormatXAxisLabel(dataPoints[i]), chartArea.Left + (i * categoryWidth) + (categoryWidth / 2)),
            xLabelFont, xLabelPaint, chartArea);
    }

    /// <summary>
    /// Renders a line chart using SkiaSharp.
    /// Supports Line, StepLine, and Area styles.
    /// </summary>
    private void RenderLineChart(SKCanvas canvas, SKRect chartArea, List<ChartDataPoint> dataPoints, ChartReportElement chart)
    {
        if (dataPoints.Count == 0) return;

        // Get typeface for chart labels
        var chartTypeface = PlatformTypefaces.Resolve(chart.FontFamily);

        // Calculate value range
        var maxValue = dataPoints.Max(p => p.Value);
        var minValue = dataPoints.Min(p => p.Value);

        // Ensure we have a sensible range
        if (maxValue == 0 && minValue == 0) maxValue = 1;

        // Determine if we have negative values
        var hasNegatives = minValue < 0;

        // 20% headroom above the max so the tallest point isn't flush with the top edge.
        // The floor stays pinned at 0 unless the data goes negative, in which case the
        // bottom gets the same 20%.
        var paddedMaxValue = maxValue > 0 ? maxValue * 1.2 : maxValue;
        var paddedMinValue = hasNegatives ? minValue * 1.2 : 0;

        // Calculate the total range the chart needs to display
        var totalRange = paddedMaxValue - paddedMinValue;
        if (totalRange == 0) totalRange = 1;

        // Calculate baseline position (where value = 0)
        var baselineY = hasNegatives
            ? chartArea.Top + chartArea.Height * (float)(paddedMaxValue / totalRange)
            : chartArea.Bottom;

        // Draw grid lines
        using var gridPaint = new SKPaint();
        gridPaint.Color = ChartGridColor;
        gridPaint.Style = SKPaintStyle.Stroke;
        gridPaint.StrokeWidth = 1 * _renderScale;
        gridPaint.IsAntialias = true;

        var gridLineCount = 5;
        for (int i = 0; i <= gridLineCount; i++)
        {
            var y = chartArea.Top + (chartArea.Height * i / gridLineCount);
            canvas.DrawLine(chartArea.Left, y, chartArea.Right, y, gridPaint);

            // Draw Y-axis value labels using the full padded range
            var value = paddedMaxValue - totalRange * i / gridLineCount;
            using var yLabelFont = new SKFont(chartTypeface, (float)chart.AxisFontSize * _renderScale);
            using var yLabelPaint = new SKPaint();
            yLabelPaint.Color = ChartAxisColor;
            yLabelPaint.IsAntialias = true;

            canvas.DrawText(FormatYAxisValue(value, chart.ChartType), chartArea.Left - 5 * _renderScale, y + 4 * _renderScale, SKTextAlign.Right, yLabelFont, yLabelPaint);
        }

        // Draw axes
        using var axisPaint = new SKPaint();
        axisPaint.Color = ChartAxisColor;
        axisPaint.Style = SKPaintStyle.Stroke;
        axisPaint.StrokeWidth = 1 * _renderScale;
        axisPaint.IsAntialias = true;
        canvas.DrawLine(chartArea.Left, chartArea.Top, chartArea.Left, chartArea.Bottom, axisPaint);
        canvas.DrawLine(chartArea.Left, baselineY, chartArea.Right, baselineY, axisPaint);

        // Line uses a single representative color per chart type.
        // For profit/tax-liability, that's the color matching positive values
        // (green for profits, red for owed tax).
        var lineColor = ChartColors.ForValue(chart.ChartType, 0);

        // Calculate point positions
        var pointCount = dataPoints.Count;
        var points = new SKPoint[pointCount];
        var xSpacing = chartArea.Width / Math.Max(1, pointCount - 1);

        for (int i = 0; i < pointCount; i++)
        {
            var x = chartArea.Left + (i * xSpacing);
            if (pointCount == 1) x = chartArea.MidX; // Center single point
            // Map value to Y position: paddedMaxValue at top, paddedMinValue at bottom
            var y = chartArea.Top + chartArea.Height * (float)((paddedMaxValue - dataPoints[i].Value) / totalRange);
            points[i] = new SKPoint(x, y);
        }

        // Render based on chart style
        if (chart.ChartStyle == ReportChartStyle.Area)
        {
            // Draw filled area under line using spline curve
            using var path = new SKPath();
            path.MoveTo(points[0].X, baselineY);
            path.LineTo(points[0].X, points[0].Y);

            // Use cubic Bezier curves for smooth spline
            for (int i = 0; i < points.Length - 1; i++)
            {
                var p0 = i > 0 ? points[i - 1] : points[i];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = i < points.Length - 2 ? points[i + 2] : points[i + 1];

                // Calculate control points for cubic Bezier (Catmull-Rom to Bezier conversion)
                var tension = 0.5f;
                var cp1 = new SKPoint(
                    p1.X + (p2.X - p0.X) * tension / 3,
                    p1.Y + (p2.Y - p0.Y) * tension / 3);
                var cp2 = new SKPoint(
                    p2.X - (p3.X - p1.X) * tension / 3,
                    p2.Y - (p3.Y - p1.Y) * tension / 3);

                path.CubicTo(cp1, cp2, p2);
            }

            path.LineTo(points[^1].X, baselineY);
            path.Close();

            using var fillPaint = new SKPaint();
            fillPaint.Color = lineColor.WithAlpha(80);
            fillPaint.Style = SKPaintStyle.Fill;
            fillPaint.IsAntialias = true;
            canvas.DrawPath(path, fillPaint);
        }

        // Draw the line (skip for Scatter charts - only points)
        if (chart.ChartStyle != ReportChartStyle.Scatter)
        {
            using var linePaint = new SKPaint();
            linePaint.Color = lineColor;
            linePaint.Style = SKPaintStyle.Stroke;
            linePaint.StrokeWidth = 2 * _renderScale;
            linePaint.IsAntialias = true;
            linePaint.StrokeCap = SKStrokeCap.Round;
            linePaint.StrokeJoin = SKStrokeJoin.Round;

            using var linePath = new SKPath();
            linePath.MoveTo(points[0]);

            if (chart.ChartStyle == ReportChartStyle.StepLine)
            {
                // Step line - horizontal then vertical
                for (int i = 1; i < points.Length; i++)
                {
                    linePath.LineTo(points[i].X, points[i - 1].Y); // Horizontal
                    linePath.LineTo(points[i].X, points[i].Y);     // Vertical
                }
            }
            else
            {
                // Smooth spline using cubic Bezier curves (Catmull-Rom style)
                for (int i = 0; i < points.Length - 1; i++)
                {
                    var p0 = i > 0 ? points[i - 1] : points[i];
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    var p3 = i < points.Length - 2 ? points[i + 2] : points[i + 1];

                    // Calculate control points for cubic Bezier
                    var tension = 0.5f;
                    var cp1 = new SKPoint(
                        p1.X + (p2.X - p0.X) * tension / 3,
                        p1.Y + (p2.Y - p0.Y) * tension / 3);
                    var cp2 = new SKPoint(
                        p2.X - (p3.X - p1.X) * tension / 3,
                        p2.Y - (p3.Y - p1.Y) * tension / 3);

                    linePath.CubicTo(cp1, cp2, p2);
                }
            }

            canvas.DrawPath(linePath, linePaint);
        }

        // Draw points
        using var pointPaint = new SKPaint();
        pointPaint.Color = lineColor;
        pointPaint.Style = SKPaintStyle.Fill;
        pointPaint.IsAntialias = true;
        var pointRadius = 4 * _renderScale;

        foreach (var point in points)
        {
            canvas.DrawCircle(point, pointRadius, pointPaint);
        }

        // Draw X-axis labels - dynamically based on date range and available width
        using var xLabelFont = new SKFont(chartTypeface, (float)chart.AxisFontSize * _renderScale);
        using var xLabelPaint = new SKPaint();
        xLabelPaint.Color = ChartAxisColor;
        xLabelPaint.IsAntialias = true;

        var sampleLabel = FormatXAxisLabel(dataPoints[0]);
        var measuredLabelWidth = xLabelFont.MeasureText(sampleLabel, xLabelPaint);
        var labelIndices = GetXAxisLabelIndices(dataPoints, chartArea.Width, measuredLabelWidth, _renderScale);
        var labelY = chartArea.Bottom + 18 * _renderScale;

        DrawXAxisLabelsWithOverflowProtection(canvas, labelY, labelIndices,
            i => (FormatXAxisLabel(dataPoints[i]), points[i].X),
            xLabelFont, xLabelPaint, chartArea);
    }

    /// <summary>
    /// Renders a pie chart using SkiaSharp.
    /// Styled to match the dashboard/analytics pie charts: pie shifted left,
    /// legend vertically centered with circular color indicators, no slice borders.
    /// Respects the MaxPieSlices setting by grouping excess slices into "Other".
    /// </summary>
    private void RenderPieChart(SKCanvas canvas, SKRect chartArea, List<ChartDataPoint> dataPoints, ChartReportElement chart)
    {
        if (dataPoints.Count == 0) return;

        // Get typeface for chart labels
        var chartTypeface = PlatformTypefaces.Resolve(chart.FontFamily);

        // Calculate total value for percentages
        var total = dataPoints.Sum(p => Math.Abs(p.Value));
        if (total == 0) return;

        // --- Apply max pie slices setting (group excess into "Other") ---
        var maxSlices = _config.MaxPieSlices;
        var sorted = dataPoints.OrderByDescending(p => Math.Abs(p.Value)).ToList();
        var slices = new List<(string Label, double Value, SKColor Color)>();
        var colors = AppColors.Palette.Select(SKColor.Parse).ToArray();
        var otherColor = SKColor.Parse(AppColors.Gray);

        var topItems = sorted.Take(maxSlices - 1).ToList();
        var otherItems = sorted.Skip(maxSlices - 1).ToList();

        // If there's only one "other" item, show it directly instead of grouping
        if (otherItems.Count == 1)
        {
            topItems.Add(otherItems[0]);
            otherItems.Clear();
        }

        for (int i = 0; i < topItems.Count; i++)
            slices.Add((topItems[i].Label, Math.Abs(topItems[i].Value), colors[i % colors.Length]));

        if (otherItems.Count > 0)
        {
            var otherValue = otherItems.Sum(p => Math.Abs(p.Value));
            var itemsText = Tr("items");
            slices.Add(($"{Tr("Other")} ({otherItems.Count} {itemsText})", otherValue, otherColor));
        }

        // --- Layout ---
        var pieSize = Math.Min(chartArea.Width * 0.5f, chartArea.Height) * 0.85f;
        var radius = pieSize / 2;
        if (radius <= 0) return;

        var centerX = chartArea.Left + chartArea.Width * 0.3f;
        var centerY = chartArea.MidY;

        var startAngle = -90f;

        using var labelFont = new SKFont(chartTypeface, (float)chart.LegendFontSize * _renderScale);
        using var labelPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        var pieRect = new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);

        // --- Draw all pie slices (no white borders) ---
        foreach (var slice in slices)
        {
            var percentage = (float)(slice.Value / total);
            var sweepAngle = percentage * 360f;

            using var slicePaint = new SKPaint { Color = slice.Color, Style = SKPaintStyle.Fill, IsAntialias = true };

            if (sweepAngle >= 359.9f)
            {
                canvas.DrawOval(pieRect, slicePaint);
            }
            else if (sweepAngle > 0)
            {
                using var path = new SKPath();
                path.MoveTo(centerX, centerY);
                path.ArcTo(pieRect, startAngle, sweepAngle, false);
                path.Close();
                canvas.DrawPath(path, slicePaint);
            }

            startAngle += sweepAngle;
        }

        // --- Draw legend to the right of the pie, vertically centered ---
        if (chart.ShowLegend)
        {
            var legendX = chartArea.Left + chartArea.Width * 0.58f;
            var percentRightEdge = chartArea.Right - 5 * _renderScale;

            if (legendX < chartArea.Right - 50 * _renderScale)
            {
                var lineHeight = 18 * _renderScale;
                var totalLegendHeight = slices.Count * lineHeight;
                var legendY = chartArea.MidY - totalLegendHeight / 2;
                var circleRadius = 5 * _renderScale;

                using var percentPaint = new SKPaint { Color = new SKColor(160, 160, 160), IsAntialias = true };

                foreach (var slice in slices)
                {
                    var percentage = (float)(slice.Value / total);

                    using var dotPaint = new SKPaint { Color = slice.Color, Style = SKPaintStyle.Fill, IsAntialias = true };

                    // Circular color indicator
                    var dotCenterY = legendY + circleRadius;
                    canvas.DrawCircle(legendX + circleRadius, dotCenterY, circleRadius, dotPaint);

                    var legendText = slice.Label;
                    var maxChars = chart.LegendMaxCharacters;
                    if (legendText.Length > maxChars) legendText = legendText[..maxChars] + "...";

                    var textX = legendX + circleRadius * 2 + 6 * _renderScale;
                    var textY = dotCenterY + circleRadius * 0.4f;
                    canvas.DrawText(legendText, textX, textY, SKTextAlign.Left, labelFont, labelPaint);

                    // Percentage text (lighter gray, right-aligned to chart edge)
                    var percentText = $"{percentage * 100:F1}%";
                    canvas.DrawText(percentText, percentRightEdge, textY, SKTextAlign.Right, labelFont, percentPaint);

                    legendY += lineHeight;
                }
            }
        }
    }

    /// <summary>
    /// Renders a multi-series bar chart (e.g., Revenue vs Expenses).
    /// </summary>
    private void RenderMultiSeriesBarChart(SKCanvas canvas, SKRect chartArea, List<ChartSeriesData> seriesData, ChartReportElement chart)
    {
        if (seriesData.Count == 0) return;

        // Get typeface for chart labels
        var chartTypeface = PlatformTypefaces.Resolve(chart.FontFamily);

        // Get all data points and find max value
        var allDataPoints = seriesData.SelectMany(s => s.DataPoints).ToList();
        if (allDataPoints.Count == 0) return;

        var maxValue = allDataPoints.Max(p => p.Value);
        var minValue = allDataPoints.Min(p => p.Value);
        if (maxValue == 0 && minValue == 0) maxValue = 1;

        // Determine if we have negative values
        var hasNegatives = minValue < 0;

        // 20% headroom above the max so the tallest point isn't flush with the top edge.
        // The floor stays pinned at 0 unless the data goes negative, in which case the
        // bottom gets the same 20%.
        var paddedMaxValue = maxValue > 0 ? maxValue * 1.2 : maxValue;
        var paddedMinValue = hasNegatives ? minValue * 1.2 : 0;

        // Calculate the total range the chart needs to display
        var totalRange = paddedMaxValue - paddedMinValue;
        if (totalRange == 0) totalRange = 1;

        // Calculate baseline position (where value = 0)
        var baselineY = hasNegatives
            ? chartArea.Top + chartArea.Height * (float)(paddedMaxValue / totalRange)
            : chartArea.Bottom;

        // Draw grid lines
        using var gridPaint = new SKPaint();
        gridPaint.Color = ChartGridColor;
        gridPaint.Style = SKPaintStyle.Stroke;
        gridPaint.StrokeWidth = 1 * _renderScale;
        gridPaint.IsAntialias = true;

        var gridLineCount = 5;
        for (int i = 0; i <= gridLineCount; i++)
        {
            var y = chartArea.Top + (chartArea.Height * i / gridLineCount);
            canvas.DrawLine(chartArea.Left, y, chartArea.Right, y, gridPaint);

            // Draw Y-axis value labels using the full padded range
            var value = paddedMaxValue - totalRange * i / gridLineCount;
            using var yLabelFont = new SKFont(chartTypeface, (float)chart.AxisFontSize * _renderScale);
            using var yLabelPaint = new SKPaint();
            yLabelPaint.Color = ChartAxisColor;
            yLabelPaint.IsAntialias = true;

            canvas.DrawText(FormatYAxisValue(value, chart.ChartType), chartArea.Left - 5 * _renderScale, y + 4 * _renderScale, SKTextAlign.Right, yLabelFont, yLabelPaint);
        }

        // Draw axes
        using var axisPaint = new SKPaint();
        axisPaint.Color = ChartAxisColor;
        axisPaint.Style = SKPaintStyle.Stroke;
        axisPaint.StrokeWidth = 1 * _renderScale;
        axisPaint.IsAntialias = true;
        canvas.DrawLine(chartArea.Left, chartArea.Top, chartArea.Left, chartArea.Bottom, axisPaint);
        canvas.DrawLine(chartArea.Left, baselineY, chartArea.Right, baselineY, axisPaint);

        // Get unique labels (X-axis categories) - use formatted dates if available
        var firstSeriesPoints = seriesData.First().DataPoints;
        var labels = firstSeriesPoints.Select(FormatXAxisLabel).ToList();
        var categoryCount = labels.Count;
        var seriesCount = seriesData.Count;

        if (categoryCount == 0) return;

        // Calculate bar dimensions - ensure bars are properly sized and centered
        var categoryWidth = chartArea.Width / categoryCount;
        var barSpacing = 4 * _renderScale;
        var maxBarWidth = 40 * _renderScale;
        var totalBarSpacePerCategory = categoryWidth * 0.8f; // Use 80% of category width for bars
        var barWidth = Math.Min(maxBarWidth, (totalBarSpacePerCategory - (barSpacing * (seriesCount - 1))) / seriesCount);

        // Draw bars for each category
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            var categoryCenterX = chartArea.Left + (categoryIndex * categoryWidth) + (categoryWidth / 2);
            var totalBarsWidth = (barWidth * seriesCount) + (barSpacing * (seriesCount - 1));
            var barStartX = categoryCenterX - (totalBarsWidth / 2);

            // Draw bars for each series
            for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                var series = seriesData[seriesIndex];
                if (categoryIndex >= series.DataPoints.Count) continue;

                var point = series.DataPoints[categoryIndex];
                var x = barStartX + (seriesIndex * (barWidth + barSpacing));

                // Calculate bar height based on value relative to total range
                var barHeight = chartArea.Height * (float)(Math.Abs(point.Value) / totalRange);

                var barColor = SKColor.Parse(series.Color);
                using var barPaint = new SKPaint();
                barPaint.Color = barColor;
                barPaint.Style = SKPaintStyle.Fill;
                barPaint.IsAntialias = true;

                var barRect = point.Value >= 0
                    ? new SKRect(x, baselineY - barHeight, x + barWidth, baselineY)
                    : new SKRect(x, baselineY, x + barWidth, baselineY + barHeight);

                canvas.DrawRect(barRect, barPaint);
            }
        }

        // Draw X-axis labels - dynamically based on date range and available width
        using var xLabelFont = new SKFont(chartTypeface, (float)chart.AxisFontSize * _renderScale);
        using var xLabelPaint = new SKPaint();
        xLabelPaint.Color = ChartAxisColor;
        xLabelPaint.IsAntialias = true;

        var sampleLabel = labels.First();
        var measuredLabelWidth = xLabelFont.MeasureText(sampleLabel, xLabelPaint);
        var labelIndices = GetXAxisLabelIndices(firstSeriesPoints, chartArea.Width, measuredLabelWidth, _renderScale);
        var labelY = chartArea.Bottom + 18 * _renderScale;

        DrawXAxisLabelsWithOverflowProtection(canvas, labelY, labelIndices,
            categoryIndex => (labels[categoryIndex], chartArea.Left + (categoryIndex * categoryWidth) + (categoryWidth / 2)),
            xLabelFont, xLabelPaint, chartArea);
    }

    /// <summary>
    /// Renders a multi-series line/area chart (e.g., Revenue vs Expenses as lines).
    /// </summary>
    private void RenderMultiSeriesLineChart(SKCanvas canvas, SKRect chartArea, List<ChartSeriesData> seriesData, ChartReportElement chart)
    {
        if (seriesData.Count == 0) return;

        // Get typeface for chart labels
        var chartTypeface = PlatformTypefaces.Resolve(chart.FontFamily);

        // Get all data points and find max/min values
        var allDataPoints = seriesData.SelectMany(s => s.DataPoints).ToList();
        if (allDataPoints.Count == 0) return;

        var maxValue = allDataPoints.Max(p => p.Value);
        var minValue = allDataPoints.Min(p => p.Value);
        if (maxValue == 0 && minValue == 0) maxValue = 1;

        // Determine if we have negative values
        var hasNegatives = minValue < 0;

        // 20% headroom above the max so the tallest point isn't flush with the top edge.
        // The floor stays pinned at 0 unless the data goes negative, in which case the
        // bottom gets the same 20%.
        var paddedMaxValue = maxValue > 0 ? maxValue * 1.2 : maxValue;
        var paddedMinValue = hasNegatives ? minValue * 1.2 : 0;

        // Calculate the total range the chart needs to display
        var totalRange = paddedMaxValue - paddedMinValue;
        if (totalRange == 0) totalRange = 1;

        // Calculate baseline position (where value = 0)
        var baselineY = hasNegatives
            ? chartArea.Top + chartArea.Height * (float)(paddedMaxValue / totalRange)
            : chartArea.Bottom;

        // Draw grid lines
        using var gridPaint = new SKPaint();
        gridPaint.Color = ChartGridColor;
        gridPaint.Style = SKPaintStyle.Stroke;
        gridPaint.StrokeWidth = 1 * _renderScale;
        gridPaint.IsAntialias = true;

        var gridLineCount = 5;
        for (int i = 0; i <= gridLineCount; i++)
        {
            var y = chartArea.Top + (chartArea.Height * i / gridLineCount);
            canvas.DrawLine(chartArea.Left, y, chartArea.Right, y, gridPaint);

            // Draw Y-axis value labels using the full padded range
            var value = paddedMaxValue - totalRange * i / gridLineCount;
            using var yLabelFont = new SKFont(chartTypeface, (float)chart.AxisFontSize * _renderScale);
            using var yLabelPaint = new SKPaint();
            yLabelPaint.Color = ChartAxisColor;
            yLabelPaint.IsAntialias = true;

            canvas.DrawText(FormatYAxisValue(value, chart.ChartType), chartArea.Left - 5 * _renderScale, y + 4 * _renderScale, SKTextAlign.Right, yLabelFont, yLabelPaint);
        }

        // Draw axes
        using var axisPaint = new SKPaint();
        axisPaint.Color = ChartAxisColor;
        axisPaint.Style = SKPaintStyle.Stroke;
        axisPaint.StrokeWidth = 1 * _renderScale;
        axisPaint.IsAntialias = true;
        canvas.DrawLine(chartArea.Left, chartArea.Top, chartArea.Left, chartArea.Bottom, axisPaint);
        canvas.DrawLine(chartArea.Left, baselineY, chartArea.Right, baselineY, axisPaint);

        // Get unique labels (X-axis categories)
        var firstSeriesPoints = seriesData.First().DataPoints;
        var pointCount = firstSeriesPoints.Count;
        if (pointCount == 0) return;

        var xSpacing = chartArea.Width / Math.Max(1, pointCount - 1);

        // Render each series
        foreach (var series in seriesData)
        {
            if (series.DataPoints.Count == 0) continue;

            var lineColor = SKColor.Parse(series.Color);

            // Calculate points for this series
            var points = new SKPoint[series.DataPoints.Count];
            for (int i = 0; i < series.DataPoints.Count; i++)
            {
                var x = chartArea.Left + (i * xSpacing);
                if (pointCount == 1) x = chartArea.MidX;
                // Map value to Y position: paddedMaxValue at top, paddedMinValue at bottom
                var y = chartArea.Top + chartArea.Height * (float)((paddedMaxValue - series.DataPoints[i].Value) / totalRange);
                points[i] = new SKPoint(x, y);
            }

            // Render based on chart style
            if (chart.ChartStyle == ReportChartStyle.Area)
            {
                // Draw filled area under line
                using var path = new SKPath();
                path.MoveTo(points[0].X, baselineY);
                path.LineTo(points[0].X, points[0].Y);

                // Use cubic Bezier curves for smooth spline
                for (int i = 0; i < points.Length - 1; i++)
                {
                    var p0 = i > 0 ? points[i - 1] : points[i];
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    var p3 = i < points.Length - 2 ? points[i + 2] : points[i + 1];

                    var tension = 0.5f;
                    var cp1 = new SKPoint(
                        p1.X + (p2.X - p0.X) * tension / 3,
                        p1.Y + (p2.Y - p0.Y) * tension / 3);
                    var cp2 = new SKPoint(
                        p2.X - (p3.X - p1.X) * tension / 3,
                        p2.Y - (p3.Y - p1.Y) * tension / 3);

                    path.CubicTo(cp1, cp2, p2);
                }

                path.LineTo(points[^1].X, baselineY);
                path.Close();

                using var fillPaint = new SKPaint();
                fillPaint.Color = lineColor.WithAlpha(80);
                fillPaint.Style = SKPaintStyle.Fill;
                fillPaint.IsAntialias = true;
                canvas.DrawPath(path, fillPaint);
            }

            // Draw the line (skip for Scatter charts - only points)
            if (chart.ChartStyle != ReportChartStyle.Scatter)
            {
                using var linePaint = new SKPaint();
                linePaint.Color = lineColor;
                linePaint.Style = SKPaintStyle.Stroke;
                linePaint.StrokeWidth = 2 * _renderScale;
                linePaint.IsAntialias = true;
                linePaint.StrokeCap = SKStrokeCap.Round;
                linePaint.StrokeJoin = SKStrokeJoin.Round;

                using var linePath = new SKPath();
                linePath.MoveTo(points[0]);

                if (chart.ChartStyle == ReportChartStyle.StepLine)
                {
                    // Step line - horizontal then vertical
                    for (int i = 1; i < points.Length; i++)
                    {
                        linePath.LineTo(points[i].X, points[i - 1].Y);
                        linePath.LineTo(points[i].X, points[i].Y);
                    }
                }
                else
                {
                    // Smooth spline using cubic Bezier curves
                    for (int i = 0; i < points.Length - 1; i++)
                    {
                        var p0 = i > 0 ? points[i - 1] : points[i];
                        var p1 = points[i];
                        var p2 = points[i + 1];
                        var p3 = i < points.Length - 2 ? points[i + 2] : points[i + 1];

                        var tension = 0.5f;
                        var cp1 = new SKPoint(
                            p1.X + (p2.X - p0.X) * tension / 3,
                            p1.Y + (p2.Y - p0.Y) * tension / 3);
                        var cp2 = new SKPoint(
                            p2.X - (p3.X - p1.X) * tension / 3,
                            p2.Y - (p3.Y - p1.Y) * tension / 3);

                        linePath.CubicTo(cp1, cp2, p2);
                    }
                }

                canvas.DrawPath(linePath, linePaint);
            }

            // Draw points
            using var pointPaint = new SKPaint();
            pointPaint.Color = lineColor;
            pointPaint.Style = SKPaintStyle.Fill;
            pointPaint.IsAntialias = true;
            var pointRadius = 4 * _renderScale;

            foreach (var point in points)
            {
                canvas.DrawCircle(point, pointRadius, pointPaint);
            }
        }

        // Draw X-axis labels
        using var xLabelFont = new SKFont(chartTypeface, (float)chart.AxisFontSize * _renderScale);
        using var xLabelPaint = new SKPaint();
        xLabelPaint.Color = ChartAxisColor;
        xLabelPaint.IsAntialias = true;

        var sampleLabel = FormatXAxisLabel(firstSeriesPoints[0]);
        var measuredLabelWidth = xLabelFont.MeasureText(sampleLabel, xLabelPaint);
        var labelIndices = GetXAxisLabelIndices(firstSeriesPoints, chartArea.Width, measuredLabelWidth, _renderScale);
        var labelY = chartArea.Bottom + 18 * _renderScale;

        DrawXAxisLabelsWithOverflowProtection(canvas, labelY, labelIndices,
            i => (FormatXAxisLabel(firstSeriesPoints[i]), chartArea.Left + (i * xSpacing)),
            xLabelFont, xLabelPaint, chartArea);
    }

    /// <summary>
    /// Renders a GeoMap chart using LiveCharts2 SKGeoMap for headless rendering.
    /// </summary>
    private void RenderGeoMap(SKCanvas canvas, SKRect chartArea)
    {
        var mapData = GetWorldMapData();

        if (mapData == null || mapData.Count == 0)
        {
            DrawNoDataPlaceholder(canvas, chartArea);
            return;
        }

        // Convert country names to ISO codes for HeatLandSeries
        var lands = mapData
            .Select(kvp =>
            {
                var isoCode = CountryCodeMapping.GetIsoCode(kvp.Key);
                return !string.IsNullOrEmpty(isoCode) ? new HeatLand { Name = isoCode, Value = kvp.Value } : null;
            })
            .Where(l => l != null)
            .Cast<HeatLand>()
            .ToArray();

        if (lands.Length == 0)
        {
            DrawNoDataPlaceholder(canvas, chartArea);
            return;
        }

        var width = (int)chartArea.Width;
        var height = (int)chartArea.Height;
        if (width <= 0 || height <= 0) return;

        try
        {
            var geoMap = new SKGeoMap
            {
                Width = width,
                Height = height,
                Series = [new HeatLandSeries { Lands = lands }],
                MapProjection = LiveChartsCore.Geo.MapProjection.Mercator
            };

            using var image = geoMap.GetImage();
            canvas.DrawImage(image, chartArea.Left, chartArea.Top);
        }
        catch (Exception ex)
        {
            // Fallback to placeholder if SKGeoMap rendering fails
            _errorLogger?.LogError(ex, ErrorCategory.UI, "GeoMap render failed");
            DrawNoDataPlaceholder(canvas, chartArea);
        }
    }

    private void RenderTable(SKCanvas canvas, TableReportElement table)
    {
        var rect = GetScaledRect(table);
        var columns = GetVisibleColumns(table);

        var tableData = GetTableData(table, columns);

        var headerRowHeight = table.HeaderRowHeight * _renderScale;
        var dataRowHeight = table.DataRowHeight * _renderScale;
        var titleRowHeight = headerRowHeight; // Title uses same height as header

        // Create fonts using table's font settings
        var titleTypeface = PlatformTypefaces.Resolve(table.TitleFontFamily, SKFontStyle.Bold);
        var headerTypeface = PlatformTypefaces.Resolve(table.HeaderFontFamily, SKFontStyle.Bold);
        var dataTypeface = PlatformTypefaces.Resolve(table.FontFamily);
        using var titleFont = new SKFont(titleTypeface, (float)table.TitleFontSize * _renderScale);
        using var headerFont = new SKFont(headerTypeface, (float)table.HeaderFontSize * _renderScale);
        using var dataFont = new SKFont(dataTypeface, (float)table.FontSize * _renderScale);

        var textAlign = table.TextAlignment switch
        {
            HorizontalTextAlignment.Left => SKTextAlign.Left,
            HorizontalTextAlignment.Right => SKTextAlign.Right,
            _ => SKTextAlign.Center
        };
        var cellPadding = table.CellPadding * _renderScale;

        // Calculate available height for data rows
        var availableHeight = rect.Height;
        if (table.ShowTitle)
            availableHeight -= titleRowHeight;
        if (table.ShowHeaders)
            availableHeight -= headerRowHeight;
        if (table.ShowTotalsRow)
            availableHeight -= headerRowHeight; // Totals row uses header row height

        // Calculate max rows that fit in available space
        var maxRowsByHeight = (int)Math.Floor(availableHeight / dataRowHeight);
        var maxRowsSetting = table.MaxRows > 0 ? table.MaxRows : int.MaxValue;
        var dataRowCount = Math.Min(Math.Min(tableData.Count, maxRowsSetting), Math.Max(0, maxRowsByHeight));

        var columnWidths = CalculateColumnWidths(table, columns, tableData, rect.Width, headerFont, dataFont);

        // Calculate actual table height (constrained to element bounds)
        var actualHeight = (table.ShowTitle ? titleRowHeight : 0) +
                          (table.ShowHeaders ? headerRowHeight : 0) +
                          (dataRowCount * dataRowHeight) +
                          (table.ShowTotalsRow ? headerRowHeight : 0);
        actualHeight = Math.Min(actualHeight, rect.Height);
        var tableRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + actualHeight);

        // Draw table background only behind actual table content (not the full element bounds)
        canvas.DrawRect(tableRect, new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill });

        var currentY = rect.Top;

        // Draw title row
        if (table.ShowTitle)
        {
            var titleRect = new SKRect(rect.Left, currentY, rect.Right, currentY + titleRowHeight);
            var titleFill = new SKPaint { Color = ParseColor(table.TitleBackgroundColor), Style = SKPaintStyle.Fill };
            canvas.DrawRect(titleRect, titleFill);

            var titleText = BuildTableTitle(table);

            using var titleTextPaint = new SKPaint();
            titleTextPaint.Color = ParseColor(table.TitleTextColor);
            titleTextPaint.IsAntialias = true;

            var x = titleRect.MidX;
            var y = titleRect.MidY + (float)(table.TitleFontSize * _renderScale) / 3;
            canvas.DrawText(titleText, x, y, SKTextAlign.Center, titleFont, titleTextPaint);

            // Draw title bottom border
            if (table.ShowGridLines)
            {
                var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                canvas.DrawLine(rect.Left, titleRect.Bottom, rect.Right, titleRect.Bottom, gridPaint);
            }

            currentY += titleRowHeight;
        }

        // Draw header row
        if (table.ShowHeaders)
        {
            var headerRect = new SKRect(rect.Left, currentY, rect.Right, currentY + headerRowHeight);
            var headerFill = new SKPaint { Color = ParseColor(table.HeaderBackgroundColor), Style = SKPaintStyle.Fill };
            canvas.DrawRect(headerRect, headerFill);

            // Draw column headers
            using var headerTextPaint = new SKPaint();
            headerTextPaint.Color = ParseColor(table.HeaderTextColor);
            headerTextPaint.IsAntialias = true;

            float colX = rect.Left;
            for (int i = 0; i < columns.Count; i++)
            {
                var colWidth = columnWidths[i];
                var y = headerRect.MidY + (float)(table.HeaderFontSize * _renderScale) / 3;
                var truncatedText = TruncateText(columns[i], colWidth - cellPadding * 2, headerFont);
                DrawAlignedText(canvas, truncatedText, colX, colWidth, y, cellPadding, textAlign, headerFont, headerTextPaint);

                // Draw vertical grid line
                if (table.ShowGridLines && i > 0)
                {
                    var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                    canvas.DrawLine(colX, headerRect.Top, colX, headerRect.Bottom, gridPaint);
                }

                colX += colWidth;
            }

            // Draw header bottom border
            if (table.ShowGridLines)
            {
                var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                canvas.DrawLine(rect.Left, headerRect.Bottom, rect.Right, headerRect.Bottom, gridPaint);
            }

            currentY += headerRowHeight;
        }

        // Draw data rows
        using var dataTextPaint = new SKPaint();
        dataTextPaint.Color = ParseColor(table.DataRowTextColor);
        dataTextPaint.IsAntialias = true;

        for (int rowIndex = 0; rowIndex < dataRowCount; rowIndex++)
        {
            var rowData = tableData[rowIndex];
            var rowRect = new SKRect(rect.Left, currentY, rect.Right, currentY + dataRowHeight);

            // Alternate row colors
            var isAlternate = rowIndex % 2 == 1;
            var rowBgColor = table.AlternateRowColors && isAlternate
                ? ParseColor(table.AlternateRowColor)
                : ParseColor(table.BaseRowColor);
            canvas.DrawRect(rowRect, new SKPaint { Color = rowBgColor, Style = SKPaintStyle.Fill });

            // Draw cell data
            float colX = rect.Left;
            for (int colIndex = 0; colIndex < columns.Count; colIndex++)
            {
                var colWidth = columnWidths[colIndex];
                var cellText = colIndex < rowData.Count ? rowData[colIndex] : "";
                var y = rowRect.MidY + (float)(table.FontSize * _renderScale) / 3;
                var truncatedText = TruncateText(cellText, colWidth - cellPadding * 2, dataFont);
                DrawAlignedText(canvas, truncatedText, colX, colWidth, y, cellPadding, textAlign, dataFont, dataTextPaint);

                // Draw vertical grid line
                if (table.ShowGridLines && colIndex > 0)
                {
                    var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                    canvas.DrawLine(colX, rowRect.Top, colX, rowRect.Bottom, gridPaint);
                }

                colX += colWidth;
            }

            // Draw row bottom border
            if (table.ShowGridLines)
            {
                var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                canvas.DrawLine(rect.Left, rowRect.Bottom, rect.Right, rowRect.Bottom, gridPaint);
            }

            currentY += dataRowHeight;
        }

        // Draw totals row
        if (table.ShowTotalsRow)
        {
            var totalsRect = new SKRect(rect.Left, currentY, rect.Right, currentY + headerRowHeight);
            var totalsFill = new SKPaint { Color = ParseColor(table.HeaderBackgroundColor), Style = SKPaintStyle.Fill };
            canvas.DrawRect(totalsRect, totalsFill);

            // Calculate totals from displayed data
            var totals = CalculateTableTotals(tableData, columns, dataRowCount);

            using var totalsTextPaint = new SKPaint();
            totalsTextPaint.Color = ParseColor(table.HeaderTextColor);
            totalsTextPaint.IsAntialias = true;

            float colX = rect.Left;
            for (int i = 0; i < columns.Count; i++)
            {
                var colWidth = columnWidths[i];
                var y = totalsRect.MidY + (float)(table.HeaderFontSize * _renderScale) / 3;

                string cellText;
                if (i == 0)
                    cellText = Tr("Total");
                else if (totals.TryGetValue(columns[i], out var total))
                    cellText = total;
                else
                    cellText = "";

                var truncatedText = TruncateText(cellText, colWidth - cellPadding * 2, headerFont);
                DrawAlignedText(canvas, truncatedText, colX, colWidth, y, cellPadding, textAlign, headerFont, totalsTextPaint);

                // Draw vertical grid line
                if (table.ShowGridLines && i > 0)
                {
                    var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                    canvas.DrawLine(colX, totalsRect.Top, colX, totalsRect.Bottom, gridPaint);
                }

                colX += colWidth;
            }
        }

        // Draw outer border
        _borderPaint.Color = ParseColor(table.GridLineColor);
        canvas.DrawRect(tableRect, _borderPaint);
    }

    /// <summary>
    /// Renders a slice of a normal table (for pagination support).
    /// Similar to RenderTable but only renders rows from startRowIndex to startRowIndex+rowCount.
    /// </summary>
    private void RenderTableSlice(
        SKCanvas canvas,
        TableReportElement table,
        List<List<string>> tableData,
        List<string> columns,
        int startRowIndex,
        int rowCount,
        int dataRowStartIndex,
        bool showTitle,
        bool showHeaders,
        bool showTotalsRow,
        bool showContinuedIndicator,
        SKRect? overrideRect = null)
    {
        var rect = overrideRect ?? GetScaledRect(table);

        var headerRowHeight = table.HeaderRowHeight * _renderScale;
        var dataRowHeight = table.DataRowHeight * _renderScale;
        var titleRowHeight = headerRowHeight;
        var continuedIndicatorHeight = dataRowHeight * 0.8f;

        // Create fonts using table's font settings
        var titleTypeface = PlatformTypefaces.Resolve(table.TitleFontFamily, SKFontStyle.Bold);
        var headerTypeface = PlatformTypefaces.Resolve(table.HeaderFontFamily, SKFontStyle.Bold);
        var dataTypeface = PlatformTypefaces.Resolve(table.FontFamily);
        using var titleFont = new SKFont(titleTypeface, (float)table.TitleFontSize * _renderScale);
        using var headerFont = new SKFont(headerTypeface, (float)table.HeaderFontSize * _renderScale);
        using var dataFont = new SKFont(dataTypeface, (float)table.FontSize * _renderScale);

        var textAlign = table.TextAlignment switch
        {
            HorizontalTextAlignment.Left => SKTextAlign.Left,
            HorizontalTextAlignment.Right => SKTextAlign.Right,
            _ => SKTextAlign.Center
        };
        var cellPadding = table.CellPadding * _renderScale;

        // Calculate column widths based on full data set
        var columnWidths = CalculateColumnWidths(table, columns, tableData, rect.Width, headerFont, dataFont);

        // Calculate actual table height for this slice
        var actualHeight = (showTitle ? titleRowHeight : 0) +
                          (showContinuedIndicator ? continuedIndicatorHeight : 0) +
                          (showHeaders ? headerRowHeight : 0) +
                          (rowCount * dataRowHeight) +
                          (showTotalsRow ? headerRowHeight : 0);
        actualHeight = Math.Min(actualHeight, rect.Height);
        var tableRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + actualHeight);

        // Draw table background
        canvas.DrawRect(tableRect, new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill });

        var currentY = rect.Top;

        // Draw title row
        if (showTitle)
        {
            var titleRect = new SKRect(rect.Left, currentY, rect.Right, currentY + titleRowHeight);
            var titleFill = new SKPaint { Color = ParseColor(table.TitleBackgroundColor), Style = SKPaintStyle.Fill };
            canvas.DrawRect(titleRect, titleFill);

            var titleText = BuildTableTitle(table);

            using var titleTextPaint = new SKPaint();
            titleTextPaint.Color = ParseColor(table.TitleTextColor);
            titleTextPaint.IsAntialias = true;

            var x = titleRect.MidX;
            var y = titleRect.MidY + (float)(table.TitleFontSize * _renderScale) / 3;
            canvas.DrawText(titleText, x, y, SKTextAlign.Center, titleFont, titleTextPaint);

            if (table.ShowGridLines)
            {
                var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                canvas.DrawLine(rect.Left, titleRect.Bottom, rect.Right, titleRect.Bottom, gridPaint);
            }

            currentY += titleRowHeight;
        }

        // Draw "(Continued)" indicator
        if (showContinuedIndicator)
        {
            var continuedRect = new SKRect(rect.Left, currentY, rect.Right, currentY + continuedIndicatorHeight);
            var continuedFill = new SKPaint { Color = ParseColor(table.TitleBackgroundColor), Style = SKPaintStyle.Fill };
            canvas.DrawRect(continuedRect, continuedFill);

            using var continuedFont = new SKFont(titleTypeface, (float)table.FontSize * 0.85f * _renderScale);
            using var continuedPaint = new SKPaint();
            continuedPaint.Color = ParseColor(table.TitleTextColor);
            continuedPaint.IsAntialias = true;

            var continuedText = Tr("(Continued)");
            var y = continuedRect.MidY + (float)(table.FontSize * 0.85f * _renderScale) / 3;
            canvas.DrawText(continuedText, continuedRect.MidX, y, SKTextAlign.Center, continuedFont, continuedPaint);

            if (table.ShowGridLines)
            {
                var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                canvas.DrawLine(rect.Left, continuedRect.Bottom, rect.Right, continuedRect.Bottom, gridPaint);
            }

            currentY += continuedIndicatorHeight;
        }

        // Draw header row
        if (showHeaders)
        {
            var headerRect = new SKRect(rect.Left, currentY, rect.Right, currentY + headerRowHeight);
            var headerFill = new SKPaint { Color = ParseColor(table.HeaderBackgroundColor), Style = SKPaintStyle.Fill };
            canvas.DrawRect(headerRect, headerFill);

            using var headerTextPaint = new SKPaint();
            headerTextPaint.Color = ParseColor(table.HeaderTextColor);
            headerTextPaint.IsAntialias = true;

            float colX = rect.Left;
            for (int i = 0; i < columns.Count; i++)
            {
                var colWidth = columnWidths[i];
                var y = headerRect.MidY + (float)(table.HeaderFontSize * _renderScale) / 3;
                var truncatedText = TruncateText(columns[i], colWidth - cellPadding * 2, headerFont);
                DrawAlignedText(canvas, truncatedText, colX, colWidth, y, cellPadding, textAlign, headerFont, headerTextPaint);

                if (table.ShowGridLines && i > 0)
                {
                    var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                    canvas.DrawLine(colX, headerRect.Top, colX, headerRect.Bottom, gridPaint);
                }

                colX += colWidth;
            }

            if (table.ShowGridLines)
            {
                var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                canvas.DrawLine(rect.Left, headerRect.Bottom, rect.Right, headerRect.Bottom, gridPaint);
            }

            currentY += headerRowHeight;
        }

        // Draw data rows
        using var dataTextPaint = new SKPaint();
        dataTextPaint.Color = ParseColor(table.DataRowTextColor);
        dataTextPaint.IsAntialias = true;

        for (int i = 0; i < rowCount && (startRowIndex + i) < tableData.Count; i++)
        {
            var rowData = tableData[startRowIndex + i];
            var rowRect = new SKRect(rect.Left, currentY, rect.Right, currentY + dataRowHeight);

            // Alternate row colors (using dataRowStartIndex for continuity)
            var isAlternate = (dataRowStartIndex + i) % 2 == 1;
            var rowBgColor = table.AlternateRowColors && isAlternate
                ? ParseColor(table.AlternateRowColor)
                : ParseColor(table.BaseRowColor);
            canvas.DrawRect(rowRect, new SKPaint { Color = rowBgColor, Style = SKPaintStyle.Fill });

            float colX = rect.Left;
            for (int colIndex = 0; colIndex < columns.Count; colIndex++)
            {
                var colWidth = columnWidths[colIndex];
                var cellText = colIndex < rowData.Count ? rowData[colIndex] : "";
                var y = rowRect.MidY + (float)(table.FontSize * _renderScale) / 3;
                var truncatedText = TruncateText(cellText, colWidth - cellPadding * 2, dataFont);
                DrawAlignedText(canvas, truncatedText, colX, colWidth, y, cellPadding, textAlign, dataFont, dataTextPaint);

                if (table.ShowGridLines && colIndex > 0)
                {
                    var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                    canvas.DrawLine(colX, rowRect.Top, colX, rowRect.Bottom, gridPaint);
                }

                colX += colWidth;
            }

            if (table.ShowGridLines)
            {
                var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                canvas.DrawLine(rect.Left, rowRect.Bottom, rect.Right, rowRect.Bottom, gridPaint);
            }

            currentY += dataRowHeight;
        }

        // Draw totals row
        if (showTotalsRow)
        {
            var totalsRect = new SKRect(rect.Left, currentY, rect.Right, currentY + headerRowHeight);
            var totalsFill = new SKPaint { Color = ParseColor(table.HeaderBackgroundColor), Style = SKPaintStyle.Fill };
            canvas.DrawRect(totalsRect, totalsFill);

            // Apply MaxRows limit for totals calculation
            var maxRowsSetting = table.MaxRows > 0 ? table.MaxRows : int.MaxValue;
            var totalDataRows = Math.Min(tableData.Count, maxRowsSetting);
            var totals = CalculateTableTotals(tableData, columns, totalDataRows);

            using var totalsTextPaint = new SKPaint();
            totalsTextPaint.Color = ParseColor(table.HeaderTextColor);
            totalsTextPaint.IsAntialias = true;

            float colX = rect.Left;
            for (int i = 0; i < columns.Count; i++)
            {
                var colWidth = columnWidths[i];
                var y = totalsRect.MidY + (float)(table.HeaderFontSize * _renderScale) / 3;

                string cellText;
                if (i == 0)
                    cellText = Tr("Total");
                else if (totals.TryGetValue(columns[i], out var total))
                    cellText = total;
                else
                    cellText = "";

                var truncatedText = TruncateText(cellText, colWidth - cellPadding * 2, headerFont);
                DrawAlignedText(canvas, truncatedText, colX, colWidth, y, cellPadding, textAlign, headerFont, totalsTextPaint);

                if (table.ShowGridLines && i > 0)
                {
                    var gridPaint = new SKPaint { Color = ParseColor(table.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale };
                    canvas.DrawLine(colX, totalsRect.Top, colX, totalsRect.Bottom, gridPaint);
                }

                colX += colWidth;
            }
        }

        // Draw outer border
        _borderPaint.Color = ParseColor(table.GridLineColor);
        canvas.DrawRect(tableRect, _borderPaint);
    }

    private string BuildTableTitle(TableReportElement table)
    {
        var parts = new List<string>();

        var typeText = table.TransactionType switch
        {
            TransactionType.Revenue => Tr("Revenue"),
            TransactionType.Expenses => Tr("Expenses"),
            TransactionType.Invoices => Tr("Invoices"),
            TransactionType.Payments => Tr("Payments"),
            TransactionType.RentalRecords => Tr("Rental Records"),
            TransactionType.RentalItems => Tr("Rental Items"),
            TransactionType.Inventory => Tr("Inventory"),
            TransactionType.PurchaseOrders => Tr("Purchase Orders"),
            TransactionType.StockAdjustments => Tr("Stock Adjustments"),
            TransactionType.StockTransfers => Tr("Stock Transfers"),
            TransactionType.Returns => Tr("Returns"),
            TransactionType.LostDamaged => Tr("Lost & Damaged"),
            TransactionType.Receipts => Tr("Receipts"),
            TransactionType.Customers => Tr("Customers"),
            TransactionType.Suppliers => Tr("Suppliers"),
            TransactionType.Products => Tr("Products"),
            TransactionType.Categories => Tr("Categories"),
            TransactionType.Locations => Tr("Locations"),
            TransactionType.Accountants => Tr("Accountants"),
            _ => Tr("Data")
        };
        parts.Add(typeText);

        // Include returns/losses (only relevant for Revenue/Expenses)
        if (table.TransactionType is TransactionType.Revenue or TransactionType.Expenses)
        {
            var inclusions = new List<string>();
            if (table.IncludeReturns)
                inclusions.Add(Tr("Returns"));
            if (table.IncludeLosses)
                inclusions.Add(Tr("Losses"));

            if (inclusions.Count > 0)
                parts.Add("(" + Tr("incl.") + " " + string.Join(", ", inclusions) + ")");
        }

        return string.Join(" ", parts);
    }

    private void DrawAlignedText(SKCanvas canvas, string text, float colX, float colWidth, float y, float padding, SKTextAlign align, SKFont font, SKPaint paint)
    {
        float x = align switch
        {
            SKTextAlign.Left => colX + padding,
            SKTextAlign.Right => colX + colWidth - padding,
            _ => colX + colWidth / 2
        };
        canvas.DrawText(text, x, y, align, font, paint);
    }

    private string TruncateText(string text, float maxWidth, SKFont font)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var textWidth = font.MeasureText(text);
        if (textWidth <= maxWidth)
            return text;

        const string ellipsis = "...";
        var ellipsisWidth = font.MeasureText(ellipsis);

        if (maxWidth <= ellipsisWidth)
            return ellipsis;

        // Binary search for the right length
        var availableWidth = maxWidth - ellipsisWidth;
        var length = text.Length;

        while (length > 0)
        {
            var truncated = text[..length];
            var width = font.MeasureText(truncated);
            if (width <= availableWidth)
                return truncated + ellipsis;
            length--;
        }

        return ellipsis;
    }

    private Dictionary<string, string> CalculateTableTotals(List<List<string>> tableData, List<string> columns, int rowCount)
    {
        var totals = new Dictionary<string, string>();
        var numericColumns = new HashSet<string>
        {
            "Qty", "Unit Price", "Total", "Shipping", "Paid", "Balance",
            "Rate", "Daily Rate", "Weekly Rate", "Monthly Rate",
            "Unit Cost", "In Stock", "Reserved", "Available",
            "Total Qty", "Rented", "Previous", "New",
            "Capacity", "In Use", "Budget", "Salary", "Price", "Cost",
            "Transactions"
        };

        for (int colIndex = 0; colIndex < columns.Count; colIndex++)
        {
            var colName = columns[colIndex];
            if (!numericColumns.Contains(colName))
                continue;

            decimal sum = 0;
            var pending = false;
            for (int rowIndex = 0; rowIndex < rowCount && rowIndex < tableData.Count; rowIndex++)
            {
                var row = tableData[rowIndex];
                if (colIndex < row.Count)
                {
                    var text = row[colIndex];
                    // A partial total would understate the column, so it waits on the rate too.
                    if (text == PendingText)
                    {
                        pending = true;
                        continue;
                    }
                    // Strip the company's actual currency symbol (plus the common ones) and grouping
                    // separators, then parse invariantly.
                    var cleanText = text
                        .Replace(_currencySymbol, "")
                        .Replace("$", "").Replace("€", "").Replace("£", "")
                        .Replace(",", "")
                        .Trim();
                    if (decimal.TryParse(cleanText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
                        sum += value;
                }
            }

            // Format based on column type
            if (pending)
                totals[colName] = PendingText;
            else if (colName == "Qty")
                totals[colName] = sum.ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture);
            else
                totals[colName] = FormatCurrency(sum);
        }

        return totals;
    }

    private float[] CalculateColumnWidths(TableReportElement table, List<string> columns, List<List<string>> tableData, float totalWidth, SKFont headerFont, SKFont dataFont)
    {
        var columnCount = columns.Count;
        var columnWidths = new float[columnCount];

        if (!table.AutoSizeColumns || columnCount == 0)
        {
            // Equal width for all columns
            var equalWidth = totalWidth / Math.Max(columnCount, 1);
            for (int i = 0; i < columnCount; i++)
                columnWidths[i] = equalWidth;
            return columnWidths;
        }

        // Calculate max content width for each column
        var maxWidths = new float[columnCount];
        var cellPadding = table.CellPadding * _renderScale * 2; // Padding on both sides

        for (int i = 0; i < columnCount; i++)
        {
            var headerWidth = headerFont.MeasureText(columns[i]) + cellPadding;
            maxWidths[i] = headerWidth;

            // Measure data cells
            foreach (var row in tableData)
            {
                if (i < row.Count)
                {
                    var cellWidth = dataFont.MeasureText(row[i]) + cellPadding;
                    maxWidths[i] = Math.Max(maxWidths[i], cellWidth);
                }
            }
        }

        // Calculate total measured width
        var totalMeasured = maxWidths.Sum();

        if (totalMeasured <= 0)
        {
            // Fallback to equal widths
            var equalWidth = totalWidth / columnCount;
            for (int i = 0; i < columnCount; i++)
                columnWidths[i] = equalWidth;
            return columnWidths;
        }

        // Scale widths proportionally to fit available width
        var scale = totalWidth / totalMeasured;
        for (int i = 0; i < columnCount; i++)
        {
            columnWidths[i] = maxWidths[i] * scale;
        }

        return columnWidths;
    }

    private List<List<string>> GetTableData(TableReportElement table, List<string> columns)
    {
        var result = new List<List<string>>();

        if (_companyData == null)
            return result;

        var tableDataService = new ReportTableDataService(_companyData, _config.Filters);

        switch (table.TransactionType)
        {
            case TransactionType.Revenue:
                foreach (var r in tableDataService.GetRevenueTableData(table))
                    result.Add(MapTransactionRow(r, columns));
                break;
            case TransactionType.Expenses:
                foreach (var r in tableDataService.GetExpensesTableData(table))
                    result.Add(MapTransactionRow(r, columns));
                break;
            case TransactionType.Invoices:
                foreach (var r in tableDataService.GetInvoicesTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Date" => r.IssueDate.ToString("MM/dd/yyyy"),
                        "ID" => r.InvoiceNumber,
                        "Company" => r.CustomerName,
                        "Due Date" => r.DueDate.ToString("MM/dd/yyyy"),
                        // Amounts are USD-normalized; convert to the display currency at the invoice's date.
                        "Total" => FormatCurrency(ToDisplayCurrency(r.Total, r.IssueDate)),
                        "Paid" => FormatCurrency(ToDisplayCurrency(r.AmountPaid, r.IssueDate)),
                        "Balance" => FormatCurrency(ToDisplayCurrency(r.Balance, r.IssueDate)),
                        "Status" => r.Status,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.Payments:
                foreach (var r in tableDataService.GetPaymentsTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Date" => r.Date.ToString("MM/dd/yyyy"),
                        "Company" => r.CustomerName,
                        // USD-normalized; convert to the display currency at the payment's date.
                        "Total" => FormatCurrency(ToDisplayCurrency(r.Amount, r.Date)),
                        "Method" => r.PaymentMethod,
                        "ID" => r.Id,
                        "Invoice" => r.InvoiceId,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.RentalRecords:
                foreach (var r in tableDataService.GetRentalRecordsTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Item" => r.ItemName,
                        "Company" => r.CustomerName,
                        "Date" => r.StartDate.ToString("MM/dd/yyyy"),
                        "Due Date" => r.DueDate.ToString("MM/dd/yyyy"),
                        "Return Date" => r.ReturnDate?.ToString("MM/dd/yyyy") ?? "",
                        "Rate" => FormatCurrency(r.RateAmount),
                        "Total" => FormatCurrency(r.TotalCost),
                        "Status" => r.Status,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.RentalItems:
                foreach (var r in tableDataService.GetRentalItemsTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Name" => r.Name,
                        "Inventory Item" => r.InventoryItemId,
                        "Daily Rate" => FormatCurrency(r.DailyRate),
                        "Weekly Rate" => FormatCurrency(r.WeeklyRate),
                        "Monthly Rate" => FormatCurrency(r.MonthlyRate),
                        "Status" => r.Status,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.Inventory:
                foreach (var r in tableDataService.GetInventoryTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Product" => r.ProductName,
                        "SKU" => r.Sku,
                        "Location" => r.LocationName,
                        "In Stock" => StockUnits.Format(r.InStock),
                        "Reserved" => StockUnits.Format(r.Reserved),
                        "Available" => StockUnits.Format(r.Available),
                        "Unit Cost" => FormatCurrency(r.UnitCost),
                        "Total" => FormatCurrency(r.TotalValue),
                        "Status" => r.Status,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.PurchaseOrders:
                foreach (var r in tableDataService.GetPurchaseOrdersTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "ID" => r.PoNumber,
                        "Company" => r.SupplierName,
                        "Date" => r.OrderDate.ToString("MM/dd/yyyy"),
                        "Due Date" => r.ExpectedDeliveryDate.ToString("MM/dd/yyyy"),
                        // USD-normalized; convert to the display currency at the order's date.
                        "Total" => FormatCurrency(ToDisplayCurrency(r.Total, r.OrderDate)),
                        "Status" => r.Status,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.StockAdjustments:
                foreach (var r in tableDataService.GetStockAdjustmentsTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Date" => r.Timestamp.ToString("MM/dd/yyyy"),
                        "Product" => r.ProductName,
                        "Type" => r.AdjustmentType,
                        "Qty" => StockUnits.Format(r.Quantity),
                        "Previous" => StockUnits.Format(r.PreviousStock),
                        "New" => StockUnits.Format(r.NewStock),
                        "Reason" => r.Reason,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.StockTransfers:
                foreach (var r in tableDataService.GetStockTransfersTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Date" => r.TransferDate.ToString("MM/dd/yyyy"),
                        "Product" => r.ProductName,
                        "From" => r.SourceLocation,
                        "To" => r.DestinationLocation,
                        "Qty" => StockUnits.Format(r.Quantity),
                        "Status" => r.Status,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.Returns:
                foreach (var r in tableDataService.GetReturnsTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Date" => r.ReturnDate.ToString("MM/dd/yyyy"),
                        "ID" => r.OriginalTransactionId,
                        "Product" => r.ProductName,
                        "Category" => r.CategoryName,
                        "Qty" => StockUnits.Format(r.Quantity),
                        "Total" => FormatRecordedAmount(r.RefundAmount, r.Currency, r.ReturnDate),
                        "Reason" => r.Reason,
                        "Status" => r.Status,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.LostDamaged:
                foreach (var r in tableDataService.GetLossesTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Date" => r.ReportedDate.ToString("MM/dd/yyyy"),
                        "Product" => r.ProductName,
                        "Category" => r.CategoryName,
                        "Qty" => StockUnits.Format(r.Quantity),
                        "Total" => FormatRecordedAmount(r.EstimatedValue, r.Currency, r.ReportedDate),
                        "Reason" => r.Reason,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.Receipts:
                foreach (var r in tableDataService.GetReceiptsTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Date" => r.Date.ToString("MM/dd/yyyy"),
                        "File" => r.FileName,
                        "Company" => r.Supplier,
                        "Total" => FormatCurrency(r.Amount),
                        "Source" => r.Source,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.Customers:
                foreach (var r in tableDataService.GetCustomersTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Name" => r.Name,
                        "Company" => r.CompanyName,
                        "Country" => r.Country,
                        "Total" => FormatCurrency(r.TotalPurchases),
                        "Status" => r.Status,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.Suppliers:
                foreach (var r in tableDataService.GetSuppliersTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Name" => r.Name,
                        "Contact" => r.ContactPerson,
                        "Country" => r.Country,
                        "Terms" => r.PaymentTerms,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.Products:
                foreach (var r in tableDataService.GetProductsTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Name" => r.Name,
                        "SKU" => r.Sku,
                        "Category" => r.CategoryName,
                        "Price" => FormatCurrency(r.UnitPrice),
                        "Cost" => FormatCurrency(r.CostPrice),
                        "Status" => r.Status,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.Categories:
                foreach (var r in tableDataService.GetCategoriesTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Name" => r.Name,
                        "Type" => r.Type,
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.Locations:
                foreach (var r in tableDataService.GetLocationsTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Name" => r.Name,
                        "Contact" => r.ContactPerson,
                        "Capacity" => r.Capacity.ToString("N0"),
                        "In Use" => r.CurrentUtilization.ToString("N0"),
                        "Utilization" => r.UtilizationPercentage.ToString("F1") + "%",
                        _ => ""
                    }).ToList());
                break;
            case TransactionType.Accountants:
                foreach (var r in tableDataService.GetAccountantsTableData(table))
                    result.Add(columns.Select(col => col switch
                    {
                        "Name" => r.Name,
                        "Email" => r.Email,
                        "Phone" => r.Phone,
                        "Transactions" => r.AssignedTransactions.ToString("N0"),
                        _ => ""
                    }).ToList());
                break;
        }

        return result;
    }

    private List<string> MapTransactionRow(TransactionTableRow r, List<string> columns)
    {
        return columns.Select(col => col switch
        {
            "Date" => r.Date.ToString("MM/dd/yyyy"),
            "ID" => r.TransactionId,
            "Company" => r.CompanyName,
            "Product" => r.ProductName,
            // Invariant, like the currency cells, so the footer's re-parse reads "1.5" as one and a half.
            "Qty" => r.Quantity.ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture),
            "Unit Price" => FormatCurrency(r.UnitPrice),
            // r.Total is USD-normalized (see ReportTableDataService); convert it to the display
            // currency at the row's date instead of stamping the symbol on a raw dollar figure. The
            // footer re-parses these rendered cells, so its total follows automatically.
            "Total" => FormatCurrency(ToDisplayCurrency(r.Total, r.Date)),
            "Status" => r.Status,
            "Accountant" => r.AccountantName,
            "Shipping" => FormatCurrency(ToDisplayCurrency(r.ShippingCost, r.Date)),
            _ => ""
        }).ToList();
    }

    private void RenderLabel(SKCanvas canvas, LabelReportElement label)
    {
        var rect = GetScaledRect(label);

        var style = SKFontStyle.Normal;
        if (label.IsBold && label.IsItalic)
            style = SKFontStyle.BoldItalic;
        else if (label.IsBold)
            style = SKFontStyle.Bold;
        else if (label.IsItalic)
            style = SKFontStyle.Italic;

        var typeface = PlatformTypefaces.Resolve(label.FontFamily, style);
        using var font = new SKFont(typeface, (float)label.FontSize * _renderScale);
        using var paint = new SKPaint();
        paint.Color = ParseColor(label.TextColor);
        paint.IsAntialias = true;

        var textAlign = label.HorizontalAlignment switch
        {
            HorizontalTextAlignment.Left => SKTextAlign.Left,
            HorizontalTextAlignment.Center => SKTextAlign.Center,
            HorizontalTextAlignment.Right => SKTextAlign.Right,
            _ => SKTextAlign.Left
        };

        var x = label.HorizontalAlignment switch
        {
            HorizontalTextAlignment.Left => rect.Left,
            HorizontalTextAlignment.Center => rect.MidX,
            HorizontalTextAlignment.Right => rect.Right,
            _ => rect.Left
        };

        var y = label.VerticalAlignment switch
        {
            VerticalTextAlignment.Top => rect.Top + (float)label.FontSize * _renderScale,
            VerticalTextAlignment.Center => rect.MidY + (float)(label.FontSize * _renderScale) / 3,
            VerticalTextAlignment.Bottom => rect.Bottom,
            _ => rect.MidY
        };

        canvas.DrawText(label.Text, x, y, textAlign, font, paint);

        // Draw underline if specified
        if (label.IsUnderline)
        {
            font.GetFontMetrics(out var metrics);
            var underlinePaint = new SKPaint
            {
                Color = paint.Color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1 * _renderScale,
                IsAntialias = true
            };

            var textWidth = font.MeasureText(label.Text);
            var underlineY = y + (metrics.UnderlinePosition ?? 3 * _renderScale);
            var startX = label.HorizontalAlignment switch
            {
                HorizontalTextAlignment.Left => x,
                HorizontalTextAlignment.Center => x - textWidth / 2,
                HorizontalTextAlignment.Right => x - textWidth,
                _ => x
            };

            canvas.DrawLine(startX, underlineY, startX + textWidth, underlineY, underlinePaint);
        }
    }

    private void RenderImage(SKCanvas canvas, ImageReportElement image)
    {
        var rect = GetScaledRect(image);

        // Calculate corner radius from percentage (based on shorter side)
        var cornerRadius = 0f;
        if (image.CornerRadiusPercent > 0)
        {
            var shortSide = Math.Min(rect.Width, rect.Height);
            cornerRadius = shortSide * image.CornerRadiusPercent / 200f; // 100% = fully round
        }

        // Apply rounded clip if needed
        var hasRoundedCorners = cornerRadius > 0;
        if (hasRoundedCorners)
        {
            canvas.Save();
            var clipPath = new SKPath();
            clipPath.AddRoundRect(rect, cornerRadius, cornerRadius);
            canvas.ClipPath(clipPath, SKClipOperation.Intersect, true);
        }

        // Check if user has set a background color (not transparent)
        var hasUserBackground = !string.IsNullOrEmpty(image.BackgroundColor) && image.BackgroundColor != "#00FFFFFF";

        // Draw background color if set
        if (hasUserBackground)
        {
            var bgPaint = new SKPaint { Color = ParseColor(image.BackgroundColor), Style = SKPaintStyle.Fill };
            canvas.DrawRect(rect, bgPaint);
        }

        // Load and draw image if path specified
        if (!string.IsNullOrEmpty(image.ImagePath))
        {
            // Try to load the image
            try
            {
                var templateStorage = new ReportTemplateStorage();
                var resolvedPath = templateStorage.ResolveImagePath(image.ImagePath);

                if (File.Exists(resolvedPath))
                {
                    using var stream = SharedFileReader.OpenRead(resolvedPath);
                    using var bitmap = SKBitmap.Decode(stream);

                    if (bitmap != null)
                    {
                        var destRect = CalculateImageRect(bitmap, rect, image.ScaleMode);

                        // Apply opacity (convert percentage 0-100 to alpha 0-255)
                        if (image.Opacity < 100)
                        {
                            var alpha = (byte)(image.Opacity * 255 / 100);
                            var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
                            canvas.DrawBitmap(bitmap, destRect, paint);
                        }
                        else
                        {
                            canvas.DrawBitmap(bitmap, destRect);
                        }
                    }
                }
                else
                {
                    DrawPlaceholder(canvas, rect, Tr("Image not found"), !hasUserBackground);
                }
            }
            catch
            {
                DrawPlaceholder(canvas, rect, Tr("Error loading image"), !hasUserBackground);
            }
        }
        else
        {
            DrawPlaceholder(canvas, rect, Tr("No image selected"), !hasUserBackground);
        }

        // Restore canvas before drawing border (so border draws on top of clip)
        if (hasRoundedCorners)
        {
            canvas.Restore();
        }

        // Draw border
        if (image.BorderThickness > 0 && image.BorderColor != "#00FFFFFF")
        {
            var borderPaint = new SKPaint
            {
                Color = ParseColor(image.BorderColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = image.BorderThickness * _renderScale,
                IsAntialias = true
            };
            if (hasRoundedCorners)
            {
                var borderPath = new SKPath();
                borderPath.AddRoundRect(rect, cornerRadius, cornerRadius);
                canvas.DrawPath(borderPath, borderPaint);
            }
            else
            {
                canvas.DrawRect(rect, borderPaint);
            }
        }
    }

    private void RenderDateRange(SKCanvas canvas, DateRangeReportElement dateRange)
    {
        var rect = GetScaledRect(dateRange);

        // Handle bold and italic font styles
        var style = SKFontStyle.Normal;
        if (dateRange.IsBold && dateRange.IsItalic)
            style = SKFontStyle.BoldItalic;
        else if (dateRange.IsBold)
            style = SKFontStyle.Bold;
        else if (dateRange.IsItalic)
            style = SKFontStyle.Italic;

        var typeface = PlatformTypefaces.Resolve(dateRange.FontFamily, style);
        using var font = new SKFont(typeface, (float)dateRange.FontSize * _renderScale);
        using var paint = new SKPaint();
        paint.Color = ParseColor(dateRange.TextColor);
        paint.IsAntialias = true;

        var textAlign = dateRange.HorizontalAlignment switch
        {
            HorizontalTextAlignment.Left => SKTextAlign.Left,
            HorizontalTextAlignment.Center => SKTextAlign.Center,
            HorizontalTextAlignment.Right => SKTextAlign.Right,
            _ => SKTextAlign.Center
        };

        var text = dateRange.IsAsOfDate
            ? GetAsOfDateText(dateRange.DateFormat)
            : GetDateRangeText(dateRange.DateFormat);

        var x = dateRange.HorizontalAlignment switch
        {
            HorizontalTextAlignment.Left => rect.Left,
            HorizontalTextAlignment.Center => rect.MidX,
            HorizontalTextAlignment.Right => rect.Right,
            _ => rect.MidX
        };

        var y = dateRange.VerticalAlignment switch
        {
            VerticalTextAlignment.Top => rect.Top + (float)dateRange.FontSize * _renderScale,
            VerticalTextAlignment.Center => rect.MidY + (float)(dateRange.FontSize * _renderScale) / 3,
            VerticalTextAlignment.Bottom => rect.Bottom,
            _ => rect.MidY
        };

        canvas.DrawText(text, x, y, textAlign, font, paint);

        // Draw underline if specified
        if (dateRange.IsUnderline)
        {
            font.GetFontMetrics(out var metrics);
            var underlinePaint = new SKPaint
            {
                Color = paint.Color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1 * _renderScale,
                IsAntialias = true
            };

            var textWidth = font.MeasureText(text);
            var underlineY = y + (metrics.UnderlinePosition ?? 3 * _renderScale);
            var startX = dateRange.HorizontalAlignment switch
            {
                HorizontalTextAlignment.Left => x,
                HorizontalTextAlignment.Center => x - textWidth / 2,
                HorizontalTextAlignment.Right => x - textWidth,
                _ => x - textWidth / 2
            };

            canvas.DrawLine(startX, underlineY, startX + textWidth, underlineY, underlinePaint);
        }
    }

    private void RenderSummary(SKCanvas canvas, SummaryReportElement summary)
    {
        var rect = GetScaledRect(summary);

        // Draw background
        var bgPaint = new SKPaint { Color = ParseColor(summary.BackgroundColor), Style = SKPaintStyle.Fill };
        canvas.DrawRect(rect, bgPaint);

        // Draw border
        if (summary.BorderThickness > 0)
        {
            var borderPaint = new SKPaint
            {
                Color = ParseColor(summary.BorderColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = summary.BorderThickness * _renderScale,
                IsAntialias = true
            };
            canvas.DrawRect(rect, borderPaint);
        }

        // Draw summary statistics
        var summaryTypeface = PlatformTypefaces.Resolve(summary.FontFamily);
        using var font = new SKFont(summaryTypeface, (float)summary.FontSize * _renderScale);
        using var textPaint = new SKPaint();
        textPaint.Color = SKColors.Black;
        textPaint.IsAntialias = true;

        var textAlign = summary.HorizontalAlignment switch
        {
            HorizontalTextAlignment.Left => SKTextAlign.Left,
            HorizontalTextAlignment.Center => SKTextAlign.Center,
            HorizontalTextAlignment.Right => SKTextAlign.Right,
            _ => SKTextAlign.Left
        };

        var lines = new List<string>();

        if (summary.ShowTotalRevenue)
        {
            var total = CalculateTotalRevenue(summary);
            var label = summary.TransactionType == TransactionType.Expenses ? Tr("Total Expenses") : Tr("Total Revenue");
            lines.Add($"{label}: {FormatCurrency(total)}");
        }

        if (summary.ShowTotalTransactions)
        {
            var count = CalculateTransactionCount(summary);
            lines.Add($"{Tr("Transactions")}: {count:N0}");
        }

        if (summary.ShowAverageValue)
        {
            var avg = CalculateAverageValue(summary);
            lines.Add($"{Tr("Average Value")}: {FormatCurrency(avg)}");
        }

        if (summary.ShowGrowthRate)
        {
            var growth = CalculateGrowthRate(summary);
            var sign = growth >= 0 ? "+" : "";
            lines.Add($"{Tr("Growth Rate")}: {sign}{growth:N1}%");
        }

        var lineHeight = (float)summary.FontSize * _renderScale * 1.5f;
        var startY = summary.VerticalAlignment switch
        {
            VerticalTextAlignment.Top => rect.Top + 10 * _renderScale + (float)summary.FontSize * _renderScale,
            VerticalTextAlignment.Center => rect.MidY - ((lines.Count - 1) * lineHeight / 2),
            VerticalTextAlignment.Bottom => rect.Bottom - (lines.Count * lineHeight) - 10 * _renderScale,
            _ => rect.Top + 10 * _renderScale + (float)summary.FontSize * _renderScale
        };

        var x = summary.HorizontalAlignment switch
        {
            HorizontalTextAlignment.Left => rect.Left + 10 * _renderScale,
            HorizontalTextAlignment.Center => rect.MidX,
            HorizontalTextAlignment.Right => rect.Right - 10 * _renderScale,
            _ => rect.Left + 10 * _renderScale
        };

        for (int i = 0; i < lines.Count; i++)
        {
            canvas.DrawText(lines[i], x, startY + (i * lineHeight), textAlign, font, textPaint);
        }
    }

    private void RenderAccountingTable(SKCanvas canvas, AccountingTableReportElement element)
    {
        // Backward-compatible wrapper for designer mode (no continuation plan)
        var dataService = new AccountingReportDataService(_companyData, _config.Filters);
        var tableData = dataService.GetReportData(element.ReportType);

        RenderAccountingTableSlice(canvas, element, tableData,
            startRowIndex: 0, rowCount: tableData.Rows.Count, dataRowStartIndex: 0,
            showTitle: true, showSubtitle: true, showFootnote: true,
            showContinuedIndicator: false);
    }

    /// <summary>
    /// Renders a slice of an accounting table. Used for both single-page rendering
    /// and multi-page continuation rendering.
    /// </summary>
    private void RenderAccountingTableSlice(
        SKCanvas canvas, AccountingTableReportElement element, AccountingTableData tableData,
        int startRowIndex, int rowCount, int dataRowStartIndex,
        bool showTitle, bool showSubtitle, bool showFootnote, bool showContinuedIndicator,
        SKRect? overrideRect = null)
    {
        var rect = overrideRect ?? GetScaledRect(element);

        // Draw white background
        canvas.DrawRect(rect, new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill });

        // Create typefaces and fonts
        var typeface = PlatformTypefaces.Resolve(element.FontFamily);
        var boldTypeface = PlatformTypefaces.Resolve(element.FontFamily, SKFontStyle.Bold);
        var italicTypeface = PlatformTypefaces.Resolve(element.FontFamily, SKFontStyle.Italic);

        var titleFontSize = (float)element.TitleFontSize * _renderScale;
        var headerFontSize = (float)element.HeaderFontSize * _renderScale;
        var dataFontSize = (float)element.FontSize * _renderScale;
        var cellPadding = (float)element.CellPadding * _renderScale;
        var dataRowHeight = (float)element.DataRowHeight * _renderScale;
        var headerRowHeight = (float)element.HeaderRowHeight * _renderScale;
        var indentWidth = (float)element.IndentWidth * _renderScale;

        using var titleFont = new SKFont(boldTypeface, titleFontSize);
        using var headerFont = new SKFont(boldTypeface, headerFontSize);
        using var dataFont = new SKFont(typeface, dataFontSize);
        using var boldDataFont = new SKFont(boldTypeface, dataFontSize);
        using var sectionFont = new SKFont(boldTypeface, headerFontSize);
        using var italicFont = new SKFont(italicTypeface, dataFontSize * 0.9f);
        using var grandTotalFont = new SKFont(boldTypeface, dataFontSize * 1.1f);

        // Calculate column widths from ratios (prefer element's custom ratios if set and matching column count)
        var availableWidth = rect.Width - (cellPadding * 2);
        var effectiveRatios = (element.ColumnWidthRatios?.Count == tableData.ColumnWidthRatios.Count)
            ? element.ColumnWidthRatios : tableData.ColumnWidthRatios;
        var columnWidths = new float[effectiveRatios.Count];
        for (int i = 0; i < effectiveRatios.Count; i++)
            columnWidths[i] = availableWidth * (float)effectiveRatios[i];

        // If no ratios defined, use default 2-column split
        if (columnWidths.Length == 0)
        {
            columnWidths = [availableWidth * 0.65f, availableWidth * 0.35f];
        }

        var currentY = rect.Top;
        var contentLeft = rect.Left + cellPadding;

        // --- Draw Title (only on first page) ---
        if (showTitle)
        {
            var titleBarHeight = headerRowHeight * 1.4f;
            var titleRect = new SKRect(rect.Left, currentY, rect.Right, currentY + titleBarHeight);
            canvas.DrawRect(titleRect, new SKPaint { Color = ParseColor(element.HeaderBackgroundColor), Style = SKPaintStyle.Fill });

            using var titlePaint = new SKPaint { Color = ParseColor(element.HeaderTextColor), IsAntialias = true };
            canvas.DrawText(tableData.Title, rect.MidX, currentY + titleBarHeight * 0.65f, SKTextAlign.Center, titleFont, titlePaint);
            currentY += titleBarHeight;
        }

        // --- Draw "(Continued)" indicator (on continuation pages) ---
        if (showContinuedIndicator)
        {
            var continuedHeight = dataRowHeight * 0.8f;
            var continuedRect = new SKRect(rect.Left, currentY, rect.Right, currentY + continuedHeight);
            canvas.DrawRect(continuedRect, new SKPaint { Color = ParseColor(element.HeaderBackgroundColor), Style = SKPaintStyle.Fill });

            using var continuedPaint = new SKPaint { Color = ParseColor(element.HeaderTextColor), IsAntialias = true };
            var continuedText = $"{tableData.Title} ({Tr("Continued")})";
            canvas.DrawText(continuedText, rect.MidX, currentY + continuedHeight * 0.65f, SKTextAlign.Center, italicFont, continuedPaint);
            currentY += continuedHeight;
        }

        // --- Draw Subtitle (only on first page) ---
        if (showSubtitle && !string.IsNullOrEmpty(tableData.Subtitle))
        {
            var subtitleHeight = dataRowHeight * 1.2f;
            using var subtitlePaint = new SKPaint { Color = ParseColor(element.DataRowTextColor), IsAntialias = true };
            canvas.DrawText(tableData.Subtitle, rect.MidX, currentY + subtitleHeight * 0.7f, SKTextAlign.Center, italicFont, subtitlePaint);
            currentY += subtitleHeight;
        }

        // --- Draw Column Headers (always shown) ---
        if (tableData.ColumnHeaders.Count > 0)
        {
            var colHeaderRect = new SKRect(rect.Left, currentY, rect.Right, currentY + headerRowHeight);
            canvas.DrawRect(colHeaderRect, new SKPaint { Color = ParseColor(element.HeaderBackgroundColor), Style = SKPaintStyle.Fill });

            using var colHeaderPaint = new SKPaint { Color = ParseColor(element.HeaderTextColor), IsAntialias = true };
            var colX = contentLeft;
            for (int i = 0; i < tableData.ColumnHeaders.Count && i < columnWidths.Length; i++)
            {
                var textX = colX + columnWidths[i] / 2;
                var cellClip = new SKRect(colX, currentY, colX + columnWidths[i], currentY + headerRowHeight);
                DrawClippedText(canvas, tableData.ColumnHeaders[i], textX, currentY + headerRowHeight * 0.65f, SKTextAlign.Center, headerFont, colHeaderPaint, cellClip);
                colX += columnWidths[i];
            }
            // Draw vertical column separator lines in header
            using var separatorPaint = new SKPaint
            {
                Color = new SKColor(180, 180, 180, 128),
                StrokeWidth = 1 * _renderScale,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            var sepX = contentLeft;
            for (int i = 0; i < effectiveRatios.Count - 1; i++)
            {
                sepX += columnWidths[i];
                canvas.DrawLine(sepX, colHeaderRect.Top + 4 * _renderScale, sepX, colHeaderRect.Bottom - 4 * _renderScale, separatorPaint);
            }

            currentY += headerRowHeight;

            // Header bottom border
            canvas.DrawLine(rect.Left, currentY, rect.Right, currentY,
                new SKPaint { Color = ParseColor(element.GridLineColor), StrokeWidth = 1 * _renderScale, Style = SKPaintStyle.Stroke });
        }

        // --- Draw Rows (only the assigned slice) ---
        int dataRowIndex = dataRowStartIndex;
        var endRowIndex = Math.Min(startRowIndex + rowCount, tableData.Rows.Count);

        for (int rowIdx = startRowIndex; rowIdx < endRowIndex; rowIdx++)
        {
            var row = tableData.Rows[rowIdx];

            // The planner's own test (a row fits if it ends by the bottom), plus half a pixel for float
            // drift. Reserving a spare row here dropped rows the planner had placed on this page.
            if (currentY + GetAccountingRowHeight(row.RowType, dataRowHeight, headerRowHeight) > rect.Bottom + 0.5f)
                break; // Safety: stop if we'd overflow (shouldn't happen with correct planning)

            AccountingRowsDrawn++;

            switch (row.RowType)
            {
                case AccountingRowType.SectionHeader:
                {
                    // Extend fill 1px upward to cover any grid line from the previous row
                    var fillTop = currentY > rect.Top + 1 * _renderScale ? currentY - 1 * _renderScale : currentY;
                    var rowRect = new SKRect(rect.Left, fillTop, rect.Right, currentY + headerRowHeight);
                    canvas.DrawRect(rowRect, new SKPaint { Color = ParseColor(element.SectionHeaderBackgroundColor), Style = SKPaintStyle.Fill });

                    using var sectionPaint = new SKPaint { Color = ParseColor(element.SectionHeaderTextColor), IsAntialias = true };
                    canvas.DrawText(row.Label, contentLeft + cellPadding, currentY + headerRowHeight * 0.65f, SKTextAlign.Left, sectionFont, sectionPaint);

                    // Draw value columns (right-aligned), e.g., "Amount" on the first section header
                    var valX = contentLeft + columnWidths[0];
                    for (int i = 0; i < row.Values.Count && i + 1 < columnWidths.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(row.Values[i]))
                        {
                            var cellClip = new SKRect(valX, currentY, valX + columnWidths[i + 1], currentY + headerRowHeight);
                            DrawClippedText(canvas, row.Values[i], valX + columnWidths[i + 1] - cellPadding, currentY + headerRowHeight * 0.65f, SKTextAlign.Right, sectionFont, sectionPaint, cellClip);
                        }
                        valX += columnWidths[i + 1];
                    }

                    currentY += headerRowHeight;
                    break;
                }

                case AccountingRowType.DataRow:
                {
                    // Alternate row coloring
                    if (element.AlternateRowColors && dataRowIndex % 2 == 1)
                    {
                        var rowRect = new SKRect(rect.Left, currentY, rect.Right, currentY + dataRowHeight);
                        canvas.DrawRect(rowRect, new SKPaint { Color = ParseColor(element.AlternateRowColor), Style = SKPaintStyle.Fill });
                    }

                    using var dataPaint = new SKPaint { Color = ParseColor(element.DataRowTextColor), IsAntialias = true };
                    var indent = row.IndentLevel * indentWidth;
                    var labelClip = new SKRect(contentLeft, currentY, contentLeft + columnWidths[0], currentY + dataRowHeight);
                    DrawClippedText(canvas, row.Label, contentLeft + cellPadding + indent, currentY + dataRowHeight * 0.7f, SKTextAlign.Left, dataFont, dataPaint, labelClip);

                    // Draw value columns (right-aligned)
                    var valX = contentLeft + columnWidths[0];
                    for (int i = 0; i < row.Values.Count && i + 1 < columnWidths.Length; i++)
                    {
                        var cellClip = new SKRect(valX, currentY, valX + columnWidths[i + 1], currentY + dataRowHeight);
                        DrawClippedText(canvas, row.Values[i], valX + columnWidths[i + 1] - cellPadding, currentY + dataRowHeight * 0.7f, SKTextAlign.Right, dataFont, dataPaint, cellClip);
                        valX += columnWidths[i + 1];
                    }

                    // Grid line
                    if (element.ShowGridLines)
                    {
                        canvas.DrawLine(rect.Left, currentY + dataRowHeight, rect.Right, currentY + dataRowHeight,
                            new SKPaint { Color = ParseColor(element.GridLineColor), StrokeWidth = 0.5f * _renderScale, Style = SKPaintStyle.Stroke });
                    }

                    currentY += dataRowHeight;
                    dataRowIndex++;
                    break;
                }

                case AccountingRowType.SubtotalRow:
                {
                    // Fill first (extending up to cover any grid line from the previous row)
                    var fillTop = currentY > rect.Top + 1 * _renderScale ? currentY - 1 * _renderScale : currentY;
                    var rowRect = new SKRect(rect.Left, fillTop, rect.Right, currentY + dataRowHeight);
                    canvas.DrawRect(rowRect, new SKPaint { Color = ParseColor(element.SubtotalBackgroundColor), Style = SKPaintStyle.Fill });

                    // Top border line, drawn after fill
                    canvas.DrawLine(rect.Left + cellPadding, currentY, rect.Right - cellPadding, currentY,
                        new SKPaint { Color = ParseColor(element.DataRowTextColor), StrokeWidth = 1 * _renderScale, Style = SKPaintStyle.Stroke });

                    using var subtotalPaint = new SKPaint { Color = ParseColor(element.DataRowTextColor), IsAntialias = true };
                    var indent = row.IndentLevel * indentWidth;
                    canvas.DrawText(row.Label, contentLeft + cellPadding + indent, currentY + dataRowHeight * 0.7f, SKTextAlign.Left, boldDataFont, subtotalPaint);

                    var valX = contentLeft + columnWidths[0];
                    for (int i = 0; i < row.Values.Count && i + 1 < columnWidths.Length; i++)
                    {
                        var cellClip = new SKRect(valX, currentY, valX + columnWidths[i + 1], currentY + dataRowHeight);
                        DrawClippedText(canvas, row.Values[i], valX + columnWidths[i + 1] - cellPadding, currentY + dataRowHeight * 0.7f, SKTextAlign.Right, boldDataFont, subtotalPaint, cellClip);
                        valX += columnWidths[i + 1];
                    }

                    currentY += dataRowHeight;
                    break;
                }

                case AccountingRowType.TotalRow:
                {
                    // Fill first (extending up to cover any grid line from the previous row), then draw border on top
                    var fillTop = currentY > rect.Top + 1 * _renderScale ? currentY - 1 * _renderScale : currentY;
                    var rowRect = new SKRect(rect.Left, fillTop, rect.Right, currentY + dataRowHeight * 1.1f);
                    canvas.DrawRect(rowRect, new SKPaint { Color = ParseColor(element.TotalBackgroundColor), Style = SKPaintStyle.Fill });

                    // Top border (thicker), drawn after fill so it's visible on the background
                    canvas.DrawLine(rect.Left, currentY, rect.Right, currentY,
                        new SKPaint { Color = ParseColor(element.DataRowTextColor), StrokeWidth = 1.5f * _renderScale, Style = SKPaintStyle.Stroke });

                    using var totalPaint = new SKPaint { Color = ParseColor(element.TotalTextColor), IsAntialias = true };
                    canvas.DrawText(row.Label, contentLeft + cellPadding, currentY + dataRowHeight * 0.75f, SKTextAlign.Left, boldDataFont, totalPaint);

                    var valX = contentLeft + columnWidths[0];
                    for (int i = 0; i < row.Values.Count && i + 1 < columnWidths.Length; i++)
                    {
                        var cellClip = new SKRect(valX, currentY, valX + columnWidths[i + 1], currentY + dataRowHeight * 1.1f);
                        DrawClippedText(canvas, row.Values[i], valX + columnWidths[i + 1] - cellPadding, currentY + dataRowHeight * 0.75f, SKTextAlign.Right, boldDataFont, totalPaint, cellClip);
                        valX += columnWidths[i + 1];
                    }

                    currentY += dataRowHeight * 1.1f;
                    break;
                }

                case AccountingRowType.GrandTotalRow:
                {
                    var grandRowHeight = dataRowHeight * 1.3f;

                    // Fill first (extending up to cover any grid/separator lines from previous rows)
                    var fillTop = currentY > rect.Top + 1 * _renderScale ? currentY - 1 * _renderScale : currentY;
                    var rowRect = new SKRect(rect.Left, fillTop, rect.Right, currentY + grandRowHeight);
                    canvas.DrawRect(rowRect, new SKPaint { Color = ParseColor(element.TotalBackgroundColor), Style = SKPaintStyle.Fill });

                    // Double top border, drawn after fill so it's visible on the background
                    canvas.DrawLine(rect.Left, currentY, rect.Right, currentY,
                        new SKPaint { Color = ParseColor(element.DataRowTextColor), StrokeWidth = 1 * _renderScale, Style = SKPaintStyle.Stroke });
                    canvas.DrawLine(rect.Left, currentY + 3 * _renderScale, rect.Right, currentY + 3 * _renderScale,
                        new SKPaint { Color = ParseColor(element.DataRowTextColor), StrokeWidth = 1 * _renderScale, Style = SKPaintStyle.Stroke });

                    using var grandPaint = new SKPaint { Color = ParseColor(element.TotalTextColor), IsAntialias = true };
                    canvas.DrawText(row.Label, contentLeft + cellPadding, currentY + grandRowHeight * 0.65f, SKTextAlign.Left, grandTotalFont, grandPaint);

                    var valX = contentLeft + columnWidths[0];
                    for (int i = 0; i < row.Values.Count && i + 1 < columnWidths.Length; i++)
                    {
                        var cellClip = new SKRect(valX, currentY, valX + columnWidths[i + 1], currentY + grandRowHeight);
                        DrawClippedText(canvas, row.Values[i], valX + columnWidths[i + 1] - cellPadding, currentY + grandRowHeight * 0.65f, SKTextAlign.Right, grandTotalFont, grandPaint, cellClip);
                        valX += columnWidths[i + 1];
                    }

                    currentY += grandRowHeight;
                    break;
                }

                case AccountingRowType.SeparatorLine:
                {
                    currentY += dataRowHeight * 0.25f;
                    canvas.DrawLine(rect.Left + cellPadding, currentY, rect.Right - cellPadding, currentY,
                        new SKPaint { Color = ParseColor(element.GridLineColor), StrokeWidth = 1 * _renderScale, Style = SKPaintStyle.Stroke });
                    currentY += dataRowHeight * 0.25f;
                    break;
                }

                case AccountingRowType.BlankRow:
                {
                    currentY += dataRowHeight * 0.5f;
                    break;
                }
            }
        }

        // --- Draw Footnote (only on last page) ---
        if (showFootnote && !string.IsNullOrEmpty(tableData.Footnote) && currentY + dataRowHeight <= rect.Bottom)
        {
            currentY += dataRowHeight * 0.3f;
            using var footnotePaint = new SKPaint { Color = ParseColor(element.DataRowTextColor), IsAntialias = true };
            footnotePaint.Color = footnotePaint.Color.WithAlpha(150);
            canvas.DrawText(tableData.Footnote, rect.MidX, currentY + dataFontSize, SKTextAlign.Center, italicFont, footnotePaint);
        }

        // --- Draw Outer Border ---
        canvas.DrawRect(rect, new SKPaint { Color = ParseColor(element.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale });
    }

    #endregion

    #region Header and Footer

    private void RenderHeader(SKCanvas canvas, int width)
    {
        var headerHeight = PageDimensions.GetHeaderHeight(_config.ShowCompanyDetails) * _renderScale;
        var marginLeft = (float)_config.PageMargins.Left * _renderScale;
        var marginRight = (float)_config.PageMargins.Right * _renderScale;
        var marginTop = (float)_config.PageMargins.Top * _renderScale;

        // Draw header background (covers margin area and header)
        var headerRect = new SKRect(0, 0, width, marginTop + headerHeight);
        canvas.DrawRect(headerRect, new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill });

        if (_config.ShowCompanyDetails && _companyData != null)
        {
            RenderCompanyDetailsHeader(canvas, width, marginLeft, marginTop);
        }
        else
        {
            // Draw title centered in header area (offset by top margin)
            canvas.DrawText(_config.Title, width / 2f, marginTop + headerHeight / 2 + 6 * _renderScale, SKTextAlign.Center, _headerFont, _headerPaint);
        }

        // Draw separator line
        var separatorPaint = new SKPaint
        {
            Color = SKColors.LightGray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * _renderScale
        };
        canvas.DrawLine(marginLeft, marginTop + headerHeight - 5 * _renderScale, width - marginRight, marginTop + headerHeight - 5 * _renderScale, separatorPaint);
    }

    private void RenderCompanyDetailsHeader(SKCanvas canvas, int width, float margin, float marginTop)
    {
        var companyInfo = _companyData!.Settings.Company;
        var logoPath = _config.CompanyLogoPath;

        // Font sizes
        var companyNameFontSize = 14f * _renderScale;
        var detailFontSize = 8.5f * _renderScale;
        var titleFontSize = (float)_config.TitleFontSize * _renderScale;

        using var companyNameFont = new SKFont(_boldTypeface, companyNameFontSize);
        using var detailFont = new SKFont(_defaultTypeface, detailFontSize);
        using var titleFont = new SKFont(_boldTypeface, titleFontSize);

        using var blackPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var grayPaint = new SKPaint { Color = SKColor.Parse(AppColors.GrayDark), IsAntialias = true };

        var logoSize = 40f * _renderScale;
        var logoPadding = 10f * _renderScale;
        var hasLogo = !string.IsNullOrEmpty(logoPath) && File.Exists(logoPath);

        // Calculate text start X (after logo if present)
        var textStartX = margin;
        if (hasLogo)
            textStartX = margin + logoSize + logoPadding;

        // Top padding includes the page top margin to keep company info within the header area
        var topPadding = marginTop + 10f * _renderScale;

        // --- Draw Logo ---
        if (hasLogo)
        {
            try
            {
                using var stream = SharedFileReader.OpenRead(logoPath!);
                using var bitmap = SKBitmap.Decode(stream);
                if (bitmap != null)
                {
                    var logoRect = new SKRect(margin, topPadding + 8 * _renderScale, margin + logoSize, topPadding + 8 * _renderScale + logoSize);
                    canvas.DrawBitmap(bitmap, logoRect);
                }
            }
            catch
            {
                // Silently skip logo if it can't be loaded
            }
        }

        // --- Draw Company Name ---
        var currentY = topPadding + 18f * _renderScale;
        if (!string.IsNullOrEmpty(companyInfo.Name))
        {
            canvas.DrawText(companyInfo.Name, textStartX, currentY, SKTextAlign.Left, companyNameFont, blackPaint);
            currentY += companyNameFontSize + 2 * _renderScale;
        }

        // --- Draw Address Line ---
        var addressParts = new List<string>();
        if (!string.IsNullOrEmpty(companyInfo.Address))
            addressParts.Add(companyInfo.Address);
        if (!string.IsNullOrEmpty(companyInfo.City))
            addressParts.Add(companyInfo.City);
        if (!string.IsNullOrEmpty(companyInfo.ProvinceState))
            addressParts.Add(companyInfo.ProvinceState);
        if (!string.IsNullOrEmpty(companyInfo.Country))
            addressParts.Add(companyInfo.Country);

        if (addressParts.Count > 0)
        {
            canvas.DrawText(string.Join(", ", addressParts), textStartX, currentY, SKTextAlign.Left, detailFont, grayPaint);
            currentY += detailFontSize + 1 * _renderScale;
        }

        // --- Draw Contact Line (Phone | Email) ---
        var contactParts = new List<string>();
        if (!string.IsNullOrEmpty(companyInfo.Phone))
            contactParts.Add($"{Tr("Phone")}: {companyInfo.Phone}");
        if (!string.IsNullOrEmpty(companyInfo.Email))
            contactParts.Add($"{Tr("Email")}: {companyInfo.Email}");

        if (contactParts.Count > 0)
        {
            canvas.DrawText(string.Join("  |  ", contactParts), textStartX, currentY, SKTextAlign.Left, detailFont, grayPaint);
            currentY += detailFontSize + 2 * _renderScale;
        }

        // --- Draw Report Title (centered, immediately below company info) ---
        // Ensure title clears the logo area
        if (hasLogo)
        {
            var logoBottom = topPadding + (8 + 40) * _renderScale + 2 * _renderScale;
            currentY = Math.Max(currentY, logoBottom);
        }
        currentY += 4 * _renderScale;
        canvas.DrawText(_config.Title, width / 2f, currentY, SKTextAlign.Center, titleFont, blackPaint);
    }

    private void RenderFooter(SKCanvas canvas, int width, int height)
    {
        var footerHeight = PageDimensions.FooterHeight * _renderScale;
        var footerY = height - footerHeight;
        var marginLeft = (float)_config.PageMargins.Left * _renderScale;
        var marginRight = (float)_config.PageMargins.Right * _renderScale;

        // Draw separator line
        var separatorPaint = new SKPaint
        {
            Color = SKColors.LightGray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * _renderScale
        };
        canvas.DrawLine(marginLeft, footerY + 5 * _renderScale, width - marginRight, footerY + 5 * _renderScale, separatorPaint);

        // Draw timestamp
        using var footerFont = new SKFont(_defaultTypeface, 10 * _renderScale);
        using var footerPaint = new SKPaint();
        footerPaint.Color = SKColors.Gray;
        footerPaint.IsAntialias = true;

        var timeFormat = _config.Use24HourFormat ? "HH:mm" : "h:mm tt";
        var timestamp = DateTime.Now.ToString($"MMM dd, yyyy {timeFormat}");
        canvas.DrawText($"{Tr("Generated")}: {timestamp}", marginLeft, footerY + footerHeight / 2 + 4 * _renderScale, SKTextAlign.Left, footerFont, footerPaint);

        // Draw page number if enabled
        if (_config.ShowPageNumbers)
        {
            var pageText = _config.TotalPageCount > 1
                ? $"{Tr("Page")} {_config.CurrentPageNumber} {Tr("of")} {_config.TotalPageCount}"
                : $"{Tr("Page")} {_config.CurrentPageNumber}";
            canvas.DrawText(pageText, width - marginRight, footerY + footerHeight / 2 + 4 * _renderScale, SKTextAlign.Right, footerFont, footerPaint);
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Helper method to translate strings using the translation provider.
    /// </summary>
    private string Tr(string text) => _translationProvider.Translate(text);

    private SKRect GetScaledRect(ReportElementBase element)
    {
        return new SKRect(
            (float)element.X * _renderScale,
            (float)element.Y * _renderScale,
            (float)(element.X + element.Width) * _renderScale,
            (float)(element.Y + element.Height) * _renderScale
        );
    }

    /// <summary>
    /// Formats a chart data point's label for X-axis display.
    /// Uses the configured date format if a Date is available, otherwise falls back to Label.
    /// </summary>
    private string FormatXAxisLabel(ChartDataPoint point)
    {
        if (point.Date.HasValue)
        {
            var dateFormat = _config.Filters.DateFormat;
            return point.Date.Value.ToString(dateFormat);
        }
        return point.Label;
    }

    /// <summary>
    /// Calculates which indices should have X-axis labels displayed.
    /// Calculates how many labels fit based on available width and distributes them evenly,
    /// matching how LiveCharts handles axis labels.
    /// </summary>
    private static int[] GetXAxisLabelIndices(List<ChartDataPoint> dataPoints, float chartWidth, float measuredLabelWidth, float renderScale)
    {
        var totalCount = dataPoints.Count;
        if (totalCount == 0) return [];
        if (totalCount == 1) return [0];
        if (totalCount == 2) return [0, 1];

        // Use measured label width plus a minimum gap between labels
        var labelGap = 20 * renderScale;
        var labelSlotWidth = measuredLabelWidth + labelGap;

        // Calculate how many labels can fit with spacing
        var maxLabels = Math.Max(2, (int)(chartWidth / labelSlotWidth));

        // Don't show more labels than data points
        if (maxLabels >= totalCount)
        {
            return Enumerable.Range(0, totalCount).ToArray();
        }

        // Distribute maxLabels evenly across the data points
        var indices = new List<int>();
        var step = (double)(totalCount - 1) / (maxLabels - 1);

        for (int i = 0; i < maxLabels; i++)
        {
            var index = (int)Math.Round(i * step);
            if (!indices.Contains(index))
            {
                indices.Add(index);
            }
        }

        return indices.OrderBy(x => x).ToArray();
    }

    /// <summary>
    /// Draws X-axis labels with special handling for the last label to prevent right-edge overflow.
    /// If the last label would overflow the chart's right edge, it is shifted left to fit.
    /// If shifting would cause it to overlap with the previous label, it is skipped entirely.
    /// </summary>
    private void DrawXAxisLabelsWithOverflowProtection(
        SKCanvas canvas, float labelY, int[] labelIndices,
        Func<int, (string label, float x)> getLabelInfo,
        SKFont font, SKPaint paint, SKRect chartArea)
    {
        float prevLabelRightEdge = float.MinValue;

        for (int idx = 0; idx < labelIndices.Length; idx++)
        {
            var (label, labelX) = getLabelInfo(labelIndices[idx]);
            var labelWidth = font.MeasureText(label, paint);
            var halfWidth = labelWidth / 2;

            // For the last label, prevent right-edge overflow
            if (idx == labelIndices.Length - 1 && labelX + halfWidth > chartArea.Right)
            {
                var adjustedX = chartArea.Right - halfWidth;
                var minGap = 4 * _renderScale;

                // If shifting left would overlap the previous label, skip it entirely
                if (adjustedX - halfWidth < prevLabelRightEdge + minGap)
                    continue;

                labelX = adjustedX;
            }

            canvas.DrawText(label, labelX, labelY, SKTextAlign.Center, font, paint);
            prevLabelRightEdge = labelX + halfWidth;
        }
    }

    /// <summary>
    /// Draws text clipped to the given rectangle so it cannot overflow into adjacent cells.
    /// </summary>
    private static void DrawClippedText(SKCanvas canvas, string text, float x, float y,
        SKTextAlign align, SKFont font, SKPaint paint, SKRect clipRect)
    {
        if (string.IsNullOrEmpty(text)) return;
        canvas.Save();
        canvas.ClipRect(clipRect);
        canvas.DrawText(text, x, y, align, font, paint);
        canvas.Restore();
    }

    private static SKColor ParseColor(string colorString)
    {
        if (string.IsNullOrEmpty(colorString))
            return SKColors.Black;

        try
        {
            if (colorString.StartsWith("#"))
            {
                return SKColor.Parse(colorString);
            }
        }
        catch
        {
            // Fall through to default
        }

        return SKColors.Black;
    }

    private string GetChartTitle(ChartDataType chartType) => Tr(chartType.GetDisplayName());

    private static List<string> GetVisibleColumns(TableReportElement table)
    {
        // For Revenue/Expenses, use the existing column visibility flags
        if (table.TransactionType is TransactionType.Revenue or TransactionType.Expenses)
        {
            var columns = new List<string>();
            if (table.ShowDateColumn) columns.Add("Date");
            if (table.ShowTransactionIdColumn) columns.Add("ID");
            if (table.ShowCompanyColumn) columns.Add("Company");
            if (table.ShowProductColumn) columns.Add("Product");
            if (table.ShowQuantityColumn) columns.Add("Qty");
            if (table.ShowTotalColumn) columns.Add("Total");
            if (table.ShowStatusColumn) columns.Add("Status");
            if (table.ShowAccountantColumn) columns.Add("Accountant");
            if (table.ShowShippingColumn) columns.Add("Shipping");
            return columns.Count > 0 ? columns : ["Date", "Description", "Amount"];
        }

        // For other types, return their natural column set
        return table.TransactionType switch
        {
            TransactionType.Invoices => ["Date", "ID", "Company", "Due Date", "Total", "Paid", "Balance", "Status"],
            TransactionType.Payments => ["Date", "Company", "Total", "Method", "ID"],
            TransactionType.RentalRecords => ["Item", "Company", "Date", "Due Date", "Rate", "Total", "Status"],
            TransactionType.RentalItems => ["Name", "Inventory Item", "Daily Rate", "Status"],
            TransactionType.Inventory => ["Product", "SKU", "Location", "In Stock", "Available", "Unit Cost", "Total", "Status"],
            TransactionType.PurchaseOrders => ["ID", "Company", "Date", "Due Date", "Total", "Status"],
            TransactionType.StockAdjustments => ["Date", "Product", "Type", "Qty", "Previous", "New", "Reason"],
            TransactionType.StockTransfers => ["Date", "Product", "From", "To", "Qty", "Status"],
            TransactionType.Returns => ["Date", "Product", "Qty", "Total", "Reason", "Status"],
            TransactionType.LostDamaged => ["Date", "Product", "Qty", "Total", "Reason"],
            TransactionType.Receipts => ["Date", "File", "Company", "Total", "Source"],
            TransactionType.Customers => ["Name", "Company", "Country", "Total", "Status"],
            TransactionType.Suppliers => ["Name", "Contact", "Country", "Terms"],
            TransactionType.Products => ["Name", "SKU", "Category", "Price", "Cost", "Status"],
            TransactionType.Categories => ["Name", "Type"],
            TransactionType.Locations => ["Name", "Contact", "Capacity", "In Use", "Utilization"],
            TransactionType.Accountants => ["Name", "Email", "Phone", "Transactions"],
            _ => ["Date", "Description", "Amount"]
        };
    }

    private string GetDateRangeText(string dateFormat)
    {
        var start = _config.Filters.StartDate;
        var end = _config.Filters.EndDate;

        if (start.HasValue && end.HasValue)
        {
            // Replace "All Time" sentinel (year 2000) with actual earliest data date
            var effectiveStart = start.Value;
            if (effectiveStart.Year <= 2000 && _companyData != null)
            {
                effectiveStart = _companyData.GetEarliestDate();
            }

            return $"{Tr("Period")}: {effectiveStart.ToString(dateFormat)} {Tr("to")} {end.Value.ToString(dateFormat)}";
        }

        return $"{Tr("Period")}: {Tr("All Time")}";
    }

    private string GetAsOfDateText(string dateFormat)
    {
        var end = _config.Filters.EndDate;
        if (end.HasValue)
            return $"{Tr("As of")} {end.Value.ToString(dateFormat)}";
        return $"{Tr("As of")} {DateTime.Today.ToString(dateFormat)}";
    }

    private void DrawPlaceholder(SKCanvas canvas, SKRect rect, string message, bool drawBackground = true)
    {
        if (drawBackground)
        {
            var bgPaint = new SKPaint { Color = new SKColor(240, 240, 240), Style = SKPaintStyle.Fill };
            canvas.DrawRect(rect, bgPaint);
        }

        using var font = new SKFont(_defaultTypeface, 10 * _renderScale);
        using var textPaint = new SKPaint();
        textPaint.Color = SKColors.Gray;
        textPaint.IsAntialias = true;
        canvas.DrawText(message, rect.MidX, rect.MidY, SKTextAlign.Center, font, textPaint);

        var borderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawRect(rect, borderPaint);
    }

    private static SKRect CalculateImageRect(SKBitmap bitmap, SKRect bounds, ImageScaleMode scaleMode)
    {
        if (scaleMode == ImageScaleMode.Stretch)
            return bounds;

        var imageAspect = (float)bitmap.Width / bitmap.Height;
        var boundsAspect = bounds.Width / bounds.Height;

        float width, height;

        switch (scaleMode)
        {
            case ImageScaleMode.Fit:
                if (imageAspect > boundsAspect)
                {
                    width = bounds.Width;
                    height = bounds.Width / imageAspect;
                }
                else
                {
                    width = bounds.Height * imageAspect;
                    height = bounds.Height;
                }
                break;

            case ImageScaleMode.Fill:
                if (imageAspect > boundsAspect)
                {
                    width = bounds.Height * imageAspect;
                    height = bounds.Height;
                }
                else
                {
                    width = bounds.Width;
                    height = bounds.Width / imageAspect;
                }
                break;

            case ImageScaleMode.Center:
                width = Math.Min(bitmap.Width, bounds.Width);
                height = Math.Min(bitmap.Height, bounds.Height);
                break;

            default:
                return bounds;
        }

        var x = bounds.Left + (bounds.Width - width) / 2;
        var y = bounds.Top + (bounds.Height - height) / 2;

        return new SKRect(x, y, x + width, y + height);
    }

    #endregion

    #region Statistics Calculation

    private decimal CalculateTotalRevenue(SummaryReportElement summary)
    {
        if (_companyData == null) return 0;

        var (startDate, endDate) = GetFilterDateRange();

        return summary.TransactionType switch
        {
            TransactionType.Revenue => _companyData.Revenues
                .Where(s => s.Date >= startDate && s.Date <= endDate)
                .Sum(s => ToDisplayCurrency(s.EffectiveTotalUSD, s.Date)),
            TransactionType.Expenses => _companyData.Expenses
                .Where(p => p.Date >= startDate && p.Date <= endDate)
                .Sum(p => ToDisplayCurrency(p.EffectiveTotalUSD, p.Date)),
            _ => _companyData.Revenues
                .Where(s => s.Date >= startDate && s.Date <= endDate)
                .Sum(s => ToDisplayCurrency(s.EffectiveTotalUSD, s.Date)) -
                 _companyData.Expenses
                .Where(p => p.Date >= startDate && p.Date <= endDate)
                .Sum(p => ToDisplayCurrency(p.EffectiveTotalUSD, p.Date))
        };
    }

    private int CalculateTransactionCount(SummaryReportElement summary)
    {
        if (_companyData == null) return 0;

        var (startDate, endDate) = GetFilterDateRange();

        return summary.TransactionType switch
        {
            TransactionType.Revenue => _companyData.Revenues
                .Count(s => s.Date >= startDate && s.Date <= endDate),
            TransactionType.Expenses => _companyData.Expenses
                .Count(p => p.Date >= startDate && p.Date <= endDate),
            _ => _companyData.Revenues.Count(s => s.Date >= startDate && s.Date <= endDate) +
                 _companyData.Expenses.Count(p => p.Date >= startDate && p.Date <= endDate)
        };
    }

    private decimal CalculateAverageValue(SummaryReportElement summary)
    {
        if (_companyData == null) return 0;

        var (startDate, endDate) = GetFilterDateRange();

        var totals = new List<decimal>();

        if (summary.TransactionType is TransactionType.Revenue)
        {
            var sales = _companyData.Revenues
                .Where(s => s.Date >= startDate && s.Date <= endDate)
                .Select(s => ToDisplayCurrency(s.EffectiveTotalUSD, s.Date));
            totals.AddRange(sales);
        }

        if (summary.TransactionType is TransactionType.Expenses)
        {
            var purchases = _companyData.Expenses
                .Where(p => p.Date >= startDate && p.Date <= endDate)
                .Select(p => ToDisplayCurrency(p.EffectiveTotalUSD, p.Date));
            totals.AddRange(purchases);
        }

        return totals.Count > 0 ? totals.Average() : 0;
    }

    private double CalculateGrowthRate(SummaryReportElement summary)
    {
        if (_companyData == null) return 0;

        var (startDate, endDate) = GetFilterDateRange();

        if (!startDate.HasValue || !endDate.HasValue)
            return 0;

        // +1 so the period is inclusive of both endpoints (Jan 1-31 is 31 days, not 30). Without it
        // the previous comparison window was one day shorter than the current one, biasing growth
        // positive (a flat business showed a few percent "growth").
        var periodLength = (endDate.Value - startDate.Value).Days + 1;
        if (periodLength <= 0) return 0;

        var previousStart = startDate.Value.AddDays(-periodLength);
        var previousEnd = startDate.Value.AddTicks(-1);

        decimal currentPeriod, previousPeriod;

        // Growth is a ratio, so aggregate in USD (EffectiveTotalUSD): the percentage is
        // currency-agnostic and summing native Total would mix currencies (Calculations.md §3).
        if (summary.TransactionType == TransactionType.Revenue)
        {
            currentPeriod = _companyData.Revenues
                .Where(s => s.Date >= startDate && s.Date <= endDate)
                .Sum(s => s.EffectiveTotalUSD);
            previousPeriod = _companyData.Revenues
                .Where(s => s.Date >= previousStart && s.Date <= previousEnd)
                .Sum(s => s.EffectiveTotalUSD);
        }
        else if (summary.TransactionType == TransactionType.Expenses)
        {
            currentPeriod = _companyData.Expenses
                .Where(p => p.Date >= startDate && p.Date <= endDate)
                .Sum(p => p.EffectiveTotalUSD);
            previousPeriod = _companyData.Expenses
                .Where(p => p.Date >= previousStart && p.Date <= previousEnd)
                .Sum(p => p.EffectiveTotalUSD);
        }
        else
        {
            currentPeriod = _companyData.Revenues
                    .Where(s => s.Date >= startDate && s.Date <= endDate)
                    .Sum(s => s.EffectiveTotalUSD) -
                _companyData.Expenses
                    .Where(p => p.Date >= startDate && p.Date <= endDate)
                    .Sum(p => p.EffectiveTotalUSD);
            previousPeriod = _companyData.Revenues
                     .Where(s => s.Date >= previousStart && s.Date <= previousEnd)
                     .Sum(s => s.EffectiveTotalUSD) -
                 _companyData.Expenses
                     .Where(p => p.Date >= previousStart && p.Date <= previousEnd)
                     .Sum(p => p.EffectiveTotalUSD);
        }

        if (previousPeriod == 0)
            return currentPeriod > 0 ? 100 : 0;

        return (double)((currentPeriod - previousPeriod) / Math.Abs(previousPeriod) * 100);
    }

    private (DateTime? Start, DateTime? End) GetFilterDateRange()
    {
        if (!string.IsNullOrEmpty(_config.Filters.DatePresetName) &&
            _config.Filters.DatePresetName != DatePresetNames.Custom)
        {
            var (start, end) = DatePresetNames.GetDateRange(_config.Filters.DatePresetName, _companyData?.GetEarliestDate());
            return (start, end);
        }

        // A custom end date is midnight; run it to the end of that day as tables and charts do
        // (ReportFilters.GetDateRange), or the last day's transactions are left out.
        return (_config.Filters.StartDate?.Date, _config.Filters.EndDate?.Date.AddDays(1).AddTicks(-1));
    }

    /// <summary>
    /// Formats a currency amount using the company's currency setting.
    /// </summary>
    private string FormatCurrency(decimal amount)
    {
        var currencyCode = _companyData?.Settings.Localization.Currency ?? "USD";
        return CurrencyInfo.FormatAmount(amount, currencyCode);
    }

    #endregion

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _textPaint.Dispose();
        _headerPaint.Dispose();
        _borderPaint.Dispose();
        _defaultTypeface.Dispose();
        _boldTypeface.Dispose();
        _defaultFont.Dispose();
        _headerFont.Dispose();
        GC.SuppressFinalize(this);
    }
}
