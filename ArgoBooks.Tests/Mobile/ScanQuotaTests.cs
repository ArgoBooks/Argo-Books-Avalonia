using ArgoBooks.Shared.Mobile;
using Xunit;

namespace ArgoBooks.Tests.Mobile;

/// <summary>Unit tests for ScanQuota's pure free-tier scan math.</summary>
public class ScanQuotaTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(3, 7)]
    [InlineData(9, 1)]
    [InlineData(10, 0)]
    [InlineData(15, 0)]
    public void Remaining_ClampsAtZero(int used, int expected)
    {
        Assert.Equal(expected, ScanQuota.Remaining(used));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void IsOverLimit_TrueOnceLimitReached(int used, bool expected)
    {
        Assert.Equal(expected, ScanQuota.IsOverLimit(used));
    }

    [Fact]
    public void MonthKey_FormatsAsYearDashMonth()
    {
        var key = ScanQuota.MonthKey(new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("2026-07", key);
    }

    [Fact]
    public void MonthKey_DifferentCalendarMonths_ProduceDifferentKeys()
    {
        var june = ScanQuota.MonthKey(new DateTime(2026, 6, 30, 23, 59, 0, DateTimeKind.Utc));
        var july = ScanQuota.MonthKey(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotEqual(june, july);
    }

    [Fact]
    public void MonthKey_SameCalendarMonth_ProducesSameKey()
    {
        var early = ScanQuota.MonthKey(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var late = ScanQuota.MonthKey(new DateTime(2026, 7, 31, 23, 59, 0, DateTimeKind.Utc));

        Assert.Equal(early, late);
    }
}
