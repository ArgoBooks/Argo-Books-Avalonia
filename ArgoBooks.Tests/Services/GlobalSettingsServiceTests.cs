using ArgoBooks.Core.Models;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the GlobalSettingsService class.
/// </summary>
public class GlobalSettingsServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly MockPlatformService _platformService;
    private readonly GlobalSettingsService _settingsService;

    public GlobalSettingsServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"GlobalSettingsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _platformService = new MockPlatformService(_testDirectory);
        _settingsService = new GlobalSettingsService(_platformService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region LoadAsync Tests

    [Fact]
    public async Task LoadAsync_WhenNoFileExists_ReturnsDefaultSettings()
    {
        IGlobalSettingsService globalService = _settingsService;

        var settings = await globalService.LoadAsync();

        Assert.NotNull(settings);
        Assert.Empty(settings.RecentCompanies);
    }

    [Fact]
    public async Task LoadAsync_WhenFileExists_ReturnsPersistedSettings()
    {
        // Arrange: save settings first
        var original = new GlobalSettings();
        original.RecentCompanies.Add("/path/to/company.argo");
        original.Ui.Theme = "Light";

        IGlobalSettingsService globalService = _settingsService;
        await globalService.SaveAsync(original);

        // Act: create a new service instance and load
        var newService = new GlobalSettingsService(_platformService);
        IGlobalSettingsService newGlobalService = newService;
        var loaded = await newGlobalService.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Single(loaded.RecentCompanies);
        Assert.Equal("/path/to/company.argo", loaded.RecentCompanies[0]);
        Assert.Equal("Light", loaded.Ui.Theme);
    }

    [Fact]
    public async Task LoadAsync_WithCorruptedFile_ReturnsDefaultSettings()
    {
        // Arrange: write invalid JSON to the settings file
        var settingsPath = Path.Combine(_testDirectory, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{ this is not valid json }}}");

        IGlobalSettingsService globalService = _settingsService;
        var settings = await globalService.LoadAsync();

        Assert.NotNull(settings);
        Assert.Empty(settings.RecentCompanies);
    }

    // IsFirstRun is what decides whether the machine's own language is adopted, so it has to mean
    // "this install has never run", not "the settings didn't load".
    [Fact]
    public void IsFirstRun_WithNoSettingsFile_IsTrue()
    {
        _settingsService.LoadGlobalSettings();

        Assert.True(_settingsService.IsFirstRun);
    }

    [Fact]
    public async Task IsFirstRun_AfterSettingsHaveBeenSavedOnce_IsFalse()
    {
        IGlobalSettingsService globalService = _settingsService;
        await globalService.SaveAsync(new GlobalSettings());

        _settingsService.LoadGlobalSettings();

        Assert.False(_settingsService.IsFirstRun);
    }

    [Fact]
    public async Task IsFirstRun_WithACorruptedFile_IsFalse()
    {
        var settingsPath = Path.Combine(_testDirectory, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{ this is not valid json }}}");

        _settingsService.LoadGlobalSettings();

        Assert.False(_settingsService.IsFirstRun);
    }

    #endregion

    #region SaveAsync Tests

    [Fact]
    public async Task SaveAsync_PersistsSettingsToDisk()
    {
        var settings = new GlobalSettings();
        settings.Ui.Theme = "Light";
        settings.RecentCompanies.Add("/test/path.argo");

        IGlobalSettingsService globalService = _settingsService;
        await globalService.SaveAsync(settings);

        // Verify file was created
        var settingsPath = Path.Combine(_testDirectory, "settings.json");
        Assert.True(File.Exists(settingsPath));

        // Verify content
        var json = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("Light", json);
    }

    [Fact]
    public async Task SaveAsync_NullSettings_ThrowsArgumentNullException()
    {
        IGlobalSettingsService globalService = _settingsService;

        await Assert.ThrowsAsync<ArgumentNullException>(() => globalService.SaveAsync(null!));
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrip_RestoresSettings()
    {
        var original = new GlobalSettings();
        original.Ui.Theme = "Dark";
        original.Ui.AccentColor = "Red";
        original.Welcome.EulaAccepted = true;

        IGlobalSettingsService globalService = _settingsService;
        await globalService.SaveAsync(original);

        var newService = new GlobalSettingsService(_platformService);
        IGlobalSettingsService newGlobalService = newService;
        var loaded = await newGlobalService.LoadAsync();

        Assert.Equal("Dark", loaded.Ui.Theme);
        Assert.Equal("Red", loaded.Ui.AccentColor);
        Assert.True(loaded.Welcome.EulaAccepted);
    }

    [Fact]
    public async Task SaveGlobalSettingsAsync_WhenCancelled_Propagates()
    {
        // The best-effort save handler swallows transient filesystem errors, but a
        // cancellation must still surface rather than being silently swallowed.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _settingsService.SaveGlobalSettingsAsync(cts.Token));
    }

    #endregion

    #region AddRecentCompany Tests

    [Fact]
    public void AddRecentCompany_AddsToFront()
    {
        _settingsService.AddRecentCompany("/path/first.argo");
        _settingsService.AddRecentCompany("/path/second.argo");

        var settings = _settingsService.GlobalSettings;

        Assert.Equal(2, settings.RecentCompanies.Count);
        Assert.Equal("/path/second.argo", settings.RecentCompanies[0]);
        Assert.Equal("/path/first.argo", settings.RecentCompanies[1]);
    }

    [Fact]
    public void AddRecentCompany_PreventsDuplicates_MovesToFront()
    {
        _settingsService.AddRecentCompany("/path/first.argo");
        _settingsService.AddRecentCompany("/path/second.argo");
        _settingsService.AddRecentCompany("/path/first.argo");

        var settings = _settingsService.GlobalSettings;

        Assert.Equal(2, settings.RecentCompanies.Count);
        Assert.Equal("/path/first.argo", settings.RecentCompanies[0]);
        Assert.Equal("/path/second.argo", settings.RecentCompanies[1]);
    }

    [Fact]
    public void AddRecentCompany_TrimsToMaxSize()
    {
        // MockPlatformService.MaxRecentCompanies is 10
        for (var i = 0; i < 15; i++)
        {
            _settingsService.AddRecentCompany($"/path/company{i}.argo");
        }

        var settings = _settingsService.GlobalSettings;

        Assert.Equal(_platformService.MaxRecentCompanies, settings.RecentCompanies.Count);
    }

    [Fact]
    public void AddRecentCompany_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _settingsService.AddRecentCompany(string.Empty));
    }

    #endregion

    #region RemoveRecentCompany Tests

    [Fact]
    public void RemoveRecentCompany_RemovesByPath()
    {
        _settingsService.AddRecentCompany("/path/first.argo");
        _settingsService.AddRecentCompany("/path/second.argo");

        _settingsService.RemoveRecentCompany("/path/first.argo");

        var settings = _settingsService.GlobalSettings;

        Assert.Single(settings.RecentCompanies);
        Assert.Equal("/path/second.argo", settings.RecentCompanies[0]);
    }

    [Fact]
    public void RemoveRecentCompany_NonexistentPath_DoesNothing()
    {
        _settingsService.AddRecentCompany("/path/first.argo");

        _settingsService.RemoveRecentCompany("/path/nonexistent.argo");

        var settings = _settingsService.GlobalSettings;
        Assert.Single(settings.RecentCompanies);
    }

    [Fact]
    public void RemoveRecentCompany_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _settingsService.RemoveRecentCompany(string.Empty));
    }

    #endregion

    #region GetSettings Tests

    [Fact]
    public void GetSettings_ReturnsCurrentSettings()
    {
        IGlobalSettingsService globalService = _settingsService;

        var settings = globalService.GetSettings();

        Assert.NotNull(settings);
    }

    [Fact]
    public void GetSettings_AfterSaveSettings_ReturnsUpdatedSettings()
    {
        IGlobalSettingsService globalService = _settingsService;

        var newSettings = new GlobalSettings();
        newSettings.Ui.Theme = "Custom";
        globalService.SaveSettings(newSettings);

        var retrieved = globalService.GetSettings();

        Assert.NotNull(retrieved);
        Assert.Equal("Custom", retrieved.Ui.Theme);
    }

    #endregion

    #region CompanySettings Tests

    [Fact]
    public void CreateCompanySettings_SetsCompanyName()
    {
        var companySettings = _settingsService.CreateCompanySettings("Test Company");

        Assert.NotNull(companySettings);
        Assert.Equal("Test Company", companySettings.Company.Name);
        Assert.Same(companySettings, _settingsService.CompanySettings);
    }

    [Fact]
    public void ClearCompanySettings_SetsToNull()
    {
        _settingsService.CreateCompanySettings("Test");

        _settingsService.ClearCompanySettings();

        Assert.Null(_settingsService.CompanySettings);
    }

    [Fact]
    public async Task LoadCompanySettingsAsync_WhenNoFile_CreatesDefaults()
    {
        var tempDir = Path.Combine(_testDirectory, "company_temp");
        Directory.CreateDirectory(tempDir);

        await _settingsService.LoadCompanySettingsAsync(tempDir);

        Assert.NotNull(_settingsService.CompanySettings);
    }

    [Fact]
    public async Task SaveCompanySettingsAsync_ThenLoad_RoundTrip()
    {
        var tempDir = Path.Combine(_testDirectory, "company_temp2");
        Directory.CreateDirectory(tempDir);

        _settingsService.CreateCompanySettings("Round Trip Co");
        await _settingsService.SaveCompanySettingsAsync(tempDir);

        var newService = new GlobalSettingsService(_platformService);
        await newService.LoadCompanySettingsAsync(tempDir);

        Assert.NotNull(newService.CompanySettings);
        Assert.Equal("Round Trip Co", newService.CompanySettings!.Company.Name);
    }

    #endregion

    #region Mock Classes

    private class MockPlatformService(string appDataPath) : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => appDataPath;
        public string GetTempPath() => Path.Combine(appDataPath, "temp");
        public string GetDefaultDocumentsPath() => Path.Combine(appDataPath, "docs");
        public string GetLogsPath() => Path.Combine(appDataPath, "logs");
        public string GetCachePath() => Path.Combine(appDataPath, "cache");

        public void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

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
        public void RegisterFileTypeAssociations(string iconPath) { }
        public StringComparer PathComparer => StringComparer.Ordinal;
    }

    #endregion
}
