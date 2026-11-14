using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for <see cref="AtomicFile"/>. These cover the deterministic semantics:
/// a successful rename, overwrite, and the no-retry path when the temp source is
/// missing. The transient-lock retry path is not unit-tested here because forcing
/// a real, time-bounded file lock cross-platform is inherently flaky.
/// </summary>
public class AtomicFileTests : IDisposable
{
    private readonly string _dir;

    public AtomicFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"AtomicFileTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore cleanup errors */ }
    }

    [Fact]
    public async Task ReplaceAsync_MovesTempOntoFinal()
    {
        var temp = Path.Combine(_dir, "a.tmp");
        var final = Path.Combine(_dir, "a.txt");
        await File.WriteAllTextAsync(temp, "hello");

        await AtomicFile.ReplaceAsync(temp, final);

        Assert.True(File.Exists(final));
        Assert.False(File.Exists(temp));
        Assert.Equal("hello", await File.ReadAllTextAsync(final));
    }

    [Fact]
    public async Task ReplaceAsync_OverwritesExistingFinal()
    {
        var temp = Path.Combine(_dir, "b.tmp");
        var final = Path.Combine(_dir, "b.txt");
        await File.WriteAllTextAsync(final, "old");
        await File.WriteAllTextAsync(temp, "new");

        await AtomicFile.ReplaceAsync(temp, final, overwrite: true);

        Assert.Equal("new", await File.ReadAllTextAsync(final));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public async Task ReplaceAsync_WhenTempMissing_ThrowsFileNotFoundWithoutRetrying()
    {
        var temp = Path.Combine(_dir, "missing.tmp");
        var final = Path.Combine(_dir, "c.txt");

        // A missing source can't be recovered by retrying, so it should throw
        // immediately rather than after the backoff loop.
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => AtomicFile.ReplaceAsync(temp, final));
    }
    /// <summary>
    /// A save on Windows moves the old file aside before the new one takes its name, and a folder
    /// watcher sees that as the file being renamed away.
    /// </summary>
    [Fact]
    public async Task AFileMovedAsideBySaving_DoesNotStayGone()
    {
        var final = Path.Combine(_dir, "Slow Sunday Games.argo");
        await File.WriteAllTextAsync(final, "old");
        var movedAway = new TaskCompletionSource<string>();
        using var watcher = new FileSystemWatcher(_dir) { Filter = "*.argo", NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true };
        watcher.Renamed += (_, e) => { if (e.OldFullPath == final) movedAway.TrySetResult(e.OldFullPath); };
        watcher.Deleted += (_, e) => { if (e.FullPath == final) movedAway.TrySetResult(e.FullPath); };

        var temp = AtomicFile.TempPathFor(final);
        await File.WriteAllTextAsync(temp, "new");
        await AtomicFile.ReplaceAsync(temp, final);

        var seen = await Task.WhenAny(movedAway.Task, Task.Delay(2000)) == movedAway.Task;
        Assert.False(seen && await AtomicFile.StaysGoneAsync(final));
    }

    [Fact]
    public async Task ADeletedFile_StaysGone()
    {
        var final = Path.Combine(_dir, "gone.argo");
        await File.WriteAllTextAsync(final, "x");
        File.Delete(final);

        Assert.True(await AtomicFile.StaysGoneAsync(final));
    }
}
