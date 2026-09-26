using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the ExchangeRateCache class.
/// </summary>
public class ExchangeRateCacheTests
{
    private readonly ExchangeRateCache _cache;

    public ExchangeRateCacheTests()
    {
        var platformService = new MockPlatformService();
        _cache = new ExchangeRateCache(platformService);
    }

    #region SetRate / TryGetRate Roundtrip Tests

    [Fact]
    public void SetRate_ThenTryGetRate_RoundTrip_ReturnsStoredRate()
    {
        var date = new DateTime(2025, 1, 15);
        _cache.SetRate("USD", "EUR", date, 0.92m);

        var found = _cache.TryGetRate("USD", "EUR", date, out var rate);

        Assert.True(found);
        Assert.Equal(0.92m, rate);
    }

    [Fact]
    public void SetRate_TryGetRate_InverseLookup_ReturnsInverseRate()
    {
        var date = new DateTime(2025, 1, 15);
        _cache.SetRate("USD", "EUR", date, 0.92m);

        var found = _cache.TryGetRate("EUR", "USD", date, out var rate);

        Assert.True(found);
        Assert.Equal(1m / 0.92m, rate);
    }

    [Fact]
    public void SetRate_MultipleRates_AllRetrievable()
    {
        var date = new DateTime(2025, 3, 1);
        _cache.SetRate("USD", "EUR", date, 0.92m);
        _cache.SetRate("USD", "GBP", date, 0.79m);
        _cache.SetRate("USD", "JPY", date, 150.5m);

        Assert.True(_cache.TryGetRate("USD", "EUR", date, out var eurRate));
        Assert.Equal(0.92m, eurRate);

        Assert.True(_cache.TryGetRate("USD", "GBP", date, out var gbpRate));
        Assert.Equal(0.79m, gbpRate);

        Assert.True(_cache.TryGetRate("USD", "JPY", date, out var jpyRate));
        Assert.Equal(150.5m, jpyRate);
    }

    [Fact]
    public void SetRate_ZeroOrNegativeRate_DoesNotStore()
    {
        var date = new DateTime(2025, 1, 15);

        _cache.SetRate("USD", "EUR", date, 0m);
        Assert.False(_cache.TryGetRate("USD", "EUR", date, out _));

        _cache.SetRate("USD", "GBP", date, -1.5m);
        Assert.False(_cache.TryGetRate("USD", "GBP", date, out _));
    }

    #endregion

    #region Same Currency Tests

    [Fact]
    public void SetRatesFromBase_SameCurrency_ReturnsOne()
    {
        var date = new DateTime(2025, 1, 15);
        var rates = new Dictionary<string, decimal>
        {
            { "EUR", 0.92m },
            { "GBP", 0.79m }
        };

        _cache.SetRatesFromBase(rates, "USD", date);

        var found = _cache.TryGetRate("USD", "USD", date, out var rate);

        Assert.True(found);
        Assert.Equal(1m, rate);
    }

    #endregion

    #region Missing Rate Tests

    [Fact]
    public void TryGetRate_MissingRate_ReturnsFalseAndZero()
    {
        var date = new DateTime(2025, 1, 15);

        var found = _cache.TryGetRate("USD", "EUR", date, out var rate);

        Assert.False(found);
        Assert.Equal(0m, rate);
    }

    [Fact]
    public void TryGetRate_EmptyCache_ReturnsFalse()
    {
        var found = _cache.TryGetRate("ABC", "XYZ", DateTime.Today, out _);

        Assert.False(found);
    }

    #endregion

    #region Date-Based Lookup Tests

    [Fact]
    public void TryGetRate_DifferentDates_ReturnsCorrectRatePerDate()
    {
        var date1 = new DateTime(2025, 1, 1);
        var date2 = new DateTime(2025, 6, 1);

        _cache.SetRate("USD", "EUR", date1, 0.90m);
        _cache.SetRate("USD", "EUR", date2, 0.95m);

        _cache.TryGetRate("USD", "EUR", date1, out var rate1);
        _cache.TryGetRate("USD", "EUR", date2, out var rate2);

        Assert.Equal(0.90m, rate1);
        Assert.Equal(0.95m, rate2);
    }

