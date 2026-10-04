using ArgoBooks.Core.Models;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Which copies belong to a company, and when its next one is due. Getting the first wrong prunes
/// another company's copies; getting the second wrong leaves a company with none.
/// </summary>
public class BackupServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"argo-bk-{Guid.NewGuid():N}");
    private readonly BackupService _service = new();

    public BackupServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Copy(string name, DateTime writtenUtc)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    [Fact]
    public void List_LeavesOutACompanyWhoseNameStartsWithThisOnesCopyName()
    {
        Copy("Acme--backup-20260930-143200.argobk", DateTime.UtcNow);
        Copy("Acme--backup-20260930-143200--backup-20261001-090000.argobk", DateTime.UtcNow);

        var copies = _service.List(_folder, "Acme");

        Assert.Equal("Acme--backup-20260930-143200.argobk", Path.GetFileName(Assert.Single(copies).Path));
    }

    [Fact]
    public void IsDue_IsMeasuredFromThisCompanysOwnNewestCopy()
    {
        var settings = new BackupSettings { Enabled = true, Frequency = BackupFrequency.Daily, Folder = _folder };
        var now = DateTime.UtcNow;
        Copy("Acme--backup-20260930-143200.argobk", now.AddHours(-1));

        Assert.False(_service.IsDue(settings, null, "Acme", now));
        // Acme's copy an hour ago does not use up the other company's turn.
        Assert.True(_service.IsDue(settings, null, "Borealis", now));
    }

    [Fact]
    public void CompanyNameOf_DropsTheCopyStamp()
    {
        // Built with Path.Combine rather than written out. CompanyNameOf goes through
        // Path.GetFileNameWithoutExtension, which splits on the running OS's separator, so a
        // literal "C:\x\..." is one long filename on macOS: nothing is stripped and the
        // assertion sees "C:\x\Acme".
        var folder = Path.Combine("x", "y");
        Assert.Equal("Acme", BackupService.CompanyNameOf(Path.Combine(folder, "Acme--backup-20260930-143200.argobk")));
        Assert.Equal("My old books", BackupService.CompanyNameOf(Path.Combine(folder, "My old books.argobk")));
    }
}
