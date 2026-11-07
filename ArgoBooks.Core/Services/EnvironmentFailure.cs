using System.Net.Sockets;
using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Recognises a failure caused by the user's computer or connection rather than by Argo Books:
/// a file another program has open, a folder security software protects, a full disk, a drive
/// that has gone, or a request that never reached a server. There is nothing in our code to fix
/// for these, so they are reported as warnings.
/// </summary>
public static class EnvironmentFailure
{
    private const int Win32Facility = unchecked((int)0x80070000);
    private const int ErrorAccessDenied = 0x5;
    private const int ErrorNotReady = 0x15;
    private const int ErrorSharingViolation = 0x20;
    private const int ErrorLockViolation = 0x21;
    private const int ErrorHandleDiskFull = 0x27;
    private const int ErrorDevNotExist = 0x37;
    private const int ErrorDiskFull = 0x70;

    // ENOSPC. On macOS and Linux an IOException carries the raw errno as its HResult.
    private const int UnixNoSpace = 28;

    /// <summary>The cause of <paramref name="exception"/>, or null when the failure is ours.</summary>
    public static EnvironmentCause? Classify(Exception exception)
    {
        for (Exception? ex = exception; ex != null; ex = ex.InnerException)
        {
            if (ClassifyOne(ex) is { } cause)
            {
                return cause;
            }
        }

        return null;
    }

    private static EnvironmentCause? ClassifyOne(Exception ex)
    {
        switch (ex)
        {
            case UnauthorizedAccessException:
                return FileCause("AccessDenied", "Access to the file or folder was denied");
            case DriveNotFoundException:
                return FileCause("DriveUnavailable", "The drive is no longer available");
            case HttpRequestException { StatusCode: null } http:
                return NetworkCause(NetworkFailure.TransportCode(http));
            case SocketException:
                return NetworkCause("TransportFailure");

            // Only reached inside a wrapper: a request that gave up waiting, which a slow connection causes as often as our server.
            case TimeoutException:
            case TaskCanceledException:
                return NetworkCause("Timeout");

            // A missing file or folder is as likely to be a path we built wrongly as one the user removed.
            case FileNotFoundException:
            case DirectoryNotFoundException:
                return null;

            case IOException io when (io.HResult & unchecked((int)0xFFFF0000)) == Win32Facility:
                return (io.HResult & 0xFFFF) switch
                {
                    ErrorAccessDenied => FileCause("AccessDenied", "Access to the file or folder was denied"),
                    ErrorSharingViolation or ErrorLockViolation => FileCause("FileLocked", "The file is open in another program"),
                    ErrorDiskFull or ErrorHandleDiskFull => FileCause("DiskFull", "The disk is full"),
                    ErrorNotReady or ErrorDevNotExist => FileCause("DriveUnavailable", "The drive is no longer available"),
                    _ => null,
                };

            case IOException { HResult: UnixNoSpace } when !OperatingSystem.IsWindows():
                return FileCause("DiskFull", "The disk is full");

            default:
                return null;
        }
    }

    private static EnvironmentCause FileCause(string code, string description) =>
        new(code, ErrorCategory.FileSystem, description);

    private static EnvironmentCause NetworkCause(string code) =>
        new(code, ErrorCategory.Network, "The request did not reach the server");
}

/// <summary>
/// What went wrong outside Argo Books. <see cref="Description"/> is our own wording, so a path or
/// file name in the exception's message is never what gets uploaded.
/// </summary>
public sealed record EnvironmentCause(string Code, ErrorCategory Category, string Description);
