using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The period a date range is compared against for "vs previous period" figures. The dashboard,
/// Analytics and Insights all use it, so the same label never means two different comparisons.
/// Ranges run from the start of their first day to the end of their last.
/// </summary>
public static class ComparisonPeriod
{
    /// <summary>
    /// This month, quarter or year covers only the days so far, so it is compared with the same
    /// days of the period before, stopping at that period's end when it is shorter. Last month,
    /// quarter or year is compared with the whole calendar period before it. Anything else, All
    /// Time included, is compared with the same number of days just before.
    /// </summary>
    public static (DateTime Start, DateTime End) For(DateRangePreset? preset, DateTime start, DateTime end) => preset switch
    {
        DateRangePreset.ThisMonth => SameDaysOf(start.AddMonths(-1), start, end),
        DateRangePreset.ThisQuarter => SameDaysOf(start.AddMonths(-3), start, end),
        DateRangePreset.ThisYear => (start.AddYears(-1), EndOfDay(end.AddYears(-1))),
        DateRangePreset.LastMonth => (start.AddMonths(-1), start.AddTicks(-1)),
        DateRangePreset.LastQuarter => (start.AddMonths(-3), start.AddTicks(-1)),
        DateRangePreset.LastYear => (start.AddYears(-1), start.AddTicks(-1)),
        _ => SameLengthBefore(start, end)
    };

    private static (DateTime Start, DateTime End) SameDaysOf(DateTime previousStart, DateTime start, DateTime end)
    {
        var lastDay = previousStart.AddDays((end.Date - start.Date).Days);
        if (lastDay >= start.Date)
            lastDay = start.Date.AddDays(-1);
        return (previousStart, EndOfDay(lastDay));
    }

    private static DateTime EndOfDay(DateTime day) => day.Date.AddDays(1).AddTicks(-1);

    /// <summary>
    /// The period of the same number of days immediately before [start, end]. It ends a tick before
    /// the range starts, since transactions carry a time of day. MinValue when the range reaches
    /// back too far to have one.
    /// </summary>
    public static (DateTime Start, DateTime End) SameLengthBefore(DateTime start, DateTime end)
    {
        if (start <= DateTime.MinValue)
            return (DateTime.MinValue, DateTime.MinValue);

        var days = (end.Date - start.Date).Days + 1;
        var prevStart = (start.Date - DateTime.MinValue).TotalDays > days
            ? start.Date.AddDays(-days)
            : DateTime.MinValue;
        return (prevStart, start.AddTicks(-1));
    }
}
