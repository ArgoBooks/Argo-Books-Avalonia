using ArgoBooks.Core.Data;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The one currency a report or an Insights run shows its amounts in (docs/Calculations.md §3a): the
/// company currency when an exact-date rate exists for every date the run converts at, otherwise USD
/// for the whole run, so a document never mixes currencies or shows a wrong-date figure.
/// </summary>
public static class DisplayCurrency
{
    public static string Resolve(string? companyCurrency, IEnumerable<DateTime> dates)
    {
        if (string.IsNullOrEmpty(companyCurrency) || string.Equals(companyCurrency, "USD", StringComparison.OrdinalIgnoreCase))
            return "USD";

        var rates = ExchangeRateService.Instance;
        if (rates == null)
            return "USD";

        foreach (var date in dates.Select(d => d.Date).Distinct())
        {
            if (!rates.TryConvertFromUSD(1m, companyCurrency, date, out _))
                return "USD";
        }

        return companyCurrency;
    }

    /// <summary>
    /// A USD amount in <paramref name="displayCurrency"/> at its own date. <see cref="Resolve"/> only
    /// picks a currency whose rates exist, so the USD fallback here is defensive.
    /// </summary>
    public static decimal FromUSD(decimal amountUSD, string displayCurrency, DateTime date)
    {
        if (string.Equals(displayCurrency, "USD", StringComparison.OrdinalIgnoreCase))
            return amountUSD;

        return ExchangeRateService.Instance != null
               && ExchangeRateService.Instance.TryConvertFromUSD(amountUSD, displayCurrency, date, out var converted)
            ? converted
            : amountUSD;
    }

    /// <summary>
    /// An amount recorded in <paramref name="currency"/> rather than USD (a return's refund amount, a
    /// loss's value) in <paramref name="displayCurrency"/> at the exact <paramref name="date"/>, through
    /// the USD base. An amount already in the display currency is used as-is. Null when the
    /// exact-date rate is unavailable.
    /// </summary>
    public static decimal? FromNative(decimal amount, string currency, string displayCurrency, DateTime date)
    {
        if (string.Equals(currency, displayCurrency, StringComparison.OrdinalIgnoreCase))
            return amount;

        var svc = ExchangeRateService.Instance;
        if (svc == null)
            return amount;

        return svc.TryConvertToUsdBase(amount, currency, date, out var usd)
               && svc.TryConvertFromUSD(usd, displayCurrency, date, out var converted)
            ? converted
            : null;
    }

    /// <summary>
    /// Every date a report converts at: its revenue, expenses, payments, purchase orders, invoices,
    /// returns and losses, plus the end date that point-in-time figures such as inventory are valued at.
    /// </summary>
    public static IEnumerable<DateTime> ReportDates(CompanyData data, DateTime? endDate)
    {
        foreach (var r in data.Revenues) yield return r.Date;
        foreach (var e in data.Expenses) yield return e.Date;
        foreach (var p in data.Payments) yield return p.Date;
        foreach (var po in data.PurchaseOrders) yield return po.OrderDate;
        foreach (var i in data.Invoices) yield return i.IssueDate;
        foreach (var r in data.Returns) yield return r.ReturnDate;
        foreach (var l in data.LostDamaged) yield return l.DateDiscovered;
        yield return endDate ?? DateTime.Today;
    }
}
