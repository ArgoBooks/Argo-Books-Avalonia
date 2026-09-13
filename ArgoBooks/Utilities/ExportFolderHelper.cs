namespace ArgoBooks.Utilities;

/// <summary>
/// Where a multi-file export writes to, and what its files are called.
///
/// Anything that saves more than one file puts them in a subfolder of the folder the user
/// picked, rather than scattering them loose. Picking Downloads and receiving a dozen pay stubs
/// among everything else already there is the behaviour this exists to prevent.
///
/// A single file is written straight into the chosen folder: wrapping one PDF in a folder is
/// just an extra click on the way to it.
/// </summary>
public static class ExportFolderHelper
{
    /// <summary>
    /// The directory to write to, creating a subfolder when more than one file is coming.
    /// </summary>
    /// <param name="chosen">The folder the user picked.</param>
    /// <param name="folderName">Subfolder name, sanitised here so callers need not.</param>
    /// <param name="fileCount">How many files the export will produce.</param>
    public static string Resolve(string chosen, string folderName, int fileCount)
    {
        if (fileCount <= 1)
        {
            return chosen;
        }

        // Re-exporting the same run lands in the same folder and overwrites, which is what
        // someone correcting a mistake expects. Matches the receipts bulk export.
        string path = Path.Combine(chosen, Sanitize(folderName));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// A name that is safe on disk. Spaces become dashes as well as the invalid characters,
    /// because these end up in file names that get emailed around.
    /// </summary>
    public static string Sanitize(string? name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = new((name ?? string.Empty)
            .Select(c => invalid.Contains(c) || c == ' ' ? '-' : c)
            .ToArray());

        result = result.Trim('-');
        return result.Length == 0 ? "export" : result;
    }
}
