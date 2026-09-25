using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Reports;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Single source of truth for per-product sales (revenue and units). Both the
/// Analytics "Products" tab and the "Sales by Product" report call this so their
/// numbers never diverge; only the cash-basis flag differs. Revenue is gross
/// (tax-inclusive) and allocated proportionally per line item. See
/// docs/Calculations.md §13.
/// </summary>
public static class ProductSalesService
{
    /// <summary>
    /// Aggregates per-product sales over a date range. When
    /// <paramref name="cashBasis"/> is true, only collected revenue counts
    /// (analytics convention); when false, all invoiced revenue counts
    /// (formal-report convention). Results are ordered by revenue descending.
    /// </summary>
    public static List<ProductSalesData> GetProductSales(
        CompanyData data, DateTime start, DateTime end, bool cashBasis,
        Func<decimal, DateTime, decimal>? toDisplay = null)
    {
        var acc = new Dictionary<string, (decimal Revenue, decimal Quantity)>();

        foreach (var s in FilterRevenues(data, start, end, cashBasis))
        {
            if (s.LineItems.Count == 0) continue;

            // Allocate the transaction's gross USD total across its line items
            // in proportion to each item's native pre-tax subtotal.
            var lineItemsTotal = s.LineItems.Sum(li => li.Subtotal);
            var totalUSD = s.EffectiveTotalUSD;

            foreach (var li in s.LineItems)
            {
                var pid = li.ProductId ?? "";
                var revenueUSD = lineItemsTotal != 0
                    ? Math.Round(li.Subtotal / lineItemsTotal * totalUSD, 2)
                    : 0;

                // When a display converter is supplied, convert each allocated amount at the
                // transaction's OWN date before accumulating (Calculations.md Rule 3a), so the
                // per-product totals aren't re-priced at one date. Default (null) keeps USD for the
                // formal report path (a documented exception) and unit tests.
                var revenue = toDisplay != null ? toDisplay(revenueUSD, s.Date) : revenueUSD;

                var cur = acc.GetValueOrDefault(pid);
                acc[pid] = (cur.Revenue + revenue, cur.Quantity + li.Quantity);
            }
        }

        var result = new List<ProductSalesData>(acc.Count);
        foreach (var kvp in acc)
        {
            var product = data.GetProduct(kvp.Key);
            var (revenue, qty) = kvp.Value;
            result.Add(new ProductSalesData
            {
                ProductId = kvp.Key,
                ProductName = product?.Name ?? "Unknown",
                Sku = product?.Sku ?? string.Empty,
                UnitsSold = qty,
                RevenueUSD = revenue,
                AvgSalePriceUSD = qty != 0 ? Math.Round(revenue / qty, 2) : 0
            });
        }

        return result.OrderByDescending(p => p.RevenueUSD).ToList();
    }

    /// <summary>
    /// Per-day gross revenue (USD) for a single product, for the detail trend
    /// chart. Same allocation as <see cref="GetProductSales"/>, bucketed by the
    /// transaction date. Honors the same <paramref name="cashBasis"/> flag.
    /// </summary>
    public static Dictionary<DateTime, decimal> GetProductRevenueByDayUSD(
        CompanyData data, string productId, DateTime start, DateTime end, bool cashBasis)
    {
        var byDay = new Dictionary<DateTime, decimal>();

        foreach (var s in FilterRevenues(data, start, end, cashBasis))
        {
            if (s.LineItems.Count == 0) continue;

            var lineItemsTotal = s.LineItems.Sum(li => li.Subtotal);
            var totalUSD = s.EffectiveTotalUSD;

            decimal dayRevenue = 0;
            var matched = false;
            foreach (var li in s.LineItems)
            {
                if ((li.ProductId ?? "") != productId) continue;
                matched = true;
                dayRevenue += lineItemsTotal != 0
                    ? Math.Round(li.Subtotal / lineItemsTotal * totalUSD, 2)
                    : 0;
            }

            if (matched)
            {
                var day = s.Date.Date;
                byDay[day] = byDay.GetValueOrDefault(day, 0m) + dayRevenue;
            }
        }

        return byDay;
    }

    private static IEnumerable<Models.Transactions.Revenue> FilterRevenues(
        CompanyData data, DateTime start, DateTime end, bool cashBasis)
    {
        var revenues = data.Revenues.Where(r => r.Date >= start && r.Date <= end);
        return cashBasis ? revenues.Where(RevenueAggregator.IsCollected) : revenues;
    }
}
