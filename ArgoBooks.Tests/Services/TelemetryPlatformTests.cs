using System.Text.RegularExpressions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The website's telemetry filter drops an OS version that is anything but digits and dots
/// (api/data/telemetry_filter.php), so a version in any other shape would never arrive.
/// </summary>
public class TelemetryPlatformTests
{
    [Fact]
    public void Version_IsInTheShapeTheWebsiteKeeps()
    {
        Assert.Matches(new Regex(@"^\d{1,6}(\.\d{1,6}){1,3}$"), TelemetryPlatform.Version);
    }

    [Fact]
    public void Version_IsThisMachinesVersion()
    {
        var version = Environment.OSVersion.Version;

        Assert.StartsWith($"{version.Major}.{version.Minor}", TelemetryPlatform.Version);
    }
}
