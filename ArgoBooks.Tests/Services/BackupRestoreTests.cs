using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A backup is only worth keeping if restoring it gives back the books it was taken of. These follow
/// the steps the app takes to restore one: copy the .argobk to a new .argo file and open that.
/// </summary>
public class BackupRestoreTests : IDisposable
{
    private const string Password = "CorrectHorse#1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"argo-bkrestore-{Guid.NewGuid():N}");
    private readonly string _companyPath;
    private readonly string _backupPath;
    private readonly string _restoredPath;
    private readonly CompanyManager _manager;

    public BackupRestoreTests()
    {
        Directory.CreateDirectory(_root);
        _companyPath = Path.Combine(_root, "Acme.argo");
        _backupPath = Path.Combine(_root, "Acme--backup-20260930-143200.argobk");
        _restoredPath = Path.Combine(_root, "Acme (restored).argo");

        var footerService = new FooterService();
        _manager = new CompanyManager(
            new FileService(new CompressionService(), footerService, new EncryptionService()),
            new GlobalSettingsService(new TempPlatform(Path.Combine(_root, "appdata"))),
            footerService);
    }

    public void Dispose()
    {
        _manager.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task RestoreAsync()
    {
        await _manager.CloseCompanyAsync();
        File.Copy(_backupPath, _restoredPath, overwrite: true);
    }

    /// <summary>
    /// A backup of a password-protected company restores with the customer and the revenue it held,
    /// amount included, and opens with the same password. It must not open without the password or
    /// with a wrong one, or a backup left in a shared folder would expose the books.
    /// </summary>
    [Fact]
    public async Task ExportBackup_PasswordProtectedCompany_RestoresSameDataBehindSamePassword()
    {
        try
        {
            await _manager.CreateCompanyAsync(_companyPath, "Acme", Password);
            var data = _manager.CompanyData!;
            data.Customers.Add(new Customer { Id = "CUS-001", Name = "Northwind Traders" });
            data.Revenues.Add(new Revenue
            {
                Id = "REV-001",
                CustomerId = "CUS-001",
                Date = new DateTime(2026, 3, 14),
                Total = 1234.56m,
                OriginalCurrency = "USD"
            });
            await _manager.SaveCompanyAsync();

            await _manager.ExportBackupAsync(_backupPath);
            await RestoreAsync();

            // With no password supplied and nobody to ask, an encrypted file is not opened.
            Assert.False(await _manager.OpenCompanyAsync(_restoredPath));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _manager.OpenCompanyAsync(_restoredPath, "WrongHorse#2"));

            Assert.True(await _manager.OpenCompanyAsync(_restoredPath, Password));
            Assert.True(_manager.IsEncrypted);
            var restored = _manager.CompanyData!;
            var customer = Assert.Single(restored.Customers);
            Assert.Equal("CUS-001", customer.Id);
            Assert.Equal("Northwind Traders", customer.Name);
            var revenue = Assert.Single(restored.Revenues);
            Assert.Equal("REV-001", revenue.Id);
            Assert.Equal("CUS-001", revenue.CustomerId);
            Assert.Equal(1234.56m, revenue.Total);
        }
        finally
        {
            await _manager.CloseCompanyAsync();
        }
    }

    /// <summary>
    /// Receipts load in the background after a company opens. A backup taken before they finish,
    /// such as the one taken just before an import, must still contain them, or restoring it would
    /// lose every receipt image.
    /// </summary>
    [Fact]
    public async Task ExportBackup_RightAfterOpen_IncludesReceiptsStillLoading()
    {
        try
        {
            await _manager.CreateCompanyAsync(_companyPath, "Acme", Password);
            _manager.CompanyData!.Receipts.Add(new Receipt { Id = "RCP-1", FileName = "r.jpg", FileData = "QUJD" });
            await _manager.SaveCompanyAsync();
            await _manager.CloseCompanyAsync();

            Assert.True(await _manager.OpenCompanyAsync(_companyPath, Password));
            // No EnsureReceiptsLoadedAsync here, because the export has to wait for them itself.
            await _manager.ExportBackupAsync(_backupPath);
            await RestoreAsync();

            Assert.True(await _manager.OpenCompanyAsync(_restoredPath, Password));
            await _manager.EnsureReceiptsLoadedAsync();
            var receipt = Assert.Single(_manager.CompanyData!.Receipts);
            Assert.Equal("RCP-1", receipt.Id);
            Assert.Equal("QUJD", receipt.FileData);
        }
        finally
        {
            await _manager.CloseCompanyAsync();
        }
    }

    /// <summary>
    /// A backup is a separate file. Taking one must not save the user's unsaved edits into the
    /// company file or mark them as saved, or closing without saving could no longer discard them.
    /// The backup itself holds the books as they are in memory, unsaved edits included.
    /// </summary>
    [Fact]
    public async Task ExportBackup_WithUnsavedEdits_LeavesCompanyFileAndUnsavedStateAlone()
    {
        try
        {
            await _manager.CreateCompanyAsync(_companyPath, "Acme", Password);
            await _manager.SaveCompanyAsync();
            _manager.CompanyData!.Customers.Add(new Customer { Id = "CUS-UNSAVED", Name = "Unsaved edit" });
            _manager.MarkAsChanged();

            await _manager.ExportBackupAsync(_backupPath);

            Assert.True(_manager.HasUnsavedChanges);
            Assert.Equal(_companyPath, _manager.CurrentFilePath);

            await RestoreAsync();
            Assert.True(await _manager.OpenCompanyAsync(_companyPath, Password));
            Assert.DoesNotContain(_manager.CompanyData!.Customers, c => c.Id == "CUS-UNSAVED");

            Assert.True(await _manager.OpenCompanyAsync(_restoredPath, Password));
            Assert.Contains(_manager.CompanyData!.Customers, c => c.Id == "CUS-UNSAVED");
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
