using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>Keeps the asset bought with an expense in step with the "this is an asset" choice on the expense form.</summary>
public static class ExpenseAssetLink
{
    /// <summary>How to take back, and put back, what <see cref="Apply"/> changed, for the expense's own undo step.</summary>
    public sealed record Change(Action Undo, Action Redo);

    /// <summary>A CRA class picked on the form, with the rate and first-year share CRA gives it for the expense's date.</summary>
    public sealed record CcaChoice(string ClassNumber, decimal RatePercent, decimal FirstYearFactor);

    public static FixedAsset? AssetFor(CompanyData data, string expenseId) =>
        data.Assets.FirstOrDefault(a => a.ExpenseId == expenseId);

    private static void UseCca(FixedAsset asset, CcaChoice cca)
    {
        asset.Method = DepreciationMethod.DecliningBalance;
        asset.RatePercent = cca.RatePercent;
        asset.CcaClass = cca.ClassNumber;
        asset.FirstYearFactor = cca.FirstYearFactor;
        asset.UsefulLifeYears = null;
    }

    // Renaming the expense only reaches assets in the books, so one put back after a rename takes the expense's id as it is now.
    private static void Restore(CompanyData data, FixedAsset asset, Expense expense)
    {
        asset.ExpenseId = expense.Id;
        data.Assets.RestoreRecord(asset);
    }

    /// <summary>Adds, updates or removes the asset bought with <paramref name="expense"/> to match the form, or returns null when nothing changed.</summary>
    /// <param name="costBeforeEdit">What the expense cost the business before this save (<see cref="PurchaseTax.Cost"/>), or null for a new expense.</param>
    /// <param name="currencyBeforeEdit">The expense's currency as it was before this save, or null for a new expense.</param>
    /// <param name="cca">The CRA class picked, which depreciates it by CRA's method, or null to depreciate it over <paramref name="usefulLifeYears"/>.</param>
    public static Change? Apply(CompanyData data, Expense expense, decimal? costBeforeEdit, string? currencyBeforeEdit,
        bool isAsset, AssetType type, decimal? usefulLifeYears, CcaChoice? cca = null)
    {
        var existing = AssetFor(data, expense.Id);
        var rule = PurchaseTax.RuleFor(data);

        if (!isAsset)
        {
            if (existing == null)
                return null;
            data.Assets.RemoveRecord(existing);
            return new Change(() => Restore(data, existing, expense), () => data.Assets.RemoveRecord(existing));
        }

        if (existing == null)
        {
            var asset = new FixedAsset
            {
                Id = new IdGenerator(data).NextAssetId(),
                Name = string.IsNullOrWhiteSpace(expense.Description) ? expense.Id : expense.Description,
                Type = type,
                PurchaseDate = expense.Date,
                Cost = PurchaseTax.Cost(expense, rule),
                Currency = expense.OriginalCurrency,
                ExpenseId = expense.Id,
                Method = DepreciationMethod.StraightLine,
                UsefulLifeYears = type == AssetType.Land ? null : usefulLifeYears
            };
            if (type != AssetType.Land && cca != null)
                UseCca(asset, cca);
            data.Assets.Add(asset);
            return new Change(() => data.Assets.RemoveRecord(asset), () => Restore(data, asset, expense));
        }

        var before = existing.Clone();
        existing.Type = type;
        if (type != AssetType.Land && cca != null)
        {
            // A rate changed by hand on the Assets page is kept while the class stays the same.
            var keepRate = existing.FollowsCca && existing.CcaClass == cca.ClassNumber;
            var rate = existing.RatePercent;
            UseCca(existing, cca);
            if (keepRate)
                existing.RatePercent = rate;
        }
        else if (type != AssetType.Land && usefulLifeYears.HasValue)
        {
            existing.Method = DepreciationMethod.StraightLine;
            existing.UsefulLifeYears = usefulLifeYears;
            existing.CcaClass = null;
            existing.FirstYearFactor = null;
        }
        existing.PurchaseDate = expense.Date;

        // An asset that took the whole expense follows its new amount; one that took only part of it keeps the cost it was given.
        if (costBeforeEdit is { } oldCost && existing.Cost == oldCost
            && string.Equals(existing.Currency, currencyBeforeEdit, StringComparison.OrdinalIgnoreCase))
        {
            existing.Cost = PurchaseTax.Cost(expense, rule);
            existing.Currency = expense.OriginalCurrency;
        }

        existing.UpdatedAt = DateTime.UtcNow;
        var after = existing.Clone();
        return new Change(() => existing.CopyFrom(before), () => existing.CopyFrom(after));
    }
}
