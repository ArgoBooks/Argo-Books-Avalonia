using System.Net.Http.Json;
using System.Text.Json;
using ArgoBooks.Data;
using ArgoBooks.Translations;

// Translation Generator Tool
// Usage: dotnet run -- [options]
//
// Options:
//   --translate         Translate to all languages (requires Azure API key)
//   --languages fr,de   Translate to specific languages only
//                       Pass --languages en to rebuild en.json and stop: English is
//                       skipped by the translation loop, so nothing else is touched
//                       and no API key is needed
//   --output <path>     Output directory for JSON files
//   --source <path>     Source directory to scan (default: ../ArgoBooks)
//   --retry-same        Clear translations matching the English source (excluding allowlist)
//                       so Azure re-translates them on the next run
//   --yes               Skip confirmation prompt

Console.WriteLine("=== Argo Books Translation Generator ===\n");

// Parse command line arguments (args is provided by top-level statements)
var retrySame = args.Contains("--retry-same");
var skipConfirmation = args.Contains("--yes") || args.Contains("-y");
var specificLanguages = new List<string>();
var outputDir = "./languages";
var sourceDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "ArgoBooks"));
var allowlistPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "translation-allowlist.json"));

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--languages" && i + 1 < args.Length)
    {
        specificLanguages.AddRange(args[i + 1].Split(',').Select(l => l.Trim()));
    }
    else if (args[i] == "--output" && i + 1 < args.Length)
    {
        outputDir = args[i + 1];
    }
    else if (args[i] == "--source" && i + 1 < args.Length)
    {
        sourceDir = args[i + 1];
    }
}

// Load .env file for API keys
EnvFile.Load();

// Get Azure API key from environment
var azureKey = Environment.GetEnvironmentVariable("AZURE_TRANSLATOR_KEY");
var azureRegion = Environment.GetEnvironmentVariable("AZURE_TRANSLATOR_REGION") ?? "eastus";

// Fetch USD to CAD exchange rate for cost display
var usdToCad = await GetUsdToCadRateAsync();

Console.WriteLine($"Source directory: {sourceDir}");
Console.WriteLine($"Output directory: {outputDir}");
Console.WriteLine();

if (!Directory.Exists(sourceDir))
{
    Console.WriteLine($"ERROR: Source directory not found: {sourceDir}");
    Console.WriteLine("Use --source <path> to specify the ArgoBooks source directory.");
    return 1;
}

// Create output directory
Directory.CreateDirectory(outputDir);

// Create generator
var generator = new TranslationGenerator(azureKey ?? "", azureRegion);
generator.Progress += (_, e) => Console.WriteLine($"  {e.Message}");

// Load allowlist of source strings that legitimately stay unchanged in the target.
// Suppresses no-op warnings and protects them from --retry-same.
// Format: { "*": [...] } applies globally; { "<iso>": [...] } adds language-specific entries.
if (File.Exists(allowlistPath))
{
    try
    {
        var allowlistJson = await File.ReadAllTextAsync(allowlistPath);
        var allowlistDoc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(allowlistJson);
        if (allowlistDoc != null)
        {
            foreach (var (iso, element) in allowlistDoc)
            {
                if (iso.StartsWith("_") || element.ValueKind != JsonValueKind.Array) continue;
                var entries = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in element.EnumerateArray())
                {
                    var s = entry.GetString();
                    if (!string.IsNullOrEmpty(s)) entries.Add(s);
                }
                generator.Allowlist[iso] = entries;
            }
            var globalCount = generator.Allowlist.TryGetValue("*", out var g) ? g.Count : 0;
            var perLangCount = generator.Allowlist.Where(kv => kv.Key != "*").Sum(kv => kv.Value.Count);
            Console.WriteLine($"Loaded allowlist: {globalCount} global + {perLangCount} language-specific entries.\n");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: failed to read allowlist at {allowlistPath}: {ex.Message}\n");
    }
}

