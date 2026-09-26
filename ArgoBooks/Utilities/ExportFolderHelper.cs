using ArgoBooks.Core.Utilities;

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
    /// <param name="folderName">Subfolder name, made safe here so callers need not.</param>
    /// <param name="fileCount">How many files the export will produce.</param>
    public static string Resolve(string chosen, string folderName, int fileCount)
    {
        if (fileCount <= 1)
        {
            return chosen;
        }

        // Re-exporting the same run lands in the same folder and overwrites, which is what
        // someone correcting a mistake expects. Matches the receipts bulk export.
        string path = Path.Combine(chosen, SafeFileName.Create(folderName, "export", replaceSpaces: true));
        Directory.CreateDirectory(path);
        return path;
    }
}
