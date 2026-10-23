using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Single source of truth for net profit and net profit by day.
///
/// Profit = pre-tax revenue (collected) − expenses other than tracked stock bought − cost of
/// goods sold − pre-tax refunds + what fixed assets sold for.
/// Sales tax we collect is owed to the government, so it isn't profit;
/// tax we paid suppliers is a cost unless the company claims it back (PurchaseTax). See
/// docs/Calculations.md §2 and §8 for the full rules.
/// </summary>
public static class ProfitCalculator
{
    /// <summary>
    /// Net profit USD inside a date range, paid-only, refunds adjusted for
    /// tax. Returns a plain decimal; callers format and apply currency
    /// conversion at the display layer.
    /// </summary>
    public static decimal CalculateNetProfitUSD(
        CompanyData data, DateTime start, DateTime end)
    {
        var revenuePreTax = RevenueAggregator.SumCollectedRevenuePreTaxUSD(
            data.Revenues, start, end);
        var expenses = CostOfGoodsAggregator.SumOperatingExpensesUSD(data.Expenses, start, end, PurchaseTax.RuleFor(data));
        var costOfGoods = CostOfGoodsAggregator.SumCostOfGoodsSoldUSD(data.Revenues, start, end, collectedOnly: true);
        var refundsPreTax = RefundAggregator.GetRefundedPreTaxInDateRangeUSD(
            data.Payments, BuildInvoiceLookup(data.Invoices), start, end);
        var assetSales = AssetSalesByDayUSD(data.Assets, start, end).Values.Sum();
        return revenuePreTax - expenses - costOfGoods - refundsPreTax + assetSales;
    }

    /// <summary>
    /// Display-currency variant of <see cref="CalculateNetProfitUSD"/>: each component (revenue,
    /// expenses, refunds) is converted to the display currency at each transaction's OWN date via
    /// <paramref name="toDisplay"/> before the profit subtraction, per docs/Calculations.md Rule 4.
    /// Pass <c>CurrencyService.GetDisplayAmount</c>. Equals the USD profit for USD display.
    /// </summary>
    public static decimal CalculateNetProfitDisplay(
        CompanyData data, DateTime start, DateTime end, Func<decimal, DateTime, decimal> toDisplay)
    {
        var revenuePreTax = RevenueAggregator.SumCollectedRevenuePreTaxDisplay(
            data.Revenues, start, end, toDisplay);
        var expenses = CostOfGoodsAggregator.SumOperatingExpensesDisplay(data.Expenses, start, end, PurchaseTax.RuleFor(data), toDisplay);
        var costOfGoods = CostOfGoodsAggregator.SumCostOfGoodsSoldDisplay(
            data.Revenues, start, end, collectedOnly: true, toDisplay);
        var refundsPreTax = RefundAggregator.GetRefundedPreTaxInDateRangeDisplay(
            data.Payments, BuildInvoiceLookup(data.Invoices), start, end, toDisplay);
        var assetSales = AssetSalesByDayUSD(data.Assets, start, end).Sum(kv => toDisplay(kv.Value, kv.Key));
        return revenuePreTax - expenses - costOfGoods - refundsPreTax + assetSales;
    }

    /// <summary>
    /// Profit per day inside the range, suitable for time-series charts.
    /// Each day's value is collected pre-tax revenue minus pre-tax refunds
    /// (both keyed by the day they happened) minus expenses on that day.
    /// </summary>
    public static Dictionary<DateTime, decimal> CalculateNetProfitByDayUSD(
        CompanyData data, DateTime start, DateTime end)
    {
        var revenueByDay = RevenueAggregator.GroupCollectedRevenuePreTaxByDayUSD(data.Revenues, start, end);
        var rule = PurchaseTax.RuleFor(data);

        var expensesByDay = data.Expenses
            .Where(p => p.Date >= start && p.Date <= end)
            .GroupBy(p => p.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(e => CostOfGoodsAggregator.OperatingExpenseUSD(e, rule)));

        var refundsByDay = RefundAggregator.GroupPreTaxRefundsByDayUSD(data.Payments, BuildInvoiceLookup(data.Invoices), start, end);

        var costOfGoodsByDay = data.Revenues
            .Where(s => s.Date >= start && s.Date <= end)
            .Where(RevenueAggregator.IsCollected)
            .GroupBy(s => s.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(CostOfGoodsAggregator.CostOfGoodsSoldUSD));

        var assetSalesByDay = AssetSalesByDayUSD(data.Assets, start, end);

        var allDays = revenueByDay.Keys
            .Union(expensesByDay.Keys)
            .Union(refundsByDay.Keys)
            .Union(costOfGoodsByDay.Keys)
            .Union(assetSalesByDay.Keys);

        return allDays.ToDictionary(
            day => day,
            day =>
                revenueByDay.GetValueOrDefault(day, 0m)
                - refundsByDay.GetValueOrDefault(day, 0m)
                - expensesByDay.GetValueOrDefault(day, 0m)
                - costOfGoodsByDay.GetValueOrDefault(day, 0m)
                + assetSalesByDay.GetValueOrDefault(day, 0m));
    }

    /// <summary>
    /// What fixed assets sold for, in USD by the day each was sold. Profit counts it because the purchase was counted as an expense in full; revenue does not, since it is not a sale to a customer.
    /// </summary>
    public static Dictionary<DateTime, decimal> AssetSalesByDayUSD(IEnumerable<FixedAsset> assets, DateTime start, DateTime end) =>
        SoldInRange(assets, start, end)
            .GroupBy(a => a.DisposedDate!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Sum(a => SaleProceedsUSD(a) ?? 0m));

    /// <summary>Whether a fixed asset sold in the range is still waiting for the exchange rate on the day it was sold.</summary>
    public static bool AnyAssetSaleWaiting(IEnumerable<FixedAsset> assets, DateTime start, DateTime end) =>
        SoldInRange(assets, start, end).Any(a => SaleProceedsUSD(a) == null);

    private static IEnumerable<FixedAsset> SoldInRange(IEnumerable<FixedAsset> assets, DateTime start, DateTime end) =>
        assets.Where(a => a.Proceeds != 0 && a.DisposedDate is { } sold && sold >= start && sold <= end);

    private static decimal? SaleProceedsUSD(FixedAsset asset)
    {
        var rates = ExchangeRateService.Instance;
        if (rates == null || UsdConversion.IsUsd(asset.Currency))
            return asset.Proceeds;
        return rates.TryConvertToUsdBase(asset.Proceeds, asset.Currency, asset.DisposedDate!.Value.Date, out var usd) ? usd : null;
    }

    public static Dictionary<string, Invoice> BuildInvoiceLookup(IEnumerable<Invoice> invoices)
    {
        var dict = new Dictionary<string, Invoice>();
        foreach (var inv in invoices)
        {
            if (!string.IsNullOrEmpty(inv.Id))
                dict[inv.Id] = inv;
        }
        return dict;
    }
}
