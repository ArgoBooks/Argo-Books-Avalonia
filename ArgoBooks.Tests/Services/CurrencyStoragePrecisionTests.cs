using System.Net;
using System.Text;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The USD base is the aggregation currency and is stored at FULL precision: rounding it to 2
/// decimals made a same-currency round-trip (native -> USD base -> native) drift by a cent, so a
/// $10 CAD expense could read $9.99 on a chart that re-derives from the USD base. Display still
/// rounds to 2 decimals at the boundary (<see cref="ExchangeRateService.TryConvertExact"/>); only
/// the stored base is unrounded. See docs/Calculations.md Rule 3.
/// </summary>
public class CurrencyStoragePrecisionTests
{
    // USD->CAD = 1.63 is a rate where the 2dp-rounded base drifts: round(10/1.63, 2) = 6.13,
    // and round(6.13 * 1.63, 2) = 9.99, not 10.00.
    private const decimal UsdToCad = 1.63m;
    private static readonly DateTime RateDate = new(2026, 1, 5);

    private static async Task<ExchangeRateService> SeededServiceAsync()
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new StubCadHandler(UsdToCad)));
        await service.GetExchangeRateAsync("USD", "CAD", RateDate); // seeds USD->CAD and its inverse
        return service;
    }

    [Fact]
    public async Task ConvertToUSD_StoresFullPrecision_NotRoundedToCents()
    {
        var service = await SeededServiceAsync();

        var usdBase = await service.ConvertToUSDAsync(10m, "CAD", RateDate);

        // A $10 CAD amount is 6.1349... USD; the stored base must keep the sub-cent precision.
        Assert.NotEqual(Math.Round(usdBase, 2), usdBase);
    }

    [Fact]
    public async Task NativeToUsdBase_ThenDisplayBack_RecoversAmountToTheCent()
    {
        var service = await SeededServiceAsync();

        // Store: CAD -> USD base (the value a chart/aggregate reads).
        var usdBase = await service.ConvertToUSDAsync(10m, "CAD", RateDate);

        // Display: USD base -> CAD at the boundary (still 2dp rounded).
        var ok = service.TryConvertExact(usdBase, "USD", "CAD", RateDate, out var displayed);

        Assert.True(ok);
        Assert.Equal(10.00m, displayed);
    }

    [Fact]
    public async Task TryConvertToUsdBase_DoesNotRound_UnlikeDisplayConversion()
    {
        var service = await SeededServiceAsync();

        var storeOk = service.TryConvertToUsdBase(10m, "CAD", RateDate, out var storedUsd);

        Assert.True(storeOk);
        Assert.Equal(10m * (1m / UsdToCad), storedUsd);        // unrounded USD base
        Assert.NotEqual(Math.Round(storedUsd, 2), storedUsd);  // carries sub-cent precision

        // The display sibling on the same inputs DOES round to 2 decimals.
        var displayOk = service.TryConvertExact(6.1349m, "USD", "CAD", RateDate, out var displayed);
        Assert.True(displayOk);
        Assert.Equal(Math.Round(6.1349m * UsdToCad, 2), displayed);
    }

    [Fact]
    public void TryConvertToUsdBase_SameCurrency_ReturnsAmountUnchanged()
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new StubCadHandler(UsdToCad)));

        var ok = service.TryConvertToUsdBase(123.456m, "USD", RateDate, out var result);

        Assert.True(ok);
        Assert.Equal(123.456m, result);
    }

    [Fact]
    public void TryConvertToUsdBase_RateUncached_ReturnsFalse()
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new StubCadHandler(UsdToCad)));

        var ok = service.TryConvertToUsdBase(10m, "CAD", RateDate, out var result);

        Assert.False(ok);
        Assert.Equal(0m, result);
    }

    private sealed class StubCadHandler(decimal usdToCad) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "CAD": {{usdToCad}} } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MockPlatformService : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetTempPath() => Path.GetTempPath();
        public string GetCachePath() => Path.GetTempPath();
        public void EnsureDirectoryExists(string path) { }
        public bool SupportsFileSystem => false;
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
}
