using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>Works out a fixed asset's depreciation (docs/Calculations.md §16), in the asset's own currency.</summary>
public static class AssetDepreciation
{
    // Declining balance never reaches zero, so the schedule stops after a century or once a month rounds to nothing.
    private const int MaxMonths = 1200;

    /// <summary>One month's depreciation, booked on the month's last day.</summary>
    public readonly record struct DepreciationMonth(DateTime MonthEnd, decimal Amount);

    public readonly record struct DepreciationYear(int Year, decimal Amount, decimal BookValueAfter);

    public static bool Depreciates(FixedAsset asset) => asset.Type != AssetType.Land && asset.Method switch
    {
        DepreciationMethod.StraightLine => asset.UsefulLifeYears is > 0,
        DepreciationMethod.DecliningBalance => asset.RatePercent is > 0 and <= 100,
        _ => false
    };

    /// <summary>The value the books start depreciating from, after any depreciation claimed in earlier books.</summary>
    public static decimal StartingValue(FixedAsset asset) =>
        asset.Cost - (asset.TrackedFrom.HasValue ? asset.PriorDepreciation : 0m);

    /// <summary>Each month's depreciation until it reaches its end value or leaves the books, leaving out the month it left in (the year, under CRA's method).</summary>
    public static IEnumerable<DepreciationMonth> Months(FixedAsset asset)
    {
        if (!Depreciates(asset))
            return [];
        return asset.FollowsCca ? CcaMonths(asset) : MonthlyMonths(asset);
    }

    private static IEnumerable<DepreciationMonth> MonthlyMonths(FixedAsset asset)
    {
        var start = asset.TrackedFrom ?? asset.PurchaseDate;
        var month = new DateTime(start.Year, start.Month, 1);
        DateTime? stopMonth = asset.DisposedDate is { } disposed ? new DateTime(disposed.Year, disposed.Month, 1) : null;

        var bookValue = StartingValue(asset);
        var floor = Math.Max(0m, asset.EndValue);
        var straightLineMonth = asset.Method == DepreciationMethod.StraightLine
            ? (asset.Cost - floor) / (asset.UsefulLifeYears!.Value * 12m)
            : 0m;
        var rate = (asset.RatePercent ?? 0m) / 100m;
        var yearAmount = 0m;

        for (var i = 0; i < MaxMonths && bookValue > floor; i++, month = month.AddMonths(1))
        {
            if (stopMonth.HasValue && month >= stopMonth.Value)
                yield break;

            // Each month is the running total rounded to the cent less what is already booked, so a year adds up to its exact amount.
            decimal amount;
            if (asset.Method == DepreciationMethod.StraightLine)
            {
                amount = Cents(straightLineMonth * (i + 1)) - Cents(straightLineMonth * i);
            }
            else
            {
                var monthOfYear = i % 12;
                if (monthOfYear == 0)
                    yearAmount = Cents(bookValue * rate);
                amount = Cents(yearAmount * (monthOfYear + 1) / 12m) - Cents(yearAmount * monthOfYear / 12m);
            }

            amount = Math.Min(amount, bookValue - floor);
            if (amount <= 0)
                yield break;

            bookValue -= amount;
            yield return new DepreciationMonth(month.AddMonths(1).AddDays(-1), amount);
        }
    }

    /// <summary>
    /// CRA's method: each calendar year takes the rate of what is left, the first year only its first-year share, and the year it leaves the books none.
    /// </summary>
    private static IEnumerable<DepreciationMonth> CcaMonths(FixedAsset asset)
    {
        var start = asset.TrackedFrom ?? asset.PurchaseDate;
        var bookValue = StartingValue(asset);
        var floor = Math.Max(0m, asset.EndValue);
        var rate = asset.RatePercent!.Value / 100m;

        // An asset carried over from earlier books is past its first year by the time these books start.
        var firstYearFactor = asset.TrackedFrom.HasValue ? 1m : asset.FirstYearFactor!.Value;

        for (var year = start.Year; year < start.Year + MaxMonths / 12 && bookValue > floor; year++)
        {
            if (asset.DisposedDate is { } disposed && year >= disposed.Year)
                yield break;

            var yearAmount = Math.Min(Cents(bookValue * rate * (year == start.Year ? firstYearFactor : 1m)), bookValue - floor);
            if (yearAmount <= 0)
                yield break;

            // The year's amount is spread over the months the asset was held, each the running total rounded to the cent less what is already booked.
            var firstMonth = year == start.Year ? start.Month : 1;
            var monthCount = 13 - firstMonth;
            for (var i = 0; i < monthCount; i++)
            {
                var amount = Cents(yearAmount * (i + 1) / monthCount) - Cents(yearAmount * i / monthCount);
                if (amount == 0)
                    continue;
                var month = new DateTime(year, firstMonth + i, 1);
                yield return new DepreciationMonth(month.AddMonths(1).AddDays(-1), amount);
            }

            bookValue -= yearAmount;
        }
    }

