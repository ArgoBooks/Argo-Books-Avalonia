using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A company keeps the API switched on in its file after the plan lapses. Without Premium the tab
/// only offers an upgrade, so asking the server for keys just puts its refusal on screen.
/// </summary>
[Collection("ModalViewModels")]
public class SettingsArgoApiTests : IDisposable
{
    private static readonly PropertyInfo SharedHttpClientProperty =
        typeof(App).GetProperty(nameof(App.SharedHttpClient), BindingFlags.Public | BindingFlags.Static)!;

    private readonly HttpClient? _priorClient = App.SharedHttpClient;
    private readonly RecordingHandler _server = new();

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Paths) Paths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"success":false,"message":"The API is not enabled for this company.","error_code":"API_NOT_ENABLED"}""",
                    Encoding.UTF8, "application/json")
            });
        }
    }

    public SettingsArgoApiTests()
    {
        SharedHttpClientProperty.SetValue(null, new HttpClient(_server));

        var data = new CompanyData();
        data.Settings.Integrations.ArgoApi.Enabled = true;
        data.Settings.Integrations.ArgoApi.CompanyUid = "uid";
        App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(data));
    }

    public void Dispose()
    {
        SharedHttpClientProperty.SetValue(null, _priorClient);
        GC.SuppressFinalize(this);
    }

    private bool AskedForKeys()
    {
        lock (_server.Paths) return _server.Paths.Any(p => p.EndsWith("/keys"));
    }

    [Fact]
    public void WithoutPremium_TheKeysAreNotRequested()
    {
        var vm = new SettingsModalViewModel { HasPremium = false };

        vm.OpenWithTab(SettingsTab.Integrations);

        Assert.False(AskedForKeys());
        Assert.Null(vm.ArgoApiError);
    }

    [Fact]
    public void PremiumConfirmedAfterOpening_RequestsTheKeys()
    {
        var vm = new SettingsModalViewModel { HasPremium = false };
        vm.OpenWithTab(SettingsTab.Integrations);

        vm.HasPremium = true;

        Assert.True(AskedForKeys());
    }
}
