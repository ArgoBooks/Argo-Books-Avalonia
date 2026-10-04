using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The startup sweep of company folders left in temp. It deletes whole directories, so what
/// matters most is what it leaves alone: a company open in another window.
/// </summary>
public class AbandonedWorkingDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sweep_{Guid.NewGuid():N}");
    private static DateTime Later => DateTime.UtcNow.AddHours(2);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string MakeWorkingDirectory()
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "customers.json"), "[]");
        return directory;
    }

    [Fact]
    public void AnAbandonedDirectory_IsDeleted_AndOneStillHeldIsNot()
    {
        var abandoned = MakeWorkingDirectory();
        var open = MakeWorkingDirectory();
        var previews = Path.Combine(_root, "Receipts");
        Directory.CreateDirectory(previews);

        using (SecureTempDirectory.Hold(open))
        {
            Assert.Equal(1, SecureTempDirectory.DeleteAbandoned(_root, Later));

            Assert.False(Directory.Exists(abandoned));
            Assert.True(File.Exists(Path.Combine(open, "customers.json")));
            Assert.True(Directory.Exists(previews));
        }
    }

    // A company still opening in another window has its directory but not yet its marker.
    [Fact]
    public void ADirectoryJustCreated_IsLeftAlone()
    {
        var opening = MakeWorkingDirectory();

        Assert.Equal(0, SecureTempDirectory.DeleteAbandoned(_root));

        Assert.True(Directory.Exists(opening));
    }
}
