using ArgoBooks.Core.Services;
using SkiaSharp;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Which installed font the charts and printed reports are drawn in.
/// </summary>
public class PlatformTypefacesTests
{
    // On Windows the system default family is Segoe UI itself.
    [Fact]
    public void AFontThatIsTheSystemDefault_StillMatchesWhenAskedForByName()
    {
        Assert.True(PlatformTypefaces.IsResolvedMatch("Segoe UI", "Segoe UI", "Segoe UI"));
    }

    [Fact]
    public void AMissingFont_SubstitutedWithTheSystemDefault_DoesNotMatch()
    {
        Assert.False(PlatformTypefaces.IsResolvedMatch("Helvetica Neue", "Segoe UI", "Segoe UI"));
    }

    // macOS reports its UI font under another name than the alias it is asked for.
    [Fact]
    public void AnAliasResolvingToARealFontOtherThanTheDefault_Matches()
    {
        Assert.True(PlatformTypefaces.IsResolvedMatch(".AppleSystemUIFont", ".SF NS", "Helvetica"));
    }

    [Fact]
    public void OnWindowsWithSegoeUi_TheUiFontIsSegoeUi()
    {
        if (!OperatingSystem.IsWindows()
            || SKTypeface.FromFamilyName("Segoe UI")?.FamilyName != "Segoe UI")
            return;

        Assert.Equal("Segoe UI", PlatformTypefaces.Default.FamilyName);
    }
}