// Step 1: Collect strings
Console.WriteLine("Step 1: Collecting translatable strings...");
var strings = generator.CollectStrings(sourceDir);
Console.WriteLine($"\nFound {strings.Count} translatable strings.\n");

// Warn about key collisions caused by 50-char truncation in GetStringKey.
// Only the first source text wins; collided strings won't get translated separately.
// Suppress "safe" collisions where all variants normalize to the same letters/digits
// (e.g., "Tax ($)" vs "Tax", "Select Logo..." vs "Select Logo", "ID" vs "Id"), those
// share a sensible single translation by design.
if (generator.KeyCollisions.Count > 0)
{
    static string LooseNormalize(string s) =>
        new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    var realCollisions = generator.KeyCollisions
        .Where(kv => kv.Value.Select(LooseNormalize).Distinct().Count() > 1)
        .ToList();

    var benignCount = generator.KeyCollisions.Count - realCollisions.Count;

    if (realCollisions.Count > 0)
    {
        Console.WriteLine("[WARN] Key collisions detected (50-char truncation produced duplicate keys):");
        foreach (var (key, sources) in realCollisions)
        {
            Console.WriteLine($"  Key '{key}' is shared by:");
            foreach (var s in sources)
                Console.WriteLine($"    - {s}");
        }
        Console.WriteLine("Shorten one of the conflicting source strings to disambiguate.\n");
    }

    if (benignCount > 0)
        Console.WriteLine($"[INFO] {benignCount} benign collision(s) suppressed (variants share a sensible single translation).\n");
}

// Save English strings
var englishPath = Path.Combine(outputDir, "en.json");
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
// Sort by key for deterministic output (avoids spurious git diffs)
var sortedStrings = strings
    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
await File.WriteAllTextAsync(englishPath, JsonSerializer.Serialize(sortedStrings, options));
Console.WriteLine($"Saved English strings to: {englishPath}\n");

Console.WriteLine("Sample strings found:");
foreach (var kvp in strings.Take(10))
{
    Console.WriteLine($"  {kvp.Key}: \"{kvp.Value}\"");
}
if (strings.Count > 10)
{
    Console.WriteLine($"  ... and {strings.Count - 10} more\n");
}

// Step 2: Translate
var languagesToTranslate = specificLanguages.Count > 0
    ? specificLanguages
    : Languages.All.Where(l => l != "English").ToList();

// Resolve ISO codes for each language
var resolvedLanguages = new List<(string isoCode, string displayName)>();
foreach (var lang in languagesToTranslate)
{
    if (Languages.IsValidIsoCode(lang))
        resolvedLanguages.Add((lang, Languages.GetLanguageName(lang)));
    else
        resolvedLanguages.Add((Languages.GetIsoCode(lang), lang));
}

// Optionally clear same-as-source translations so they get retried.
// Useful when Azure previously returned the English source unchanged.
if (retrySame)
{
    Console.WriteLine("Clearing same-as-source translations (--retry-same)...");
    var totalCleared = 0;
    var jsonOut = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    foreach (var (isoCode, _) in resolvedLanguages)
    {
        if (isoCode == "en") continue;
        var langFile = Path.Combine(outputDir, $"{isoCode}.json");
        if (!File.Exists(langFile)) continue;

        Dictionary<string, string>? existing;
        try
        {
            existing = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(langFile));
        }
        catch
        {
            continue;
        }
        if (existing == null) continue;

        generator.Allowlist.TryGetValue(isoCode, out var allowlistForLang);
        generator.Allowlist.TryGetValue("*", out var globalAllowlist);
        var clearedHere = 0;
        foreach (var (key, sourceText) in strings)
        {
            var isAllowlisted = (allowlistForLang?.Contains(sourceText) ?? false)
                                || (globalAllowlist?.Contains(sourceText) ?? false);
            if (existing.TryGetValue(key, out var translated) &&
                string.Equals(translated, sourceText, StringComparison.Ordinal) &&
                sourceText.Contains(' ') &&
                sourceText.Length > 2 &&
                !isAllowlisted)
            {
                existing[key] = "";
                clearedHere++;
            }
        }
        if (clearedHere > 0)
        {
            var sortedExisting = existing
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(langFile, JsonSerializer.Serialize(sortedExisting, jsonOut));
            Console.WriteLine($"  {isoCode}: cleared {clearedHere} same-as-source entries");
            totalCleared += clearedHere;
        }
    }
    Console.WriteLine($"Cleared {totalCleared} entries total. They will be re-translated below.\n");
}

