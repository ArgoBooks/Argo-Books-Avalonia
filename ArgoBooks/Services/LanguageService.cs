using System.Text.Json;
using System.Text.RegularExpressions;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Data;

namespace ArgoBooks.Services;

/// <summary>
/// Service for managing application language and translations.
/// Downloads pre-translated JSON files from server and applies them to UI.
/// </summary>
public partial class LanguageService
{
    private static readonly Lock Lock = new();

    /// <summary>
    /// Gets the singleton instance of the LanguageService.
    /// </summary>
    public static LanguageService Instance
    {
        get
        {
            if (field == null)
            {
                lock (Lock)
                {
                    field ??= new LanguageService();
                }
            }
            return field;
        }
    }

    // HTTP client for downloading translations
    private readonly HttpClient _httpClient = new();

    // Translation caches - only current language loaded in memory
    private Dictionary<string, string> _englishCache = new();
    private Dictionary<string, string> _currentLanguageCache = new();
    private string _currentLoadedIsoCode = "";

    // File paths
    private readonly string _cacheDirectory;

    // Download URL template (version will be inserted)
    private static readonly string DownloadUrlTemplate = $"{ApiConfig.BaseUrl}/resources/downloads/{{0}}/languages/{{1}}.json";

    /// <summary>
    /// Gets the current language name (e.g., "English", "French").
    /// </summary>
    public string CurrentLanguage { get; private set; } = "English";

    /// <summary>
    /// Gets the current ISO language code (e.g., "en", "fr").
    /// </summary>
    public string CurrentIsoCode { get; private set; } = "en";

    /// <summary>
    /// Gets whether the current language is English.
    /// </summary>
    public bool IsEnglish => CurrentIsoCode == "en";

    /// <summary>
    /// Whether the last download failed because the server has no file for this language and version,
    /// rather than because it couldn't be reached.
    /// </summary>
    public bool LastDownloadNotPublished { get; private set; }

    /// <summary>
    /// Event raised when the language changes.
    /// </summary>
    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    /// <summary>
    /// Event raised when translations are being downloaded.
    /// </summary>
    public event EventHandler<TranslationProgressEventArgs>? TranslationProgress;

    /// <summary>
    /// Regex for extracting string key from text.
    /// </summary>
    [GeneratedRegex(@"[^\w{}]")]
    private static partial Regex NonWordCharactersRegex();

