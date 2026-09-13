using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for managing global and company settings.
/// </summary>
public class GlobalSettingsService : IGlobalSettingsService
{
    private const string GlobalSettingsFileName = "settings.json";
    private const string CompanySettingsFileName = "settings.json";

    private readonly IPlatformService _platformService;
    private readonly IErrorLogger? _errorLogger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the GlobalSettingsService.
    /// </summary>
    /// <param name="errorLogger">Optional logger for best-effort save failures.</param>
    public GlobalSettingsService(IErrorLogger? errorLogger = null)
        : this(PlatformServiceFactory.GetPlatformService(), errorLogger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the GlobalSettingsService with a specific platform service.
    /// </summary>
    /// <param name="platformService">Platform service for file paths.</param>
    /// <param name="errorLogger">Optional logger for best-effort save failures.</param>
    public GlobalSettingsService(IPlatformService platformService, IErrorLogger? errorLogger = null)
    {
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _errorLogger = errorLogger;

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public GlobalSettings GlobalSettings { get; private set; } = new();

    /// <summary>
    /// Whether this install had no settings file when it loaded, i.e. it has never run before.
    /// A file that fails to parse does not count: those settings are someone's, just unreadable.
    /// </summary>
    public bool IsFirstRun { get; private set; }

    public CompanySettings? CompanySettings { get; private set; }

    public async Task LoadGlobalSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settingsPath = GetGlobalSettingsPath();

        IsFirstRun = !File.Exists(settingsPath);
        if (IsFirstRun)
        {
            GlobalSettings = new GlobalSettings();
            return;
        }

        try
        {
            await using var fileStream = File.OpenRead(settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<GlobalSettings>(
                fileStream,
                _jsonOptions,
                cancellationToken);

            GlobalSettings = settings ?? new GlobalSettings();
        }
        catch (JsonException)
        {
            // Corrupted settings file, use defaults
            GlobalSettings = new GlobalSettings();
        }
    }

    /// <summary>
    /// Synchronously loads global settings from disk. Intended for use on the UI thread
    /// during startup, where the sidebar/theme/language read settings before the first
    /// window is shown. The settings file is small, so a sync read avoids the thread-pool
    /// marshaling overhead of sync-over-async.
    /// </summary>
    public void LoadGlobalSettings()
    {
        var settingsPath = GetGlobalSettingsPath();

        IsFirstRun = !File.Exists(settingsPath);
        if (IsFirstRun)
        {
            GlobalSettings = new GlobalSettings();
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(settingsPath);
            var settings = JsonSerializer.Deserialize<GlobalSettings>(bytes, _jsonOptions);
            GlobalSettings = settings ?? new GlobalSettings();
        }
        catch (JsonException)
        {
            // Corrupted settings file, use defaults
            GlobalSettings = new GlobalSettings();
        }
    }

    public async Task SaveGlobalSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            var settingsPath = GetGlobalSettingsPath();
            var directory = Path.GetDirectoryName(settingsPath);

            if (!string.IsNullOrEmpty(directory))
            {
                _platformService.EnsureDirectoryExists(directory);
            }

            // Write atomically: write to temp, then move. A crash mid-write would
            // otherwise leave a truncated settings.json and lose recent companies,
            // theme, and language on next launch.
            var tempPath = AtomicFile.TempPathFor(settingsPath);
            try
            {
                await using (var fileStream = new FileStream(
                    tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(
                        fileStream,
                        GlobalSettings,
                        _jsonOptions,
                        cancellationToken);
                }
                await AtomicFile.ReplaceAsync(tempPath, settingsPath, overwrite: true, cancellationToken);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* best effort */ }
                }

                // Only transient filesystem interference is treated as best-effort. Settings
                // persistence has many fire-and-forget callers (theme/sidebar/column toggles),
                // so an AV/indexer lock or a quarantined temp on first run must not surface as
                // an unobserved task exception that crashes startup. Anything else, including
                // cancellation and serialization bugs, propagates so it can be seen and fixed.
                if (ex is not IOException and not UnauthorizedAccessException)
                    throw;

                // The next save reattempts with the latest in-memory settings.
                _errorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to save global settings");
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task LoadCompanySettingsAsync(string tempDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tempDirectory);

        var settingsPath = Path.Combine(tempDirectory, CompanySettingsFileName);

        if (!File.Exists(settingsPath))
        {
            CompanySettings = new CompanySettings();
            return;
        }

        try
        {
            await using var fileStream = File.OpenRead(settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<CompanySettings>(
                fileStream,
                _jsonOptions,
                cancellationToken);

            CompanySettings = settings ?? new CompanySettings();
        }
        catch (JsonException)
        {
            // Corrupted settings file, use defaults
            CompanySettings = new CompanySettings();
        }
    }

    public async Task SaveCompanySettingsAsync(string tempDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tempDirectory);

        if (CompanySettings == null)
            return;

        var settingsPath = Path.Combine(tempDirectory, CompanySettingsFileName);
        _platformService.EnsureDirectoryExists(tempDirectory);

        await using var fileStream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(
            fileStream,
            CompanySettings,
            _jsonOptions,
            cancellationToken);
    }

    public void ClearCompanySettings()
    {
        CompanySettings = null;
    }

    /// <inheritdoc cref="IGlobalSettingsService.AddRecentCompany"/>
    public void AddRecentCompany(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        // Don't add sample company to recent list
        var samplePath = SampleCompanyService.GetSampleCompanyPath();
        if (string.Equals(filePath, samplePath, StringComparison.OrdinalIgnoreCase))
            return;

        var normalizedPath = _platformService.NormalizePath(filePath);
        var recentCompanies = GlobalSettings.RecentCompanies;

        // Remove existing entry if present (using platform-appropriate comparison)
        var existingIndex = recentCompanies.FindIndex(p =>
            _platformService.PathComparer.Equals(p, normalizedPath));
        if (existingIndex >= 0)
        {
            recentCompanies.RemoveAt(existingIndex);
        }

        // Add to the beginning
        recentCompanies.Insert(0, normalizedPath);

        // Trim to max size
        var maxRecent = _platformService.MaxRecentCompanies;
        while (recentCompanies.Count > maxRecent)
        {
            recentCompanies.RemoveAt(recentCompanies.Count - 1);
        }
    }

    /// <inheritdoc cref="IGlobalSettingsService.RemoveRecentCompany"/>
    public void RemoveRecentCompany(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var normalizedPath = _platformService.NormalizePath(filePath);
        var recentCompanies = GlobalSettings.RecentCompanies;

        // Remove using platform-appropriate comparison
        var existingIndex = recentCompanies.FindIndex(p =>
            _platformService.PathComparer.Equals(p, normalizedPath));
        if (existingIndex >= 0)
        {
            recentCompanies.RemoveAt(existingIndex);
        }
    }

    public string GetAppDataPath()
    {
        return _platformService.GetAppDataPath();
    }

    /// <summary>
    /// Gets all recent company paths that still exist.
    /// </summary>
    /// <returns>List of valid recent company paths.</returns>
    public IReadOnlyList<string> GetValidRecentCompanies()
    {
        // Get sample company path to filter it out
        var samplePath = SampleCompanyService.GetSampleCompanyPath();

        if (!_platformService.SupportsFileSystem)
        {
            // Browser platform - return all without file existence check, but exclude sample company
            return GlobalSettings.RecentCompanies
                .Where(p => !string.Equals(p, samplePath, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();
        }

        // Filter to existing files, exclude sample company, and deduplicate using platform-appropriate comparison
        // (handles case-insensitive duplicates on Windows)
        return GlobalSettings.RecentCompanies
            .Where(p => File.Exists(p) && !string.Equals(p, samplePath, StringComparison.OrdinalIgnoreCase))
            .Distinct(_platformService.PathComparer)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Creates a new company settings instance for a new company.
    /// </summary>
    /// <param name="companyName">Name of the company.</param>
    /// <returns>The created company settings.</returns>
    public CompanySettings CreateCompanySettings(string companyName)
    {
        CompanySettings = new CompanySettings
        {
            Company = new CompanyInfo
            {
                Name = companyName
            }
        };
        return CompanySettings;
    }

    private string GetGlobalSettingsPath()
    {
        return _platformService.CombinePaths(_platformService.GetAppDataPath(), GlobalSettingsFileName);
    }

    #region IGlobalSettingsService Implementation

    /// <inheritdoc />
    GlobalSettings IGlobalSettingsService.GetSettings() => GlobalSettings;

    /// <inheritdoc />
    void IGlobalSettingsService.SaveSettings(GlobalSettings settings)
    {
        GlobalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ = SaveGlobalSettingsAsync();
    }

    /// <inheritdoc />
    async Task<GlobalSettings> IGlobalSettingsService.LoadAsync()
    {
        await LoadGlobalSettingsAsync();
        return GlobalSettings;
    }

    /// <inheritdoc />
    Task IGlobalSettingsService.SaveAsync(GlobalSettings settings)
    {
        GlobalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        return SaveGlobalSettingsAsync();
    }

    /// <inheritdoc />
    IReadOnlyList<string> IGlobalSettingsService.GetRecentCompanies() => GetValidRecentCompanies();

    #endregion
}
