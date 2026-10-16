using System.Globalization;

namespace ArgoBooks.Utilities;

/// <summary>How a file size is shown to the user.</summary>
public static class FileSizeText
{
    /// <summary>"2.4 MB", or whole kilobytes below a megabyte, never less than "1 KB".</summary>
    public static string Format(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / 1024d / 1024d).ToString("0.#", CultureInfo.CurrentCulture) + " MB"
            : Math.Max(1, bytes / 1024).ToString(CultureInfo.CurrentCulture) + " KB";
}
