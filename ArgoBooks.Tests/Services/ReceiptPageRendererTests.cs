using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Services;
using SkiaSharp;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Every receipt's rendered pages share one temp folder, so the cache key has to be unique per
/// receipt. Keyed by file name, two receipts both called IMG_0001.jpg showed and exported each
/// other's image.
/// </summary>
public class ReceiptPageRendererTests
{
    private static Receipt ImageReceipt(string id, string fileName, byte[] bytes) => new()
    {
        Id = id,
        FileName = fileName,
        FileType = "image/jpeg",
        FileData = Convert.ToBase64String(bytes)
    };

    /// <summary>
    /// A real encoded image, because the renderer writes nothing it cannot decode. The colour
    /// makes each one's bytes different, which is what the cache key is meant to tell apart.
    /// </summary>
    private static byte[] JpegBytes(byte red)
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(new SKColor(red, 0, 0));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }

    [Fact]
    public async Task GetPagePaths_TwoReceiptsWithTheSameFileName_EachGetsItsOwnImage()
    {
        var sharedName = $"IMG_{Guid.NewGuid():N}.jpg";
        byte[] firstBytes = JpegBytes(10), secondBytes = JpegBytes(200);
        var first = ImageReceipt($"RCP-{Guid.NewGuid():N}", sharedName, firstBytes);
        var second = ImageReceipt($"RCP-{Guid.NewGuid():N}", sharedName, secondBytes);

        var firstPath = Assert.Single(await ReceiptPageRenderer.GetPagePathsAsync(first));
        var secondPath = Assert.Single(await ReceiptPageRenderer.GetPagePathsAsync(second));

        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal(secondBytes, await File.ReadAllBytesAsync(secondPath));
    }

    /// <summary>Receipt ids are per-company counters, so every company has an RCP-2026-00001.</summary>
    [Fact]
    public async Task GetPagePaths_SameReceiptIdInAnotherCompany_DoesNotReuseItsImage()
    {
        var id = $"RCP-{Guid.NewGuid():N}";
        byte[] bytesA = JpegBytes(10), bytesB = JpegBytes(200);
        var companyA = ImageReceipt(id, "receipt.jpg", bytesA);
        var companyB = ImageReceipt(id, "receipt.jpg", bytesB);

        await ReceiptPageRenderer.GetPagePathsAsync(companyA);
        var companyBPath = Assert.Single(await ReceiptPageRenderer.GetPagePathsAsync(companyB));

        Assert.Equal(bytesB, await File.ReadAllBytesAsync(companyBPath));
    }

    [Fact]
    public void PageCount_OfOneScanPdf_IsNotReadForAnotherScanPdf()
    {
        ReceiptPageRenderer.EnsureTempDir();
        var sharedName = $"Scan_{Guid.NewGuid():N}.pdf";
        var first = ImageReceipt($"RCP-{Guid.NewGuid():N}", sharedName, [1]);
        var second = ImageReceipt($"RCP-{Guid.NewGuid():N}", sharedName, [2]);

        ReceiptPageRenderer.WritePageCount(first, 4);

        Assert.Equal(4, ReceiptPageRenderer.CachedPageCount(first));
        Assert.Equal(1, ReceiptPageRenderer.CachedPageCount(second));
    }

    /// <summary>Download names the saved file from the cached image's extension.</summary>
    [Fact]
    public void ImagePath_KeepsTheReceiptFilesExtension()
    {
        var receipt = ImageReceipt("RCP-2026-00001", "IMG_0001.webp", [1]);

        Assert.Equal(".webp", Path.GetExtension(ReceiptPageRenderer.ImagePath(receipt)));
    }
}
