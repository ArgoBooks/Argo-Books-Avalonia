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
        var delimiter = DetectDelimiter(text);
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

        var all = new List<string[]>();
        while (csv.Read())
            all.Add(csv.Record ?? []);

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

    private static bool IsCommentOrBlank(string[] row)
        => row.Length == 0
           || row.All(string.IsNullOrWhiteSpace)
           || row[0].TrimStart().StartsWith('#');

    private static char DetectDelimiter(string text) =>
        SpreadsheetAnalysisService.DetectCsvDelimiter(text.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
}
