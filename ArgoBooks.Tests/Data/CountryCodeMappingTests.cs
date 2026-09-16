using ArgoBooks.Core.Data;
using Xunit;

namespace ArgoBooks.Tests.Data;

public class CountryCodeMappingTests
{
    [Theory]
    [InlineData("Germany", "deu")]
    [InlineData("Ivory Coast", "civ")]
    [InlineData("The Democratic Republic of the Congo", "cod")]
    public void CanonicalNameMapsToAlpha3(string name, string expected) =>
        Assert.Equal(expected, CountryCodeMapping.GetIsoCode(name));

    [Theory]
    [InlineData("U.S.", "usa")]
    [InlineData("England", "gbr")]
    [InlineData("Burma", "mmr")]
    public void AliasMapsToAlpha3(string alias, string expected) =>
        Assert.Equal(expected, CountryCodeMapping.GetIsoCode(alias));

    [Theory]
    [InlineData("CA", "can")]
    [InlineData("ke", "ken")]
    [InlineData("PER", "per")]
    public void IsoCodeMapsToAlpha3(string code, string expected) =>
        Assert.Equal(expected, CountryCodeMapping.GetIsoCode(code));

    [Fact]
    public void UnknownNameFallsBackToLowercasedInput() =>
        Assert.Equal("atlantis", CountryCodeMapping.GetIsoCode("Atlantis"));

    [Fact]
    public void EmptyInputReturnsEmpty() =>
        Assert.Equal(string.Empty, CountryCodeMapping.GetIsoCode(null));

    /// <summary>The world map only shows countries that get a real code, so none may fall through.</summary>
    [Fact]
    public void EveryCanonicalCountryHasAnAlpha3Code()
    {
        var missing = Countries.All
            .Where(c => Countries.GetAlpha3Code(c.Code) is not { Length: 3 })
            .Select(c => c.Name)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void ExportUsesCanonicalNamesAndKeepsUnknownCodes()
    {
        var export = CountryCodeMapping.ConvertGeoMapDataForExport(new Dictionary<string, double>
        {
            ["usa"] = 10,
            ["kor"] = 5,
            ["xyz"] = 2,
            ["deu"] = 0,
        });

        Assert.Equal(10, export["United States"]);
        Assert.Equal(5, export["South Korea"]);
        Assert.Equal(2, export["XYZ"]);
        Assert.DoesNotContain("Germany", export.Keys);
    }
}