    private static decimal Cents(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Depreciation booked on days from <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    public static decimal Between(FixedAsset asset, DateTime from, DateTime to) =>
        Months(asset).Where(m => m.MonthEnd >= from.Date && m.MonthEnd <= to.Date).Sum(m => m.Amount);

    /// <summary>Whether the asset is on the books at the end of <paramref name="date"/>: bought by then and not yet gone.</summary>
    public static bool IsHeldOn(FixedAsset asset, DateTime date) =>
        asset.PurchaseDate.Date <= date.Date && (asset.DisposedDate is not { } gone || gone.Date > date.Date);

    /// <summary>All depreciation up to the end of <paramref name="date"/>, including what earlier books claimed.</summary>
    public static decimal AccumulatedAsOf(FixedAsset asset, DateTime date)
    {
        if (asset.PurchaseDate.Date > date.Date)
            return 0m;
        var prior = asset.TrackedFrom.HasValue ? asset.PriorDepreciation : 0m;
        return prior + Months(asset).Where(m => m.MonthEnd <= date.Date).Sum(m => m.Amount);
    }

    public static decimal BookValueAsOf(FixedAsset asset, DateTime date) => asset.Cost - AccumulatedAsOf(asset, date);

    /// <summary>What it was worth on the books when it left them.</summary>
    public static decimal BookValueAtDisposal(FixedAsset asset) =>
        asset.Cost - (asset.TrackedFrom.HasValue ? asset.PriorDepreciation : 0m) - Months(asset).Sum(m => m.Amount);

    /// <summary>What it brought in less its book value when it left: positive is a gain, negative a loss. Zero while it is still held.</summary>
    public static decimal GainOnDisposal(FixedAsset asset) =>
        asset.IsDisposed ? asset.Proceeds - BookValueAtDisposal(asset) : 0m;

    /// <summary>A full year's depreciation at the start: the same every year for straight line, then less each year for declining balance.</summary>
    public static decimal FirstYear(FixedAsset asset) => Months(asset).Take(12).Sum(m => m.Amount);

    /// <summary>A full year's depreciation once the first year is past, which for CRA's method is what the class rate gives.</summary>
    public static decimal FullYear(FixedAsset asset) =>
        asset.FollowsCca ? Cents(StartingValue(asset) * asset.RatePercent!.Value / 100m) : FirstYear(asset);

    /// <summary>Depreciation in each calendar year, and the book value left at the end of it.</summary>
    public static List<DepreciationYear> Schedule(FixedAsset asset)
    {
        var years = new List<DepreciationYear>();
        var bookValue = StartingValue(asset);
        foreach (var group in Months(asset).GroupBy(m => m.MonthEnd.Year))
        {
            var amount = group.Sum(m => m.Amount);
            bookValue -= amount;
            years.Add(new DepreciationYear(group.Key, amount, bookValue));
        }
        return years;
    }

    /// <summary>An expense's cost before tax, in its own currency, which is what an asset bought with it costs.</summary>
    public static decimal PreTaxCost(Expense expense) => expense.Total - expense.TaxAmount;

    /// <summary>The share of an expense, from 0 to 1, that bought assets, which the reports count as depreciation over the years instead.</summary>
    public static decimal CapitalizedShare(Expense expense, IEnumerable<FixedAsset> assets)
    {
        var preTax = PreTaxCost(expense);
        if (preTax <= 0)
            return 0m;

        var capitalized = assets
            .Where(a => a.ExpenseId == expense.Id
                        && string.Equals(a.Currency, expense.OriginalCurrency, StringComparison.OrdinalIgnoreCase))
            .Sum(a => a.Cost);
        return Math.Clamp(capitalized / preTax, 0m, 1m);
    }

    /// <summary>Each expense that bought an asset, with the share of it that did, for looking up while adding up expenses.</summary>
    public static Dictionary<string, decimal> CapitalizedShares(IEnumerable<Expense> expenses, IReadOnlyCollection<FixedAsset> assets)
    {
        var linked = assets.Where(a => !string.IsNullOrEmpty(a.ExpenseId)).Select(a => a.ExpenseId!).ToHashSet();
        var shares = new Dictionary<string, decimal>();
        if (linked.Count == 0)
            return shares;

        foreach (var expense in expenses.Where(e => linked.Contains(e.Id)))
        {
            var share = CapitalizedShare(expense, assets);
            if (share > 0)
                shares[expense.Id] = share;
        }
        return shares;
    }
}
