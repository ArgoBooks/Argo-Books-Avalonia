using System.Security.Cryptography;
using ArgoBooks.Shared.Sync;
using Xunit;

namespace ArgoBooks.Tests.Mobile;

/// <summary>
/// Unit tests for PairingKeyExchange / PairingKeyPair: the RSA-OAEP key exchange used to hand
/// the 32-byte company sync key from the desktop to a phone during short-code pairing.
/// </summary>
public class PairingKeyExchangeTests
{
    [Fact]
    public void EncryptThenDecrypt_WithMatchingKeyPair_ReturnsOriginalBytes()
    {
        using var keyPair = PairingKeyExchange.GenerateKeyPair();
        var syncKey = RandomNumberGenerator.GetBytes(32);

        var ciphertextBase64 = PairingKeyExchange.EncryptSyncKey(keyPair.PublicKeyBase64, syncKey);
        var decrypted = keyPair.DecryptSyncKey(ciphertextBase64);

        Assert.Equal(syncKey, decrypted);
    }

    [Fact]
    public void Decrypt_WithDifferentKeyPair_Throws()
    {
        using var keyPair = PairingKeyExchange.GenerateKeyPair();
        using var otherKeyPair = PairingKeyExchange.GenerateKeyPair();
        var syncKey = RandomNumberGenerator.GetBytes(32);

        var ciphertextBase64 = PairingKeyExchange.EncryptSyncKey(keyPair.PublicKeyBase64, syncKey);

        // ThrowsAny, not Throws: the point is that the wrong key cannot decrypt, and the platform decides which CryptographicException says so.
        Assert.ThrowsAny<CryptographicException>(() => otherKeyPair.DecryptSyncKey(ciphertextBase64));
    }

    [Fact]
    public void GenerateKeyPair_ProducesDistinctPublicKeysEachCall()
    {
        using var first = PairingKeyExchange.GenerateKeyPair();
        using var second = PairingKeyExchange.GenerateKeyPair();

        Assert.NotEqual(first.PublicKeyBase64, second.PublicKeyBase64);
    }

    [Fact]
    public void PublicKeyBase64_IsValidBase64SubjectPublicKeyInfo()
    {
        using var keyPair = PairingKeyExchange.GenerateKeyPair();

        var bytes = Convert.FromBase64String(keyPair.PublicKeyBase64);
        using var rsa = RSA.Create();

        // Should not throw: confirms it's a well-formed SubjectPublicKeyInfo DER blob.
        rsa.ImportSubjectPublicKeyInfo(bytes, out _);
    }
}
