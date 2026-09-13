using System.Text.Json;
using ArgoBooks.Core.Services.Sync;

namespace ArgoBooks.Shared.Mobile;

/// <summary>One cropped receipt still waiting to be reviewed: its stable queue <see cref="Id"/>
/// (reused as the transaction's ScanUid so a later push is idempotent), the cropped
/// <see cref="Image"/> bytes, and the <see cref="CompanyUid"/> that was active when it was captured
/// (null for an item queued by a build that did not record one).</summary>
public sealed record PendingScan(string Id, byte[] Image, string? CompanyUid);

/// <summary>A capture the user has already reviewed and confirmed, waiting only for a push to
/// succeed. The <see cref="Transaction"/> is the reviewed one, corrections and receipt image
/// included, so a retry never asks the user to review anything twice.</summary>
public sealed record PendingCapture(string Id, CapturedTransaction Transaction, string? CompanyUid);

/// <summary>How much is sitting in the outbox, split by what each item is waiting for.</summary>
/// <param name="AwaitingReview">Captured offline, still needs the scan -> review -> confirm pass.</param>
/// <param name="AwaitingPush">Already reviewed and confirmed, waiting to reach the desktop.</param>
/// <param name="Stranded">Bound to a company that is no longer paired, so neither can happen.</param>
public sealed record OutboxCounts(int AwaitingReview, int AwaitingPush, int Stranded);

/// <summary>
/// The phone's capture outbox: everything captured that has not yet reached the desktop, in one
/// queue keyed by an id that is also the transaction's ScanUid (the desktop de-duplicates on it, so
/// re-sending is always safe).
///
/// An entry is in one of two states. Captured with no network, it holds the cropped image and waits
/// for review: once connectivity returns the Capture screen surfaces a "N receipts ready to review"
/// prompt and the user walks each one through the SAME scan -> review -> confirm flow as an online
/// capture (see ShellViewModel.StartOfflineReviewAsync), so nothing is ever posted to the books
/// unreviewed. Confirmed but not delivered - the push failed, or it was an online capture whose push
/// failed at confirm time - it holds the reviewed <see cref="CapturedTransaction"/> instead, and
/// <see cref="CaptureDeliveryCoordinator.RetryAwaitingPushAsync"/> re-sends it in the background
/// with no further user action.
///
/// Every entry records the company it was captured for, and is only ever delivered to that company
/// (see <see cref="CapturePushCoordinator"/>): switching or unpairing companies between capture and
/// delivery must not redirect a receipt into someone else's books. An entry whose company is no
/// longer paired is kept, not deleted or redirected - re-pairing that company makes it deliverable
/// again - and is reported as <see cref="OutboxCounts.Stranded"/> so the Capture screen can say so
/// rather than looping the user through a review that cannot be sent.
///
/// Pure logic behind the <see cref="IPendingScanStorage"/> seam, so it's unit-tested with an
/// in-memory fake (see PendingScanOutboxTests) rather than real device file storage
/// (<see cref="FilePendingScanStorage"/> is the real implementation).
/// </summary>
public class PendingScanOutbox
{
    private readonly IPendingScanStorage _storage;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The JSON sidecar stored next to a queued capture. An older build wrote none, so a
    /// missing sidecar reads as "no company recorded, not reviewed yet".</summary>
    private sealed class Metadata
    {
        public string? CompanyUid { get; set; }
        public CapturedTransaction? Transaction { get; set; }
    }

    public PendingScanOutbox(IPendingScanStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>Queues a cropped image for a later scan, bound to <paramref name="companyUid"/>, and
    /// returns its queue id. Throws if the image is empty - callers should only enqueue a real
    /// ML-Kit-cropped image.</summary>
    public async Task<string> EnqueueAsync(byte[] imageBytes, string? companyUid = null)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Cannot queue an empty image.", nameof(imageBytes));
        }

