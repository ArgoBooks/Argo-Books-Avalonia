using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace ArgoBooks.Core.Services;

/// <summary>
/// RFC-4180 CSV reader: handles quoted fields with embedded newlines/commas, any encoding
/// TextFileDecoder reads, and auto-detects the delimiter from the first lines.
/// </summary>
public static class CsvReader
{
    public static List<List<string>> ReadAllRows(string path, out List<string> headers)
    {
        var text = TextFileDecoder.ReadAllText(path);
        var all = ParseRows(text, DetectDelimiter(text));

        // Leading section-comment lines, such as the "# Customers" line our own export writes, and blank rows are skipped so the real header is used.
        var start = 0;
        while (start < all.Count && IsCommentOrBlank(all[start]))
            start++;

        headers = start < all.Count ? all[start].ToList() : [];
        return all.Skip(start + 1)
                  .Where(r => r.Any(f => !string.IsNullOrWhiteSpace(f)))
                  .Select(r => r.ToList())
                  .ToList();
    }

    /// <summary>Splits CSV text into rows, keeping a quoted field that runs over several lines in one row.</summary>
    internal static List<string[]> ParseRows(string text, char delimiter)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = false,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
            DetectColumnCountChanges = false,
        };

        using var reader = new StringReader(text);
        using var csv = new CsvParser(reader, config);

        var rows = new List<string[]>();
        while (csv.Read())
            rows.Add(csv.Record ?? []);
        return rows;
    }

    private static bool IsCommentOrBlank(string[] row)
        => row.Length == 0
           || row.All(string.IsNullOrWhiteSpace)
           || row[0].TrimStart().StartsWith('#');

    private static char DetectDelimiter(string text) =>
        SpreadsheetAnalysisService.DetectCsvDelimiter(text.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
}