    /// <summary>
    /// Private constructor for singleton pattern.
    /// </summary>
    private LanguageService()
    {
        _cacheDirectory = Path.Combine(
            PlatformServiceFactory.GetPlatformService().GetCachePath(), "Languages");

        // Ensure directory exists
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Gets the file path for a language's cache file.
    /// </summary>
    private string GetLanguageFilePath(string isoCode) =>
        Path.Combine(_cacheDirectory, $"{isoCode}.json");

    /// <summary>
    /// Initializes the language service by loading cached translations.
    /// </summary>
    public void Initialize()
    {
        LoadCachedTranslations();
    }

    /// <summary>
    /// Loads cached translations from disk. Only loads English on startup.
    /// Other languages are loaded on demand when switching.
    /// </summary>
    private void LoadCachedTranslations()
    {
        try
        {
            // Migrate legacy monolithic translations.json to per-language files
            MigrateLegacyTranslationsFile();

            // Load English translations
            LoadLanguageFile("en", ref _englishCache);

            App.ErrorLogger?.LogDebug($"LanguageService: Loaded {_englishCache.Count} English translations");
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to load cached translations");
        }
    }

    /// <summary>
    /// Loads a single language file from disk into the provided dictionary.
    /// </summary>
    private void LoadLanguageFile(string isoCode, ref Dictionary<string, string> target)
    {
        var filePath = GetLanguageFilePath(isoCode);
        if (!File.Exists(filePath)) return;

        try
        {
            var content = File.ReadAllText(filePath);
            if (!string.IsNullOrWhiteSpace(content))
            {
                var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
                if (translations != null && translations.Count > 0)
                {
                    target = translations;
                    App.ErrorLogger?.LogDebug($"LanguageService: Loaded {translations.Count} translations from {isoCode}.json");
                }
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, $"Failed to load language file: {isoCode}.json");
        }
    }

    /// <summary>
    /// Ensures the current language's translations are loaded into memory.
    /// </summary>
    private void EnsureLanguageLoaded(string isoCode)
    {
        if (isoCode == "en" || _currentLoadedIsoCode == isoCode)
            return;

        var cache = new Dictionary<string, string>();
        LoadLanguageFile(isoCode, ref cache);
        _currentLanguageCache = cache;
        _currentLoadedIsoCode = isoCode;
    }

    /// <summary>
    /// Migrates the legacy monolithic translations.json file to individual per-language files.
    /// </summary>
    private void MigrateLegacyTranslationsFile()
    {
        var legacyPath = Path.Combine(_cacheDirectory, "translations.json");
        if (!File.Exists(legacyPath))
            return;

        try
        {
            var content = File.ReadAllText(legacyPath);
            if (string.IsNullOrWhiteSpace(content))
                return;

            var allTranslations = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(content);
            if (allTranslations == null)
                return;

            var options = new JsonSerializerOptions { WriteIndented = true };
            foreach (var (isoCode, translations) in allTranslations)
            {
                var filePath = GetLanguageFilePath(isoCode);
                if (!File.Exists(filePath)) // Don't overwrite existing per-language files
                {
                    var sorted = translations
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                    File.WriteAllText(filePath, JsonSerializer.Serialize(sorted, options));
                }
            }

            // Remove the legacy file after successful migration
            File.Delete(legacyPath);
            App.ErrorLogger?.LogDebug($"LanguageService: Migrated {allTranslations.Count} languages from legacy translations.json");
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to migrate legacy translations.json");
        }
    }

    /// <summary>
    /// Saves a single language's translations to its own file.
    /// </summary>
    private void SaveLanguageFile(string isoCode, Dictionary<string, string> translations)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            // Sort by key for deterministic output (matches the translation tool's repo files)
            var sorted = translations
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            var jsonContent = JsonSerializer.Serialize(sorted, options);
            File.WriteAllText(GetLanguageFilePath(isoCode), jsonContent);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, $"Failed to save translation cache for {isoCode}");
        }
    }

    /// <summary>
    /// Sets the current language and downloads translations if needed.
    /// </summary>
    /// <param name="languageName">The language name (e.g., "French").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if language was successfully changed.</returns>
    public async Task<bool> SetLanguageAsync(string languageName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(languageName))
            return false;

        if (!Languages.IsValidLanguage(languageName))
        {
            App.ErrorLogger?.LogWarning($"LanguageService: Invalid language '{languageName}'");
            return false;
        }

        var isoCode = Languages.GetIsoCode(languageName);
        var previousLanguage = CurrentLanguage;
        var previousIsoCode = CurrentIsoCode;

        // Download translations if not already cached
        var downloadSuccess = await DownloadAndCacheLanguageAsync(languageName, false, cancellationToken);

        if (!downloadSuccess && isoCode != "en")
        {
            // If download failed and it's not English, check if we have cached translations on disk
            if (!File.Exists(GetLanguageFilePath(isoCode)))
            {
                App.ErrorLogger?.LogWarning($"LanguageService: Failed to download or find cached translations for {languageName}");
                return false;
            }
        }

        // Load the language into memory
        EnsureLanguageLoaded(isoCode);

        CurrentLanguage = languageName;
        CurrentIsoCode = isoCode;

        // Fire language changed event
        if (previousLanguage != languageName)
        {
            LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(previousLanguage, languageName, previousIsoCode, isoCode));
        }

        App.ErrorLogger?.LogDebug($"LanguageService: Language changed to {languageName} ({isoCode})");
        return true;
    }

    /// <summary>
    /// Downloads and caches translations for a language.
    /// </summary>
    /// <param name="languageName">The language name.</param>
    /// <param name="overwrite">Whether to overwrite existing cached translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful or already cached.</returns>
    public async Task<bool> DownloadAndCacheLanguageAsync(string languageName, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var isoCode = Languages.GetIsoCode(languageName);

        // Check if already cached (in memory or on disk)
        if (!overwrite)
        {
            if (isoCode == "en")
            {
                if (_englishCache.Count > 0)
                {
                    App.ErrorLogger?.LogDebug("LanguageService: English already cached");
                    return true;
                }
            }
            else if (File.Exists(GetLanguageFilePath(isoCode)))
            {
                App.ErrorLogger?.LogDebug($"LanguageService: {languageName} already cached on disk");
                return true;
            }
        }

        LastDownloadNotPublished = false;

        try
        {
            TranslationProgress?.Invoke(this, new TranslationProgressEventArgs(languageName, true, "Downloading translations..."));

            // Get app version for download URL
            var version = Core.Services.AppInfo.VersionNumber;
            var downloadUrl = string.Format(DownloadUrlTemplate, version, isoCode);

            App.ErrorLogger?.LogDebug($"LanguageService: Downloading from {downloadUrl}");

            var response = await _httpClient.GetAsync(downloadUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 404 typically means the server hasn't been updated with this version's
                // translations yet. Keep the existing cache intact rather than failing hard.
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    App.ErrorLogger?.LogDebug($"LanguageService: No translations published for version {version} ({isoCode}); keeping cached copy");
                    TranslationProgress?.Invoke(this, new TranslationProgressEventArgs(languageName, false, "No update available"));
                    LastDownloadNotPublished = true;
                    return File.Exists(GetLanguageFilePath(isoCode));
                }
                App.ErrorLogger?.LogWarning($"LanguageService: Download failed with status {response.StatusCode}");
                TranslationProgress?.Invoke(this, new TranslationProgressEventArgs(languageName, false, "Download failed"));
                return false;
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var downloadedTranslations = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);

            if (downloadedTranslations == null || downloadedTranslations.Count == 0)
            {
                App.ErrorLogger?.LogWarning("LanguageService: Downloaded translations are empty");
                TranslationProgress?.Invoke(this, new TranslationProgressEventArgs(languageName, false, "No translations found"));
                return false;
            }

            // Save to per-language file and update in-memory cache
            if (isoCode == "en")
            {
                _englishCache = downloadedTranslations;
            }
            else if (isoCode == _currentLoadedIsoCode || isoCode == CurrentIsoCode)
            {
                _currentLanguageCache = downloadedTranslations;
                _currentLoadedIsoCode = isoCode;
            }

            SaveLanguageFile(isoCode, downloadedTranslations);

            TranslationProgress?.Invoke(this, new TranslationProgressEventArgs(languageName, false, "Translations loaded"));
            App.ErrorLogger?.LogDebug($"LanguageService: Successfully downloaded {downloadedTranslations.Count} translations for {languageName}");
            return true;
        }
        catch (OperationCanceledException)
        {
            App.ErrorLogger?.LogDebug("LanguageService: Download cancelled");
            TranslationProgress?.Invoke(this, new TranslationProgressEventArgs(languageName, false, "Download cancelled"));
            return false;
        }
        catch (Exception ex)
        {
            NetworkFailure.Report(App.ErrorLogger, ex, $"Failed to download translations for {languageName}");
            TranslationProgress?.Invoke(this, new TranslationProgressEventArgs(languageName, false, $"Error: {ex.Message}"));
            return false;
        }
    }

    /// <summary>
    /// Updates all cached translations after an app upgrade.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if all updates were successful.</returns>
    public async Task<bool> UpdateAllCachedTranslationsAsync(CancellationToken cancellationToken = default)
    {
        var languagesToUpdate = new HashSet<string>();

        // Scan per-language files on disk to find cached languages
        try
        {
            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
            {
                var isoCode = Path.GetFileNameWithoutExtension(file);
                if (Languages.IsValidIsoCode(isoCode))
                {
                    var name = Languages.GetLanguageName(isoCode);
                    if (!string.IsNullOrEmpty(name))
                        languagesToUpdate.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, ErrorCategory.FileSystem, "Failed to scan language files for update");
        }

        if (languagesToUpdate.Count == 0)
        {
            App.ErrorLogger?.LogDebug("LanguageService: No cached translations to update");
            return true;
        }

        var successCount = 0;
        foreach (var languageName in languagesToUpdate)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            if (await DownloadAndCacheLanguageAsync(languageName, true, cancellationToken))
            {
                successCount++;
            }
        }

        return successCount == languagesToUpdate.Count;
    }

    /// <summary>
    /// Translates a string using the current language.
    /// </summary>
    /// <param name="text">The English text to translate.</param>
    /// <returns>The translated text, or the original if no translation is found.</returns>
    public string Translate(string text) => LocalizeShortcutModifier(TranslateCore(text));

    /// <summary>
    /// Rewrites the shortcut modifier in a translated string for the running platform, so
    /// a label reads "Cmd+S" on macOS and "Ctrl+S" everywhere else.
    ///
    /// Done here rather than at each label because the modifier appears inside otherwise
    /// ordinary sentences ("Toggle Grid (Ctrl+G)", "Ctrl+Scroll to zoom") that are
    /// translated as a whole. Catching it at the one point every localized string passes
    /// through covers every language without a parallel set of keys per platform.
    ///
    /// A no-op on Windows and Linux, where the modifier is already Ctrl.
    ///
    /// The key's name is matched rather than the exact text "Ctrl+", because translations
    /// spell it their own way ("Strg+" in German, "Ktrl+" in Basque) and some put spaces round
    /// the plus. Spelled "Cmd" rather than the U+2318 glyph: Inter, the app's font, has no glyph
    /// there, and the macOS fallback is what drew the stacked bars.
    /// </summary>
    private static string LocalizeShortcutModifier(string text)
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrEmpty(text))
            return text;

        return ShortcutModifierName().Replace(text, "Cmd");
    }

    [GeneratedRegex(@"(?<!\p{L})(?:Ctrl|Strg|Ktrl)(?=\s*\+)")]
    private static partial Regex ShortcutModifierName();

    private string TranslateCore(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // If English, return original text (or lookup from English cache if needed)
        if (CurrentIsoCode == "en")
        {
            var englishKey = GetStringKey(text);
            if (_englishCache.TryGetValue(englishKey, out var englishValue))
            {
                // Decode HTML entities in case the translation file contains encoded characters
                return DecodeHtmlEntities(englishValue);
            }
            return DecodeHtmlEntities(text);
        }

        // Look up in translation cache
        var key = GetStringKey(text);
        var result = GetCachedTranslationByKey(CurrentIsoCode, key);

        // Log missing translations to console
        if (result == null && text.Length < 100)
        {
            Console.WriteLine($"[TRANSLATE] Missing: '{DecodeHtmlEntities(text)}' (key: {key}) for {CurrentIsoCode}");
            return DecodeHtmlEntities(text);
        }

        return result ?? DecodeHtmlEntities(text);
    }

    /// <summary>
    /// Gets a cached translation by key directly.
    /// </summary>
    private string? GetCachedTranslationByKey(string isoCode, string key)
    {
        if (isoCode == "en")
        {
            if (_englishCache.TryGetValue(key, out var cachedTranslation))
            {
                return DecodeHtmlEntities(cachedTranslation);
            }
        }
        else
        {
            EnsureLanguageLoaded(isoCode);
            if (_currentLanguageCache.TryGetValue(key, out var cachedTranslation))
            {
                return DecodeHtmlEntities(cachedTranslation);
            }
        }

        return null;
    }

    /// <summary>
    /// Translates a string using a specific language.
    /// </summary>
    /// <param name="text">The English text to translate.</param>
    /// <param name="isoCode">The target language ISO code.</param>
    /// <returns>The translated text, or the original if no translation is found.</returns>
    public string Translate(string text, string isoCode)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (isoCode == "en")
            return DecodeHtmlEntities(text);

        var key = GetStringKey(text);
        var result = GetCachedTranslationByKey(isoCode, key);

        // Log missing translations to console
        if (result == null && text.Length < 100)
        {
            Console.WriteLine($"[TRANSLATE] Missing: '{DecodeHtmlEntities(text)}' (key: {key}) for {isoCode}");
            return DecodeHtmlEntities(text);
        }

        return result ?? DecodeHtmlEntities(text);
    }

    /// <summary>
    /// Decodes common HTML/XML entities that may come from XAML markup extensions.
    /// </summary>
    private static string DecodeHtmlEntities(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('&'))
            return text;

        return text
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'");
    }

    /// <summary>
    /// Gets a unique key for a string using text content.
    /// </summary>
    /// <param name="text">The text to generate a key for.</param>
    /// <returns>A unique key for the string.</returns>
    public static string GetStringKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        // Decode HTML/XML entities that may not be decoded by XAML parser in markup extensions
        var decodedText = text
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'");

        // Replace & with "amp" to match server-side key generation
        var processedText = decodedText.ToLowerInvariant().Replace("&", "amp");

        // Remove non-word characters (except placeholders like {0})
        var cleanText = NonWordCharactersRegex().Replace(processedText, "");

        // Limit length to avoid extremely long keys
        if (cleanText.Length > 50)
        {
            cleanText = cleanText[..50];
        }

        return $"str_{cleanText}";
    }

    /// <summary>
    /// Checks if a translation exists for the given text in the current language.
    /// </summary>
    /// <param name="text">The English text to check.</param>
    /// <returns>True if a translation exists.</returns>
    public bool HasTranslation(string text)
    {
        if (string.IsNullOrEmpty(text) || CurrentIsoCode == "en")
            return true;

        var textKey = GetStringKey(text);
        EnsureLanguageLoaded(CurrentIsoCode);
        return _currentLanguageCache.ContainsKey(textKey);
    }

}

