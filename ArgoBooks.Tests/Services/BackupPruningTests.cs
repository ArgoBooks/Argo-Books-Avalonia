using ArgoBooks.Core.Models;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Which older copies a new backup clears away. Deleting the wrong file removes a copy someone was
/// counting on, so a prune may only remove this company's own oldest copies, and only once the new
/// one is written.
/// </summary>
public class BackupPruningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"argo-bkprune-{Guid.NewGuid():N}");
    private readonly string _backups;
    private readonly string _companyPath;
    private readonly CompanyManager _manager;
    private readonly BackupService _service = new();

    public BackupPruningTests()
    {
        _backups = Path.Combine(_root, "Backups");
        _companyPath = Path.Combine(_root, "Acme.argo");
        Directory.CreateDirectory(_backups);

        var footerService = new FooterService();
        _manager = new CompanyManager(
            new FileService(new CompressionService(), footerService),
            new GlobalSettingsService(new TempPlatform(Path.Combine(_root, "appdata"))),
            footerService);
    }

    public void Dispose()
    {
        _manager.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private BackupSettings Settings(int keep) =>
        new() { Enabled = true, Frequency = BackupFrequency.OnSave, CopiesToKeep = keep, Folder = _backups };

    private string SeedCopy(string fileName, DateTime writtenUtc)
    {
        var path = Path.Combine(_backups, fileName);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    private string[] FileNamesInFolder() =>
        [.. Directory.GetFiles(_backups).Select(p => Path.GetFileName(p)!).Order(StringComparer.Ordinal)];

    /// <summary>
    /// With more copies than the user chose to keep, the newest ones stay, counting the copy just
    /// written, and only the older ones beyond that number are deleted.
    /// </summary>
    [Fact]
    public async Task CreateAsync_MoreCopiesThanKept_DeletesOnlyTheOldest()
    {
        var now = DateTime.UtcNow;
        SeedCopy("Acme--backup-20200104-000000.argobk", now.AddDays(-1));
        SeedCopy("Acme--backup-20200103-000000.argobk", now.AddDays(-2));
        SeedCopy("Acme--backup-20200102-000000.argobk", now.AddDays(-3));
        SeedCopy("Acme--backup-20200101-000000.argobk", now.AddDays(-4));
        try
        {
            await _manager.CreateCompanyAsync(_companyPath, "Acme");

            var written = await _service.CreateAsync(_manager, Settings(keep: 3));

            Assert.NotNull(written);
            string[] expected =
            [
                Path.GetFileName(written),
                "Acme--backup-20200103-000000.argobk",
                "Acme--backup-20200104-000000.argobk"
            ];
            Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), FileNamesInFolder());
        }
        finally
        {
            await _manager.CloseCompanyAsync();
        }
    }

    /// <summary>
    /// Which copy is oldest comes from when it was written, not from how its name sorts or the
    /// order the folder lists it in. Here the names sort the opposite way to the write times, so
    /// a prune going by name would keep the copy that is actually the oldest.
    /// </summary>
    [Fact]
    public async Task CreateAsync_NamesSortAgainstWriteTimes_KeepsTheMostRecentlyWritten()
    {
        var now = DateTime.UtcNow;
        SeedCopy("Acme--backup-20200101-000000.argobk", now.AddDays(-1));
        SeedCopy("Acme--backup-20200105-000000.argobk", now.AddDays(-3));
        SeedCopy("Acme--backup-20200109-000000.argobk", now.AddDays(-5));
        try
        {
            await _manager.CreateCompanyAsync(_companyPath, "Acme");

            var written = await _service.CreateAsync(_manager, Settings(keep: 2));

            Assert.NotNull(written);
            string[] expected = [Path.GetFileName(written), "Acme--backup-20200101-000000.argobk"];
            Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), FileNamesInFolder());
        }
        finally
        {
            await _manager.CloseCompanyAsync();
        }
    }

    /// <summary>
    /// A backup folder can be shared by several companies and hold files the user named by hand.
    /// Pruning one company must leave every file that is not one of its own dated copies, including
    /// another company whose name starts with this one's and a company literally named like a copy.
    /// </summary>
    [Fact]
    public async Task CreateAsync_FolderSharedWithOtherCompanies_PrunesOnlyThisCompanysCopies()
    {
        var old = DateTime.UtcNow.AddDays(-30);
        var ownOldCopy = SeedCopy("Acme--backup-20200101-000000.argobk", old);
        string[] notAcmesCopies =
        [
            SeedCopy("Borealis--backup-20200101-000000.argobk", old),
            SeedCopy("Borealis--backup-20200102-000000.argobk", old),
            SeedCopy("Acme West--backup-20200101-000000.argobk", old),
            SeedCopy("Acme--backup-20200101-000000--backup-20200102-000000.argobk", old),
            SeedCopy("Acme--backup-before-migration.argobk", old),
            SeedCopy("Acme.argobk", old)
        ];
        try
        {
            await _manager.CreateCompanyAsync(_companyPath, "Acme");

            var written = await _service.CreateAsync(_manager, Settings(keep: 1));

            Assert.NotNull(written);
            Assert.True(File.Exists(written));
            Assert.False(File.Exists(ownOldCopy));
            Assert.All(notAcmesCopies, p => Assert.True(File.Exists(p), $"{Path.GetFileName(p)} was deleted"));
        }
        finally
        {
            await _manager.CloseCompanyAsync();
        }
    }

    /// <summary>
    /// When the new copy cannot be written, nothing older is deleted, or a failing backup would
    /// eat through the good copies one attempt at a time.
    /// </summary>
    [Fact]
    public async Task CreateAsync_NewCopyNotWritten_KeepsEveryOlderCopy()
    {
        var now = DateTime.UtcNow;
        SeedCopy("Acme--backup-20200102-000000.argobk", now.AddDays(-1));
        SeedCopy("Acme--backup-20200101-000000.argobk", now.AddDays(-2));
        try
        {
            await _manager.CreateCompanyAsync(_companyPath, "Acme");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            // Cancelling makes the export fail after the folder is ready but before anything is written.
            var written = await _service.CreateAsync(_manager, Settings(keep: 1), cancelled.Token);

            Assert.Null(written);
            string[] expected = ["Acme--backup-20200101-000000.argobk", "Acme--backup-20200102-000000.argobk"];
            Assert.Equal(expected, FileNamesInFolder());
        }
        finally
        {
            await _manager.CloseCompanyAsync();
        }
    }

    private sealed class TempPlatform(string root) : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => root;
        public string GetTempPath() => Path.Combine(root, "temp");
        public string GetCachePath() => Path.Combine(root, "cache");
        public void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);
        public bool SupportsFileSystem => true;
        public bool SupportsNativeDialogs => false;
        public bool SupportsBiometrics => false;
        public Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("Not supported");
        public Task<bool> AuthenticateWithBiometricAsync(string reason) => Task.FromResult(false);
        public void StorePasswordForBiometric(string fileId, string password) { }
        public string? GetPasswordForBiometric(string fileId) => null;
        public void ClearPasswordForBiometric(string fileId) { }
        public bool SupportsAutoUpdate => false;
        public int MaxRecentCompanies => 10;
        public string NormalizePath(string path) => path;
        public string CombinePaths(params string[] paths) => Path.Combine(paths);
        public string GetMachineId() => "test-machine-id";
        public StringComparer PathComparer => StringComparer.Ordinal;
    }
}
