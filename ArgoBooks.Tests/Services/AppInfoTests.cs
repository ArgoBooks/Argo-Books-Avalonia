using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the ArgoBooks.Core.Services.AppInfo static class.
/// </summary>
public class AppInfoTests
{
    [Fact]
    public void VersionNumber_MatchesXDotXDotXFormat()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.VersionNumber);
    }

    [Fact]
    public void VersionNumber_IsTheSharedProductVersion()
    {
        // The test assembly gets its version from the same Directory.Build.props, so this fails if
        // AppInfo reports the test host's version or falls back to "1.0.0".
        var shared = typeof(AppInfoTests).Assembly.GetName().Version!;

        Assert.Equal($"{shared.Major}.{shared.Minor}.{shared.Build}", AppInfo.VersionNumber);
        Assert.Equal(shared.ToString(3), AppInfo.AssemblyVersion?.ToString(3));
    }

    [Fact]
    public void Version_IsVPrefixedVersionNumber()
    {
        Assert.Equal($"V.{AppInfo.VersionNumber}", AppInfo.Version);
    }
}
