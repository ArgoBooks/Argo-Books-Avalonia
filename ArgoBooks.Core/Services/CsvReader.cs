using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace ArgoBooks.Core.Services;

/// <summary>
/// RFC-4180 CSV reader: handles quoted fields with embedded newlines/commas, BOM, and
/// auto-detects the delimiter from the first non-empty line.
/// </summary>
public static class CsvReader
{
    public static List<List<string>> ReadAllRows(string path, out List<string> headers)
    {
        var delimiter = DetectDelimiter(path);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = false,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
            DetectColumnCountChanges = false,
        };

        using var reader = OpenSharedReader(path);
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

    private static char DetectDelimiter(string path)
    {
        using var reader = OpenSharedReader(path);
        string? first;
        do { first = reader.ReadLine(); }
        while (first != null && (string.IsNullOrWhiteSpace(first) || first.TrimStart().StartsWith('#')));
        return SpreadsheetAnalysisService.DetectCsvDelimiter(first ?? "");
    }

    /// <summary>
    /// Opens the file with FileShare.ReadWrite so it can still be read while it is open in
    /// another program (Excel, LibreOffice, etc.) instead of failing with a sharing violation.
    /// </summary>
    private static StreamReader OpenSharedReader(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
    }
}
