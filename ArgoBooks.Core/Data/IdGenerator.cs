using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Data;

/// <summary>
/// The one place new record IDs are made. Each Next method advances that record's counter in
/// <see cref="IdCounters"/> and skips any ID a record already has, so a counter only ever moves
/// forward and a new ID never repeats one already in the company.
/// </summary>
/// <remarks>
/// Every Next method takes an optional <c>taken</c> set of IDs to avoid besides the ones already in
/// the company, and claims the ID it returns into that set. An import passes the same set for the
/// whole sheet, so rows given IDs before any of them are added to the company still come out distinct.
/// Year-based IDs use the UTC year, except revenue and expense IDs, which carry their own date's year.
/// </remarks>
public class IdGenerator(CompanyData companyData)
{
    private IdCounters Counters => companyData.IdCounters;

    private static int Year => DateTime.UtcNow.Year;

    #region Entities

    /// <summary>
    /// Generates a new category ID (CAT-REV-001, CAT-EXP-001, CAT-RNT-001, CAT-GEN-001). Older
    /// files also hold CAT-SAL and CAT-PUR ids; those stay as they are.
    /// </summary>
    public string NextCategoryId(CategoryType type, ISet<string>? taken = null) =>
        Next(() => ++Counters.Category, n => FormatCategoryId(type, n), companyData.Categories, c => c.Id, taken);

