using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;

using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for fetching and managing currency exchange rates.
/// Routes requests through the argorobots.com server proxy.
/// </summary>
public class ExchangeRateService
{
    /// <summary>
    /// The wire format for a rate date, in both the request and the key used to read the response
    /// back. Always invariant: a plain ToString("yyyy-MM-dd") follows the machine's culture, and on
    /// a non-Gregorian calendar it yields a different year, which would silently stop every date
    /// matching and send the whole preload down the per-date repair path.
    /// </summary>
    private static string DateKey(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>How many repair requests may be in flight at once. See PreloadRatesAsync.</summary>
    private const int MaxParallelRateFetches = 6;

    private static readonly string BaseUrl = $"{ApiConfig.BaseUrl}/api/exchange-rates.php";
    private static readonly string BatchUrl = $"{ApiConfig.BaseUrl}/api/exchange-rates-batch.php";
    private const string BaseCurrency = "USD"; // All rates are relative to USD

    private readonly ExchangeRateCache _cache;
    private readonly HttpClient _httpClient;
    private readonly IErrorLogger? _errorLogger;
    private readonly ITelemetryManager? _telemetryManager;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    /// <summary>
    /// Singleton instance for the exchange rate service.
    /// </summary>
    public static ExchangeRateService? Instance { get; private set; }

    /// <summary>
    /// Creates a new ExchangeRateService instance.
    /// </summary>
    /// <param name="errorLogger">Optional error logger for tracking errors.</param>
    /// <param name="telemetryManager">Optional telemetry manager for tracking API calls.</param>
    public ExchangeRateService(IErrorLogger? errorLogger = null, ITelemetryManager? telemetryManager = null)
        : this(PlatformServiceFactory.GetPlatformService(), new HttpClient(), errorLogger, telemetryManager)
    {
    }

    /// <summary>
    /// Creates a new ExchangeRateService instance with custom dependencies.
    /// </summary>
    public ExchangeRateService(IPlatformService platformService, HttpClient httpClient, IErrorLogger? errorLogger = null, ITelemetryManager? telemetryManager = null)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
        _cache = new ExchangeRateCache(platformService);
        _errorLogger = errorLogger;
        _telemetryManager = telemetryManager;

        Instance ??= this;
    }

