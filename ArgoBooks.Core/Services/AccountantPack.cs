using System.Globalization;
using System.IO.Compression;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// What goes into the year-end pack a business sends its accountant: the accounting statements as
/// PDFs, every transaction in a spreadsheet, and the receipts behind them. The files themselves come
/// from the existing report and spreadsheet exports; this decides what is included and bundles it.
/// </summary>
public static class AccountantPack
{
    /// <summary>
    /// The most the emailed attachments may add up to, before base64. The website takes about 10 MB
    /// per request and base64 grows the payload by a third.
    /// </summary>
    public const long MaxEmailBytes = 7 * 1024 * 1024;

    public const string DownloadUrl = "https://argorobots.com/downloads/?source=loop-accountant-pack";

    public const string TransactionsFileName = "Transactions.xlsx";

    public static readonly IReadOnlyList<(string TemplateName, string FileName)> Reports =
    [
        (ReportTemplateFactory.TemplateNames.IncomeStatement, "Income Statement.pdf"),
        (ReportTemplateFactory.TemplateNames.BalanceSheet, "Balance Sheet.pdf"),
        (ReportTemplateFactory.TemplateNames.CashFlowStatement, "Cash Flow Statement.pdf"),
        (ReportTemplateFactory.TemplateNames.GeneralLedger, "General Ledger.pdf"),
        (ReportTemplateFactory.TemplateNames.TaxSummary, "Tax Summary.pdf")
    ];

    public static readonly IReadOnlyList<string> TransactionSheets =
        ["Revenue", "Expenses", "Invoices", "Invoice Line Items", "Payments"];

    /// <summary>
    /// The receipts on expenses and revenue dated in the range, each with its name in the pack.
    /// Chosen by the transaction's date, which is the date the accountant books it on, and each
    /// receipt listed once even when two transactions share it.
    /// </summary>
    public static List<(string EntryName, Receipt Receipt)> ReceiptsInRange(CompanyData data, DateTime start, DateTime end)
    {
        var seen = new HashSet<string>();
        var result = new List<(string EntryName, Receipt Receipt)>();

        var transactions = data.Expenses.Cast<Transaction>().Concat(data.Revenues)
            .Where(t => t.Date.Date >= start.Date && t.Date.Date <= end.Date && !string.IsNullOrEmpty(t.ReceiptId))
            .OrderBy(t => t.Date);

        foreach (var t in transactions)
        {
            if (!seen.Add(t.ReceiptId!) || data.GetReceipt(t.ReceiptId!) is not { FileData.Length: > 0 } receipt)
                continue;

            result.Add(($"Receipts/{t.Date:yyyy-MM-dd} {t.Id}{Path.GetExtension(receipt.FileName)}", receipt));
        }

        return result;
    }

    /// <summary>
    /// Whether anything at all falls in the range, so an empty year is caught before it is sent
    /// as a set of blank statements.
    /// </summary>
    public static bool HasDataInRange(CompanyData data, DateTime start, DateTime end)
    {
        bool InRange(DateTime date) => date.Date >= start.Date && date.Date <= end.Date;

        return data.Expenses.Any(e => InRange(e.Date))
               || data.Revenues.Any(r => InRange(r.Date))
               || data.Invoices.Any(i => InRange(i.IssueDate))
               || data.Payments.Any(p => InRange(p.Date));
    }

    /// <summary>The number of bytes a base64 string decodes to, without decoding it.</summary>
    public static long DecodedSize(string base64)
    {
        var padding = base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith('=') ? 1 : 0;
        return base64.Length / 4L * 3 - padding;
    }

    /// <summary>"2025" for a calendar year, otherwise the two dates.</summary>
    public static string PeriodLabel(DateTime start, DateTime end) =>
        start.Year == end.Year && start is { Month: 1, Day: 1 } && end is { Month: 12, Day: 31 }
            ? start.Year.ToString(CultureInfo.InvariantCulture)
            : $"{start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to {end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    public static string Readme(string companyName, string period, bool hasReports, bool hasTransactions, int receiptCount)
    {
        var lines = new List<string> { $"{companyName}: books for {period}", "", "In this pack:" };
        if (hasReports)
            lines.Add("- Reports: Income Statement, Balance Sheet, Cash Flow Statement, General Ledger and Tax Summary, as PDFs.");
        if (hasTransactions)
            lines.Add($"- {TransactionsFileName}: every revenue, expense, invoice and payment in the period, one sheet each.");
        if (receiptCount > 0)
            lines.Add($"- Receipts: {receiptCount} files, each named by its date and the transaction it belongs to.");

        lines.Add("");
        lines.Add("Prepared in Argo Books, free accounting software for small businesses:");
        lines.Add(DownloadUrl);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Writes the pack as a zip. Files already on disk are added from their paths, and receipts from
    /// the bytes stored in the company file.
    /// </summary>
    public static void WriteZip(Stream destination, IEnumerable<(string EntryName, string FilePath)> files,
        IEnumerable<(string EntryName, Receipt Receipt)> receipts, string? readme)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var (entryName, filePath) in files)
            archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);

        foreach (var (entryName, receipt) in receipts)
        {
            var bytes = Convert.FromBase64String(receipt.FileData!);
            using var stream = archive.CreateEntry(entryName, CompressionLevel.Optimal).Open();
            stream.Write(bytes, 0, bytes.Length);
        }

        if (readme != null)
        {
            using var writer = new StreamWriter(archive.CreateEntry("README.txt", CompressionLevel.Optimal).Open());
            writer.Write(readme);
        }
    }
}
