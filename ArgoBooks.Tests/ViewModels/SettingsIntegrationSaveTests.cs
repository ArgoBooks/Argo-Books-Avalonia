using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Connecting or disconnecting an integration already takes effect with Stripe or the server, and
/// closing the app does not ask about it, so the change has to reach the file straight away.
/// </summary>
[Collection("ModalViewModels")]
public class SettingsIntegrationSaveTests : IDisposable
{
    private static readonly PropertyInfo SharedHttpClientProperty =
        typeof(App).GetProperty(nameof(App.SharedHttpClient), BindingFlags.Public | BindingFlags.Static)!;

    private readonly HttpClient? _priorClient = App.SharedHttpClient;
    private readonly CompanyManager? _priorManager = App.CompanyManager;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"argo-integration-{Guid.NewGuid():N}.argo");
    private readonly CompanyManager _manager;

    public SettingsIntegrationSaveTests()
    {
        var footer = new FooterService();
        _manager = new CompanyManager(
            new FileService(new CompressionService(), footer),
            new GlobalSettingsService(new MockPlatformService()),
            footer);
        App.SetCompanyManagerForTesting(_manager);
        SharedHttpClientProperty.SetValue(null, new HttpClient(new StripeAcceptsTheKey()));
    }

    public void Dispose()
    {
        SharedHttpClientProperty.SetValue(null, _priorClient);
        App.SetCompanyManagerForTesting(_priorManager);
        _manager.CloseCompanyAsync().GetAwaiter().GetResult();
        _manager.Dispose();
        if (File.Exists(_path)) File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private sealed class MockPlatformService : ArgoBooks.Core.Platform.IPlatformService
    {
        private readonly string _appDataPath = TestTempFolder.New("ArgoBooks_Test_");

        public ArgoBooks.Core.Platform.PlatformType Platform => ArgoBooks.Core.Platform.PlatformType.Linux;
        public string GetAppDataPath() => _appDataPath;
        public string GetCachePath() => Path.GetTempPath();
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
        public StringComparer PathComparer => StringComparer.Ordinal;
    }

    private sealed class StripeAcceptsTheKey : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"object\":\"list\",\"data\":[]}", Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public async Task ConnectingStripe_IsKept_WhenTheCompanyClosesWithoutSaving()
    {
        await _manager.CreateCompanyAsync(_path, "Acme");
        await _manager.SaveCompanyAsync();
        var vm = new SettingsModalViewModel { StripeKeyInput = "rk_test_abc" };

        await vm.ConnectStripeIntegrationCommand.ExecuteAsync(null);
        await _manager.CloseCompanyAsync();

        Assert.True(await _manager.OpenCompanyAsync(_path));
        var stripe = _manager.CompanyData!.Settings.Integrations.Stripe;
        Assert.True(stripe.Connected);
        Assert.Equal("rk_test_abc", stripe.ApiKey);
    }

    [Fact]
    public async Task DisconnectingStripe_IsKept_WhenTheCompanyClosesWithoutSaving()
    {
        await _manager.CreateCompanyAsync(_path, "Acme");
        var stripe = _manager.CompanyData!.Settings.Integrations.Stripe;
        stripe.ApiKey = "rk_test_abc";
        stripe.Connected = true;
        await _manager.SaveCompanyAsync();
        var vm = new SettingsModalViewModel();

        await vm.DisconnectStripeIntegrationCommand.ExecuteAsync(null);
        await _manager.CloseCompanyAsync();

        Assert.True(await _manager.OpenCompanyAsync(_path));
        Assert.False(_manager.CompanyData!.Settings.Integrations.Stripe.Connected);
        Assert.Null(_manager.CompanyData.Settings.Integrations.Stripe.ApiKey);
    }
}
