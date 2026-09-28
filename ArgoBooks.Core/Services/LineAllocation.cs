using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>Which of a transaction's USD amounts is shared out across its lines.</summary>
public enum LineAllocationBasis
{
    /// <summary><c>EffectiveTotalUSD</c>, tax included: sales by product, category charts, Insights.</summary>
    Gross,

    /// <summary><c>EffectiveSubtotalUSD</c>, tax excluded: the Income Statement and General Ledger.</summary>
    PreTax,

    /// <summary><c>EffectiveTaxAmountUSD</c>: the tax by category and by product charts.</summary>
    Tax
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
/// The one way a transaction's amount is split across its line items (docs/Calculations.md §13): in
/// proportion to each line's own <see cref="LineItem.Subtotal"/>, or for tax to each line's own
/// <see cref="LineItem.TaxAmount"/> when the lines carry tax, at full precision, with the last line
/// that has a weight taking the remainder so the shares add up to the amount exactly (Rule 3).
/// </summary>
public static class LineAllocation
{
    public static LineAllocationResult Allocate(Transaction transaction, LineAllocationBasis basis) => basis switch
    {
        LineAllocationBasis.Gross => Allocate(transaction.LineItems, transaction.EffectiveTotalUSD),
        LineAllocationBasis.PreTax => Allocate(transaction.LineItems, transaction.EffectiveSubtotalUSD),
        _ => AllocateTax(transaction.LineItems, transaction.EffectiveTaxAmountUSD)
    };

    /// <summary>Shares <paramref name="amountUSD"/> by each line's subtotal.</summary>
    public static LineAllocationResult Allocate(IReadOnlyList<LineItem> lines, decimal amountUSD) =>
        Allocate(lines, amountUSD, li => li.Subtotal);

    /// <summary>
    /// Shares a tax amount by each line's own tax when any line carries some, so an untaxed line
    /// takes none of it; otherwise by subtotal, as the tax was worked out on the whole invoice (§4).
    /// </summary>
    public static LineAllocationResult AllocateTax(IReadOnlyList<LineItem> lines, decimal amount) =>
        HasLineTax(lines) ? Allocate(lines, amount, li => li.TaxAmount) : Allocate(lines, amount);

    /// <summary>Whether any line carries a tax of its own.</summary>
    public static bool HasLineTax(IReadOnlyList<LineItem> lines) => lines.Any(li => li.TaxAmount != 0);

    private static LineAllocationResult Allocate(IReadOnlyList<LineItem> lines, decimal amount, Func<LineItem, decimal> weight)
    {
        var weights = lines.Select(weight).ToList();
        var weightTotal = weights.Sum();
        if (weightTotal == 0)
            return new LineAllocationResult(lines.Select(li => new LineShare(li, 0m)).ToList(), false, amount);

        // A line with no weight gets exactly 0, so the remainder goes to the last line that has one.
        var remainderIndex = lines.Count - 1;
        while (weights[remainderIndex] == 0) remainderIndex--;

        var shares = new List<LineShare>(lines.Count);
        var allocated = 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            var share = i == remainderIndex
                ? amount - allocated
                : i > remainderIndex ? 0m : weights[i] / weightTotal * amount;
            allocated += share;
            shares.Add(new LineShare(lines[i], share));
        }
        return new LineAllocationResult(shares, true, 0m);
    }
}
