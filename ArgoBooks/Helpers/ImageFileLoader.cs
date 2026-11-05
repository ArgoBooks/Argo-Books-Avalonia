using ArgoBooks.Core.Services;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace ArgoBooks.Helpers;

/// <summary>
/// Prepares an image the user picked, for a logo or an avatar.
///
/// The image is copied in when the form it was picked in is saved, which can be minutes later, so
/// it is staged here straight away: the original may have been moved, deleted, or only lent by
/// Windows from inside a zip or a phone by then. A format Skia cannot decode is converted here
/// rather than only for the preview, or the stored image would fail every time it was drawn.
/// </summary>
public static class ImageFileLoader
{
    /// <summary>The staged copy to store, and the bitmap to show.</summary>
    public sealed record PreparedImage(string Path, Bitmap Bitmap);

    /// <summary>
    /// Where a picked image is staged before the caller copies it into the company. The
    /// caller keeps its own copy, so nothing here outlives the pick that produced it.
    /// </summary>
    private static string ConvertedDirectory { get; } = Path.Combine(Core.Services.AppPaths.Temp, "PickedImages");

    /// <summary>
    /// Clears the staging directory. Called at startup rather than after each pick, because
    /// the bitmap handed back is read from that file and the caller decides when it is done
    /// with it. Every file here belongs to a pick from an earlier run and is already copied
    /// into whichever company wanted it.
    /// </summary>
    public static void ClearConvertedCache()
    {
        try
        {
            if (Directory.Exists(ConvertedDirectory))
                Directory.Delete(ConvertedDirectory, recursive: true);
        }
        catch
        {
            // A file still held open by something else is not worth failing a launch over;
            // the next run tries again.
        }
    }

    /// <summary>
    /// Returns null when the file cannot be turned into something drawable, which the caller
    /// must report rather than swallow.
    /// </summary>
    public static PreparedImage? TryPrepare(string pickedPath)
    {
        try
        {
            var data = File.ReadAllBytes(pickedPath);
            var extension = Path.GetExtension(pickedPath);

            // Detected by the decode failing rather than by the extension, so a HEIC someone
            // renamed to .jpg is still handled. Mirrors ReceiptImageHelper.PreprocessForOcr.
            if (!CanDecode(data))
            {
                data = HeifImageDecoder.TryConvertToJpeg(data);
                if (data == null)
                    return null;
                extension = ".jpg";
            }

            var staged = Path.Combine(
                ConvertedDirectory,
                $"{Path.GetFileNameWithoutExtension(pickedPath)}_{Guid.NewGuid():N}{extension}");

            Directory.CreateDirectory(ConvertedDirectory);
            File.WriteAllBytes(staged, data);

            return new PreparedImage(staged, new Bitmap(staged));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns bytes Skia can decode: the originals when they already decode, a JPEG when they
    /// had to be converted, or null when neither is possible.
    /// </summary>
    public static byte[]? TryMakeDecodable(byte[] data)
    {
        if (CanDecode(data))
            return data;

        return HeifImageDecoder.TryConvertToJpeg(data);
    }

    private static bool CanDecode(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            using var codec = SKCodec.Create(stream);
            return codec != null;
        }
        catch
        {
            return false;
        }
    }
}
