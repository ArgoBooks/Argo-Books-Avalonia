using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for UpdateOutcome, which reports on the next launch whether an accepted update installed.
/// </summary>
public class UpdateOutcomeTests
{
    [Theory]
    [InlineData("2.0.20", "2.0.20", true)]
    [InlineData("2.0.21", "2.0.20", true)]
    [InlineData("2.0.19", "2.0.20", false)]
    [InlineData("2.0.9", "2.0.10", false)]
    public void IsInstalled_ComparesVersionsByNumber(string running, string target, bool expected)
    {
        Assert.Equal(expected, UpdateOutcome.IsInstalled(running, target));
    }

    [Fact]
    public void Context_NamesBothVersionsAndTheDetail()
    {
        Assert.Equal("2.0.19>2.0.20", UpdateOutcome.Context("2.0.19", "2.0.20"));
        Assert.Equal("2.0.19>2.0.20 update did not start", UpdateOutcome.Context("2.0.19", "2.0.20", "update did not start"));
    }

    [Fact]
    public void Context_FitsWhatTheWebsiteKeeps()
    {
        var context = UpdateOutcome.Context("2.0.19", "2.0.20", new string('x', 200));

        Assert.Equal(64, context.Length);
        Assert.StartsWith("2.0.19>2.0.20 ", context);
    }
}
