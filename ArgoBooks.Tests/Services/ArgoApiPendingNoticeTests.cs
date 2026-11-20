using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The notice that a connected app has sent data still waiting to be imported, which the app checks for
/// when a company opens and on every visit to the Revenue page.
/// </summary>
[Collection("ModalViewModels")]
public class ArgoApiPendingNoticeTests
{
    private sealed class AccountHandler : HttpMessageHandler
    {
        public int Expenses { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = $$$"""{"id":"acct_1","display_name":"Shop","company_uid":"uid","pending":{"expenses":{{{Expenses}}},"revenue":1}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static CompanyData Company(string key)
    {
        var data = new CompanyData();
        var api = data.Settings.Integrations.ArgoApi;
        api.Enabled = true;
        api.CompanyUid = "uid";
        api.DesktopKey = key;
        return data;
    }

    [Fact]
    public async Task WaitingData_IsToldOnceForEachNewCount()
    {
        ArgoApiPendingNotice.Forget();
        var handler = new AccountHandler { Expenses = 2 };
        var http = new HttpClient(handler);
        var told = 0;
        void Tell() => told++;

        Assert.Equal(3, await ArgoApiPendingNotice.CheckAsync(Company("ab_one"), http, Tell));
        Assert.Equal(1, told);

        await ArgoApiPendingNotice.CheckAsync(Company("ab_one"), http, Tell);
        Assert.Equal(1, told);

        handler.Expenses = 5;
        await ArgoApiPendingNotice.CheckAsync(Company("ab_one"), http, Tell);
        Assert.Equal(2, told);

        // Another company with the same count waiting still gets its own notice.
        await ArgoApiPendingNotice.CheckAsync(Company("ab_two"), http, Tell);
        Assert.Equal(3, told);
    }

    [Fact]
    public async Task ACompanyWithoutTheApi_IsNotChecked()
    {
        var told = 0;

        Assert.Null(await ArgoApiPendingNotice.CheckAsync(new CompanyData(), new HttpClient(new AccountHandler()), () => told++));
        Assert.Equal(0, told);
    }
}