    /// <summary>Generates a new customer ID (CUS-001).</summary>
    public string NextCustomerId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Customer, n => $"CUS-{n:D3}", companyData.Customers, c => c.Id, taken);

    /// <summary>Generates a new supplier ID (SUP-001).</summary>
    public string NextSupplierId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Supplier, n => $"SUP-{n:D3}", companyData.Suppliers, s => s.Id, taken);

    /// <summary>Generates a new product ID (PRD-001).</summary>
    public string NextProductId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Product, n => $"PRD-{n:D3}", companyData.Products, p => p.Id, taken);

    /// <summary>
    /// Generates an ID (PRD-IMP-001) for a product an import made up because a row named one the
    /// company doesn't have. The import recognizes these by the prefix and lets a later Products
    /// row take the placeholder over, so they are kept apart from PRD- ids.
    /// </summary>
    public string NextPlaceholderProductId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Product, n => $"PRD-IMP-{n:D3}", companyData.Products, p => p.Id, taken);

    /// <summary>Generates a new location code (LOC-001).</summary>
    public string NextLocationId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Location, n => $"LOC-{n:D3}", companyData.Locations, l => l.Id, taken);

    /// <summary>
    /// Generates a new employee ID (EMP-001), numbered past the highest one on file. The company
    /// file has no counter for employees.
    /// </summary>
    public string NextEmployeeId(ISet<string>? taken = null)
    {
        var highest = HighestNumber(companyData.Employees.Select(e => e.Id), "EMP-");
        return Next(() => ++highest, n => $"EMP-{n:D3}", companyData.Employees, e => e.Id, taken);
    }

    /// <summary>
    /// Generates a new pay run ID (PR-0001), numbered past the highest one on file. The company
    /// file has no counter for pay runs.
    /// </summary>
    public string NextPayRunId()
    {
        var highest = HighestNumber(companyData.PayRuns.Select(r => r.Id), "PR-");
        return Next(() => ++highest, n => $"PR-{n:D4}", companyData.PayRuns, r => r.Id, null);
    }

    /// <summary>Generates a new invoice template ID (template-1).</summary>
    public string NextInvoiceTemplateId() =>
        Next(() => ++Counters.InvoiceTemplate, n => $"template-{n}", companyData.InvoiceTemplates, t => t.Id, null);

    #endregion

    #region Transactions

    /// <summary>Generates a new revenue ID (REV-2024-00001), dated by the revenue's own date.</summary>
    public string NextRevenueId(DateTime date, ISet<string>? taken = null) =>
        Next(() => ++Counters.Revenue, n => FormatRevenueId(date, n), companyData.Revenues, r => r.Id, taken);

    /// <summary>Generates a new expense ID (PUR-2024-00001), dated by the expense's own date.</summary>
    public string NextExpenseId(DateTime date, ISet<string>? taken = null) =>
        Next(() => ++Counters.Expense, n => FormatExpenseId(date, n), companyData.Expenses, e => e.Id, taken);

    /// <summary>Generates a new invoice ID (INV-2024-00001).</summary>
    public string NextInvoiceId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Invoice, FormatInvoiceId, companyData.Invoices, i => i.Id, taken);

    /// <summary>
    /// The display number (#INV-2024-00001) of the invoice ID just generated. Must be called after
    /// <see cref="NextInvoiceId"/>, which moves the counter.
    /// </summary>
    public string NextInvoiceNumber() => FormatInvoiceNumber(Counters.Invoice);

    /// <summary>What the next invoice ID and number would be, without moving the counter.</summary>
    public (string Id, string Number) PeekNextInvoice()
    {
        var n = PeekFreeNumber(Counters.Invoice, FormatInvoiceId, id => companyData.Invoices.Any(i => i.Id == id));
        return (FormatInvoiceId(n), FormatInvoiceNumber(n));
    }

    /// <summary>Generates a new quote ID (QUO-2024-00001).</summary>
    public string NextQuoteId() =>
        Next(() => ++Counters.Quote, FormatQuoteId, companyData.Quotes, q => q.Id, null);

    /// <summary>
    /// The display number (#QUO-2024-00001) of the quote ID just generated. Must be called after
    /// <see cref="NextQuoteId"/>, which moves the counter.
    /// </summary>
    public string NextQuoteNumber() => FormatQuoteNumber(Counters.Quote);

    /// <summary>What the next quote's display number would be, without moving the counter.</summary>
    public string PeekNextQuoteNumber() =>
        FormatQuoteNumber(PeekFreeNumber(Counters.Quote, FormatQuoteId, id => companyData.Quotes.Any(q => q.Id == id)));

    /// <summary>Generates a new payment ID (PAY-001).</summary>
    public string NextPaymentId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Payment, n => $"PAY-{n:D3}", companyData.Payments, p => p.Id, taken);

    /// <summary>Generates a new receipt ID (RCP-2024-00001).</summary>
    public string NextReceiptId() =>
        Next(() => ++Counters.Receipt, n => $"RCP-{Year}-{n:D5}", companyData.Receipts, r => r.Id, null);

    /// <summary>Generates a new recurring-invoice schedule ID (REC-INV-00001).</summary>
    public string NextRecurringInvoiceId(ISet<string>? taken = null) =>
        Next(() => ++Counters.RecurringInvoice, n => $"REC-INV-{n:D5}", companyData.RecurringInvoices, r => r.Id, taken);

    /// <summary>Generates a new recurring-transaction schedule ID (REC-TXN-00001).</summary>
    public string NextRecurringTransactionId() =>
        Next(() => ++Counters.RecurringTransaction, n => $"REC-TXN-{n:D5}", companyData.RecurringTransactions, r => r.Id, null);

    #endregion

    #region Inventory, rentals and tracking

    /// <summary>Generates a new inventory item ID (INV-ITM-00001).</summary>
    public string NextInventoryItemId(ISet<string>? taken = null) =>
        Next(() => ++Counters.InventoryItem, n => $"INV-ITM-{n:D5}", companyData.Inventory, i => i.Id, taken);

    /// <summary>Generates a new stock adjustment ID (ADJ-00001).</summary>
    public string NextStockAdjustmentId(ISet<string>? taken = null) =>
        Next(() => ++Counters.StockAdjustment, n => $"ADJ-{n:D5}", companyData.StockAdjustments, a => a.Id, taken);

    /// <summary>Generates a new stock transfer ID (TRF-00001).</summary>
    public string NextStockTransferId() =>
        Next(() => ++Counters.StockTransfer, n => $"TRF-{n:D5}", companyData.StockTransfers, t => t.Id, null);

    /// <summary>Generates a new purchase order ID (PO-00001).</summary>
    public string NextPurchaseOrderId(ISet<string>? taken = null) =>
        Next(() => ++Counters.PurchaseOrder, n => $"PO-{n:D5}", companyData.PurchaseOrders, p => p.Id, taken);

    /// <summary>
    /// The display number (#PO-2024-001) of the purchase order ID just generated. Must be called
    /// after <see cref="NextPurchaseOrderId"/>, which moves the counter.
    /// </summary>
    public string NextPurchaseOrderNumber() => $"#PO-{Year}-{Counters.PurchaseOrder:D3}";

    /// <summary>Generates a new rental item ID (RNT-ITM-001).</summary>
    public string NextRentalItemId(ISet<string>? taken = null) =>
        Next(() => ++Counters.RentalItem, n => $"RNT-ITM-{n:D3}", companyData.RentalInventory, r => r.Id, taken);

    /// <summary>Generates a new rental record ID (RNT-001).</summary>
    public string NextRentalId() =>
        Next(() => ++Counters.Rental, n => $"RNT-{n:D3}", companyData.Rentals, r => r.Id, null);

    /// <summary>Generates a new return ID (RET-001).</summary>
    public string NextReturnId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Return, n => $"RET-{n:D3}", companyData.Returns, r => r.Id, taken);

    /// <summary>Generates a new lost/damaged record ID (LOST-001).</summary>
    public string NextLostDamagedId(ISet<string>? taken = null) =>
        Next(() => ++Counters.LostDamaged, n => $"LOST-{n:D3}", companyData.LostDamaged, l => l.Id, taken);

    #endregion

    #region Formats

    private static string FormatCategoryId(CategoryType type, int number)
    {
        var typePrefix = type switch
        {
            CategoryType.Revenue => "REV",
            CategoryType.Expense => "EXP",
            CategoryType.Rental => "RNT",
            _ => "GEN"
        };
        return $"CAT-{typePrefix}-{number:D3}";
    }

    private static string FormatRevenueId(DateTime date, int number) => $"REV-{date:yyyy}-{number:D5}";

    private static string FormatExpenseId(DateTime date, int number) => $"PUR-{date:yyyy}-{number:D5}";

    private static string FormatInvoiceId(int number) => $"INV-{Year}-{number:D5}";

    public static string FormatInvoiceNumber(int number) => $"#INV-{Year}-{number:D5}";

    private static string FormatQuoteId(int number) => $"QUO-{Year}-{number:D5}";

    private static string FormatQuoteNumber(int number) => $"#QUO-{Year}-{number:D5}";

    #endregion

    // An ID typed by hand, given in a rename, or brought in by an import doesn't move the
    // counter, so the counter can reach one that is already taken.
    private static string Next<T>(Func<int> advance, Func<int, string> format, IEnumerable<T> records,
        Func<T, string> idOf, ISet<string>? taken)
    {
        string id;
        do id = format(advance());
        while ((taken?.Contains(id) ?? false) || records.Any(r => idOf(r) == id));
        taken?.Add(id);
        return id;
    }

    private static int PeekFreeNumber(int counter, Func<int, string> format, Func<string, bool> isTaken)
    {
        var n = counter + 1;
        while (isTaken(format(n))) n++;
        return n;
    }

    private static int HighestNumber(IEnumerable<string> ids, string prefix)
    {
        var highest = 0;
        foreach (var id in ids)
        {
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(id[prefix.Length..], out var n) && n > highest)
                highest = n;
        }
        return highest;
    }
}
