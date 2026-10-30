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
    private readonly Func<string?, string?, string?>? _regionName;
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
    // Report text is black rather than grey, so it stays readable on a dim screen or a page printed with low ink.
    private static readonly SKColor ChartAxisColor = SKColors.Black;
    private static readonly SKColor ChartGridColor = SKColor.Parse(AppColors.ChartGrid);

    /// <summary>
    /// Determines if a chart type should display currency formatting on the Y-axis.
    /// </summary>
    private static bool ShouldShowCurrency(ChartDataType chartType) => !chartType.IsCount();

    private double ConvertFromUSD(double amountUSD, DateTime? date = null) =>
        (double)DisplayCurrency.FromUSD((decimal)amountUSD, _currencyCode, date ?? DateTime.Today);

    // The chart data service keeps its USD points for the next render, so a converted copy is made rather than changing them.
    private ChartDataPoint InDisplayCurrency(ChartDataPoint point) => new()
    {
        Label = point.Label,
        Value = ConvertFromUSD(point.Value, point.Date),
        Date = point.Date,
        Color = point.Color
    };

    /// <summary>
    /// A single USD amount in the report's display currency at its own date (docs/Calculations.md Rule 4).
    /// Aggregate the USD amount properties (<c>EffectiveTotalUSD</c> and the like) through this; never sum the native <c>Total</c> field.
    /// </summary>
    private decimal ToDisplayCurrency(decimal amountUSD, DateTime date) =>
        DisplayCurrency.FromUSD(amountUSD, _currencyCode, date);

    /// <summary>
    /// The date stock is valued at in a report: its end date, as on the Balance Sheet, which
    /// <see cref="DisplayCurrency.ReportDates"/> makes sure has a rate. See docs/Calculations.md §14.
    /// </summary>
    private DateTime InventoryValuationDate => (_config.Filters.EndDate ?? DateTime.Today).Date;

    private string PendingText => Tr("Pending");

    /// <summary>
    /// An amount recorded in <paramref name="currency"/> rather than USD (a return's refund, a loss's
    /// value), formatted in the display currency at its own date, or pending when that date's rate is
    /// unavailable. See docs/Calculations.md §10, Returns and Losses.
    /// </summary>
    private string FormatRecordedAmount(decimal amount, string currency, DateTime date) =>
        DisplayCurrency.FromNative(amount, currency, _currencyCode, date) is { } converted
            ? FormatCurrency(converted)
            : PendingText;

    /// <param name="regionName">Spells a region out from the code an address holds, so a chart of regions reads "Ontario" rather than "ON".</param>
    public ReportRenderer(ReportConfiguration config, CompanyData? companyData, float renderScale = 1f, ITranslationProvider? translationProvider = null, IErrorLogger? errorLogger = null, Func<string?, string?, string?>? regionName = null)
    {
        _config = config;
        _companyData = companyData;
        _renderScale = renderScale;
        _translationProvider = translationProvider ?? DefaultTranslationProvider.Instance;
        _errorLogger = errorLogger;
        _regionName = regionName;

        // Resolve currency code and symbol from company settings
        _currencyCode = companyData == null
            ? "USD"
            : DisplayCurrency.Resolve(companyData.Settings.Localization.Currency, DisplayCurrency.ReportDates(companyData, config.Filters.EndDate));
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
                    ? dataRowHeight * 1.3f + (FootnoteLines(element, tableData.Footnote, rect.Width).Count - 1) * FootnoteLineStep(element)
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

            var title = GetChartTitle(chart.ChartType, chart.GeoLevel);
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
            if (seriesData == null || seriesData.Count == 0 || seriesData[0].DataPoints.Count == 0)
            {
                DrawNoDataPlaceholder(canvas, pieChartArea);
                return;
            }

            // The legend sits on its own row under the axis labels.
            var seriesArea = new SKRect(barChartArea.Left, barChartArea.Top, barChartArea.Right, barChartArea.Bottom - 14 * _renderScale);
            if (DrawsAsLine(chart.ChartStyle, seriesData[0].DataPoints.Count))
                RenderMultiSeriesLineChart(canvas, seriesArea, rect, seriesData, chart);
            else
                RenderMultiSeriesBarChart(canvas, seriesArea, rect, seriesData, chart);
            return;
        }

        // Get single-series chart data
        var chartData = GetChartDataPoints(chart.ChartType, chart.GeoLevel);

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
        else if (DrawsAsLine(chart.ChartStyle, chartData.Count))
        {
            RenderLineChart(canvas, barChartArea, rect, chartData, chart);
        }
        else
        {
            RenderBarChart(canvas, barChartArea, rect, chartData, chart);
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
        noDataPaint.Color = SKColors.Black;
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
            or ChartDataType.AverageTransactionValue
            or ChartDataType.TaxCollectedVsPaid
            or ChartDataType.ExpenseVsRevenueTax
            or ChartDataType.TaxRateDistribution;
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
    /// which falls back to the raw USD figure). Calculations.md Rule 4.
    /// </summary>
    private static bool ConvertsPerTransaction(ChartDataType chartType) => chartType is
        ChartDataType.RevenueDistribution
        or ChartDataType.ExpensesDistribution
        or ChartDataType.AverageTransactionValue
        or ChartDataType.AverageShippingCosts
        or ChartDataType.CountriesOfOrigin
        or ChartDataType.CountriesOfDestination
        or ChartDataType.CompaniesOfOrigin
        or ChartDataType.CompaniesOfDestination
        or ChartDataType.TopCustomersByRevenue
        or ChartDataType.CustomerLifetimeValue
        or ChartDataType.TaxCollectedVsPaid
        or ChartDataType.TaxByCategory
        or ChartDataType.TaxByProduct
        or ChartDataType.ExpenseVsRevenueTax;

    private List<ChartDataPoint>? GetChartDataPoints(ChartDataType chartType, GeoLevel level)
    {
        if (_chartDataService == null)
            return null;

        if (ConvertsPerTransaction(chartType)
            && !UsdConversion.IsUsd(_currencyCode))
        {
            return _chartDataService.GetChartData(
                chartType, (usd, date) => (decimal)ConvertFromUSD((double)usd, date),
                level, _regionName) as List<ChartDataPoint>;
        }

        // Return and loss amounts are in their sale's or purchase's currency, not USD, so each one
        // converts from that currency at its own date (Calculations.md §10).
        decimal? FromNative(decimal amount, string currency, DateTime date) =>
            DisplayCurrency.FromNative(amount, currency, _currencyCode, date);
        if (chartType == ChartDataType.ReturnFinancialImpact)
            return _chartDataService.GetReturnFinancialImpact(FromNative);
        if (chartType == ChartDataType.LossFinancialImpact)
            return _chartDataService.GetLossFinancialImpact(FromNative);

        var data = _chartDataService.GetChartData(chartType, null, level, _regionName);

        if (data is List<ChartDataPoint> dataPoints)
            return ShouldShowCurrency(chartType) ? dataPoints.Select(InDisplayCurrency).ToList() : dataPoints;

        return null;
    }

    /// <summary>
    /// Gets multi-series chart data.
    /// </summary>
    private List<ChartSeriesData>? GetMultiSeriesData(ChartDataType chartType)
    {
        if (_chartDataService == null)
            return null;

        // Counts of transactions in each tax rate bracket, which come back as two lists rather than as series.
        if (chartType == ChartDataType.TaxRateDistribution)
        {
            var (revenueRates, expenseRates, _) = _chartDataService.GetTaxRateDistribution();
            return revenueRates.Count == 0
                ? null
                :
                [
                    new ChartSeriesData { Name = ChartSeriesData.RevenueName, Color = AppColors.Success, DataPoints = revenueRates },
                    new ChartSeriesData { Name = ChartSeriesData.ExpensesName, Color = AppColors.ExpenseRed, DataPoints = expenseRates }
                ];
        }

        // Each day's value converts at its own rate before bucketing, so a wide range does not convert a month total at the month-start date.
        if (chartType == ChartDataType.RevenueVsExpenses
            && !UsdConversion.IsUsd(_currencyCode))
        {
            return _chartDataService.GetRevenueVsExpensesConverted(
                (usd, date) => (decimal)ConvertFromUSD((double)usd, date));
        }

        // Other monthly monetary multi-series charts convert each transaction at its own date inside
        // the data service (same reason as above), so pass the converter and don't re-convert here.
        if (ConvertsPerTransaction(chartType)
            && !UsdConversion.IsUsd(_currencyCode))
        {
            return _chartDataService.GetChartData(
                chartType, (usd, date) => (decimal)ConvertFromUSD((double)usd, date)) as List<ChartSeriesData>;
        }

        var data = _chartDataService.GetChartData(chartType);

        if (data is List<ChartSeriesData> seriesData)
        {
            return ShouldShowCurrency(chartType)
                ? seriesData.Select(s => new ChartSeriesData { Name = s.Name, Color = s.Color, DataPoints = s.DataPoints.Select(InDisplayCurrency).ToList() }).ToList()
                : seriesData;
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

        // Convert each country's revenue at each transaction's OWN date during aggregation (docs/Calculations.md Rule 4) instead of converting the country total at today's rate.
        var converter = UsdConversion.IsUsd(_currencyCode)
            ? (Func<decimal, DateTime, decimal>?)null
            : (usd, date) => (decimal)ConvertFromUSD((double)usd, date);

        var data = _chartDataService.GetChartData(ChartDataType.WorldMap, converter);

        // Values are already in the display currency (or USD); do not re-convert.
        return data as Dictionary<string, double>;
    }

    /// <summary>
    /// The value axis of a bar or line chart: round numbers from <see cref="Min"/> to <see cref="Max"/> in steps of <see cref="Step"/>, always including zero.
    /// </summary>
    private readonly record struct ValueAxis(double Min, double Max, double Step)
    {
        public float ToY(double value, SKRect area) => area.Top + area.Height * (float)((Max - value) / (Max - Min));
    }

    /// <summary>
    /// A value axis that covers the data and zero in about five round steps, such as 0, 5,000, 10,000, with whole steps for a chart that counts things.
    /// </summary>
    private static ValueAxis NiceAxis(double dataMin, double dataMax, bool wholeNumbers)
    {
        var low = Math.Min(0, dataMin);
        var high = Math.Max(0, dataMax);
        if (high - low <= 0)
            high = low + 1;

        var rough = (high - low) / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double[] multiples = wholeNumbers ? [1, 2, 5, 10] : [1, 2, 2.5, 5, 10];
        var step = multiples.Select(m => m * magnitude).First(s => s >= rough * 0.999);
        if (wholeNumbers)
            step = Math.Max(1, Math.Round(step));

        var min = Math.Floor(low / step + 1e-9) * step;
        var max = Math.Ceiling(high / step - 1e-9) * step;

        // A point within a tenth of a step of the edge would touch it, so the axis gets one more step there.
        if (dataMax > 0 && max - dataMax < step * 0.1) max += step;
        if (dataMin < 0 && dataMin - min < step * 0.1) min -= step;

        return new ValueAxis(min, max, step);
    }

    /// <summary>Draws the grid lines, the value labels and both axes of a bar or line chart.</summary>
    private void DrawValueAxis(SKCanvas canvas, SKRect area, ValueAxis axis, ChartReportElement chart, SKTypeface typeface)
    {
        using var gridPaint = new SKPaint { Color = ChartGridColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale, IsAntialias = true };
        using var axisPaint = new SKPaint { Color = ChartAxisColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale, IsAntialias = true };
        using var labelFont = new SKFont(typeface, (float)chart.AxisFontSize * _renderScale);
        using var labelPaint = new SKPaint { Color = ChartAxisColor, IsAntialias = true };

        var steps = (int)Math.Round((axis.Max - axis.Min) / axis.Step);
        var stepPixels = Math.Abs(axis.ToY(axis.Min, area) - axis.ToY(axis.Min + axis.Step, area));
        // A short chart labels only every few steps, counted from zero, so its labels never overlap.
        var every = Math.Max(1, (int)Math.Ceiling(labelFont.Size * 1.5f / stepPixels));
        for (var i = 0; i <= steps; i++)
        {
            var value = axis.Min + i * axis.Step;
            if (Math.Round(value / axis.Step) % every != 0)
                continue;
            var y = axis.ToY(value, area);
            canvas.DrawLine(area.Left, y, area.Right, y, gridPaint);
            canvas.DrawText(FormatAxisValue(value, axis, chart.ChartType), area.Left - 5 * _renderScale, y + 4 * _renderScale, SKTextAlign.Right, labelFont, labelPaint);
        }

        canvas.DrawLine(area.Left, area.Top, area.Left, area.Bottom, axisPaint);
        var zeroY = axis.ToY(0, area);
        canvas.DrawLine(area.Left, zeroY, area.Right, zeroY, axisPaint);
    }

    /// <summary>
    /// A value axis label with as many decimals as the step needs, shortened with K or M when the axis reaches six figures, and with any minus sign before the currency symbol.
    /// </summary>
    private string FormatAxisValue(double value, ValueAxis axis, ChartDataType chartType)
    {
        // Adding up the steps can leave a tiny negative instead of zero, which would print as "-0".
        if (Math.Abs(value) < axis.Step / 1000)
            value = 0;

        var largest = Math.Max(Math.Abs(axis.Min), Math.Abs(axis.Max));
        var (divisor, suffix) = largest >= 1_000_000 ? (1_000_000d, "M") : largest >= 100_000 ? (1_000d, "K") : (1d, "");
        var step = axis.Step / divisor;
        var currency = ShouldShowCurrency(chartType);
        var decimals = Math.Abs(step - Math.Round(step)) < 1e-9 ? 0
            : currency ? 2
            : Math.Abs(step * 10 - Math.Round(step * 10)) < 1e-9 ? 1 : 2;

        var number = (Math.Abs(value) / divisor).ToString("N" + decimals) + suffix;
        var sign = value < 0 ? "-" : "";
        return currency ? $"{sign}{_currencySymbol}{number}" : $"{sign}{number}";
    }

    /// <summary>
    /// Where each point sits across the chart, from <paramref name="left"/> to <paramref name="right"/>: by date when every point has one, so a gap in time shows as a gap, otherwise evenly.
    /// </summary>
    private static float[] XPositions(IReadOnlyList<ChartDataPoint> points, float left, float right)
    {
        if (points.Count == 1)
            return [(left + right) / 2];

        var dates = points.Select(p => p.Date).ToList();
        if (dates.All(d => d.HasValue))
        {
            var first = dates[0]!.Value;
            var span = (dates[^1]!.Value - first).TotalDays;
            var inOrder = dates.Zip(dates.Skip(1)).All(pair => pair.First < pair.Second);
            if (span > 0 && inOrder)
                return dates.Select(d => left + (right - left) * (float)((d!.Value - first).TotalDays / span)).ToArray();
        }

        return Enumerable.Range(0, points.Count).Select(i => left + (right - left) * i / (points.Count - 1)).ToArray();
    }

    /// <summary>
    /// Bar centres: by date while every bar still has room to be seen, otherwise evenly, since days spread over a year would leave hairline bars.
    /// </summary>
    private float[] BarPositions(IReadOnlyList<ChartDataPoint> points, SKRect area)
    {
        var inset = area.Width / points.Count / 2;
        var xs = XPositions(points, area.Left + inset, area.Right - inset);
        if (Slot(xs, area.Width) >= 8 * _renderScale)
            return xs;

        return Enumerable.Range(0, points.Count)
            .Select(i => area.Left + inset + (area.Width - 2 * inset) * i / Math.Max(1, points.Count - 1))
            .ToArray();
    }

    /// <summary>The narrowest gap between two neighbouring points, which is the most room a bar or a label beside it has.</summary>
    private static float Slot(float[] xs, float width) =>
        xs.Length > 1 ? Enumerable.Range(1, xs.Length - 1).Min(i => xs[i] - xs[i - 1]) : width;

    /// <summary>
    /// Each point's x-axis label: its own label, or for dated points the month when the points are months, and the day when they are days.
    /// </summary>
    private string[] XAxisLabels(IReadOnlyList<ChartDataPoint> points)
    {
        if (!points.All(p => p.Date.HasValue))
            return points.Select(p => p.Label).ToArray();

        var dates = points.Select(p => p.Date!.Value).ToList();
        var months = dates.All(d => d.Day == 1) && dates.Select(d => (d.Year, d.Month)).Distinct().Count() == dates.Count;
        var format = months ? _config.Filters.DateFormat
            : dates.Select(d => d.Year).Distinct().Count() > 1 ? "MMM d, yyyy" : "MMM d";
        return dates.Select(d => d.ToString(format)).ToArray();
    }

    /// <summary>
    /// Draws the x-axis labels under the chart. Names are shortened to the room their point has, and a label that would overlap the one before it is left out. Every label stays inside the chart's box.
    /// </summary>
    private void DrawXAxisLabels(SKCanvas canvas, SKRect area, SKRect box, string[] labels, float[] xs, bool shorten,
        ChartReportElement chart, SKTypeface typeface)
    {
        if (labels.Length == 0)
            return;

        using var font = new SKFont(typeface, (float)chart.AxisFontSize * _renderScale);
        using var paint = new SKPaint { Color = ChartAxisColor, IsAntialias = true };
        var gap = 6 * _renderScale;
        var edge = 2 * _renderScale;
        var y = area.Bottom + 18 * _renderScale;

        if (shorten)
        {
            var room = Math.Max(24 * _renderScale, Slot(xs, area.Width) - gap);
            labels = labels.Select(l => TruncateText(l, room, font)).ToArray();
        }

        // Shortened names already fit their own room, so every one is tried; dates are thinned to as many as fit.
        var candidates = shorten
            ? Enumerable.Range(0, labels.Length).ToArray()
            : GetXAxisLabelIndices(labels.Length, area.Width, labels.Max(l => font.MeasureText(l)), _renderScale);
        var previousRight = float.MinValue;
        foreach (var i in candidates)
        {
            var width = font.MeasureText(labels[i]);
            var x = Math.Min(Math.Max(xs[i], box.Left + edge + width / 2), box.Right - edge - width / 2);
            if (x - width / 2 < previousRight + gap)
                continue;

            canvas.DrawText(labels[i], x, y, SKTextAlign.Center, font, paint);
            previousRight = x + width / 2;
        }
    }

    /// <summary>
    /// Continues <paramref name="path"/> through <paramref name="points"/> as a smooth curve. Each control point stays between the two points it joins, so the curve never swings past the data, such as dipping below zero between two small values.
    /// </summary>
    private static void AddSmoothCurve(SKPath path, SKPoint[] points)
    {
        const float tension = 0.5f;
        for (var i = 0; i < points.Length - 1; i++)
        {
            var p0 = i > 0 ? points[i - 1] : points[i];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i < points.Length - 2 ? points[i + 2] : points[i + 1];

            float Between(float value, float a, float b) => Math.Clamp(value, Math.Min(a, b), Math.Max(a, b));
            var cp1 = new SKPoint(
                Between(p1.X + (p2.X - p0.X) * tension / 3, p1.X, p2.X),
                Between(p1.Y + (p2.Y - p0.Y) * tension / 3, p1.Y, p2.Y));
            var cp2 = new SKPoint(
                Between(p2.X - (p3.X - p1.X) * tension / 3, p1.X, p2.X),
                Between(p2.Y - (p3.Y - p1.Y) * tension / 3, p1.Y, p2.Y));

            path.CubicTo(cp1, cp2, p2);
        }
    }

    /// <summary>The line through the points: steps for the step style, otherwise a smooth curve.</summary>
    private static SKPath LinePath(SKPoint[] points, ReportChartStyle style)
    {
        var path = new SKPath();
        path.MoveTo(points[0]);
        if (style == ReportChartStyle.StepLine)
        {
            for (var i = 1; i < points.Length; i++)
            {
                path.LineTo(points[i].X, points[i - 1].Y);
                path.LineTo(points[i]);
            }
        }
        else
        {
            AddSmoothCurve(path, points);
        }
        return path;
    }

    /// <summary>
    /// Whether a chart is drawn as a line rather than bars. A line through one point would be a lone dot, so one point is drawn as a bar, except in a scatter chart, which is meant to be dots.
    /// </summary>
    private static bool DrawsAsLine(ReportChartStyle style, int pointCount) =>
        style == ReportChartStyle.Scatter
        || (style is ReportChartStyle.Line or ReportChartStyle.StepLine or ReportChartStyle.Area && pointCount > 1);

    /// <summary>
    /// Renders a bar chart using SkiaSharp.
    /// </summary>
    private void RenderBarChart(SKCanvas canvas, SKRect chartArea, SKRect box, List<ChartDataPoint> dataPoints, ChartReportElement chart)
    {
        if (dataPoints.Count == 0) return;

        var typeface = PlatformTypefaces.Resolve(chart.FontFamily);
        var axis = NiceAxis(dataPoints.Min(p => p.Value), dataPoints.Max(p => p.Value), !ShouldShowCurrency(chart.ChartType));
        DrawValueAxis(canvas, chartArea, axis, chart, typeface);

        var xs = BarPositions(dataPoints, chartArea);
        var barWidth = Math.Max(1 * _renderScale, Math.Min(50 * _renderScale, Slot(xs, chartArea.Width) * 0.7f));
        var zeroY = axis.ToY(0, chartArea);

        // Profit and tax liability bars take their color from their sign; other charts use one color throughout.
        using var barPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        for (var i = 0; i < dataPoints.Count; i++)
        {
            var y = axis.ToY(dataPoints[i].Value, chartArea);
            barPaint.Color = ChartColors.ForValue(chart.ChartType, dataPoints[i].Value);
            canvas.DrawRect(new SKRect(xs[i] - barWidth / 2, Math.Min(y, zeroY), xs[i] + barWidth / 2, Math.Max(y, zeroY)), barPaint);
        }

        DrawXAxisLabels(canvas, chartArea, box, XAxisLabels(dataPoints), xs, !dataPoints.All(p => p.Date.HasValue), chart, typeface);
    }

    /// <summary>
    /// Renders a line chart using SkiaSharp.
    /// Supports Line, StepLine, Area and Scatter styles.
    /// </summary>
    private void RenderLineChart(SKCanvas canvas, SKRect chartArea, SKRect box, List<ChartDataPoint> dataPoints, ChartReportElement chart)
    {
        if (dataPoints.Count == 0) return;

        var typeface = PlatformTypefaces.Resolve(chart.FontFamily);
        var axis = NiceAxis(dataPoints.Min(p => p.Value), dataPoints.Max(p => p.Value), !ShouldShowCurrency(chart.ChartType));
        DrawValueAxis(canvas, chartArea, axis, chart, typeface);

        var xs = XPositions(dataPoints, chartArea.Left, chartArea.Right);
        var points = dataPoints.Select((p, i) => new SKPoint(xs[i], axis.ToY(p.Value, chartArea))).ToArray();
        var zeroY = axis.ToY(0, chartArea);

        // A profit chart is green above zero and red below it, so the line and its fill are drawn once for each side of zero.
        var sides = new[]
        {
            (Color: ChartColors.ForValue(chart.ChartType, 1), Clip: new SKRect(box.Left, box.Top, box.Right, zeroY)),
            (Color: ChartColors.ForValue(chart.ChartType, -1), Clip: new SKRect(box.Left, zeroY, box.Right, box.Bottom))
        };

        if (chart.ChartStyle == ReportChartStyle.Area)
        {
            using var path = new SKPath();
            path.MoveTo(points[0].X, zeroY);
            path.LineTo(points[0]);
            AddSmoothCurve(path, points);
            path.LineTo(points[^1].X, zeroY);
            path.Close();

            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            foreach (var (color, clip) in sides)
            {
                fillPaint.Color = color.WithAlpha(80);
                canvas.Save();
                canvas.ClipRect(clip);
                canvas.DrawPath(path, fillPaint);
                canvas.Restore();
            }
        }

        // Draw the line (skip for Scatter charts - only points)
        if (chart.ChartStyle != ReportChartStyle.Scatter)
        {
            using var linePath = LinePath(points, chart.ChartStyle);
            using var linePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2 * _renderScale,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };
            foreach (var (color, clip) in sides)
            {
                linePaint.Color = color;
                canvas.Save();
                canvas.ClipRect(clip);
                canvas.DrawPath(linePath, linePaint);
                canvas.Restore();
            }
        }

        using var pointPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        for (var i = 0; i < points.Length; i++)
        {
            pointPaint.Color = ChartColors.ForValue(chart.ChartType, dataPoints[i].Value);
            canvas.DrawCircle(points[i], 4 * _renderScale, pointPaint);
        }

        DrawXAxisLabels(canvas, chartArea, box, XAxisLabels(dataPoints), xs, !dataPoints.All(p => p.Date.HasValue), chart, typeface);
    }

    /// <summary>
    /// Renders a pie chart using SkiaSharp.
    /// Styled to match the dashboard/analytics pie charts: pie shifted left of the legend, or centered
    /// without one, legend vertically centered with circular color indicators, no slice borders.
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
        // The pie sits left of a legend, or in the middle when there is none: hidden, or no room for it.
        var legendX = chartArea.Left + chartArea.Width * 0.58f;
        var hasLegend = chart.ShowLegend && legendX < chartArea.Right - 50 * _renderScale;

        var pieSize = Math.Min(hasLegend ? chartArea.Width * 0.5f : chartArea.Width, chartArea.Height) * 0.85f;
        var radius = pieSize / 2;
        if (radius <= 0) return;

        var centerX = hasLegend ? chartArea.Left + chartArea.Width * 0.3f : chartArea.MidX;
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
        if (hasLegend)
        {
            var percentRightEdge = chartArea.Right - 5 * _renderScale;
            var lineHeight = 18 * _renderScale;
            var totalLegendHeight = slices.Count * lineHeight;
            var legendY = chartArea.MidY - totalLegendHeight / 2;
            var circleRadius = 5 * _renderScale;

            using var percentPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

            foreach (var slice in slices)
            {
                var percentage = (float)(slice.Value / total);

                using var dotPaint = new SKPaint { Color = slice.Color, Style = SKPaintStyle.Fill, IsAntialias = true };

                // Circular color indicator
                var dotCenterY = legendY + circleRadius;
                canvas.DrawCircle(legendX + circleRadius, dotCenterY, circleRadius, dotPaint);

                var textX = legendX + circleRadius * 2 + 6 * _renderScale;
                var textY = dotCenterY + circleRadius * 0.4f;

                // Percentage text (lighter gray, right-aligned to chart edge)
                var percentText = $"{percentage * 100:F1}%";
                canvas.DrawText(percentText, percentRightEdge, textY, SKTextAlign.Right, labelFont, percentPaint);

                // A name is cut only where it would run into its percentage, so a smaller font shows more of it.
                var nameWidth = percentRightEdge - labelFont.MeasureText(percentText) - 8 * _renderScale - textX;
                canvas.DrawText(TruncateText(slice.Label, nameWidth, labelFont), textX, textY, SKTextAlign.Left, labelFont, labelPaint);

                legendY += lineHeight;
            }
        }
    }

    /// <summary>
    /// Renders a multi-series bar chart (e.g., Revenue vs Expenses).
    /// </summary>
    private void RenderMultiSeriesBarChart(SKCanvas canvas, SKRect chartArea, SKRect box, List<ChartSeriesData> seriesData, ChartReportElement chart)
    {
        var allDataPoints = seriesData.SelectMany(s => s.DataPoints).ToList();
        if (allDataPoints.Count == 0) return;

        var typeface = PlatformTypefaces.Resolve(chart.FontFamily);
        var axis = NiceAxis(allDataPoints.Min(p => p.Value), allDataPoints.Max(p => p.Value), !ShouldShowCurrency(chart.ChartType));
        DrawValueAxis(canvas, chartArea, axis, chart, typeface);

        var categories = seriesData[0].DataPoints;
        if (categories.Count == 0) return;

        var xs = BarPositions(categories, chartArea);
        var seriesCount = seriesData.Count;
        var groupWidth = Math.Min(Slot(xs, chartArea.Width) * 0.8f, seriesCount * 40 * _renderScale);
        var barSpacing = Math.Min(4 * _renderScale, groupWidth * 0.1f);
        var barWidth = Math.Max(1 * _renderScale, (groupWidth - barSpacing * (seriesCount - 1)) / seriesCount);
        var zeroY = axis.ToY(0, chartArea);

        using var barPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
        {
            var barStartX = xs[categoryIndex] - groupWidth / 2;
            for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                var series = seriesData[seriesIndex];
                if (categoryIndex >= series.DataPoints.Count) continue;

                var x = barStartX + seriesIndex * (barWidth + barSpacing);
                var y = axis.ToY(series.DataPoints[categoryIndex].Value, chartArea);
                barPaint.Color = SKColor.Parse(series.Color);
                canvas.DrawRect(new SKRect(x, Math.Min(y, zeroY), x + barWidth, Math.Max(y, zeroY)), barPaint);
            }
        }

        DrawXAxisLabels(canvas, chartArea, box, XAxisLabels(categories), xs, !categories.All(p => p.Date.HasValue), chart, typeface);
        DrawSeriesLegend(canvas, box, seriesData, chart, typeface);
    }

    /// <summary>
    /// Renders a multi-series line/area chart (e.g., Revenue vs Expenses as lines).
    /// </summary>
    private void RenderMultiSeriesLineChart(SKCanvas canvas, SKRect chartArea, SKRect box, List<ChartSeriesData> seriesData, ChartReportElement chart)
    {
        var allDataPoints = seriesData.SelectMany(s => s.DataPoints).ToList();
        if (allDataPoints.Count == 0) return;

        var typeface = PlatformTypefaces.Resolve(chart.FontFamily);
        var axis = NiceAxis(allDataPoints.Min(p => p.Value), allDataPoints.Max(p => p.Value), !ShouldShowCurrency(chart.ChartType));
        DrawValueAxis(canvas, chartArea, axis, chart, typeface);

        var categories = seriesData[0].DataPoints;
        if (categories.Count == 0) return;

        var xs = XPositions(categories, chartArea.Left, chartArea.Right);
        var zeroY = axis.ToY(0, chartArea);

        foreach (var series in seriesData)
        {
            var count = Math.Min(series.DataPoints.Count, xs.Length);
            if (count == 0) continue;

            var color = SKColor.Parse(series.Color);
            var points = series.DataPoints.Take(count).Select((p, i) => new SKPoint(xs[i], axis.ToY(p.Value, chartArea))).ToArray();

            if (chart.ChartStyle == ReportChartStyle.Area)
            {
                using var path = new SKPath();
                path.MoveTo(points[0].X, zeroY);
                path.LineTo(points[0]);
                AddSmoothCurve(path, points);
                path.LineTo(points[^1].X, zeroY);
                path.Close();

                using var fillPaint = new SKPaint { Color = color.WithAlpha(80), Style = SKPaintStyle.Fill, IsAntialias = true };
                canvas.DrawPath(path, fillPaint);
            }

            // Draw the line (skip for Scatter charts - only points)
            if (chart.ChartStyle != ReportChartStyle.Scatter)
            {
                using var linePath = LinePath(points, chart.ChartStyle);
                using var linePaint = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2 * _renderScale,
                    IsAntialias = true,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round
                };
                canvas.DrawPath(linePath, linePaint);
            }

            using var pointPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
            foreach (var point in points)
                canvas.DrawCircle(point, 4 * _renderScale, pointPaint);
        }

        DrawXAxisLabels(canvas, chartArea, box, XAxisLabels(categories), xs, !categories.All(p => p.Date.HasValue), chart, typeface);
        DrawSeriesLegend(canvas, box, seriesData, chart, typeface);
    }

    /// <summary>Draws which color is which series, centered along the bottom of the chart's box.</summary>
    private void DrawSeriesLegend(SKCanvas canvas, SKRect box, List<ChartSeriesData> seriesData, ChartReportElement chart, SKTypeface typeface)
    {
        using var font = new SKFont(typeface, (float)chart.AxisFontSize * _renderScale);
        using var textPaint = new SKPaint { Color = ChartAxisColor, IsAntialias = true };
        using var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };

        var radius = 4 * _renderScale;
        var dotGap = 4 * _renderScale;
        var itemGap = 14 * _renderScale;
        var names = seriesData.Select(s => Tr(s.Name)).ToList();
        var total = names.Sum(n => radius * 2 + dotGap + font.MeasureText(n)) + itemGap * (names.Count - 1);

        var x = box.MidX - total / 2;
        var y = box.Bottom - 10 * _renderScale;
        for (var i = 0; i < names.Count; i++)
        {
            dotPaint.Color = SKColor.Parse(seriesData[i].Color);
            canvas.DrawCircle(x + radius, y - font.Size * 0.35f, radius, dotPaint);
            x += radius * 2 + dotGap;
            canvas.DrawText(names[i], x, y, SKTextAlign.Left, font, textPaint);
            x += font.MeasureText(names[i]) + itemGap;
        }
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
            // The map needs LiveCharts started, which creating its first axis does, and a report can be drawn before any chart on screen has made one.
            _ = new Axis();
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

    /// <summary>A date in the format the company picked in Settings.</summary>
    private string FormatDate(DateTime date) => DateFormats.Format(date, _companyData?.Settings.Localization.DateFormat);

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
                        "Date" => FormatDate(r.IssueDate),
                        "ID" => r.InvoiceNumber,
                        "Company" => r.CustomerName,
                        "Due Date" => FormatDate(r.DueDate),
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
                        "Date" => FormatDate(r.Date),
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
                        "Date" => FormatDate(r.StartDate),
                        "Due Date" => FormatDate(r.DueDate),
                        "Return Date" => r.ReturnDate is { } returned ? FormatDate(returned) : "",
                        "Rate" => r.RateAmount is { } rate ? FormatCurrency(rate) : "",
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
                        "Unit Cost" => FormatCurrency(ToDisplayCurrency(r.UnitCost, InventoryValuationDate)),
                        "Total" => FormatCurrency(ToDisplayCurrency(r.TotalValue, InventoryValuationDate)),
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
                        "Date" => FormatDate(r.OrderDate),
                        "Due Date" => FormatDate(r.ExpectedDeliveryDate),
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
                        "Date" => FormatDate(r.Timestamp),
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
                        "Date" => FormatDate(r.TransferDate),
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
                        "Date" => FormatDate(r.ReturnDate),
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
                        "Date" => FormatDate(r.ReportedDate),
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
                        "Date" => FormatDate(r.Date),
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
            "Date" => FormatDate(r.Date),
            "ID" => r.TransactionId,
            "Company" => r.CompanyName,
            "Product" => r.ProductName,
            // Invariant, like the currency cells, so the footer's re-parse reads "1.5" as one and a half.
            "Qty" => r.Quantity.ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture),
            "Unit Price" => FormatCurrency(r.UnitPrice),
            // r.Total is USD-normalized (see ReportTableDataService); convert it to the display currency at the row's date instead of stamping the symbol on a raw dollar figure.
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
            var label = summary.TransactionType switch
            {
                TransactionType.Revenue => Tr("Total Revenue"),
                TransactionType.Expenses => Tr("Total Expenses"),
                _ => Tr("Net Profit")
            };
            lines.Add($"{label}: {(IsSummaryCostPending(summary) ? PendingText : FormatCurrency(total))}");
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
            if (IsSummaryGrowthPending(summary))
            {
                lines.Add($"{Tr("Growth Rate")}: {PendingText}");
            }
            else
            {
                var growth = CalculateGrowthRate(summary);
                var sign = growth >= 0 ? "+" : "";
                lines.Add($"{Tr("Growth Rate")}: {sign}{growth:N1}%");
            }
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
                    // The filled bar marks the total on its own, so it extends up over the previous row's grid line rather than adding a border.
                    var fillTop = currentY > rect.Top + 1 * _renderScale ? currentY - 1 * _renderScale : currentY;
                    var rowRect = new SKRect(rect.Left, fillTop, rect.Right, currentY + dataRowHeight * 1.1f);
                    canvas.DrawRect(rowRect, new SKPaint { Color = ParseColor(element.TotalBackgroundColor), Style = SKPaintStyle.Fill });

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
                    var barTop = currentY;
                    var barHeight = dataRowHeight * 1.3f;

                    // The filled bar marks the total on its own, so it extends up over the previous row's grid line rather than adding a border.
                    var fillTop = currentY > rect.Top + 1 * _renderScale ? currentY - 1 * _renderScale : currentY;
                    var rowRect = new SKRect(rect.Left, fillTop, rect.Right, barTop + barHeight);
                    canvas.DrawRect(rowRect, new SKPaint { Color = ParseColor(element.TotalBackgroundColor), Style = SKPaintStyle.Fill });

                    using var grandPaint = new SKPaint { Color = ParseColor(element.TotalTextColor), IsAntialias = true };
                    canvas.DrawText(row.Label, contentLeft + cellPadding, barTop + barHeight * 0.65f, SKTextAlign.Left, grandTotalFont, grandPaint);

                    var valX = contentLeft + columnWidths[0];
                    for (int i = 0; i < row.Values.Count && i + 1 < columnWidths.Length; i++)
                    {
                        var cellClip = new SKRect(valX, barTop, valX + columnWidths[i + 1], barTop + barHeight);
                        DrawClippedText(canvas, row.Values[i], valX + columnWidths[i + 1] - cellPadding, barTop + barHeight * 0.65f, SKTextAlign.Right, grandTotalFont, grandPaint, cellClip);
                        valX += columnWidths[i + 1];
                    }

                    currentY = barTop + barHeight;
                    break;
                }

                case AccountingRowType.SeparatorLine:
                {
                    // A subtotal's rule or a total's bar already sets it apart, so a separator just above it would be a second line.
                    if (rowIdx + 1 < endRowIndex && tableData.Rows[rowIdx + 1].RowType
                            is AccountingRowType.SubtotalRow or AccountingRowType.TotalRow or AccountingRowType.GrandTotalRow)
                        break;

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
            var baseline = currentY + dataFontSize;
            foreach (var line in FootnoteLines(element, tableData.Footnote, rect.Width))
            {
                if (baseline > rect.Bottom - 2 * _renderScale)
                    break;
                canvas.DrawText(line, rect.MidX, baseline, SKTextAlign.Center, italicFont, footnotePaint);
                baseline += FootnoteLineStep(element);
            }
        }

        // --- Draw Outer Border ---
        canvas.DrawRect(rect, new SKPaint { Color = ParseColor(element.GridLineColor), Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _renderScale });
    }

    private float FootnoteLineStep(AccountingTableReportElement element) => (float)element.FontSize * _renderScale * 1.25f;

    /// <summary>An accounting table's footnote broken into lines that fit inside the table, in the italic font it is drawn in.</summary>
    private List<string> FootnoteLines(AccountingTableReportElement element, string footnote, float tableWidth)
    {
        using var font = new SKFont(PlatformTypefaces.Resolve(element.FontFamily, SKFontStyle.Italic), (float)element.FontSize * _renderScale * 0.9f);
        var maxWidth = tableWidth - (float)element.CellPadding * _renderScale * 2;

        var lines = new List<string>();
        var line = "";
        foreach (var word in footnote.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.MeasureText(candidate) > maxWidth)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }
        if (line.Length > 0)
            lines.Add(line);
        return lines;
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
            canvas.DrawText(string.Join(", ", addressParts), textStartX, currentY, SKTextAlign.Left, detailFont, blackPaint);
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
            canvas.DrawText(string.Join("  |  ", contactParts), textStartX, currentY, SKTextAlign.Left, detailFont, blackPaint);
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
        footerPaint.Color = SKColors.Black;
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
    /// Calculates which indices should have X-axis labels displayed.
    /// Calculates how many labels fit based on available width and distributes them evenly,
    /// matching how LiveCharts handles axis labels.
    /// </summary>
    private static int[] GetXAxisLabelIndices(int totalCount, float chartWidth, float measuredLabelWidth, float renderScale)
    {
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

    private string GetChartTitle(ChartDataType chartType, GeoLevel level) =>
        Tr(chartType.GetDisplayName(level));

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
        textPaint.Color = SKColors.Black;
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

        var (startDate, endDate) = SummaryDateRange();
        return SummaryAmount(summary.TransactionType, startDate, endDate, ToDisplayCurrency);
    }

    /// <summary>
    /// Whether the Summary box's net profit is still waiting for a sale's stock cost, as the Net
    /// Profit card is (Calculations.md §14).
    /// </summary>
    private bool IsSummaryCostPending(SummaryReportElement summary)
    {
        if (_companyData == null || summary.TransactionType is TransactionType.Revenue or TransactionType.Expenses)
            return false;

        var (startDate, endDate) = SummaryDateRange();
        return CostOfGoodsAggregator.IsCostOfGoodsPending(_companyData.Revenues, startDate, endDate, collectedOnly: true);
    }

    /// <summary>The net profit growth rate compares two periods, so either one waiting for a stock cost leaves it pending.</summary>
    private bool IsSummaryGrowthPending(SummaryReportElement summary)
    {
        if (_companyData == null || summary.TransactionType is TransactionType.Revenue or TransactionType.Expenses)
            return false;

        var (startDate, endDate) = SummaryDateRange();
        var (previousStart, previousEnd) = SummaryComparisonRange(startDate, endDate);
        return CostOfGoodsAggregator.IsProfitChangePending(_companyData.Revenues, startDate, endDate, previousStart, previousEnd);
    }

    private int CalculateTransactionCount(SummaryReportElement summary)
    {
        if (_companyData == null) return 0;

        var (startDate, endDate) = SummaryDateRange();
        var revenues = RevenueAggregator.OnlyCollected(_companyData.Revenues).Count(s => s.Date >= startDate && s.Date <= endDate);
        var expenses = _companyData.Expenses.Count(p => p.Date >= startDate && p.Date <= endDate);

        return summary.TransactionType switch
        {
            TransactionType.Revenue => revenues,
            TransactionType.Expenses => expenses,
            _ => revenues + expenses
        };
    }

    private decimal CalculateAverageValue(SummaryReportElement summary)
    {
        if (_companyData == null) return 0;

        var (startDate, endDate) = SummaryDateRange();
        IEnumerable<decimal> amounts = summary.TransactionType switch
        {
            TransactionType.Revenue => RevenueAggregator.OnlyCollected(_companyData.Revenues)
                .Where(s => s.Date >= startDate && s.Date <= endDate)
                .Select(s => ToDisplayCurrency(s.EffectiveTotalUSD, s.Date)),
            TransactionType.Expenses => _companyData.Expenses
                .Where(p => p.Date >= startDate && p.Date <= endDate)
                .Select(p => ToDisplayCurrency(p.EffectiveTotalUSD, p.Date)),
            _ => []
        };

        var list = amounts.ToList();
        return list.Count > 0 ? list.Average() : 0;
    }

    private double CalculateGrowthRate(SummaryReportElement summary)
    {
        if (_companyData == null) return 0;

        var (startDate, endDate) = SummaryDateRange();
        var (previousStart, previousEnd) = SummaryComparisonRange(startDate, endDate);

        // A ratio, so it compares USD totals (Calculations.md §3).
        var current = SummaryAmount(summary.TransactionType, startDate, endDate, (usd, _) => usd);
        var previous = SummaryAmount(summary.TransactionType, previousStart, previousEnd, (usd, _) => usd);

        if (previous == 0)
            return current > 0 ? 100 : 0;

        return (double)((current - previous) / Math.Abs(previous) * 100);
    }

    /// <summary>
    /// What the Summary box totals, on the basis of the report's own charts (Calculations.md §11):
    /// collected revenue less refunds, all expenses, or net profit for any other transaction type.
    /// </summary>
    private decimal SummaryAmount(TransactionType type, DateTime start, DateTime end, Func<decimal, DateTime, decimal> convert)
    {
        var data = _companyData!;
        return type switch
        {
            TransactionType.Revenue =>
                RevenueAggregator.SumCollectedRevenueDisplay(data.Revenues, start, end, convert)
                - RefundAggregator.GetRefundedInDateRangeDisplay(RefundAggregator.ForPaidSales(data), start, end, convert),
            TransactionType.Expenses => ExpenseAggregator.SumExpensesDisplay(data.Expenses, start, end, convert),
            _ => ProfitCalculator.CalculateNetProfitDisplay(data, start, end, convert)
        };
    }

    private (DateTime Start, DateTime End) SummaryDateRange() =>
        _config.Filters.GetDateRange(_companyData?.GetEarliestDate());

    private (DateTime Start, DateTime End) SummaryComparisonRange(DateTime start, DateTime end) =>
        ComparisonPeriod.For(DateRangePresetExtensions.ParseDateRange(_config.Filters.DatePresetName), start, end);

    /// <summary>
    /// Formats an amount in the report's display currency.
    /// </summary>
    private string FormatCurrency(decimal amount) => CurrencyInfo.FormatAmount(amount, _currencyCode);

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
