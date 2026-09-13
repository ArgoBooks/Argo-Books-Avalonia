using System.Net;
using System.Text;
using System.Text.Json;
using ArgoBooks.Core.Services.Sync;
using ArgoBooks.Shared.Mobile;
using ArgoBooks.Shared.Sync;
using Xunit;

namespace ArgoBooks.Tests.Mobile;

/// <summary>
/// Unit tests for CaptureDeliveryCoordinator: a confirmed capture must survive a failed push and go
/// out on a later retry, and it must reach the company it was captured for rather than whichever one
/// happens to be active when it is reviewed.
/// </summary>
public class CaptureDeliveryCoordinatorTests
{
    /// <summary>Fakes the sync server's /queue/push, recording every request so a test can assert
    /// which company a capture went to and how many times it was sent.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public bool FailEveryRequest;
        public readonly List<(string? DeviceToken, string Ciphertext)> Pushes = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var token = request.Headers.TryGetValues("X-Sync-Device-Token", out var values)
                ? values.FirstOrDefault()
                : null;

            if (body != null)
            {
                using var doc = JsonDocument.Parse(body);
                Pushes.Add((token, doc.RootElement.GetProperty("ciphertext").GetString() ?? string.Empty));
            }

            var status = FailEveryRequest ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class InMemorySecureStore : ISecureStore
    {
        private readonly Dictionary<string, string> _storage = new();

        public Task SetAsync(string key, string value)
        {
            _storage[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string key)
        {
            _storage.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task RemoveAsync(string key)
        {
            _storage.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryPendingScanStorage : IPendingScanStorage
    {
        private readonly Dictionary<string, byte[]> _images = new();
        private readonly Dictionary<string, string> _metadata = new();

        public Task<IReadOnlyList<string>> ListIdsAsync() =>
            Task.FromResult<IReadOnlyList<string>>(_images.Keys.Concat(_metadata.Keys).Distinct().ToList());

        public Task SaveAsync(string id, byte[] imageBytes)
        {
            _images[id] = imageBytes;
            return Task.CompletedTask;
        }

        public Task<byte[]?> LoadAsync(string id)
        {
            _images.TryGetValue(id, out var bytes);
            return Task.FromResult(bytes);
        }

        public Task SaveMetadataAsync(string id, string json)
        {
            _metadata[id] = json;
            return Task.CompletedTask;
        }

        public Task<string?> LoadMetadataAsync(string id)
        {
            _metadata.TryGetValue(id, out var json);
            return Task.FromResult(json);
        }

        public Task DeleteAsync(string id)
        {
            _images.Remove(id);
            _metadata.Remove(id);
            return Task.CompletedTask;
        }
    }

    private const string CompanyA = "company-a";
    private const string CompanyB = "company-b";
    private const string TokenA = "device-tok-a";
    private const string TokenB = "device-tok-b";
    private const string KeyA = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";
    private const string KeyB = "OTg3NjU0MzIxMDk4NzY1NDMyMTA5ODc2NTQzMjEwOTg=";

    private static CapturedTransaction ReviewedTransaction(string supplier = "Office Depot") => new()
    {
        Type = CapturedTransactionType.Expense,
        SupplierOrCustomer = supplier,
        Date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        Total = 54.00m,
        Tax = 4.00m,
        LineItems =
        [
            new CapturedLineItem
            {
                Description = "Printer paper",
                Quantity = 2,
                UnitPrice = 25.00m,
                Total = 50.00m,
                ProductName = "Recycled paper"
            }
        ],
        ScanUid = "aaaabbbbccccddddeeeeffff00001111"
    };

    private static async Task<PairedCompanyStore> NewStoreAsync(params string[] companyUids)
    {
        var store = new PairedCompanyStore(new InMemorySecureStore());

        foreach (var uid in companyUids)
        {
            await store.SaveAsync(new PairedCompanyRecord
            {
                CompanyUid = uid,
                CompanyLabel = uid == CompanyA ? "Acme Corp" : "Beta Ltd",
                DeviceToken = uid == CompanyA ? TokenA : TokenB,
                SyncKeyBase64 = uid == CompanyA ? KeyA : KeyB,
            });
        }

        return store;
    }

    private static (CaptureDeliveryCoordinator Delivery, PendingScanOutbox Outbox, RecordingHandler Handler) Build(
        PairedCompanyStore store, IPendingScanStorage? storage = null)
    {
        var handler = new RecordingHandler();
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");
        var outbox = new PendingScanOutbox(storage ?? new InMemoryPendingScanStorage());
        var delivery = new CaptureDeliveryCoordinator(new CapturePushCoordinator(client, store), outbox);
        return (delivery, outbox, handler);
    }

    [Fact]
    public async Task DeliverAsync_PushFails_KeepsTheReviewedTransactionForALaterRetry()
    {
        var store = await NewStoreAsync(CompanyA);
        await store.SetActiveAsync(CompanyA);
        var (delivery, outbox, handler) = Build(store);
        handler.FailEveryRequest = true;

        var transaction = ReviewedTransaction();
        transaction.SupplierOrCustomer = "Corrected supplier";

        var delivered = await delivery.DeliverAsync(transaction, queueId: null, CompanyA, CancellationToken.None);

        Assert.False(delivered);

        var awaitingPush = await outbox.GetAwaitingPushAsync([CompanyA]);
        var pending = Assert.Single(awaitingPush);
        Assert.Equal("Corrected supplier", pending.Transaction.SupplierOrCustomer);
        Assert.Equal(54.00m, pending.Transaction.Total);
        Assert.Equal("Recycled paper", pending.Transaction.LineItems[0].ProductName);
        Assert.Equal(CompanyA, pending.CompanyUid);
    }

    [Fact]
    public async Task RetryAwaitingPushAsync_AfterAFailedPush_DeliversTheReviewedTransactionExactlyOnce()
    {
        var store = await NewStoreAsync(CompanyA);
        await store.SetActiveAsync(CompanyA);
        var (delivery, outbox, handler) = Build(store);
        handler.FailEveryRequest = true;

        var transaction = ReviewedTransaction("Corrected supplier");
        await delivery.DeliverAsync(transaction, queueId: null, CompanyA, CancellationToken.None);
        var attemptsBeforeRetry = handler.Pushes.Count;

        handler.FailEveryRequest = false;
        var delivered = await delivery.RetryAwaitingPushAsync([CompanyA], CancellationToken.None);

        Assert.Equal(1, delivered);
        Assert.Equal(attemptsBeforeRetry + 1, handler.Pushes.Count);

        var sent = JsonSerializer.Deserialize<CapturedTransaction>(
            SyncCrypto.Decrypt(handler.Pushes[^1].Ciphertext, KeyA));
        Assert.NotNull(sent);
        Assert.Equal("Corrected supplier", sent.SupplierOrCustomer);
        Assert.Equal(54.00m, sent.Total);
        Assert.Equal(transaction.ScanUid, sent.ScanUid);

        // Delivered means gone: a second retry must not send it again.
        var again = await delivery.RetryAwaitingPushAsync([CompanyA], CancellationToken.None);
        Assert.Equal(0, again);
        Assert.Equal(attemptsBeforeRetry + 1, handler.Pushes.Count);
        Assert.Equal(new OutboxCounts(0, 0, 0), await outbox.GetCountsAsync([CompanyA]));
    }

    [Fact]
    public async Task DeliverAsync_QueuedForOneCompany_PushesToItEvenWhenAnotherIsActive()
    {
        var store = await NewStoreAsync(CompanyA, CompanyB);
        await store.SetActiveAsync(CompanyA);
        var (delivery, outbox, handler) = Build(store);

        // Captured offline while A was active, then the user switches to B before reviewing it.
        var queueId = await outbox.EnqueueAsync([1, 2, 3], CompanyA);
        await store.SetActiveAsync(CompanyB);

        var queued = await outbox.PeekNextAsync([CompanyA, CompanyB]);
        Assert.NotNull(queued);
        Assert.Equal(CompanyA, queued.CompanyUid);

        var delivered = await delivery.DeliverAsync(
            ReviewedTransaction(), queueId, queued.CompanyUid, CancellationToken.None);

        Assert.True(delivered);
        var push = Assert.Single(handler.Pushes);
        Assert.Equal(TokenA, push.DeviceToken);

        // Encrypted with A's key, so it can only ever be read by A's desktop.
        var sent = JsonSerializer.Deserialize<CapturedTransaction>(SyncCrypto.Decrypt(push.Ciphertext, KeyA));
        Assert.Equal("Office Depot", sent!.SupplierOrCustomer);
        Assert.Equal(new OutboxCounts(0, 0, 0), await outbox.GetCountsAsync([CompanyA, CompanyB]));
    }

    [Fact]
    public async Task RetryAwaitingPushAsync_CompanyNoLongerPaired_SendsNothingAndKeepsItQueued()
    {
        var store = await NewStoreAsync(CompanyA, CompanyB);
        await store.SetActiveAsync(CompanyA);
        var (delivery, outbox, handler) = Build(store);
        handler.FailEveryRequest = true;

        await delivery.DeliverAsync(ReviewedTransaction(), queueId: null, CompanyA, CancellationToken.None);
        handler.FailEveryRequest = false;

        // A is unpaired; B is all that is left and becomes active.
        await store.RemoveAsync(CompanyA);
        await store.SetActiveAsync(CompanyB);

        var delivered = await delivery.RetryAwaitingPushAsync([CompanyB], CancellationToken.None);

        Assert.Equal(0, delivered);
        Assert.DoesNotContain(handler.Pushes, p => p.DeviceToken == TokenB);
        Assert.Equal(new OutboxCounts(0, 0, 1), await outbox.GetCountsAsync([CompanyB]));

        // Re-pairing A makes it deliverable again, which is why it was kept.
        await store.SaveAsync(new PairedCompanyRecord
        {
            CompanyUid = CompanyA,
            CompanyLabel = "Acme Corp",
            DeviceToken = TokenA,
            SyncKeyBase64 = KeyA,
        });

        Assert.Equal(1, await delivery.RetryAwaitingPushAsync([CompanyA, CompanyB], CancellationToken.None));
        Assert.Equal(TokenA, handler.Pushes[^1].DeviceToken);
    }

    [Fact]
    public async Task DeliverAsync_ItemQueuedByAnOlderBuild_FallsBackToTheActiveCompany()
    {
        var store = await NewStoreAsync(CompanyA, CompanyB);
        await store.SetActiveAsync(CompanyB);
        var storage = new InMemoryPendingScanStorage();
        var (delivery, outbox, handler) = Build(store, storage);

        // An older build wrote the image with no metadata alongside it.
        await storage.SaveAsync("legacy-queue-id", [9, 9, 9]);

        var queued = await outbox.PeekNextAsync([CompanyA, CompanyB]);
        Assert.NotNull(queued);
        Assert.Equal("legacy-queue-id", queued.Id);
        Assert.Null(queued.CompanyUid);

        var delivered = await delivery.DeliverAsync(
            ReviewedTransaction(), queued.Id, queued.CompanyUid, CancellationToken.None);

        Assert.True(delivered);
        Assert.Equal(TokenB, Assert.Single(handler.Pushes).DeviceToken);
        Assert.Equal(new OutboxCounts(0, 0, 0), await outbox.GetCountsAsync([CompanyA, CompanyB]));
    }
}
