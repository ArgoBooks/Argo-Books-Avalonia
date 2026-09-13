using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Services;

namespace ArgoBooks.Services;

/// <summary>
/// Resolves a receipt's displayable page image paths, rendering PDFs to one JPEG per page
/// (cached in temp) and orienting raster images. Shared by every full-size receipt viewer so the
/// PDF-vs-image branching, page naming, and temp caching live in one place. Page files are named
/// <c>&lt;key&gt;_p{n}.jpg</c> (1-based) and the page count is cached in a
/// <c>&lt;key&gt;.pagecount</c> marker so the viewer can reuse pages already rendered for the
/// card thumbnail and skip re-rendering them.
///
/// Every receipt shares the one temp folder, so a receipt's key is its id plus a hash of its file.
/// File names repeat (Scan.pdf, IMG_0001.jpg) and so do ids across companies, and a shared key
/// shows one receipt's image in place of another's.
/// </summary>
public static class ReceiptPageRenderer
{
    private static string TempDir => Path.Combine(Path.GetTempPath(), "ArgoBooks", "Receipts");

    // Hashing the file is the costly part of a key, so it is kept per receipt object and only
    // redone if that receipt's FileData is replaced.
    private static readonly ConditionalWeakTable<Receipt, CachedKey> CacheKeys = new();

    private sealed record CachedKey(string FileData, string Key);

    /// <summary>Path of the JPEG for a given 0-based page index of a receipt.</summary>
    public static string PagePath(Receipt receipt, int zeroBasedIndex)
        => KeyedPagePath(CacheKey(receipt), zeroBasedIndex);

    /// <summary>
    /// Path of the cached image for a non-PDF receipt. Keeps the receipt file's extension, which
    /// the download uses to type the saved file.
    /// </summary>
    public static string ImagePath(Receipt receipt)
        => Path.Combine(TempDir, CacheKey(receipt) + Path.GetExtension(receipt.FileName));

    /// <summary>Ensures the receipt temp directory exists.</summary>
    public static void EnsureTempDir() => Directory.CreateDirectory(TempDir);

    /// <summary>Records how many pages a PDF receipt has, so viewers know the full set.</summary>
    public static void WritePageCount(Receipt receipt, int count) => WriteKeyedPageCount(CacheKey(receipt), count);

    /// <summary>Cached page count for a receipt, or 1 when unknown (no marker yet).</summary>
    public static int CachedPageCount(Receipt receipt) => ReadPageCount(CacheKey(receipt)) ?? 1;

