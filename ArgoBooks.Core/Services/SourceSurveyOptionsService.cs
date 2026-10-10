using System.Net.Http.Json;

namespace ArgoBooks.Core.Services;

/// <summary>
/// A single answer option for a survey question.
/// <paramref name="Key"/> is the stable identifier POSTed to and stored by the
/// server; <paramref name="Label"/> is the (English) display text;
/// <paramref name="Freeform"/> marks the option that reveals the freeform text box.
/// </summary>
public sealed record SurveyOption(string Key, string Label, bool Freeform);

/// <summary>
/// The choices for the app's two questions: where the person heard about Argo Books, and what
/// they came to do.
/// </summary>
public sealed record SurveyChoices(IReadOnlyList<SurveyOption> Sources, IReadOnlyList<SurveyOption> Goals);

/// <summary>
/// Fetches the survey choices from the website so new ones (e.g. a newly
/// launched platform) appear in already-installed apps without a release. On
/// any failure the service returns the bundled default lists, so the survey
/// always renders even offline.
/// </summary>
public sealed class SourceSurveyOptionsService
{
    private const string EndpointPath = "/api/survey-options.php";

    private readonly HttpClient _httpClient;
    private readonly IErrorLogger? _errorLogger;

    /// <summary>
    /// The options baked into the app. Used as a fallback when the server is
    /// unreachable or returns an unusable payload. Keys must stay in sync with
    /// the server's <c>config/survey-options.json</c> for consistent reporting.
    /// </summary>
    public static IReadOnlyList<SurveyOption> DefaultOptions { get; } = new[]
    {
        new SurveyOption("google",      "Google",       false),
        new SurveyOption("bing",        "Bing",         false),
        new SurveyOption("youtube",     "YouTube",      false),
        new SurveyOption("reddit",      "Reddit",       false),
        new SurveyOption("friend",      "A friend",     false),
        new SurveyOption("email",       "Email",        false),
        new SurveyOption("capterra",    "Capterra",     false),
        new SurveyOption("producthunt", "Product Hunt", false),
        new SurveyOption("microsoftstore", "Microsoft Store", false),
        new SurveyOption("homebrew",    "Homebrew",      false),
        new SurveyOption("other",       "Other",        true),
    };

    /// <summary>
    /// The goals baked into the app, used the same way as <see cref="DefaultOptions"/>. Keys must
    /// stay in sync with the "goals" list in the server's <c>config/survey-options.json</c>.
    /// </summary>
    public static IReadOnlyList<SurveyOption> DefaultGoals { get; } = new[]
    {
        new SurveyOption("invoices",  "Send invoices and get paid",            false),
        new SurveyOption("track",     "Track income and expenses",             false),
        new SurveyOption("receipts",  "Scan and store receipts",               false),
        new SurveyOption("switch",    "Move from QuickBooks or a spreadsheet", false),
        new SurveyOption("inventory", "Manage stock or rentals",               false),
        new SurveyOption("payroll",   "Run payroll",                           false),
        new SurveyOption("looking",   "Just looking around",                   false),
        new SurveyOption("other",     "Something else",                        true),
    };

    /// <summary>Both bundled lists, returned whenever the server cannot be used.</summary>
    public static SurveyChoices Defaults { get; } = new(DefaultOptions, DefaultGoals);

    public SourceSurveyOptionsService(HttpClient httpClient, IErrorLogger? errorLogger = null)
    {
        _httpClient = httpClient;
        _errorLogger = errorLogger;
    }

    /// <summary>
    /// GETs the survey choices from the website. Returns the parsed server lists
    /// on success, or <see cref="Defaults"/> on any failure (network error,
    /// timeout, non-2xx, malformed JSON, or an empty source list). A response
    /// with no usable goals, as an older server gives, keeps its sources and takes
    /// the bundled goals. Only throws <see cref="OperationCanceledException"/>
    /// when the caller cancels <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task<SurveyChoices> GetChoicesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{ApiConfig.BaseUrl}{EndpointPath}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _errorLogger?.LogWarning(
                    $"SourceSurveyOptionsService received HTTP {(int)response.StatusCode}",
                    context: "SourceSurveyOptionsService.GetChoicesAsync");
                return Defaults;
            }

            var payload = await response.Content.ReadFromJsonAsync<OptionsPayload>(cancellationToken);
            var sources = Parse(payload?.Options);
            if (sources.Count == 0)
                return Defaults;

            var goals = Parse(payload?.Goals);
            return new SurveyChoices(sources, goals.Count > 0 ? goals : DefaultGoals);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine caller cancellation: propagate so callers can stop cleanly
            // (matches the convention in other ArgoBooks.Core HTTP services).
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout (not a caller cancellation): fall back like any
            // other transient failure so the survey still renders.
            return Defaults;
        }
        catch (Exception ex)
        {
            NetworkFailure.Report(_errorLogger, ex, "SourceSurveyOptionsService.GetChoicesAsync");
            return Defaults;
        }
    }

    private static List<SurveyOption> Parse(List<OptionDto>? options)
    {
        var parsed = new List<SurveyOption>(options?.Count ?? 0);
        foreach (var o in options ?? [])
        {
            if (string.IsNullOrWhiteSpace(o.Key) || string.IsNullOrWhiteSpace(o.Label))
                continue;
            parsed.Add(new SurveyOption(o.Key!, o.Label!, o.Freeform));
        }
        return parsed;
    }

    private sealed class OptionsPayload
    {
        [JsonPropertyName("options")]
        public List<OptionDto>? Options { get; set; }

        [JsonPropertyName("goals")]
        public List<OptionDto>? Goals { get; set; }
    }

    private sealed class OptionDto
    {
        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("freeform")]
        public bool Freeform { get; set; }
    }
}
