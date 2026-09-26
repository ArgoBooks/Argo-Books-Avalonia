using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;

namespace ArgoBooks.Services;

/// <summary>
/// Service for formatting currency values based on user settings.
/// Handles conversion between currencies using USD as the base.
/// </summary>
public static class CurrencyService
{
    /// <summary>
    /// Event raised when the currency setting changes.
    /// </summary>
    public static event EventHandler? CurrencyChanged;

    /// <summary>
    /// Raises the CurrencyChanged event to notify subscribers that the currency has changed.
    /// </summary>
    public static void NotifyCurrencyChanged()
    {
        CurrencyChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the current currency code from company settings (e.g., "USD", "EUR").
    /// </summary>
    public static string CurrentCurrencyCode =>
        App.CompanyManager?.CompanyData?.Settings.Localization.Currency ?? "USD";

    public static CurrencyInfo CurrentCurrency => CurrencyInfo.GetByCode(CurrentCurrencyCode);

    /// <summary>
    /// Gets the current currency symbol (e.g., "$", "€").
    /// </summary>
    public static string CurrentSymbol => CurrentCurrency.Symbol;

    /// <summary>
    /// Shown in place of an amount when no exact-date rate is available to convert it to the
    /// display currency (e.g. a future-dated row, or one saved offline whose rate was never
    /// fetched). The exact-date rule forbids showing a wrong-date number. See docs/Calculations.md.
    /// </summary>
    public const string PendingMarker = "Pending";

    /// <summary>
    /// Builds a friendly, per-row explanation for the info tooltip shown next to a
    /// <see cref="PendingMarker"/>: the amount can't be shown in the display currency yet because the
    /// exact-date exchange rate isn't available. Future-dated rows (the common case) get
    /// date-arrives wording; a past date whose rate simply hasn't been fetched gets rate-available
    /// wording. Both promise an automatic conversion to the default currency, so the user never
    /// thinks the value is lost. See docs/Calculations.md (Rule 3a).
    /// </summary>
    public static string BuildPendingConversionHint(decimal originalAmount, string originalCurrency, DateTime date)
    {
        var original = CurrencyInfo.GetByCode(originalCurrency).Format(originalAmount);
        var defaultCode = CurrentCurrencyCode;
        var dateText = date.ToString("MMM d, yyyy");

        if (date.Date > DateTime.Today)
        {
            return $"This amount is dated {dateText}, which is in the future. {original} will "
                 + $"convert to your default currency ({defaultCode}) automatically using that "
                 + "day's exchange rate once the date arrives.";
        }

        return $"{original} will convert to your default currency ({defaultCode}) automatically as "
             + $"soon as the exchange rate for {dateText} is available.";
    }

    /// <summary>
    /// Exact-date USD-&gt;display-currency conversion. Returns <see langword="false"/> when the rate
    /// for <paramref name="date"/> is unavailable, so formatters can show <see cref="PendingMarker"/>
    /// instead of a wrong number. Returns the USD amount unchanged only when no exchange service is
    /// available (headless), which never happens in the running app.
    /// </summary>
    private static bool TryDisplayFromUSD(decimal amountUSD, DateTime date, out decimal amount)
    {
        var svc = ExchangeRateService.Instance;
        if (svc == null)
        {
            amount = amountUSD;
            return true;
        }
        return svc.TryConvertFromUSD(amountUSD, CurrentCurrencyCode, date, out amount);
    }

    /// <summary>
    /// Exact-date USD-&gt;display-currency conversion. Returns <see langword="false"/> when the rate for
    /// <paramref name="date"/> is unavailable, so a caller can show <see cref="PendingMarker"/> (or its
    /// own whole-unit format) instead of a wrong number. Public wrapper over
    /// <see cref="TryDisplayFromUSD"/> for callers that format the converted amount themselves.
    /// </summary>
    public static bool TryGetDisplayFromUSD(decimal amountUSD, DateTime date, out decimal amount)
        => TryDisplayFromUSD(amountUSD, date, out amount);

    /// <summary>Exact-date display amount for a MonetaryValue. See <see cref="TryDisplayFromUSD"/>.</summary>
    private static bool TryDisplay(MonetaryValue value, out decimal amount)
    {
        var svc = ExchangeRateService.Instance;
        return value.TryGetDisplayAmount(
            CurrentCurrencyCode,
            (from, to, rateDate) => svc != null && svc.TryConvertExact(value.AmountUSD, from, to, rateDate, out var v)
                ? (true, v)
                : (false, 0m),
            out amount);
    }

    /// <summary>
    /// Formats an amount using the current currency symbol.
    /// </summary>
    /// <param name="amount">The amount to format.</param>
    /// <param name="includeCode">Whether to include the currency code (e.g., "$100.00 USD").</param>
    /// <returns>The formatted currency string.</returns>
    public static string Format(decimal amount, bool includeCode = false)
    {
        return CurrentCurrency.Format(amount, includeCode);
    }

    /// <summary>
    /// Formats an amount from a MonetaryValue, converting to the current display currency.
    /// </summary>
    /// <param name="value">The monetary value to format.</param>
    /// <returns>The formatted currency string in the current display currency.</returns>
    public static string Format(MonetaryValue? value)
    {
        if (value == null)
            return Format(0m);

        return TryDisplay(value, out var amount) ? Format(amount) : PendingMarker;
    }

    /// <summary>
    /// Gets the display amount for a MonetaryValue in the current display currency, at its exact
    /// date. Returns the stored USD amount when no exact-date rate is available (a numeric
    /// best-effort for aggregation callers); display callers use <see cref="Format(MonetaryValue?)"/>
    /// which shows <see cref="PendingMarker"/> instead.
    /// </summary>
    /// <param name="value">The monetary value.</param>
    /// <returns>The amount converted to the current display currency.</returns>
    public static decimal GetDisplayAmount(MonetaryValue value)
    {
        return TryDisplay(value, out var amount) ? amount : value.AmountUSD;
    }

    /// <summary>
    /// Gets the display amount for a legacy decimal value (assumes USD), at the exact
    /// <paramref name="date"/>. Returns the USD amount when no exact-date rate is available (numeric
    /// best-effort); display callers use <see cref="FormatFromUSD"/> which shows the pending marker.
    /// </summary>
    /// <param name="amountUSD">The amount in USD.</param>
    /// <param name="date">The date for exchange rate lookup.</param>
    /// <returns>The amount in the current display currency.</returns>
    public static decimal GetDisplayAmount(decimal amountUSD, DateTime date)
    {
        return TryDisplayFromUSD(amountUSD, date, out var amount) ? amount : amountUSD;
    }

    /// <summary>
    /// Converts an amount recorded in <paramref name="currency"/> rather than USD (a return's refund
    /// amount, a loss's value) to the display currency at the exact <paramref name="date"/>, through
    /// the USD base. An amount already in the display currency is used as-is and never waits on a
    /// rate. Null when the exact-date rate is unavailable, which callers treat as pending.
    /// </summary>
    public static decimal? GetDisplayAmountFromNative(decimal amount, string currency, DateTime date) =>
        DisplayCurrency.FromNative(amount, currency, CurrentCurrencyCode, date);

    /// <summary>
    /// Formats a legacy decimal value (assumes USD) in the current display currency, at the exact
    /// <paramref name="date"/>. Shows <see cref="PendingMarker"/> when no exact-date rate is available.
    /// </summary>
    /// <param name="amountUSD">The amount in USD.</param>
    /// <param name="date">The date for exchange rate lookup.</param>
    /// <returns>The formatted currency string.</returns>
    public static string FormatFromUSD(decimal amountUSD, DateTime date)
    {
        return TryDisplayFromUSD(amountUSD, date, out var amount) ? Format(amount) : PendingMarker;
    }

    /// <summary>
    /// Formats a stock value, which is kept in USD, in the display currency at today's rate, because
    /// stock on hand is valued as it stands now. See docs/Calculations.md §14.
    /// </summary>
    public static string FormatStockValue(decimal valueUSD) => FormatFromUSD(valueUSD, DateTime.Today);

    /// <summary>
    /// Currency-aware per-item sum that reports (via the return value) whether EVERY item could be
    /// shown in the display currency. Mirrors <see cref="FormatWithOriginal"/> per item: a row whose
    /// original currency already matches the display currency uses its original amount directly (no
    /// conversion, never "pending"); other rows convert from USD at their own date and mark the sum
    /// incomplete when that exact-date rate isn't cached. <paramref name="total"/> is the best-effort
    /// sum either way. This keeps company-currency rows (e.g. bank imports) out of the pending state.
    /// </summary>
    public static bool TrySumDisplayFromUSD<T>(
        IEnumerable<T> items, Func<T, decimal> originalAmount, Func<T, string> originalCurrency,
        Func<T, decimal> amountUSD, Func<T, DateTime> date, out decimal total)
    {
        total = 0m;
        var complete = true;
        var target = CurrentCurrencyCode;
        foreach (var item in items)
        {
            // Already in the display currency: use the original amount as-is, no conversion needed. A
            // row still waiting for its USD value counts 0 until it converts, as it does in every USD
            // total (Calculations.md §3), so a card agrees with its % change and with Net Profit.
            if (string.Equals(target, originalCurrency(item), StringComparison.OrdinalIgnoreCase))
            {
                if (!IsPendingConversion(item))
                    total += originalAmount(item);
                continue;
            }
            var usd = amountUSD(item);
            if (TryDisplayFromUSD(usd, date(item), out var amount))
                total += amount;
            else
            {
                total += usd;
                complete = false;
            }
        }
        return complete;
    }

    private static bool IsPendingConversion(object? item) => item switch
    {
        Transaction t => t.IsPendingConversion,
        Invoice i => i.IsPendingConversion,
        Payment p => p.IsPendingConversion,
        PurchaseOrder o => o.IsPendingConversion,
        _ => false
    };

    /// <summary>
    /// Sums per-item amounts in the display currency, or returns <see cref="PendingMarker"/> when any
    /// item that needs conversion is still awaiting its exact-date rate, so a total never silently
    /// shows a partial figure as if it were complete. Rows already in the display currency never
    /// trigger pending (see <see cref="TrySumDisplayFromUSD{T}"/>).
    /// </summary>
    public static string FormatSumDisplayFromUSD<T>(
        IEnumerable<T> items, Func<T, decimal> originalAmount, Func<T, string> originalCurrency,
        Func<T, decimal> amountUSD, Func<T, DateTime> date)
        => TrySumDisplayFromUSD(items, originalAmount, originalCurrency, amountUSD, date, out var total)
            ? Format(total) : PendingMarker;

    /// <summary>
    /// Formats a total that converts each transaction at its own date, or returns <see cref="PendingMarker"/>
    /// when any of them is still waiting for its exact-date rate. Pass the aggregate as a function of the
    /// converter, e.g. <c>convert =&gt; ProfitCalculator.CalculateNetProfitDisplay(data, start, end, convert)</c>.
    /// </summary>
    public static string FormatTotalOrPending(Func<Func<decimal, DateTime, decimal>, decimal> total) =>
        TryComputeDisplay(total, out var amount) ? Format(amount) : PendingMarker;

    /// <summary>
    /// Runs <paramref name="compute"/> with a converter that converts each amount at its own date, and
    /// returns false when any of them is still waiting for its exact-date rate, so the caller shows
    /// <see cref="PendingMarker"/> instead of a figure with USD mixed in.
    /// </summary>
    public static bool TryComputeDisplay<T>(Func<Func<decimal, DateTime, decimal>, T> compute, out T result)
    {
        var complete = true;
        result = compute((amountUSD, date) =>
        {
            if (TryDisplayFromUSD(amountUSD, date, out var converted))
                return converted;
            complete = false;
            return amountUSD;
        });
        return complete;
    }

    /// <summary>
    /// Ensures today's exact-date USD-&gt;display-currency rate is cached, fetching it if missing and
    /// online. "As of now" aggregate displays (e.g. the profit chart title) convert at today's rate,
    /// which is never fetched on its own when no transaction is dated today and the currency wasn't
    /// changed this session, so they show <see cref="PendingMarker"/> until this fills it in. Returns
    /// <see langword="true"/> only when a fetch actually filled a previously-missing rate, so the
    /// caller knows to recompute. No-op (returns <see langword="false"/>) for a USD display currency
    /// or when the rate is already cached.
    /// </summary>
    public static async Task<bool> TryWarmTodayRateAsync(CancellationToken cancellationToken = default)
    {
        var code = CurrentCurrencyCode;
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase))
            return false;

