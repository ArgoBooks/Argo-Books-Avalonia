using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Provides caching for exchange rates to minimize API calls.
/// Rates are cached both in memory and persisted to disk.
/// </summary>
public class ExchangeRateCache
{
    private const string CacheFileName = "exchange_rates.json";
    private readonly Dictionary<string, decimal> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPlatformService _platformService;
    private readonly IErrorLogger? _errorLogger;
    private readonly Lock _lock = new();
    private bool _isDirty;

    /// <summary>
    /// Creates a new ExchangeRateCache instance.
    /// </summary>
    public ExchangeRateCache(IErrorLogger? errorLogger = null) : this(PlatformServiceFactory.GetPlatformService(), errorLogger)
    {
    }

    /// <summary>
    /// Creates a new ExchangeRateCache instance with a specific platform service.
    /// </summary>
    public ExchangeRateCache(IPlatformService platformService, IErrorLogger? errorLogger = null)
    {
        _platformService = platformService;
        _errorLogger = errorLogger;
    }

    /// <summary>
    /// Generates a cache key for a currency pair and date.
    /// </summary>
    /// <param name="fromCurrency">Source currency code.</param>
    /// <param name="toCurrency">Target currency code.</param>
    /// <param name="date">The date for the exchange rate.</param>
    /// <returns>A unique cache key.</returns>
    public static string GetCacheKey(string fromCurrency, string toCurrency, DateTime date)
    {
        return $"{date:yyyy-MM-dd}_{fromCurrency.ToUpperInvariant()}_{toCurrency.ToUpperInvariant()}";
    }

    /// <summary>
    /// Tries to get a cached exchange rate.
    /// </summary>
    /// <param name="fromCurrency">Source currency code.</param>
    /// <param name="toCurrency">Target currency code.</param>
    /// <param name="date">The date for the exchange rate.</param>
    /// <param name="rate">The cached rate if found.</param>
    /// <returns>True if the rate was found in cache.</returns>
    public bool TryGetRate(string fromCurrency, string toCurrency, DateTime date, out decimal rate)
    {
        var key = GetCacheKey(fromCurrency, toCurrency, date);

        lock (_lock)
        {
            if (_memoryCache.TryGetValue(key, out rate))
            {
                return true;
            }

            // Try inverse rate
            var inverseKey = GetCacheKey(toCurrency, fromCurrency, date);
            if (_memoryCache.TryGetValue(inverseKey, out var inverseRate) && inverseRate != 0)
            {
                rate = 1m / inverseRate;
                return true;
            }
        }

        rate = 0;
        return false;
    }

    /// <summary>
    /// Stores an exchange rate in the cache.
    /// </summary>
    /// <param name="fromCurrency">Source currency code.</param>
    /// <param name="toCurrency">Target currency code.</param>
    /// <param name="date">The date for the exchange rate.</param>
    /// <param name="rate">The exchange rate.</param>
    public void SetRate(string fromCurrency, string toCurrency, DateTime date, decimal rate)
    {
        if (rate <= 0) return;

        var key = GetCacheKey(fromCurrency, toCurrency, date);
        var inverseKey = GetCacheKey(toCurrency, fromCurrency, date);

        lock (_lock)
        {
            _memoryCache[key] = rate;

            // Also store the inverse rate for efficiency
            if (rate != 0)
            {
                _memoryCache[inverseKey] = 1m / rate;
            }

            _isDirty = true;
        }
    }

    /// <summary>
    /// Stores multiple rates for a single date (typically from an API response with all rates relative to USD).
    /// </summary>
    /// <param name="baseToRates">Dictionary of currency codes to their rates relative to base currency.</param>
    /// <param name="baseCurrency">The base currency (typically USD).</param>
    /// <param name="date">The date for these rates.</param>
    public void SetRatesFromBase(Dictionary<string, decimal> baseToRates, string baseCurrency, DateTime date)
    {
        lock (_lock)
        {
            foreach (var kvp in baseToRates)
            {
                if (kvp.Value <= 0) continue;

                var key = GetCacheKey(baseCurrency, kvp.Key, date);
                var inverseKey = GetCacheKey(kvp.Key, baseCurrency, date);

                _memoryCache[key] = kvp.Value;
                _memoryCache[inverseKey] = 1m / kvp.Value;
            }

            // Also store the base currency to itself (rate = 1)
            var selfKey = GetCacheKey(baseCurrency, baseCurrency, date);
            _memoryCache[selfKey] = 1m;

            _isDirty = true;
        }
    }

    /// <summary>
    /// Loads the cache from disk.
    /// </summary>
    public Task LoadAsync()
    {
        if (!_platformService.SupportsFileSystem)
            return Task.CompletedTask;

        // The file gains every date's rates as they are fetched, so parsing it is real work. It
        // runs on the thread pool rather than on the caller's thread, which at launch is the UI's.
        return Task.Run(async () =>
        {
            var cachePath = GetCachePath();
            if (!File.Exists(cachePath))
                return;

            try
            {
                var json = await File.ReadAllTextAsync(cachePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, decimal>>(json);

                if (data != null)
                {
                    lock (_lock)
                    {
                        foreach (var kvp in data)
                        {
                            _memoryCache[kvp.Key] = kvp.Value;
                        }
                        _isDirty = false;
                    }
                }
            }
            catch (Exception ex)
            {
                // Cache file corrupted or unreadable - start fresh
                _errorLogger?.LogWarning($"Failed to load exchange rate cache: {ex.Message}", "ExchangeRateCache");
            }
        });
    }

    /// <summary>
    /// Saves the cache to disk if there are changes.
    /// </summary>
    public async Task SaveAsync()
    {
        if (!_platformService.SupportsFileSystem)
            return;

        Dictionary<string, decimal> snapshot;
        lock (_lock)
        {
            if (!_isDirty)
                return;

            snapshot = new Dictionary<string, decimal>(_memoryCache);
        }

        try
        {
            var cachePath = GetCachePath();
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory))
            {
                _platformService.EnsureDirectoryExists(directory);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(snapshot, options);
            await File.WriteAllTextAsync(cachePath, json);

            // Only clear dirty flag after successful write
            lock (_lock)
            {
                _isDirty = false;
            }
        }
        catch (Exception ex)
        {
            // Failed to save cache - not critical, dirty flag remains set so we retry next time
            _errorLogger?.LogWarning($"Failed to save exchange rate cache: {ex.Message}", "ExchangeRateCache");
        }
    }

    /// <summary>
    /// Clears all cached rates.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _memoryCache.Clear();
            _isDirty = true;
        }
    }

    /// <summary>
    /// Gets the number of cached rates.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _memoryCache.Count;
            }
        }
    }

    private string GetCachePath()
    {
        return _platformService.CombinePaths(_platformService.GetAppDataPath(), CacheFileName);
    }
}