    [Fact]
    public void TryGetRate_WrongDate_ReturnsFalse()
    {
        var date = new DateTime(2025, 1, 15);
        var wrongDate = new DateTime(2025, 2, 15);

        _cache.SetRate("USD", "EUR", date, 0.92m);

        var found = _cache.TryGetRate("USD", "EUR", wrongDate, out _);

        Assert.False(found);
    }

    #endregion

    #region SetRatesFromBase Tests

    [Fact]
    public void SetRatesFromBase_StoresAllRatesAndInverses()
    {
        var date = new DateTime(2025, 5, 10);
        var rates = new Dictionary<string, decimal>
        {
            { "EUR", 0.92m },
            { "GBP", 0.79m },
            { "CAD", 1.36m }
        };

        _cache.SetRatesFromBase(rates, "USD", date);

        Assert.True(_cache.TryGetRate("USD", "EUR", date, out var eurRate));
        Assert.Equal(0.92m, eurRate);

        Assert.True(_cache.TryGetRate("EUR", "USD", date, out var eurInverse));
        Assert.Equal(1m / 0.92m, eurInverse);

        Assert.True(_cache.TryGetRate("USD", "GBP", date, out _));
        Assert.True(_cache.TryGetRate("USD", "CAD", date, out _));
    }

    #endregion

    #region GetCacheKey Tests

    [Fact]
    public void GetCacheKey_FormatsCorrectly()
    {
        var date = new DateTime(2025, 3, 15);

        var key = ExchangeRateCache.GetCacheKey("usd", "eur", date);

        Assert.Equal("2025-03-15_USD_EUR", key);
    }

    [Theory]
    [InlineData("usd", "eur")]
    [InlineData("USD", "EUR")]
    [InlineData("Usd", "Eur")]
    public void GetCacheKey_CaseInsensitive_ProducesSameKey(string from, string to)
    {
        var date = new DateTime(2025, 1, 1);

        var key = ExchangeRateCache.GetCacheKey(from, to, date);

        Assert.Equal("2025-01-01_USD_EUR", key);
    }

    #endregion

    #region Clear and Count Tests

    [Fact]
    public void Clear_EmptiesCache()
    {
        var date = new DateTime(2025, 1, 15);
        _cache.SetRate("USD", "EUR", date, 0.92m);
        Assert.True(_cache.Count > 0);

        _cache.Clear();

        Assert.Equal(0, _cache.Count);
        Assert.False(_cache.TryGetRate("USD", "EUR", date, out _));
    }

    [Fact]
    public void Count_ReflectsNumberOfCachedEntries()
    {
        Assert.Equal(0, _cache.Count);

        var date = new DateTime(2025, 1, 15);
        _cache.SetRate("USD", "EUR", date, 0.92m);

        // SetRate also stores the inverse, so count should be 2
        Assert.Equal(2, _cache.Count);
    }

    #endregion

    #region Mock Classes

    private class MockPlatformService : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => Path.Combine(Path.GetTempPath(), "ExchangeRateCacheTest");
        public string GetTempPath() => Path.GetTempPath();
        public string GetCachePath() => Path.GetTempPath();

        public void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        public bool SupportsFileSystem => true;
        public bool SupportsNativeDialogs => false;
        public bool SupportsBiometrics => false;
        public Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("Not supported");
        public Task<bool> AuthenticateWithBiometricAsync(string reason) => Task.FromResult(false);
        public void StorePasswordForBiometric(string fileId, string password) { }
        public string? GetPasswordForBiometric(string fileId) => null;
        public void ClearPasswordForBiometric(string fileId) { }
        public bool SupportsAutoUpdate => false;
        public int MaxRecentCompanies => 10;
        public string NormalizePath(string path) => path;
        public string CombinePaths(params string[] paths) => Path.Combine(paths);
        public string GetMachineId() => "test-machine-id";
        public StringComparer PathComparer => StringComparer.Ordinal;
    }

    #endregion
}
