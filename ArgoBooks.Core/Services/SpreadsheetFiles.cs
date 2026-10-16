namespace ArgoBooks.Core.Services;

/// <summary>
/// Which spreadsheet files the importers take and how each is read: as delimited text through
/// <see cref="CsvReader"/>, or as a workbook through ClosedXML, after conversion for the formats
/// ClosedXML cannot open.
/// </summary>
public static class SpreadsheetFiles
{
    /// <summary>Delimited text. A .tsv or a tab-separated .txt is read like a CSV, since the separator is detected.</summary>
    public static readonly string[] DelimitedTextExtensions = [".csv", ".tsv", ".txt"];

    /// <summary>Workbooks. ClosedXML opens .xlsx and the macro-enabled .xlsm, and the others are converted first.</summary>
    public static readonly string[] WorkbookExtensions = [".xlsx", ".xlsm", ".xls", ".ods"];

    public static bool IsDelimitedText(string? path) => HasExtension(path, DelimitedTextExtensions);

    public static bool NeedsConversion(string? path) => HasExtension(path, [".xls", ".ods"]);

    /// <summary>Writes a workbook ClosedXML cannot open to a temporary .xlsx and returns its path. The caller deletes it.</summary>
    public static string ConvertToTempXlsx(string path) =>
        HasExtension(path, [".ods"])
            ? OdsConverter.ConvertOdsToTempXlsx(path)
            : LegacyXlsConverter.ConvertXlsToTempXlsx(path);

    private static bool HasExtension(string? path, string[] extensions) =>
        extensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());
}
