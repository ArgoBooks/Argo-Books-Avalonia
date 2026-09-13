using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Anchor-day behaviour is the reason this lives in one place: a monthly schedule that clamps in
/// February must return to its original day afterwards rather than drifting earlier for good.
/// </summary>
public class RecurrenceScheduleTests
{
    [Fact]
    public void AdvanceDate_MonthlyOnThe31st_ClampsThenReturnsToTheAnchor()
    {
        var jan = new DateTime(2026, 1, 31);

        var feb = RecurrenceSchedule.AdvanceDate(jan, Frequency.Monthly, anchorDay: 31);
        var mar = RecurrenceSchedule.AdvanceDate(feb, Frequency.Monthly, anchorDay: 31);

        Assert.Equal(new DateTime(2026, 2, 28), feb);
        Assert.Equal(new DateTime(2026, 3, 31), mar);
    }

    [Fact]
    public void AdvanceDate_WithoutAnAnchor_DriftsFromTheCurrentDay()
    {
        var feb = RecurrenceSchedule.AdvanceDate(new DateTime(2026, 1, 31), Frequency.Monthly);

        Assert.Equal(new DateTime(2026, 2, 28), feb);
        Assert.Equal(new DateTime(2026, 3, 28), RecurrenceSchedule.AdvanceDate(feb, Frequency.Monthly));
    }

    [Theory]
    [InlineData(Frequency.Weekly, 7)]
    [InlineData(Frequency.BiWeekly, 14)]
    public void AdvanceDate_ShortCadences_AddDays(Frequency frequency, int days)
    {
        var start = new DateTime(2026, 3, 4);

        Assert.Equal(start.AddDays(days), RecurrenceSchedule.AdvanceDate(start, frequency));
    }

    [Fact]
    public void AdvanceDate_Quarterly_MovesThreeMonthsKeepingTheAnchor()
    {
        var result = RecurrenceSchedule.AdvanceDate(new DateTime(2026, 1, 31), Frequency.Quarterly, anchorDay: 31);

        Assert.Equal(new DateTime(2026, 4, 30), result);
    }

    [Fact]
    public void AdvanceDate_AnnuallyFromALeapDay_ReturnsToItInTheNextLeapYear()
    {
        var date = new DateTime(2028, 2, 29);

        var next = RecurrenceSchedule.AdvanceDate(date, Frequency.Annually, anchorDay: 29);
        for (var i = 0; i < 3; i++)
            date = RecurrenceSchedule.AdvanceDate(i == 0 ? next : date, Frequency.Annually, anchorDay: 29);

        Assert.Equal(new DateTime(2029, 2, 28), next);
        Assert.Equal(new DateTime(2032, 2, 29), date);
    }

    [Fact]
    public void AdvanceDate_Annually_MovesAYear()
    {
        Assert.Equal(
            new DateTime(2027, 3, 4),
            RecurrenceSchedule.AdvanceDate(new DateTime(2026, 3, 4), Frequency.Annually));
    }
}
