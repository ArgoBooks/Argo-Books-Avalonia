using System.Net;
using System.Text;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A converted amount is rounded for display, and a total is the sum of those rounded rows. Rounded
/// to cents, three rows of 100.44 yen each showed as ¥100 while their total, 301.32, showed as ¥301.
/// </summary>
public class ExchangeRateRoundingTests
{
    [Fact]
    public async Task ConvertingToYen_RoundsToWholeYen()
    {
        var date = new DateTime(2026, 3, 2);
        var service = new ExchangeRateService(new NoDiskPlatform(), new HttpClient(new RateHandler("JPY", 150.137m)));
        await service.GetExchangeRateAsync("USD", "JPY", date);

        Assert.True(service.TryConvertFromUSD(0.669m, "JPY", date, out var yen)); // 100.441653
        Assert.Equal(100m, yen);
    }

    [Fact]
    public async Task ConvertingToEuros_StillRoundsToCents()
    {
        var date = new DateTime(2026, 3, 2);
        var service = new ExchangeRateService(new NoDiskPlatform(), new HttpClient(new RateHandler("EUR", 0.9137m)));
        await service.GetExchangeRateAsync("USD", "EUR", date);

        Assert.True(service.TryConvertFromUSD(10m, "EUR", date, out var euros)); // 9.137
        Assert.Equal(9.14m, euros);
    }

    private sealed class RateHandler(string code, decimal usdToCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "{{code}}": {{usdToCode}} } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class NoDiskPlatform : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetTempPath() => Path.GetTempPath();
        public string GetDefaultDocumentsPath() => Path.GetTempPath();
        public string GetLogsPath() => Path.GetTempPath();
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
        public void RegisterFileTypeAssociations(string iconPath) { }
        public StringComparer PathComparer => StringComparer.Ordinal;
    }
}
