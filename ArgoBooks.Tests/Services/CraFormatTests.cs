using ArgoBooks.Core.Services.Payroll;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the text rules CRA's XML parser enforces.
///
/// Every case here is one CRA would have rejected, which is a
/// failure that surfaces months later at the filing deadline rather than at the keyboard. The
/// postal code one is the most ordinary of them: a space is how almost every Canadian writes it,
/// and CRA's format has no room for one.
/// </summary>
public class CraFormatTests
{
    [Theory]
    [InlineData("K1A 0B1", "K1A0B1")]
    [InlineData("k1a0b1", "K1A0B1")]
    [InlineData("K1A-0B1", "K1A0B1")]
    [InlineData("  K1A 0B1  ", "K1A0B1")]
    public void CanadianPostalCodeLosesItsSeparators(string typed, string expected) =>
        Assert.Equal(expected, CraFormat.NormalizePostalCode(typed, "Canada"));

    [Theory]
    [InlineData("K1A 0B1")]
    [InlineData("K1A0B1")]
    public void CanadianPostalCodeIsAcceptedWithOrWithoutTheSpace(string typed) =>
        Assert.True(CraFormat.IsPostalCode(typed, "Canada"));

    [Theory]
    [InlineData("K1A0B")]
    [InlineData("KKA0B1")]
    [InlineData("12345")]
    [InlineData("K1A 0B1 2")]
    public void MalformedCanadianPostalCodeIsRejected(string typed) =>
        Assert.False(CraFormat.IsPostalCode(typed, "Canada"));

    [Theory]
    [InlineData("902101234", "90210-1234")]
    [InlineData("90210", "90210")]
    public void UsZipKeepsItsFiveAndFourShape(string typed, string expected) =>
        Assert.Equal(expected, CraFormat.NormalizePostalCode(typed, "United States"));

    /// <summary>
    /// A wrong code is rejected while an absent one is not.
    /// </summary>
    [Theory]
    [InlineData("Canada", "CAN")]
    [InlineData("CA", "CAN")]
    [InlineData("CAN", "CAN")]
    [InlineData("United States", "USA")]
    [InlineData("US", "USA")]
    [InlineData("Germany", "DEU")]
    [InlineData("Japan", "JPN")]
    [InlineData("United Kingdom", "GBR")]
    public void CountryBecomesItsIsoAlpha3Code(string typed, string expected) =>
        Assert.Equal(expected, CraFormat.Alpha3Country(typed));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nowhere At All")]
    public void UnrecognisedCountryIsOmittedRatherThanGuessed(string typed) =>
        Assert.Null(CraFormat.Alpha3Country(typed));

    /// <summary>
    /// The informal names the chart mapping already carried. That lookup returns the input
    /// lowercased when it does not recognise a name, so the one thing this must not do is let
    /// "germany" through as a country code.
    /// </summary>
    [Theory]
    [InlineData("America", "USA")]
    [InlineData("England", "GBR")]
    [InlineData("Korea", "KOR")]
    public void InformalCountryNamesStillResolve(string typed, string expected) =>
        Assert.Equal(expected, CraFormat.Alpha3Country(typed));

    [Theory]
    [InlineData("ON")]
    [InlineData("qc")]
    [InlineData(" BC ")]
    public void ProvinceCodeIsAccepted(string typed) => Assert.True(CraFormat.IsProvinceCode(typed));

    [Theory]
    [InlineData("XX")]
    [InlineData("QU")]
    [InlineData("O")]
    [InlineData("Ontario")]
    public void NonProvinceCodeIsRejected(string typed) => Assert.False(CraFormat.IsProvinceCode(typed));

    /// <summary>
    /// The app stores one name field and splits it on spaces, so "Smith, John" becomes a given
    /// name carrying a comma. CRA's name character set has no comma in it.
    /// </summary>
    [Fact]
    public void CommaInANameIsReported()
    {
        Assert.Equal(",", CraFormat.DisallowedCharacters("Smith, John"));
        Assert.Equal("Smith John", CraFormat.CleanName("Smith, John"));
    }

    [Theory]
    [InlineData("O'Brien")]
    [InlineData("Marie-Claire Tremblay")]
    [InlineData("Jean Lefebvre")]
    [InlineData("Smith & Sons")]
    [InlineData("R2 D2")]
    public void AcceptableNamesAreLeftAlone(string name)
    {
        Assert.Empty(CraFormat.DisallowedCharacters(name));
        Assert.Equal(name, CraFormat.CleanName(name));
    }

    /// <summary>Word and Windows both substitute this the moment anyone types an apostrophe.</summary>
    [Fact]
    public void CurlyApostropheIsFoldedToTheOneCraLists()
    {
        Assert.Empty(CraFormat.DisallowedCharacters("O’Brien"));
        Assert.Equal("O'Brien", CraFormat.CleanName("O’Brien"));
    }

    [Fact]
    public void AddressesMayCarryASlashAndANumberSignAndNamesMayNot()
    {
        Assert.Empty(CraFormat.DisallowedCharacters("#3 12/14 King Street", address: true));
        Assert.Equal("#/", CraFormat.DisallowedCharacters("#3 12/14 King Street"));
    }

    /// <summary>Removing a character must not leave the double space behind that it was between.</summary>
    [Fact]
    public void CleaningCollapsesTheGapItLeaves() =>
        Assert.Equal("Anne Marie Roy", CraFormat.CleanName("Anne (Marie) Roy"));
}
