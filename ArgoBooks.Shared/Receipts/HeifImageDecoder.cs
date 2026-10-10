using LibHeifSharp;
using SkiaSharp;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Turns an iPhone's HEIC photo into something the rest of the app can read.
///
/// Skia has no HEIF decoder on Windows or Linux, so every other path here, the preview,
/// the thumbnail, the OCR preprocessing, gets a null bitmap and gives up. Since iPhones
/// shoot HEIC by default and receipt scanning is the feature people arrive for, that is a
/// large hole to leave open.
///
/// Conversion happens once, at import, and produces JPEG. Nothing downstream needs to know
/// HEIF exists, and the decoder is never touched for the formats Skia already handles.
/// </summary>
public static class HeifImageDecoder
{
    /// <summary>
    /// Decodes HEIF bytes and re-encodes them as JPEG, or returns null if that is not
    /// possible on this machine.
    ///
    /// Null is a normal outcome, not a fault. The native library may be missing from a
    /// sideloaded build, or the file may be a variant this version cannot read, and in both
    /// cases the caller carries on with the original bytes: the vision API accepts HEIC
    /// directly, so the scan still works and only the on-screen preview is lost. Throwing
    /// here would turn a missing thumbnail into a failed import.
    /// </summary>
    public static byte[]? TryConvertToJpeg(byte[] heifData, int quality = 95)
    {
        if (heifData.Length == 0)
        {
            return null;
        }

        try
        {
            using var context = new HeifContext(heifData);
            using var handle = context.GetPrimaryImageHandle();

            var options = new HeifDecodingOptions
            {
                // A recent iPhone shoots 10-bit HDR HEIC, which decodes to a 16-bit surface that the 8-bit layout below would read as garbage.
                ConvertHdrToEightBit = true,

                // A receipt photo that is slightly malformed is still worth reading. Strict
                // would reject the whole file over a detail no one is going to look at.
                Strict = false
            };

            // Interleaved RGBA32 lines up with SKColorType.Rgba8888, so the pixels transfer
            // without a per-row channel swap.
            using var image = handle.Decode(HeifColorspace.Rgb, HeifChroma.InterleavedRgba32, options);

            var plane = image.GetPlane(HeifChannel.Interleaved);
            var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

            using var bitmap = new SKBitmap();
            if (!bitmap.InstallPixels(info, plane.Scan0, plane.Stride))
            {
                return null;
            }

            using var skImage = SKImage.FromBitmap(bitmap);
            using var encoded = skImage.Encode(SKEncodedImageFormat.Jpeg, quality);

            return encoded?.ToArray();
        }
        catch (Exception)
        {
            // HeifException for a file this build cannot read, DllNotFoundException when the native library did not ship.
            return null;
        }
    }
}
