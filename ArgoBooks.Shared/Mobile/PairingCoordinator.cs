using System.Text.Json;
using ArgoBooks.Core.Services;
using ArgoBooks.Shared.Sync;

namespace ArgoBooks.Shared.Mobile;

/// <summary>
/// Result of a pairing attempt (QR payload or pasted pairing data -> redeemed device token
/// -> saved paired-company record).
/// </summary>
public class PairingOutcome
{
    /// <summary>True if the payload was parsed, the pairing token was redeemed, and the
    /// resulting company was saved + set active.</summary>
    public bool Success { get; init; }

    /// <summary>The paired company's display label, set only on success.</summary>
    public string? CompanyLabel { get; init; }

    /// <summary>A user-facing error message, set only on failure.</summary>
    public string? Error { get; init; }

    public static PairingOutcome Ok(string companyLabel) => new() { Success = true, CompanyLabel = companyLabel };

    public static PairingOutcome Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Drives the pairing flow: parses the QR payload JSON, redeems the pairing token against the
/// sync server, and persists the resulting paired company. Pure logic with no UI/device
/// dependencies, so it's fully unit-testable via a fake MobileSyncClient handler and an
/// in-memory ISecureStore.
/// </summary>
public class PairingCoordinator
{
    private readonly MobileSyncClient _client;
    private readonly PairedCompanyStore _store;

