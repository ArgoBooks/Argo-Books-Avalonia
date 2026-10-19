using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Utilities;

/// <summary>How a transaction is summarised in an expense or revenue list row.</summary>
public static class TransactionDisplay
{
    /// <summary>The first product's name, and " +N more" when the transaction has other lines.</summary>
    public static (string Name, string MoreText) ProductSummary(Transaction transaction)
    {
        if (transaction.LineItems.Count <= 1)
            return (transaction.Description, string.Empty);

        var firstName = transaction.LineItems[0].Description;
        if (string.IsNullOrEmpty(firstName))
            firstName = transaction.Description.Split(',')[0].Trim();

        return (firstName, $" +{transaction.LineItems.Count - 1} more");
    }
}
