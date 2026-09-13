using System.Net;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Fetching a rate edition from the server, so a CRA changeover is a file upload rather than an
/// app release.
///
/// The whole risk of doing this over the network is that a bad file starts deciding real
/// withholding, so the behaviour worth pinning is what happens when the download is NOT good.
/// In every one of those cases the answer has to be the same: leave what is already there
/// alone. An app that keeps calculating on last week's verified edition is in a far better
/// state than one that has just overwritten it with a truncated response.
/// </summary>
public class PayrollRateUpdateServiceTests : IDisposable
{
    private readonly string _cache = Path.Combine(
        Path.GetTempPath(), "argo-rate-update-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private PayrollRateService Rates() => new(new CacheOnlyPlatformService(_cache));

    private PayrollRateUpdateService Updater(PayrollRateService rates, HttpStatusCode status, string body) =>
        new(rates, new StubHandler(status, body));

    private string CachedFile(string edition) => Path.Combine(_cache, "Payroll", edition + ".json");

    /// <summary>
    /// A well formed edition covering 2027, which no shipped file covers, so finding it proves
    /// the download rather than the embedded copy.
    /// </summary>
    private static string ValidEdition(string id = "2027-01") => $$"""
        {
          "editionId": "{{id}}",
          "effectiveFrom": "2027-01-01T00:00:00",
          "effectiveTo": "2027-06-30T00:00:00",
          "federal": {
            "brackets": [ { "upTo": 60000, "rate": 0.15, "constantK": 0 },
                          { "upTo": null, "rate": 0.20, "constantK": 3000 } ],
            "basicPersonalAmount": { "maximum": 16000 },
            "canadaEmploymentAmount": 1500,
            "lowestRateForCredits": 0.15
          },
          "cpp":  { "rateEmployee": 0.06, "baseRateEmployee": 0.05, "basicExemptionAnnual": 3500,
                    "ympeCeiling": 75000, "maxContributionEmployee": 4290.00 },
          "cpp2": { "rateEmployee": 0.04, "yampeCeiling": 85000, "maxContributionEmployee": 400.00 },
          "ei":   { "rateEmployee": 0.016, "employerMultiplier": 1.4, "maxInsurableEarnings": 70000,
                    "maxPremiumEmployee": 1120.00 },
          "provinces": {
            "AB": { "brackets": [ { "upTo": null, "rate": 0.10, "constantK": 0 } ],
                    "basicPersonalAmount": { "maximum": 23000 } }
          }
        }
        """;

    [Fact]
    public async Task AValidEdition_IsCachedAndUsableWithoutARestart()
    {
        // The point of the whole feature. Downloading is not enough: PayrollRateService caches
        // its parsed editions, so without the Invalidate afterwards the new file sits on disk
        // and nothing sees it until the app is restarted.
        PayrollRateService rates = Rates();
        Assert.Null(rates.GetForDate(new DateTime(2027, 3, 1)));

        bool updated = await Updater(rates, HttpStatusCode.OK, ValidEdition()).TryUpdateAsync("2027-01");

        Assert.True(updated);
        Assert.Equal("2027-01", rates.GetForDate(new DateTime(2027, 3, 1))?.EditionId);
    }

    [Fact]
    public async Task AnEditionThatContradictsItself_IsNotWrittenAtAll()
    {
        // A CPP maximum that does not follow from its own rate. This is the case the validator
        // exists for, and the file must not reach the cache directory even once: anything on
        // disk is picked up ahead of the embedded copy on the next load.
        string broken = ValidEdition().Replace("\"maxContributionEmployee\": 4290.00", "\"maxContributionEmployee\": 9999.00");
        PayrollRateService rates = Rates();

        bool updated = await Updater(rates, HttpStatusCode.OK, broken).TryUpdateAsync("2027-01");

        Assert.False(updated);
        Assert.False(File.Exists(CachedFile("2027-01")));
        Assert.Null(rates.GetForDate(new DateTime(2027, 3, 1)));
    }

    [Fact]
    public async Task AResponseThatIsNotAnEditionAtAll_IsNotWritten()
    {
        // A captive portal or an error page returning 200 with HTML is the ordinary way this
        // goes wrong on a hotel network.
        PayrollRateService rates = Rates();

        bool updated = await Updater(rates, HttpStatusCode.OK, "<html>Sign in to continue</html>")
            .TryUpdateAsync("2027-01");

        Assert.False(updated);
        Assert.False(File.Exists(CachedFile("2027-01")));
    }

    [Fact]
    public async Task AnEditionWhoseIdDoesNotMatchWhatWasAskedFor_IsNotWritten()
    {
        // Otherwise a misconfigured route could serve one edition under every name and quietly
        // overwrite the file for a period it does not cover.
        PayrollRateService rates = Rates();

        bool updated = await Updater(rates, HttpStatusCode.OK, ValidEdition("2027-07"))
            .TryUpdateAsync("2027-01");

        Assert.False(updated);
        Assert.False(File.Exists(CachedFile("2027-01")));
    }

    [Fact]
    public async Task NothingPublishedYet_IsNotTreatedAsAFailure()
    {
        // The reminder fires two weeks before the changeover, so asking before the file is
        // uploaded is the normal case rather than an error worth logging as one.
        PayrollRateService rates = Rates();

        bool updated = await Updater(rates, HttpStatusCode.NotFound, "Not found").TryUpdateAsync("2027-01");

        Assert.False(updated);
        Assert.False(File.Exists(CachedFile("2027-01")));
    }

    [Fact]
    public async Task AnEditionAlreadyCached_SurvivesALaterBadDownload()
    {
        // The one that matters most. Having got a good edition, a later broken response must
        // not take it away: the app would stop being able to run payroll for that period.
        PayrollRateService rates = Rates();
        await Updater(rates, HttpStatusCode.OK, ValidEdition()).TryUpdateAsync("2027-01");
        Assert.NotNull(rates.GetForDate(new DateTime(2027, 3, 1)));

        bool updated = await Updater(rates, HttpStatusCode.OK, "{ garbage").TryUpdateAsync("2027-01");

        Assert.False(updated);
        Assert.Equal("2027-01", rates.GetForDate(new DateTime(2027, 3, 1))?.EditionId);
    }

    [Fact]
    public async Task AServerThatIsUnreachable_ReturnsFalseRatherThanThrowing()
    {
        // Called on a schedule in the background, so an offline machine must not surface an
        // exception to a user who was not asking for anything.
        PayrollRateService rates = Rates();
        var updater = new PayrollRateUpdateService(rates, new ThrowingHandler());

        Assert.False(await updater.TryUpdateAsync("2027-01"));
    }

    [Fact]
    public async Task TheEditionIdIsUsedInTheUrl_SoTheServerCanHostBothHalvesOfAYear()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidEdition());
        await new PayrollRateUpdateService(Rates(), handler).TryUpdateAsync("2027-01");

