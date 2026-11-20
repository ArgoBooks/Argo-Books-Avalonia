using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The date a recent company was last saved, as the recent lists show it.
/// </summary>
public class RecentCompanyInfoTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void LastSavedLocal_IsTheStoredUtcTimeOnThisComputersClock(DateTimeKind kind)
    {
        var savedUtc = new DateTime(2026, 11, 15, 3, 30, 0, kind);

        var info = new RecentCompanyInfo { ModifiedAt = savedUtc };

        Assert.Equal(DateTime.SpecifyKind(savedUtc, DateTimeKind.Utc).ToLocalTime(), info.LastSavedLocal);
        Assert.Equal(DateTimeKind.Local, info.LastSavedLocal.Kind);
    }
}