    private static string CacheKey(Receipt receipt)
    {
        var data = receipt.FileData ?? string.Empty;
        if (CacheKeys.TryGetValue(receipt, out var cached) && ReferenceEquals(cached.FileData, data))
            return cached.Key;

        var hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(data.AsSpan())))[..12];
        var key = $"{SafeName(receipt.Id)}-{hash}";
        CacheKeys.AddOrUpdate(receipt, new CachedKey(data, key));
        return key;
    }

    /// <summary>
    /// A temp-file key for content that is not a stored receipt: the name plus a hash of the bytes,
    /// since names repeat and the content behind a name can change.
    /// </summary>
    public static string ContentKey(string name, byte[] bytes)
        => $"{SafeName(Path.GetFileNameWithoutExtension(name))}-{Convert.ToHexString(SHA256.HashData(bytes))[..12]}";

    private static string SafeName(string name)
        => new(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());

    private static string KeyedPagePath(string key, int zeroBasedIndex)
        => Path.Combine(TempDir, $"{key}_p{zeroBasedIndex + 1}.jpg");

    private static string PageCountMarkerPath(string key) => Path.Combine(TempDir, $"{key}.pagecount");

    private static void WriteKeyedPageCount(string key, int count)
    {
        try { File.WriteAllText(PageCountMarkerPath(key), count.ToString()); }
        catch { /* non-critical cache marker */ }
    }

    private static int? ReadPageCount(string key)
    {
        try
        {
            var path = PageCountMarkerPath(key);
            if (File.Exists(path) && int.TryParse(File.ReadAllText(path), out var count) && count > 0)
                return count;
        }
        catch { /* ignore */ }
        return null;
    }

    /// <summary>
    /// Returns the page image paths for a receipt, in order. PDFs render all pages; raster images
    /// return a single oriented file. Returns an empty list when there is no data or rendering
    /// fails. Pages already on disk (e.g. the card's first-page thumbnail) are reused, and only
    /// missing pages are rendered.
    /// </summary>
    /// <param name="receipt">The receipt to render.</param>
    /// <param name="onPage">
    /// Optional progress callback invoked once per page (0-based index + path) as each becomes
    /// available, so the UI can stream pages in instead of waiting for the whole document.
    /// </param>
    public static async Task<IReadOnlyList<string>> GetPagePathsAsync(
        Receipt receipt,
        IProgress<(int Index, string Path)>? onPage = null)
    {
        if (string.IsNullOrEmpty(receipt.FileData))
            return [];

        try
        {
            EnsureTempDir();
            var bytes = Convert.FromBase64String(receipt.FileData);
            var isPdf = receipt.FileType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                        || receipt.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            if (!isPdf)
            {
                var imgPath = ImagePath(receipt);
                if (!File.Exists(imgPath))
                    await File.WriteAllBytesAsync(imgPath, ReceiptImageHelper.FixOrientation(bytes));
                onPage?.Report((0, imgPath));
                return [imgPath];
            }

            return await GetPdfPagePathsAsync(CacheKey(receipt), bytes, onPage);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Renders a PDF that is not a stored receipt, such as a generated report, through the same
    /// page cache and streaming path.
    ///
    /// The cache key is derived from the CONTENT rather than from the caller's name, because a
    /// generated document changes whenever the underlying data does. Keying by name alone would
    /// serve the previous render from cache, and a payroll document showing last week's figures
    /// is worse than one that takes a moment to appear.
    /// </summary>
    /// <param name="name">Used in the temp file name, so the cached pages are recognisable.</param>
    public static async Task<IReadOnlyList<string>> GetPagePathsAsync(
        string name,
        byte[] pdfBytes,
        IProgress<(int Index, string Path)>? onPage = null)
    {
        if (pdfBytes is not { Length: > 0 })
        {
            return [];
        }

        try
        {
            EnsureTempDir();

            return await GetPdfPagePathsAsync(ContentKey(name, pdfBytes), pdfBytes, onPage);
        }
        catch
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<string>> GetPdfPagePathsAsync(
        string key, byte[] bytes, IProgress<(int Index, string Path)>? onPage)
    {
        // Writes a rendered page to disk and reports it. Runs on the render callback thread.
        void WriteAndReport(int index, byte[] jpeg)
        {
            var p = KeyedPagePath(key, index);
            try { File.WriteAllBytes(p, jpeg); } catch { return; }
            onPage?.Report((index, p));
        }

        var known = ReadPageCount(key);

        if (known is int total)
        {
            // We know how many pages exist. Reuse cached pages, render only the missing ones.
            var cached = new List<int>();
            for (var i = 0; i < total; i++)
                if (File.Exists(KeyedPagePath(key, i)))
                    cached.Add(i);

            // Report cached pages first, in order.
            foreach (var i in cached)
                onPage?.Report((i, KeyedPagePath(key, i)));

            var missing = Enumerable.Range(0, total).Except(cached).ToList();
            if (missing.Count > 0)
            {
                var rendered = await PdfThumbnailService.Instance.RenderPdfAllPagesAsync(
                    bytes, onPage: WriteAndReport, skipZeroBasedPages: cached);
                if (rendered == null && cached.Count == 0)
                    return [];
            }

            return BuildExistingPaths(key, total);
        }

        // No marker yet: render the whole document, then record the count.
        var all = await PdfThumbnailService.Instance.RenderPdfAllPagesAsync(bytes, onPage: WriteAndReport);
        if (all == null || all.Length == 0)
            return [];

        WriteKeyedPageCount(key, all.Length);
        return BuildExistingPaths(key, all.Length);
    }

    private static List<string> BuildExistingPaths(string key, int total)
    {
        var paths = new List<string>(total);
        for (var i = 0; i < total; i++)
        {
            var p = KeyedPagePath(key, i);
            if (File.Exists(p))
                paths.Add(p);
        }
        return paths;
    }
}
