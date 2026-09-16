using System.Reflection;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Provides application version information.
/// </summary>
public static class AppInfo
{
    /// <summary>
    /// Gets the raw version object. Every project shares one version (Directory.Build.props), so this
    /// assembly is read instead of the entry assembly, which is the test host or designer outside the app.
    /// </summary>
    public static Version? AssemblyVersion { get; } = typeof(AppInfo).Assembly.GetName().Version;

    /// <summary>
    /// Gets the application version in "X.X.X" format.
    /// </summary>
    public static string VersionNumber { get; } = AssemblyVersion is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}"
        : "1.0.0";

    /// <summary>
    /// Gets the application version in "V.X.X.X" format for display.
    /// </summary>
    public static string Version => $"V.{VersionNumber}";
}
