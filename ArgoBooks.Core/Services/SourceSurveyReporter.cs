using System.Net.Http.Json;
using System.Runtime.InteropServices;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Posts the post-onboarding "Where did you hear about Argo Books?" answer to
/// the website. The answer updates the existing <c>app_first_run</c> row for
/// this machine on the server, so installs without a referral token can still
/// be attributed to a source.
///
/// Idempotency lives in <c>TutorialSettings.SourceSurveyAnswer</c> on the
/// client and in a server-side <c>WHERE source_survey_answer IS NULL</c> guard.
/// </summary>
public sealed class SourceSurveyReporter
{
    private const string EndpointPath = "/api/track-app-event.php";

    private readonly HttpClient _httpClient;
    private readonly IErrorLogger? _errorLogger;
    private readonly string _appVersion;

    public SourceSurveyReporter(
        HttpClient httpClient,
        string appVersion,
        IErrorLogger? errorLogger = null)
    {
        _httpClient = httpClient;
        _errorLogger = errorLogger;
        _appVersion = appVersion;
    }

    /// <summary>
    /// POSTs the survey answer. Returns true on HTTP 2xx, false otherwise.
    /// Network errors are caught and logged.
    /// </summary>
    public async Task<bool> ReportAsync(
        string answer,
        string machineUuid,
        string? otherText = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new SurveyPayload
            {
                Event = "signup_survey",
                Platform = GetPlatformKey(),
                AppVersion = _appVersion,
                MachineUuid = machineUuid,
                Answer = answer,
                // The caller supplies otherText only for a freeform option; the
                // server stores it only for keys flagged freeform. Forwarding it
                // as-given keeps freeform working regardless of the option's key.
                OtherText = otherText,
            };

            var url = $"{ApiConfig.BaseUrl}{EndpointPath}";
            using var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _errorLogger?.LogWarning(
                    $"SourceSurveyReporter received HTTP {(int)response.StatusCode}",
                    context: "SourceSurveyReporter.ReportAsync");
                return false;
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine caller cancellation: propagate so callers can stop cleanly
            // (matches the convention in other ArgoBooks.Core HTTP services).
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout (not a caller cancellation): treat as a normal
            // failed report so the caller can move on.
            return false;
        }
        catch (Exception ex)
        {
            NetworkFailure.Report(_errorLogger, ex, "SourceSurveyReporter.ReportAsync");
            return false;
        }
    }

    private static string GetPlatformKey()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return "mac";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return "linux";
        return "unknown";
    }

    private sealed class SurveyPayload
    {
        [JsonPropertyName("event")]
        public string Event { get; set; } = string.Empty;

        [JsonPropertyName("platform")]
        public string Platform { get; set; } = string.Empty;

        [JsonPropertyName("app_version")]
        public string AppVersion { get; set; } = string.Empty;

        [JsonPropertyName("machine_uuid")]
        public string MachineUuid { get; set; } = string.Empty;

        [JsonPropertyName("answer")]
        public string Answer { get; set; } = string.Empty;

        [JsonPropertyName("other_text")]
        public string? OtherText { get; set; }
    }
}
