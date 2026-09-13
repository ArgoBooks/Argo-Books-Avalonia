using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Services;
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

    [Fact]
    public async Task GetPagePaths_TwoReceiptsWithTheSameFileName_EachGetsItsOwnImage()
    {
        var sharedName = $"IMG_{Guid.NewGuid():N}.jpg";
        var first = ImageReceipt($"RCP-{Guid.NewGuid():N}", sharedName, [1, 1, 1]);
        var second = ImageReceipt($"RCP-{Guid.NewGuid():N}", sharedName, [2, 2, 2]);

        var firstPath = Assert.Single(await ReceiptPageRenderer.GetPagePathsAsync(first));
        var secondPath = Assert.Single(await ReceiptPageRenderer.GetPagePathsAsync(second));

        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal(new byte[] { 2, 2, 2 }, await File.ReadAllBytesAsync(secondPath));
    }

    /// <summary>Receipt ids are per-company counters, so every company has an RCP-2026-00001.</summary>
    [Fact]
    public async Task GetPagePaths_SameReceiptIdInAnotherCompany_DoesNotReuseItsImage()
    {
        var id = $"RCP-{Guid.NewGuid():N}";
        var companyA = ImageReceipt(id, "receipt.jpg", [1, 1, 1]);
        var companyB = ImageReceipt(id, "receipt.jpg", [2, 2, 2]);

        await ReceiptPageRenderer.GetPagePathsAsync(companyA);
        var companyBPath = Assert.Single(await ReceiptPageRenderer.GetPagePathsAsync(companyB));

        Assert.Equal(new byte[] { 2, 2, 2 }, await File.ReadAllBytesAsync(companyBPath));
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
