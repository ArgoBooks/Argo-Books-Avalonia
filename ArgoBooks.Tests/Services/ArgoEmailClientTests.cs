using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.InvoiceTemplates;
using ArgoBooks.Core.Services.PurchaseOrders;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The invoice, purchase order and accountant pack emails go through one client, so they read the
/// server's answer and fail in the same words for the same reason.
/// </summary>
public class ArgoEmailClientTests
{
    private const string Endpoint = "https://example.test/api/send-email.php";

    private sealed record Request(string To, string? ReplyTo);

    private static ArgoEmailClient Client(Handler handler, string? licenseKey = "KEY-1", string? deviceId = "device-1", bool internet = true) =>
        new(new HttpClient(handler), () => (licenseKey, deviceId), new Connectivity(internet));

    private static Task<AccountantPackEmailResponse> Send(ArgoEmailClient client, EmailAuth auth = EmailAuth.LicenseKeyOrDevice,
        Func<HttpStatusCode, string?>? statusMessage = null, CancellationToken cancellationToken = default) =>
        client.SendAsync<Request, AccountantPackEmailResponse>(Endpoint, new Request("a@b.test", null), auth, statusMessage, cancellationToken);

    [Fact]
    public async Task TheServersJsonAnswer_IsReturnedAsIs()
    {
        var handler = Handler.Respond(HttpStatusCode.BadRequest, """{"success":false,"message":"Invalid recipient.","errorCode":"INVALID_EMAIL"}""");

        var response = await Send(Client(handler));

        Assert.Equal((false, "Invalid recipient.", "INVALID_EMAIL"), (response.Success, response.Message, response.ErrorCode));
    }

    [Fact]
    public async Task ASuccessStatusWithoutJson_CountsAsSent()
    {
        var response = await Send(Client(Handler.Respond(HttpStatusCode.OK, "OK")));

        Assert.True(response.Success);
        Assert.Equal(ArgoEmailClient.SentMessage, response.Message);
    }

    [Fact]
    public async Task AnHtmlErrorPage_IsSummarized_NotShownRaw()
    {
        var handler = Handler.Respond(HttpStatusCode.BadGateway, "<html><body><h1>502 Bad Gateway</h1></body></html>");

        var response = await Send(Client(handler));

        Assert.False(response.Success);
        Assert.Equal("502", response.ErrorCode);
        Assert.DoesNotContain("<", response.Message);
        Assert.Contains("502", response.Message);
    }

    [Fact]
    public async Task TheAccountantPacksTooLargeMessage_ReplacesTheSummaryFor413()
    {
        var handler = Handler.Respond(HttpStatusCode.RequestEntityTooLarge, "<html>413 Request Entity Too Large</html>");
        using var service = new AccountantPackEmailService(Client(handler));

        var response = await service.SendAsync(new AccountantPackEmailRequest { To = "a@b.test" });

        Assert.Equal("This pack is too large to email. Save it as a zip instead.", response.Message);
        Assert.Equal("413", response.ErrorCode);
    }

    [Fact]
    public async Task NoConnection_IsANetworkError_WithTheConnectivityMessage()
    {
        var response = await Send(Client(Handler.Throw(new HttpRequestException("No such host")), internet: false));

        Assert.Equal(("NETWORK_ERROR", ConnectivityMessage.NoInternet), (response.ErrorCode, response.Message));
    }

    [Fact]
    public async Task ATimeout_IsReportedAsOne()
    {
        var response = await Send(Client(Handler.Throw(new TaskCanceledException("timeout", new TimeoutException())), internet: false));

        Assert.Equal(("TIMEOUT", ConnectivityMessage.NoInternet), (response.ErrorCode, response.Message));
    }

    [Fact]
    public async Task Cancelling_IsNotReportedAsANetworkError()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var response = await Send(Client(Handler.Respond(HttpStatusCode.OK, "{}")), cancellationToken: cts.Token);

        Assert.Equal("CANCELLED", response.ErrorCode);
    }

    [Fact]
    public async Task AnEndpointNeedingALicenseKey_IsNotCalledWithOnlyADeviceId()
    {
        var handler = Handler.Respond(HttpStatusCode.OK, "{}");

        var response = await Send(Client(handler, licenseKey: null), EmailAuth.LicenseKey);

        Assert.Equal(("NOT_CONFIGURED", ArgoEmailClient.PremiumRequiredMessage), (response.ErrorCode, response.Message));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task AFreePlanEndpoint_IsCalledWithTheDeviceId()
    {
        var handler = Handler.Respond(HttpStatusCode.OK, """{"success":true,"message":"Email sent successfully."}""");

        var response = await Send(Client(handler, licenseKey: null));

        Assert.True(response.Success);
        Assert.Equal(["device-1"], handler.LastRequest!.Headers.GetValues("X-Device-Id"));
        Assert.False(handler.LastRequest.Headers.Contains("X-License-Key"));
    }

    [Fact]
    public async Task TheRequestBody_IsCamelCaseJson_WithNullsKept()
    {
        var handler = Handler.Respond(HttpStatusCode.OK, "{}");

        await Send(Client(handler));

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(Endpoint, handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("""{"to":"a@b.test","replyTo":null}""", handler.LastBody);
        Assert.Equal(["Bearer KEY-1"], handler.LastRequest.Headers.GetValues("Authorization"));
    }

    [Fact]
    public void TheInvoiceSubjectTotal_IsInTheInvoicesOwnCurrency()
    {
        var invoice = new Invoice { InvoiceNumber = "#INV-2026-00001", Total = 1234.5m, OriginalCurrency = "EUR" };
        var yen = new Invoice { Total = 1234.4m, OriginalCurrency = "JPY" };
        var settings = new CompanySettings();

        Assert.Equal("Invoice #INV-2026-00001 for €1,234.50", InvoiceEmailService.BuildSubject("Invoice {InvoiceNumber} for {Total}", invoice, settings));
        Assert.Equal("¥1,234", InvoiceEmailService.BuildSubject("{Total}", yen, settings));
    }

    [Fact]
    public void ThePurchaseOrderTotal_IsInTheOrdersOwnCurrency()
    {
        var data = new CompanyData();
        data.Suppliers.Add(new Supplier { Id = "SUP-001", Name = "Acme" });
        var order = new PurchaseOrder
        {
            PoNumber = "#PO-2026-001",
            SupplierId = "SUP-001",
            OriginalCurrency = "EUR",
            LineItems = [new PurchaseOrderLineItem { Quantity = 2, UnitCost = 500.25m }]
        };
        order.Subtotal = 1000.5m;
        order.Total = 1000.5m;

        Assert.Equal("#PO-2026-001 to Acme: €1,000.50",
            PurchaseOrderEmailService.FillTemplate("{PoNumber} to {SupplierName}: {Total}", order, data));
    }

    private sealed class Handler : HttpMessageHandler
    {
        private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.OK);
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public static Handler Respond(HttpStatusCode status, string body) => new()
        {
            _respond = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/html") }
        };

        public static Handler Throw(Exception exception) => new() { _respond = () => throw exception };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond();
        }
    }

    private sealed class Connectivity(bool internet) : IConnectivityService
    {
        public Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(internet);
        public Task<bool> IsHostReachableAsync(string host, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
