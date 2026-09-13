namespace ArgoBooks.Core.Services.Layout;

/// <summary>
/// Turns a <see cref="SheetGrid"/> plus a hand-written or LLM-produced
/// <see cref="TableRegion"/> into a clean <c>headers + rows</c> table.
/// <para>
/// This class is <b>pure and deterministic</b> with no LLM dependency. It is the
/// piece that makes future AI layout output safe and testable: the AI proposes a
/// <see cref="LayoutDescriptor"/>, and this extractor applies it predictably.
/// </para>
/// <para>Pinned conventions:</para>
/// <list type="bullet">
///   <item>Multi-row headers are joined top-to-bottom with <c>" &gt; "</c>, empty
///   parts skipped, result trimmed.</item>
///   <item>A horizontally merged header cell contributes its top-left value to every
///   column it spans (so "2024" over Q1..Q4 yields "2024 &gt; Q1", "2024 &gt; Q2", ...).</item>
///   <item>Wide/cross-tab regions transpose to long form with headers
///   <c>[&lt;key headers...&gt;, "Column", "Value"]</c>, one row per (data row x spread
///   column), skipping empty spread cells.</item>
/// </list>
/// </summary>
public static class GridExtractor
{
    private const string HeaderJoin = " > ";

    /// <summary>
    /// Extracts a clean table from <paramref name="grid"/> according to
    /// <paramref name="region"/>. Robust to out-of-range indices (clamped/skipped),
    /// ragged rows, and merged header cells.
    /// </summary>
    public static (List<string> Headers, List<List<string>> Rows) Extract(SheetGrid grid, TableRegion region)
    {
        int rowCount = grid.RowCount;
        int colCount = grid.ColCount;

        if (rowCount == 0 || colCount == 0)
            return (new List<string>(), new List<List<string>>());

        // Clamp the column window to the grid.
        int firstCol = Math.Clamp(region.FirstCol, 0, colCount - 1);
        int lastCol = Math.Clamp(region.LastCol, firstCol, colCount - 1);

        // Clamp the data-row window to the grid.
        int firstDataRow = Math.Clamp(region.FirstDataRow, 0, rowCount - 1);
        int lastDataRow = Math.Clamp(region.LastDataRow, firstDataRow, rowCount - 1);

        var ignore = region.IgnoreRows is { Count: > 0 }
            ? new HashSet<int>(region.IgnoreRows)
            : null;

        bool isWide = string.Equals(region.Orientation, "wide", StringComparison.OrdinalIgnoreCase);

        if (isWide)
            return ExtractWide(grid, region, firstCol, lastCol, firstDataRow, lastDataRow, ignore);

        return ExtractLong(grid, region, firstCol, lastCol, firstDataRow, lastDataRow, ignore);
    }

    // ─── Long orientation ────────────────────────────────────────────────────

    private static (List<string>, List<List<string>>) ExtractLong(
        SheetGrid grid, TableRegion region,
        int firstCol, int lastCol, int firstDataRow, int lastDataRow,
        HashSet<int>? ignore)
    {
        var headers = BuildHeaders(grid, region, firstCol, lastCol);

        var rows = new List<List<string>>();
        for (int r = firstDataRow; r <= lastDataRow; r++)
        {
            if (ignore is not null && ignore.Contains(r))
                continue;

            var values = SliceRow(grid, r, firstCol, lastCol);
            if (values.All(string.IsNullOrEmpty))
                continue; // skip fully-empty rows

            rows.Add(values);
        }

        return (headers, rows);
    }

    // ─── Wide / cross-tab orientation (transpose to long) ────────────────────

