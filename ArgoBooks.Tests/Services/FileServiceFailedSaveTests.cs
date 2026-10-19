using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for a save of the .argo file that does not finish. The company file is the only copy of
/// the user's books, so a save that stops part-way must leave the previous file as it was and
/// openable, and must not leave its scratch file behind.
/// </summary>
public class FileServiceFailedSaveTests : IDisposable
{
    private readonly string _dir;
    private readonly List<string> _workingDirectories = [];

    public FileServiceFailedSaveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"argo-failed-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        foreach (var dir in _workingDirectories.Append(_dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static FileService CreateService() =>
        new(new CompressionService(), new FooterService(), new EncryptionService());

    /// <summary>
    /// Saves a company whose only customer is named "Before", then renames that customer to "After"
    /// in the working directory without saving.
    /// </summary>
    private async Task<(FileService Service, string FilePath, string WorkingDirectory)> CompanyWithUnsavedEditAsync()
    {
        var service = CreateService();
        var filePath = Path.Combine(_dir, "Acme.argo");
        await service.CreateCompanyAsync(filePath, "Acme");
        var working = await service.OpenCompanyAsync(filePath);
        _workingDirectories.Add(working);

        await service.WriteJsonAsync(working, "customers.json",
            new List<Customer> { new() { Id = "CUS-1", Name = "Before" } });
        await service.SaveCompanyAsync(filePath, working);

        await service.WriteJsonAsync(working, "customers.json",
            new List<Customer> { new() { Id = "CUS-1", Name = "After" } });
        return (service, filePath, working);
    }

    private async Task<string> CustomerNameInFileAsync(FileService service, string filePath)
    {
        var opened = await service.OpenCompanyAsync(filePath);
        _workingDirectories.Add(opened);
        var data = await service.LoadCompanyDataAsync(opened);
        return Assert.Single(data.Customers).Name;
    }

    #region FileService.SaveCompanyAsync Tests

    /// <summary>
    /// A save cancelled before it completes must not change the company file at all. The file still
    /// opens with the data of the last finished save, and no scratch file is left next to it.
    /// </summary>
    [Fact]
    public async Task SaveCompanyAsync_Cancelled_KeepsPreviousFileAndLeavesNoTempFile()
    {
        var (service, filePath, working) = await CompanyWithUnsavedEditAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SaveCompanyAsync(filePath, working, cancellationToken: cts.Token));

        Assert.Equal("Acme.argo", Path.GetFileName(Assert.Single(Directory.GetFiles(_dir))));
        Assert.Equal("Before", await CustomerNameInFileAsync(service, filePath));
    }

    /// <summary>
    /// When the new file is written in full but cannot be swapped into place, because another
    /// program such as a sync client or backup tool holds the company file, the save must fail
    /// with the previous file intact, delete the written scratch file, and let the next save
    /// succeed once the other program lets go.
    /// </summary>
    [Fact]
    public async Task SaveCompanyAsync_FileCannotBeReplaced_KeepsPreviousFileAndRemovesTempFile()
    {
        // Unix lets a rename replace a file another program has open, so only Windows can refuse the swap.
        if (!OperatingSystem.IsWindows())
            return;

        var (service, filePath, working) = await CompanyWithUnsavedEditAsync();

        Exception? failure;
        // Without delete sharing, Windows cannot replace the file while this handle is open.
        await using (new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            failure = await Record.ExceptionAsync(() => service.SaveCompanyAsync(filePath, working));
        }

        Assert.True(failure is IOException or UnauthorizedAccessException, $"Unexpected outcome: {failure}");
        Assert.Equal("Acme.argo", Path.GetFileName(Assert.Single(Directory.GetFiles(_dir))));
        Assert.Equal("Before", await CustomerNameInFileAsync(service, filePath));

        await service.SaveCompanyAsync(filePath, working);
        Assert.Equal("After", await CustomerNameInFileAsync(service, filePath));
        Assert.Single(Directory.GetFiles(_dir));
    }

    #endregion

    #region AtomicFile Tests

    /// <summary>
    /// When the destination stays locked through every retry, the replace must give up with an
    /// error and leave the destination's old content whole. Falling back to copying over it would
    /// truncate the company file first, and an interrupted copy would leave neither version.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_DestinationLockedThroughout_ThrowsAndKeepsOldContent()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var temp = Path.Combine(_dir, "locked.tmp");
        var final = Path.Combine(_dir, "locked.argo");
        await File.WriteAllTextAsync(final, "old");
        await File.WriteAllTextAsync(temp, "new");

        Exception? failure;
        await using (new FileStream(final, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            failure = await Record.ExceptionAsync(() => AtomicFile.ReplaceAsync(temp, final));
        }

        Assert.True(failure is IOException or UnauthorizedAccessException, $"Unexpected outcome: {failure}");
        Assert.Equal("old", await File.ReadAllTextAsync(final));
    }

    #endregion
}
