using System.Runtime.InteropServices;

namespace ArgoBooks.Core.Services;

/// <summary>
/// How telemetry names the operating system, and where app events are posted. The website stores
/// these values as they are sent, so every reporter must send the same ones.
/// </summary>
internal static class TelemetryPlatform
{
    /// <summary>The website endpoint that records first runs and survey answers.</summary>
    public const string AppEventEndpoint = "/api/track-app-event.php";

    /// <summary>"win", "mac" or "linux", the keys the app event endpoint expects.</summary>
    public static string Key =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "mac"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
        : "unknown";

    /// <summary>"Windows", "macOS" or "Linux", as crash and usage reports show it.</summary>
    public static string Name =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux"
        : "Unknown";

    /// <summary>
    /// The operating system's version number alone, such as "10.0.26100" on Windows or "15.1.0" on macOS.
    /// The website keeps it only if it is digits and dots, so nothing else may be added to it.
    /// </summary>
    public static string Version
    {
        get
        {
            var version = Environment.OSVersion.Version;
            return version.Build >= 0
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : $"{version.Major}.{version.Minor}";
        }
    }
}
