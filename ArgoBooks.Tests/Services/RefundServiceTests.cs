using System.Net;
using System.Text;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the RefundService HTTP client. Mocks the underlying HttpClient
/// so we can verify request shape (path, headers, body) and response parsing
/// without standing up the PHP server.
/// </summary>
[Collection("DotEnv")]
public class RefundServiceTests
{
    private static (RefundService service, RecordingHandler handler) BuildService(
        HttpStatusCode status = HttpStatusCode.OK,
        string body = "{}")
    {
        // Activate a fake API key so the service emits Authorization: Bearer
        DotEnv.SetInMemory(PortalSettings.ApiKeyEnvVar, "test-api-key-fake");
        var handler = new RecordingHandler(status, body);
        var http = new HttpClient(handler);
        return (new RefundService(http), handler);
    }

    [Fact]
    public async Task RequestRefundAsync_HappyPath_SendsExpectedShape()
    {
        var (service, handler) = BuildService(HttpStatusCode.OK,
            "{\"success\":true,\"requestId\":42,\"expiresInSeconds\":600,\"maskedEmail\":\"ev**@argobooks.app\"}");
        try
        {
            var draft = new RefundDraft(
                InvoiceId: "INV-1",
                InvoiceNumber: "INV-2026-001",
                CustomerName: "Acme",
                Provider: "stripe",
                ProviderPaymentId: "pi_xyz",
                AmountCents: 12345,
                Currency: "USD",
                LineItems: null,
                Reason: null);

            var result = await service.RequestRefundAsync(draft);

            Assert.True(result.Ok);
            Assert.Equal(42, result.RequestId);
            Assert.Equal(600, result.ExpiresInSeconds);
            Assert.Equal("ev**@argobooks.app", result.MaskedEmail);

            var sent = handler.LastRequest!;
            Assert.Equal(HttpMethod.Post, sent.Method);
            Assert.EndsWith("/api/portal/refunds/request.php", sent.RequestUri!.ToString());
            Assert.Equal("Bearer", sent.Headers.Authorization?.Scheme);
            Assert.Equal("test-api-key-fake", sent.Headers.Authorization?.Parameter);
            Assert.True(sent.Headers.Contains("Idempotency-Key"));

            var sentBody = await sent.Content!.ReadAsStringAsync();
            // The PHP server expects snake_case (matches the spec's API contract).
            Assert.Contains("\"invoice_id\":\"INV-1\"", sentBody);
            Assert.Contains("\"amount_cents\":12345", sentBody);
            Assert.Contains("\"provider\":\"stripe\"", sentBody);
        }
        finally
        {
            DotEnv.Unset(PortalSettings.ApiKeyEnvVar);
        }
    }

    [Fact]
    public async Task RequestRefundAsync_ServerError_ReturnsParsedError()
    {
        var (service, _) = BuildService(HttpStatusCode.UnprocessableEntity,
            "{\"success\":false,\"error\":\"AMOUNT_EXCEEDS_REFUNDABLE\",\"refundable_cents\":1000}");
        try
        {
            var result = await service.RequestRefundAsync(new RefundDraft("INV-1", "001", "X", "stripe", "pi_x", 9999, "USD", null, null));
            Assert.False(result.Ok);
            Assert.Equal("AMOUNT_EXCEEDS_REFUNDABLE", result.ErrorCode);
            Assert.Equal((int)HttpStatusCode.UnprocessableEntity, result.HttpStatus);
        }
        finally { DotEnv.Unset(PortalSettings.ApiKeyEnvVar); }
    }

    [Fact]
    public async Task ConfirmCodeAsync_ParsesStateAndVelocityTier()
    {
        var (service, handler) = BuildService(HttpStatusCode.OK,
            "{\"success\":true,\"state\":\"cooling_off\",\"velocityTier\":\"delayed\",\"coolingOffSeconds\":900}");
        try
        {
            var result = await service.ConfirmCodeAsync(42, "123456");
            Assert.True(result.Ok);
            Assert.Equal("cooling_off", result.State);
            Assert.Equal("delayed", result.VelocityTier);
            Assert.Equal(900, result.CoolingOffSeconds);

            var sentBody = await handler.LastRequest!.Content!.ReadAsStringAsync();
            Assert.Contains("\"request_id\":42", sentBody);
            Assert.Contains("\"code\":\"123456\"", sentBody);
        }
        finally { DotEnv.Unset(PortalSettings.ApiKeyEnvVar); }
    }

