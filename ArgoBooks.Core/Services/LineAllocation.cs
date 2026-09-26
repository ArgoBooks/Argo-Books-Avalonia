using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>Which of a transaction's USD amounts is shared out across its lines.</summary>
public enum LineAllocationBasis
{
    /// <summary><c>EffectiveTotalUSD</c>, tax included: sales by product, category charts, Insights.</summary>
    Gross,

    /// <summary><c>EffectiveSubtotalUSD</c>, tax excluded: the Income Statement and General Ledger.</summary>
    PreTax
}

/// <summary>One line's share of a transaction's USD amount.</summary>
public readonly record struct LineShare(LineItem Line, decimal AmountUSD);

/// <summary>
/// A transaction's USD amount shared out across its lines. When the lines can't take it (there are
/// none, or they add up to 0) <see cref="IsSplit"/> is false, every share is 0, and the whole amount
/// is <see cref="UnallocatedUSD"/>; each caller decides where that goes.
/// </summary>
public sealed record LineAllocationResult(IReadOnlyList<LineShare> Shares, bool IsSplit, decimal UnallocatedUSD);

/// <summary>
/// The one way a transaction's USD amount is split across its line items (docs/Calculations.md §13):
/// in proportion to each line's own <see cref="LineItem.Subtotal"/>, at full precision, with the last
/// line taking the remainder so the shares add up to the amount exactly (Rule 3).
/// </summary>
public static class LineAllocation
{
    public static LineAllocationResult Allocate(Transaction transaction, LineAllocationBasis basis) =>
        Allocate(transaction.LineItems, basis == LineAllocationBasis.Gross
            ? transaction.EffectiveTotalUSD
            : transaction.EffectiveSubtotalUSD);

    public static LineAllocationResult Allocate(IReadOnlyList<LineItem> lines, decimal amountUSD)
    {
        var linesTotal = lines.Sum(li => li.Subtotal);
        if (linesTotal == 0)
            return new LineAllocationResult(lines.Select(li => new LineShare(li, 0m)).ToList(), false, amountUSD);

        var shares = new List<LineShare>(lines.Count);
        var allocated = 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            var share = i == lines.Count - 1
                ? amountUSD - allocated
                : lines[i].Subtotal / linesTotal * amountUSD;
            allocated += share;
            shares.Add(new LineShare(lines[i], share));
        }
        return new LineAllocationResult(shares, true, 0m);
    }
}
