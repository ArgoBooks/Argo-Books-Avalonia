using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Data;

/// <summary>
/// Service for generating sequential IDs for entities.
/// IDs follow the pattern: PREFIX-SEQUENCE or PREFIX-YEAR-SEQUENCE
/// </summary>
public class IdGenerator(CompanyData companyData)
{
    /// <summary>
    /// Generates a new category ID (CAT-SAL-001, CAT-PUR-001, CAT-RNT-001).
    /// </summary>
    public string NextCategoryId(CategoryType type)
    {
        companyData.IdCounters.Category++;
        var typePrefix = type switch
        {
            CategoryType.Revenue => "REV",
            CategoryType.Expense => "EXP",
            CategoryType.Rental => "RNT",
            _ => "GEN"
        };
        return $"CAT-{typePrefix}-{companyData.IdCounters.Category:D3}";
    }

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
        NextFreeId("CUS", () => ++companyData.IdCounters.Customer, id => companyData.Customers.Any(c => c.Id == id));

    /// <summary>Generates a new supplier ID (SUP-001) that no supplier has.</summary>
    public string NextSupplierId() =>
        NextFreeId("SUP", () => ++companyData.IdCounters.Supplier, id => companyData.Suppliers.Any(s => s.Id == id));

    /// <summary>Generates a new product ID (PRD-001) that no product has.</summary>
    public string NextProductId() =>
        NextFreeId("PRD", () => ++companyData.IdCounters.Product, id => companyData.Products.Any(p => p.Id == id));

    /// <summary>Generates a new location code (LOC-001) that no location has.</summary>
    public string NextLocationId() =>
        NextFreeId("LOC", () => ++companyData.IdCounters.Location, id => companyData.Locations.Any(l => l.Id == id));

    // An ID typed by hand or given in a rename doesn't move the counter, so the counter can
    // reach one that is already taken.
    private static string NextFreeId(string prefix, Func<int> advance, Func<string, bool> isTaken)
    {
        string id;
        do id = $"{prefix}-{advance():D3}";
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