        var svc = ExchangeRateService.Instance;
        if (svc == null)
            return false;

        var today = DateTime.Today;
        if (svc.GetExchangeRate("USD", code, today) > 0)
            return false; // already cached, nothing pending to fix

        var rate = await svc.GetExchangeRateAsync("USD", code, today, fetchIfMissing: true, cancellationToken: cancellationToken);
        return rate > 0;
    }

    /// <summary>
    /// Fetches the rates the company's transaction dates are missing, then refreshes every money
    /// display. The rate cache is per machine, so a company opened on another computer, or the sample
    /// company after its dates move, can start without them and show Pending.
    /// </summary>
    public static async Task WarmCompanyRatesAsync(CompanyData data)
    {
        var code = CurrentCurrencyCode;
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase) || ExchangeRateService.Instance is not { } rates)
            return;

        var dates = DisplayCurrency.ReportDates(data, null).ToList();
        if (dates.All(d => d.Date > DateTime.Today || rates.GetExchangeRate("USD", code, d) > 0))
            return;

        await new RateReadinessService(rates, new ConnectivityService(), App.ErrorLogger).EnsureRatesAsync(dates);
        NotifyCurrencyChanged();
    }

    /// <summary>
    /// Ensures the exact-date display-currency-&gt;USD rate for <paramref name="date"/> is cached,
    /// fetching it if missing. Lets a synchronous save path (receipts, purchase orders) convert from
    /// cache without a momentary "Pending", matching the manual-entry flow that fetches the rate up
    /// front. No-op for a USD display currency or when the rate is already cached. Best-effort: a
    /// failed fetch just leaves the row to fall back to pending + the self-heal.
    /// </summary>
    public static Task WarmRateForDateAsync(DateTime date, CancellationToken cancellationToken = default)
        => WarmRateForDateAsync(date, CurrentCurrencyCode, cancellationToken);

    /// <summary>
    /// <see cref="WarmRateForDateAsync(DateTime, CancellationToken)"/> for a transaction in
    /// <paramref name="code"/> rather than the display currency, such as a receipt scanned abroad.
    /// </summary>
    public static async Task WarmRateForDateAsync(DateTime date, string code, CancellationToken cancellationToken = default)
    {
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase))
            return;

        var svc = ExchangeRateService.Instance;
        if (svc == null)
            return;

        if (svc.GetExchangeRate(code, "USD", date) > 0)
            return; // already cached

        try
        {
            await svc.GetExchangeRateAsync(code, "USD", date, fetchIfMissing: true, cancellationToken: cancellationToken);
        }
        catch
        {
            // Best-effort: ApplyDisplayCurrency falls back to pending + the self-heal.
        }
    }

    /// <summary>
    /// Formats an amount using the original value when the display currency matches
    /// the original currency, avoiding rounding errors from USD round-trip conversion.
    /// </summary>
    public static string FormatWithOriginal(decimal originalAmount, string originalCurrency, decimal amountUSD, DateTime date)
    {
        var targetCurrency = CurrentCurrencyCode;

        // If display currency matches the original currency, use exact original amount
        if (string.Equals(targetCurrency, originalCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Format(originalAmount);
        }

        // Otherwise convert from USD to the target currency
        return FormatFromUSD(amountUSD, date);
    }

    /// <summary>
    /// Creates a MonetaryValue from a user-entered amount in the current currency.
    /// </summary>
    /// <param name="amount">The amount entered by the user.</param>
    /// <param name="date">The transaction date for exchange rate lookup.</param>
    /// <returns>A MonetaryValue with both original and USD amounts.</returns>
    public static Task<MonetaryValue> CreateMonetaryValueAsync(decimal amount, DateTime date)
        => CreateMonetaryValueAsync(amount, CurrentCurrencyCode, date);

    /// <summary>
    /// <see cref="CreateMonetaryValueAsync(decimal, DateTime)"/> for an amount in
    /// <paramref name="currency"/> rather than the company currency, such as an entry being edited
    /// that was recorded in another currency.
    /// </summary>
    public static async Task<MonetaryValue> CreateMonetaryValueAsync(decimal amount, string currency, DateTime date)
    {
        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return new MonetaryValue(amount, "USD", amount, date);
        }

        // Convert to USD
        var exchangeService = ExchangeRateService.Instance;
        decimal amountUSD = amount;

        if (exchangeService != null)
        {
            amountUSD = await exchangeService.ConvertToUSDAsync(amount, currency, date);
        }

        return new MonetaryValue(amount, currency, amountUSD, date);
    }

    /// <summary>
    /// Gets the currency code from a display string like "USD - US Dollar ($)".
    /// </summary>
    public static string ParseCurrencyCode(string displayString)
    {
        return CurrencyInfo.ParseCodeFromDisplayString(displayString);
    }

    public static string GetDisplayString(string currencyCode)
    {
        return CurrencyInfo.GetByCode(currencyCode).DisplayString;
    }

    public static string GetSymbol(string currencyCode)
    {
        return CurrencyInfo.GetSymbol(currencyCode);
    }
}
