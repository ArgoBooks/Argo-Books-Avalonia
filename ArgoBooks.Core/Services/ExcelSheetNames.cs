using ClosedXML.Excel;

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

    /// <summary>The cleaned name, with a numbered suffix when the workbook already has a sheet by that name.</summary>
    public static string CleanAndUnique(XLWorkbook workbook, string? name, string fallback)
    {
        var cleaned = Clean(name, fallback);
        var candidate = cleaned;
        for (var suffix = 1; workbook.Worksheets.Any(w => string.Equals(w.Name, candidate, StringComparison.OrdinalIgnoreCase)); suffix++)
        {
            var tail = $"_{suffix}";
            candidate = cleaned[..Math.Min(cleaned.Length, MaxLength - tail.Length)] + tail;
        }
        return candidate;
    }
}
