using System.Globalization;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Services.Sync;

/// <summary>
/// Projects <see cref="CompanyData"/> into the small, read-only <see cref="MobileSnapshot"/>
/// the phone renders. Money In is the desktop's Total Revenue card (collected revenue less
/// refunds, docs/Calculations.md Rule 2 and §8) and Money Out its Expenses card, all-time.
/// Profit stays the simple MoneyIn - MoneyOut the mobile summary card shows, since this is a
/// lightweight snapshot, not a full accounting report. Every amount is in one currency, picked
/// and converted at each row's own date the way a report is (<see cref="DisplayCurrency"/>).
/// </summary>
public static class SnapshotBuilder
{
    /// <summary>Builds a <see cref="MobileSnapshot"/> from the given company data.</summary>
    public static MobileSnapshot Build(CompanyData data)
    {
        var code = DisplayCurrency.Resolve(data.Settings.Localization.Currency, DisplayCurrency.ReportDates(data, null));
        var info = CurrencyInfo.GetByCode(code);
        var currency = new CurrencyDto { Code = info.Code, Symbol = info.Symbol, DecimalPlaces = info.DecimalPlaces };
        decimal Convert(decimal amountUSD, DateTime date) => DisplayCurrency.FromUSD(amountUSD, code, date);

        var moneyIn = RevenueAggregator.SumCollectedRevenueDisplay(data.Revenues, DateTime.MinValue, DateTime.MaxValue, Convert)
                      - RefundAggregator.GetRefundedInDateRangeDisplay(data.Payments, DateTime.MinValue, DateTime.MaxValue, Convert);
        var moneyOut = ExpenseAggregator.SumExpensesDisplay(data.Expenses, DateTime.MinValue, DateTime.MaxValue, Convert);
        var profit = moneyIn - moneyOut;

        var dashboard = new DashboardDto
        {
            MoneyIn = moneyIn,
            MoneyOut = moneyOut,
            Profit = profit,
            ProfitMargin = moneyIn == 0 ? 0 : profit / moneyIn
        };

        RowDto Money(string title, string subtitle, decimal value, string sign = "") => new()
        {
            Title = title,
            Subtitle = subtitle,
            Amount = sign.Length > 0 ? sign + currency.Format(Math.Abs(value)) : currency.Format(value),
            Value = value
        };

        return new MobileSnapshot
        {
            Dashboard = dashboard,
            Currency = currency,
            Expenses = data.Expenses
                .OrderByDescending(e => e.Date)
                .Select(e => Money(ResolveSupplierName(data, e.SupplierId, e.Description), FormatDate(e.Date),
                    -Convert(e.EffectiveTotalUSD, e.Date), "-"))
                .ToList(),
            Revenue = data.Revenues
                .OrderByDescending(r => r.Date)
                .Select(r => Money(ResolveCustomerName(data, r.CustomerId, r.Description), FormatDate(r.Date),
                    Convert(r.EffectiveTotalUSD, r.Date), "+"))
                .ToList(),
            Invoices = data.Invoices
                .OrderByDescending(i => i.IssueDate)
                .Select(i => Money(string.IsNullOrEmpty(i.InvoiceNumber) ? i.Id : i.InvoiceNumber, i.Status.ToString(),
                    Convert(i.EffectiveTotalUSD, i.IssueDate)))
                .ToList(),
            Customers = data.Customers
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => Money(c.Name, string.IsNullOrEmpty(c.CompanyName) ? c.Status.ToString() : c.CompanyName,
                    data.Invoices.Where(i => i.CustomerId == c.Id).Sum(i => Convert(i.EffectiveBalanceUSD, i.IssueDate))))
                .ToList(),
            Suppliers = data.Suppliers
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => Money(s.Name, s.ContactPerson,
                    data.Expenses.Where(e => e.SupplierId == s.Id).Sum(e => Convert(e.EffectiveTotalUSD, e.Date))))
                .ToList(),
            Products = BuildProductRows(data),
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>Serializes a snapshot to UTF-8 JSON bytes (input to the later encrypt/upload task).</summary>
    public static byte[] Serialize(MobileSnapshot snap) => JsonSerializer.SerializeToUtf8Bytes(snap);

    private static string FormatDate(DateTime date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

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

    private static decimal SumStockOnHand(CompanyData data, string productId) => data.Inventory
        .Where(i => i.ProductId == productId)
        .Sum(i => i.InStock);
}
