using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the FileService class.
/// </summary>
public class FileServiceTests
{
    private static FileService CreateService() =>
        new(new CompressionService(), new FooterService(), new EncryptionService());

    #region IsFileEncryptedAsync Tests

    [Fact]
    public async Task IsFileEncryptedAsync_NonExistentFile_ThrowsOrReturnsFalse()
    {
        var compressionService = new CompressionService();
        var footerService = new FooterService();
        var service = new FileService(compressionService, footerService);

        // Non-existent file should throw or handle gracefully
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await service.IsFileEncryptedAsync("/nonexistent/file.argo"));
    }

    #endregion

    #region Encrypted Open Round-Trip Tests

    [Fact]
    public async Task EncryptedSaveThenOpen_CorrectPassword_RoundTripsCompanyData()
    {
        var service = CreateService();
        const string password = "Sup3r$ecret!";
        var filePath = Path.Combine(Path.GetTempPath(), $"argo-test-{Guid.NewGuid():N}.argo");
        string? tempForSave = null;
        string? tempForOpen = null;

        try
        {
            // Create a company, then save it encrypted.
            await service.CreateCompanyAsync(filePath, "Round Trip Co");
            tempForSave = await service.OpenCompanyAsync(filePath);
            await service.SaveCompanyAsync(filePath, tempForSave, password);

            Assert.True(await service.IsFileEncryptedAsync(filePath));

            // Open with the correct password (exercises DecryptWithVerificationAsync).
            tempForOpen = await service.OpenCompanyAsync(filePath, password);
            var data = await service.LoadCompanyDataAsync(tempForOpen);

            Assert.Equal("Round Trip Co", data.Settings.Company.Name);
        }
        finally
        {
            CleanupTemp(tempForSave);
            CleanupTemp(tempForOpen);
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task EncryptedOpen_WrongPassword_ThrowsUnauthorized()
    {
        var service = CreateService();
        const string password = "CorrectHorse1!";
        var filePath = Path.Combine(Path.GetTempPath(), $"argo-test-{Guid.NewGuid():N}.argo");
        string? tempForSave = null;

        try
        {
            await service.CreateCompanyAsync(filePath, "Locked Co");
            tempForSave = await service.OpenCompanyAsync(filePath);
            await service.SaveCompanyAsync(filePath, tempForSave, password);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await service.OpenCompanyAsync(filePath, "wrong-password"));
        }
        finally
        {
            CleanupTemp(tempForSave);
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    private static void CleanupTemp(string? dir)
    {
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    #endregion

    #region Deferred Receipts Tests

    [Fact]
    public async Task LoadCompanyData_WithLoadReceiptsFalse_DefersReceipts()
    {
        var service = CreateService();
        var filePath = Path.Combine(Path.GetTempPath(), $"argo-test-{Guid.NewGuid():N}.argo");
        string? temp = null;

        try
        {
            await service.CreateCompanyAsync(filePath, "Receipts Co");
            temp = await service.OpenCompanyAsync(filePath);

            // Place a receipt into the extracted company so receipts.json is non-empty.
            var receipt = new Receipt { Id = "RCP-1", FileName = "r.jpg", FileData = "QUJD" };
            await service.WriteJsonAsync(temp, "receipts.json", new List<Receipt> { receipt });

            // Deferred load leaves Receipts empty...
            var deferred = await service.LoadCompanyDataAsync(temp, loadReceipts: false);
            Assert.Empty(deferred.Receipts);

            // ...but the receipts are still readable separately, intact.
            var loaded = await service.LoadReceiptsAsync(temp);
            Assert.Single(loaded);
            Assert.Equal("RCP-1", loaded[0].Id);
            Assert.Equal("QUJD", loaded[0].FileData);

            // Default load still loads receipts eagerly (back-compat for other callers).
            var eager = await service.LoadCompanyDataAsync(temp);
            Assert.Single(eager.Receipts);
        }
        finally
        {
            CleanupTemp(temp);
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    #endregion

    #region Save State Tests

    [Fact]
    public async Task SaveCompanyDataAsync_StagesToTemp_DoesNotMarkSaved()
    {
        // SaveCompanyDataAsync only stages JSON into the temp directory, so the data is not durable until the caller commits the .argo file.
        var service = CreateService();
        var data = new CompanyData();
        data.MarkAsModified();
        Assert.True(data.ChangesMade);

        var temp = Path.Combine(Path.GetTempPath(), $"argo-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            await service.SaveCompanyDataAsync(temp, data);

            Assert.True(data.ChangesMade);
        }
        finally
        {
            CleanupTemp(temp);
        }
    }

    #endregion
}
