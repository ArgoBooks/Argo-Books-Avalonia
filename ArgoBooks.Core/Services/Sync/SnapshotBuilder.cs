using System.Globalization;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Data;

namespace ArgoBooks.Core.Services.Sync;

/// <summary>
/// Projects <see cref="CompanyData"/> into the small, read-only <see cref="MobileSnapshot"/>
/// the phone renders. This mirrors the existing dashboard aggregation conventions
/// (<see cref="RevenueAggregator"/> / <see cref="ExpenseAggregator"/>: gross, USD-effective
/// totals, collected-only revenue) but keeps the profit math to the simple
/// MoneyIn - MoneyOut the mobile summary card shows, all-time (no date range), since this
/// is a lightweight snapshot, not a full accounting report.
/// </summary>
public static class SnapshotBuilder
{
    /// <summary>Builds a <see cref="MobileSnapshot"/> from the given company data.</summary>
    public static MobileSnapshot Build(CompanyData data)
    {
        var moneyIn = RevenueAggregator.SumCollectedRevenueUSD(data.Revenues, DateTime.MinValue, DateTime.MaxValue);
        var moneyOut = ExpenseAggregator.SumExpensesUSD(data.Expenses, DateTime.MinValue, DateTime.MaxValue);
        var profit = moneyIn - moneyOut;

        var dashboard = new DashboardDto
        {
            MoneyIn = moneyIn,
            MoneyOut = moneyOut,
            Profit = profit,
            ProfitMargin = moneyIn == 0 ? 0 : profit / moneyIn
        };

        return new MobileSnapshot
        {
            Dashboard = dashboard,
            Expenses = BuildExpenseRows(data),
            Revenue = BuildRevenueRows(data),
            Invoices = BuildInvoiceRows(data),
            Customers = BuildCustomerRows(data),
            Suppliers = BuildSupplierRows(data),
            Products = BuildProductRows(data),
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>Serializes a snapshot to UTF-8 JSON bytes (input to the later encrypt/upload task).</summary>
    public static byte[] Serialize(MobileSnapshot snap) => JsonSerializer.SerializeToUtf8Bytes(snap);

    private static List<RowDto> BuildExpenseRows(CompanyData data) => data.Expenses
        .OrderByDescending(e => e.Date)
        .Select(e => new RowDto
        {
            Title = ResolveSupplierName(data, e.SupplierId, e.Description),
            Subtitle = e.Date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
            Amount = "-" + FormatMoney(e.EffectiveTotalUSD)
        })
        .ToList();

    private static List<RowDto> BuildRevenueRows(CompanyData data) => data.Revenues
        .OrderByDescending(r => r.Date)
        .Select(r => new RowDto
        {
            Title = ResolveCustomerName(data, r.CustomerId, r.Description),
            Subtitle = r.Date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
            Amount = "+" + FormatMoney(r.EffectiveTotalUSD)
        })
        .ToList();

    private static List<RowDto> BuildInvoiceRows(CompanyData data) => data.Invoices
        .OrderByDescending(i => i.IssueDate)
        .Select(i => new RowDto
        {
            Title = string.IsNullOrEmpty(i.InvoiceNumber) ? i.Id : i.InvoiceNumber,
            Subtitle = i.Status.ToString(),
            Amount = FormatMoney(i.EffectiveTotalUSD)
        })
        .ToList();

    private static List<RowDto> BuildCustomerRows(CompanyData data) => data.Customers
        .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
        .Select(c => new RowDto
        {
            Title = c.Name,
            Subtitle = string.IsNullOrEmpty(c.CompanyName) ? c.Status.ToString() : c.CompanyName,
            Amount = FormatMoney(SumOutstandingBalanceUSD(data, c.Id))
        })
        .ToList();

    private static List<RowDto> BuildSupplierRows(CompanyData data) => data.Suppliers
        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
        .Select(s => new RowDto
        {
            Title = s.Name,
            Subtitle = s.ContactPerson,
            Amount = FormatMoney(SumSpentWithSupplierUSD(data, s.Id))
        })
        .ToList();

    private static List<RowDto> BuildProductRows(CompanyData data) => data.Products
        .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        .Select(p => new RowDto
        {
            Title = p.Name,
            Subtitle = p.Sku,
            Amount = $"{StockUnits.Format(SumStockOnHand(data, p.Id), p.UnitOfMeasure)} in stock"
        })
        .ToList();

    private static string ResolveSupplierName(CompanyData data, string? supplierId, string fallback)
    {
        var name = string.IsNullOrEmpty(supplierId) ? null : data.GetSupplier(supplierId)?.Name;
        return string.IsNullOrEmpty(name) ? (string.IsNullOrEmpty(fallback) ? "Expense" : fallback) : name;
    }

    private static string ResolveCustomerName(CompanyData data, string? customerId, string fallback)
    {
        var name = string.IsNullOrEmpty(customerId) ? null : data.GetCustomer(customerId)?.Name;
        return string.IsNullOrEmpty(name) ? (string.IsNullOrEmpty(fallback) ? "Revenue" : fallback) : name;
    }

    private static decimal SumOutstandingBalanceUSD(CompanyData data, string customerId) => data.Invoices
        .Where(i => i.CustomerId == customerId)
        .Sum(i => i.EffectiveBalanceUSD);

    private static decimal SumSpentWithSupplierUSD(CompanyData data, string supplierId) => data.Expenses
        .Where(e => e.SupplierId == supplierId)
        .Sum(e => e.EffectiveTotalUSD);

    private static decimal SumStockOnHand(CompanyData data, string productId) => data.Inventory
        .Where(i => i.ProductId == productId)
        .Sum(i => i.InStock);

    private static string FormatMoney(decimal amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);
}
