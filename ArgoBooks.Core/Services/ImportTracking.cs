using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Where an expense or revenue that a scan or import created came from, and which of its fields the
/// user changed afterwards. A correction to an imported record is the clearest sign of how well it was read.
/// </summary>
public static class ImportTracking
{
    public const string ReceiptScan = "receipt-scan";
    public const string BankPdf = "bank-pdf";
    public const string BankPhoto = "bank-photo";
    public const string BankFile = "bank-file";
    public const string Spreadsheet = "spreadsheet";

    /// <summary>A change long after the import is ordinary bookkeeping, not a correction of how it was read.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>The source for rows read from a bank statement file, by its extension.</summary>
    public static string ForStatementFile(string? fileName) =>
        Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
        {
            ".pdf" => BankPdf,
            ".jpg" or ".jpeg" or ".png" or ".heic" or ".heif" or ".webp" or ".bmp" or ".tif" or ".tiff" or ".gif" => BankPhoto,
            _ => BankFile
        };

    /// <summary>The import a record came from, while a change to it still says something about that import.</summary>
    public static string? SourceOf(Transaction record, DateTime utcNow) =>
        record.ImportSource != null && utcNow - record.CreatedAt <= Window ? record.ImportSource : null;

    /// <summary>The parts of a record a correction is about. The time of day and the generated description are left out, because saving the form changes them without the user doing so.</summary>
    public sealed record Snapshot(DateTime Day, decimal Total, decimal Tax, string? CounterpartyId, string Items);

    public static Snapshot Capture(Transaction record, string? counterpartyId) => new(
        record.Date.Date,
        record.Total,
        record.TaxAmount,
        string.IsNullOrEmpty(counterpartyId) ? null : counterpartyId,
        string.Join("|", record.LineItems.Select(li => li.ProductId ?? "").OrderBy(id => id, StringComparer.Ordinal)));

    /// <summary>The changed fields as a comma list, such as "date,amount", or empty when nothing the import set has changed.</summary>
    public static string ChangedFields(Snapshot before, Snapshot after, string counterpartyName)
    {
        var fields = new List<string>();
        if (before.Day != after.Day) fields.Add("date");
        if (before.Total != after.Total) fields.Add("amount");
        if (before.Tax != after.Tax) fields.Add("tax");
        if (before.CounterpartyId != after.CounterpartyId) fields.Add(counterpartyName);
        if (before.Items != after.Items) fields.Add("product");
        return string.Join(",", fields);
    }
}