    private static (List<string>, List<List<string>>) ExtractWide(
        SheetGrid grid, TableRegion region,
        int firstCol, int lastCol, int firstDataRow, int lastDataRow,
        HashSet<int>? ignore)
    {
        var fullHeaders = BuildHeaders(grid, region, firstCol, lastCol);

        // Map a 0-based grid column to its index within the [firstCol..lastCol] header slice.
        // headerSliceIndex = gridCol - firstCol.
        var keyCols = (region.KeyColumns ?? new List<int>())
            .Where(c => c >= firstCol && c <= lastCol)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        var keyColSet = new HashSet<int>(keyCols);

        // Spread columns: every column in the window that is not a key column.
        var spreadCols = new List<int>();
        for (int c = firstCol; c <= lastCol; c++)
            if (!keyColSet.Contains(c))
                spreadCols.Add(c);

        // Output headers: [<key column headers...>, "Column", "Value"].
        var headers = new List<string>();
        foreach (var kc in keyCols)
            headers.Add(fullHeaders[kc - firstCol]);
        headers.Add("Column");
        headers.Add("Value");

        var rows = new List<List<string>>();
        for (int r = firstDataRow; r <= lastDataRow; r++)
        {
            if (ignore is not null && ignore.Contains(r))
                continue;

            // Skip fully-empty key rows (no key values present).
            var keyValues = keyCols.Select(c => CellAt(grid, r, c)).ToList();
            if (keyValues.All(string.IsNullOrEmpty))
                continue;

            foreach (var sc in spreadCols)
            {
                var value = CellAt(grid, r, sc);
                if (string.IsNullOrEmpty(value))
                    continue; // skip empty spread cells

                var outRow = new List<string>(keyValues.Count + 2);
                outRow.AddRange(keyValues);
                outRow.Add(fullHeaders[sc - firstCol]); // spread column's header label
                outRow.Add(value);
                rows.Add(outRow);
            }
        }

        return (headers, rows);
    }

    // ─── Header construction (with merged-cell awareness) ────────────────────

    /// <summary>
    /// Builds one header per column in [firstCol..lastCol] by concatenating each
    /// header row's value (top-to-bottom). A header cell covered by a horizontal
    /// merge uses the merged range's top-left value for every spanned column.
    /// </summary>
    private static List<string> BuildHeaders(SheetGrid grid, TableRegion region, int firstCol, int lastCol)
    {
        var headers = new List<string>(lastCol - firstCol + 1);

        for (int c = firstCol; c <= lastCol; c++)
        {
            var parts = new List<string>();
            foreach (var headerRow in region.HeaderRows)
            {
                if (headerRow < 0 || headerRow >= grid.RowCount)
                    continue;

                var value = ResolveHeaderCell(grid, headerRow, c);
                if (!string.IsNullOrEmpty(value))
                    parts.Add(value);
            }

            headers.Add(string.Join(HeaderJoin, parts).Trim());
        }

        return headers;
    }

    /// <summary>
    /// Returns the effective header value at (row, col). If the cell is empty but
    /// covered by a horizontal merged range, returns the merged range's top-left
    /// value (so merged parent headers span their child columns).
    /// </summary>
    private static string ResolveHeaderCell(SheetGrid grid, int row, int col)
    {
        var direct = CellAt(grid, row, col);
        if (!string.IsNullOrEmpty(direct))
            return direct;

        // Look for a merged range (1-based coords) covering this 0-based cell.
        int oneBasedRow = row + 1;
        int oneBasedCol = col + 1;

        foreach (var m in grid.MergedRanges)
        {
            if (oneBasedRow >= m.FirstRow && oneBasedRow <= m.LastRow &&
                oneBasedCol >= m.FirstCol && oneBasedCol <= m.LastCol)
            {
                // Use the merged range's top-left value (0-based into Cells).
                return CellAt(grid, m.FirstRow - 1, m.FirstCol - 1);
            }
        }

        return direct; // empty
    }

    // ─── Cell access helpers (ragged-safe) ───────────────────────────────────

    private static List<string> SliceRow(SheetGrid grid, int row, int firstCol, int lastCol)
    {
        var values = new List<string>(lastCol - firstCol + 1);
        for (int c = firstCol; c <= lastCol; c++)
            values.Add(CellAt(grid, row, c));
        return values;
    }

    /// <summary>Reads a cell defensively, returning "" for any out-of-range index.</summary>
    private static string CellAt(SheetGrid grid, int row, int col)
    {
        if (row < 0 || row >= grid.Cells.Count)
            return "";
        var rowCells = grid.Cells[row];
        if (col < 0 || col >= rowCells.Count)
            return "";
        return rowCells[col];
    }
}
