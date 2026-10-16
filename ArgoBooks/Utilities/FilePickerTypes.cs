using Avalonia.Platform.Storage;

namespace ArgoBooks.Utilities;

/// <summary>
/// Shared file picker type definitions used across multiple pages.
/// </summary>
public static class FilePickerTypes
{
    /// <summary>
    /// One extension the receipt scanner accepts: what to send it as, what to call it on
    /// screen, and whether it is an image rather than a document.
    ///
    /// <para>
    /// A display name is deliberately allowed to repeat, since .jpg and .jpeg are both JPEG
    /// and .heic and .heif are both HEIC to a user, while their content types differ.
    /// </para>
    /// </summary>
    private sealed record ReceiptFormat(string Extension, string ContentType, string DisplayName, bool IsImage);

    /// <summary>
    /// Every accepted receipt format, in the order they are listed to the user.
    ///
    /// <para>
    /// The one place this is written down. The picker filters, the drag and drop validation,
    /// the content type sent with a scan and the wording on screen are all derived from it.
    /// </para>
    /// </summary>
    private static readonly ReceiptFormat[] ReceiptFormats =
    [
        new(".jpg", "image/jpeg", "JPEG", IsImage: true),
        new(".jpeg", "image/jpeg", "JPEG", IsImage: true),
        new(".png", "image/png", "PNG", IsImage: true),
        new(".heic", "image/heic", "HEIC", IsImage: true),
        new(".heif", "image/heif", "HEIC", IsImage: true),
        new(".webp", "image/webp", "WebP", IsImage: true),
        // Converted to JPEG or PDF as it is added, by ReadableReceiptPathAsync, so this type is never sent.
        new(".tif", "image/tiff", "TIFF", IsImage: true),
        new(".tiff", "image/tiff", "TIFF", IsImage: true),
        new(".pdf", "application/pdf", "PDF", IsImage: false)
    ];

    /// <summary>
    /// File extensions (lowercase, leading dot) the AI receipt scanner can process.
    /// </summary>
    public static readonly string[] SupportedReceiptExtensions =
        [.. ReceiptFormats.Select(f => f.Extension)];

    /// <summary>
    /// The accepted formats as a user reads them, e.g. "JPEG, PNG, HEIC, WebP, or PDF".
    /// Every message and caption that names them uses this, so adding a format above is
    /// enough to have it announced everywhere it is offered.
    /// </summary>
    public static readonly string SupportedReceiptFormats = JoinWithOr(
        [.. ReceiptFormats.Select(f => f.DisplayName).Distinct(StringComparer.Ordinal)]);