        var id = Guid.NewGuid().ToString("N");
        await _storage.SaveAsync(id, imageBytes);
        await WriteMetadataAsync(id, new Metadata { CompanyUid = companyUid });
        return id;
    }

    /// <summary>
    /// Records a confirmed review so a failed push can be retried without the user touching it
    /// again. Works for both an online capture (nothing was queued before, so this creates the
    /// entry) and one that came from the queue (the image entry is upgraded in place, and the
    /// reviewed transaction - which carries the receipt image itself - replaces it as the thing to
    /// send). The id is the transaction's ScanUid, so every retry re-sends the same idempotency key.
    /// </summary>
    public async Task SaveReviewedAsync(string id, CapturedTransaction transaction, string? companyUid)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Queue id cannot be empty.", nameof(id));
        if (transaction == null) throw new ArgumentNullException(nameof(transaction));

        await WriteMetadataAsync(id, new Metadata { CompanyUid = companyUid, Transaction = transaction });
    }

    /// <summary>
    /// What the outbox is holding, split by state. <paramref name="pairedCompanyUids"/> is every
    /// currently paired company; pass null when it isn't known, and nothing is counted as stranded.
    /// </summary>
    public async Task<OutboxCounts> GetCountsAsync(IReadOnlyCollection<string>? pairedCompanyUids = null)
    {
        var awaitingReview = 0;
        var awaitingPush = 0;
        var stranded = 0;

        foreach (var id in await _storage.ListIdsAsync())
        {
            var metadata = await ReadMetadataAsync(id);
            if (IsStranded(metadata?.CompanyUid, pairedCompanyUids))
            {
                stranded++;
            }
            else if (metadata?.Transaction != null)
            {
                awaitingPush++;
            }
            else
            {
                awaitingReview++;
            }
        }

        return new OutboxCounts(awaitingReview, awaitingPush, stranded);
    }

    /// <summary>
    /// Returns the next capture still waiting to be reviewed (its stable id, cropped bytes, and the
    /// company it was taken for) for the review flow to scan, or null when there is none. Entries
    /// already reviewed are skipped - those only need a push retry - as are entries bound to a
    /// company that is no longer paired, so the user is never walked through a review that could not
    /// be sent anywhere. Stale entries whose bytes and metadata have both gone missing are dropped
    /// in passing. The item is NOT removed here - the caller removes it via <see cref="RemoveAsync"/>
    /// only once the user has confirmed the review and it has pushed, so a user who backs out (or a
    /// failed push) leaves the receipt queued for another attempt.
    /// </summary>
    public async Task<PendingScan?> PeekNextAsync(IReadOnlyCollection<string>? pairedCompanyUids = null)
    {
        foreach (var id in await _storage.ListIdsAsync())
        {
            var metadata = await ReadMetadataAsync(id);
            if (metadata?.Transaction != null || IsStranded(metadata?.CompanyUid, pairedCompanyUids))
            {
                continue;
            }

            var imageBytes = await _storage.LoadAsync(id);
            if (imageBytes == null || imageBytes.Length == 0)
            {
                // Nothing usable under this id - drop the stale entry and look at the next.
                await _storage.DeleteAsync(id);
                continue;
            }

            return new PendingScan(id, imageBytes, metadata?.CompanyUid);
        }

        return null;
    }

    /// <summary>
    /// Every reviewed capture still waiting to reach the desktop, for the background retry. Entries
    /// bound to a company that is no longer paired are left out: they stay queued rather than being
    /// delivered somewhere they don't belong.
    /// </summary>
    public async Task<IReadOnlyList<PendingCapture>> GetAwaitingPushAsync(IReadOnlyCollection<string>? pairedCompanyUids = null)
    {
        var pending = new List<PendingCapture>();

        foreach (var id in await _storage.ListIdsAsync())
        {
            var metadata = await ReadMetadataAsync(id);
            if (metadata?.Transaction == null || IsStranded(metadata.CompanyUid, pairedCompanyUids))
            {
                continue;
            }

            pending.Add(new PendingCapture(id, metadata.Transaction, metadata.CompanyUid));
        }

        return pending;
    }

    /// <summary>Drops a queued capture once it has reached the desktop. Idempotent - deleting an id
    /// that's already gone is a no-op.</summary>
    public Task RemoveAsync(string id) => _storage.DeleteAsync(id);

    /// <summary>An entry recorded for a company that is no longer paired. An entry with no company
    /// (queued by an older build) is never stranded - there is nothing to strand it against.</summary>
    private static bool IsStranded(string? companyUid, IReadOnlyCollection<string>? pairedCompanyUids) =>
        companyUid != null && pairedCompanyUids != null && !pairedCompanyUids.Contains(companyUid);

    private Task WriteMetadataAsync(string id, Metadata metadata) =>
        _storage.SaveMetadataAsync(id, JsonSerializer.Serialize(metadata, JsonOptions));

    private async Task<Metadata?> ReadMetadataAsync(string id)
    {
        var json = await _storage.LoadMetadataAsync(id);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Metadata>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // A corrupt sidecar must not strand the image: treat it as an unreviewed, uncompanied
            // entry so the user can still review and send it.
            return null;
        }
    }
}
