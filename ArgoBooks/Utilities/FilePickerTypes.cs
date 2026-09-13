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
