namespace ArgoBooks.Shared.Mobile;

/// <summary>
/// Storage seam for <see cref="PendingScanOutbox"/>'s queued captures - the cropped image while one
/// is still waiting to be reviewed, plus a small JSON sidecar holding which company it belongs to
/// and, once reviewed, the confirmed transaction - so the outbox's enqueue/drain logic can be
/// unit-tested with an in-memory fake instead of real device storage.
/// <see cref="FilePendingScanStorage"/> is the real implementation, mirroring the
/// ISnapshotCache/FileSnapshotCache split already used for the snapshot cache.
/// </summary>
public interface IPendingScanStorage
{
    /// <summary>Every queued id, in no particular guaranteed order - including ids that only have
    /// metadata (a reviewed capture waiting to be sent carries no separate image; its receipt is
    /// inside the stored transaction).</summary>
    Task<IReadOnlyList<string>> ListIdsAsync();

    Task SaveAsync(string id, byte[] imageBytes);

    /// <summary>The queued image's bytes, or null if it doesn't exist (already drained, never
    /// written, or a metadata-only entry).</summary>
    Task<byte[]?> LoadAsync(string id);

    /// <summary>Stores the small JSON sidecar holding which company the capture was taken for and,
    /// once reviewed, the confirmed transaction itself.</summary>
    Task SaveMetadataAsync(string id, string json);

    /// <summary>The entry's JSON sidecar, or null when it has none - which is how an item queued by
    /// an older build looks.</summary>
    Task<string?> LoadMetadataAsync(string id);

    /// <summary>Drops the whole entry: image and metadata.</summary>
    Task DeleteAsync(string id);
}
