using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using SkiaSharp;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Turns a TIFF, which office scanners save and which neither Skia nor the receipt scanner reads,
/// into a JPEG for one page or a PDF for several. It runs once, as a receipt is added, so
/// everything after it only ever sees formats it already handles.
/// </summary>
public static class TiffConverter
{
    private const float DefaultDpi = 200f;

    private const int JpegQuality = 90;

    public static bool IsTiff(string? path) =>
        Path.GetExtension(path ?? "").ToLowerInvariant() is ".tif" or ".tiff";

    /// <summary>
    /// Writes the converted file to a new temporary folder under the TIFF's own name, so it shows
    /// under the name the user gave it, and returns its path.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a TIFF this can read.</exception>
    public static string ConvertToTempFile(string tiffPath)
    {
        var pages = ReadPages(SharedFileReader.ReadAllBytes(tiffPath));
        if (pages.Count == 0)
            throw new InvalidDataException("The TIFF has no pages that could be read.");

        var folder = Path.Combine(AppPaths.Temp, "ConvertedReceipts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(tiffPath);

        if (pages.Count == 1)
        {
            var jpegPath = Path.Combine(folder, name + ".jpg");
            File.WriteAllBytes(jpegPath, pages[0].Jpeg);
            return jpegPath;
        }

        var pdfPath = Path.Combine(folder, name + ".pdf");
        File.WriteAllBytes(pdfPath, ImagePdf.Create(pages));
        return pdfPath;
    }

    /// <summary>Every page of a TIFF as a JPEG at the paper's size, or none when no page could be read.</summary>
    /// <exception cref="InvalidDataException">The file is not a TIFF.</exception>
    internal static List<ImagePdf.Page> ReadPages(byte[] content)
    {
        Tiff.SetErrorHandler(QuietErrorHandler.Instance);
        using var tiff = Tiff.ClientOpen("receipt", "r", new MemoryStream(content), new TiffStream())
                         ?? throw new InvalidDataException("The file is not a TIFF.");

        var pages = new List<ImagePdf.Page>();
        var count = tiff.NumberOfDirectories();
        for (short i = 0; i < count; i++)
        {
            if (!tiff.SetDirectory(i) || IsReducedImage(tiff))
                continue;

            var width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            var height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            var raster = new int[width * height];
            if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                continue;

            // Each pixel is packed with red in the lowest byte, which in memory is the order Rgba8888 reads.
            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            Marshal.Copy(raster, 0, bitmap.GetPixels(), raster.Length);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

            var (dpiX, dpiY) = Resolution(tiff);
            pages.Add(new ImagePdf.Page(encoded.ToArray(), width / dpiX * 72f, height / dpiY * 72f));
        }

        return pages;
    }

    /// <summary>A scanner may store a small preview of a page as its own entry, which is not a page.</summary>
    private static bool IsReducedImage(Tiff tiff)
    {
        var type = tiff.GetField(TiffTag.SUBFILETYPE);
        return type != null && ((FileType)type[0].ToInt()).HasFlag(FileType.REDUCEDIMAGE);
    }

    /// <summary>The page's dots per inch, so the PDF page is the paper's size.</summary>
    private static (float X, float Y) Resolution(Tiff tiff)
    {
        var x = tiff.GetField(TiffTag.XRESOLUTION)?[0].ToFloat() ?? 0f;
        var y = tiff.GetField(TiffTag.YRESOLUTION)?[0].ToFloat() ?? 0f;
        var unit = (ResUnit?)tiff.GetField(TiffTag.RESOLUTIONUNIT)?[0].ToInt() ?? ResUnit.INCH;
        if (unit == ResUnit.CENTIMETER)
        {
            x *= 2.54f;
            y *= 2.54f;
        }
        else if (unit == ResUnit.NONE)
        {
            x = y = 0f;
        }

        return (x > 0 ? x : DefaultDpi, y > 0 ? y : x > 0 ? x : DefaultDpi);
    }

    /// <summary>
    /// LibTiff writes every warning and error to the console, and scanners write tags it warns about
    /// as a matter of course. A failure still comes back through its return values, which are checked.
    /// </summary>
    private sealed class QuietErrorHandler : TiffErrorHandler
    {
        public static readonly QuietErrorHandler Instance = new();

        public override void WarningHandler(Tiff tif, string method, string format, params object[] args) { }

        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }

        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args) { }

        public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
    }
}
