using System.Net;
using System.Text;
using System.Text.Json;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Receipt scans, AI imports and invoice sends share one usage-limit policy
/// (docs/LicenseKey.md "Usage limits"). Each case runs for every limit, since the three used to
/// behave differently on the same failure.
/// </summary>
public class UsageLimitServiceTests
{
    public static TheoryData<string> Limits => ["receipt", "ai", "invoice"];

    private static UsageLimit LimitFor(string name) => name switch
    {
        "receipt" => UsageLimit.ReceiptScans,
        "ai" => UsageLimit.AiImports("bank"),
        _ => UsageLimit.InvoiceSends
    };

    private sealed class Harness
    {
        public readonly Handler Handler = new();
        public readonly Connectivity Connectivity = new();
        public readonly SpyErrorLogger Logger = new();
        public readonly UsageLimitService Service;
        public readonly UsageLimit Limit;

        public Harness(string limitName, string? licenseKey = "KEY-1", string? deviceId = "device-1")
        {
            Limit = LimitFor(limitName);
            Service = new UsageLimitService(Limit, () => (licenseKey, deviceId), new HttpClient(Handler), Connectivity, Logger);
        }

        public string Answer(bool success, bool allowed, int count = 3, int limit = 10, int remaining = 7, string? error = null) =>
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["success"] = success,
                [Limit.AllowedField] = allowed,
                [Limit.CountField] = count,
                ["monthly_limit"] = limit,
                ["remaining"] = remaining,
                ["tier"] = "free",
                ["resets_at"] = "2026-10-01",
                ["error"] = error
            });
    }

    #region Check

    [Theory, MemberData(nameof(Limits))]
    public async Task Allowed_CarriesTheCounts_AndIsReusedForFiveMinutes(string limit)
    {
        var h = new Harness(limit);
        h.Handler.Respond(h.Answer(success: true, allowed: true));

        var first = await h.Service.CheckUsageAsync();
        var second = await h.Service.CheckUsageAsync();

        Assert.True(first.Allowed);
        Assert.False(first.IsOffline);
        Assert.Equal((3, 10, 7, "free", "2026-10-01"), (first.Used, first.MonthlyLimit, first.Remaining, first.Tier, first.ResetsAt));
        Assert.Same(first, second);
        Assert.Equal(1, h.Handler.Calls);
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task LimitReached_Blocks_WithTheResetDateAndNoErrorMessage(string limit)
    {
        var h = new Harness(limit);
        h.Handler.Respond(h.Answer(success: true, allowed: false, count: 10, remaining: 0));

        var result = await h.Service.CheckUsageAsync();

        Assert.False(result.Allowed);
        Assert.Null(result.ErrorMessage);
        Assert.Equal((10, 10, "2026-10-01"), (result.Used, result.MonthlyLimit, result.ResetsAt));
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task LimitReached_ReportedAsAFailure_StillBlocks(string limit)
    {
        var h = new Harness(limit);
        h.Handler.Respond(h.Answer(success: false, allowed: false, count: 10, remaining: 0, error: "Monthly limit reached"));

        var result = await h.Service.CheckUsageAsync();

        Assert.False(result.Allowed);
        Assert.Null(result.ErrorMessage);
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task ServerError_Allows(string limit)
    {
        var h = new Harness(limit);
        h.Handler.Respond("""{"success":false,"error":"Database unavailable"}""", HttpStatusCode.InternalServerError);

        var result = await h.Service.CheckUsageAsync();

        Assert.True(result.Allowed);
        Assert.True(result.IsOffline);
        Assert.Equal(1, h.Logger.ErrorCount);
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task AnHtmlErrorPage_Allows(string limit)
    {
        var h = new Harness(limit);
        h.Handler.Respond("<html><body>502 Bad Gateway</body></html>", HttpStatusCode.BadGateway);

        var result = await h.Service.CheckUsageAsync();

        Assert.True(result.Allowed);
        Assert.True(result.IsOffline);
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task Unreachable_WithInternet_Allows(string limit)
    {
        var h = new Harness(limit);
        h.Handler.Throw(new HttpRequestException("Connection refused"));

        var result = await h.Service.CheckUsageAsync();

        Assert.True(result.Allowed);
        Assert.Null(result.ErrorMessage);
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task Unreachable_WithoutInternet_Blocks_WithTheConnectivityMessage(string limit)
    {
        var h = new Harness(limit);
        h.Connectivity.Internet = false;
        h.Handler.Throw(new HttpRequestException("No such host"));

        var result = await h.Service.CheckUsageAsync();

        Assert.False(result.Allowed);
        Assert.Equal(ConnectivityMessage.NoInternet, result.ErrorMessage);
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task TimedOut_WithoutInternet_Blocks_WithTheConnectivityMessage(string limit)
    {
        var h = new Harness(limit);
        h.Connectivity.Internet = false;
        h.Handler.Throw(new TaskCanceledException("Simulated HttpClient timeout", new TimeoutException()));

        var result = await h.Service.CheckUsageAsync();

        Assert.False(result.Allowed);
        Assert.Equal(ConnectivityMessage.NoInternet, result.ErrorMessage);
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task NoLicenseKeyOrDevice_Blocks_WithoutCallingTheServer(string limit)
    {
        var h = new Harness(limit, licenseKey: null, deviceId: "");

        var result = await h.Service.CheckUsageAsync();

        Assert.False(result.Allowed);
        Assert.Equal(UsageLimitService.NoIdentityMessage, result.ErrorMessage);
        Assert.Equal(0, h.Handler.Calls);
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task Cancelled_Blocks_AndIsNotLoggedAsAFailure(string limit)
    {
        var h = new Harness(limit);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        h.Handler.Respond(h.Answer(success: true, allowed: true));

        var result = await h.Service.CheckUsageAsync(cts.Token);

        Assert.False(result.Allowed);
        Assert.Equal(UsageLimitService.CancelledMessage, result.ErrorMessage);
        Assert.Equal((0, 0), (h.Logger.ErrorCount, h.Logger.WarningCount));
    }

    #endregion

    #region Increment

    [Theory, MemberData(nameof(Limits))]
    public async Task Increment_UpdatesTheCache(string limit)
    {
        var h = new Harness(limit);
        h.Handler.Respond(h.Answer(success: true, allowed: false, count: 10, remaining: 0));

        var increment = await h.Service.IncrementUsageAsync();
        var check = await h.Service.CheckUsageAsync();

        Assert.True(increment.Success);
        Assert.Equal((10, 0), (increment.Used, increment.Remaining));
        Assert.False(check.Allowed);
        Assert.Equal(1, h.Handler.Calls);
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task Increment_NetworkError_IsSuccessOffline(string limit)
    {
        var h = new Harness(limit);
        h.Handler.Throw(new HttpRequestException("Connection reset"));

        var result = await h.Service.IncrementUsageAsync();

        Assert.True(result.Success);
        Assert.True(result.IsOffline);
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task Increment_Cancelled_IsNotReportedAsANetworkFailure(string limit)
    {
        var h = new Harness(limit);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        h.Handler.Respond(h.Answer(success: true, allowed: true));

        var result = await h.Service.IncrementUsageAsync(cts.Token);

        Assert.False(result.Success);
        Assert.False(result.IsOffline);
        Assert.Equal(UsageLimitService.CancelledMessage, result.ErrorMessage);
        Assert.Equal((0, 0), (h.Logger.ErrorCount, h.Logger.WarningCount));
    }

    [Theory, MemberData(nameof(Limits))]
    public async Task EveryResponse_IsDisposed(string limit)
    {
        var h = new Harness(limit);
        h.Handler.Respond(h.Answer(success: true, allowed: true));

        await h.Service.IncrementUsageAsync();
        h.Service.InvalidateCache();
        await h.Service.CheckUsageAsync();

        Assert.Equal(2, h.Handler.Calls);
        Assert.Equal(2, h.Handler.DisposedResponses);
    }

    #endregion

    #region Wire format

    [Theory]
    [InlineData("receipt", "/api/receipt/usage.php", """{"license_key":"KEY-1","device_id":"device-1","action":"check"}""")]
    [InlineData("ai", "/api/ai-import/usage.php", """{"license_key":"KEY-1","device_id":"device-1","action":"check","type":"bank"}""")]
    [InlineData("invoice", "/api/invoice/usage.php", """{"license_key":"KEY-1","device_id":"device-1","action":"check"}""")]
    public async Task TheRequest_IsTheSameBodyToTheSameEndpoint(string limit, string path, string body)
    {
        var h = new Harness(limit);
        h.Handler.Respond(h.Answer(success: true, allowed: true));

        await h.Service.CheckUsageAsync();

        Assert.Equal(HttpMethod.Post, h.Handler.LastMethod);
        Assert.Equal($"{ApiConfig.BaseUrl}{path}", h.Handler.LastUrl);
        Assert.Equal("application/json", h.Handler.LastContentType);
        Assert.Equal(body, h.Handler.LastBody);
    }

    [Fact]
    public async Task IncrementWithoutTheAllowedFlag_KeepsAnUnlimitedPlanAllowed()
    {
        var h = new Harness("receipt");
        h.Handler.Respond("""{"success":true,"scan_count":40,"monthly_limit":-1,"remaining":-1}""");

        await h.Service.IncrementUsageAsync();

        Assert.True((await h.Service.CheckUsageAsync()).Allowed);
    }

    #endregion

    #region Fakes

    private sealed class Handler : HttpMessageHandler
    {
        private Func<HttpResponseMessage>? _respond;
        public int Calls { get; private set; }
        public int DisposedResponses { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastUrl { get; private set; }
        public string? LastContentType { get; private set; }
        public string? LastBody { get; private set; }

        public void Respond(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            _respond = () => new TrackedResponse(status, this) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public void Throw(Exception exception) => _respond = () => throw exception;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastMethod = request.Method;
            LastUrl = request.RequestUri?.ToString();
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond!();
        }

        private sealed class TrackedResponse(HttpStatusCode status, Handler owner) : HttpResponseMessage(status)
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing) owner.DisposedResponses++;
                base.Dispose(disposing);
            }
        }
    }

    private sealed class Connectivity : IConnectivityService
    {
        public bool Internet { get; set; } = true;
        public Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Internet);
        public Task<bool> IsHostReachableAsync(string host, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class SpyErrorLogger : IErrorLogger
    {
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }
        public void LogError(Exception exception, ErrorCategory category, string? context = null, string callerFile = "", int callerLine = 0, string callerMember = "") => ErrorCount++;
        public void LogError(string message, ErrorCategory category, string? context = null, string callerFile = "", int callerLine = 0, string callerMember = "") => ErrorCount++;
        public void LogWarning(string message, string? context = null, ErrorCategory category = ErrorCategory.Unknown, string? code = null, string callerFile = "", int callerLine = 0, string callerMember = "") => WarningCount++;
        public void LogInfo(string message) { }
        public void LogDebug(string message) { }
        public IReadOnlyList<ErrorLogEntry> GetRecentErrors(int count = 50) => [];
        public event EventHandler<ErrorLogEntry>? ErrorLogged { add { } remove { } }
    }

    #endregion
}
