using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Services;

namespace ArgoBooks.ViewModels;

/// <summary>
/// A product in the Analytics "Sales by Product" picker. Wraps a
/// <see cref="ProductSalesData"/> and exposes display-currency strings for
/// binding, plus the raw USD figures for sorting. Currency conversion happens
/// here, at the presentation boundary (see docs/Calculations.md §13).
/// </summary>
public class ProductSalesRow
{
    // The ProductSalesData here already holds display-currency amounts: GetProductSales was called
    // with a per-date converter so each sale was converted at its OWN date before aggregation
    // (Calculations.md Rule 3a). So format directly, with no further currency conversion.
    public ProductSalesRow(ProductSalesData data)
    {
        ProductId = data.ProductId;
        ProductName = data.ProductName;
        Sku = data.Sku;

        UnitsSold = data.UnitsSold;
        RevenueUSD = data.RevenueUSD;
        AvgSalePriceUSD = data.AvgSalePriceUSD;

        UnitsDisplay = data.UnitsSold.ToString("0.##");
        RevenueDisplay = CurrencyService.Format(data.RevenueUSD);
        AvgSalePriceDisplay = CurrencyService.Format(data.AvgSalePriceUSD);
    }

    public string ProductId { get; }
    public string ProductName { get; }
    public string Sku { get; }

    // Raw USD values for sorting.
    public decimal UnitsSold { get; }
    public decimal RevenueUSD { get; }
    public decimal AvgSalePriceUSD { get; }

    // Display-currency strings for binding.
    public string UnitsDisplay { get; }
    public string RevenueDisplay { get; }
    public string AvgSalePriceDisplay { get; }

    /// <summary>True when the product has a SKU worth displaying.</summary>
    public bool HasSku => !string.IsNullOrEmpty(Sku);

    /// <summary>
    /// Combined "Name · SKU" label shown in the product picker. Also the text the
    /// picker searches against, so typing either the name or the SKU finds the row.
    /// </summary>
    public string DisplayLabel => HasSku ? $"{ProductName} · {Sku}" : ProductName;
}