    [Fact]
    public async Task ConfirmCodeAsync_WrongCode_ParsesAttemptsRemaining()
    {
        var (service, _) = BuildService(HttpStatusCode.Unauthorized,
            "{\"success\":false,\"error\":\"WRONG_CODE\",\"attemptsRemaining\":3}");
        try
        {
            var result = await service.ConfirmCodeAsync(42, "999999");
            Assert.False(result.Ok);
            Assert.Equal("WRONG_CODE", result.ErrorCode);
            Assert.Equal(3, result.AttemptsRemaining);
        }
        finally { DotEnv.Unset(PortalSettings.ApiKeyEnvVar); }
    }

    [Fact]
    public async Task GetStatusAsync_UsesGetWithIdInQuery()
    {
        var (service, handler) = BuildService(HttpStatusCode.OK,
            "{\"success\":true,\"requestId\":42,\"state\":\"completed\",\"providerRefundId\":\"re_abc\"}");
        try
        {
            var result = await service.GetStatusAsync(42);
            Assert.True(result.Ok);
            Assert.Equal("completed", result.State);
            Assert.Equal("re_abc", result.ProviderRefundId);

            Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
            Assert.EndsWith("/api/portal/refunds/status.php?id=42", handler.LastRequest.RequestUri!.ToString());
            Assert.False(handler.LastRequest.Headers.Contains("Idempotency-Key"), "GET should not send Idempotency-Key");
        }
        finally { DotEnv.Unset(PortalSettings.ApiKeyEnvVar); }
    }

    [Fact]
    public async Task NetworkError_IsCaughtAndReturnedStructurally()
    {
        DotEnv.SetInMemory(PortalSettings.ApiKeyEnvVar, "k");
        try
        {
            var http = new HttpClient(new ThrowingHandler(new HttpRequestException("DNS fail")));
            var service = new RefundService(http);
            var result = await service.RequestRefundAsync(
                new RefundDraft("INV-1", "001", "X", "stripe", "pi_x", 100, "USD", null, null));
            Assert.False(result.Ok);
            Assert.Equal("NETWORK_ERROR", result.ErrorCode);
            Assert.Equal((int)HttpStatusCode.ServiceUnavailable, result.HttpStatus);
        }
        finally { DotEnv.Unset(PortalSettings.ApiKeyEnvVar); }
    }

    [Fact]
    public async Task EmailChangeRequestAsync_RoutesToEmailChangeEndpoint()
    {
        var (service, handler) = BuildService(HttpStatusCode.OK,
            "{\"success\":true,\"changeId\":7,\"state\":\"pending\",\"maskedOldEmail\":\"ev**@x.com\"}");
        try
        {
            var result = await service.RequestEmailChangeAsync("new@example.com", passwordVerified: true);
            Assert.True(result.Ok);
            Assert.Equal(7, result.ChangeId);
            Assert.Equal("pending", result.State);

            Assert.EndsWith("/api/portal/account/email-change/request.php", handler.LastRequest!.RequestUri!.ToString());
            var body = await handler.LastRequest.Content!.ReadAsStringAsync();
            Assert.Contains("\"new_email\":\"new@example.com\"", body);
            Assert.Contains("\"password_verified\":true", body);
        }
        finally { DotEnv.Unset(PortalSettings.ApiKeyEnvVar); }
    }

    [Fact]
    public async Task ConfirmRegistrationCodeAsync_RoutesToVerifyEndpoint()
    {
        var (service, handler) = BuildService(HttpStatusCode.OK, "{\"success\":true,\"verifiedAt\":\"2026-05-09T00:00:00Z\"}");
        try
        {
            var result = await service.ConfirmRegistrationCodeAsync("424242");
            Assert.True(result.Ok);
            Assert.EndsWith("/api/portal/account/verify-email/confirm.php", handler.LastRequest!.RequestUri!.ToString());
            var body = await handler.LastRequest.Content!.ReadAsStringAsync();
            Assert.Contains("\"code\":\"424242\"", body);
        }
        finally { DotEnv.Unset(PortalSettings.ApiKeyEnvVar); }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }

        public RecordingHandler(HttpStatusCode status, string body)
        {
            _status = status; _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Buffer the request content so we can inspect it after SendAsync returns
            if (request.Content != null)
            {
                var raw = await request.Content.ReadAsStringAsync(cancellationToken);
                request.Content = new StringContent(raw, Encoding.UTF8, "application/json");
            }
            LastRequest = request;
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw ex;
    }
}