/// <summary>
/// Event arguments for language change events.
/// </summary>
public class LanguageChangedEventArgs : EventArgs
{
    /// <summary>
    /// The previous language name.
    /// </summary>
    public string PreviousLanguage { get; }

    /// <summary>
    /// The new language name.
    /// </summary>
    public string NewLanguage { get; }

    /// <summary>
    /// The previous ISO code.
    /// </summary>
    public string PreviousIsoCode { get; }

    /// <summary>
    /// The new ISO code.
    /// </summary>
    public string NewIsoCode { get; }

    public LanguageChangedEventArgs(string previousLanguage, string newLanguage, string previousIsoCode, string newIsoCode)
    {
        PreviousLanguage = previousLanguage;
        NewLanguage = newLanguage;
        PreviousIsoCode = previousIsoCode;
        NewIsoCode = newIsoCode;
    }
}

/// <summary>
/// Event arguments for translation progress events.
/// </summary>
public class TranslationProgressEventArgs : EventArgs
{
    /// <summary>
    /// The language being downloaded.
    /// </summary>
    public string Language { get; }

    /// <summary>
    /// Whether the download is in progress.
    /// </summary>
    public bool IsDownloading { get; }

    /// <summary>
    /// Status message.
    /// </summary>
    public string Message { get; }

    public TranslationProgressEventArgs(string language, bool isDownloading, string message)
    {
        Language = language;
        IsDownloading = isDownloading;
        Message = message;
    }
}
