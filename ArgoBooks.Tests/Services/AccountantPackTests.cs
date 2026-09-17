using System.IO.Compression;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Which receipts go in the year-end pack sent to an accountant. A receipt left out, or one from the
/// wrong year, is a gap in the books the accountant is signing off on.
/// </summary>
public class AccountantPackTests
{
    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static CompanyData Company()
    {
        var data = new CompanyData();
        data.Receipts.Add(new Receipt { Id = "RCP-1", FileName = "fuel.jpg", FileData = Base64("one") });
        data.Receipts.Add(new Receipt { Id = "RCP-2", FileName = "sale.pdf", FileData = Base64("two") });
        data.Receipts.Add(new Receipt { Id = "RCP-3", FileName = "lost.png", FileData = null });

        data.Expenses.Add(new Expense { Id = "EXP-1", Date = new DateTime(2025, 3, 1, 14, 30, 0), ReceiptId = "RCP-1" });
        data.Revenues.Add(new Revenue { Id = "REV-1", Date = new DateTime(2025, 12, 31, 23, 0, 0), ReceiptId = "RCP-2" });
        data.Expenses.Add(new Expense { Id = "EXP-2", Date = new DateTime(2026, 1, 1), ReceiptId = "RCP-2" });
        data.Expenses.Add(new Expense { Id = "EXP-3", Date = new DateTime(2025, 6, 1), ReceiptId = "RCP-3" });
        data.Expenses.Add(new Expense { Id = "EXP-4", Date = new DateTime(2025, 7, 1) });
        return data;
    }

    // Chosen by the transaction's date, including the last evening of the year, and a receipt with no
    // stored file is skipped rather than written as an empty file.
    [Fact]
    public void ReceiptsInRange_PicksByTransactionDate_AndSkipsReceiptsWithNoFile()
    {
        var receipts = AccountantPack.ReceiptsInRange(Company(), new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));

        Assert.Equal(["Receipts/2025-03-01 EXP-1.jpg", "Receipts/2025-12-31 REV-1.pdf"], receipts.Select(r => r.EntryName));
    }

    [Fact]
    public void ReceiptsInRange_ListsASharedReceiptOnce()
    {
        var receipts = AccountantPack.ReceiptsInRange(Company(), new DateTime(2025, 1, 1), new DateTime(2026, 12, 31));

        Assert.Single(receipts, r => r.Receipt.Id == "RCP-2");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("a longer receipt body")]
    public void DecodedSize_MatchesTheDecodedLength(string text)
    {
        var base64 = Base64(text);

        Assert.Equal(Convert.FromBase64String(base64).Length, AccountantPack.DecodedSize(base64));
    }

    [Fact]
    public void PeriodLabel_NamesACalendarYearByItsYear()
    {
        Assert.Equal("2025", AccountantPack.PeriodLabel(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)));
        Assert.Equal("2025-04-01 to 2026-03-31", AccountantPack.PeriodLabel(new DateTime(2025, 4, 1), new DateTime(2026, 3, 31)));
    }

    [Fact]
    public void WriteZip_HoldsTheFilesReceiptsAndReadme()
    {
        var data = Company();
        var report = Path.Combine(Path.GetTempPath(), $"accountant-pack-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(report, "report");
        try
        {
            using var zip = new MemoryStream();
            AccountantPack.WriteZip(zip,
                [("Reports/Income Statement.pdf", report)],
                AccountantPack.ReceiptsInRange(data, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)),
                "readme");

            zip.Position = 0;
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
            Assert.Equal(
                ["README.txt", "Receipts/2025-03-01 EXP-1.jpg", "Receipts/2025-12-31 REV-1.pdf", "Reports/Income Statement.pdf"],
                archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));

            using var reader = new StreamReader(archive.GetEntry("Receipts/2025-03-01 EXP-1.jpg")!.Open());
            Assert.Equal("one", reader.ReadToEnd());
        }
        finally
        {
            File.Delete(report);
        }
    }
}
