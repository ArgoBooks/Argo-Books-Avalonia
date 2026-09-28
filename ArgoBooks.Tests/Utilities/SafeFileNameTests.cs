using ArgoBooks.Core.Utilities;
using Xunit;

namespace ArgoBooks.Tests.Utilities;

/// <summary>
/// The one rule for turning text into a file or folder name.
/// </summary>
public class SafeFileNameTests
{
    [Theory]
    [InlineData("T4 2026", "T4-2026")]
    [InlineData("RL-1 2026", "RL-1-2026")]
    [InlineData("Pay stubs 2026-07-03", "Pay-stubs-2026-07-03")]
    public void ReplacingSpaces_TurnsThemIntoDashes(string given, string expected) =>
        Assert.Equal(expected, SafeFileName.Create(given, "export", replaceSpaces: true));

    /// <summary>
    /// The full Windows set is replaced on every platform, so a file made on a Mac or Linux
    /// machine still opens on a PC.
    /// </summary>
    [Fact]
    public void CharactersWindowsRefuses_AreReplacedOnEveryPlatform()
    {
        Assert.Equal("Q1- Revenue-Costs", SafeFileName.Create("Q1: Revenue/Costs", "x"));
        Assert.Equal("a-b-c-d-e-f-g-h-i-j", SafeFileName.Create("a<b>c:d\"e/f\\g|h?i*j", "x"));
        Assert.Equal("tab-here", SafeFileName.Create("tab\there", "x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    [InlineData("..")]
    public void ANameWithNothingUsableLeft_FallsBack(string? given)
    {
        // An empty segment, or "..", would write into the parent folder instead.
        Assert.Equal("export", SafeFileName.Create(given, "export", replaceSpaces: true));
    }

    /// <summary>
    /// A company's file is found again by comparing it with the name worked out from the
    /// company's name, so the result must not change for names already saved: spaces, dashes
    /// and dots at either end are kept exactly.
    /// </summary>
    [Theory]
    [InlineData("Smith/Jones: Books?", "Smith-Jones- Books-")]
    [InlineData(" Acme Inc. ", " Acme Inc. ")]
    [InlineData("-Acme-", "-Acme-")]
    public void WithoutReplacingSpaces_TheNameIsOnlyChangedWhereItMustBe(string given, string expected) =>
        Assert.Equal(expected, SafeFileName.Create(given, "Company"));
}
