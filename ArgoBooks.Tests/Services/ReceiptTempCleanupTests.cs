using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class ReceiptTempCleanupTests
{
    [Fact]
    public async Task AnOldConvertedTiffReceipt_IsDeletedWithItsFolder()
    {
        var folder = Path.Combine(AppPaths.Temp, "ConvertedReceipts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "scan.jpg");
        await File.WriteAllBytesAsync(file, [0xFF, 0xD8]);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8));

        await ReceiptTempCleanup.CleanOldFilesAsync();

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task ARecentConvertedTiffReceipt_IsKept()
    {
        var folder = Path.Combine(AppPaths.Temp, "ConvertedReceipts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "scan.jpg");
        await File.WriteAllBytesAsync(file, [0xFF, 0xD8]);

        try
        {
            await ReceiptTempCleanup.CleanOldFilesAsync();

            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