// Check existing translations and count new vs existing vs stale per language
Console.WriteLine($"\nStep 2: Checking existing translations...");
var totalNewStrings = 0;
var totalExistingStrings = 0;
var totalStaleStrings = 0;
var totalCharsToTranslate = 0L;

foreach (var (isoCode, displayName) in resolvedLanguages)
{
    if (isoCode == "en") continue;

    var existingFile = Path.Combine(outputDir, $"{isoCode}.json");
    var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (File.Exists(existingFile))
    {
        try
        {
            var json = await File.ReadAllTextAsync(existingFile);
            var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (existing != null)
            {
                foreach (var kvp in existing)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                        existingKeys.Add(kvp.Key);
                }
            }
        }
        catch { /* ignore parse errors */ }
    }

    var newCount = strings.Keys.Count(k => !existingKeys.Contains(k));
    var existingCount = strings.Keys.Count(k => existingKeys.Contains(k));
    var staleCount = existingKeys.Count(k => !strings.ContainsKey(k));
    var newChars = strings.Where(kvp => !existingKeys.Contains(kvp.Key)).Sum(kvp => (long)kvp.Value.Length);

    totalNewStrings += newCount;
    totalExistingStrings += existingCount;
    totalStaleStrings += staleCount;
    totalCharsToTranslate += newChars;

    if (existingKeys.Count > 0)
    {
        var parts = new List<string> { $"{newCount} new", $"{existingCount} existing" };
        if (staleCount > 0)
            parts.Add($"{staleCount} stale");
        Console.WriteLine($"  {displayName} ({isoCode}): {string.Join(", ", parts)}");
    }
    else
        Console.WriteLine($"  {displayName} ({isoCode}): {strings.Count} new (no existing file)");
}

var nonEnglishCount = resolvedLanguages.Count(l => l.isoCode != "en");

// If nothing new to translate and no stale keys to remove, skip entirely
if (totalNewStrings == 0 && totalStaleStrings == 0)
{
    Console.WriteLine($"\nAll {totalExistingStrings} translations are up to date. Nothing to do.");
    return 0;
}

// Cost estimate based only on new characters that need translating
var estimatedCostUsd = totalCharsToTranslate / 1_000_000m * 10m;

Console.WriteLine();
if (totalNewStrings > 0)
{
    Console.WriteLine($"New strings to translate: {totalNewStrings} across {nonEnglishCount} language(s)");
    Console.WriteLine($"Characters to translate:  {totalCharsToTranslate:N0}");
    Console.WriteLine($"Estimated cost:           {FormatCost(estimatedCostUsd, usdToCad)}");
}
if (totalStaleStrings > 0)
    Console.WriteLine($"Stale keys to remove:     {totalStaleStrings} across {nonEnglishCount} language(s)");
Console.WriteLine();

// Azure API key is required only when there are new strings to translate
if (totalNewStrings > 0 && string.IsNullOrEmpty(azureKey))
{
    Console.WriteLine("ERROR: AZURE_TRANSLATOR_KEY environment variable not set.\n");
    Console.WriteLine("Set the environment variable and try again:");
    Console.WriteLine("  PowerShell:  $env:AZURE_TRANSLATOR_KEY = \"your-api-key\"");
    Console.WriteLine("  Cmd:         set AZURE_TRANSLATOR_KEY=your-api-key");
    Console.WriteLine("  Bash:        export AZURE_TRANSLATOR_KEY=your-api-key\n");
    Console.WriteLine("Or use --languages en to rebuild en.json without translating.");
    return 1;
}

