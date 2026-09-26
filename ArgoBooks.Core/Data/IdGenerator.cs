using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Data;

/// <summary>
/// The one place new record IDs are made. Each Next method advances that record's counter in
/// <see cref="IdCounters"/> and skips any ID a record already has, so a counter only ever moves
/// forward and a new ID never repeats one already in the company.
/// </summary>
/// <remarks>
/// Every Next method takes an optional <c>taken</c> set of the IDs to avoid, and claims the ID it
/// returns into that set. When a set is given it is the whole check, so it must already hold every
/// ID the company has for that record (an import seeds it that way, then adds the IDs written on
/// the sheet); without one, the company's IDs are read into a set for the call. An import passes
/// the same set for the whole sheet, so rows given IDs before any of them are added to the company
/// still come out distinct. Invoices, quotes and purchase orders also skip a number whose printed
/// form (#INV-2024-00001) is already some record's printed number.
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
        Next(() => ++Counters.Category, n => FormatCategoryId(type, n), taken ?? Ids(companyData.Categories.Select(c => c.Id)));

    /// <summary>Generates a new customer ID (CUS-001).</summary>
    public string NextCustomerId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Customer, n => $"CUS-{n:D3}", taken ?? Ids(companyData.Customers.Select(c => c.Id)));

    /// <summary>Generates a new supplier ID (SUP-001).</summary>
    public string NextSupplierId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Supplier, n => $"SUP-{n:D3}", taken ?? Ids(companyData.Suppliers.Select(s => s.Id)));

    /// <summary>Generates a new product ID (PRD-001).</summary>
    public string NextProductId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Product, n => $"PRD-{n:D3}", taken ?? Ids(companyData.Products.Select(p => p.Id)));

    /// <summary>
    /// Generates an ID (PRD-IMP-001) for a product an import made up because a row named one the
    /// company doesn't have. The import recognizes these by the prefix and lets a later Products
    /// row take the placeholder over, so they are kept apart from PRD- ids.
    /// </summary>
    public string NextPlaceholderProductId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Product, n => $"PRD-IMP-{n:D3}", taken ?? Ids(companyData.Products.Select(p => p.Id)));

    /// <summary>Generates a new location code (LOC-001).</summary>
    public string NextLocationId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Location, n => $"LOC-{n:D3}", taken ?? Ids(companyData.Locations.Select(l => l.Id)));

    /// <summary>
    /// Generates a new employee ID (EMP-001), numbered past the highest one on file or in
    /// <paramref name="taken"/>. The company file has no counter for employees.
    /// </summary>
    public string NextEmployeeId(ISet<string>? taken = null)
    {
        var ids = taken ?? Ids(companyData.Employees.Select(e => e.Id));
        var highest = HighestNumber(ids, "EMP-");
        return Next(() => ++highest, n => $"EMP-{n:D3}", ids);
    }

    /// <summary>
    /// Generates a new pay run ID (PR-0001), numbered past the highest one on file. The company
    /// file has no counter for pay runs.
    /// </summary>
    public string NextPayRunId()
    {
        var ids = Ids(companyData.PayRuns.Select(r => r.Id));
        var highest = HighestNumber(ids, "PR-");
        return Next(() => ++highest, n => $"PR-{n:D4}", ids);
    }

    /// <summary>Generates a new invoice template ID (template-1).</summary>
    public string NextInvoiceTemplateId() =>
        Next(() => ++Counters.InvoiceTemplate, n => $"template-{n}", Ids(companyData.InvoiceTemplates.Select(t => t.Id)));

    #endregion

    #region Transactions

    /// <summary>Generates a new revenue ID (REV-2024-00001), dated by the revenue's own date.</summary>
    public string NextRevenueId(DateTime date, ISet<string>? taken = null) =>
        Next(() => ++Counters.Revenue, n => FormatRevenueId(date, n), taken ?? Ids(companyData.Revenues.Select(r => r.Id)));

    /// <summary>Generates a new expense ID (PUR-2024-00001), dated by the expense's own date.</summary>
    public string NextExpenseId(DateTime date, ISet<string>? taken = null) =>
        Next(() => ++Counters.Expense, n => FormatExpenseId(date, n), taken ?? Ids(companyData.Expenses.Select(e => e.Id)));

    /// <summary>
    /// Generates a new invoice ID (INV-2024-00001) whose display number is not already an
    /// invoice's number either. A <paramref name="taken"/> set must hold the invoices' numbers as
    /// well as their IDs.
    /// </summary>
    public string NextInvoiceId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Invoice, FormatInvoiceId, taken ?? InvoiceIdsAndNumbers(), FormatInvoiceNumber);

    /// <summary>
    /// The display number (#INV-2024-00001) of the invoice ID just generated. Must be called after
    /// <see cref="NextInvoiceId"/>, which moves the counter.
    /// </summary>
    public string NextInvoiceNumber() => FormatInvoiceNumber(Counters.Invoice);

    /// <summary>What the next invoice ID and number would be, without moving the counter.</summary>
    public (string Id, string Number) PeekNextInvoice()
    {
        var n = PeekFreeNumber(Counters.Invoice, FormatInvoiceId, InvoiceIdsAndNumbers(), FormatInvoiceNumber);
        return (FormatInvoiceId(n), FormatInvoiceNumber(n));
    }

    /// <summary>Generates a new quote ID (QUO-2024-00001) whose display number is not already a quote's number.</summary>
    public string NextQuoteId() =>
        Next(() => ++Counters.Quote, FormatQuoteId, QuoteIdsAndNumbers(), FormatQuoteNumber);

    /// <summary>
    /// The display number (#QUO-2024-00001) of the quote ID just generated. Must be called after
    /// <see cref="NextQuoteId"/>, which moves the counter.
    /// </summary>
    public string NextQuoteNumber() => FormatQuoteNumber(Counters.Quote);

    /// <summary>What the next quote's display number would be, without moving the counter.</summary>
    public string PeekNextQuoteNumber() =>
        FormatQuoteNumber(PeekFreeNumber(Counters.Quote, FormatQuoteId, QuoteIdsAndNumbers(), FormatQuoteNumber));

    /// <summary>Generates a new payment ID (PAY-001).</summary>
    public string NextPaymentId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Payment, n => $"PAY-{n:D3}", taken ?? Ids(companyData.Payments.Select(p => p.Id)));

    /// <summary>Generates a new receipt ID (RCP-2024-00001).</summary>
    public string NextReceiptId() =>
        Next(() => ++Counters.Receipt, n => $"RCP-{Year}-{n:D5}", Ids(companyData.Receipts.Select(r => r.Id)));

    /// <summary>Generates a new recurring-invoice schedule ID (REC-INV-00001).</summary>
    public string NextRecurringInvoiceId(ISet<string>? taken = null) =>
        Next(() => ++Counters.RecurringInvoice, n => $"REC-INV-{n:D5}", taken ?? Ids(companyData.RecurringInvoices.Select(r => r.Id)));

    /// <summary>Generates a new recurring-transaction schedule ID (REC-TXN-00001).</summary>
    public string NextRecurringTransactionId() =>
        Next(() => ++Counters.RecurringTransaction, n => $"REC-TXN-{n:D5}", Ids(companyData.RecurringTransactions.Select(r => r.Id)));

    #endregion

    #region Inventory, rentals and tracking

    /// <summary>Generates a new inventory item ID (INV-ITM-00001).</summary>
    public string NextInventoryItemId(ISet<string>? taken = null) =>
        Next(() => ++Counters.InventoryItem, n => $"INV-ITM-{n:D5}", taken ?? Ids(companyData.Inventory.Select(i => i.Id)));

    /// <summary>Generates a new stock adjustment ID (ADJ-00001).</summary>
    public string NextStockAdjustmentId(ISet<string>? taken = null) =>
        Next(() => ++Counters.StockAdjustment, n => $"ADJ-{n:D5}", taken ?? Ids(companyData.StockAdjustments.Select(a => a.Id)));

    /// <summary>Generates a new stock transfer ID (TRF-00001).</summary>
    public string NextStockTransferId() =>
        Next(() => ++Counters.StockTransfer, n => $"TRF-{n:D5}", Ids(companyData.StockTransfers.Select(t => t.Id)));

    /// <summary>
    /// Generates a new purchase order ID (PO-00001) whose display number is not already an
    /// order's number either. A <paramref name="taken"/> set must hold the orders' numbers as well
    /// as their IDs.
    /// </summary>
    public string NextPurchaseOrderId(ISet<string>? taken = null) =>
        Next(() => ++Counters.PurchaseOrder, n => $"PO-{n:D5}",
            taken ?? Ids(companyData.PurchaseOrders.SelectMany(p => new[] { p.Id, p.PoNumber })), FormatPurchaseOrderNumber);

    /// <summary>
    /// The display number (#PO-2024-001) of the purchase order ID just generated. Must be called
    /// after <see cref="NextPurchaseOrderId"/>, which moves the counter.
    /// </summary>
    public string NextPurchaseOrderNumber() => FormatPurchaseOrderNumber(Counters.PurchaseOrder);

    /// <summary>Generates a new rental item ID (RNT-ITM-001).</summary>
    public string NextRentalItemId(ISet<string>? taken = null) =>
        Next(() => ++Counters.RentalItem, n => $"RNT-ITM-{n:D3}", taken ?? Ids(companyData.RentalInventory.Select(r => r.Id)));

    /// <summary>Generates a new rental record ID (RNT-001).</summary>
    public string NextRentalId() =>
        Next(() => ++Counters.Rental, n => $"RNT-{n:D3}", Ids(companyData.Rentals.Select(r => r.Id)));

    /// <summary>Generates a new return ID (RET-001).</summary>
    public string NextReturnId(ISet<string>? taken = null) =>
        Next(() => ++Counters.Return, n => $"RET-{n:D3}", taken ?? Ids(companyData.Returns.Select(r => r.Id)));

    /// <summary>Generates a new lost/damaged record ID (LOST-001).</summary>
    public string NextLostDamagedId(ISet<string>? taken = null) =>
        Next(() => ++Counters.LostDamaged, n => $"LOST-{n:D3}", taken ?? Ids(companyData.LostDamaged.Select(l => l.Id)));

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

    private static string FormatPurchaseOrderNumber(int number) => $"#PO-{Year}-{number:D3}";

    #endregion

    /// <summary>
    /// The highest number among <paramref name="ids"/>: the part after the last hyphen, with
    /// <paramref name="prefix"/> removed first when present (CUS-001 is 1, INV-2024-00042 is 42).
    /// Used to bring a counter past IDs that did not move it.
    /// </summary>
    public static int HighestNumber(IEnumerable<string?> ids, string prefix)
    {
        var max = 0;
        foreach (var id in ids)
        {
            if (string.IsNullOrEmpty(id)) continue;

            var rest = id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? id[prefix.Length..] : id;
            var lastPart = rest.Split('-')[^1];

            if (int.TryParse(lastPart, out var num) && num > max)
                max = num;
        }
        return max;
    }

    private HashSet<string> InvoiceIdsAndNumbers() =>
        Ids(companyData.Invoices.SelectMany(i => new[] { i.Id, i.InvoiceNumber }));

    private HashSet<string> QuoteIdsAndNumbers() =>
        Ids(companyData.Quotes.SelectMany(q => new[] { q.Id, q.QuoteNumber }));

    private static HashSet<string> Ids(IEnumerable<string?> ids) =>
        new(ids.OfType<string>().Where(id => id.Length > 0), StringComparer.OrdinalIgnoreCase);

    // An ID typed by hand, given in a rename, or brought in by an import doesn't move the
    // counter, so the counter can reach one that is already taken.
    private static string Next(Func<int> advance, Func<int, string> format, ISet<string> taken,
        Func<int, string>? number = null)
    {
        int n;
        do n = advance();
        while (IsTaken(n, format, taken, number));

        var id = format(n);
        taken.Add(id);
        if (number != null) taken.Add(number(n));
        return id;
    }

    private static int PeekFreeNumber(int counter, Func<int, string> format, ISet<string> taken, Func<int, string>? number)
    {
        var n = counter + 1;
        while (IsTaken(n, format, taken, number)) n++;
        return n;
    }

    private static bool IsTaken(int n, Func<int, string> format, ISet<string> taken, Func<int, string>? number) =>
        taken.Contains(format(n)) || (number != null && taken.Contains(number(n)));
}
