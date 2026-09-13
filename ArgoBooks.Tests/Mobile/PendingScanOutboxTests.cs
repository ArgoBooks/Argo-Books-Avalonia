using ArgoBooks.Core.Services.Sync;
using ArgoBooks.Shared.Mobile;
using Xunit;

namespace ArgoBooks.Tests.Mobile;

/// <summary>
/// Unit tests for PendingScanOutbox (the phone's capture outbox), using an in-memory
/// IPendingScanStorage fake rather than real device file storage.
/// </summary>
public class PendingScanOutboxTests
{
    private sealed class InMemoryPendingScanStorage : IPendingScanStorage
    {
        private readonly Dictionary<string, byte[]> _files = new();
        private readonly Dictionary<string, string> _metadata = new();

        public Task<IReadOnlyList<string>> ListIdsAsync() =>
            Task.FromResult<IReadOnlyList<string>>(_files.Keys.Concat(_metadata.Keys).Distinct().ToList());

        public Task SaveAsync(string id, byte[] imageBytes)
        {
            _files[id] = imageBytes;
            return Task.CompletedTask;
        }

        public Task<byte[]?> LoadAsync(string id)
        {
            _files.TryGetValue(id, out var bytes);
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
            _files.Remove(id);
            _metadata.Remove(id);
            return Task.CompletedTask;
        }
    }

    private const string CompanyA = "company-a";
    private const string CompanyB = "company-b";

    private static CapturedTransaction NewTransaction() => new()
    {
        Type = CapturedTransactionType.Expense,
        SupplierOrCustomer = "Office Depot",
        Total = 12.50m,
        ScanUid = "scan-uid-1",
    };

    private static async Task<int> AwaitingReviewAsync(PendingScanOutbox outbox) =>
        (await outbox.GetCountsAsync()).AwaitingReview;

    [Fact]
    public async Task EnqueueAsync_IncreasesPendingCount()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());

        await outbox.EnqueueAsync([1, 2, 3]);
        await outbox.EnqueueAsync([4, 5, 6]);

        Assert.Equal(2, await AwaitingReviewAsync(outbox));
    }

    [Fact]
    public async Task EnqueueAsync_EmptyImage_Throws()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());

        await Assert.ThrowsAsync<ArgumentException>(() => outbox.EnqueueAsync([]));
    }

    [Fact]
    public async Task PeekNextAsync_ReturnsAQueuedImageWithItsBytes()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());
        await outbox.EnqueueAsync([1, 2, 3]);

        var next = await outbox.PeekNextAsync();

        Assert.NotNull(next);
        Assert.Equal([1, 2, 3], next.Image);
        Assert.False(string.IsNullOrEmpty(next.Id));
    }

    [Fact]
    public async Task PeekNextAsync_EmptyQueue_ReturnsNull()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());

        Assert.Null(await outbox.PeekNextAsync());
    }

    [Fact]
    public async Task PeekNextAsync_DoesNotRemove_SoRepeatedPeeksSeeTheSameItem()
    {
        // Peek must not consume: a user who backs out of review (or a failed push) has to leave the
        // receipt queued. Removal is explicit, via RemoveAsync only after a confirmed+pushed review.
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());
        await outbox.EnqueueAsync([7]);

        var first = await outbox.PeekNextAsync();
        var second = await outbox.PeekNextAsync();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await AwaitingReviewAsync(outbox));
    }

    [Fact]
    public async Task RemoveAsync_DropsTheReviewedImage()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());
        await outbox.EnqueueAsync([7]);
        var item = await outbox.PeekNextAsync();

        await outbox.RemoveAsync(item!.Id);

        Assert.Equal(0, await AwaitingReviewAsync(outbox));
        Assert.Null(await outbox.PeekNextAsync());
    }

    [Fact]
    public async Task RemoveAsync_UnknownId_IsANoOp()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());
        await outbox.EnqueueAsync([7]);

        await outbox.RemoveAsync("does-not-exist");

        Assert.Equal(1, await AwaitingReviewAsync(outbox));
    }

    [Fact]
    public async Task GetCountsAsync_Empty_ReturnsZeroes()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());

        Assert.Equal(new OutboxCounts(0, 0, 0), await outbox.GetCountsAsync([CompanyA]));
    }

    [Fact]
    public async Task EnqueueAsync_RecordsTheCompanyTheCaptureWasTakenFor()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());
        await outbox.EnqueueAsync([1, 2, 3], CompanyA);

        var next = await outbox.PeekNextAsync([CompanyA, CompanyB]);

        Assert.NotNull(next);
        Assert.Equal(CompanyA, next.CompanyUid);
    }

    [Fact]
    public async Task PeekNextAsync_ItemQueuedByAnOlderBuild_IsStillReviewableWithNoCompany()
    {
        // An older build wrote the image with no metadata beside it.
        var storage = new InMemoryPendingScanStorage();
        await storage.SaveAsync("legacy-id", [4, 5, 6]);
        var outbox = new PendingScanOutbox(storage);

        var next = await outbox.PeekNextAsync([CompanyA]);

        Assert.NotNull(next);
        Assert.Equal("legacy-id", next.Id);
        Assert.Null(next.CompanyUid);
        Assert.Equal(new OutboxCounts(1, 0, 0), await outbox.GetCountsAsync([CompanyA]));
    }

    [Fact]
    public async Task PeekNextAsync_SkipsAnItemWhoseCompanyIsNoLongerPaired()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());
        await outbox.EnqueueAsync([1], CompanyA);

        Assert.Null(await outbox.PeekNextAsync([CompanyB]));
        Assert.Equal(new OutboxCounts(0, 0, 1), await outbox.GetCountsAsync([CompanyB]));

        // Still there, and reviewable again once that company is back.
        Assert.NotNull(await outbox.PeekNextAsync([CompanyA, CompanyB]));
    }

    [Fact]
    public async Task SaveReviewedAsync_MovesTheItemFromAwaitingReviewToAwaitingPush()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());
        var id = await outbox.EnqueueAsync([1, 2, 3], CompanyA);

        await outbox.SaveReviewedAsync(id, NewTransaction(), CompanyA);

        Assert.Equal(new OutboxCounts(0, 1, 0), await outbox.GetCountsAsync([CompanyA]));
        Assert.Null(await outbox.PeekNextAsync([CompanyA]));

        var pending = Assert.Single(await outbox.GetAwaitingPushAsync([CompanyA]));
        Assert.Equal(id, pending.Id);
        Assert.Equal(CompanyA, pending.CompanyUid);
        Assert.Equal("Office Depot", pending.Transaction.SupplierOrCustomer);
        Assert.Equal(12.50m, pending.Transaction.Total);
    }

    [Fact]
    public async Task GetAwaitingPushAsync_LeavesOutItemsWhoseCompanyIsNoLongerPaired()
    {
        var outbox = new PendingScanOutbox(new InMemoryPendingScanStorage());
        await outbox.SaveReviewedAsync("reviewed-a", NewTransaction(), CompanyA);
        await outbox.SaveReviewedAsync("reviewed-b", NewTransaction(), CompanyB);

        var pending = Assert.Single(await outbox.GetAwaitingPushAsync([CompanyB]));

        Assert.Equal("reviewed-b", pending.Id);
        Assert.Equal(new OutboxCounts(0, 1, 1), await outbox.GetCountsAsync([CompanyB]));
    }
}
