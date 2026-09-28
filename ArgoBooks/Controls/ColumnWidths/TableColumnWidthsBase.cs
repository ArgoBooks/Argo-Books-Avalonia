using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Controls.ColumnWidths;

/// <summary>
/// Represents a column definition with its sizing properties.
/// </summary>
public class ColumnDef
{
    public string Name { get; set; } = "";
    public double StarValue { get; set; } = 1.0;
    public double MinWidth { get; set; } = 50;
    public double MaxWidth { get; set; } = double.PositiveInfinity;
    public bool IsVisible { get; set; } = true;
    public bool IsFixed { get; set; }
    public double FixedWidth { get; set; } = 100;
    public double CurrentWidth { get; set; }
    public double PreferredWidth { get; set; } = 120;
    public double MeasuredContentWidth { get; set; }
}

/// <summary>
/// Abstract base class for managing column widths in a resizable table.
/// Provides common logic for proportional column distribution, resizing, and scrolling.
/// </summary>
public abstract partial class TableColumnWidthsBase : ObservableObject, ITableColumnWidths
{
    // The table has a 24px inset on its left. On the right a matching 24px gap is left while the
    // columns fit; once they don't and the table scrolls, the columns run to its right edge.
    private const double LeftInset = 24;
    private const double RightGap = 24;
    private const double Insets = LeftInset + RightGap;

    // Every column's registered MinWidth is raised by this much, so a narrow window scrolls
    // before the columns get cramped.
    private const double MinWidthScale = 1.25;

    private double _availableWidth = 1200;
    private bool _isUpdating;
    private bool _hasManualOverflow;
    private bool _hasReceivedInitialWidth;

    /// <summary>
    /// Column definitions dictionary.
    /// </summary>
    protected readonly Dictionary<string, ColumnDef> Columns = new();

    /// <summary>
    /// Ordered list of column names for consistent iteration.
    /// </summary>
    protected string[] ColumnOrder { get; set; } = [];

    /// <summary>
    /// Maps column names to their width setter actions.
    /// </summary>
    protected readonly Dictionary<string, Action<double>> ColumnSetters = new();

    /// <summary>
    /// Gets the minimum total width required for all visible columns.
    /// </summary>
    [ObservableProperty]
    private double _minimumTotalWidth;

    /// <summary>
    /// Gets whether the table needs horizontal scrolling.
    /// </summary>
    [ObservableProperty]
    private bool _needsHorizontalScroll;

    /// <summary>
    /// Gets or sets the X position for the column visibility menu.
    /// </summary>
    [ObservableProperty]
    private double _columnMenuX;

    /// <summary>
    /// Gets or sets the Y position for the column visibility menu.
    /// </summary>
    [ObservableProperty]
    private double _columnMenuY;

    /// <summary>
    /// Registers a column with its definition and width setter.
    /// </summary>
    protected void RegisterColumn(string name, ColumnDef def, Action<double> setter)
    {
        def.Name = name;
        if (!def.IsFixed) def.MinWidth *= MinWidthScale;
        Columns[name] = def;
        ColumnSetters[name] = setter;
    }

    /// <summary>
    /// The star weight used for proportional distribution of a column. Override to vary the
    /// weight by the active column set (e.g. a page with Expense/Revenue tabs). Defaults to
    /// the column's single <see cref="ColumnDef.StarValue"/>.
    /// </summary>
    protected virtual double GetStarValue(ColumnDef col) => col.StarValue;

    /// <summary>
    /// Whether a column belongs to the currently active column set. Override for pages whose
    /// tabs show different columns. The default set contains every registered column.
    /// </summary>
    protected virtual bool IsInActiveSet(ColumnDef col) => true;

    /// <summary>
    /// Whether a column is effectively visible: present in the active set and not hidden by
    /// the user. All width math filters on this rather than <see cref="ColumnDef.IsVisible"/>
    /// directly so that columns absent from the active set never reserve space.
    /// </summary>
    protected bool IsColumnVisible(ColumnDef col) => col.IsVisible && IsInActiveSet(col);

    /// <summary>
    /// Updates column visibility and recalculates widths.
    /// </summary>
    public void SetColumnVisibility(string columnName, bool isVisible)
    {
        if (Columns.TryGetValue(columnName, out var col))
        {
            col.IsVisible = isVisible;
            RecalculateWidths();
        }
    }

