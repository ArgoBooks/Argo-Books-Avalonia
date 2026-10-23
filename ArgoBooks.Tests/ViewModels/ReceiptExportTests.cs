using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Export writes the stored receipt file.
/// </summary>
public class ReceiptExportTests
{
    [Fact]
    public void ExportFile_OfAPdfReceipt_IsTheOriginalPdf()
    {
        var pdf = "%PDF-1.4 two pages"u8.ToArray();
        var receipt = new Receipt
        {
            Id = "RCP-2026-00001",
            FileName = "Scan.pdf",
            FileType = "application/pdf",
            FileData = Convert.ToBase64String(pdf)
        };
        var folder = Directory.CreateTempSubdirectory().FullName;

        var path = ReceiptsPageViewModel.WriteExportFile(receipt, folder, "Receipt_RCP-2026-00001");

        Assert.Equal(".pdf", Path.GetExtension(path));
        Assert.Equal(pdf, File.ReadAllBytes(path!));
    }

    [Fact]
    public void ExportFile_WhereOneAlreadyExists_KeepsItAndAddsANumber()
    {
        var receipt = new Receipt
        {
            Id = "RCP-2026-00001",
            FileName = "Scan.pdf",
            FileData = Convert.ToBase64String("%PDF-1.4 new"u8.ToArray())
        };
        var folder = Directory.CreateTempSubdirectory().FullName;
        var existing = Path.Combine(folder, "Receipt_RCP-2026-00001.pdf");
        File.WriteAllText(existing, "earlier export");

        var path = ReceiptsPageViewModel.WriteExportFile(receipt, folder, "Receipt_RCP-2026-00001");

        Assert.Equal("Receipt_RCP-2026-00001 (2).pdf", Path.GetFileName(path));
        Assert.Equal("earlier export", File.ReadAllText(existing));
    }
}
