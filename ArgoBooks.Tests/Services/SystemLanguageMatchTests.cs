using ArgoBooks.Data;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The language a fresh install starts in, worked out from the machine's own language.
/// </summary>
public class SystemLanguageMatchTests
{
    [Theory]
    [InlineData("fr-FR", "French")]
    [InlineData("fr", "French")]
    [InlineData("de-AT", "German")]
    [InlineData("pt-BR", "Portuguese")]
    [InlineData("en-GB", "English")]
    public void ASupportedLanguage_IsMatchedFromTheCultureName(string culture, string expected)
    {
        Assert.Equal(expected, Languages.MatchSystemLanguage(culture));
    }

    // "fil-PH" starts with the Finnish code, so a plain two-letter prefix would offer a
    // Filipino speaker the app in Finnish.
    [Fact]
    public void Filipino_IsNotReadAsFinnish()
    {
        Assert.Equal("Filipino", Languages.MatchSystemLanguage("fil-PH"));
        Assert.Equal("Finnish", Languages.MatchSystemLanguage("fi-FI"));
    }

    // Chinese is two separate translations, told apart by script rather than by language code.
    [Theory]
    [InlineData("zh-Hans-CN", "Chinese (Simplified)")]
    [InlineData("zh-CN", "Chinese (Simplified)")]
    [InlineData("zh-SG", "Chinese (Simplified)")]
    [InlineData("zh-Hant-TW", "Chinese (Traditional)")]
    [InlineData("zh-TW", "Chinese (Traditional)")]
    [InlineData("zh-HK", "Chinese (Traditional)")]
    public void Chinese_PicksTheScriptTheRegionUses(string culture, string expected)
    {
        Assert.Equal(expected, Languages.MatchSystemLanguage(culture));
    }

    // Windows reports Norwegian as Bokmal or Nynorsk; both are the one translation.
    [Theory]
    [InlineData("nb-NO")]
    [InlineData("nn-NO")]
    [InlineData("no")]
    public void Norwegian_IsMatchedFromEitherWrittenForm(string culture)
    {
        Assert.Equal("Norwegian", Languages.MatchSystemLanguage(culture));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("xx-XX")]
    [InlineData("und")]
    public void AnUnsupportedOrMissingCulture_MatchesNothing(string? culture)
    {
        Assert.Null(Languages.MatchSystemLanguage(culture));
    }
}
