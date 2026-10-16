using System.Security.Cryptography;
using System.Text.Json;
using ArgoBooks.Core.Security;

namespace ArgoBooks.Core.Services.Sync;

/// <summary>
/// Crypto for the E2E mobile-sync payloads. The sync key is a raw 32-byte AES key (base64).
/// A payload is base64( salt(32) || iv(12) || ciphertext+tag ), so the phone can decrypt with the same key.
/// </summary>
public static class SyncCrypto
{
    private static readonly EncryptionService Enc = new();

    public static string GenerateCompanyUid()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(); // 48 hex chars

    public static string GenerateSyncKey()
        => Convert.ToBase64String(KeyEnvelope.GenerateDataKey());

    public static string Encrypt(byte[] plaintext, string syncKeyBase64)
    {
        var salt = Enc.GenerateSalt();  // base64
        var iv = Enc.GenerateIv();      // base64
        var cipher = Enc.Encrypt(plaintext, syncKeyBase64, salt, iv);
        var saltBytes = Convert.FromBase64String(salt);
        var ivBytes = Convert.FromBase64String(iv);
        var combined = new byte[saltBytes.Length + ivBytes.Length + cipher.Length];
        Buffer.BlockCopy(saltBytes, 0, combined, 0, saltBytes.Length);
        Buffer.BlockCopy(ivBytes, 0, combined, saltBytes.Length, ivBytes.Length);
        Buffer.BlockCopy(cipher, 0, combined, saltBytes.Length + ivBytes.Length, cipher.Length);
        return Convert.ToBase64String(combined);
    }

    public static byte[] Decrypt(string payloadBase64, string syncKeyBase64)
    {
        var combined = Convert.FromBase64String(payloadBase64);
        const int ivEnd = KeyDerivation.SaltSize + KeyDerivation.IvSize;
        var salt = Convert.ToBase64String(combined[..KeyDerivation.SaltSize]);
        var iv = Convert.ToBase64String(combined[KeyDerivation.SaltSize..ivEnd]);
        var cipher = combined[ivEnd..];
        return Enc.Decrypt(cipher, syncKeyBase64, salt, iv);
    }

    public static string BuildQrPayload(string pairingToken, string companyUid, string companyLabel, string syncKeyBase64)
        => JsonSerializer.Serialize(new { t = pairingToken, u = companyUid, l = companyLabel, k = syncKeyBase64 });
}
