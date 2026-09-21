using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the pure country/currency matching logic. The dialog-showing method
/// (ConfirmIfMismatchAsync) depends on App UI state and is not covered here.
/// </summary>
public class CurrencyCountryMatcherTests
{
    #region GetExpectedCurrency

    [Theory]
    [InlineData("Canada", "CAD")]
    [InlineData("United States", "USD")]
    [InlineData("France", "EUR")]
    [InlineData("Germany", "EUR")]
    [InlineData("Japan", "JPY")]
    [InlineData("Australia", "AUD")]
    [InlineData("India", "INR")]
    [InlineData("Mexico", "MXN")]
    [InlineData("Sri Lanka", "LKR")]
    public void GetExpectedCurrency_KnownCountry_ReturnsSupportedCurrency(string country, string expected)
    {
        Assert.Equal(expected, CurrencyCountryMatcher.GetExpectedCurrency(country));
    }

    [Theory]
    [InlineData("USA", "USD")]
    [InlineData("UK", "GBP")]
    public void GetExpectedCurrency_CountryAlias_IsNormalized(string alias, string expected)
    {
        Assert.Equal(expected, CurrencyCountryMatcher.GetExpectedCurrency(alias));
    }

    [Theory]
    [InlineData("Kuwait")]  // KWD not supported: 3 decimal places
    [InlineData("Oman")]    // OMR not supported: 3 decimal places
    public void GetExpectedCurrency_UnsupportedCurrencyCountry_ReturnsNull(string country)
    {
        Assert.Null(CurrencyCountryMatcher.GetExpectedCurrency(country));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Atlantis")]
    public void GetExpectedCurrency_UnknownOrEmpty_ReturnsNull(string? country)
    {
        Assert.Null(CurrencyCountryMatcher.GetExpectedCurrency(country));
    }

    #endregion

    #region IsMismatch

    [Fact]
    public void IsMismatch_CurrencyDiffersFromCountry_ReturnsTrueWithExpected()
    {
        var result = CurrencyCountryMatcher.IsMismatch("Canada", "USD", out var expected);

        Assert.True(result);
        Assert.Equal("CAD", expected);
    }

    [Fact]
    public void IsMismatch_CurrencyMatchesCountry_ReturnsFalse()
    {
        var result = CurrencyCountryMatcher.IsMismatch("Canada", "CAD", out var expected);

        Assert.False(result);
        Assert.Equal("CAD", expected);
    }

    [Fact]
    public void IsMismatch_MatchIsCaseInsensitive()
    {
        var result = CurrencyCountryMatcher.IsMismatch("France", "eur", out _);

        Assert.False(result);
    }

    [Fact]
    public void IsMismatch_EurozoneCountryWithNonEuro_ReturnsTrue()
    {
        var result = CurrencyCountryMatcher.IsMismatch("France", "USD", out var expected);

        Assert.True(result);
        Assert.Equal("EUR", expected);
    }

    [Fact]
    public void IsMismatch_UnsupportedCurrencyCountry_ReturnsFalse()
    {
        // Kuwait's currency (KWD) isn't selectable, so we never warn even with USD.
        var result = CurrencyCountryMatcher.IsMismatch("Kuwait", "USD", out var expected);

        Assert.False(result);
        Assert.Null(expected);
    }

    [Fact]
    public void IsMismatch_NewlySupportedCountryWithUsd_ReturnsTrue()
    {
        // Sri Lanka's rupee is selectable now, so picking USD there is worth a warning. Before
        // LKR existed the only way to file a Sri Lankan company was under another currency.
        var result = CurrencyCountryMatcher.IsMismatch("Sri Lanka", "USD", out var expected);

        Assert.True(result);
        Assert.Equal("LKR", expected);
    }

    [Fact]
    public void IsMismatch_UnknownCountry_ReturnsFalse()
    {
        var result = CurrencyCountryMatcher.IsMismatch("Atlantis", "USD", out var expected);

        Assert.False(result);
        Assert.Null(expected);
    }

    [Fact]
    public void IsMismatch_NoCurrencySelected_ReturnsFalse()
    {
        var result = CurrencyCountryMatcher.IsMismatch("Canada", "", out _);

        Assert.False(result);
    }

    #endregion
}
