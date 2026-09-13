using System.Text.Json;
using ArgoBooks.Core.Services.Sync;
using ArgoBooks.Shared.Sync;

namespace ArgoBooks.Shared.Mobile;

/// <summary>
/// Encrypts a confirmed <see cref="CapturedTransaction"/> with the active paired company's
/// sync key and pushes it onto the desktop's capture queue (<see cref="MobileSyncClient.PushCaptureAsync"/>),
/// mirroring what <see cref="SnapshotStore"/> does in reverse for downloading a snapshot. Pure logic
/// with no UI/device dependency beyond the injected <see cref="MobileSyncClient"/>/
/// <see cref="PairedCompanyStore"/> seams, so it is fully unit-testable (see
/// ArgoBooks.Tests/Mobile/CapturePushCoordinatorTests.cs).
/// </summary>
public class CapturePushCoordinator
{
    private readonly MobileSyncClient _client;
    private readonly PairedCompanyStore _store;

    public CapturePushCoordinator(MobileSyncClient client, PairedCompanyStore store)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Encrypts <paramref name="tx"/> with the active paired company's sync key and pushes it to
    /// the server queue. Returns false (without throwing) if there's no active paired company, or
    /// if the push fails for any reason (network error, non-success response, etc.) - the caller
    /// still gets to keep the confirmed scan locally either way.
    /// </summary>
    public Task<bool> PushAsync(CapturedTransaction tx, CancellationToken ct) =>
        PushAsync(tx, companyUid: null, ct);

    /// <summary>
    /// Same, but for a capture that belongs to a particular company: <paramref name="companyUid"/>
    /// is the company it was taken for, which is not necessarily the one active now (the user can
    /// switch, or unpair, between capturing a receipt offline and reviewing it). Returns false if
    /// that company is no longer paired - the capture is never redirected into another company's
    /// books, it stays queued until that company is back. A null <paramref name="companyUid"/> means
    /// no company was recorded (an item queued by an older build), and only then does this fall back
    /// to whichever company is active.
    /// </summary>
    public async Task<bool> PushAsync(CapturedTransaction tx, string? companyUid, CancellationToken ct)
    {
        if (tx == null) throw new ArgumentNullException(nameof(tx));

        var record = companyUid == null
            ? await _store.GetActiveAsync()
            : (await _store.GetAllAsync()).FirstOrDefault(c => c.CompanyUid == companyUid);

        if (record == null)
        {
            return false;
        }

        try
        {
            var cipher = SyncCrypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(tx), record.SyncKeyBase64);
            await _client.PushCaptureAsync(record.DeviceToken, cipher, ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
