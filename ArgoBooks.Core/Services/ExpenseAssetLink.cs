using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>Keeps the asset bought with an expense in step with the "this is an asset" choice on the expense form.</summary>
public static class ExpenseAssetLink
{
    /// <summary>How to take back, and put back, what <see cref="Apply"/> changed, for the expense's own undo step.</summary>
    public sealed record Change(Action Undo, Action Redo);

    public static FixedAsset? AssetFor(CompanyData data, string expenseId) =>
        data.Assets.FirstOrDefault(a => a.ExpenseId == expenseId);

    // Renaming the expense only reaches assets in the books, so one put back after a rename takes the expense's id as it is now.
    private static void Restore(CompanyData data, FixedAsset asset, Expense expense)
    {
        asset.ExpenseId = expense.Id;
        data.Assets.RestoreRecord(asset);
    }

    /// <summary>Adds, updates or removes the asset bought with <paramref name="expense"/> to match the form, or returns null when nothing changed.</summary>
    /// <param name="preTaxBeforeEdit">The expense's cost before tax as it was before this save, or null for a new expense.</param>
    /// <param name="currencyBeforeEdit">The expense's currency as it was before this save, or null for a new expense.</param>
    public static Change? Apply(CompanyData data, Expense expense, decimal? preTaxBeforeEdit, string? currencyBeforeEdit,
        bool isAsset, AssetType type, decimal? usefulLifeYears)
    {
        var existing = AssetFor(data, expense.Id);

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
                Cost = AssetDepreciation.PreTaxCost(expense),
                Currency = expense.OriginalCurrency,
                ExpenseId = expense.Id,
                Method = DepreciationMethod.StraightLine,
                UsefulLifeYears = type == AssetType.Land ? null : usefulLifeYears
            };
            data.Assets.Add(asset);
            return new Change(() => data.Assets.RemoveRecord(asset), () => Restore(data, asset, expense));
        }

        var before = existing.Clone();
        existing.Type = type;
        if (type != AssetType.Land)
            existing.UsefulLifeYears = usefulLifeYears;
        existing.PurchaseDate = expense.Date;

        // An asset that took the whole expense follows its new amount; one that took only part of it keeps the cost it was given.
        if (preTaxBeforeEdit is { } oldPreTax && existing.Cost == oldPreTax
            && string.Equals(existing.Currency, currencyBeforeEdit, StringComparison.OrdinalIgnoreCase))
        {
            existing.Cost = AssetDepreciation.PreTaxCost(expense);
            existing.Currency = expense.OriginalCurrency;
        }

        existing.UpdatedAt = DateTime.UtcNow;
        var after = existing.Clone();
        return new Change(() => existing.CopyFrom(before), () => existing.CopyFrom(after));
    }
}
