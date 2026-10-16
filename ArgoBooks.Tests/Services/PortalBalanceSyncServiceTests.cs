using System.Net;
using System.Text;
using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// How payments recorded on this device reach the payment portal. A push that never lands leaves
/// the server believing an invoice is unpaid, and its reminder emails then chase a customer who has
/// already paid, so a failed push has to be sent again and every published invoice has to be
/// covered by the periodic sweep.
/// </summary>
[Collection("PortalApiKey")]
public class PortalBalanceSyncServiceTests : IDisposable
{
    private const string PublishedAction = "Published to Portal";

    private readonly string _priorKey = PortalSettings.ApiKey;
    private readonly CompanyData _company = new();
    private readonly RecordingHandler _handler = new();
    private readonly PaymentPortalService _portal;
    private readonly PortalBalanceSyncService _sync;

    public PortalBalanceSyncServiceTests()
    {
        PortalSettings.SetActiveApiKey("test-portal-key");
        _portal = new PaymentPortalService(new HttpClient(_handler));
        _sync = new PortalBalanceSyncService(_portal, () => _company);
    }

    public void Dispose()
    {
        _sync.Dispose();
        _portal.Dispose();
        PortalSettings.SetActiveApiKey(_priorKey);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A push the server did not accept goes back into the queue, so the next flush sends it again.
    /// Nothing else retries a queued invoice before the next full sweep.
    /// </summary>
    [Fact]
    public async Task FlushAsync_WhenThePushFails_SendsTheInvoiceAgainOnTheNextFlush()
    {
        AddInvoice("INV-1");
        _handler.Status = HttpStatusCode.InternalServerError;
        _sync.Queue("INV-1");
        await _sync.FlushAsync();

        _handler.Status = HttpStatusCode.OK;
        await _sync.FlushAsync();

        Assert.Contains(_handler.Requests, r => r.Status == HttpStatusCode.InternalServerError && r.InvoiceIds.Contains("INV-1"));
        Assert.Contains(_handler.Requests, r => r.Status == HttpStatusCode.OK && r.InvoiceIds.Contains("INV-1"));
    }

    /// <summary>A push the server accepted is done, so a later flush has nothing to send for it.</summary>
    [Fact]
    public async Task FlushAsync_AfterASuccessfulPush_DoesNotSendTheInvoiceAgain()
    {
        AddInvoice("INV-1");
        _sync.Queue("INV-1");
        await _sync.FlushAsync();
        await _sync.FlushAsync();

        Assert.Single(_handler.Requests, r => r.InvoiceIds.Contains("INV-1"));
    }

    /// <summary>
    /// Only published invoices exist on the server. Sending an unpublished one would hand the server
    /// amounts for an invoice it was never told about.
    /// </summary>
    [Fact]
    public async Task FlushAsync_InvoiceNeverPublished_IsNotSent()
    {
        AddInvoice("INV-1", published: false);
        _sync.Queue("INV-1");
        await _sync.FlushAsync();

        Assert.Empty(_handler.Requests);
    }

    /// <summary>
    /// The sweep sends a limited batch each time and starts the next sweep where the last one ended,
    /// so a company with more published invoices than one batch has every one of them sent within
    /// the expected number of sweeps instead of the same first batch forever.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_MoreInvoicesThanOneBatch_CoversEveryInvoiceAcrossSweeps()
    {
        const int invoiceCount = 60;
        for (var i = 0; i < invoiceCount; i++)
            AddInvoice($"INV-{i:D3}");

        await _sync.ReconcileAsync();
        var batchSize = _handler.Requests[0].InvoiceIds.Count;
        // A batch that already holds every invoice would not exercise the rotation at all.
        Assert.InRange(batchSize, 1, invoiceCount - 1);

        var sweeps = (invoiceCount + batchSize - 1) / batchSize;
        for (var sweep = 1; sweep < sweeps; sweep++)
            await _sync.ReconcileAsync();

        var covered = _handler.Requests.SelectMany(r => r.InvoiceIds).ToHashSet();
        Assert.Equal(invoiceCount, covered.Count);
    }

    /// <summary>
    /// A sweep that failed, for example while offline, is sent again from the same place. Moving on
    /// would leave those invoices stale on the server until the sweep came all the way round.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_WhenThePushFails_RetriesTheSameInvoicesNextSweep()
    {
        for (var i = 0; i < 60; i++)
            AddInvoice($"INV-{i:D3}");

        _handler.Status = HttpStatusCode.InternalServerError;
        await _sync.ReconcileAsync();
        _handler.Status = HttpStatusCode.OK;
        await _sync.ReconcileAsync();

        Assert.Equal(2, _handler.Requests.Count);
        Assert.Equal(_handler.Requests[0].InvoiceIds, _handler.Requests[1].InvoiceIds);
    }

    /// <summary>
    /// The sweep is the only retry for an invoice whose push failed, so paid and cancelled invoices
    /// are included: a paid one must stop being chased and a cancelled one must stop its reminders.
    /// Drafts and invoices never published do not exist on the server and are left out.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_IncludesPaidAndCancelled_ButNotDraftsOrUnpublished()
    {
        AddInvoice("INV-PAID", InvoiceStatus.Paid);
        AddInvoice("INV-CANCELLED", InvoiceStatus.Cancelled);
        AddInvoice("INV-SENT", InvoiceStatus.Sent);
        AddInvoice("INV-DRAFT", InvoiceStatus.Draft);
        AddInvoice("INV-UNPUBLISHED", InvoiceStatus.Sent, published: false);

        await _sync.ReconcileAsync();

        var sent = Assert.Single(_handler.Requests).InvoiceIds;
        Assert.Equal(["INV-CANCELLED", "INV-PAID", "INV-SENT"], sent.Order().ToList());
    }

    private void AddInvoice(string id, InvoiceStatus status = InvoiceStatus.Sent, bool published = true)
    {
        var invoice = new Invoice
        {
            Id = id,
            InvoiceNumber = id,
            CustomerId = "CUS-001",
            Status = status,
            Total = 100m,
            OriginalCurrency = "USD",
            DueDate = new DateTime(2026, 11, 5),
        };
        if (published)
            invoice.History.Add(new InvoiceHistoryEntry { Action = PublishedAction });
        _company.Invoices.Add(invoice);
    }

    private sealed record SentRequest(HttpStatusCode Status, List<string> InvoiceIds);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<SentRequest> _requests = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public List<SentRequest> Requests
        {
            get
            {
                lock (_gate)
                    return [.. _requests];
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = Status;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            var ids = json.RootElement.GetProperty("invoices").EnumerateArray()
                .Select(i => i.GetProperty("invoiceId").GetString()!)
                .ToList();
            lock (_gate)
                _requests.Add(new SentRequest(status, ids));

            var reply = status == HttpStatusCode.OK
                ? """{"success":true}"""
                : """{"success":false,"message":"Server unavailable"}""";
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(reply, Encoding.UTF8, "application/json")
            };
        }
    }
}
