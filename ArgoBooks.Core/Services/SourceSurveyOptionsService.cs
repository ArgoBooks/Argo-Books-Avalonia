using System.Net.Http.Json;

namespace ArgoBooks.Core.Services;

/// <summary>
/// A single answer option for the "Where did you hear about Argo Books?" survey.
/// <paramref name="Key"/> is the stable identifier POSTed to and stored by the
/// server; <paramref name="Label"/> is the (English) display text;
/// <paramref name="Freeform"/> marks the option that reveals the freeform text box.
/// </summary>
public sealed record SurveyOption(string Key, string Label, bool Freeform);

/// <summary>
/// Fetches the source-survey answer options from the website so new options
/// (e.g. a newly launched platform) appear in already-installed apps without a
/// release. On any failure the service returns a bundled default list, so the
/// survey always renders even offline.
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
        new SurveyOption("other",       "Other",        true),
    };

    public SourceSurveyOptionsService(HttpClient httpClient, IErrorLogger? errorLogger = null)
    {
        _httpClient = httpClient;
        _errorLogger = errorLogger;
    }

    /// <summary>
    /// GETs the survey options from the website. Returns the parsed server list
    /// on success, or <see cref="DefaultOptions"/> on any failure (network error,
    /// timeout, non-2xx, malformed JSON, or empty list). Only throws
    /// <see cref="OperationCanceledException"/> when the caller cancels
    /// <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task<IReadOnlyList<SurveyOption>> GetOptionsAsync(
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
                    context: "SourceSurveyOptionsService.GetOptionsAsync");
                return DefaultOptions;
            }

            var payload = await response.Content.ReadFromJsonAsync<OptionsPayload>(cancellationToken);
            var options = payload?.Options;
            if (options == null || options.Count == 0)
                return DefaultOptions;

            var parsed = new List<SurveyOption>(options.Count);
            foreach (var o in options)
            {
                if (string.IsNullOrWhiteSpace(o.Key) || string.IsNullOrWhiteSpace(o.Label))
                    continue;
                parsed.Add(new SurveyOption(o.Key!, o.Label!, o.Freeform));
            }

            return parsed.Count > 0 ? parsed : DefaultOptions;
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
            return DefaultOptions;
        }
        catch (Exception ex)
        {
            NetworkFailure.Report(_errorLogger, ex, "SourceSurveyOptionsService.GetOptionsAsync");
            return DefaultOptions;
        }
    }

    private sealed class OptionsPayload
    {
        [JsonPropertyName("options")]
        public List<OptionDto>? Options { get; set; }
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
