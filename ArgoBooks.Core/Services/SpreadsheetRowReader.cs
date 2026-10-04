using System.Globalization;
using ClosedXML.Excel;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Pure, reusable helpers for reading and parsing spreadsheet rows. Extracted from
/// <see cref="SpreadsheetImportService"/> so that other features (e.g. bank statement
/// matching) can parse arbitrary spreadsheet/CSV data into typed values without
/// pulling in the entity-import machinery. Has no dependency on CompanyData.
/// </summary>
internal static class SpreadsheetRowReader
{
    /// <summary>
    /// Finds the header row by scanning for the first row with at least 2 non-empty cells.
    /// Falls back to row 1 if no such row is found within the first 10 rows.
    /// </summary>
    public static int FindHeaderRow(IXLWorksheet worksheet)
    {
        var lastRow = Math.Min(worksheet.LastRowUsed()?.RowNumber() ?? 1, 10);
        var colCount = worksheet.ColumnsUsed().Count();

        for (int rowNum = 1; rowNum <= lastRow; rowNum++)
        {
            var row = worksheet.Row(rowNum);
            int nonEmpty = 0;
            for (int col = 1; col <= colCount; col++)
            {
                if (!row.Cell(col).IsEmpty()) nonEmpty++;
                if (nonEmpty >= 2) return rowNum;
            }
        }

        return 1;
    }

    public static List<string> GetHeaders(IXLWorksheet worksheet)
    {
        return GetHeaders(worksheet, FindHeaderRow(worksheet));
    }

    public static List<string> GetHeaders(IXLWorksheet worksheet, int headerRow)
    {
        var headers = new List<string>();
        var row = worksheet.Row(headerRow);
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (int col = 1; col <= lastColumn; col++)
        {
            var cell = row.Cell(col);
            headers.Add(cell.IsEmpty() ? "" : cell.GetString().Trim());
        }

        return headers;
    }

    public static List<List<object?>> GetDataRows(IXLWorksheet worksheet, int columnCount)
        => GetDataRows(worksheet, columnCount, FindHeaderRow(worksheet));

    public static List<List<object?>> GetDataRows(IXLWorksheet worksheet, int columnCount, int headerRow)
    {
        var rows = new List<List<object?>>();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (int rowNum = headerRow + 1; rowNum <= lastRow; rowNum++)
        {
            var row = worksheet.Row(rowNum);
            var rowData = new List<object?>();
            var isEmpty = true;

            for (int col = 1; col <= columnCount; col++)
            {
                var cell = row.Cell(col);
                if (!cell.IsEmpty()) isEmpty = false;
                rowData.Add(GetCellValue(cell));
            }

            if (!isEmpty)
            {
                rows.Add(rowData);
            }
        }

        return rows;
    }

    public static object? GetCellValue(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;

        return cell.DataType switch
        {
            XLDataType.Number => cell.GetDouble(),
            XLDataType.DateTime => cell.GetDateTime(),
            XLDataType.Boolean => cell.GetBoolean(),
            _ => cell.GetString()
        };
    }

