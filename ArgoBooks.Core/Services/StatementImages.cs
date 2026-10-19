using SkiaSharp;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Photos and scans of a paper bank statement. The statement reader on the server takes only a
/// PDF, so a picture is put on a PDF page here and read the same way a PDF statement is.
/// </summary>
public static class StatementImages
{
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif", ".tif", ".tiff"];

    private const int JpegQuality = 90;

    /// <summary>Letter, in points, which the photo is fitted to since a photo has no paper size of its own.</summary>
    private const float PageLongSide = 792f;
    private const float PageShortSide = 612f;

    public static bool IsImage(string? path) =>
        Extensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

    /// <summary>
    /// The picture as a PDF: a photo on one page turned the right way up, and a TIFF scan with a
    /// page for each of its pages.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a picture this can read.</exception>
    public static byte[] ToPdf(string path)
    {
        var content = SharedFileReader.ReadAllBytes(path);
        var pages = TiffConverter.IsTiff(path) ? TiffConverter.ReadPages(content) : [PhotoPage(content)];
        if (pages.Count == 0)
            throw new InvalidDataException("The scan has no pages that could be read.");
        return ImagePdf.Create(pages);
    }

    private static ImagePdf.Page PhotoPage(byte[] content)
    {
        // Turns a phone photo upright by its EXIF tag and converts HEIC, which Skia cannot read.
        var upright = ReceiptImageHelper.ToDisplayable(content) ?? throw new InvalidDataException("The file is not a picture.");
        using var bitmap = SKBitmap.Decode(upright) ?? throw new InvalidDataException("The picture could not be decoded.");
        using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        var portrait = bitmap.Height >= bitmap.Width;
        var (maxWidth, maxHeight) = portrait ? (PageShortSide, PageLongSide) : (PageLongSide, PageShortSide);
        var scale = Math.Min(maxWidth / bitmap.Width, maxHeight / bitmap.Height);
        return new ImagePdf.Page(jpeg.ToArray(), bitmap.Width * scale, bitmap.Height * scale);
    }
}