// Confirmation prompt (skip for stale-key-only cleanup since it's free)
if (!skipConfirmation && totalNewStrings > 0)
{
    Console.Write("Proceed with translation? [y/N] ");
    var response = Console.ReadLine()?.Trim().ToLowerInvariant();
    if (response != "y" && response != "yes")
    {
        Console.WriteLine("Translation cancelled.");
        return 0;
    }
    Console.WriteLine();
}

var action = totalNewStrings > 0 ? "Translating" : "Cleaning up";
Console.WriteLine($"{action} {nonEnglishCount} languages...");
Console.WriteLine("Languages: " + string.Join(", ", resolvedLanguages.Where(l => l.isoCode != "en").Select(l => l.displayName).Take(10)) +
    (nonEnglishCount > 10 ? $" ... and {nonEnglishCount - 10} more" : ""));
Console.WriteLine();

try
{
    await generator.TranslateAllAsync(
        strings,
        languagesToTranslate,
        outputDir,
        CancellationToken.None);

    Console.WriteLine($"\nDone! Translations saved to: {outputDir}");
    if (generator.LanguagesTranslated > 0)
        Console.WriteLine($"Translated {generator.LanguagesTranslated} languages.");
    if (generator.StaleKeysRemoved > 0)
        Console.WriteLine($"Removed {generator.StaleKeysRemoved} stale key(s) across all languages.");

    // Print Azure API usage summary (only if API was used)
    if (generator.ApiCallCount > 0)
    {
        Console.WriteLine();
        Console.WriteLine("--- Azure Translator API Usage ---");
        Console.WriteLine($"  Languages translated:  {generator.LanguagesTranslated}");
        Console.WriteLine($"  API calls made:        {generator.ApiCallCount}");
        Console.WriteLine($"  Characters translated: {generator.TotalCharactersTranslated:N0}");
        Console.WriteLine($"  Estimated cost:        {FormatCost(generator.EstimatedCost, usdToCad)}");
        Console.WriteLine("----------------------------------");
    }

    // Print suspicious no-op summary so the user can review and allowlist
    if (generator.SuspiciousNoOps.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("--- Suspicious no-op translations ---");
        Console.WriteLine("Azure returned the source unchanged for these strings.");
        Console.WriteLine("Add legitimate ones to translation-allowlist.json; rerun with --retry-same to retry the rest.");
        foreach (var (iso, list) in generator.SuspiciousNoOps.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"  {iso}: {list.Count} entr{(list.Count == 1 ? "y" : "ies")}");
            foreach (var src in list.Take(5))
                Console.WriteLine($"    - {src}");
            if (list.Count > 5)
                Console.WriteLine($"    ... and {list.Count - 5} more");
        }
        Console.WriteLine("-------------------------------------");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\nERROR: Translation failed: {ex.Message}");
    if (generator.LanguagesTranslated > 0)
        Console.WriteLine($"  {generator.LanguagesTranslated} language(s) were saved before the error. Re-run to continue.");
    return 1;
}

return 0;

// --- Helper functions ---

static string FormatCost(decimal costUsd, decimal? usdToCad)
{
    if (usdToCad.HasValue)
    {
        var costCad = costUsd * usdToCad.Value;
        return $"${costCad:F4} CAD (${costUsd:F4} USD @ {usdToCad.Value:F4})";
    }
    return $"${costUsd:F4} USD (S1 @ $10 USD/1M chars)";
}

static async Task<decimal?> GetUsdToCadRateAsync()
{
    var oxrKey = Environment.GetEnvironmentVariable("OPENEXCHANGERATES_API_KEY");
    if (string.IsNullOrEmpty(oxrKey))
        return null;

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var url = $"https://openexchangerates.org/api/latest.json?app_id={oxrKey}&symbols=CAD";
        var response = await http.GetFromJsonAsync<JsonElement>(url);
        if (response.TryGetProperty("rates", out var rates) && rates.TryGetProperty("CAD", out var cad))
            return cad.GetDecimal();
    }
    catch
    {
        // Silently fall back to USD-only display
    }
    return null;
}
