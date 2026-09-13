using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services.Integrations;

/// <summary>
/// Fetches the exact-date exchange rates an integration sync is about to need.
///
/// Money is displayed by converting the USD figure at the transaction's own date,
/// and a date whose rate is not cached shows "Pending" instead of an amount. The
/// spreadsheet import gates on this already (see RateReadinessService). A sync
/// did not, so a row dated on any day the cache happened not to hold arrived
/// showing "Pending" and stayed that way, because nothing backfills the cache for
/// rows that are already in the books.
///
/// Best-effort by design, unlike the spreadsheet gate: a sync is something the
/// merchant kicked off to get their data in, and refusing to import because a
/// rate server is unreachable would be a worse trade than a row that reads
/// "Pending" until the next sync fills the gap.
/// </summary>
public static class IntegrationRates
{
    /// <summary>
    /// Cache the rates a set of incoming rows will need, given as (date, currency) pairs.
    ///
    /// A date needs a rate for either of two reasons: the row is not in USD, so storing
    /// its USD base needs one, or the books are not displayed in USD, so showing the row
    /// needs one. Only when neither holds is the date free, which is why the currencies
    /// are passed rather than the dates alone.
    /// </summary>
    /// <param name="progress">
    /// Reports 0-100 across the fetch. Worth wiring up: on a busy Stripe account this
    /// is a 15-second network call in the middle of an import that otherwise shows
    /// nothing, which reads as the app having hung.
    /// </param>
    public static async Task EnsureAsync(
        IEnumerable<(DateTime Date, string Currency)> rows,
        string? displayCurrency,
        IProgress<int>? progress = null,
        IErrorLogger? errorLogger = null,
        CancellationToken ct = default)
    {
        var rates = ExchangeRateService.Instance;
        if (rates == null) return;

        var displayNeedsRates =
            !string.IsNullOrWhiteSpace(displayCurrency) &&
            !string.Equals(displayCurrency, "USD", StringComparison.OrdinalIgnoreCase);

        var today = DateTime.Today;

        // Future dates are unpriceable, not missing. They stay pending until the
        // day arrives, which is the documented behaviour rather than a failure.
        var needed = rows
            .Where(r => displayNeedsRates || !string.Equals(r.Currency, "USD", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Date.Date)
            .Where(d => d <= today)
            .Distinct()
            .ToList();

        if (needed.Count == 0) return;

        try
        {
            await rates.PreloadRatesAsync(needed, progress, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Worth knowing about, not worth failing the sync over: the rows land
            // showing "Pending" and pick up their amount on a later sync.
            errorLogger?.LogWarning(
                $"Could not fetch exchange rates for {needed.Count} dates before an integration import: {ex.Message}",
                "IntegrationRates");
        }
    }

    /// <summary>
    /// Store the USD base amounts for a row an integration has just built, converting
    /// from the row's own currency at its own date.
    ///
    /// The *USD fields are the aggregation base that reports, charts and COGS read.
    /// Setting them equal to the native amounts is only right when the row is already
    /// in USD; for any other currency it files the native figure as though it were
    /// dollars, which reads as a plausible but wrong number rather than as an error.
    ///
    /// On a rate miss the row is deferred rather than guessed at: the USD fields are
    /// zeroed, the row is flagged, and it joins the queue the background
    /// <see cref="PendingConversionService"/> drains once that date's rate exists.
    /// The field list mirrors that service's heal path, so a row converted here and a
    /// row healed later are identical. See docs/Calculations.md Rule 3.
    /// </summary>
    public static void ApplyUsdAmounts(Transaction txn, string currency, CompanyData data)
    {
        txn.OriginalCurrency = currency;

        // Checked before touching the rate service: with no service present (headless,
        // tests) a USD row must still come out converted rather than deferred.
        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            MirrorNativeAmounts(txn);
            return;
        }

        var rates = ExchangeRateService.Instance;

        // Gate on the rate itself rather than on Total, so a row with a zero total but
        // a non-zero tax or fee still defers instead of being marked converted with
        // those secondary fields silently zeroed.
        if (rates == null || rates.GetExchangeRate(currency, "USD", txn.Date) <= 0)
        {
            txn.TotalUSD = 0m;
            txn.TaxAmountUSD = 0m;
            txn.ShippingCostUSD = 0m;
            txn.DiscountUSD = 0m;
            txn.FeeUSD = 0m;
            txn.UnitPriceUSD = 0m;
            txn.IsPendingConversion = true;
            Enqueue(data, txn);
            return;
        }

        txn.TotalUSD = ToUsdBase(rates, txn.Total, currency, txn.Date);
        txn.TaxAmountUSD = ToUsdBase(rates, txn.TaxAmount, currency, txn.Date);
        txn.ShippingCostUSD = ToUsdBase(rates, txn.ShippingCost, currency, txn.Date);
        txn.DiscountUSD = ToUsdBase(rates, txn.Discount, currency, txn.Date);
        txn.FeeUSD = ToUsdBase(rates, txn.Fee, currency, txn.Date);
        txn.UnitPriceUSD = ToUsdBase(rates, txn.UnitPrice, currency, txn.Date);
        txn.IsPendingConversion = false;
    }

    /// <summary>
    /// Full precision, no 2dp round: the USD base is the aggregation currency, and rounding
    /// it to cents makes a native to base to native round-trip drift by a cent. Display
    /// rounds at its own boundary instead. See docs/Calculations.md Rule 3.
    /// </summary>
    private static decimal ToUsdBase(ExchangeRateService rates, decimal amount, string currency, DateTime date)
        => rates.TryConvertToUsdBase(amount, currency, date, out var usd) ? usd : 0m;

    private static void MirrorNativeAmounts(Transaction txn)
    {
        txn.TotalUSD = txn.Total;
        txn.TaxAmountUSD = txn.TaxAmount;
        txn.ShippingCostUSD = txn.ShippingCost;
        txn.DiscountUSD = txn.Discount;
        txn.FeeUSD = txn.Fee;
        txn.UnitPriceUSD = txn.UnitPrice;
        txn.IsPendingConversion = false;
    }

    /// <summary>
    /// Hand an unconverted row to the background conversion queue. Mirrors the
    /// spreadsheet import's own enqueue; only Revenue and Expense are supported there,
    /// which is all either integration creates.
    /// </summary>
    private static void Enqueue(CompanyData data, Transaction txn)
    {
        if (data.PendingConversions.Any(p => p.TransactionId == txn.Id))
            return;

        var entry = new PendingConversion
        {
            TransactionId = txn.Id,
            TransactionType = txn is Revenue ? "Revenue" : "Expense",
            OriginalCurrency = txn.OriginalCurrency,
            TransactionDate = txn.Date,
            Total = txn.Total,
            TaxAmount = txn.TaxAmount,
            ShippingCost = txn.ShippingCost,
            Discount = txn.Discount,
            Fee = txn.Fee,
            UnitPrice = txn.UnitPrice
        };
        data.PendingConversions.Add(entry);

        // The retry timer drains the service's own queue, which takes in the company file's list
        // only when the file opens; without this the row stays pending until the next open.
        _ = PendingConversionService.Instance?.AddPendingConversionAsync(entry);
    }
}
