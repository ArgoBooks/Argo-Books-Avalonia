namespace ArgoBooks.Core.Utilities;

/// <summary>
/// A file or folder name nothing is using yet, so an export never replaces one written before it.
/// </summary>
public static class UnusedPath
{
    /// <summary><paramref name="path"/>, or the same name with " (2)", " (3)" and so on added before the extension when something already has it.</summary>
    public static string ForFile(string path) => ForFile(path, Exists);

    /// <summary>
    /// <paramref name="path"/>, or the same name with " (2)", " (3)" and so on added before the extension until <paramref name="taken"/> says no.
    /// </summary>
    /// <param name="path">The name the export would like to use.</param>
    /// <param name="taken">Whether a candidate is in use, for an export that writes more than the one path, such as a file per page.</param>
    public static string ForFile(string path, Func<string, bool> taken) =>
        Find(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path), Path.GetExtension(path), taken);

    /// <summary><paramref name="path"/>, or the same name with " (2)", " (3)" and so on added to the end when something already has it.</summary>
    public static string ForFolder(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        return Find(Path.GetDirectoryName(trimmed) ?? string.Empty, Path.GetFileName(trimmed), string.Empty, Exists);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string Find(string dir, string name, string ext, Func<string, bool> taken)
    {
        var path = Path.Combine(dir, name + ext);
        for (var n = 2; taken(path); n++)
            path = Path.Combine(dir, $"{name} ({n}){ext}");
        return path;
    }
}
