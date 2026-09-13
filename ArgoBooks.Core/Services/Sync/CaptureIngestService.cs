using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services.Integrations;

namespace ArgoBooks.Core.Services.Sync;

/// <summary>
/// Turns a decrypted phone-scanned <see cref="CapturedTransaction"/> into an
/// <c>Expense</c>/<c>Revenue</c> + <c>Receipt</c> pair in <see cref="CompanyData"/>, replicating the
/// same construction path <c>ReceiptsModalsViewModel.CreateExpenseTransaction</c> /
/// <c>CreateRevenueTransaction</c> use for a desktop-scanned receipt, with <c>Receipt.Source</c> set to
/// <c>"Mobile"</c> instead of <c>"AI Scanned"</c>.
///
/// This is a plain Core service: unlike the ViewModel, it does not record an undo/redo action (that's a
/// UI-only concern tied to <c>App.UndoRedoManager</c>) and it does not call
/// <c>ReceiptsModalsViewModel.ApplyDisplayCurrency</c> (that reads <c>ArgoBooks.Services.CurrencyService
/// .CurrentCurrencyCode</c>, a UI-project static Core cannot reference). Amounts are stamped with the
/// company's default currency (<c>data.Settings.Localization.Currency</c>), matching the same
/// <c>OriginalCurrency</c> stamping <see cref="BankLineImportService"/> does for its own Core-only
/// transaction creation path, and given their USD base (or marked pending and queued) through
/// <see cref="IntegrationRates.ApplyUsdAmounts"/>.
/// </summary>
public static class CaptureIngestService
{
    /// <summary>Bound on <c>CompanyData.IngestedScanUids</c>; oldest entries are trimmed past this.</summary>
    private const int MaxTrackedScanUids = 1000;

    /// <summary>
    /// Ingests a captured transaction into <paramref name="data"/>, adding the new
    /// <c>Expense</c>/<c>Revenue</c> and its linked <c>Receipt</c>. Returns the created transaction id,
    /// or <c>null</c> when <see cref="CapturedTransaction.ScanUid"/> was already ingested previously
    /// (restart-safe de-dupe via <c>CompanyData.IngestedScanUids</c>) - in that case nothing is added.
    /// </summary>
    public static string? Ingest(CompanyData data, CapturedTransaction tx)
    {
        if (tx.Total <= 0)
            throw new ArgumentException("Captured transaction total must be positive.", nameof(tx));

        if (tx.LineItems == null || tx.LineItems.Count == 0)
            throw new ArgumentException("Captured transaction must have at least one line item.", nameof(tx));

        if (!string.IsNullOrEmpty(tx.ScanUid) && data.IngestedScanUids.Contains(tx.ScanUid))
            return null;

        var lineItems = BuildLineItems(data, tx);
        var subtotal = tx.Total - tx.Tax;
        var amount = subtotal > 0 ? subtotal : tx.Total;
        var taxRate = subtotal > 0 && tx.Tax > 0 ? (tx.Tax / subtotal) * 100 : 0;
        var unitPrice = lineItems.Count > 0 ? lineItems.Average(li => li.UnitPrice) : subtotal;
        var description = lineItems.Count > 0 ? lineItems[0].Description : tx.SupplierOrCustomer ?? string.Empty;
        var companyCurrency = string.IsNullOrWhiteSpace(data.Settings.Localization.Currency)
            ? "USD"
            : data.Settings.Localization.Currency;

        var id = tx.Type == CapturedTransactionType.Revenue
            ? IngestRevenue(data, tx, lineItems, amount, taxRate, unitPrice, description, companyCurrency)
            : IngestExpense(data, tx, lineItems, amount, taxRate, unitPrice, description, companyCurrency);

        if (!string.IsNullOrEmpty(tx.ScanUid))
        {
            data.IngestedScanUids.Add(tx.ScanUid);
            if (data.IngestedScanUids.Count > MaxTrackedScanUids)
                data.IngestedScanUids.RemoveRange(0, data.IngestedScanUids.Count - MaxTrackedScanUids);
        }

        return id;
    }

