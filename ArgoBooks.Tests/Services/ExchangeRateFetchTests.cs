using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// How the app asks the server for exchange rates when an import needs many dates. The fake server
/// below behaves like the real one: it refuses a batch of more than 500 dates, and prices only a
/// limited number of new dates per batch request, listing the rest as failed.
/// </summary>
public class ExchangeRateFetchTests : IDisposable
{
    private static readonly System.Reflection.PropertyInfo SharedInstance = typeof(ExchangeRateService).GetProperty(nameof(ExchangeRateService.Instance))!;
    private readonly ExchangeRateService? _sharedBefore = ExchangeRateService.Instance;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"argo-rates-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SharedInstance.SetValue(null, _sharedBefore);
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static List<DateTime> Days(int count) =>
        Enumerable.Range(0, count).Select(i => new DateTime(2015, 1, 1).AddDays(i)).ToList();

    [Fact]
    public async Task ManyDates_AreSentInBatchesTheServerAccepts()
    {
        var server = new FakeRateServer();
        var service = new ExchangeRateService(new TempPlatform(_folder, onDisk: false), new HttpClient(server));

        await service.PreloadRatesAsync(Days(1200));

        Assert.Equal(3, server.BatchRequests);
        Assert.Equal(0, server.SingleDateRequests);
        Assert.True(service.GetExchangeRate("USD", "EUR", new DateTime(2018, 4, 14)) > 0);
    }

    [Fact]
    public async Task DatesTheServerLeavesForLater_AreAskedForInAnotherBatch()
    {
        var server = new FakeRateServer { NewDatesPricedPerRequest = 150 };
        var service = new ExchangeRateService(new TempPlatform(_folder, onDisk: false), new HttpClient(server));

        await service.PreloadRatesAsync(Days(400));

        Assert.Equal(3, server.BatchRequests);
        Assert.Equal(0, server.SingleDateRequests);
        Assert.True(service.GetExchangeRate("USD", "EUR", new DateTime(2016, 2, 4)) > 0);
    }

    [Fact]
    public async Task DateTheServerCannotPrice_IsAskedForOnce()
    {
        var server = new FakeRateServer { SingleDateAnswer = (HttpStatusCode.BadGateway, """{ "success": false, "errorCode": "UPSTREAM_ERROR" }""") };
        var service = new ExchangeRateService(new TempPlatform(_folder, onDisk: false), new HttpClient(server));

        var rate = await service.GetExchangeRateAsync("USD", "EUR", new DateTime(2010, 7, 28));

        Assert.Equal(-1m, rate);
        Assert.Equal(1, server.SingleDateRequests);
    }

    /// <summary>A fake of the two rate endpoints, keyed on the request path.</summary>
    private sealed class FakeRateServer : HttpMessageHandler
    {
        public int NewDatesPricedPerRequest { get; init; } = int.MaxValue;
        public (HttpStatusCode Status, string Body) SingleDateAnswer { get; init; } =
            (HttpStatusCode.OK, """{ "success": true, "base": "USD", "rates": { "EUR": 0.9 } }""");
        public int BatchRequests { get; private set; }
        public int SingleDateRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("exchange-rates-batch.php", StringComparison.Ordinal))
            {
                SingleDateRequests++;
                return Json(SingleDateAnswer.Status, SingleDateAnswer.Body);
            }

            BatchRequests++;
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var dates = body.RootElement.GetProperty("dates").EnumerateArray().Select(d => d.GetString()!).ToList();
            if (dates.Count > 500)
                return Json(HttpStatusCode.BadRequest, """{ "success": false, "errorCode": "TOO_MANY_DATES" }""");

            var priced = dates.Take(NewDatesPricedPerRequest).ToList();
            var results = string.Join(",", priced.Select(d => $"\"{d}\": {{ \"EUR\": 0.9 }}"));
            var failed = string.Join(",", dates.Skip(priced.Count).Select(d => $"\"{d}\""));
            return Json(HttpStatusCode.OK, $$"""{ "success": true, "base": "USD", "results": { {{results}} }, "failed": [ {{failed}} ] }""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class TempPlatform(string folder, bool onDisk) : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => folder;
        public string GetCachePath() => folder;
        public void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);
        public bool SupportsFileSystem => onDisk;
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
