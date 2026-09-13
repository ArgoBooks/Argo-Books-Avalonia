namespace ArgoBooks.Data;

/// <summary>
/// Provides a shared list of supported languages with their ISO codes.
/// </summary>
public static class Languages
{
    /// <summary>
    /// Priority/common languages shown at the top of dropdowns.
    /// </summary>
    public static readonly IReadOnlyList<string> Priority =
    [
        "English",
        "French",
        "German",
        "Italian"
    ];

    /// <summary>
    /// Complete list of supported languages.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        "Albanian",
        "Arabic",
        "Basque",
        "Belarusian",
        "Bengali",
        "Bosnian",
        "Bulgarian",
        "Catalan",
        "Chinese (Simplified)",
        "Chinese (Traditional)",
        "Croatian",
        "Czech",
        "Danish",
        "Dutch",
        "English",
        "Estonian",
        "Filipino",
        "Finnish",
        "French",
        "Galician",
        "German",
        "Greek",
        "Hebrew",
        "Hindi",
        "Hungarian",
        "Icelandic",
        "Indonesian",
        "Irish",
        "Italian",
        "Japanese",
        "Korean",
        "Latvian",
        "Lithuanian",
        "Luxembourgish",
        "Macedonian",
        "Malay",
        "Maltese",
        "Norwegian",
        "Persian",
        "Polish",
        "Portuguese",
        "Romanian",
        "Russian",
        "Serbian",
        "Slovak",
        "Slovenian",
        "Spanish",
        "Swahili",
        "Swedish",
        "Thai",
        "Turkish",
        "Ukrainian",
        "Urdu",
        "Vietnamese"
    ];

    /// <summary>
    /// Mapping from language name to ISO 639-1/639-2 language code.
    /// Used for translation API calls and file naming.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ToIsoCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Albanian"] = "sq",
        ["Arabic"] = "ar",
        ["Basque"] = "eu",
        ["Belarusian"] = "be",
        ["Bengali"] = "bn",
        ["Bosnian"] = "bs",
        ["Bulgarian"] = "bg",
        ["Catalan"] = "ca",
        ["Chinese (Simplified)"] = "zh-Hans",
        ["Chinese (Traditional)"] = "zh-Hant",
        ["Croatian"] = "hr",
        ["Czech"] = "cs",
        ["Danish"] = "da",
        ["Dutch"] = "nl",
        ["English"] = "en",
        ["Estonian"] = "et",
        ["Filipino"] = "fil",
        ["Finnish"] = "fi",
        ["French"] = "fr",
        ["Galician"] = "gl",
        ["German"] = "de",
        ["Greek"] = "el",
        ["Hebrew"] = "he",
        ["Hindi"] = "hi",
        ["Hungarian"] = "hu",
        ["Icelandic"] = "is",
        ["Indonesian"] = "id",
        ["Irish"] = "ga",
        ["Italian"] = "it",
        ["Japanese"] = "ja",
        ["Korean"] = "ko",
        ["Latvian"] = "lv",
        ["Lithuanian"] = "lt",
        ["Luxembourgish"] = "lb",
        ["Macedonian"] = "mk",
        ["Malay"] = "ms",
        ["Maltese"] = "mt",
        ["Norwegian"] = "no",
        ["Persian"] = "fa",
        ["Polish"] = "pl",
        ["Portuguese"] = "pt",
        ["Romanian"] = "ro",
        ["Russian"] = "ru",
        ["Serbian"] = "sr",
        ["Slovak"] = "sk",
        ["Slovenian"] = "sl",
        ["Spanish"] = "es",
        ["Swahili"] = "sw",
        ["Swedish"] = "sv",
        ["Thai"] = "th",
        ["Turkish"] = "tr",
        ["Ukrainian"] = "uk",
        ["Urdu"] = "ur",
        ["Vietnamese"] = "vi"
    };

    /// <summary>
    /// Mapping from ISO code back to language name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> FromIsoCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sq"] = "Albanian",
        ["ar"] = "Arabic",
        ["eu"] = "Basque",
        ["be"] = "Belarusian",
        ["bn"] = "Bengali",
        ["bs"] = "Bosnian",
        ["bg"] = "Bulgarian",
        ["ca"] = "Catalan",
        ["zh-Hans"] = "Chinese (Simplified)",
        ["zh-Hant"] = "Chinese (Traditional)",
        ["hr"] = "Croatian",
        ["cs"] = "Czech",
        ["da"] = "Danish",
        ["nl"] = "Dutch",
        ["en"] = "English",
        ["et"] = "Estonian",
        ["fil"] = "Filipino",
        ["fi"] = "Finnish",
        ["fr"] = "French",
        ["gl"] = "Galician",
        ["de"] = "German",
        ["el"] = "Greek",
        ["he"] = "Hebrew",
        ["hi"] = "Hindi",
        ["hu"] = "Hungarian",
        ["is"] = "Icelandic",
        ["id"] = "Indonesian",
        ["ga"] = "Irish",
        ["it"] = "Italian",
        ["ja"] = "Japanese",
        ["ko"] = "Korean",
        ["lv"] = "Latvian",
        ["lt"] = "Lithuanian",
        ["lb"] = "Luxembourgish",
        ["mk"] = "Macedonian",
        ["ms"] = "Malay",
        ["mt"] = "Maltese",
        ["no"] = "Norwegian",
        ["fa"] = "Persian",
        ["pl"] = "Polish",
        ["pt"] = "Portuguese",
        ["ro"] = "Romanian",
        ["ru"] = "Russian",
        ["sr"] = "Serbian",
        ["sk"] = "Slovak",
        ["sl"] = "Slovenian",
        ["es"] = "Spanish",
        ["sw"] = "Swahili",
        ["sv"] = "Swedish",
        ["th"] = "Thai",
        ["tr"] = "Turkish",
        ["uk"] = "Ukrainian",
        ["ur"] = "Urdu",
        ["vi"] = "Vietnamese"
    };

    /// <summary>
    /// Gets the ISO code for a language name. Returns "en" if not found.
    /// </summary>
    /// <param name="languageName">The language name (e.g., "English", "French").</param>
    /// <returns>The ISO code (e.g., "en", "fr").</returns>
    public static string GetIsoCode(string languageName)
    {
        if (string.IsNullOrEmpty(languageName))
            return "en";

        return ToIsoCode.TryGetValue(languageName, out var code) ? code : "en";
    }

    /// <summary>
    /// Gets the language name for an ISO code. Returns "English" if not found.
    /// </summary>
    /// <param name="isoCode">The ISO code (e.g., "en", "fr").</param>
    /// <returns>The language name (e.g., "English", "French").</returns>
    public static string GetLanguageName(string isoCode)
    {
        if (string.IsNullOrEmpty(isoCode))
            return "English";

        return FromIsoCode.TryGetValue(isoCode, out var name) ? name : "English";
    }

    /// <summary>
    /// Checks if a language name is valid (exists in the supported languages list).
    /// </summary>
    /// <param name="languageName">The language name to check.</param>
    /// <returns>True if the language is supported.</returns>
    public static bool IsValidLanguage(string languageName)
    {
        return !string.IsNullOrEmpty(languageName) && ToIsoCode.ContainsKey(languageName);
    }

    /// <summary>
    /// Checks if an ISO code is valid (exists in the supported languages list).
    /// </summary>
    /// <param name="isoCode">The ISO code to check.</param>
    /// <returns>True if the language code is supported.</returns>
    public static bool IsValidIsoCode(string isoCode)
    {
        return !string.IsNullOrEmpty(isoCode) && FromIsoCode.ContainsKey(isoCode);
    }

    /// <summary>
    /// The language a machine set to <paramref name="cultureName"/> should start in, or null when
    /// none of ours matches. Windows reports tags like "fil-PH", "zh-Hant-TW" and "nb-NO", so the
    /// whole primary subtag is looked up before its first two letters: "fil" shortened to "fi"
    /// would offer a Filipino speaker the app in Finnish.
    /// </summary>
    public static string? MatchSystemLanguage(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return null;

        var parts = cultureName.Trim().Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        var language = parts[0].ToLowerInvariant();

        if (language == "zh")
            return ChineseFor(parts);

        // Bokmal and Nynorsk are both written Norwegian, and we have the one translation.
        if (language is "nb" or "nn")
            return "Norwegian";

        if (FromIsoCode.TryGetValue(language, out var exact))
            return exact;

        return language.Length > 2 && FromIsoCode.TryGetValue(language[..2], out var shortened)
            ? shortened
            : null;
    }

    /// <summary>
    /// Simplified or Traditional: the script when the tag names one, else the script the region writes.
    /// </summary>
    private static string ChineseFor(string[] parts)
    {
        foreach (var part in parts.Skip(1))
        {
            if (part.Equals("Hant", StringComparison.OrdinalIgnoreCase))
                return "Chinese (Traditional)";
            if (part.Equals("Hans", StringComparison.OrdinalIgnoreCase))
                return "Chinese (Simplified)";
            if (part.Equals("TW", StringComparison.OrdinalIgnoreCase)
                || part.Equals("HK", StringComparison.OrdinalIgnoreCase)
                || part.Equals("MO", StringComparison.OrdinalIgnoreCase))
                return "Chinese (Traditional)";
        }

        return "Chinese (Simplified)";
    }
}
