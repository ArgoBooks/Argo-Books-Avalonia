using ArgoBooks.Core.Models;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the LicenseService class.
/// </summary>
public class LicenseServiceTests
{
    private readonly MockEncryptionService _encryptionService;
    private readonly MockGlobalSettingsService _settingsService;
    private readonly MockPlatformService _platformService;
    private readonly LicenseService _licenseService;

    public LicenseServiceTests()
    {
        _encryptionService = new MockEncryptionService();
        _settingsService = new MockGlobalSettingsService();
        _platformService = new MockPlatformService();
        var connectivityService = new MockConnectivityService();
        _licenseService = new LicenseService(_encryptionService, _settingsService, _platformService, connectivityService);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullEncryptionService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LicenseService(null!, _settingsService, _platformService));
    }

    [Fact]
    public void Constructor_NullSettingsService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LicenseService(_encryptionService, null!, _platformService));
    }

    [Fact]
    public void Constructor_NullPlatformService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LicenseService(_encryptionService, _settingsService, (IPlatformService)null!));
    }

    #endregion

    #region SaveLicenseAsync Tests

    [Fact]
    public async Task SaveLicenseAsync_ValidLicense_SavesSuccessfully()
    {
        await _licenseService.SaveLicenseAsync(true, "TEST-LICENSE-KEY");

        Assert.NotNull(_settingsService.SavedSettings);
        Assert.NotNull(_settingsService.SavedSettings.License.LicenseData);
        Assert.NotNull(_settingsService.SavedSettings.License.Salt);
        Assert.NotNull(_settingsService.SavedSettings.License.Iv);
    }

    [Fact]
    public async Task SaveLicenseAsync_NullSettings_DoesNotThrow()
    {
        _settingsService.ReturnNullSettings = true;

        await _licenseService.SaveLicenseAsync(true, "TEST-KEY");

        // Should not throw and should not save
        Assert.Null(_settingsService.SavedSettings);
    }

    [Fact]
    public async Task SaveLicenseAsync_PremiumLicense_SavesCorrectly()
    {
        await _licenseService.SaveLicenseAsync(true, "PREMIUM-KEY");

        var loaded = _licenseService.LoadLicense();

        Assert.True(loaded);
    }

    [Fact]
    public async Task SaveLicenseAsync_NoLicense_SavesCorrectly()
    {
        await _licenseService.SaveLicenseAsync(false, null);

        var loaded = _licenseService.LoadLicense();

        Assert.False(loaded);
    }

    #endregion

    #region LoadLicense Tests

    [Fact]
    public void LoadLicense_NoSavedLicense_ReturnsFalse()
    {
        _settingsService.ClearLicenseData();

        var result = _licenseService.LoadLicense();

        Assert.False(result);
    }

    [Fact]
    public void LoadLicense_NullSettings_ReturnsFalse()
    {
        _settingsService.ReturnNullSettings = true;

        var result = _licenseService.LoadLicense();

        Assert.False(result);
    }

    [Fact]
    public void LoadLicense_NullLicenseData_ReturnsFalse()
    {
        _settingsService.GetSettings()!.License.LicenseData = null;

        var result = _licenseService.LoadLicense();

        Assert.False(result);
    }

    [Fact]
    public void LoadLicense_NullSalt_ReturnsFalse()
    {
        _settingsService.GetSettings()!.License.Salt = null;

        var result = _licenseService.LoadLicense();

        Assert.False(result);
    }

    [Fact]
    public void LoadLicense_NullIv_ReturnsFalse()
    {
        _settingsService.GetSettings()!.License.Iv = null;

        var result = _licenseService.LoadLicense();

        Assert.False(result);
    }

    [Fact]
    public async Task LoadLicense_AfterSave_ReturnsCorrectValue()
    {
        await _licenseService.SaveLicenseAsync(true, "TEST-KEY");

        var result = _licenseService.LoadLicense();

        Assert.True(result);
    }

    [Fact]
    public void LoadLicense_InvalidEncryptedData_ReturnsFalse()
    {
        _encryptionService.ThrowOnDecrypt = true;
        var settings = _settingsService.GetSettings()!;
        settings.License.LicenseData = "invalid-base64-data==";
        settings.License.Salt = "test-salt";
        settings.License.Iv = "test-iv";

        var result = _licenseService.LoadLicense();

        Assert.False(result);
    }

    #endregion

    #region ClearLicenseAsync Tests

    [Fact]
    public async Task ClearLicenseAsync_ClearsLicenseData()
    {
        await _licenseService.SaveLicenseAsync(true, "TEST-KEY");
        await _licenseService.ClearLicenseAsync();

        Assert.Null(_settingsService.SavedSettings?.License.LicenseData);
    }

    [Fact]
    public async Task ClearLicenseAsync_NullSettings_DoesNotThrow()
    {
        _settingsService.ReturnNullSettings = true;

        await _licenseService.ClearLicenseAsync();

        // Should not throw
    }

    [Fact]
    public async Task ClearLicenseAsync_AfterClear_LoadReturnsFalse()
    {
        await _licenseService.SaveLicenseAsync(true, "TEST-KEY");
        await _licenseService.ClearLicenseAsync();

        var result = _licenseService.LoadLicense();

        Assert.False(result);
    }

    #endregion

    #region Round-Trip Tests

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveAndLoad_RoundTrip_PreservesValues(bool hasPremium)
    {
        await _licenseService.SaveLicenseAsync(hasPremium, "LICENSE-KEY");

        var result = _licenseService.LoadLicense();

        Assert.Equal(hasPremium, result);
    }

    #endregion

    #region GetDeviceId Tests

    [Fact]
    public void GetDeviceId_ReturnsNonNullNonEmptyString()
    {
        var deviceId = _licenseService.GetDeviceId();

        Assert.False(string.IsNullOrEmpty(deviceId));
    }

    [Fact]
    public void GetDeviceId_ReturnsConsistentValue()
    {
        var deviceId1 = _licenseService.GetDeviceId();
        var deviceId2 = _licenseService.GetDeviceId();

        Assert.Equal(deviceId1, deviceId2);
    }

    [Fact]
    public async Task ValidateLicenseOnlineAsync_NoLicenseKey_ReturnsInvalidKey()
    {
        // No license saved, GetLicenseKey() returns null
        var result = await _licenseService.ValidateLicenseOnlineAsync();

        Assert.Equal(LicenseValidationStatus.InvalidKey, result.Status);
    }

    #endregion

    #region Mock Classes

    private class MockEncryptionService : IEncryptionService
    {
        private readonly Dictionary<string, byte[]> _encryptedData = new();
        public bool ThrowOnDecrypt { get; set; }

        public string GenerateSalt() => Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        public string GenerateIv() => Convert.ToBase64String(Guid.NewGuid().ToByteArray()[..12]);

        private static string HashPassword(string password, string salt) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password + salt));

        public byte[] Encrypt(byte[] data, string password, string salt, string iv)
        {
            // Simple mock: just store the data and return a marker
            var key = $"{salt}:{iv}";
            _encryptedData[key] = data;
            return System.Text.Encoding.UTF8.GetBytes(key);
        }

        public byte[] Decrypt(byte[] encryptedData, string password, string salt, string iv)
        {
            if (ThrowOnDecrypt)
                throw new System.Security.Cryptography.CryptographicException("Mock decryption failure");

            var key = $"{salt}:{iv}";
            if (_encryptedData.TryGetValue(key, out var data))
                return data;

            throw new System.Security.Cryptography.CryptographicException("Data not found");
        }

        public byte[] EncryptWithKey(byte[] data, byte[] key, byte[] nonce)
        {
            var handle = Convert.ToBase64String(nonce);
            _encryptedData[handle] = data;
            return System.Text.Encoding.UTF8.GetBytes(handle);
        }

        public byte[] DecryptWithKey(byte[] encryptedData, byte[] key, byte[] nonce)
        {
            if (ThrowOnDecrypt)
                throw new System.Security.Cryptography.CryptographicException("Mock decryption failure");

            var handle = Convert.ToBase64String(nonce);
            if (_encryptedData.TryGetValue(handle, out var data))
                return data;

            throw new System.Security.Cryptography.CryptographicException("Data not found");
        }

        public byte[] DecryptWithVerification(
            byte[] encryptedData, string password, string salt, string iv, string expectedPasswordHash)
        {
            if (HashPassword(password, salt) != expectedPasswordHash)
                throw new UnauthorizedAccessException("Invalid password.");
            return Decrypt(encryptedData, password, salt, iv);
        }

        public Task<MemoryStream> DecryptWithVerificationAsync(
            Stream encryptedStream, string password, string salt, string iv, string expectedPasswordHash)
        {
            using var ms = new MemoryStream();
            encryptedStream.CopyTo(ms);
            var decrypted = DecryptWithVerification(ms.ToArray(), password, salt, iv, expectedPasswordHash);
            return Task.FromResult(new MemoryStream(decrypted));
        }
    }

    private class MockGlobalSettingsService : IGlobalSettingsService
    {
        private GlobalSettings _settings = new();
        public GlobalSettings? SavedSettings { get; private set; }
        public bool ReturnNullSettings { get; set; }

        public GlobalSettings? GetSettings() => ReturnNullSettings ? null : _settings;

        public void SaveSettings(GlobalSettings settings)
        {
            SavedSettings = settings;
            _settings = settings;
        }

        public Task<GlobalSettings> LoadAsync() => Task.FromResult(_settings);

        public Task SaveAsync(GlobalSettings settings)
        {
            SaveSettings(settings);
            return Task.CompletedTask;
        }

        public IReadOnlyList<string> GetRecentCompanies() => _settings.RecentCompanies.AsReadOnly();
        public void AddRecentCompany(string filePath) => _settings.RecentCompanies.Add(filePath);
        public void RemoveRecentCompany(string filePath) => _settings.RecentCompanies.Remove(filePath);

        public void ClearLicenseData()
        {
            _settings.License = new LicenseSettings();
        }
    }

    private class MockPlatformService : IPlatformService
    {
        public PlatformType Platform => PlatformType.Windows;
        public bool SupportsFileSystem => true;
        public bool SupportsNativeDialogs => true;
        public bool SupportsBiometrics => false;
        public bool SupportsAutoUpdate => true;
        public int MaxRecentCompanies => 10;

        public string GetAppDataPath() => "/mock/appdata";
        public string GetTempPath() => "/mock/temp";
        public string GetCachePath() => "/mock/cache";
        public string NormalizePath(string path) => path;
        public string CombinePaths(params string[] paths) => string.Join("/", paths);
        public void EnsureDirectoryExists(string path) { }
        public Task<bool> AuthenticateWithBiometricAsync(string reason) => Task.FromResult(false);
        public void ClearPasswordForBiometric(string key) { }
        public string? GetPasswordForBiometric(string key) => null;
        public Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("Not supported");
        public StringComparer PathComparer => StringComparer.OrdinalIgnoreCase;
        public void StorePasswordForBiometric(string key, string password) { }

        /// <summary>
        /// Returns a stable mock machine ID for testing.
        /// </summary>
        public string GetMachineId() => "MOCK-MACHINE-ID-12345";

        /// <summary>
        /// Mock implementation that does nothing.
        /// </summary>
    }

    private class MockConnectivityService : IConnectivityService
    {
        public Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> IsHostReachableAsync(string host, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    #endregion
}
