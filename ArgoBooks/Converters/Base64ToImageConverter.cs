using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ArgoBooks.Converters;

/// <summary>
/// Converts a base64-encoded PNG string to a Bitmap for display.
/// </summary>
public class Base64ToImageConverter : IValueConverter
{
    /// <summary>The 220px template card at 2x scaling. Thumbnails are captured far larger.</summary>
    private const int MaxDecodeWidth = 440;

    /// <summary>
    /// Nothing disposes a bitmap handed to a binding, and bindings re-evaluate every time the
    /// modal opens, so decoding afresh each time strands the previous one.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();

    private const int MaxCacheEntries = 32;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string base64 || string.IsNullOrEmpty(base64))
            return null;

        if (Cache.TryGetValue(base64, out var cached))
            return cached;

        try
        {
            var bytes = System.Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);
            var bitmap = Bitmap.DecodeToWidth(stream, MaxDecodeWidth);

            // Not disposed: a card on screen may still be drawing one of these.
            if (Cache.Count >= MaxCacheEntries)
                Cache.Clear();

            return Cache.GetOrAdd(base64, bitmap);
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
