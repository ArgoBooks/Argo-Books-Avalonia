using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Files a failed network call at the severity it actually deserves.
///
/// <para>
/// Every network failure used to be logged as an Error, which meant a user on a dropping
/// connection and our own API falling over produced identical rows on the dashboard. Only
/// one of those is a defect we can act on, and burying it under the other is how a real
/// outage goes unnoticed. The split is:
/// </para>
///
/// <list type="bullet">
///   <item>The user is offline, or the request never reached a server: <b>warning</b>. Not
///   our software failing, and nothing to fix. Still uploaded, because the rate of these
///   by country is worth knowing when deciding how offline-tolerant a feature must be.</item>
///   <item>A server answered and the answer was a failure: <b>error</b>. We were reachable
///   and still could not do the job, so this is ours.</item>
/// </list>
///
/// <para>
/// <see cref="Report"/> classifies from the exception alone and costs nothing, which is what
/// background work needs: a silent retry must not spend five seconds probing Google to
/// decide how to phrase a log line. <see cref="ResolveAndReportAsync"/> is for paths that
/// are already about to tell the user something, where the probe is happening regardless
/// and its answer is more precise than any guess.
/// </para>
/// </summary>
public static class NetworkFailure
{
    /// <summary>
    /// Files <paramref name="exception"/> without probing the network. Use from background
    /// work and silent retries.
    /// </summary>
    /// <param name="reachedCategory">
    /// Category to use when the request did reach a server, so a caller talking to our own
    /// API can keep those under <see cref="ErrorCategory.Api"/>. A request that never got
    /// there is always a network warning, whatever this says.
    /// </param>
    public static void Report(
        IErrorLogger? errorLogger,
        Exception exception,
        string context,
        ErrorCategory reachedCategory = ErrorCategory.Network)
    {
        if (errorLogger is null)
        {
            return;
        }

        // A cancelled or timed-out request is not a failure worth a row of its own;
        // ErrorLogger already drops OperationCanceledException outright.
        if (exception is OperationCanceledException)
        {
            return;
        }

        // A server that answered, a reply we could not read, or a fault in our own code is ours to fix, whatever catch block it arrived in.
        if (EnvironmentFailure.Classify(exception) is not { Category: ErrorCategory.Network } cause)
        {
            errorLogger.LogError(exception, reachedCategory, context);
            return;
        }

        ErrorLogger.MarkReported(exception);

        // The code, not the message, is what groups these on the dashboard, and a warning
        // is only uploaded at all when it carries one.
        errorLogger.LogWarning(
            $"Network unreachable: {Describe(exception)}",
            context,
            ErrorCategory.Network,
            code: cause.Code);
    }

    /// <summary>
    /// Probes the connection, files the failure accordingly, and returns the message to put
    /// in front of the user. Use from a catch block that is already going to show a dialog.
    /// </summary>
    public static async Task<string> ResolveAndReportAsync(
        IErrorLogger? errorLogger,
        Exception exception,
        string context,
        IConnectivityService? connectivity = null,
        CancellationToken cancellationToken = default)
    {
        var message = connectivity is null
            ? await ConnectivityMessage.ResolveAsync(cancellationToken)
            : await ConnectivityMessage.ResolveAsync(connectivity, cancellationToken);

        if (errorLogger is not null && !IsCancellation(exception))
        {
            if (message == ConnectivityMessage.NoInternet)
            {
                // The probe could not reach Google or Cloudflare either, so this is the
                // user's connection rather than anything of ours.
                ErrorLogger.MarkReported(exception);
                errorLogger.LogWarning(
                    $"Offline: {Describe(exception)}",
                    context,
                    ErrorCategory.Network,
                    code: "Offline");
            }
            else if (EnvironmentFailure.Classify(exception) is { Category: ErrorCategory.Network } cause)
            {
                // The internet works and our host still did not answer, which is our end, so it is filed directly rather than by LogError's own rule for a request that never reached a server.
                ErrorLogger.MarkReported(exception);
                errorLogger.LogError($"Our server did not answer: {Describe(exception)}", ErrorCategory.Network, context, cause.Code);
            }
            else
            {
                // It answered, and the answer was a failure.
                errorLogger.LogError(exception, ErrorCategory.Network, context);
            }
        }

        return message;
    }

    /// <summary>
    /// True for a request someone called off. A request that gave up waiting arrives as the same
    /// exception, carrying a TimeoutException inside, and that one is a failure worth filing.
    /// </summary>
    private static bool IsCancellation(Exception exception) =>
        exception is OperationCanceledException && exception.InnerException is not TimeoutException;

    internal static string TransportCode(Exception exception)
    {
        if (exception is not HttpRequestException httpException)
        {
            return exception.GetType().Name;
        }

        // HttpRequestError separates name resolution from a refused connection from a TLS problem, which is no internet, our host down, or interception.
        return httpException.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => "DnsFailure",
            HttpRequestError.ConnectionError => "ConnectionRefused",
            HttpRequestError.SecureConnectionError => "TlsFailure",
            HttpRequestError.ProxyTunnelError => "ProxyFailure",
            _ => "TransportFailure"
        };
    }

    private static string Describe(Exception exception)
    {
        // ErrorLogger sanitises and length-caps whatever it is handed; this only needs to
        // be short enough to stay readable next to the code.
        var message = exception.Message;
        return message.Length > 120 ? message[..117] + "..." : message;
    }
}