    /// <summary>
    /// Returns true if the path points to a file the receipt scanner accepts.
    /// </summary>
    public static bool IsSupportedReceiptFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return Array.IndexOf(SupportedReceiptExtensions, extension) >= 0;
    }

    /// <summary>
    /// The content type for an accepted receipt file, or null for anything else. Callers that
    /// have to store something for an unknown file supply their own fallback.
    /// </summary>
    public static string? GetReceiptContentType(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return Array.Find(ReceiptFormats, f => f.Extension == extension)?.ContentType;
    }

    /// <summary>
    /// The file to read a chosen receipt from: the file itself, or for a TIFF the JPEG or PDF it is
    /// converted to, since nothing past this point reads TIFF. Null when a TIFF cannot be read.
    /// </summary>
    public static async Task<string?> ReadableReceiptPathAsync(string path)
    {
        if (!Core.Services.TiffConverter.IsTiff(path))
            return path;

        try
        {
            return await Task.Run(() => Core.Services.TiffConverter.ConvertToTempFile(path));
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.FileSystem, "Failed to convert a TIFF receipt");
            return null;
        }
    }

    /// <summary>Whether a stored receipt is a PDF, by its content type or, failing that, its file name.</summary>
    public static bool IsPdfReceipt(string? fileType, string? fileName) =>
        fileType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true
        || fileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Whether a stored receipt is an image, by its content type or, failing that, its file name.
    /// Older receipts stored a bare extension such as "jpg" as the type, which is read the same way.
    /// </summary>
    public static bool IsImageReceipt(string? fileType, string? fileName) =>
        fileType?.Contains("image", StringComparison.OrdinalIgnoreCase) == true
        || IsImageName(fileName)
        || IsImageName("." + fileType?.TrimStart('.'));

    private static bool IsImageName(string? fileName) =>
        Core.Services.MimeTypes.For(fileName).StartsWith("image/", StringComparison.Ordinal);

    public static readonly FilePickerFileType ImageFileType = new("Images")
    {
        Patterns = [.. Patterns(images: true)],
        MimeTypes = [.. ContentTypes(images: true)]
    };

    public static readonly FilePickerFileType PdfFileType = new("PDF Documents")
    {
        Patterns = ["*.pdf"],
        MimeTypes = ["application/pdf"]
    };

    public static readonly FilePickerFileType AllSupportedTypes = new("All Supported")
    {
        Patterns = [.. Patterns()],
        MimeTypes = [.. ContentTypes()]
    };

    /// <summary>The filters every receipt picker offers, in the order it offers them.</summary>
    public static readonly IReadOnlyList<FilePickerFileType> ReceiptFilters = [AllSupportedTypes, ImageFileType, PdfFileType];

    /// <summary>Logos and avatars: every format ImageFileLoader reads, which is wider than what the receipt scanner accepts.</summary>
    public static readonly FilePickerFileType PictureFileType = new("Images")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.heic", "*.heif"]
    };

    public static readonly FilePickerFileType CompanyFileType = new("Argo Books Files")
    {
        Patterns = ["*" + Core.Models.FileFormatConstants.CompanyFileExtension]
    };

    public static readonly FilePickerFileType BackupFileType = new("Argo Books Backup")
    {
        Patterns = ["*" + Core.Models.FileFormatConstants.BackupFileExtension]
    };

    /// <summary>Every spreadsheet the importer reads, workbook or delimited text.</summary>
    public static readonly FilePickerFileType SpreadsheetFileType = new("Spreadsheets")
    {
        Patterns = [.. Core.Services.SpreadsheetFiles.WorkbookExtensions.Concat(Core.Services.SpreadsheetFiles.DelimitedTextExtensions).Select(e => "*" + e)]
    };

    /// <summary>A workbook to import: any Excel format, or LibreOffice's .ods.</summary>
    public static readonly FilePickerFileType ExcelFileType = new("Excel or LibreOffice workbook")
    {
        Patterns = [.. Core.Services.SpreadsheetFiles.WorkbookExtensions.Select(e => "*" + e)]
    };

    /// <summary>A spreadsheet the app writes, which is always .xlsx.</summary>
    public static readonly FilePickerFileType XlsxSaveType = new("Excel Workbook") { Patterns = ["*.xlsx"] };

    public static readonly FilePickerFileType CsvFileType = new("CSV or tab-separated text")
    {
        Patterns = [.. Core.Services.SpreadsheetFiles.DelimitedTextExtensions.Select(e => "*" + e)]
    };

    public static readonly FilePickerFileType BankStatementFileType = new("Bank statements")
    {
        Patterns = [.. SpreadsheetFileType.Patterns!, "*.pdf", .. Core.Services.OfxStatementReader.Extensions.Select(e => "*" + e)]
    };

    private static IEnumerable<string> Patterns(bool? images = null) => ReceiptFormats
        .Where(f => images == null || f.IsImage == images)
        .Select(f => "*" + f.Extension);

    private static IEnumerable<string> ContentTypes(bool? images = null) => ReceiptFormats
        .Where(f => images == null || f.IsImage == images)
        .Select(f => f.ContentType)
        .Distinct(StringComparer.Ordinal);

    /// <summary>"A", "A or B", "A, B, or C".</summary>
    private static string JoinWithOr(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} or {names[1]}",
        _ => $"{string.Join(", ", names.Take(names.Count - 1))}, or {names[^1]}"
    };
}
