using SkiaSharp;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Shared image compression and format utilities for receipt scanning services.
/// </summary>
public static class ReceiptImageHelper
{
    internal static string? GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            // iPhones shoot HEIC by default. Skia cannot decode it, so these files reach
            // the vision API as their original bytes rather than as preprocessed JPEG,
            // which is why the content type has to survive rather than be assumed.
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".pdf" => "application/pdf",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            _ => null
        };
    }

    /// <summary>
    /// The image as bytes the app can draw, turned right-side up, or null when it cannot be shown.
    /// A HEIC photo, which iPhones shoot by default and Skia cannot read, is converted to JPEG.
    /// Null is anything that is not an image at all, such as a Word file attached as a receipt.
    /// </summary>
    public static byte[]? ToDisplayable(byte[] imageData)
    {
        using (var stream = new MemoryStream(imageData))
        using (var codec = SKCodec.Create(stream))
        {
            if (codec != null)
                return FixOrientation(imageData);
        }

        return HeifImageDecoder.TryConvertToJpeg(imageData);
    }

    /// <summary>
    /// Applies EXIF orientation correction so the image displays right-side up.
    /// Returns the original bytes if no rotation is needed or the format is unsupported.
    /// </summary>
    public static byte[] FixOrientation(byte[] imageData)
    {
        using var stream = new MemoryStream(imageData);
        using var codec = SKCodec.Create(stream);
        if (codec == null)
            return imageData;

        var origin = codec.EncodedOrigin;
        if (origin == SKEncodedOrigin.TopLeft)
            return imageData; // Already correct

        using var original = SKBitmap.Decode(imageData);
        if (original == null)
            return imageData;

        var swapDims = origin is SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
        var outWidth = swapDims ? original.Height : original.Width;
        var outHeight = swapDims ? original.Width : original.Height;

        using var surface = SKSurface.Create(new SKImageInfo(outWidth, outHeight));
        var canvas = surface.Canvas;
        ApplyExifTransform(canvas, origin, original.Width, original.Height, outWidth, outHeight);
        canvas.DrawBitmap(original, 0, 0);

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }

    /// <summary>
    /// Preprocesses a receipt image to improve OCR accuracy.
    /// Applies EXIF orientation fix, contrast boost, and sharpening.
    /// PDFs are returned unchanged.
    /// </summary>
    public static byte[] PreprocessForOcr(byte[] imageData, string fileName)
        => PreprocessForOcr(imageData, fileName, out _);

    /// <summary>
    /// As above, and reports whether the returned bytes are actually re-encoded JPEG.
    ///
    /// Callers rename the file to .jpg afterwards, which is only true when this says so.
    /// A PDF passes through untouched, and so does any image Skia cannot decode, HEIC
    /// being the one that matters: an iPhone photo renamed to .jpg would be sent to the
    /// vision API labelled as something it is not.
    /// </summary>
    public static byte[] PreprocessForOcr(byte[] imageData, string fileName, out bool convertedToJpeg)
    {
        convertedToJpeg = false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".pdf")
            return imageData;

        // Use SKCodec to read EXIF orientation, then decode with correct rotation applied.
        using var stream = new MemoryStream(imageData);
        using var codec = SKCodec.Create(stream);
        if (codec == null)
        {
            // Skia could not read it. HEIF is the case that matters, and it is detected by
            // the decode failing rather than by the extension, so a HEIC file someone has
            // renamed to .jpg is still handled. Converting to JPEG and starting again means
            // orientation, contrast and sharpening all still apply, exactly as for any
            // other photo.
            var asJpeg = HeifImageDecoder.TryConvertToJpeg(imageData);
            if (asJpeg != null)
                return PreprocessForOcr(asJpeg, Path.ChangeExtension(fileName, ".jpg"), out convertedToJpeg);

            return imageData;
        }

        var origin = codec.EncodedOrigin;
        using var original = SKBitmap.Decode(imageData);
        if (original == null)
            return imageData;

        // Determine output dimensions: swap width/height for 90°/270° rotations.
        var swapDims = origin is SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
        var outWidth = swapDims ? original.Height : original.Width;
        var outHeight = swapDims ? original.Width : original.Height;

        // Mild contrast boost (1.2x) to help faded thermal receipts.
        // Keeps color intact, vision models use color to parse receipts.
        const float contrast = 1.2f;
        const float bias = (1f - contrast) / 2f;
        float[] contrastMatrix =
        [
            contrast, 0,        0,        0, bias,
            0,        contrast, 0,        0, bias,
            0,        0,        contrast, 0, bias,
            0,        0,        0,        1, 0
        ];
        var colorFilter = SKColorFilter.CreateColorMatrix(contrastMatrix);

        // Light sharpen to improve text edge clarity for blurry phone photos.
        var sharpenKernel = new float[]
        {
             0, -0.5f,  0,
            -0.5f,  3, -0.5f,
             0, -0.5f,  0
        };
        var sharpenFilter = SKImageFilter.CreateMatrixConvolution(
            new SKSizeI(3, 3),
            sharpenKernel,
            gain: 1f,
            bias: 0f,
            kernelOffset: new SKPointI(1, 1),
            tileMode: SKShaderTileMode.Clamp,
            convolveAlpha: false);

        using var surface = SKSurface.Create(new SKImageInfo(outWidth, outHeight));
        var canvas = surface.Canvas;

        ApplyExifTransform(canvas, origin, original.Width, original.Height, outWidth, outHeight);

        using var paint = new SKPaint();
        paint.ColorFilter = colorFilter;
        paint.ImageFilter = sharpenFilter;
        canvas.DrawBitmap(original, 0, 0, paint);

        using var snapshot = surface.Snapshot();
        // Match original file size, use quality 95 to avoid inflating compressed JPEGs.
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, 95);
        convertedToJpeg = true;
        return encoded.ToArray();
    }

    /// <summary>
    /// Generates a small JPEG thumbnail suitable for preview cards.
    /// Only applies EXIF rotation and downscale, no contrast/sharpen filters.
    /// </summary>
    public static byte[]? GenerateThumbnail(byte[] imageData, int maxDimension = 200)
    {
        using var stream = new MemoryStream(imageData);
        using var codec = SKCodec.Create(stream);
        if (codec == null)
        {
            // As in PreprocessForOcr: a HEIF photo gets one conversion and then follows the
            // ordinary path. Without this the receipt scans correctly but its card is blank.
            var asJpeg = HeifImageDecoder.TryConvertToJpeg(imageData);
            return asJpeg != null ? GenerateThumbnail(asJpeg, maxDimension) : null;
        }

        var origin = codec.EncodedOrigin;
        using var original = SKBitmap.Decode(imageData);
        if (original == null)
            return null;

        // Determine EXIF-corrected dimensions
        var swapDims = origin is SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
        var orientedWidth = swapDims ? original.Height : original.Width;
        var orientedHeight = swapDims ? original.Width : original.Height;

        // Compute thumbnail size preserving aspect ratio
        var scale = Math.Min((float)maxDimension / orientedWidth, (float)maxDimension / orientedHeight);
        if (scale > 1f) scale = 1f; // Don't upscale
        var thumbWidth = Math.Max(1, (int)(orientedWidth * scale));
        var thumbHeight = Math.Max(1, (int)(orientedHeight * scale));

        using var surface = SKSurface.Create(new SKImageInfo(thumbWidth, thumbHeight));
        var canvas = surface.Canvas;
        canvas.Scale(scale, scale);
        ApplyExifTransform(canvas, origin, original.Width, original.Height, orientedWidth, orientedHeight);
        canvas.DrawBitmap(original, 0, 0);

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, 70);
        return encoded.ToArray();
    }

    /// <summary>
    /// Loads an image from disk, downscales it to fit within <paramref name="maxDimension"/>
    /// preserving aspect ratio, and writes it as PNG to <paramref name="destPath"/>.
    /// PNG is used (lossless) so logos with text and small icons stay crisp at avatar sizes.
    /// Returns false if the source could not be decoded.
    /// </summary>
    public static bool ResizeAndSaveAsPng(string sourcePath, string destPath, int maxDimension)
    {
        // Read the EXIF origin via SKCodec, then decode pixels separately from the path.
        // Phone JPEGs encode rotation in EXIF rather than re-encoding the pixels, so
        // portrait photos would otherwise end up sideways at the avatar size.
        SKEncodedOrigin origin;
        using (var orientationStream = SharedFileReader.OpenRead(sourcePath))
        using (var codec = SKCodec.Create(orientationStream))
        {
            if (codec == null)
                return false;
            origin = codec.EncodedOrigin;
        }

        using var bitmap = SKBitmap.Decode(sourcePath);
        return WriteResizedPng(bitmap, origin, destPath, maxDimension);
    }

    /// <summary>
    /// Bytes-based variant of <see cref="ResizeAndSaveAsPng(string,string,int)"/> for callers
    /// that already have the source image in memory (e.g. a downloaded favicon). Handles
    /// .ico, .png, .jpg, and other Skia-supported formats.
    /// </summary>
    public static bool ResizeBytesAndSaveAsPng(byte[] sourceBytes, string destPath, int maxDimension)
    {
        if (sourceBytes == null || sourceBytes.Length == 0)
            return false;

        SKEncodedOrigin origin = SKEncodedOrigin.TopLeft;
        using (var stream = new MemoryStream(sourceBytes, writable: false))
        using (var codec = SKCodec.Create(stream))
        {
            if (codec != null)
                origin = codec.EncodedOrigin;
        }

        using var bitmap = SKBitmap.Decode(sourceBytes);
        return WriteResizedPng(bitmap, origin, destPath, maxDimension);
    }

    private static bool WriteResizedPng(SKBitmap? bitmap, SKEncodedOrigin origin, string destPath, int maxDimension)
    {
        if (bitmap == null)
            return false;

        var swapDims = origin is SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
        var orientedWidth = swapDims ? bitmap.Height : bitmap.Width;
        var orientedHeight = swapDims ? bitmap.Width : bitmap.Height;

        var scale = Math.Min(
            (float)maxDimension / orientedWidth,
            (float)maxDimension / orientedHeight);
        if (scale > 1f)
            scale = 1f;

        var targetWidth = Math.Max(1, (int)(orientedWidth * scale));
        var targetHeight = Math.Max(1, (int)(orientedHeight * scale));

        using var surface = SKSurface.Create(new SKImageInfo(targetWidth, targetHeight));
        var canvas = surface.Canvas;
        canvas.Scale(scale, scale);
        ApplyExifTransform(canvas, origin, bitmap.Width, bitmap.Height, orientedWidth, orientedHeight);
        canvas.DrawBitmap(bitmap, 0, 0);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(destPath);
        encoded.SaveTo(stream);

        return true;
    }

    private static void ApplyExifTransform(SKCanvas canvas, SKEncodedOrigin origin,
        int srcWidth, int srcHeight, int outWidth, int outHeight)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, outWidth / 2f, 0);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, outWidth / 2f, outHeight / 2f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, 0, outHeight / 2f);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90, 0, 0);
                canvas.Scale(1, -1, srcHeight / 2f, 0);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(outWidth, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(outWidth, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1, 0, srcWidth / 2f);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, outHeight);
                canvas.RotateDegrees(270);
                break;
        }
    }
}
