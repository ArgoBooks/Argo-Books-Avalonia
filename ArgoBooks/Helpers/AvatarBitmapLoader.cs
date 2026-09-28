using ArgoBooks.Core.Models.Entities;
using Avalonia.Media.Imaging;

namespace ArgoBooks.Helpers;

/// <summary>
/// Loads a customer's or supplier's avatar bitmap from the company temp directory,
/// returning null if there's no avatar or the file fails to decode.
/// </summary>
public static class AvatarBitmapLoader
{
    // List pages rebuild their rows on every filter, sort and page change, so each avatar is
    // decoded once and shared by every row showing it. An avatar replaced at the same path has a
    // new write time or size, which misses the cache and decodes the new image.
    private static readonly Dictionary<string, (DateTime WrittenUtc, long Length, Bitmap Bitmap)> Cache = [];
    // Avatars are 256px, about 256KB decoded, so this caps the cache near 32MB.
    private const int MaxCachedAvatars = 128;

    /// <summary>Forgets every cached avatar. Called when a company closes, since its paths go with it.</summary>
    public static void Clear()
    {
        lock (Cache)
            Cache.Clear();
    }

    public static Bitmap? LoadCustomer(Customer? customer)
    {
        if (customer == null) return null;
        return LoadFromPath(App.CompanyManager?.GetCustomerAvatarPath(customer));
    }

    public static Bitmap? LoadSupplier(Supplier? supplier)
    {
        if (supplier == null) return null;
        return LoadFromPath(App.CompanyManager?.GetSupplierAvatarPath(supplier));
    }

    private static Bitmap? LoadFromPath(string? path)
    {
        if (path == null) return null;
        try
        {
            var file = new FileInfo(path);
            var writtenUtc = file.LastWriteTimeUtc;
            var length = file.Length;

            lock (Cache)
            {
                if (Cache.TryGetValue(path, out var cached) && cached.WrittenUtc == writtenUtc && cached.Length == length)
                    return cached.Bitmap;
            }

            var bitmap = new Bitmap(path);
            lock (Cache)
            {
                // Replaced bitmaps aren't disposed: rows built earlier may still be drawing them.
                if (Cache.Count >= MaxCachedAvatars && !Cache.ContainsKey(path))
                    Cache.Clear();
                Cache[path] = (writtenUtc, length, bitmap);
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
