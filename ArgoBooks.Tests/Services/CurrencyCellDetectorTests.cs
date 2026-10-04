using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for <see cref="CurrencyCellDetector"/>: reading the amount and any currency marker
/// (explicit code, unambiguous symbol, or ambiguous symbol) from a raw cell string.
/// </summary>
public class CurrencyCellDetectorTests
{
    // ─── Explicit ISO code wins (never ambiguous) ────────────────────────────

    [Theory]
    [InlineData("$10 CAD", "CAD", 10)]
    [InlineData("10 CAD", "CAD", 10)]
    [InlineData("USD 10", "USD", 10)]
    [InlineData("10 GBP", "GBP", 10)]
    [InlineData("$10CAD", "CAD", 10)]      // no space
    public void Detect_ExplicitCode_Resolves(string cell, string expectedCode, double amount)
    {
        var d = CurrencyCellDetector.Detect(cell);

        Assert.Equal(expectedCode, d.Code);
        Assert.Null(d.AmbiguousSymbol);
        Assert.Equal((decimal)amount, d.Amount);
    }

    // ─── Unambiguous symbols ─────────────────────────────────────────────────

    [Theory]
    [InlineData("£10", "GBP", 10)]
    [InlineData("€50", "EUR", 50)]
    [InlineData("₹100", "INR", 100)]
    public void Detect_UnambiguousSymbol_Resolves(string cell, string expectedCode, double amount)
    {
        var d = CurrencyCellDetector.Detect(cell);

        Assert.Equal(expectedCode, d.Code);
        Assert.Null(d.AmbiguousSymbol);
        Assert.Equal((decimal)amount, d.Amount);
    }

    // ─── Ambiguous symbols ───────────────────────────────────────────────────

    [Fact]
    public void Detect_DollarSign_IsAmbiguous_WithDollarCandidates()
    {
        var d = CurrencyCellDetector.Detect("$10");

        Assert.Null(d.Code);
        Assert.Equal("$", d.AmbiguousSymbol);
        Assert.Equal(10m, d.Amount);
        Assert.Contains("USD", d.Candidates);
        Assert.Contains("CAD", d.Candidates);
        Assert.Contains("AUD", d.Candidates);
        Assert.Equal("USD", d.Candidates[0]); // priority-ordered default
    }

    [Fact]
    public void Detect_Yen_IsAmbiguous_JpyAndCny()
    {
        var d = CurrencyCellDetector.Detect("¥1000");

        Assert.Null(d.Code);
        Assert.Equal("¥", d.AmbiguousSymbol);
        Assert.Equal(2, d.Candidates.Count);
        Assert.Contains("JPY", d.Candidates);
        Assert.Contains("CNY", d.Candidates);
    }

    [Fact]
    public void Detect_Kr_IsAmbiguous_FourNordicCodes()
    {
        var d = CurrencyCellDetector.Detect("kr 50");

        Assert.Equal("kr", d.AmbiguousSymbol);
        Assert.Equal(4, d.Candidates.Count); // DKK, ISK, NOK, SEK
        Assert.Equal(50m, d.Amount);
    }

    // ─── Longest-symbol match (NT$ before $) ─────────────────────────────────

    [Fact]
    public void Detect_NtDollar_ResolvesToTwd_NotAmbiguousDollar()
    {
        var d = CurrencyCellDetector.Detect("NT$5");

        Assert.Equal("TWD", d.Code);
        Assert.Null(d.AmbiguousSymbol);
        Assert.Equal(5m, d.Amount);
    }

    // ─── No currency marker ──────────────────────────────────────────────────

    [Theory]
    [InlineData("10", 10)]
    [InlineData("1,234.56", 1234.56)]
    public void Detect_PlainNumber_NoCurrency(string cell, double amount)
    {
        var d = CurrencyCellDetector.Detect(cell);

        Assert.Null(d.Code);
        Assert.Null(d.AmbiguousSymbol);
        Assert.Equal((decimal)amount, d.Amount);
    }

    [Fact]
    public void Detect_Parentheses_AreNegative()
    {
        var d = CurrencyCellDetector.Detect("(123.45)");

        Assert.Equal(-123.45m, d.Amount);
        Assert.Null(d.Code);
    }

    [Fact]
    public void Detect_NullOrEmpty_ReturnsZeroNoCurrency()
    {
        Assert.Equal(0m, CurrencyCellDetector.Detect(null).Amount);
        Assert.Equal(0m, CurrencyCellDetector.Detect("").Amount);
        Assert.Null(CurrencyCellDetector.Detect("   ").Code);
    }

    // ─── ParseAmount parity (the shared number parser) ───────────────────────

    [Theory]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("£10", 10)]
    [InlineData("(99.00)", -99)]
    [InlineData("USD 10", 10)]
    [InlineData("abc", 0)]
    public void ParseAmount_MatchesExpected(string cell, double expected)
    {
        Assert.Equal((decimal)expected, CurrencyCellDetector.ParseAmount(cell));
    }

    // A sign written before the currency symbol came in as 0, so the expense or refund imported
    // with no amount and no warning.
    [Theory]
    [InlineData("-$45.00", -45.00)]
    [InlineData("+$45.00", 45.00)]
    [InlineData("-£45.00", -45.00)]
    [InlineData("- $1,250.50", -1250.50)]
    [InlineData("-45.00", -45.00)]
    [InlineData("($45.00)", -45.00)]
    public void ParseAmount_ReadsASignBeforeTheSymbol(string text, double expected)
    {
        Assert.Equal((decimal)expected, CurrencyCellDetector.ParseAmount(text));
    }
}
