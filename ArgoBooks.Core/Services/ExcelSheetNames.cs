namespace ArgoBooks.Core.Services;

/// <summary>
/// Excel's rules for a worksheet name, which ClosedXML enforces by throwing.
/// </summary>
internal static class ExcelSheetNames
{
    /// <summary>The longest name Excel allows.</summary>
    public const int MaxLength = 31;

    private static readonly char[] Forbidden = ['\\', '/', '*', '?', ':', '[', ']'];

    /// <summary>The name with each forbidden character made an underscore and cut to 31 characters, or the fallback when it is blank.</summary>
    public static string Clean(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        var cleaned = name.Trim();
        foreach (var ch in Forbidden)
            cleaned = cleaned.Replace(ch, '_');

        return cleaned.Length > MaxLength ? cleaned[..MaxLength] : cleaned;
    }
}
