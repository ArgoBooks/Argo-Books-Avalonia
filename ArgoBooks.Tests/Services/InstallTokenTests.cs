using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for InstallToken, which reads the referral token the website adds to a downloaded installer's name.
/// </summary>
public class InstallTokenTests
{
    [Theory]
    [InlineData(@"C:\Users\me\Downloads\ArgoBooks-2.0.21-win-Setup_abcdef12.exe", "abcdef12")]
    [InlineData(@"C:\Users\me\Downloads\ArgoBooks-2.0.21-win-Setup_ABCDEF12.exe", "abcdef12")]
    [InlineData("/home/me/ArgoBooks-2.0.21-linux-x64_0a1b2c3d.AppImage", "0a1b2c3d")]
    [InlineData("/tmp/download_0a1b2c3d/ArgoBooks.AppImage", "0a1b2c3d")]
    [InlineData(@"C:\Users\me\Downloads\ArgoBooks-2.0.21-win-Setup_abcdef12 (1).exe", "abcdef12")]
    [InlineData("/home/me/ArgoBooks-2.0.21-linux-x64_0a1b2c3d(2).AppImage", "0a1b2c3d")]
    public void FromPath_ReadsTheTokenTheWebsiteAdded(string path, string expected)
    {
        Assert.Equal(expected, InstallToken.FromPath(path));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\ArgoBooks-2.0.21-win-Setup.exe")]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\ArgoBooks\Update\ArgoBooksApp-win-Setup.exe")]
    [InlineData(@"C:\Users\me\Downloads\ArgoBooks-2.0.21-win-Setup_xyz.exe")]
    public void FromPath_IsNullWithoutAToken(string path)
    {
        Assert.Null(InstallToken.FromPath(path));
    }

    [Theory]
    [InlineData("abcdef12", true)]
    [InlineData("ABCDEF12", true)]
    [InlineData("abcdef1", false)]
    [InlineData("abcdefgh", false)]
    public void IsValid_AcceptsOnlyEightHexCharacters(string token, bool expected)
    {
        Assert.Equal(expected, InstallToken.IsValid(token));
    }
}
