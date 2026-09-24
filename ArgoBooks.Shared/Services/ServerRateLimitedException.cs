using System.Text.Json;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The Argo server refused a request because this device or connection has made too many in a
/// short time. Kept apart from other failures so the user is told to wait, rather than that their
/// file or code is bad. An HttpRequestException, so code that already treats a failed request as
/// transient keeps doing so without knowing about this type.
/// </summary>
public sealed class ServerRateLimitedException(string message) : HttpRequestException(message)
{
    private const string DefaultMessage = "Too many requests in a short time. Please wait a few minutes and try again.";

    /// <summary>
    /// Builds the exception from a 429 body, keeping the server's own wording when it has one:
    /// that wording states how long the wait actually is.
    /// </summary>
    public static ServerRateLimitedException FromBody(string? body)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(message.GetString()))
                {
                    return new ServerRateLimitedException(message.GetString()!);
                }
            }
            catch (JsonException)
            {
                // A proxy error page, not our JSON. Fall through to the default wording.
            }
        }

        return new ServerRateLimitedException(DefaultMessage);
    }
}
