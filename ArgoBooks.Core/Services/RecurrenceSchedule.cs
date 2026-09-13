using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Date arithmetic shared by every recurring schedule, so invoices and transactions cannot drift
/// apart on how a cadence advances.
/// </summary>
public static class RecurrenceSchedule
{
    /// <summary>
    /// Advances a date by one cadence step. For monthly, quarterly and annual cadences an optional
    /// <paramref name="anchorDay"/> (the schedule's original billing day-of-month) keeps the date
    /// pinned to that day: crossing a shorter month clamps to its last day, but the following month
    /// returns to the anchor instead of drifting earlier for the rest of the schedule's life. When
    /// omitted the current date's own day is used as the anchor (single-step, backward-compatible).
    /// </summary>
    public static DateTime AdvanceDate(DateTime date, Frequency frequency, int? anchorDay = null) => frequency switch
    {
        Frequency.Weekly => date.AddDays(7),
        Frequency.BiWeekly => date.AddDays(14),
        Frequency.Monthly => AddMonthsAnchored(date, 1, anchorDay ?? date.Day),
        Frequency.Quarterly => AddMonthsAnchored(date, 3, anchorDay ?? date.Day),
        Frequency.Annually => AddMonthsAnchored(date, 12, anchorDay ?? date.Day),
        _ => AddMonthsAnchored(date, 1, anchorDay ?? date.Day)
    };

    /// <summary>
    /// Steps <paramref name="date"/> forward until it falls on or after <paramref name="floor"/>,
    /// landing on the same dates generation would.
    /// </summary>
    public static DateTime FirstOnOrAfter(DateTime date, Frequency frequency, int anchorDay, DateTime floor)
    {
        var next = date.Date;
        while (next < floor.Date)
            next = AdvanceDate(next, frequency, anchorDay);
        return next;
    }

    private static DateTime AddMonthsAnchored(DateTime date, int months, int anchorDay)
    {
        var shifted = date.AddMonths(months);
        var day = Math.Min(anchorDay, DateTime.DaysInMonth(shifted.Year, shifted.Month));
        return new DateTime(shifted.Year, shifted.Month, day);
    }
}
