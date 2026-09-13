using ArgoBooks.Core.Services.Sync;

namespace ArgoBooks.Shared.Mobile;

/// <summary>
/// Gets a confirmed capture to the desktop, whenever that turns out to be possible. Sits between the
/// review screen and <see cref="CapturePushCoordinator"/>: a push that works ends the story, a push
/// that doesn't (signal lost while the user was editing the review, server error, no active paired
/// company) parks the reviewed transaction in <see cref="PendingScanOutbox"/> instead of dropping
/// it, and <see cref="RetryAwaitingPushAsync"/> re-sends it later with no further user action. The
/// queue id is the transaction's ScanUid throughout, so a retry after a lost response re-sends the
/// same idempotency key and the desktop de-duplicates rather than booking the receipt twice.
/// Pure logic over the outbox and push seams, so it is unit-tested end to end (see
/// ArgoBooks.Tests/Mobile/CaptureDeliveryCoordinatorTests.cs).
/// </summary>
public class CaptureDeliveryCoordinator
{
    private readonly CapturePushCoordinator _push;
    private readonly PendingScanOutbox _outbox;

    public CaptureDeliveryCoordinator(CapturePushCoordinator push, PendingScanOutbox outbox)
    {
        _push = push ?? throw new ArgumentNullException(nameof(push));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    /// <summary>
    /// "Add to my books": sends <paramref name="transaction"/> to <paramref name="companyUid"/> now,
    /// or keeps it for a later attempt. <paramref name="queueId"/> is the outbox id when the capture
    /// came from the offline queue, null for one taken online. Returns whether it reached the
    /// desktop; false means it is safely queued, not lost.
    /// </summary>
    public async Task<bool> DeliverAsync(CapturedTransaction transaction, string? queueId, string? companyUid, CancellationToken ct)
    {
        if (transaction == null) throw new ArgumentNullException(nameof(transaction));

        var id = queueId ?? transaction.ScanUid;
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString("N");
        }

        transaction.ScanUid = id;

        if (await _push.PushAsync(transaction, companyUid, ct))
        {
            await _outbox.RemoveAsync(id);
            return true;
        }

        await _outbox.SaveReviewedAsync(id, transaction, companyUid);
        return false;
    }

    /// <summary>
    /// Re-sends every capture the user has already confirmed but that has not reached the desktop.
    /// Driven from the same refresh the offline-review prompt uses (snapshot refresh, app
    /// foreground, opening the Capture tab), so a capture confirmed with no signal goes out on its
    /// own once there is one. Items bound to a company that is no longer paired are skipped and stay
    /// queued rather than being delivered into another company's books. Returns how many were sent.
    /// </summary>
    public async Task<int> RetryAwaitingPushAsync(IReadOnlyCollection<string>? pairedCompanyUids, CancellationToken ct)
    {
        var delivered = 0;

        foreach (var pending in await _outbox.GetAwaitingPushAsync(pairedCompanyUids))
        {
            if (await _push.PushAsync(pending.Transaction, pending.CompanyUid, ct))
            {
                await _outbox.RemoveAsync(pending.Id);
                delivered++;
            }
        }

        return delivered;
    }
}
