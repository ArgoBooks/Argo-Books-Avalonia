using ArgoBooks.Core.Models;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Which copies belong to a company. Getting it wrong prunes another company's copies.
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
    public void CompanyNameOf_DropsTheCopyStamp()
    {
        Assert.Equal("Acme", BackupService.CompanyNameOf(@"C:\x\Acme--backup-20260930-143200.argobk"));
        Assert.Equal("My old books", BackupService.CompanyNameOf(@"C:\x\My old books.argobk"));
    }
}
