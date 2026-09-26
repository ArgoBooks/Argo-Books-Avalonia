using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArgoBooks.Data;
using ArgoBooks.Services;

namespace ArgoBooks.Translations;

/// <summary>
/// Admin tool for generating translations.
/// Scans source files for translatable strings and translates them using Azure Translator API.
/// </summary>
public partial class TranslationGenerator
{
    // Azure Translator API configuration
    private readonly string _azureKey;
    private readonly string _azureRegion;
    private const string AzureEndpoint = "https://api.cognitive.microsofttranslator.com";

    private readonly HttpClient _httpClient = new();

    // Regex patterns for extracting translatable strings
    [GeneratedRegex(@"\{loc:Loc\s+([^}]+)\}")]
    private static partial Regex LocExtensionRegex();

    // Strips whole-line C# comments (// ... and /// ...). Only matches when the //
    // is at the start of a line (after optional whitespace), so URLs inside string
    // literals like "https://example.com" aren't truncated and any .Translate() calls
    // sharing the line aren't silently dropped. End-of-line comments after code are
    // left intact, picking up a stray translatable string from one would produce a
    // visible spurious entry, which is preferable to silently losing a real one.
    [GeneratedRegex(@"(?m)^[\t ]*//.*$")]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex(@"Loc\.Tr\s*\(\s*""([^""]+)""")]
    private static partial Regex LocTrCallRegex();

    // Pattern for Tr("text") calls (local helper method used in ReportRenderer, etc.)
    [GeneratedRegex(@"(?<!Loc\.)\bTr\s*\(\s*""([^""]+)""")]
    private static partial Regex LocalTrCallRegex();

    // Pattern for CreateItem("Label", ...) calls in sidebar/menu
    [GeneratedRegex(@"CreateItem\s*\(\s*""([^""]+)""")]
    private static partial Regex CreateItemCallRegex();

    // Pattern for QuickActionItem("Title", "Description", ...) - capture both title and description
    [GeneratedRegex(@"new\s+QuickActionItem\s*\(\s*""([^""]+)""\s*,\s*""([^""]+)""")]
    private static partial Regex QuickActionItemRegex();

    // Pattern for public const string = "value" (e.g., DatePresetNames)
    [GeneratedRegex(@"public\s+const\s+string\s+\w+\s*=\s*""([^""]+)""")]
    private static partial Regex ConstStringRegex();

    // Pattern for static readonly string[] = ["item1", "item2", ...] or new[] { "item1", ... }
    [GeneratedRegex(@"static\s+readonly\s+string\s*\[\s*\]\s+\w+\s*=\s*\[([^\]]+)\]")]
    private static partial Regex StaticStringArrayRegex();

    [GeneratedRegex(@"""([^""]+)""\s*\.Translate")]
    private static partial Regex StringTranslateRegex();

    [GeneratedRegex(@"""([^""]+)""\s*\.TranslateFormat")]
    private static partial Regex StringTranslateFormatRegex();

    // Pattern for LanguageService.Instance.Translate("text") and .Translate("text") calls
    [GeneratedRegex(@"\.Translate\s*\(\s*""([^""]+)""")]
    private static partial Regex TranslateWithArgRegex();

    // Pattern for switch expression display strings: => "Some Display Text"
    [GeneratedRegex(@"=>\s*""([^""]+)""")]
    private static partial Regex SwitchDisplayStringRegex();

    // Pattern for string array/collection items: "Item" in ["Item1", "Item2"] or new[] { "Item1" }
    [GeneratedRegex(@"\[\s*""([^""]+)""")]
    private static partial Regex ArrayItemStartRegex();

    // Continuation strings inside an array literal: matches `, "value"` only when the
    // following token is another quoted string or a closing `]`. This filters out method
    // call arguments like `Load("Page", "Column", true)` where the next token after a
    // string is a non-string parameter, while still catching real array literals like
    // `["A", "B", "C"]`.
    [GeneratedRegex(@",\s*""([^""]+)""(?=\s*(?:,\s*""|\]))")]
    private static partial Regex ArrayItemContinueRegex();

    // Batch size for Azure API calls (max 100 texts per request, max 10000 chars)
    private const int BatchSize = 50;
    private const int MaxCharsPerBatch = 9000;

    // Azure Translator pricing: $10 per 1M characters (S1 pay-as-you-go)
    private const decimal PricePerMillionChars = 10m;

    // Usage tracking

    /// <summary>
    /// Event raised to report progress.
    /// </summary>
    public event EventHandler<TranslationGeneratorProgressEventArgs>? Progress;

    /// <summary>
    /// Gets the number of Azure API calls made during translation.
    /// </summary>
    public int ApiCallCount { get; private set; }

    /// <summary>
    /// Gets the total number of source characters sent for translation.
    /// </summary>
    public long TotalCharactersTranslated { get; private set; }

    /// <summary>
    /// Gets the number of languages translated.
    /// </summary>
    public int LanguagesTranslated { get; private set; }

    /// <summary>
    /// Gets the total number of stale keys removed from translation files.
    /// </summary>
    public int StaleKeysRemoved { get; private set; }

    /// <summary>
    /// Per-language list of source strings legitimately unchanged in the target.
    /// Loaded from translation-allowlist.json. Lookup uses ordinal comparison.
    /// </summary>
    public Dictionary<string, HashSet<string>> Allowlist { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-language counts of strings Azure returned unchanged from English (excluding allowlisted entries).
    /// </summary>
    public Dictionary<string, List<string>> SuspiciousNoOps { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Distinct English source strings that produce the same key after GetStringKey normalization
    /// (50-char truncation can collide). The first one wins; others are silently dropped.
    /// </summary>
    public Dictionary<string, List<string>> KeyCollisions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the estimated cost based on Azure Translator S1 pricing ($10 per 1M characters).
    /// </summary>
    public decimal EstimatedCost => TotalCharactersTranslated / 1_000_000m * PricePerMillionChars;

    /// <summary>
    /// Creates a new TranslationGenerator.
    /// </summary>
    /// <param name="azureKey">Azure Translator API key.</param>
    /// <param name="azureRegion">Azure region (e.g., "eastus").</param>
    public TranslationGenerator(string azureKey, string azureRegion = "eastus")
    {
        _azureKey = azureKey ?? throw new ArgumentNullException(nameof(azureKey));
        _azureRegion = azureRegion;
    }

    /// <summary>
    /// Collects all translatable strings from the source directory.
    /// </summary>
    /// <param name="sourceDirectory">The root source directory to scan.</param>
    /// <returns>Dictionary of translation keys to English text.</returns>
    public Dictionary<string, string> CollectStrings(string sourceDirectory)
    {
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ReportProgress("Scanning source files...", 0, 0);

        // Scan AXAML files for {loc:Loc ...} usages
        var axamlFiles = Directory.GetFiles(sourceDirectory, "*.axaml", SearchOption.AllDirectories);
        foreach (var file in axamlFiles)
        {
            CollectFromAxamlFile(file, strings);
        }

        // Scan CS files for .Translate() and Loc.Tr() calls
        var csFiles = Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories);
        foreach (var file in csFiles)
        {
            CollectFromCsFile(file, strings);
        }

        // Also scan ArgoBooks.Core for display strings in enums and services
        var coreDirectory = Path.Combine(Path.GetDirectoryName(sourceDirectory) ?? sourceDirectory, "ArgoBooks.Core");
        if (Directory.Exists(coreDirectory))
        {
            ReportProgress("Scanning ArgoBooks.Core...", 0, 0);
            var coreFiles = Directory.GetFiles(coreDirectory, "*.cs", SearchOption.AllDirectories);
            foreach (var file in coreFiles)
            {
                CollectFromCsFile(file, strings);
            }
        }

        // Fetch plan feature strings from the pricing API so they get translated
        FetchPlanStrings(strings);

        ReportProgress($"Found {strings.Count} translatable strings", strings.Count, strings.Count);

        return strings;
    }

    /// <summary>
    /// Fetches plan feature labels and details from the pricing API
    /// and adds them to the translation dictionary.
    /// </summary>
    private void FetchPlanStrings(Dictionary<string, string> strings)
    {
        try
        {
            ReportProgress("Fetching plan strings from pricing API...", 0, 0);

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var apiUrl = $"{Core.Services.ApiConfig.BaseUrl}/api/pricing/plans.php";
            var response = httpClient.GetStringAsync(apiUrl).GetAwaiter().GetResult();
            var doc = JsonSerializer.Deserialize<JsonElement>(response);

            if (!doc.TryGetProperty("plans", out var plans))
                return;

            var count = 0;
            foreach (var planName in new[] { "free", "premium" })
            {
                if (!plans.TryGetProperty(planName, out var plan) ||
                    !plan.TryGetProperty("features", out var features))
                    continue;

                foreach (var feature in features.EnumerateArray())
                {
                    if (feature.TryGetProperty("label", out var label) && label.GetString() is { } labelText)
                    {
                        AddString(strings, labelText);
                        count++;
                    }

                    if (feature.TryGetProperty("detail", out var detail) && detail.GetString() is { } detailText)
                    {
                        AddString(strings, detailText);
                        count++;
                    }
                }
            }

            ReportProgress($"Added {count} plan strings from API", 0, 0);
        }
        catch (Exception ex)
        {
            ReportProgress($"Warning: Could not fetch plan strings from API: {ex.Message}", 0, 0);
        }
    }

    /// <summary>
    /// Collects translatable strings from an AXAML file.
    /// </summary>
    private void CollectFromAxamlFile(string filePath, Dictionary<string, string> strings)
    {
        try
        {
            var content = File.ReadAllText(filePath);

            // Find all {Loc ...} usages
            var matches = LocExtensionRegex().Matches(content);
            foreach (Match match in matches)
            {
                var text = match.Groups[1].Value.Trim();

                // Remove quotes if present
                if ((text.StartsWith('\'') && text.EndsWith('\'')) ||
                    (text.StartsWith('"') && text.EndsWith('"')))
                {
                    text = text[1..^1];
                }

                // Decode XML entities so AXAML "&amp;" matches C# "&" (otherwise both forms collide)
                text = text
                    .Replace("&amp;", "&")
                    .Replace("&lt;", "<")
                    .Replace("&gt;", ">")
                    .Replace("&quot;", "\"")
                    .Replace("&apos;", "'");

                if (!string.IsNullOrWhiteSpace(text))
                {
                    var key = LanguageService.GetStringKey(text);
                    strings.TryAdd(key, text);
                }
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogWarning($"Error scanning {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Collects translatable strings from a C# file.
    /// </summary>
    private void CollectFromCsFile(string filePath, Dictionary<string, string> strings)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            // Strip line comments (incl. /// doc comments) so example snippets in
            // documentation aren't picked up as real translatable strings.
            content = LineCommentRegex().Replace(content, "");

            // Find "text".Translate() patterns
            var translateMatches = StringTranslateRegex().Matches(content);
            foreach (Match match in translateMatches)
            {
                AddString(strings, match.Groups[1].Value);
            }

            // Find "text".TranslateFormat(...) patterns
            var translateFormatMatches = StringTranslateFormatRegex().Matches(content);
            foreach (Match match in translateFormatMatches)
            {
                AddString(strings, match.Groups[1].Value);
            }

            // Find .Translate("text") patterns (e.g., LanguageService.Instance.Translate("Other"))
            var translateWithArgMatches = TranslateWithArgRegex().Matches(content);
            foreach (Match match in translateWithArgMatches)
            {
                AddString(strings, match.Groups[1].Value);
            }

            // Find Loc.Tr("text") patterns
            var locTrMatches = LocTrCallRegex().Matches(content);
            foreach (Match match in locTrMatches)
            {
                AddString(strings, match.Groups[1].Value);
            }

            // Find Tr("text") patterns (local helper method in ReportRenderer, etc.)
            var localTrMatches = LocalTrCallRegex().Matches(content);
            foreach (Match match in localTrMatches)
            {
                AddString(strings, match.Groups[1].Value);
            }

            // Find CreateItem("Label", ...) patterns for sidebar/menu items
            var createItemMatches = CreateItemCallRegex().Matches(content);
            foreach (Match match in createItemMatches)
            {
                AddString(strings, match.Groups[1].Value);
            }

            // Find QuickActionItem("Title", "Description", ...) patterns
            var quickActionMatches = QuickActionItemRegex().Matches(content);
            foreach (Match match in quickActionMatches)
            {
                AddString(strings, match.Groups[1].Value); // Title
                AddString(strings, match.Groups[2].Value); // Description
            }

            // Find public const string = "value" patterns (DatePresetNames, etc.)
            var constStringMatches = ConstStringRegex().Matches(content);
            foreach (Match match in constStringMatches)
            {
                var text = match.Groups[1].Value;
                // Only add if it looks like display text (not empty, not a path, not a URL, not a color code, not an SVG path, not a code identifier).
                // A dot is allowed in a sentence, so Core's message constants (shown with .Translate()) are collected.
                if (!string.IsNullOrEmpty(text) && text.Length > 1 &&
                    !text.Contains('/') && !text.Contains('\\') && (!text.Contains('.') || IsSentence(text)) &&
                    !text.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                    !text.StartsWith("{") &&
                    !text.StartsWith('#') &&
                    !IsSvgPathData(text) &&
                    !LooksLikeCodeIdentifier(text))
                {
                    AddString(strings, text);
                }
            }

            // Find static readonly string[] = [...] patterns
            var stringArrayMatches = StaticStringArrayRegex().Matches(content);
            foreach (Match match in stringArrayMatches)
            {
                var arrayContent = match.Groups[1].Value;
                // Extract individual strings from the array
                var stringMatches = Regex.Matches(arrayContent, @"""([^""]+)""");
                foreach (Match stringMatch in stringMatches)
                {
                    AddDisplayString(strings, stringMatch.Groups[1].Value);
                }
            }

            // Find switch expression display strings: => "Display Text"
            // Applied to all .cs files; the shape filter below guards against false positives.
            // NOTE: object-initializer assignments like `Title = "Foo"` are NOT detected, wrap
            // user-facing literals with .Translate()/.TranslateFormat() so the regex below picks them up.
            var switchMatches = SwitchDisplayStringRegex().Matches(content);
            foreach (Match match in switchMatches)
            {
                var text = match.Groups[1].Value;
                // Only include strings that look like display text (starts with uppercase, has space or common pattern,
                // and isn't a PascalCase code identifier like "InStock" or "ShowCompanyAddress")
                if (!string.IsNullOrEmpty(text) && char.IsUpper(text[0]) &&
                    (text.Contains(' ') || text.Length > 3) &&
                    !text.Contains('/') && !text.Contains('\\') && !text.Contains('.') &&
                    !text.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                    !LooksLikeCodeIdentifier(text))
                {
                    AddString(strings, text);
                }
            }

            // Find string array items: ["Item1", "Item2", "Item3"], used for ComboBox options
            // Restricted to ViewModel/Service/Enum/Configuration files because plain arrays
            // appear in many non-UI contexts and would generate noise.
            if (filePath.Contains("Enum") || filePath.Contains("Service") || filePath.Contains("ViewModel") || filePath.Contains("Configuration"))
            {
                var arrayStartMatches = ArrayItemStartRegex().Matches(content);
                foreach (Match match in arrayStartMatches)
                {
                    AddDisplayString(strings, match.Groups[1].Value);
                }

                var arrayContMatches = ArrayItemContinueRegex().Matches(content);
                foreach (Match match in arrayContMatches)
                {
                    AddDisplayString(strings, match.Groups[1].Value);
                }
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogWarning($"Error scanning {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a display string if it looks like user-facing text.
    /// </summary>
    private void AddDisplayString(Dictionary<string, string> strings, string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 2)
            return;

        // Skip technical strings
        if (text.Contains('/') || text.Contains('\\') || text.Contains('.') ||
            text.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("{") || text.Contains("{{"))
            return;

        // Skip PascalCase/camelCase code identifiers (e.g., "InStock", "ShowCompanyAddress")
        if (LooksLikeCodeIdentifier(text))
            return;

        // Display strings start with an uppercase letter or a digit. Skip lowercase-leading
        // strings, those are usually internal parsing tokens (e.g., the "this month" /
        // "last 30 days" arms in ReportConfiguration's case-insensitive switch).
        if (!char.IsUpper(text[0]) && !char.IsDigit(text[0]))
            return;

        AddString(strings, text);
    }

    /// <summary>
    /// Returns true if the text looks like a PascalCase or camelCase code identifier
    /// (e.g., "InStock", "ShowCompanyAddress", "fontFamily"). Used to filter out
    /// strings captured by blanket regexes (const string, switch arms, array items)
    /// that match property/enum-value names rather than user-facing labels.
    /// </summary>
    private static bool LooksLikeCodeIdentifier(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains(' ')) return false;
        if (!text.All(char.IsLetterOrDigit)) return false;

        // A real identifier has a lowercase letter immediately followed by an uppercase letter.
        // Pure-uppercase abbreviations like "SKU" don't trigger and stay treated as display text.
        for (int i = 1; i < text.Length; i++)
        {
            if (char.IsUpper(text[i]) && char.IsLower(text[i - 1]))
                return true;
        }
        return false;
    }

    /// <summary>A sentence of display text: starts with a capital letter and has words.</summary>
    private static bool IsSentence(string text) =>
        char.IsUpper(text[0]) && text.Contains(' ') && !IsSvgPathData(text);

    /// <summary>
    /// Returns true if the text looks like SVG path data (e.g., "M10 20v-6h4v6...").
    /// </summary>
    private static bool IsSvgPathData(string text)
    {
        // SVG paths start with M/m (moveto) and contain path commands like v, h, l, z, a, c, s, q, etc.
        if (text.Length < 3 || (text[0] != 'M' && text[0] != 'm'))
            return false;

        // Check if the string is predominantly SVG path commands and numbers
        var svgCommandChars = 0;
        var totalChars = 0;
        foreach (var c in text)
        {
            if (c == ' ' || c == ',')
                continue;
            totalChars++;
            if (char.IsDigit(c) || c == '-' || c == '.' ||
                "MmZzLlHhVvCcSsQqTtAa".Contains(c))
                svgCommandChars++;
        }

        // If >90% of non-space characters are SVG path chars, it's likely SVG path data
        return totalChars > 0 && (double)svgCommandChars / totalChars > 0.9;
    }

    /// <summary>
    /// Interprets C# string-literal escape sequences in raw text captured from source files.
    /// Without this, a source string like "Line one.\n\nLine two" was captured by the regex
    /// as the literal characters "Line one.\n\nLine two" and serialized to JSON as "\\n\\n",
    /// which the JSON reader then decoded back to literal "\n" text, so the UI displayed
    /// "\n\n" instead of two newlines.
    /// </summary>
    private static string InterpretCSharpEscapes(string source)
    {
        if (string.IsNullOrEmpty(source) || source.IndexOf('\\') < 0)
            return source;

        var sb = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\\' && i + 1 < source.Length)
            {
                switch (source[i + 1])
                {
                    case 'n': sb.Append('\n'); i++; continue;
                    case 'r': sb.Append('\r'); i++; continue;
                    case 't': sb.Append('\t'); i++; continue;
                    case '\\': sb.Append('\\'); i++; continue;
                    case '"': sb.Append('"'); i++; continue;
                    case '\'': sb.Append('\''); i++; continue;
                    case '0': sb.Append('\0'); i++; continue;
                    // Other escapes (\\u####, \\x##, etc.) are uncommon in user-facing
                    // strings; pass them through untouched rather than risk mis-interpreting.
                }
            }
            sb.Append(source[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Adds a string to the collection if valid.
    /// </summary>
    private void AddString(Dictionary<string, string> strings, string text)
    {
        // Decode C# escape sequences captured as literal text by the regex extractors,
        // so newlines, tabs, and quotes round-trip correctly through the JSON output.
        text = InterpretCSharpEscapes(text);

        if (string.IsNullOrWhiteSpace(text))
            return;

        // Skip if it looks like a variable or placeholder
        if (text.StartsWith('{') || text.Contains("{{"))
            return;

        // Skip hex color codes (e.g., "#3B82F6", "#FFF")
        if (text.StartsWith('#') && text.Length >= 4 && text.Length <= 9 &&
            text[1..].All(c => char.IsAsciiHexDigit(c)))
            return;

        var key = LanguageService.GetStringKey(text);
        if (strings.TryGetValue(key, out var existing) && existing != text)
        {
            if (!KeyCollisions.TryGetValue(key, out var bucket))
            {
                bucket = new List<string> { existing };
                KeyCollisions[key] = bucket;
            }
            if (!bucket.Contains(text))
                bucket.Add(text);
            return;
        }
        strings.TryAdd(key, text);
    }

    /// <summary>
    /// Translates all collected strings to the specified languages, saving each language file immediately.
    /// Only translates strings that don't already exist in the output files, preserving progress across runs.
    /// </summary>
    /// <param name="englishStrings">Dictionary of key -> English text.</param>
    /// <param name="targetLanguages">List of target languages (names like "French" or ISO codes like "fr").</param>
    /// <param name="outputDirectory">Directory to save JSON files to after each language completes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task TranslateAllAsync(
        Dictionary<string, string> englishStrings,
        IEnumerable<string> targetLanguages,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var languageList = targetLanguages.ToList();
        var totalLanguages = languageList.Count;
        var currentLanguage = 0;

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        foreach (var language in languageList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            currentLanguage++;

            // Accept either language name ("French") or ISO code ("fr")
            string isoCode;
            string displayName;
            if (Languages.IsValidIsoCode(language))
            {
                isoCode = language;
                displayName = Languages.GetLanguageName(language);
            }
            else
            {
                isoCode = Languages.GetIsoCode(language);
                displayName = language;
            }

            if (isoCode == "en")
                continue;

            var filePath = Path.Combine(outputDirectory, $"{isoCode}.json");

            // Load existing translations to preserve previous work
            var existing = await LoadExistingTranslationsAsync(filePath);

            // Build full translation: reuse existing, identify new strings to translate
            var fullTranslation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var stringsToTranslate = new Dictionary<string, string>();

            foreach (var (key, englishText) in englishStrings)
            {
                if (existing.TryGetValue(key, out var existingTranslation) && !string.IsNullOrEmpty(existingTranslation))
                {
                    fullTranslation[key] = existingTranslation;
                }
                else
                {
                    stringsToTranslate[key] = englishText;
                }
            }

            // Count stale keys that exist in the file but are no longer in the source
            var staleCount = existing.Keys.Count(k => !englishStrings.ContainsKey(k));

            if (stringsToTranslate.Count == 0 && staleCount == 0)
            {
                ReportProgress($"Skipping {displayName} ({isoCode}) - already up to date", currentLanguage, totalLanguages);
                continue;
            }

            if (stringsToTranslate.Count > 0)
            {
                ReportProgress($"Translating {stringsToTranslate.Count} new strings to {displayName} ({isoCode})...", currentLanguage, totalLanguages);

                // Translate only the new strings
                var translations = await TranslateToLanguageAsync(stringsToTranslate, isoCode, cancellationToken);

                foreach (var (key, value) in translations)
                {
                    fullTranslation[key] = value;
                }

                LanguagesTranslated++;
            }

            if (staleCount > 0)
            {
                var staleKeys = existing.Keys.Where(k => !englishStrings.ContainsKey(k)).ToList();
                ReportProgress($"Removing {staleCount} stale key(s) from {displayName} ({isoCode}): {string.Join(", ", staleKeys.Take(5))}{(staleKeys.Count > 5 ? $" ... and {staleKeys.Count - 5} more" : "")}", currentLanguage, totalLanguages);
                StaleKeysRemoved += staleCount;
            }

            // Sort by key for deterministic output (avoids spurious git diffs)
            var sortedTranslation = fullTranslation
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            // Save the file (with new translations added and stale keys pruned)
            var json = JsonSerializer.Serialize(sortedTranslation, jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            ReportProgress($"Saved {isoCode}.json ({stringsToTranslate.Count} new, {staleCount} removed, {fullTranslation.Count} total)", currentLanguage, totalLanguages);
        }
    }

    /// <summary>
    /// Loads existing translations from a JSON file if it exists.
    /// </summary>
    private static async Task<Dictionary<string, string>> LoadExistingTranslationsAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (existing != null)
                return new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // If file is corrupt, start fresh
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Translates strings to a single language.
    /// </summary>
    private async Task<Dictionary<string, string>> TranslateToLanguageAsync(
        Dictionary<string, string> englishStrings,
        string targetIsoCode,
        CancellationToken cancellationToken)
    {
        var translations = new Dictionary<string, string>();
        var keyList = englishStrings.Keys.ToList();
        var textList = englishStrings.Values.ToList();

        // Batch the texts
        var batches = CreateBatches(textList);
        var processedCount = 0;

        Allowlist.TryGetValue(targetIsoCode, out var allowlistForLang);
        Allowlist.TryGetValue("*", out var globalAllowlist);

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var translatedBatch = await TranslateBatchAsync(batch, targetIsoCode, cancellationToken);

            // Map translations back to keys
            for (int i = 0; i < batch.Count && processedCount < keyList.Count; i++)
            {
                var key = keyList[processedCount];
                var sourceText = batch[i];
                var translatedText = i < translatedBatch.Count ? translatedBatch[i] : englishStrings[key];

                // Detect when Azure returned the source unchanged. Multi-word phrases that
                // come back identical are usually mis-detection by Azure (e.g., "Select Premium").
                // Allowlisted entries (legitimate loanwords like "Status" in Polish, or
                // global brand names / font families) are skipped.
                var isAllowlisted = (allowlistForLang?.Contains(sourceText) ?? false)
                                    || (globalAllowlist?.Contains(sourceText) ?? false);
                if (string.Equals(translatedText, sourceText, StringComparison.Ordinal) &&
                    sourceText.Contains(' ') &&
                    sourceText.Length > 2 &&
                    !isAllowlisted)
                {
                    if (!SuspiciousNoOps.TryGetValue(targetIsoCode, out var noOpList))
                    {
                        noOpList = new List<string>();
                        SuspiciousNoOps[targetIsoCode] = noOpList;
                    }
                    noOpList.Add(sourceText);
                }

                translations[key] = translatedText;
                processedCount++;
            }
        }

        return translations;
    }

    /// <summary>
    /// Creates batches of texts for API calls.
    /// </summary>
    private static List<List<string>> CreateBatches(List<string> texts)
    {
        var batches = new List<List<string>>();
        var currentBatch = new List<string>();
        var currentCharCount = 0;

        foreach (var text in texts)
        {
            if (currentBatch.Count >= BatchSize || currentCharCount + text.Length > MaxCharsPerBatch)
            {
                if (currentBatch.Count > 0)
                {
                    batches.Add(currentBatch);
                    currentBatch = new List<string>();
                    currentCharCount = 0;
                }
            }

            currentBatch.Add(text);
            currentCharCount += text.Length;
        }

        if (currentBatch.Count > 0)
        {
            batches.Add(currentBatch);
        }

        return batches;
    }

    // Max attempts when Azure returns 429 (Too Many Requests). F0 (free tier) has
    // tight per-minute throttles independent of the 2M-char monthly cap; S1 rarely
    // 429s but we honor it there too for resilience.
    private const int MaxRateLimitRetries = 6;

    /// <summary>
    /// Translates a batch of texts using Azure Translator API.
    /// Retries on 429 (Too Many Requests) by honoring the Retry-After header, or
    /// falling back to exponential backoff if the header is absent.
    /// </summary>
    private async Task<List<string>> TranslateBatchAsync(
        List<string> texts,
        string targetIsoCode,
        CancellationToken cancellationToken)
    {
        var route = $"/translate?api-version=3.0&from=en&to={targetIsoCode}";
        var uri = new Uri(AzureEndpoint + route);

        var requestBody = texts.Select(t => new { Text = t }).ToArray();
        var requestJson = JsonSerializer.Serialize(requestBody);

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < MaxRateLimitRetries; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            request.Headers.Add("Ocp-Apim-Subscription-Key", _azureKey);
            request.Headers.Add("Ocp-Apim-Subscription-Region", _azureRegion);

            response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                break;

            var delay = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date is { } when_
                            ? when_ - DateTimeOffset.UtcNow
                            : TimeSpan.FromSeconds(Math.Pow(2, attempt) * 5));
            // Clamp so a bogus header can't stall the tool for an hour or fire instantly.
            if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);
            if (delay > TimeSpan.FromMinutes(2)) delay = TimeSpan.FromMinutes(2);

            ReportProgress($"Rate limited by Azure (429). Waiting {delay.TotalSeconds:F0}s before retry {attempt + 2}/{MaxRateLimitRetries}...", 0, 0);
            response.Dispose();
            response = null;
            await Task.Delay(delay, cancellationToken);
        }

        if (response == null)
            throw new HttpRequestException($"Azure Translator API: rate-limited {MaxRateLimitRetries} times in a row. Wait a few minutes and re-run, or temporarily upgrade the Translator resource to S1.");

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Azure Translator API error ({response.StatusCode}): {errorBody}");
        }

        ApiCallCount++;
        TotalCharactersTranslated += texts.Sum(t => (long)t.Length);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseData = JsonSerializer.Deserialize<List<TranslationResponse>>(responseJson);

        if (responseData == null)
            throw new InvalidOperationException("Azure Translator API returned null response");

        return responseData
            .Select(r => r.translations?.FirstOrDefault()?.text ?? "")
            .ToList();
    }

    /// <summary>
    /// Reports progress.
    /// </summary>
    private void ReportProgress(string message, int current, int total)
    {
        Progress?.Invoke(this, new TranslationGeneratorProgressEventArgs(message, current, total));
    }

    // Response models for Azure API
    private class TranslationResponse
    {
        public List<Translation>? translations { get; set; }
    }

    private class Translation
    {
        public string? text { get; set; }
    }
}

/// <summary>
/// Progress event arguments for TranslationGenerator.
/// </summary>
public class TranslationGeneratorProgressEventArgs : EventArgs
{
    public string Message { get; }
    public int Current { get; }
    public int Total { get; }

    public TranslationGeneratorProgressEventArgs(string message, int current, int total)
    {
        Message = message;
        Current = current;
        Total = total;
    }
}
