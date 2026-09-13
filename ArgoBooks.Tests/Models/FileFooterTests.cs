using System.Text;
using ArgoBooks.Core.Models;
using Xunit;

namespace ArgoBooks.Tests.Models;

/// <summary>
/// Tests for the FileFooter model and FileFormatConstants.
/// </summary>
public class FileFooterTests
{
    #region Accountants List Tests

    [Fact]
    public void FileFooter_Accountants_CanBePopulated()
    {
        var footer = new FileFooter();
        footer.Accountants.Add("John Doe");
        footer.Accountants.Add("Jane Smith");

        Assert.Equal(2, footer.Accountants.Count);
        Assert.Contains("John Doe", footer.Accountants);
    }

    #endregion

    #region FileFormatConstants Tests

    [Fact]
    public void FileFormatConstants_MagicBytes_EqualsArgoBytes()
    {
        var expected = Encoding.ASCII.GetBytes("ARGO");

        Assert.Equal(expected, FileFormatConstants.MagicBytes);
    }

    [Fact]
    public void FileFormatConstants_FormatVersion_IsThree()
    {
        // Version 2 introduced envelope encryption. Bumping this is a breaking change for
        // older builds, which cannot read the newer envelope, so it should not move without
        // a matching change to how files are written.
        Assert.Equal(3, FileFormatConstants.FormatVersion);
    }

    [Fact]
    public void FileFormatConstants_StillReadsTheOriginalFormat()
    {
        Assert.Equal(1, FileFormatConstants.MinimumSupportedFormatVersion);
    }

    [Fact]
    public void FileFormatConstants_CompanyFileExtension_IsArgo()
    {
        Assert.Equal(".argo", FileFormatConstants.CompanyFileExtension);
    }

    [Fact]
    public void FileFormatConstants_BackupFileExtension_IsArgobk()
    {
        Assert.Equal(".argobk", FileFormatConstants.BackupFileExtension);
    }

    #endregion
}
