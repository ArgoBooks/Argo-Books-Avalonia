using System.Net.Http.Json;
using System.Runtime.InteropServices;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Posts the app's survey answers to the website: where the person heard about
/// Argo Books, what they came to do, and what they say when asked on closing the
/// app with nothing recorded. Each updates the existing <c>app_first_run</c> row
/// for this machine on the server, so installs without a referral token can
/// still be attributed to a source.
///
/// Idempotency lives in <c>TutorialSettings</c> on the client and in
/// server-side <c>IS NULL</c> guards on each column.
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
    /// POSTs the survey answers: the source, the goal, or both, each null when it
    /// was not asked. Returns true on HTTP 2xx, false otherwise. Network errors
    /// are caught and logged.
    /// </summary>
    public Task<bool> ReportAsync(
        string? answer,
        string machineUuid,
        string? otherText = null,
        string? goal = null,
        string? goalOtherText = null,
        CancellationToken cancellationToken = default) =>
        PostAsync(new SurveyPayload
        {
            Event = "signup_survey",
            Platform = GetPlatformKey(),
            AppVersion = _appVersion,
            MachineUuid = machineUuid,
            Answer = answer,
            // The caller supplies the text only for a freeform option; the server stores it only for keys flagged freeform.
            OtherText = otherText,
            Goal = goal,
            GoalOtherText = goalOtherText,
        }, cancellationToken);

    /// <summary>
    /// POSTs what someone said when asked, on closing the app with nothing
    /// recorded, what they were hoping to do: a goal, a note on what got in the
    /// way, or both.
    /// </summary>
    public Task<bool> ReportExitAsync(
        string? goal,
        string? note,
        string machineUuid,
        CancellationToken cancellationToken = default) =>
        PostAsync(new SurveyPayload
        {
            Event = "exit_survey",
            Platform = GetPlatformKey(),
            AppVersion = _appVersion,
            MachineUuid = machineUuid,
            Answer = goal,
            OtherText = note,
        }, cancellationToken);

    private async Task<bool> PostAsync(SurveyPayload payload, CancellationToken cancellationToken)
    {
        try
        {
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
        public string? Answer { get; set; }

        [JsonPropertyName("other_text")]
        public string? OtherText { get; set; }

        [JsonPropertyName("goal")]
        public string? Goal { get; set; }

        [JsonPropertyName("goal_other_text")]
        public string? GoalOtherText { get; set; }
    }
}
