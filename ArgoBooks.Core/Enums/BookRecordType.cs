namespace ArgoBooks.Core.Enums;

/// <summary>
/// The kind of book record a bank statement line can be matched against.
/// </summary>
public enum BookRecordType
{
    Expense,
    Revenue,
    Invoice,
    Payment,
    SalesTaxFiling
}
