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
/// ID the company has for that record (<see cref="TakenSet"/> builds one); without one, the company's
/// records are searched for each candidate. Anything minting many IDs in one go (an import, a sync,
/// recurring catch-up) builds one set up front and passes it to every call, so rows given IDs before
/// any of them are added to the company still come out distinct and the records are read once rather
/// than once per ID. Invoices, quotes and purchase orders also skip a number whose printed
/// form (#INV-2024-00001) is already some record's printed number.
/// Year-based IDs use the UTC year, except revenue and expense IDs, which carry their own date's year.
/// Counters advance in checked arithmetic, so one at int.MaxValue throws rather than wrapping to a
/// negative ID.
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
        Next(() => checked(++Counters.Category), n => FormatCategoryId(type, n), taken, companyData.Categories.Select(c => c.Id));

    /// <summary>Generates a new customer ID (CUS-001).</summary>
    public string NextCustomerId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.Customer), n => $"CUS-{n:D3}", taken, companyData.Customers.Select(c => c.Id));

    /// <summary>Generates a new supplier ID (SUP-001).</summary>
    public string NextSupplierId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.Supplier), n => $"SUP-{n:D3}", taken, companyData.Suppliers.Select(s => s.Id));

    /// <summary>Generates a new product ID (PRD-001).</summary>
    public string NextProductId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.Product), n => $"PRD-{n:D3}", taken, companyData.Products.Select(p => p.Id));

    /// <summary>
    /// Generates an ID (PRD-IMP-001) for a product an import made up because a row named one the
    /// company doesn't have. The import recognizes these by the prefix and lets a later Products
    /// row take the placeholder over, so they are kept apart from PRD- ids.
    /// </summary>
    public string NextPlaceholderProductId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.Product), n => $"PRD-IMP-{n:D3}", taken, companyData.Products.Select(p => p.Id));

    /// <summary>Generates a new location code (LOC-001).</summary>
    public string NextLocationId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.Location), n => $"LOC-{n:D3}", taken, companyData.Locations.Select(l => l.Id));

    /// <summary>
    /// Generates a new employee ID (EMP-001), numbered past the highest one on file or in
    /// <paramref name="taken"/>. The company file has no counter for employees.
    /// </summary>
    public string NextEmployeeId(ISet<string>? taken = null)
    {
        var ids = companyData.Employees.Select(e => e.Id);
        var highest = HighestNumber(taken ?? ids, "EMP-");
        return Next(() => checked(++highest), n => $"EMP-{n:D3}", taken, ids);
    }

    /// <summary>
    /// Generates a new pay run ID (PR-0001), numbered past the highest one on file. The company
    /// file has no counter for pay runs.
    /// </summary>
    public string NextPayRunId()
    {
        var ids = companyData.PayRuns.Select(r => r.Id);
        var highest = HighestNumber(ids, "PR-");
        return Next(() => checked(++highest), n => $"PR-{n:D4}", null, ids);
    }

    /// <summary>Generates a new invoice template ID (template-1).</summary>
    public string NextInvoiceTemplateId() =>
        Next(() => checked(++Counters.InvoiceTemplate), n => $"template-{n}", null, companyData.InvoiceTemplates.Select(t => t.Id));

    #endregion

    #region Transactions

    /// <summary>Generates a new revenue ID (REV-2024-00001), dated by the revenue's own date.</summary>
    public string NextRevenueId(DateTime date, ISet<string>? taken = null) =>
        Next(() => checked(++Counters.Revenue), n => FormatRevenueId(date, n), taken, companyData.Revenues.Select(r => r.Id));

    /// <summary>Generates a new expense ID (PUR-2024-00001), dated by the expense's own date.</summary>
    public string NextExpenseId(DateTime date, ISet<string>? taken = null) =>
        Next(() => checked(++Counters.Expense), n => FormatExpenseId(date, n), taken, companyData.Expenses.Select(e => e.Id));

    /// <summary>
    /// Generates a new invoice ID (INV-2024-00001) whose display number is not already an
    /// invoice's number either. A <paramref name="taken"/> set must hold the invoices' numbers as
    /// well as their IDs.
    /// </summary>
    public string NextInvoiceId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.Invoice), FormatInvoiceId, taken, InvoiceIdsAndNumbers(), FormatInvoiceNumber);

    /// <summary>
    /// The display number (#INV-2024-00001) of the invoice ID just generated. Must be called after
    /// <see cref="NextInvoiceId"/>, which moves the counter.
    /// </summary>
    public string NextInvoiceNumber() => FormatInvoiceNumber(Counters.Invoice);

    /// <summary>What the next invoice ID and number would be, without moving the counter.</summary>
    public (string Id, string Number) PeekNextInvoice()
    {
        var n = PeekFreeNumber(Counters.Invoice, FormatInvoiceId, FormatInvoiceNumber, InvoiceIdsAndNumbers());
        return (FormatInvoiceId(n), FormatInvoiceNumber(n));
    }

    /// <summary>
    /// Gives back the number of an invoice that was never issued, when it is the last one
    /// <see cref="NextInvoiceId"/> handed out, so the next invoice takes it instead of leaving a gap.
    /// </summary>
    public void ReleaseInvoiceNumber(string invoiceId)
    {
        if (Counters.Invoice > 0 && FormatInvoiceId(Counters.Invoice) == invoiceId)
            Counters.Invoice--;
    }

    /// <summary>Generates a new quote ID (QUO-2024-00001) whose display number is not already a quote's number.</summary>
    public string NextQuoteId() =>
        Next(() => checked(++Counters.Quote), FormatQuoteId, null, QuoteIdsAndNumbers(), FormatQuoteNumber);

    /// <summary>
    /// The display number (#QUO-2024-00001) of the quote ID just generated. Must be called after
    /// <see cref="NextQuoteId"/>, which moves the counter.
    /// </summary>
    public string NextQuoteNumber() => FormatQuoteNumber(Counters.Quote);

    /// <summary>What the next quote's display number would be, without moving the counter.</summary>
    public string PeekNextQuoteNumber() =>
        FormatQuoteNumber(PeekFreeNumber(Counters.Quote, FormatQuoteId, FormatQuoteNumber, QuoteIdsAndNumbers()));

    /// <summary>Generates a new payment ID (PAY-001).</summary>
    public string NextPaymentId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.Payment), n => $"PAY-{n:D3}", taken, companyData.Payments.Select(p => p.Id));

    /// <summary>Generates a new receipt ID (RCP-2024-00001).</summary>
    public string NextReceiptId() =>
        Next(() => checked(++Counters.Receipt), n => $"RCP-{Year}-{n:D5}", null, companyData.Receipts.Select(r => r.Id));

    /// <summary>Generates a new recurring-invoice schedule ID (REC-INV-00001).</summary>
    public string NextRecurringInvoiceId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.RecurringInvoice), n => $"REC-INV-{n:D5}", taken, companyData.RecurringInvoices.Select(r => r.Id));

    /// <summary>Generates a new recurring-transaction schedule ID (REC-TXN-00001).</summary>
    public string NextRecurringTransactionId() =>
        Next(() => checked(++Counters.RecurringTransaction), n => $"REC-TXN-{n:D5}", null, companyData.RecurringTransactions.Select(r => r.Id));

    #endregion

    #region Inventory, rentals and tracking

    /// <summary>Generates a new inventory item ID (INV-ITM-00001).</summary>
    public string NextInventoryItemId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.InventoryItem), n => $"INV-ITM-{n:D5}", taken, companyData.Inventory.Select(i => i.Id));

    /// <summary>Generates a new stock adjustment ID (ADJ-00001).</summary>
    public string NextStockAdjustmentId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.StockAdjustment), n => $"ADJ-{n:D5}", taken, companyData.StockAdjustments.Select(a => a.Id));

    /// <summary>Generates a new stock transfer ID (TRF-00001).</summary>
    public string NextStockTransferId() =>
        Next(() => checked(++Counters.StockTransfer), n => $"TRF-{n:D5}", null, companyData.StockTransfers.Select(t => t.Id));

    /// <summary>
    /// Generates a new purchase order ID (PO-00001) whose display number is not already an
    /// order's number either. A <paramref name="taken"/> set must hold the orders' numbers as well
    /// as their IDs.
    /// </summary>
    public string NextPurchaseOrderId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.PurchaseOrder), n => $"PO-{n:D5}",
            taken, companyData.PurchaseOrders.SelectMany(p => new[] { p.Id, p.PoNumber }), FormatPurchaseOrderNumber);

    /// <summary>
    /// The display number (#PO-2024-001) of the purchase order ID just generated. Must be called
    /// after <see cref="NextPurchaseOrderId"/>, which moves the counter.
    /// </summary>
    public string NextPurchaseOrderNumber() => FormatPurchaseOrderNumber(Counters.PurchaseOrder);

    /// <summary>Generates a new rental item ID (RNT-ITM-001).</summary>
    public string NextRentalItemId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.RentalItem), n => $"RNT-ITM-{n:D3}", taken, companyData.RentalInventory.Select(r => r.Id));

    /// <summary>Generates a new rental record ID (RNT-001).</summary>
    public string NextRentalId() =>
        Next(() => checked(++Counters.Rental), n => $"RNT-{n:D3}", null, companyData.Rentals.Select(r => r.Id));

    /// <summary>Generates a new return ID (RET-001).</summary>
    public string NextReturnId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.Return), n => $"RET-{n:D3}", taken, companyData.Returns.Select(r => r.Id));

    /// <summary>Generates a new lost/damaged record ID (LOST-001).</summary>
    public string NextLostDamagedId(ISet<string>? taken = null) =>
        Next(() => checked(++Counters.LostDamaged), n => $"LOST-{n:D3}", taken, companyData.LostDamaged.Select(l => l.Id));

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
    /// The highest number among the <paramref name="ids"/> written in a record's own format: one of
    /// <paramref name="prefixes"/>, an optional four-digit year, then the number (CUS-001 is 1,
    /// INV-2024-00042 is 42). Used to bring a counter past IDs that did not move it.
    /// </summary>
    /// <remarks>
    /// Any other ID is ignored, and so is a number longer than six digits. Six covers the old
    /// three-digit and current five-digit widths; anything longer was numbered by some other scheme
    /// (a date code like INV-20260315, a phone-length number) and would throw the counter millions
    /// ahead or past int.MaxValue. Ignoring an ID here never lets it be repeated: Next skips any ID
    /// already taken.
    /// </remarks>
    public static int HighestNumber(IEnumerable<string?> ids, params string[] prefixes)
    {
        var max = 0;
        foreach (var id in ids)
        {
            if (string.IsNullOrEmpty(id)) continue;
            foreach (var prefix in prefixes)
            {
                if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && ParseNumber(id.AsSpan(prefix.Length)) is { } number && number > max)
                    max = number;
            }
        }
        return max;
    }

    private const int MaxNumberDigits = 6;

    private static int? ParseNumber(ReadOnlySpan<char> rest)
    {
        if (rest.Length > 5 && rest[4] == '-' && IsDigits(rest[..4]))
            rest = rest[5..];
        return rest.Length is > 0 and <= MaxNumberDigits && IsDigits(rest) ? int.Parse(rest) : null;
    }

    private static bool IsDigits(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
            if (!char.IsAsciiDigit(c)) return false;
        return true;
    }

    /// <summary>
    /// A set of <paramref name="ids"/> to pass as <c>taken</c> to every Next call of one batch.
    /// For invoices, quotes and purchase orders it must hold their printed numbers too.
    /// </summary>
    public static HashSet<string> TakenSet(IEnumerable<string?> ids) =>
        new(ids.OfType<string>().Where(id => id.Length > 0), StringComparer.OrdinalIgnoreCase);

    private IEnumerable<string?> InvoiceIdsAndNumbers() =>
        companyData.Invoices.SelectMany(i => new[] { i.Id, i.InvoiceNumber });

    private IEnumerable<string?> QuoteIdsAndNumbers() =>
        companyData.Quotes.SelectMany(q => new[] { q.Id, q.QuoteNumber });

    // An ID typed by hand, given in a rename, or brought in by an import doesn't move the
    // counter, so the counter can reach one that is already taken.
    private static string Next(Func<int> advance, Func<int, string> format, ISet<string>? taken,
        IEnumerable<string?> companyIds, Func<int, string>? number = null)
    {
        int n;
        do n = advance();
        while (IsTaken(n, format, number, taken, companyIds));

        var id = format(n);
        taken?.Add(id);
        if (number != null) taken?.Add(number(n));
        return id;
    }

    private static int PeekFreeNumber(int counter, Func<int, string> format, Func<int, string>? number, IEnumerable<string?> companyIds)
    {
        var n = checked(counter + 1);
        while (IsTaken(n, format, number, null, companyIds)) n = checked(n + 1);
        return n;
    }

    /// <summary>
    /// Whether number <paramref name="n"/>'s ID or printed number is in use: looked up in
    /// <paramref name="taken"/> when given, else found by one pass over the company's IDs that stops
    /// at the first match, so the usual case (a free number) reads each record once and builds nothing.
    /// </summary>
    private static bool IsTaken(int n, Func<int, string> format, Func<int, string>? number,
        ISet<string>? taken, IEnumerable<string?> companyIds)
    {
        var id = format(n);
        var printed = number?.Invoke(n);
        if (taken != null)
            return taken.Contains(id) || (printed != null && taken.Contains(printed));

        foreach (var existing in companyIds)
        {
            if (string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)
                || (printed != null && string.Equals(existing, printed, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
