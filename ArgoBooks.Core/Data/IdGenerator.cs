using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Data;

/// <summary>
/// Service for generating sequential IDs for entities.
/// IDs follow the pattern: PREFIX-SEQUENCE or PREFIX-YEAR-SEQUENCE
/// </summary>
public class IdGenerator(CompanyData companyData)
{
    /// <summary>
    /// Generates a new category ID (CAT-REV-001, CAT-EXP-001, CAT-RNT-001, CAT-GEN-001) that no
    /// category has. Older files also hold CAT-SAL and CAT-PUR ids; those stay as they are.
    /// </summary>
    public string NextCategoryId(CategoryType type) =>
        NextFreeId(() => ++companyData.IdCounters.Category, n => FormatCategoryId(type, n),
            id => companyData.Categories.Any(c => c.Id == id));

    /// <summary>Generates a new revenue ID (REV-2024-00001) that no revenue has, dated by the revenue's own date.</summary>
    public string NextRevenueId(DateTime date) =>
        NextFreeId(() => ++companyData.IdCounters.Revenue, n => FormatRevenueId(date, n),
            id => companyData.Revenues.Any(r => r.Id == id));

    /// <summary>Generates a new expense ID (PUR-2024-00001) that no expense has, dated by the expense's own date.</summary>
    public string NextExpenseId(DateTime date) =>
        NextFreeId(() => ++companyData.IdCounters.Expense, n => FormatExpenseId(date, n),
            id => companyData.Expenses.Any(e => e.Id == id));

    public static string FormatCategoryId(CategoryType type, int number)
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

    public static string FormatRevenueId(DateTime date, int number) => $"REV-{date:yyyy}-{number:D5}";

    public static string FormatExpenseId(DateTime date, int number) => $"PUR-{date:yyyy}-{number:D5}";

    /// <summary>
    /// Generates a new invoice ID (INV-2024-00001).
    /// </summary>
    public string NextInvoiceId()
    {
        companyData.IdCounters.Invoice++;
        return $"INV-{DateTime.UtcNow.Year}-{companyData.IdCounters.Invoice:D5}";
    }

    /// <summary>
    /// Generates a new invoice number for display (#INV-2024-001).
    /// Must be called after NextInvoiceId() which increments the counter.
    /// </summary>
    public string NextInvoiceNumber()
    {
        return $"#INV-{DateTime.UtcNow.Year}-{companyData.IdCounters.Invoice:D5}";
    }

    /// <summary>
    /// Generates a new recurring-invoice schedule ID (REC-INV-00001).
    /// </summary>
    public string NextRecurringInvoiceId()
    {
        companyData.IdCounters.RecurringInvoice++;
        return $"REC-INV-{companyData.IdCounters.RecurringInvoice:D5}";
    }

    /// <summary>
    /// Generates a new recurring-transaction schedule ID (REC-TXN-00001).
    /// </summary>
    public string NextRecurringTransactionId()
    {
        companyData.IdCounters.RecurringTransaction++;
        return $"REC-TXN-{companyData.IdCounters.RecurringTransaction:D5}";
    }

    /// <summary>Generates a new customer ID (CUS-001) that no customer has.</summary>
    public string NextCustomerId() =>
        NextFreeId(() => ++companyData.IdCounters.Customer, n => $"CUS-{n:D3}", id => companyData.Customers.Any(c => c.Id == id));

    /// <summary>Generates a new supplier ID (SUP-001) that no supplier has.</summary>
    public string NextSupplierId() =>
        NextFreeId(() => ++companyData.IdCounters.Supplier, n => $"SUP-{n:D3}", id => companyData.Suppliers.Any(s => s.Id == id));

    /// <summary>Generates a new product ID (PRD-001) that no product has.</summary>
    public string NextProductId() =>
        NextFreeId(() => ++companyData.IdCounters.Product, n => $"PRD-{n:D3}", id => companyData.Products.Any(p => p.Id == id));

    /// <summary>Generates a new location code (LOC-001) that no location has.</summary>
    public string NextLocationId() =>
        NextFreeId(() => ++companyData.IdCounters.Location, n => $"LOC-{n:D3}", id => companyData.Locations.Any(l => l.Id == id));

    // An ID typed by hand, given in a rename, or brought in by an import doesn't move the
    // counter, so the counter can reach one that is already taken.
    public static string NextFreeId(Func<int> advance, Func<int, string> format, Func<string, bool> isTaken)
    {
        string id;
        do id = format(advance());
        while (isTaken(id));
        return id;
    }

    /// <summary>
    /// Peeks at what the next invoice ID and number would be without incrementing the counter.
    /// </summary>
    public (string Id, string Number) PeekNextInvoice()
    {
        var next = companyData.IdCounters.Invoice + 1;
        return (
            $"INV-{DateTime.UtcNow.Year}-{next:D5}",
            $"#INV-{DateTime.UtcNow.Year}-{next:D5}"
        );
    }

}
