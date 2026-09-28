using ArgoBooks.Core.Models.Common;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for currency formatting and parsing functionality.
/// Since CurrencyService is a static class that depends on App.CompanyManager,
/// we test the underlying CurrencyInfo methods that power the service.
/// </summary>
public class CurrencyServiceTests
{
    #region Format Tests

    [Fact]
    public void Format_PositiveAmount_ReturnsSymbolWithTwoDecimals()
    {
        var usd = CurrencyInfo.GetByCode("USD");

        var result = usd.Format(1234.56m);

        Assert.Equal("$1,234.56", result);
    }

    [Fact]
    public void Format_ZeroAmount_ReturnsSymbolWithZeros()
    {
        var usd = CurrencyInfo.GetByCode("USD");

        var result = usd.Format(0m);

        Assert.Equal("$0.00", result);
    }

    [Fact]
    public void Format_NegativeAmount_ReturnsNegativeFormatted()
    {
        var usd = CurrencyInfo.GetByCode("USD");

        var result = usd.Format(-500.00m);

        Assert.Contains("500.00", result);
    }

    [Fact]
    public void Format_WithIncludeCode_ReturnsCurrencyCodeSuffix()
    {
        var usd = CurrencyInfo.GetByCode("USD");

        var result = usd.Format(100.00m, includeCode: true);

        Assert.Equal("$100.00 USD", result);
    }

    [Fact]
    public void Format_EuroCurrency_ReturnsEuroSymbol()
    {
        var eur = CurrencyInfo.GetByCode("EUR");

        var result = eur.Format(250.75m);

        Assert.StartsWith("€", result);
        Assert.Contains("250.75", result);
    }

    [Theory]
    [InlineData("USD", "$")]
    [InlineData("EUR", "€")]
    [InlineData("GBP", "£")]
    [InlineData("JPY", "¥")]
    [InlineData("CAD", "$")]
    public void Format_VariousCurrencies_StartsWithCorrectSymbol(string code, string expectedSymbol)
    {
        var currency = CurrencyInfo.GetByCode(code);

        var result = currency.Format(100m);

        Assert.StartsWith(expectedSymbol, result);
    }

    #endregion

    #region ParseCurrencyCode Tests

    [Fact]
    public void ParseCodeFromDisplayString_ValidDisplayString_ExtractsCode()
    {
        var result = CurrencyInfo.ParseCodeFromDisplayString("USD - US Dollar ($)");

        Assert.Equal("USD", result);
    }

    [Fact]
    public void ParseCodeFromDisplayString_EuroDisplayString_ExtractsEUR()
    {
        var result = CurrencyInfo.ParseCodeFromDisplayString("EUR - Euro (€)");

        Assert.Equal("EUR", result);
    }

    [Fact]
    public void ParseCodeFromDisplayString_NullOrEmpty_ReturnsUSD()
    {
        var result = CurrencyInfo.ParseCodeFromDisplayString("");

        Assert.Equal("USD", result);
    }

    [Fact]
    public void ParseCodeFromDisplayString_ThreeLetterCode_ReturnsUppercase()
    {
        var result = CurrencyInfo.ParseCodeFromDisplayString("eur");

        Assert.Equal("EUR", result);
    }

    #endregion

    #region GetDisplayString Tests

    [Fact]
    public void GetByCode_USD_ReturnsCorrectDisplayString()
    {
        var info = CurrencyInfo.GetByCode("USD");

        Assert.Equal("USD - US Dollar ($)", info.DisplayString);
    }

    [Fact]
    public void GetByCode_EUR_ReturnsCorrectDisplayString()
    {
        var info = CurrencyInfo.GetByCode("EUR");

        Assert.Equal("EUR - Euro (€)", info.DisplayString);
    }

    [Fact]
    public void GetByCode_UnknownCode_FallsBackToUSD()
    {
        var info = CurrencyInfo.GetByCode("INVALID");

        Assert.Equal("USD", info.Code);
    }

    #endregion

    #region GetSymbol Tests

    [Theory]
    [InlineData("USD", "$")]
    [InlineData("EUR", "€")]
    [InlineData("GBP", "£")]
    [InlineData("JPY", "¥")]
    [InlineData("KRW", "₩")]
    public void GetSymbol_ValidCode_ReturnsCorrectSymbol(string code, string expectedSymbol)
    {
        var result = CurrencyInfo.GetSymbol(code);

        Assert.Equal(expectedSymbol, result);
    }

    [Fact]
    public void GetSymbol_UnknownCode_ReturnsDollarSign()
    {
        var result = CurrencyInfo.GetSymbol("XYZ");

        Assert.Equal("$", result);
    }

    #endregion

    #region MonetaryValue Tests

    [Fact]
    public void MonetaryValue_CreateWithUSD_SetsCorrectValues()
    {
        var value = new MonetaryValue(100m);

        Assert.Equal(100m, value.OriginalAmount);
        Assert.Equal("USD", value.OriginalCurrency);
        Assert.Equal(100m, value.AmountUSD);
    }

    [Fact]
    public void MonetaryValue_CreateWithForeignCurrency_SetsAllFields()
    {
        var date = new DateTime(2025, 1, 15);

        var value = new MonetaryValue(150m, "CAD", 112.50m, date);

        Assert.Equal(150m, value.OriginalAmount);
        Assert.Equal("CAD", value.OriginalCurrency);
        Assert.Equal(112.50m, value.AmountUSD);
        Assert.Equal(date, value.RateDate);
    }

    #endregion

    #region ZeroDecimalCurrency Tests

    [Fact]
    public void Format_JapaneseYen_ReturnsNoDecimalPlaces()
    {
        var jpy = CurrencyInfo.GetByCode("JPY");

        var result = jpy.Format(1000m);

        Assert.Equal("¥1,000", result);
        Assert.Equal(0, jpy.DecimalPlaces);
    }

    [Fact]
    public void Format_KoreanWon_ReturnsNoDecimalPlaces()
    {
        var krw = CurrencyInfo.GetByCode("KRW");

        var result = krw.Format(50000m);

        Assert.Equal("₩50,000", result);
        Assert.Equal(0, krw.DecimalPlaces);
    }

    #endregion
}