    public static int GetColumnIndex(List<string> headers, string columnName)
    {
        // Case-insensitive column lookup
        for (int i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    public static string GetString(List<object?> row, List<string> headers, string columnName)
    {
        var index = GetColumnIndex(headers, columnName);
        if (index < 0 || index >= row.Count) return string.Empty;
        return row[index]?.ToString() ?? string.Empty;
    }

    public static string? GetNullableString(List<object?> row, List<string> headers, string columnName)
    {
        var value = GetString(row, headers, columnName);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static decimal GetDecimal(List<object?> row, List<string> headers, string columnName)
    {
        var index = GetColumnIndex(headers, columnName);
        if (index < 0 || index >= row.Count) return 0m;

        var value = row[index];
        return value switch
        {
            // A formula-error cell can surface as NaN/Infinity; (decimal)NaN throws, so coerce to 0.
            double d => double.IsFinite(d) ? (decimal)d : 0m,
            decimal dec => dec,
            int i => i,
            long l => l,
            string s => ParseDecimalString(s),
            _ => 0m
        };
    }

    /// <summary>
    /// Returns the decimal value for a column, or null when the column is absent or empty.
    /// Distinguishes "no value" from a genuine 0.
    /// </summary>
    public static decimal? GetNullableDecimal(List<object?> row, List<string> headers, string columnName)
    {
        var index = GetColumnIndex(headers, columnName);
        if (index < 0 || index >= row.Count) return null;

        var value = row[index];
        return value switch
        {
            null => null,
            // A formula-error cell can surface as NaN/Infinity; (decimal)NaN throws, so coerce to 0.
            double d => double.IsFinite(d) ? (decimal)d : 0m,
            decimal dec => dec,
            int i => i,
            long l => l,
            string s when string.IsNullOrWhiteSpace(s) => null,
            string s => ParseDecimalString(s),
            _ => null
        };
    }

    // Amount parsing (currency-symbol/code stripping, parentheses-as-negative, invariant parse)
    // is shared with the currency detector so both interpret amounts identically.
    public static decimal ParseDecimalString(string s) => CurrencyCellDetector.ParseAmount(s);

    public static int GetInt(List<object?> row, List<string> headers, string columnName)
    {
        var index = GetColumnIndex(headers, columnName);
        if (index < 0 || index >= row.Count) return 0;

        var value = row[index];
        return value switch
        {
            double d => double.IsFinite(d) ? (int)d : 0,
            decimal dec => (int)dec,
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var result) => result,
            _ => 0
        };
    }

    public enum DateOrder { Unknown, MonthFirst, DayFirst }

    public static DateTime GetDateTime(List<object?> row, List<string> headers, string columnName)
        => GetDateTime(row, headers, columnName, DateOrder.Unknown);

    /// <summary>
    /// Reads a date cell, parsing text in the given <paramref name="order"/> (from
    /// <see cref="DetectDateOrder"/>) so every row of a column is read the same way.
    /// </summary>
    public static DateTime GetDateTime(List<object?> row, List<string> headers, string columnName, DateOrder order)
    {
        var index = GetColumnIndex(headers, columnName);
        if (index < 0 || index >= row.Count) return DateTime.MinValue;

        var value = row[index];
        return value switch
        {
            DateTime dt => dt,
            double d => FromNumber(d),
            string s => ParseDateString(s, order),
            _ => DateTime.MinValue
        };
    }

    /// <summary>
    /// A date held as a number. Normally Excel's own serial date, but some exports write the date
    /// as the digits 20240315, which is far outside the serial range: FromOADate threw on it and
    /// the whole import stopped part way.
    /// </summary>
    private static DateTime FromNumber(double value)
    {
        if (value is >= 19000101 and <= 22001231 && value == Math.Floor(value)
            && DateTime.TryParseExact(((long)value).ToString(CultureInfo.InvariantCulture), "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDigits))
            return fromDigits;

        // The range FromOADate accepts.
        return value is > -657435 and < 2958466 ? DateTime.FromOADate(value) : DateTime.MinValue;
    }

    /// <summary>
    /// Decides once for a whole column whether its numeric dates are day-first or month-first. A
    /// first field over 12 can only be a day, and so can a second field over 12, so one such value
    /// settles the column. When nothing in the file settles it (every field is 12 or under), the
    /// computer's own regional date order decides, so a British or Canadian statement covering the
    /// 1st to the 12th is not read as twelve different months. Unknown when the column contradicts
    /// itself; those values keep the per-value parse, which reads month-first.
    /// </summary>
    public static DateOrder DetectDateOrder(List<List<object?>> rows, List<string> headers, string columnName)
    {
        var index = GetColumnIndex(headers, columnName);
        if (index < 0) return DateOrder.Unknown;

        var (dayFirst, monthFirst) = OrderEvidence(rows, index);
        if (dayFirst && monthFirst) return DateOrder.Unknown;
        if (dayFirst || monthFirst) return dayFirst ? DateOrder.DayFirst : DateOrder.MonthFirst;

        // Nothing in this column settles it, so look at the sheet's other date columns before the
        // computer's region. One file is written one way: an Issue Date column of 01/05 beside a
        // Due Date column holding a 25 is month-first in both.
        bool anyDayFirst = false, anyMonthFirst = false;
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        for (var column = 0; column < width; column++)
        {
            if (column == index) continue;
            var (d, m) = OrderEvidence(rows, column);
            anyDayFirst |= d;
            anyMonthFirst |= m;
        }

        if (anyDayFirst != anyMonthFirst) return anyDayFirst ? DateOrder.DayFirst : DateOrder.MonthFirst;
        return RegionIsDayFirst() ? DateOrder.DayFirst : DateOrder.Unknown;
    }

    /// <summary>What one column's written dates prove about their order, if anything.</summary>
    private static (bool DayFirst, bool MonthFirst) OrderEvidence(List<List<object?>> rows, int index)
    {
        bool dayFirst = false, monthFirst = false;
        foreach (var row in rows)
        {
            if (index >= row.Count || row[index] is not string s) continue;

            // A year-first date (2024-03-05) is never ambiguous, so only 1-2 digit leading fields
            // count. The third field has to look like a year, so text such as "13-5-A" is not
            // taken for a date.
            var parts = s.Trim().Split('/', '.', '-');
            if (parts.Length < 3 || parts[0].Length > 2 ||
                !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var second) ||
                parts[2].Length < 2 || !char.IsDigit(parts[2][0]) || !char.IsDigit(parts[2][1]))
                continue;

            if (first > 12) dayFirst = true;
            if (second > 12) monthFirst = true;
        }

        return (dayFirst, monthFirst);
    }

    private static bool RegionIsDayFirst()
    {
        var pattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
        var day = pattern.IndexOf('d');
        var month = pattern.IndexOf('M');
        return day >= 0 && month >= 0 && day < month;
    }

    // Day-first formats, e.g. UK/EU "15/03/2023" or "15.03.2023". Unless the column is known to be
    // day-first they're tried ONLY after the invariant (month-first) parse fails, so a date that
    // already parses month-first (like "03/01/2023" -> March 1) is never reinterpreted as day-first.
    private static readonly string[] DayFirstDateFormats =
        ["d/M/yyyy", "dd/MM/yyyy", "d.M.yyyy", "dd.MM.yyyy", "d-M-yyyy", "dd-MM-yyyy"];

    // Invariant with a day-first short date pattern, which is what orders an ambiguous value with
    // a time ("05/03/2024 10:30") in the lenient parse. Cloned from invariant so it doesn't depend
    // on the cultures installed on the machine.
    private static readonly CultureInfo DayFirstCulture = CreateDayFirstCulture();

    private static CultureInfo CreateDayFirstCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        return culture;
    }

    private static DateTime ParseDateString(string s, DateOrder order)
    {
        if (order == DateOrder.DayFirst)
        {
            if (DateTime.TryParseExact(s.Trim(), DayFirstDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                return exact;
            if (DateTime.TryParse(s, DayFirstCulture, DateTimeStyles.None, out var lenient))
                return lenient;
            // Not readable day-first at all, such as "12/25" with no year. Fall through to the
            // ordinary parse rather than lose the date.
        }

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            return result;
        if (DateTime.TryParseExact(s, DayFirstDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dayFirst))
            return dayFirst;
        return DateTime.MinValue;
    }

    public static DateTime? GetNullableDateTime(List<object?> row, List<string> headers, string columnName)
        => GetNullableDateTime(row, headers, columnName, DateOrder.Unknown);

    public static DateTime? GetNullableDateTime(List<object?> row, List<string> headers, string columnName, DateOrder order)
    {
        var dt = GetDateTime(row, headers, columnName, order);
        return dt == DateTime.MinValue ? null : dt;
    }
}
