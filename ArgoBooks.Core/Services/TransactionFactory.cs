using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Builds Expense/Revenue transactions from a normalized draft. Pure: the caller is
/// responsible for adding the result to CompanyData and recording undo. Shared by the
/// bank-statement importer (and, optionally, the receipt scanner).
/// </summary>
/// <remarks>
/// OriginalCurrency has deliberately no default. It used to default to "USD",
/// which was silent and only wrong for a company keeping books in another currency: the amount
/// is stored in the company's currency, so labelling it USD made the display convert it at the
/// transaction date and show a pending marker instead of the figure. Every caller knows its own
/// currency, so the compiler asks rather than guessing.
/// </remarks>
public record TransactionDraft(
    DateTime Date,
    string Description,
    decimal Total,
    string? CounterpartyId,
    string? Notes,
    string OriginalCurrency,
    string? ProductId = null);

public static class TransactionFactory
{
    public static Expense CreateExpense(CompanyData data, TransactionDraft draft, ISet<string>? takenIds = null)
    {
        return new Expense
        {
            Id = new IdGenerator(data).NextExpenseId(draft.Date, takenIds),
            Date = draft.Date,
            SupplierId = draft.CounterpartyId,
            Description = draft.Description,
            LineItems = [BuildLine(draft)],
            Quantity = 1,
            UnitPrice = draft.Total,
            Amount = draft.Total,
            Total = draft.Total,
            Notes = draft.Notes ?? string.Empty,
            OriginalCurrency = draft.OriginalCurrency,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static Revenue CreateRevenue(CompanyData data, TransactionDraft draft, ISet<string>? takenIds = null)
    {
        return new Revenue
        {
            Id = new IdGenerator(data).NextRevenueId(draft.Date, takenIds),
            Date = draft.Date,
            CustomerId = draft.CounterpartyId,
            Description = draft.Description,
            LineItems = [BuildLine(draft)],
            Quantity = 1,
            UnitPrice = draft.Total,
            Amount = draft.Total,
            Subtotal = draft.Total,
            Total = draft.Total,
            PaymentStatus = RevenuePaymentStatus.Paid,
            Notes = draft.Notes ?? string.Empty,
            OriginalCurrency = draft.OriginalCurrency,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static LineItem BuildLine(TransactionDraft draft) => new()
    {
        ProductId = draft.ProductId,
        Description = draft.Description,
        Quantity = 1,
        UnitPrice = draft.Total
    };
}
