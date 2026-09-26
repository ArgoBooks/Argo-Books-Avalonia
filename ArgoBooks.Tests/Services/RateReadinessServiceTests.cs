using System.Net;
using System.Text;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for <see cref="RateReadinessService"/>: it reports Ready only when every required (past)
/// transaction date's rate is cached, defers future dates, and classifies failures as no-internet
/// vs server-unreachable for the user-facing message.
/// </summary>
public class RateReadinessServiceTests
{
    [Fact]
    public async Task EnsureRatesAsync_AllCached_ReturnsReady_NoFetch()
    {
        var date = new DateTime(2026, 1, 5);
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new RatesHandler(0.9m)));
        await ex.GetExchangeRateAsync("USD", "EUR", date); // seed

        var svc = new RateReadinessService(ex, new AlwaysOnlineConnectivity());
        var result = await svc.EnsureRatesAsync([date]);

        Assert.Equal(RateReadinessStatus.Ready, result.Status);
        Assert.Empty(result.FutureDatesDeferred);
    }

    [Fact]
    public async Task EnsureRatesAsync_FutureDate_DeferredNotRequired()
    {
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new RatesHandler(0.9m)));
        var svc = new RateReadinessService(ex, new AlwaysOnlineConnectivity());

        var future = DateTime.Today.AddYears(1);
        var result = await svc.EnsureRatesAsync([future]);

        Assert.Equal(RateReadinessStatus.Ready, result.Status);
        Assert.Contains(future.Date, result.FutureDatesDeferred);
    }

    [Fact]
    public async Task EnsureRatesAsync_MissingDate_Offline_ReturnsNoInternet()
    {
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new FailingHandler()));
        var svc = new RateReadinessService(ex, new OfflineConnectivity());

        var result = await svc.EnsureRatesAsync([new DateTime(2026, 1, 5)]);

        Assert.Equal(RateReadinessStatus.Unavailable, result.Status);
        Assert.Equal(RateUnavailableReason.NoInternet, result.Reason);
    }

    [Fact]
    public async Task EnsureRatesAsync_MissingDate_OnlineButServerDown_ReturnsServerUnreachable()
    {
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(new FailingHandler()));
        var svc = new RateReadinessService(ex, new ServerDownConnectivity());

        var result = await svc.EnsureRatesAsync([new DateTime(2026, 1, 5)]);

        Assert.Equal(RateReadinessStatus.Unavailable, result.Status);
        Assert.Equal(RateUnavailableReason.ServerUnreachable, result.Reason);
    }

    [Fact]
    public async Task EnsureRatesAsync_BatchRateLimited_ReturnsRateLimited_WithoutFanningOut()
    {
        var handler = new RateLimitedHandler();
        var ex = new ExchangeRateService(new MockPlatform(), new HttpClient(handler));
        var svc = new RateReadinessService(ex, new AlwaysOnlineConnectivity());

        var result = await svc.EnsureRatesAsync([new DateTime(2026, 1, 5)]);

        Assert.Equal(RateReadinessStatus.Unavailable, result.Status);
        Assert.Equal(RateUnavailableReason.RateLimited, result.Reason);
        // A 429 makes exactly ONE request (the batch) and stops.
        Assert.Equal(1, handler.RequestCount);
    }

    // ---- stubs ----
    private sealed class RateLimitedHandler : HttpMessageHandler
    {
        public int RequestCount;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            Interlocked.Increment(ref RequestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            { Content = new StringContent("{\"success\":false,\"errorCode\":\"RATE_LIMITED\"}", Encoding.UTF8, "application/json") });
        }
    }
    private sealed class RatesHandler(decimal usdToEur) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent($$"""{ "success": true, "base": "USD", "rates": { "EUR": {{usdToEur}} } }""", Encoding.UTF8, "application/json") });
    }
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
    private sealed class AlwaysOnlineConnectivity : IConnectivityService
    {
        public Task<bool> IsInternetAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> IsHostReachableAsync(string host, CancellationToken ct = default) => Task.FromResult(true);
    }
    private sealed class OfflineConnectivity : IConnectivityService
    {
        public Task<bool> IsInternetAvailableAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> IsHostReachableAsync(string host, CancellationToken ct = default) => Task.FromResult(false);
    }
    private sealed class ServerDownConnectivity : IConnectivityService
    {
        public Task<bool> IsInternetAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> IsHostReachableAsync(string host, CancellationToken ct = default) => Task.FromResult(false);
    }
    private sealed class MockPlatform : IPlatformService
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
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("");
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
