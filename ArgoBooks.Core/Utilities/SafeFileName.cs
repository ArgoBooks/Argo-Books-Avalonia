namespace ArgoBooks.Core.Utilities;

/// <summary>
/// Turns any text into a name a file or folder can have on Windows, macOS and Linux alike.
/// </summary>
public static class SafeFileName
{
    // A fixed set rather than Path.GetInvalidFileNameChars(): on macOS and Linux that returns only
    // '/' and NUL, so "Q1: Revenue" saved there kept its colon and made a file Windows can't open.
    // Files get shared between machines, so a name has to be legal everywhere, not just where it
    // was written. Control characters are added separately through char.IsControl.
    private const string WindowsReservedChars = "<>:\"/\\|?*";

    /// <summary>
    /// <paramref name="name"/> with every character a file name can't hold replaced by "-".
    /// </summary>
    /// <param name="name">The text to name the file after.</param>
    /// <param name="fallback">Used when nothing usable is left, since an empty segment would write
    /// into the parent folder instead.</param>
    /// <param name="replaceSpaces">Also turn spaces into "-" and drop dashes at either end, for
    /// names that get emailed or attached, where spaces get mangled.</param>
    public static string Create(string? name, string fallback, bool replaceSpaces = false)
    {
        var result = new string((name ?? string.Empty)
            .Select(c => char.IsControl(c) || WindowsReservedChars.Contains(c) || (replaceSpaces && c == ' ') ? '-' : c)
            .ToArray());

        if (replaceSpaces)
            result = result.Trim('-');

        return string.IsNullOrWhiteSpace(result) || result.All(c => c == '.') ? fallback : result;
    }
}