    /// <summary>
    /// Sets the available width for the table and recalculates column widths.
    /// </summary>
    public void SetAvailableWidth(double width)
    {
        // Ignore zero or negative widths (can occur during layout transitions)
        if (width < 1) return;

        if (_hasReceivedInitialWidth && Math.Abs(_availableWidth - width) < 1) return;

        var isFirstRealWidth = !_hasReceivedInitialWidth;
        _hasReceivedInitialWidth = true;

        // On the first real width from the UI, always recalculate to match
        // actual available space (constructor uses a default estimate)
        if (isFirstRealWidth)
        {
            _availableWidth = width;
            _hasManualOverflow = false;
            RecalculateWidths();
            return;
        }

        var currentColumnsWidth = Columns.Values
            .Where(IsColumnVisible)
            .Sum(c => c.CurrentWidth);

        if (_hasManualOverflow)
        {
            // Already in overflow state - check if we still need it
            if (width < currentColumnsWidth + Insets + 50)
            {
                // Still overflowing or close to it, maintain scroll state
                _availableWidth = width;
                SetScrollState(true, currentColumnsWidth);
                return;
            }
            // Enough space now (with buffer), can reset overflow state
            _hasManualOverflow = false;
        }

        _availableWidth = width;
        RecalculateWidths();
    }

    /// <summary>
    /// Adjusts a column width by a delta amount.
    /// </summary>
    /// <returns>The actual delta that was applied (may be less than requested due to constraints).</returns>
    public double ResizeColumn(string columnName, double delta)
    {
        if (!Columns.TryGetValue(columnName, out var col)) return 0;
        if (!IsColumnVisible(col) || col.IsFixed) return 0;
        if (Math.Abs(delta) < 0.5) return 0;

        var visibleColumns = ColumnOrder
            .Where(name => Columns.TryGetValue(name, out var c) && IsColumnVisible(c))
            .ToList();

        var columnIndex = visibleColumns.IndexOf(columnName);
        if (columnIndex < 0) return 0;

        var columnsToRight = visibleColumns.Skip(columnIndex + 1).ToList();

        double maxTotalWidth = _availableWidth - Insets;

        var newColWidth = col.CurrentWidth + delta;
        newColWidth = Math.Max(col.MinWidth, Math.Min(col.MaxWidth, newColWidth));
        var actualDelta = newColWidth - col.CurrentWidth;

        if (Math.Abs(actualDelta) < 0.5) return 0;

        col.CurrentWidth = newColWidth;
        ApplyWidthToProperty(columnName, newColWidth);

        // Always try to shrink columns to the right when expanding
        if (actualDelta > 0.5)
        {
            double shrinkNeeded = actualDelta;
            var shrinkableColumns = columnsToRight.Where(name => !Columns[name].IsFixed).ToList();
            foreach (var rightColName in shrinkableColumns)
            {
                var rightCol = Columns[rightColName];
                double availableShrink = rightCol.CurrentWidth - rightCol.MinWidth;
                if (availableShrink > 0.5)
                {
                    double actualShrink = Math.Min(shrinkNeeded, availableShrink);
                    rightCol.CurrentWidth -= actualShrink;
                    ApplyWidthToProperty(rightColName, rightCol.CurrentWidth);
                    shrinkNeeded -= actualShrink;
                    if (shrinkNeeded < 0.5) break;
                }
            }
        }

        // Check if we've exceeded max width
        double newTotalWidth = visibleColumns.Sum(name => Columns[name].CurrentWidth);
        if (newTotalWidth > maxTotalWidth)
        {
            _hasManualOverflow = true;
        }

        UpdateScrollState(visibleColumns);
        return actualDelta;
    }

    /// <summary>
    /// Updates the horizontal scroll state based on current column widths.
    /// </summary>
    protected void UpdateScrollState(List<string> visibleColumns)
    {
        double columnsWidth = visibleColumns.Sum(name => Columns[name].CurrentWidth);

        if (columnsWidth + Insets > _availableWidth + 1)
        {
            _hasManualOverflow = true;
            SetScrollState(true, columnsWidth);
        }
        else
        {
            SetScrollState(false, Columns.Values
                .Where(IsColumnVisible)
                .Sum(c => c.IsFixed ? c.FixedWidth : c.MinWidth));
        }
    }

    /// <summary>
    /// Sets whether the table scrolls sideways, and the width its content is held to: the columns
    /// plus the left inset, plus the right gap only while nothing scrolls.
    /// </summary>
    private void SetScrollState(bool scroll, double columnsWidth)
    {
        NeedsHorizontalScroll = scroll;
        MinimumTotalWidth = columnsWidth + (scroll ? LeftInset : Insets);
    }

    /// <summary>
    /// Auto-sizes a column to fit its content or preferred width.
    /// </summary>
    public void AutoSizeColumn(string columnName)
    {
        if (!Columns.TryGetValue(columnName, out var col)) return;
        if (!IsColumnVisible(col) || col.IsFixed) return;

        double targetWidth = col.MeasuredContentWidth > 0
            ? Math.Max(col.MinWidth, col.MeasuredContentWidth)
            : col.PreferredWidth;

        targetWidth = Math.Max(col.MinWidth, Math.Min(col.MaxWidth, targetWidth));

        var delta = targetWidth - col.CurrentWidth;
        if (Math.Abs(delta) > 0.5)
        {
            ResizeColumn(columnName, delta);
        }
    }

