namespace ArgoBooks.Core.Models.Reports;

/// <summary>
/// Helper class for dynamically positioning elements in report templates.
/// Provides consistent layout calculations for grids, stacks, and other arrangements.
/// </summary>
public static class TemplateLayoutHelper
{
    /// <summary>
    /// Context containing page dimensions and layout parameters for calculating element positions.
    /// </summary>
    public class LayoutContext
    {
        /// <summary>
        /// Total page width in pixels.
        /// </summary>
        public double PageWidth { get; }

        /// <summary>
        /// Total page height in pixels.
        /// </summary>
        public double PageHeight { get; }

        /// <summary>
        /// Left margin from page edge.
        /// </summary>
        public double MarginLeft { get; }

        /// <summary>
        /// Right margin from page edge.
        /// </summary>
        public double MarginRight { get; }

        /// <summary>
        /// Top margin from page edge.
        /// </summary>
        public double MarginTop { get; }

        /// <summary>
        /// Bottom margin from page edge.
        /// </summary>
        public double MarginBottom { get; }
        /// <summary>
        /// Height of the header area. Varies based on whether company details are shown.
        /// </summary>
        public double HeaderHeight { get; }

        /// <summary>
        /// Height of the footer area.
        /// </summary>
        public double FooterHeight { get; } = PageDimensions.FooterHeight;

        /// <summary>
        /// Spacing between elements.
        /// </summary>
        public double ElementSpacing { get; } = 20;

        /// <summary>
        /// Height of the date range element.
        /// </summary>
        public double DateRangeHeight { get; } = 30;

        /// <summary>
        /// Y position where the date range element should be placed (just below header).
        /// </summary>
        public double DateRangeTop => MarginTop + HeaderHeight + 2;

        /// <summary>
        /// Y position where content starts (below date range area).
        /// </summary>
        public double ContentTop => DateRangeTop + DateRangeHeight + ElementSpacing;

        /// <summary>
        /// Available width for content (page width minus margins).
        /// </summary>
        public double ContentWidth => PageWidth - MarginLeft - MarginRight;

        /// <summary>
        /// Available height for content (from ContentTop to footer area).
        /// </summary>
        public double ContentHeight => PageHeight - ContentTop - FooterHeight - MarginBottom;

        /// <summary>
        /// Creates a layout context from a report configuration.
        /// </summary>
        public LayoutContext(ReportConfiguration config)
        {
            var (width, height) = PageDimensions.GetDimensions(config.PageSize, config.PageOrientation);
            PageWidth = width;
            PageHeight = height;
            HeaderHeight = PageDimensions.GetHeaderHeight(config.ShowCompanyDetails);
            MarginLeft = config.PageMargins.Left;
            MarginRight = config.PageMargins.Right;
            MarginTop = config.PageMargins.Top;
            MarginBottom = config.PageMargins.Bottom;
        }

        /// <summary>
        /// Creates a layout context with explicit page dimensions.
        /// </summary>
        public LayoutContext(double pageWidth, double pageHeight, bool showCompanyDetails = false)
        {
            PageWidth = pageWidth;
            PageHeight = pageHeight;
            HeaderHeight = PageDimensions.GetHeaderHeight(showCompanyDetails);
            MarginLeft = PageDimensions.Margin;
            MarginRight = PageDimensions.Margin;
            MarginTop = PageDimensions.Margin;
            MarginBottom = PageDimensions.Margin;
        }
    }

    /// <summary>
    /// Represents a rectangular area with position and dimensions.
    /// </summary>
    public readonly struct LayoutRect
    {
        /// <summary>
        /// X position (left edge).
        /// </summary>
        public double X { get; }

        /// <summary>
        /// Y position (top edge).
        /// </summary>
        public double Y { get; }

        /// <summary>
        /// Width of the rectangle.
        /// </summary>
        public double Width { get; }

        /// <summary>
        /// Height of the rectangle.
        /// </summary>
        public double Height { get; }

        /// <summary>
        /// Creates a new layout rectangle.
        /// </summary>
        public LayoutRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Creates a grid layout with the specified number of rows and columns.
    /// Grid is positioned below the date range element.
    /// </summary>
    /// <param name="context">The layout context with page dimensions.</param>
    /// <param name="rows">Number of rows in the grid.</param>
    /// <param name="columns">Number of columns in the grid.</param>
    /// <returns>A 2D array of rectangles representing the grid cells.</returns>
    public static LayoutRect[,] CreateGrid(LayoutContext context, int rows, int columns)
    {
        // ContentTop already accounts for date range area
        var startY = context.ContentTop;
        var availableHeight = context.ContentHeight;

        var cellWidth = (context.ContentWidth - (context.ElementSpacing * (columns - 1))) / columns;
        var cellHeight = (availableHeight - (context.ElementSpacing * (rows - 1))) / rows;

        var grid = new LayoutRect[rows, columns];

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                var x = context.MarginLeft + (col * (cellWidth + context.ElementSpacing));
                var y = startY + (row * (cellHeight + context.ElementSpacing));
                grid[row, col] = new LayoutRect(x, y, cellWidth, cellHeight);
            }
        }

        return grid;
    }

    /// <summary>
    /// Creates a vertical stack layout with proportional heights.
    /// Stack is positioned below the date range element.
    /// </summary>
    /// <param name="context">The layout context with page dimensions.</param>
    /// <param name="heightRatios">Proportional height ratios for each element (should sum to 1.0).</param>
    /// <returns>An array of rectangles representing the stacked elements.</returns>
    public static LayoutRect[] CreateVerticalStack(LayoutContext context, params double[] heightRatios)
    {
        // ContentTop already accounts for date range area
        var startY = context.ContentTop;
        var availableHeight = context.ContentHeight;

        availableHeight -= context.ElementSpacing * (heightRatios.Length - 1);

        var stack = new LayoutRect[heightRatios.Length];
        var currentY = startY;

        for (int i = 0; i < heightRatios.Length; i++)
        {
            var height = availableHeight * heightRatios[i];
            stack[i] = new LayoutRect(context.MarginLeft, currentY, context.ContentWidth, height);
            currentY += height + context.ElementSpacing;
        }

        return stack;
    }

    /// <summary>
    /// Gets the bounds for a date range element positioned at the top of the content area.
    /// </summary>
    /// <param name="context">The layout context with page dimensions.</param>
    /// <returns>A rectangle representing the date range element bounds.</returns>
    public static LayoutRect GetDateRangeBounds(LayoutContext context)
    {
        const double dateRangeWidth = 200;
        var centeredX = context.MarginLeft + (context.ContentWidth - dateRangeWidth) / 2;
        return new LayoutRect(centeredX, context.DateRangeTop, dateRangeWidth, context.DateRangeHeight);
    }

    /// <summary>
    /// Splits a layout rectangle into horizontal columns.
    /// </summary>
    /// <param name="area">The area to split.</param>
    /// <param name="widthRatios">Proportional width ratios for each column (should sum to 1.0).</param>
    /// <param name="spacing">Spacing between columns.</param>
    /// <returns>An array of rectangles representing the columns.</returns>
    public static LayoutRect[] SplitHorizontally(LayoutRect area, double[] widthRatios, double spacing = 10)
    {
        var availableWidth = area.Width - (spacing * (widthRatios.Length - 1));
        var columns = new LayoutRect[widthRatios.Length];
        var currentX = area.X;

        for (int i = 0; i < widthRatios.Length; i++)
        {
            var width = availableWidth * widthRatios[i];
            columns[i] = new LayoutRect(currentX, area.Y, width, area.Height);
            currentX += width + spacing;
        }

        return columns;
    }

}
