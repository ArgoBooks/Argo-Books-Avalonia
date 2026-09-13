using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// This month, quarter or year covers only the days so far.
/// </summary>
public class ComparisonPeriodTests
{
    private static DateTime EndOf(DateTime day) => day.Date.AddDays(1).AddTicks(-1);

    [Fact]
    public void ThisMonth_ComparesAgainstTheSameDaysOfLastMonth()
    {
        var (start, end) = ComparisonPeriod.For(DateRangePreset.ThisMonth, new DateTime(2026, 9, 1), EndOf(new DateTime(2026, 9, 11)));

        Assert.Equal(new DateTime(2026, 8, 1), start);
        Assert.Equal(EndOf(new DateTime(2026, 8, 11)), end);
    }

    [Theory]
    [InlineData(2026, 28)]
    [InlineData(2028, 29)]
    public void ThisMonth_OnTheLastDay_StopsAtTheEndOfAShorterLastMonth(int year, int lastDayOfFebruary)
    {
        var (start, end) = ComparisonPeriod.For(DateRangePreset.ThisMonth, new DateTime(year, 3, 1), EndOf(new DateTime(year, 3, 31)));

        Assert.Equal(new DateTime(year, 2, 1), start);
        Assert.Equal(EndOf(new DateTime(year, 2, lastDayOfFebruary)), end);
    }

    [Theory]
    [InlineData(11, 6, 12)] // 72 days into Q3 is 72 days into Q2
    [InlineData(30, 6, 30)] // Q3 is a day longer than Q2, so it stops at Q2's end
    public void ThisQuarter_ComparesAgainstTheSameNumberOfDaysOfLastQuarter(int septemberDay, int expectedMonth, int expectedDay)
    {
        var (start, end) = ComparisonPeriod.For(DateRangePreset.ThisQuarter, new DateTime(2026, 7, 1), EndOf(new DateTime(2026, 9, septemberDay)));

        Assert.Equal(new DateTime(2026, 4, 1), start);
        Assert.Equal(EndOf(new DateTime(2026, expectedMonth, expectedDay)), end);
    }

    [Fact]
    public void ThisYear_OnFebruary29_ComparesAgainstJanuary1ToFebruary28LastYear()
    {
        var (start, end) = ComparisonPeriod.For(DateRangePreset.ThisYear, new DateTime(2028, 1, 1), EndOf(new DateTime(2028, 2, 29)));

        Assert.Equal(new DateTime(2027, 1, 1), start);
        Assert.Equal(EndOf(new DateTime(2027, 2, 28)), end);
    }

    [Fact]
    public void LastMonth_ComparesAgainstTheWholeMonthBefore()
    {
        var (start, end) = ComparisonPeriod.For(DateRangePreset.LastMonth, new DateTime(2026, 9, 1), EndOf(new DateTime(2026, 9, 30)));

        Assert.Equal(new DateTime(2026, 8, 1), start);
        Assert.Equal(EndOf(new DateTime(2026, 8, 31)), end);
    }

    // Taking end - start as the length and ending at midnight on the day before made the
    // same-length period a day short, so every change leaned upward.
    [Fact]
    public void SameLengthBefore_ThirtyDays_ComparesAgainstTheThirtyDaysBefore()
    {
        var (prevStart, prevEnd) = ComparisonPeriod.SameLengthBefore(new DateTime(2026, 3, 1), EndOf(new DateTime(2026, 3, 30)));

        Assert.Equal(new DateTime(2026, 1, 30), prevStart);
        Assert.Equal(EndOf(new DateTime(2026, 2, 28)), prevEnd);
    }

    [Fact]
    public void SameLengthBefore_OneDay_ComparesAgainstTheWholeDayBefore()
    {
        var (prevStart, prevEnd) = ComparisonPeriod.SameLengthBefore(new DateTime(2026, 3, 1), EndOf(new DateTime(2026, 3, 1)));

        Assert.Equal(new DateTime(2026, 2, 28), prevStart);
        Assert.Equal(EndOf(new DateTime(2026, 2, 28)), prevEnd);
    }
}