    /// <summary>
    /// Resets all column widths to their default proportional sizes.
    /// </summary>
    public void ResetWidths()
    {
        _hasManualOverflow = false;
        RecalculateWidths();
    }

    /// <summary>
    /// Recalculates all column widths based on available space and visibility.
    /// </summary>
    public void RecalculateWidths()
    {
        if (_isUpdating) return;
        _isUpdating = true;

        try
        {
            var visibleColumns = Columns.Values.Where(IsColumnVisible).ToList();
            if (visibleColumns.Count == 0) return;

            double minColumnsWidth = visibleColumns.Sum(c => c.IsFixed ? c.FixedWidth : c.MinWidth);

            if (_hasManualOverflow)
            {
                double columnsWidth = visibleColumns.Sum(c => c.CurrentWidth);
                if (columnsWidth + Insets + 50 > _availableWidth)
                {
                    SetScrollState(true, columnsWidth);
                    return;
                }
                _hasManualOverflow = false;
            }

            SetScrollState(_availableWidth < minColumnsWidth + Insets, minColumnsWidth);

            double fixedTotal = visibleColumns.Where(c => c.IsFixed).Sum(c => c.FixedWidth);
            double availableForProportional = Math.Max(100, _availableWidth - fixedTotal - Insets);

            if (NeedsHorizontalScroll)
            {
                foreach (var col in visibleColumns)
                {
                    col.CurrentWidth = col.IsFixed ? col.FixedWidth : col.MinWidth;
                    ApplyWidthToProperty(col.Name, col.CurrentWidth);
                }
                return;
            }

            // Multi-pass distribution: when columns are clamped to MinWidth/MaxWidth,
            // redistribute the excess to remaining proportional columns
            var proportionalCols = visibleColumns.Where(c => !c.IsFixed).ToList();
            var locked = new HashSet<string>();
            var columnWidths = new Dictionary<string, double>();
            var remaining = availableForProportional;

            const int maxIterations = 100;
            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                var unlocked = proportionalCols.Where(c => !locked.Contains(c.Name)).ToList();
                if (unlocked.Count == 0) break;

                double totalStarsPass = unlocked.Sum(GetStarValue);
                double widthPerStar = totalStarsPass > 0 ? remaining / totalStarsPass : 0;

                bool anyNewLock = false;
                foreach (var col in unlocked)
                {
                    double proposedWidth = GetStarValue(col) * widthPerStar;
                    if (proposedWidth < col.MinWidth)
                    {
                        columnWidths[col.Name] = col.MinWidth;
                        locked.Add(col.Name);
                        remaining -= col.MinWidth;
                        anyNewLock = true;
                    }
                    else if (proposedWidth > col.MaxWidth)
                    {
                        columnWidths[col.Name] = col.MaxWidth;
                        locked.Add(col.Name);
                        remaining -= col.MaxWidth;
                        anyNewLock = true;
                    }
                }

                if (!anyNewLock)
                {
                    // All remaining columns fit proportionally
                    foreach (var col in unlocked)
                    {
                        columnWidths[col.Name] = GetStarValue(col) * widthPerStar;
                    }
                    break;
                }
            }

            // Apply all widths
            foreach (var col in visibleColumns)
            {
                if (col.IsFixed)
                {
                    col.CurrentWidth = col.FixedWidth;
                }
                else if (columnWidths.TryGetValue(col.Name, out var width))
                {
                    col.CurrentWidth = width;
                }
                ApplyWidthToProperty(col.Name, col.CurrentWidth);
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// Applies the width to the corresponding property using the registered setter.
    /// </summary>
    protected void ApplyWidthToProperty(string columnName, double width)
    {
        if (ColumnSetters.TryGetValue(columnName, out var setter))
        {
            setter(width);
        }
    }

    /// <summary>
    /// Calculates the required width for an Actions column based on the number of icon buttons.
    /// Based on: 8px left margin + (n × 32px button) + ((n-1) × 4px spacing) + 8px right margin
    /// </summary>
    /// <param name="buttonCount">The number of 32x32 icon buttons in the Actions column.</param>
    /// <returns>The calculated width in pixels.</returns>
    public static double ActionsWidth(int buttonCount)
    {
        if (buttonCount <= 0) return 48;
        return 16 + (buttonCount * 32) + ((buttonCount - 1) * 4);
    }
}
