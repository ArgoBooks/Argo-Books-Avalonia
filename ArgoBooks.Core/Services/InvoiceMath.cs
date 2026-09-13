using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The per-invoice arithmetic of docs/Calculations.md §4, in one place.
///
/// The form, the live preview, the saved invoice and the rendered HTML each used to carry their
/// own copy of the formula, and they drifted: a line floored at zero while the subtotal summed
/// from the raw figure, so a discount larger than its line printed 0.00 on the line and a negative
/// Subtotal and Total beneath it.
///
/// Nothing here can return a negative. A discount is worth at most the subtotal it is taken from,
/// and the taxable base floors at zero, so an invoice can be free but never owe the customer money.
/// Payment-derived totals (AmountPaid / Balance, §5) belong to <see cref="InvoiceTotalsService"/>.
/// </summary>
public static class InvoiceMath
{
    /// <summary>Sum of the line subtotals, each already quantity x price less its own discount.</summary>
    public static decimal Subtotal(IEnumerable<LineItem>? lineItems) =>
        lineItems?.Sum(li => li.Subtotal) ?? 0m;

    /// <summary>The invoice-level discount in money, capped at the subtotal it comes off.</summary>
    public static decimal Discount(decimal subtotal, decimal amount, bool isPercent)
    {
        var raw = isPercent ? subtotal * (amount / 100m) : amount;
        return Math.Clamp(raw, 0m, Math.Max(0m, subtotal));
    }

    /// <summary>The invoice-level custom fee in money.</summary>
    public static decimal CustomFee(decimal subtotal, decimal amount, bool isPercent) =>
        Math.Max(0m, isPercent ? subtotal * (amount / 100m) : amount);

    /// <summary>What tax is charged on: subtotal less the discount, plus the fee and shipping.</summary>
    public static decimal TaxableBase(decimal subtotal, decimal discount, decimal customFee, decimal shipping) =>
        Math.Max(0m, subtotal - discount + customFee + shipping);

    /// <summary>Tax on the taxable base, or the flat amount when the rate field holds one.</summary>
    public static decimal Tax(decimal taxableBase, decimal taxRate, bool taxIsFixed) =>
        Math.Max(0m, taxIsFixed ? taxRate : taxableBase * (taxRate / 100m));

    /// <summary>The grand total: taxable base plus tax plus the untaxed security deposit.</summary>
    public static decimal Total(decimal taxableBase, decimal tax, decimal securityDeposit) =>
        Math.Max(0m, taxableBase + tax + Math.Max(0m, securityDeposit));
}
