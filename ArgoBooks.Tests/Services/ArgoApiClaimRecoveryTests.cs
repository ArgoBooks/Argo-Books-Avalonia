using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Claiming an import batch, and what happens when the claim's response goes missing.
///
/// The claim is the moment the server is told a set of pending objects has reached the
/// merchant's books. A lost response used to leave the objects imported on the server
/// with nothing in the books, invisible to the next sync because they were no longer
/// pending. Nobody would have seen an error.
/// </summary>
public class ArgoApiClaimRecoveryTests
{
    private const string Key = "ab_test";
    private const string ObjectId = "rev_136ace96eaf4d428c8248b8f";

    private static CompanyData Company()
    {
        var data = new CompanyData();
        var api = data.Settings.Integrations.ArgoApi;
        api.Enabled = true;
        api.CompanyUid = "uid";
        api.DesktopKey = Key;
        return data;
    }

    private static ArgoApiSyncPreview PreviewWithOneSale() =>
        new([], [], [], [],
            [],
            [new ArgoRevenue(
                Id: ObjectId, Description: "Order #1042",
                Amount: 11300, Currency: "USD", TaxAmount: 0, DiscountAmount: 0, FeeAmount: 0,
                OccurredOn: "2026-08-14", Customer: null, Category: null, PaymentMethod: null,
                Reference: null, Notes: null, LineItems: null,
                Import: new ArgoImportState("pending", null, null, null))],
            [],
            new Dictionary<string, ArgoExternalRef>());

    /// <summary>
    /// Fails the claim, then answers the follow-up lookup with whatever the caller sets.
    /// Records every Idempotency-Key it is shown.
    /// </summary>
    private sealed class ClaimFailsHandler(string probeStatus, string? probeBatch) : HttpMessageHandler
    {
        public readonly List<string> IdempotencyKeys = [];
        public int ProbeCalls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();

            if (request.Headers.TryGetValues("Idempotency-Key", out var keys))
                IdempotencyKeys.Add(string.Join("", keys));

            if (url.Contains("/import_batches"))
            {
                // The claim commits server-side, but the answer never arrives.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
                { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
            }

            ProbeCalls++;
            var batch = probeBatch == null ? "null" : $"\"{probeBatch}\"";
            var body = $"{{\"id\":\"{ObjectId}\",\"object\":\"revenue\",\"import\":" +
                       $"{{\"status\":\"{probeStatus}\",\"batch\":{batch},\"imported_at\":1,\"local_ref\":null}}}}";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>
    /// The one that matters: the claim landed, so the rows must stay and adopt the batch.
    /// </summary>
    [Fact]
    public async Task ClaimCommittedButResponseLost_KeepsTheRowsAndAdoptsTheBatch()
    {
        var handler = new ClaimFailsHandler("imported", "imb_21cf047240398e8c8f1c661f");
        var svc = new ArgoApiSyncService(new ArgoApiClient(new HttpClient(handler)));
        var data = Company();

        var creation = await svc.ImportPreviewAsync(data, PreviewWithOneSale());

        Assert.Equal("imb_21cf047240398e8c8f1c661f", creation.BatchId);
        Assert.Single(data.Revenues);
        Assert.Contains("imb_21cf047240398e8c8f1c661f", data.Settings.Integrations.ArgoApi.ImportedBatches);
        Assert.Equal(1, handler.ProbeCalls);
    }

    /// <summary>
    /// The claim genuinely failed, so rolling back is right. A wrong "keep" here would
    /// leave books the server disagrees with, which is the worse of the two errors.
    /// </summary>
    [Fact]
    public async Task ClaimGenuinelyFailed_RollsBackAndRethrows()
    {
        var handler = new ClaimFailsHandler("pending", null);
        var svc = new ArgoApiSyncService(new ArgoApiClient(new HttpClient(handler)));
        var data = Company();

        await Assert.ThrowsAnyAsync<Exception>(() => svc.ImportPreviewAsync(data, PreviewWithOneSale()));

        Assert.Empty(data.Revenues);
        Assert.Empty(data.Settings.Integrations.ArgoApi.ImportedBatches);
    }

    /// <summary>
    /// Imported but with no batch id is not proof the claim landed, so it must not be
    /// treated as one.
    /// </summary>
    [Fact]
    public async Task ImportedWithNoBatchId_IsNotTreatedAsProof()
    {
        var handler = new ClaimFailsHandler("imported", null);
        var svc = new ArgoApiSyncService(new ArgoApiClient(new HttpClient(handler)));
        var data = Company();

        await Assert.ThrowsAnyAsync<Exception>(() => svc.ImportPreviewAsync(data, PreviewWithOneSale()));

        Assert.Empty(data.Revenues);
    }

    /// <summary>
    /// The same objects are claimed again when an import is undone and run again. The server
    /// answers a repeated key with the first claim's batch, which the undo released, so the
    /// second claim has to carry a key of its own or it claims nothing.
    /// </summary>
    [Fact]
    public async Task ClaimingTheSameObjectsAgain_SendsANewIdempotencyKey()
    {
        var first = new ClaimFailsHandler("pending", null);
        var second = new ClaimFailsHandler("pending", null);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new ArgoApiSyncService(new ArgoApiClient(new HttpClient(first)))
                .ImportPreviewAsync(Company(), PreviewWithOneSale()));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            new ArgoApiSyncService(new ArgoApiClient(new HttpClient(second)))
                .ImportPreviewAsync(Company(), PreviewWithOneSale()));

        Assert.NotEmpty(first.IdempotencyKeys);
        Assert.NotEqual(first.IdempotencyKeys[0], second.IdempotencyKeys[0]);
    }
}
