using System.Diagnostics;

namespace ArgoBooks.Core.Platform;

/// <summary>Opens the system file manager.</summary>
public static class FileManager
{
    /// <summary>
    /// Shows a file in Explorer or Finder with it selected. Linux file managers have no common way
    /// to select a file, so there the containing folder opens instead. Throws if nothing could be started.
    /// </summary>
    public static void ShowFile(string filePath)
    {
        if (OperatingSystem.IsWindows())
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", $"-R \"{filePath}\"");
        else if (OperatingSystem.IsLinux() && Path.GetDirectoryName(filePath) is { Length: > 0 } directory)
            Process.Start("xdg-open", directory);
    }
}
