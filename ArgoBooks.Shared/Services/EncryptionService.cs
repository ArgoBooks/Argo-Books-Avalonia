using System.Security.Cryptography;
using ArgoBooks.Core.Security;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Implementation of AES-256-GCM encryption service.
/// </summary>
public class EncryptionService : IEncryptionService
{
    #region Key Generation

    /// <inheritdoc />
    public string GenerateSalt()
    {
        return KeyDerivation.GenerateSaltBase64();
    }

    /// <inheritdoc />
    public string GenerateIv()
    {
        return KeyDerivation.GenerateIvBase64();
    }

    #endregion

    #region Encryption (Byte Arrays)

    /// <inheritdoc />
    public byte[] Encrypt(byte[] data, string password, string salt, string iv)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(salt);
        ArgumentException.ThrowIfNullOrEmpty(iv);

        // Derive encryption key from password
        var key = KeyDerivation.DeriveKey(password, salt);
        try
        {
            return EncryptWithKey(data, key, Convert.FromBase64String(iv));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <inheritdoc />
    public byte[] EncryptWithKey(byte[] data, byte[] key, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(nonce);

        // Allocate space for ciphertext + tag
        var ciphertext = new byte[data.Length];
        var tag = new byte[KeyDerivation.TagSize];

        // Encrypt using AES-GCM
        using var aesGcm = new AesGcm(key, KeyDerivation.TagSize);
        aesGcm.Encrypt(nonce, data, ciphertext, tag);

        // Combine ciphertext and tag
        var result = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, ciphertext.Length, tag.Length);

        return result;
    }

    /// <inheritdoc />
    public byte[] Decrypt(byte[] encryptedData, string password, string salt, string iv)
    {
        ArgumentNullException.ThrowIfNull(encryptedData);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(salt);
        ArgumentException.ThrowIfNullOrEmpty(iv);

        if (encryptedData.Length < KeyDerivation.TagSize)
            throw new CryptographicException("Invalid encrypted data.");

        // Derive encryption key from password
        var key = KeyDerivation.DeriveKey(password, salt);
        try
        {
            return DecryptWithKey(encryptedData, key, Convert.FromBase64String(iv));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <inheritdoc />
    public byte[] DecryptWithKey(byte[] encryptedData, byte[] key, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(encryptedData);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(nonce);

        if (encryptedData.Length < KeyDerivation.TagSize)
            throw new CryptographicException("Invalid encrypted data.");

        // Split ciphertext and tag
        var ciphertextLength = encryptedData.Length - KeyDerivation.TagSize;
        var ciphertext = new byte[ciphertextLength];
        var tag = new byte[KeyDerivation.TagSize];

        Buffer.BlockCopy(encryptedData, 0, ciphertext, 0, ciphertextLength);
        Buffer.BlockCopy(encryptedData, ciphertextLength, tag, 0, KeyDerivation.TagSize);

        // Decrypt using AES-GCM
        var plaintext = new byte[ciphertextLength];
        using var aesGcm = new AesGcm(key, KeyDerivation.TagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    #endregion

    #region Encryption (Streams)

    /// <inheritdoc />
    public byte[] DecryptWithVerification(
        byte[] encryptedData, string password, string salt, string iv, string expectedPasswordHash)
    {
        ArgumentNullException.ThrowIfNull(encryptedData);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(salt);
        ArgumentException.ThrowIfNullOrEmpty(iv);
        ArgumentException.ThrowIfNullOrEmpty(expectedPasswordHash);

        if (encryptedData.Length < KeyDerivation.TagSize)
            throw new CryptographicException("Invalid encrypted data.");

        var saltBytes = Convert.FromBase64String(salt);

        // Single PBKDF2 pass yields both the AES key and the verification hash.
        KeyDerivation.DeriveKeyAndHash(password, saltBytes, out var key, out var hash);
        try
        {
            var expected = Convert.FromBase64String(expectedPasswordHash);
            if (!CryptographicOperations.FixedTimeEquals(hash, expected))
                throw new UnauthorizedAccessException("Invalid password.");

            var nonce = Convert.FromBase64String(iv);

            // Split ciphertext and tag
            var ciphertextLength = encryptedData.Length - KeyDerivation.TagSize;
            var ciphertext = new byte[ciphertextLength];
            var tag = new byte[KeyDerivation.TagSize];

            Buffer.BlockCopy(encryptedData, 0, ciphertext, 0, ciphertextLength);
            Buffer.BlockCopy(encryptedData, ciphertextLength, tag, 0, KeyDerivation.TagSize);

            // Decrypt using AES-GCM
            var plaintext = new byte[ciphertextLength];
            using var aesGcm = new AesGcm(key, KeyDerivation.TagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    /// <inheritdoc />
    public async Task<MemoryStream> DecryptWithVerificationAsync(
        Stream encryptedStream, string password, string salt, string iv, string expectedPasswordHash)
    {
        ArgumentNullException.ThrowIfNull(encryptedStream);

        // Read all data from encrypted stream
        byte[] encryptedData;
        if (encryptedStream is MemoryStream ms)
        {
            encryptedData = ms.ToArray();
        }
        else
        {
            using var memStream = new MemoryStream();
            await encryptedStream.CopyToAsync(memStream);
            encryptedData = memStream.ToArray();
        }

        var decryptedData = DecryptWithVerification(encryptedData, password, salt, iv, expectedPasswordHash);

        // Return as memory stream
        var result = new MemoryStream(decryptedData);
        result.Position = 0;
        return result;
    }

    #endregion
}
