namespace ArgoBooks.Core.Services;

/// <summary>
/// Thrown when a company file's stamped AppVersion is newer than the running app's version.
/// Opening a newer file in an older app is unsafe because the newer app may have added enum
/// values, fields, or structural changes that the older deserializer can't handle.
/// </summary>
public class CompanyFileTooNewException(string fileVersion, string appVersion) : Exception(
    $"This company file was created by Argo Books {fileVersion}. " +
    $"You are running Argo Books {appVersion}. " +
    $"Please update to Argo Books {fileVersion} or later to open it.")
{
    /// <summary>Version stamped in the file's appSettings.json.</summary>
    public string FileVersion { get; } = fileVersion;

    /// <summary>Version of the currently running app.</summary>
    public string AppVersion { get; } = appVersion;
}