    /// <summary>
    /// Initializes the service by loading the cache from disk.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;
            await _cache.LoadAsync();
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Gets the exchange rate between two currencies for a specific date.
    /// </summary>
    /// <param name="fromCurrency">Source currency code (e.g., "USD").</param>
    /// <param name="toCurrency">Target currency code (e.g., "EUR").</param>
    /// <param name="date">The date for the historical rate.</param>
    /// <param name="fetchIfMissing">Whether to fetch from API if not cached.</param>
    /// <returns>The exchange rate, or -1 if unavailable.</returns>
    public async Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency, DateTime date, bool fetchIfMissing = true, CancellationToken cancellationToken = default)
    {
        // Same currency - rate is always 1
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        fromCurrency = fromCurrency.ToUpperInvariant();
        toCurrency = toCurrency.ToUpperInvariant();

        // Try cache first
        if (_cache.TryGetRate(fromCurrency, toCurrency, date, out var cachedRate))
        {
            return cachedRate;
        }

        // Fetch from API if allowed
        if (fetchIfMissing)
        {
            var rates = await FetchRatesForDateAsync(date, cancellationToken: cancellationToken);
            if (rates != null)
            {
                _cache.SetRatesFromBase(rates, BaseCurrency, date);
                await _cache.SaveAsync();

                // Calculate the requested rate
                if (_cache.TryGetRate(fromCurrency, toCurrency, date, out cachedRate))
                {
                    return cachedRate;
                }
            }
        }

        return -1m; // Rate unavailable
    }

    /// <summary>
    /// Gets the exchange rate synchronously, using only cached values.
    /// Will not fetch from API - returns -1 if not cached.
    /// </summary>
    public decimal GetExchangeRate(string fromCurrency, string toCurrency, DateTime date)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        fromCurrency = fromCurrency.ToUpperInvariant();
        toCurrency = toCurrency.ToUpperInvariant();

        if (_cache.TryGetRate(fromCurrency, toCurrency, date, out var rate))
        {
            return rate;
        }

        return -1m; // Rate unavailable
    }

    /// <summary>
    /// Converts an amount between currencies using ONLY the exact-date cached rate. Returns
    /// <see langword="true"/> with the converted amount, rounded to the target currency's decimal
    /// places so a sum of converted rows equals the rows as shown, when the rate is available
    /// (or when <paramref name="from"/> == <paramref name="to"/>); otherwise returns
    /// <see langword="false"/> and <paramref name="result"/> = 0. This is the strict chokepoint for
    /// all money conversion: it never substitutes a different date's rate, so a caller treats a
    /// false result as "pending", not as a number. See docs/Calculations.md (Rule 3a).
    /// </summary>
    public bool TryConvertExact(decimal amount, string from, string to, DateTime date, out decimal result)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            result = amount;
            return true;
        }

        var rate = GetExchangeRate(from, to, date); // cache-only, exact-date
        if (rate <= 0)
        {
            result = 0m;
            return false;
        }

        result = Math.Round(amount * rate, CurrencyInfo.GetByCode(to).DecimalPlaces);
        return true;
    }

    /// <summary>
    /// Converts a native amount to the USD storage base at the exact <paramref name="date"/>,
    /// WITHOUT the 2-decimal rounding that <see cref="TryConvertExact"/> applies for display. The USD
    /// base is the aggregation currency; rounding it to cents makes a same-currency round-trip
    /// (native -&gt; USD base -&gt; native) drift by a cent, so a $10 CAD expense can read $9.99 on a
    /// chart that re-derives from the base. Use this at every point that STORES a <c>*USD</c> field;
    /// display still rounds at the boundary via <see cref="TryConvertExact"/>. Returns
    /// <see langword="false"/> on a rate miss (caller marks the row pending), mirroring
    /// <see cref="TryConvertExact"/>. See docs/Calculations.md Rule 3.
    /// </summary>
    public bool TryConvertToUsdBase(decimal amount, string fromCurrency, DateTime date, out decimal usd)
    {
        if (string.Equals(fromCurrency, BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            usd = amount;
            return true;
        }

        var rate = GetExchangeRate(fromCurrency, BaseCurrency, date); // cache-only, exact-date
        if (rate <= 0)
        {
            usd = 0m;
            return false;
        }

        usd = amount * rate; // full precision: the USD base is never rounded to cents
        return true;
    }

    /// <summary>Exact-date USD-&gt;target conversion. See <see cref="TryConvertExact"/>.</summary>
    public bool TryConvertFromUSD(decimal amountUSD, string toCurrency, DateTime date, out decimal result)
        => TryConvertExact(amountUSD, BaseCurrency, toCurrency, date, out result);

    /// <summary>
    /// Converts a USD amount to the target currency at the exact <paramref name="date"/>. Returns
    /// the converted amount on an exact-date hit, or the USD amount unchanged on a miss. No
    /// wrong-date fallback. Retained only for the report/accounting callers that cannot yet show a
    /// pending state; prefer <see cref="TryConvertFromUSD"/> at any call site that can.
    /// </summary>
    public decimal ConvertFromUSD(decimal amountUSD, string toCurrency, DateTime date)
        => TryConvertFromUSD(amountUSD, toCurrency, date, out var converted) ? converted : amountUSD;

    /// <summary>
    /// Converts an amount from one currency to another.
    /// </summary>
    /// <param name="amount">The amount to convert.</param>
    /// <param name="fromCurrency">Source currency code.</param>
    /// <param name="toCurrency">Target currency code.</param>
    /// <param name="date">The date for the exchange rate.</param>
    /// <returns>The converted amount, or the original amount if conversion fails.</returns>
    public async Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, DateTime date)
    {
        var rate = await GetExchangeRateAsync(fromCurrency, toCurrency, date);
        if (rate <= 0)
        {
            return amount; // Return original if conversion fails
        }

        return Math.Round(amount * rate, 2);
    }

    /// <summary>
    /// Converts an amount to USD.
    /// </summary>
    /// <param name="amount">The amount in the source currency.</param>
    /// <param name="fromCurrency">Source currency code.</param>
    /// <param name="date">The date for the exchange rate.</param>
    /// <returns>The amount in USD.</returns>
    public async Task<decimal> ConvertToUSDAsync(decimal amount, string fromCurrency, DateTime date)
    {
        if (string.Equals(fromCurrency, BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return amount;
        }

        var rate = await GetExchangeRateAsync(fromCurrency, BaseCurrency, date);
        if (rate <= 0)
        {
            return amount; // conversion unavailable; the caller handles the pending state
        }

        // The USD base is stored at full precision (no 2dp round), unlike display conversion. See
        // TryConvertToUsdBase and docs/Calculations.md Rule 3.
        return amount * rate;
    }

    /// <summary>
    /// Preloads exchange rates for a range of dates.
    /// Useful for batch operations to minimize API calls.
    /// </summary>
    /// <param name="dates">The dates to preload rates for.</param>
    /// <param name="progress">Optional progress callback.</param>
    public async Task PreloadRatesAsync(IEnumerable<DateTime> dates, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var uniqueDates = dates.Select(d => d.Date).Distinct().ToList();
        var total = uniqueDates.Count;
        if (total == 0) return;

        // Filter out already-cached dates
        var datesToFetch = new List<DateTime>();
        var cached = 0;
        foreach (var date in uniqueDates)
        {
            if (_cache.TryGetRate(BaseCurrency, "EUR", date, out _))
                cached++;
            else
                datesToFetch.Add(date);
        }
        progress?.Report(cached * 100 / total);

        if (datesToFetch.Count == 0)
        {
            progress?.Report(100);
            return;
        }

        _errorLogger?.LogInfo($"[RatePreload] {total} dates needed, {datesToFetch.Count} to fetch ({cached} already cached).");

        // Try batch endpoint first (one POST for all dates)
        var fetched = await FetchBatchRatesAsync(datesToFetch, cancellationToken);
        if (fetched == null)
            _errorLogger?.LogWarning(
                $"Batch rate fetch returned nothing for {datesToFetch.Count} dates; falling back to slow per-date requests.",
                "ExchangeRate");
        var failedDates = new List<DateTime>();

        foreach (var date in datesToFetch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dateKey = DateKey(date);
            if (fetched != null && fetched.TryGetValue(dateKey, out var rates))
            {
                _cache.SetRatesFromBase(rates, BaseCurrency, date);
                cached++;
                progress?.Report(cached * 100 / total);
            }
            else
            {
                failedDates.Add(date);
            }
        }

        // Fall back to single-date requests for any dates the batch missed. Only advance progress on
        // a successful fetch, so the bar never reaches 100% while dates are still unpriced (which
        // would let the "could not get rates" prompt appear right after a misleading full bar).
        var stillFailed = new List<DateTime>();
        if (failedDates.Count > 0)
        {
            // Run the repairs concurrently. One sequential request per date costs a full round trip
            // each, which is invisible at 45 ms and adds about eight seconds at 500 ms, so a distant
            // user paid most of the wait here. The cache is written serially once every fetch has
            // returned, so this does not assume ExchangeRateCache is thread-safe.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var gate = new SemaphoreSlim(MaxParallelRateFetches);
            var rateLimited = false;

            async Task<(DateTime Date, Dictionary<string, decimal>? Rates)> FetchOneAsync(DateTime date)
            {
                try
                {
                    await gate.WaitAsync(linked.Token);
                }
                catch (OperationCanceledException)
                {
                    return (date, null); // never acquired, so nothing to release
                }

                try
                {
                    return (date, await FetchRatesForDateAsync(date, cancellationToken: linked.Token));
                }
                catch (RateLimitedException)
                {
                    // Stop the rest immediately. Letting the remaining requests fly only digs the
                    // lockout deeper, which is the same reason the per-date path does not retry a 429.
                    rateLimited = true;
                    linked.Cancel();
                    return (date, null);
                }
                catch (OperationCanceledException)
                {
                    return (date, null);
                }
                finally
                {
                    gate.Release();
                }
            }

            var repaired = await Task.WhenAll(failedDates.Select(FetchOneAsync));

            // A caller-requested cancel still wins over anything the fetches reported.
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var (date, rates) in repaired.OrderBy(r => r.Date))
            {
                if (rates != null)
                {
                    _cache.SetRatesFromBase(rates, BaseCurrency, date);
                    cached++;
                    progress?.Report(cached * 100 / total);
                }
                else
                {
                    stillFailed.Add(date);
                }
            }

            if (rateLimited)
                throw new RateLimitedException();
        }

        if (stillFailed.Count > 0)
            _errorLogger?.LogError(
                $"Rate fetch incomplete: {stillFailed.Count}/{datesToFetch.Count} dates unpriced after batch + per-date fallback: " +
                $"{string.Join(", ", stillFailed.OrderBy(d => d).Take(40).Select(DateKey))}",
                ErrorCategory.Api, "ExchangeRate");
        else if (failedDates.Count > 0)
            _errorLogger?.LogWarning(
                fetched == null
                    ? $"Batch returned nothing; the per-date repair priced all {failedDates.Count} dates."
                    : $"Batch returned {fetched.Count} dates but {failedDates.Count} of {datesToFetch.Count} were absent from that response; " +
                      "the per-date repair recovered them. The server answered, so this should be rare and is worth investigating.",
                "ExchangeRate");

        await _cache.SaveAsync();
    }

    /// <summary>A short, log-safe prefix of a response body for diagnostics.</summary>
    private static string Snippet(string s) => string.IsNullOrEmpty(s) ? "(empty)" : s.Length <= 300 ? s : s[..300] + "…";

    /// <summary>
    /// Fetches exchange rates for multiple dates in a single batch request.
    /// Returns a dictionary mapping date strings to their rate dictionaries, or null on failure.
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, decimal>>?> FetchBatchRatesAsync(
        List<DateTime> dates, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;

        try
        {
            var dateStrings = dates.Select(DateKey).ToList();
            var requestBody = new { dates = dateStrings };
            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync(BatchUrl, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _errorLogger?.LogError(
                    $"Batch exchange rate API returned {(int)response.StatusCode} for {dates.Count} dates. Body[{body.Length}]: {Snippet(body)}",
                    ErrorCategory.Api);
                // 429 = the server's rate limiter. Surface it so the caller backs off instead of
                // fanning out to one request per date (which only digs the rate-limit hole deeper).
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    throw new RateLimitedException();
                return null;
            }

            BatchExchangeRatesResponse? result;
            try
            {
                result = JsonSerializer.Deserialize<BatchExchangeRatesResponse>(body);
            }
            catch (Exception parseEx)
            {
                // A parse failure here is the prime suspect for "could not get rates": log the actual
                // status + body length + snippet so a truncated/malformed batch response is visible.
                _errorLogger?.LogError(parseEx, ErrorCategory.Api,
                    $"Batch exchange rate response failed to parse for {dates.Count} dates. Body[{body.Length}]: {Snippet(body)}");
                return null;
            }

            if (result?.Success == true && result.Results != null)
            {
                success = true;
                if (result.Failed is { Count: > 0 } serverFailed)
                    _errorLogger?.LogWarning(
                        $"Batch priced {result.Results.Count}/{dates.Count} dates; server reported {serverFailed.Count} unavailable: {string.Join(", ", serverFailed.Take(20))}",
                        "ExchangeRate");
                return result.Results;
            }

            _errorLogger?.LogError(
                $"Batch exchange rate response unusable for {dates.Count} dates (success={result?.Success}, results={(result?.Results == null ? "null" : result.Results.Count.ToString())}). Body[{body.Length}]: {Snippet(body)}",
                ErrorCategory.Api);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RateLimitedException)
        {
            throw; // propagate so PreloadRatesAsync skips the per-date fanout
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Api, "Failed to fetch batch exchange rates");
            return null;
        }
        finally
        {
            stopwatch.Stop();
            _ = _telemetryManager?.TrackApiCallAsync(
                ApiName.OpenExchangeRatesBatch,
                stopwatch.ElapsedMilliseconds,
                success);
        }
    }

    /// <summary>
    /// Fetches exchange rates for a specific date from the API.
    /// </summary>
    private async Task<Dictionary<string, decimal>?> FetchRatesForDateAsync(DateTime date, int maxRetries = 2, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var success = false;

            try
            {
                if (attempt > 0)
                {
                    await Task.Delay(1000 * attempt, cancellationToken); // 1s, 2s backoff
                }

                var isToday = date.Date == DateTime.Today;
                var endpoint = isToday
                    ? BaseUrl
                    : $"{BaseUrl}?date={DateKey(date)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _errorLogger?.LogError($"Exchange rate API returned {response.StatusCode} (attempt {attempt + 1}/{maxRetries + 1})", ErrorCategory.Api, $"Date: {DateKey(date)}");
                    // Don't retry a rate-limit: hammering it 3x per date is exactly what causes the
                    // lockout. Surface it so the whole preload backs off.
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        throw new RateLimitedException();
                    continue; // retry other transient errors
                }

                var result = await response.Content.ReadFromJsonAsync<ProxyExchangeRatesResponse>();
                if (result?.Success == true && result.Rates != null)
                {
                    success = true;
                    return result.Rates;
                }

                _errorLogger?.LogError($"Exchange rate API returned invalid data (attempt {attempt + 1}/{maxRetries + 1})", ErrorCategory.Api, $"Date: {DateKey(date)}");
            }
            catch (RateLimitedException)
            {
                throw; // propagate so the preload backs off instead of retrying
            }
            catch (OperationCanceledException)
            {
                // A cancel is not an API failure. Retrying it would burn the backoff delays and
                // log a fault for something the caller asked for, so hand it straight back.
                throw;
            }
            catch (Exception ex)
            {
                _errorLogger?.LogError(ex, ErrorCategory.Api, $"Failed to fetch exchange rates for {DateKey(date)} (attempt {attempt + 1}/{maxRetries + 1})");
                if (attempt == maxRetries) return null;
            }
            finally
            {
                stopwatch.Stop();
                _ = _telemetryManager?.TrackApiCallAsync(
                    ApiName.OpenExchangeRates,
                    stopwatch.ElapsedMilliseconds,
                    success);
            }
        }

        return null;
    }

    /// <summary>
    /// Response from the exchange rates proxy endpoint.
    /// </summary>
    private class ProxyExchangeRatesResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("base")]
        public string? Base { get; init; }

        [JsonPropertyName("date")]
        public string? Date { get; init; }

        [JsonPropertyName("rates")]
        public Dictionary<string, decimal>? Rates { get; init; }
    }

    /// <summary>
    /// Response from the batch exchange rates endpoint.
    /// </summary>
    private class BatchExchangeRatesResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("base")]
        public string? Base { get; init; }

        [JsonPropertyName("results")]
        public Dictionary<string, Dictionary<string, decimal>>? Results { get; init; }

        [JsonPropertyName("failed")]
        public List<string>? Failed { get; init; }
    }
}

/// <summary>
/// Thrown when the exchange-rate proxy responds with HTTP 429 (its rate limiter). Callers stop and
/// back off rather than retrying or fanning out to per-date requests, which would only make the
/// rate-limit worse. See <see cref="RateReadinessService"/> for how this maps to a user message.
/// </summary>
internal sealed class RateLimitedException : Exception;
