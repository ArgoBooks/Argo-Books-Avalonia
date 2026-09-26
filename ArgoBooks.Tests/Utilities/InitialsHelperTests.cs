using ArgoBooks.Helpers;
using Xunit;

namespace ArgoBooks.Tests.Utilities;

/// <summary>
/// Tests for the InitialsHelper class.
/// </summary>
public class InitialsHelperTests
{
    [Theory]
    [InlineData("John Doe", "JD")]
    [InlineData("John Michael Doe", "JD")]
    [InlineData("John", "JO")]
    [InlineData("J", "J")]
    [InlineData("john doe", "JD")]
    public void From_Name_ReturnsInitials(string name, string expected)
    {
        Assert.Equal(expected, InitialsHelper.From(name));
    }
}
