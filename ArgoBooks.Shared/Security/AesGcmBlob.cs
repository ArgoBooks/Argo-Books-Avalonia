using System.Security.Cryptography;

namespace ArgoBooks.Core.Security;

/// <summary>
/// AES-256-GCM in the layout every encrypted blob in a company file uses: the ciphertext
/// followed by its 16-byte authentication tag.
/// </summary>
internal static class AesGcmBlob
{
    public static byte[] Encrypt(byte[] plaintext, byte[] key, byte[] nonce)
    {
        var blob = new byte[plaintext.Length + KeyDerivation.TagSize];
        using var aesGcm = new AesGcm(key, KeyDerivation.TagSize);
        aesGcm.Encrypt(nonce, plaintext, blob.AsSpan(0, plaintext.Length), blob.AsSpan(plaintext.Length));
        return blob;
    }

    /// <summary>Callers check the blob is at least as long as the tag, since each has its own rule about empty plaintext.</summary>
    public static byte[] Decrypt(byte[] blob, byte[] key, byte[] nonce)
    {
        var ciphertextLength = blob.Length - KeyDerivation.TagSize;
        var plaintext = new byte[ciphertextLength];
        using var aesGcm = new AesGcm(key, KeyDerivation.TagSize);
        aesGcm.Decrypt(nonce, blob.AsSpan(0, ciphertextLength), blob.AsSpan(ciphertextLength), plaintext);
        return plaintext;
    }
}