    private static string IngestExpense(
        CompanyData data, CapturedTransaction tx, List<LineItem> lineItems,
        decimal amount, decimal taxRate, decimal unitPrice, string description, string companyCurrency)
    {
        data.IdCounters.Expense++;
        var expenseId = $"PUR-{DateTime.Now:yyyy}-{data.IdCounters.Expense:D5}";

        var receiptId = NextReceiptId(data);

        var expense = new Expense
        {
            Id = expenseId,
            Date = tx.Date,
            SupplierId = FindEntityIdByName(data.Suppliers, s => s.Name, tx.SupplierOrCustomer),
            Description = description,
            LineItems = lineItems,
            Quantity = lineItems.Sum(li => li.Quantity),
            UnitPrice = unitPrice,
            Amount = amount,
            TaxRate = taxRate,
            TaxAmount = tx.Tax,
            Total = tx.Total,
            PaymentMethod = PaymentMethod.Cash,
            ReceiptId = receiptId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        IntegrationRates.ApplyUsdAmounts(expense, companyCurrency, data);

        var receipt = BuildReceipt(tx, receiptId, expenseId, "Expense");

        data.Expenses.Add(expense);
        data.Receipts.Add(receipt);
        return expenseId;
    }

    private static string IngestRevenue(
        CompanyData data, CapturedTransaction tx, List<LineItem> lineItems,
        decimal amount, decimal taxRate, decimal unitPrice, string description, string companyCurrency)
    {
        data.IdCounters.Revenue++;
        var revenueId = $"REV-{DateTime.Now:yyyy}-{data.IdCounters.Revenue:D5}";

        var receiptId = NextReceiptId(data);

        var revenue = new Revenue
        {
            Id = revenueId,
            Date = tx.Date,
            CustomerId = FindEntityIdByName(data.Customers, c => c.Name, tx.SupplierOrCustomer),
            Description = description,
            LineItems = lineItems,
            Quantity = lineItems.Sum(li => li.Quantity),
            UnitPrice = unitPrice,
            Amount = amount,
            Subtotal = amount,
            TaxRate = taxRate,
            TaxAmount = tx.Tax,
            Total = tx.Total,
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = RevenuePaymentStatus.Paid,
            ReceiptId = receiptId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        IntegrationRates.ApplyUsdAmounts(revenue, companyCurrency, data);

        var receipt = BuildReceipt(tx, receiptId, revenueId, "Revenue");

        data.Revenues.Add(revenue);
        data.Receipts.Add(receipt);
        return revenueId;
    }

    private static string NextReceiptId(CompanyData data)
    {
        data.IdCounters.Receipt++;
        return $"RCP-{DateTime.Now:yyyy}-{data.IdCounters.Receipt:D5}";
    }

    private static Receipt BuildReceipt(CapturedTransaction tx, string receiptId, string transactionId, string transactionType)
    {
        var imageBytes = DecodeImage(tx.ImageBase64);
        return new Receipt
        {
            Id = receiptId,
            TransactionId = transactionId,
            TransactionType = transactionType,
            FileName = "receipt.jpg",
            FileType = tx.ImageBase64 != null ? "image/jpeg" : string.Empty,
            FileSize = imageBytes?.Length ?? 0,
            FileData = tx.ImageBase64,
            Amount = tx.Total,
            Date = tx.Date,
            Supplier = tx.SupplierOrCustomer ?? string.Empty,
            Source = "Mobile",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static byte[]? DecodeImage(string? imageBase64)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return null;

        try
        {
            return Convert.FromBase64String(imageBase64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a captured line item's <see cref="LineItem.ProductId"/> by matching
    /// <see cref="CapturedLineItem.ProductName"/> against <c>data.Products</c> (case-insensitive), the
    /// same way the desktop scan flow lets the OCR'd product text resolve to an existing product. Unlike
    /// the ViewModel's scan-review flow, this does not auto-create a new product when there's no match:
    /// that auto-create path is a UI/undo-tracked convenience, out of scope for a background sync
    /// ingest. An unmatched line item is kept with its raw description instead.
    /// </summary>
    private static List<LineItem> BuildLineItems(CompanyData data, CapturedTransaction tx)
    {
        return tx.LineItems.Select(li =>
        {
            var product = FindProductByName(data.Products, li.ProductName);
            return new LineItem
            {
                ProductId = product?.Id,
                Description = product?.Name ?? li.Description,
                Quantity = li.Quantity > 0 ? li.Quantity : 1,
                UnitPrice = li.UnitPrice
            };
        }).Where(li => !string.IsNullOrWhiteSpace(li.Description) || li.ProductId != null).ToList();
    }

    private static Product? FindProductByName(List<Product> products, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return products.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindEntityIdByName<T>(List<T> entities, Func<T, string> nameSelector, string? name)
        where T : BaseEntity
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var match = entities.FirstOrDefault(e => string.Equals(nameSelector(e), name, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }
}
