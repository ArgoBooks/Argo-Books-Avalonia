using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class GeoLocationServiceTests
{
    [Theory]
    [InlineData("IT", "Italy")]
    [InlineData("jm", "Jamaica")]
    [InlineData("CA", "Canada")]
    [InlineData(null, "Unknown")]
    [InlineData("", "Unknown")]
    [InlineData("not a code", "Unknown")]
    public void CountryName_NamesTheCountryForItsCode(string? code, string expected)
    {
        Assert.Equal(expected, GeoLocationService.CountryName(code));
    }
}