    public PairingCoordinator(MobileSyncClient client, PairedCompanyStore store)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Interval between polls of <c>/pair/key</c> while waiting for the desktop to approve a
    /// short-code claim. Mutable so tests can drive the poll loop quickly instead of waiting on
    /// the real-world default.
    /// </summary>
    public TimeSpan PollInterval { get; internal set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Total time to keep polling before giving up and reporting a timeout.
    /// </summary>
    public TimeSpan PollTimeout { get; internal set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Pairs this device using a QR payload (or a manually-pasted copy of the same JSON), in the
    /// shape produced by <see cref="SyncCrypto.BuildQrPayload"/>: {"t":pairingToken,"u":companyUid,
    /// "l":companyLabel,"k":syncKeyBase64}.
    /// </summary>
    public async Task<PairingOutcome> PairFromPayloadAsync(string qrPayloadJson, string deviceLabel, CancellationToken ct = default)
    {
        string pairingToken, companyUid, companyLabel, syncKeyBase64;

        try
        {
            using var doc = JsonDocument.Parse(qrPayloadJson);
            var root = doc.RootElement;

            pairingToken = root.TryGetProperty("t", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;
            companyUid = root.TryGetProperty("u", out var u) ? (u.GetString() ?? string.Empty) : string.Empty;
            companyLabel = root.TryGetProperty("l", out var l) ? (l.GetString() ?? string.Empty) : string.Empty;
            syncKeyBase64 = root.TryGetProperty("k", out var k) ? (k.GetString() ?? string.Empty) : string.Empty;
        }
        catch (JsonException)
        {
            return PairingOutcome.Fail("That doesn't look like a valid pairing code. Scan the QR code from the desktop's sync settings again.");
        }
        catch (InvalidOperationException)
        {
            // JsonDocument.Parse can succeed on valid-but-non-object JSON (e.g. a bare string or number),
            // in which case TryGetProperty throws InvalidOperationException instead of returning false.
            return PairingOutcome.Fail("That doesn't look like a valid pairing code. Scan the QR code from the desktop's sync settings again.");
        }

        if (string.IsNullOrWhiteSpace(pairingToken) || string.IsNullOrWhiteSpace(companyUid) || string.IsNullOrWhiteSpace(syncKeyBase64))
        {
            return PairingOutcome.Fail("That pairing code is missing required data. Scan the QR code from the desktop's sync settings again.");
        }

        PairResult? result;
        try
        {
            result = await _client.RedeemPairingAsync(pairingToken, deviceLabel, ct);
        }
        catch (Exception)
        {
            // Covers non-2xx responses (e.g. 410 Gone for an expired/already-used pairing token)
            // and network failures alike - all surface as EnsureSuccessStatusCode throwing.
            return PairingOutcome.Fail("This pairing code has expired or the server couldn't be reached. Generate a new QR code on the desktop and try again.");
        }

        if (result == null || string.IsNullOrWhiteSpace(result.DeviceToken))
        {
            return PairingOutcome.Fail("This pairing code has expired. Generate a new QR code on the desktop and try again.");
        }

        var record = new PairedCompanyRecord
        {
            CompanyUid = companyUid,
            CompanyLabel = companyLabel,
            DeviceToken = result.DeviceToken,
            SyncKeyBase64 = syncKeyBase64
        };

        await _store.SaveAsync(record);
        await _store.SetActiveAsync(record.CompanyUid);

        return PairingOutcome.Ok(companyLabel);
    }

    /// <summary>
    /// Pairs this device using a human-typed short pairing code shown on the desktop's sync
    /// settings screen. Unlike the QR path, the sync key never travels in plaintext: the phone
    /// generates a throwaway RSA keypair, sends only the public key when claiming the code, then
    /// polls until the desktop has encrypted the sync key to that public key and the server makes
    /// it available.
    /// </summary>
    public async Task<PairingOutcome> PairFromCodeAsync(string code, string deviceLabel, CancellationToken ct = default)
    {
        var normalizedCode = PairingCode.Normalize(code ?? string.Empty);
        if (string.IsNullOrEmpty(normalizedCode))
        {
            return PairingOutcome.Fail("Enter the code shown on your computer.");
        }

        using var keyPair = PairingKeyExchange.GenerateKeyPair();

        ClaimResult? claim;
        try
        {
            claim = await _client.ClaimPairingAsync(normalizedCode, keyPair.PublicKeyBase64, deviceLabel, ct);
        }
        catch (ServerRateLimitedException ex)
        {
            // The code may be fine; this connection has just tried too many.
            return PairingOutcome.Fail(ex.Message);
        }
        catch (Exception)
        {
            // Covers non-2xx responses and network failures alike.
            return PairingOutcome.Fail("That code is not valid or has expired. Generate a new one on your computer.");
        }

        if (claim == null || string.IsNullOrWhiteSpace(claim.DeviceToken))
        {
            return PairingOutcome.Fail("That code is not valid or has expired. Generate a new one on your computer.");
        }

        var deadline = DateTime.UtcNow + PollTimeout;
        string? ciphertext = null;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                ciphertext = await _client.FetchPairingKeyAsync(claim.DeviceToken, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                // Transient network failure mid-poll (e.g. phone switching WiFi/cellular during the
                // up-to-2-minute wait). Don't abort pairing - keep polling until the deadline and
                // fall back to the same timeout message as a key that never arrives.
                ciphertext = null;
            }

            if (!string.IsNullOrEmpty(ciphertext))
            {
                break;
            }

            await Task.Delay(PollInterval, ct);
        }

        if (string.IsNullOrEmpty(ciphertext))
        {
            return PairingOutcome.Fail("Make sure Argo Books is open on your computer and the pairing screen is showing, then try again.");
        }

        byte[] syncKeyBytes;
        try
        {
            syncKeyBytes = keyPair.DecryptSyncKey(ciphertext);
        }
        catch (Exception)
        {
            // Covers FormatException (malformed base64) and CryptographicException (corrupt or
            // foreign ciphertext) alike.
            return PairingOutcome.Fail("Something went wrong finishing the connection. Please try pairing again.");
        }

        var record = new PairedCompanyRecord
        {
            CompanyUid = claim.CompanyUid,
            CompanyLabel = claim.CompanyLabel,
            DeviceToken = claim.DeviceToken,
            SyncKeyBase64 = Convert.ToBase64String(syncKeyBytes)
        };

        await _store.SaveAsync(record);
        await _store.SetActiveAsync(record.CompanyUid);

        return PairingOutcome.Ok(claim.CompanyLabel);
    }
}
