using System.Net;
using System.Text;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The exact-date rule: money converts only at the transaction's own date. When that date's rate
/// is not cached, the strict chokepoint <see cref="ExchangeRateService.TryConvertExact"/> reports
/// failure (so callers show a pending state) rather than substituting a different date's rate or
/// the raw USD amount.
/// </summary>
public class CurrencyDisplayFallbackTests
{
    [Fact]
    public async Task TryConvertExact_ExactDateUncached_ReturnsFalse_NoWrongDateRate()
    {
        // Only "today's" USD->CAD rate is cached.
        var seededDate = new DateTime(2026, 6, 16);
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new StubCadHandler(1.40m)));
        await service.GetExchangeRateAsync("USD", "CAD", seededDate);

        // A historical transaction whose exact-date rate was never cached must NOT convert from a
        // different date's rate.
        var txnDate = new DateTime(2026, 1, 5);
        var ok = service.TryConvertExact(1611.40m, "USD", "CAD", txnDate, out var result);

        Assert.False(ok);
        Assert.Equal(0m, result);
    }

    [Fact]
    public async Task TryConvertExact_ExactDateCached_ConvertsPrecisely()
    {
        var date = new DateTime(2026, 1, 5);
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new StubCadHandler(1.40m)));
        await service.GetExchangeRateAsync("USD", "CAD", date); // seed the exact date

        var ok = service.TryConvertExact(1611.40m, "USD", "CAD", date, out var result);

        Assert.True(ok);
        Assert.Equal(Math.Round(1611.40m * 1.40m, 2), result);
    }

    [Fact]
    public void TryConvertExact_SameCurrency_ReturnsAmount()
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new StubCadHandler(1.40m)));

        var ok = service.TryConvertExact(99.99m, "CAD", "CAD", new DateTime(2026, 1, 5), out var result);

        Assert.True(ok);
        Assert.Equal(99.99m, result);
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
        public bool SupportsFileSystem => false; // skip disk persistence in tests
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
