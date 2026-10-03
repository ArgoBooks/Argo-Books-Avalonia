using System.Text.Json;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The Argo server refused a PDF statement extraction, or could not complete it.
///
/// Kept apart from an extraction that ran and found nothing, because the two need opposite
/// answers: one means the statement may not be a statement, the other means the file was
/// never read. Without this they were the same thing. Every non-429 status fell through to
/// the row parser, which saw no rows and returned none, so a 401 told people their bank
/// statement was unreadable. That is what hid PDF import being refused for the whole free
/// tier, for as long as it was.
///
/// An HttpRequestException, like ServerRateLimitedException, so callers that already treat a
/// failed request as transient keep doing so without knowing about this type.
/// </summary>
public sealed class StatementExtractionException(string message) : HttpRequestException(message)
{
    private const string DefaultMessage =
        "The server could not read that statement. Please try again in a moment.";

    /// <summary>
    /// Builds the exception from a failed response, keeping the server's own wording when it
    /// has any: it is more specific than anything that can be said from here.
    /// </summary>
    public static StatementExtractionException FromBody(string? body)
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
                    return new StatementExtractionException(message.GetString()!);
                }
            }
            catch (JsonException)
            {
                // A proxy error page, not our JSON. Fall through to the default wording.
            }
        }

        return new StatementExtractionException(DefaultMessage);
    }
}
