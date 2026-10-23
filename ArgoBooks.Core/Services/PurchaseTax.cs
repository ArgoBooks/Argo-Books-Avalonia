using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// How much of the tax on its purchases a company gets back from the government. A null
/// <see cref="ClaimableRate"/> claims all of it; otherwise at most that share of the purchase's
/// amount before tax is claimed, so 0 claims none.
/// </summary>
public readonly record struct PurchaseTaxRule(decimal? ClaimableRate)
{
    /// <summary>All of it, as with GST, HST and VAT.</summary>
    public static PurchaseTaxRule All => new(null);

    /// <summary>None of it, as with US sales tax.</summary>
    public static PurchaseTaxRule None => new(0m);
}

/// <summary>
/// The tax a company claims back on its purchases, and so what each purchase really costs it, from
/// the company's country and province (docs/Calculations.md Rule 1).
/// </summary>
public static class PurchaseTax
{
    // In these provinces only the 5% GST on a purchase is claimed back; the provincial sales tax is a cost.
    private const decimal FederalGstRate = 0.05m;

    private static readonly HashSet<string> ProvincesWithOwnSalesTax = new(StringComparer.OrdinalIgnoreCase)
    {
        "BC", "British Columbia", "SK", "Saskatchewan", "MB", "Manitoba"
    };

    public static PurchaseTaxRule RuleFor(CompanyData? data) =>
        RuleFor(data?.Settings.Company.Country, data?.Settings.Company.ProvinceState);

    public static PurchaseTaxRule RuleFor(string? country, string? provinceState)
    {
        var name = Countries.NormalizeCountryOrKeep(country);
        if (TaxSystems.For(name) == TaxSystem.SalesTax)
            return PurchaseTaxRule.None;
        if (NeedsProvince(name) && provinceState != null && ProvincesWithOwnSalesTax.Contains(provinceState.Trim()))
            return new PurchaseTaxRule(FederalGstRate);
        return PurchaseTaxRule.All;
    }

    /// <summary>Whether the company's province is needed to know how much tax it claims back.</summary>
    public static bool NeedsProvince(string? country) =>
        Countries.NormalizeCountryOrKeep(country).Equals("Canada", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The tax on an expense that is claimed back, in the expense's own currency. A supplier credit is an
    /// expense below zero, and gives back the claim its purchase made.
    /// </summary>
    public static decimal Claimable(Expense expense, PurchaseTaxRule rule)
    {
        if (rule.ClaimableRate is not { } rate)
            return expense.TaxAmount;
        var sign = expense.Total < 0 ? -1m : 1m;
        return sign * Math.Min(sign * expense.TaxAmount, Math.Max(0m, sign * (expense.Total - expense.TaxAmount) * rate));
    }

    /// <summary>The tax on an expense that is claimed back, in USD.</summary>
    public static decimal ClaimableUSD(Expense expense, PurchaseTaxRule rule)
    {
        if (rule.ClaimableRate == null)
            return expense.EffectiveTaxAmountUSD;
        return expense.TaxAmount == 0 ? 0m : expense.EffectiveTaxAmountUSD * Claimable(expense, rule) / expense.TaxAmount;
    }

    /// <summary>What an expense costs the business in its own currency: its total less the tax claimed back.</summary>
    public static decimal Cost(Expense expense, PurchaseTaxRule rule) => expense.Total - Claimable(expense, rule);

    /// <summary>What an expense costs the business in USD: its total less the tax claimed back.</summary>
    public static decimal CostUSD(Expense expense, PurchaseTaxRule rule) =>
        expense.EffectiveTotalUSD - ClaimableUSD(expense, rule);
}