        Assert.NotNull(handler.LastUrl);
        Assert.Contains("2027-01", handler.LastUrl);
    }

    [Theory]
    [InlineData(2026, 1, 1, "2026-01")]
    [InlineData(2026, 6, 30, "2026-01")]
    [InlineData(2026, 7, 1, "2026-07")]
    [InlineData(2026, 12, 31, "2026-07")]
    [InlineData(2027, 3, 15, "2027-01")]
    public void TheEditionAPayDateNeeds_IsDerivedFromTheDate(int y, int m, int d, string expected)
    {
        // CRA names its editions by the half of the year they take effect in, so the id a pay
        // date needs can be worked out without having the table that would tell you. That is
        // the point: this is used precisely when no table is loaded.
        Assert.Equal(expected, PayrollRateUpdateService.EditionIdFor(new DateTime(y, m, d)));
    }

    [Fact]
    public async Task AskingForThePayDatesEdition_FetchesTheRightHalfOfTheYear()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidEdition());
        var updater = new PayrollRateUpdateService(Rates(), handler);

        await updater.TryUpdateForDateAsync(new DateTime(2027, 2, 10));

        Assert.Contains("2027-01", handler.LastUrl!);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No such host is known.");
    }

    private sealed class CacheOnlyPlatformService(string cachePath) : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => cachePath;
        public string GetTempPath() => Path.GetTempPath();
        public string GetDefaultDocumentsPath() => Path.GetTempPath();
        public string GetLogsPath() => cachePath;
        public string GetCachePath() => cachePath;
        public void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);
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
        public void RegisterFileTypeAssociations(string iconPath) { }
        public StringComparer PathComparer => StringComparer.Ordinal;
    }
}
