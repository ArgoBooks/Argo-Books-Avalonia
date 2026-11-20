using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Reports a send (an email, a quote to the portal) that came back unsuccessful. The services
/// that send return a code instead of throwing, so without this a fault in our own code or a
/// server error reached the user's screen and nowhere else.
/// </summary>
public static class SendFailure
{
    public static void Report(IErrorLogger? errorLogger, string? errorCode, string? message, string context)
    {
        if (errorLogger == null)
        {
            return;
        }

        switch (errorCode)
        {
            // The client caught an exception of ours and turned it into this.
            case "UNKNOWN_ERROR":
                errorLogger.LogError(message ?? "Send failed", ErrorCategory.Unknown, context, errorCode);
                return;

            case "NETWORK_ERROR":
                errorLogger.LogWarning(message ?? "Send failed", context, ErrorCategory.Network, errorCode);
                return;

            case "TIMEOUT":
                errorLogger.LogWarning(message ?? "Send timed out", context, ErrorCategory.Network, "Timeout");
                return;
        }

        // A bare status code means the server failed before it could explain; a 4xx or a named code is the server's verdict on this send.
        if (int.TryParse(errorCode, out var status) && status >= 500)
        {
            errorLogger.LogError(message ?? "Send failed", ErrorCategory.Api, context, $"Http{status}");
        }
    }
}
