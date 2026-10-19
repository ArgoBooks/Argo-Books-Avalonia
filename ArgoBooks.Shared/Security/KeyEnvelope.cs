using System.Security.Cryptography;

namespace ArgoBooks.Core.Security;

/// <summary>
/// Envelope encryption for company files (file format version 2 and later).
///
/// Up to format version 1 the archive was encrypted directly with a key derived from the
/// user's password, so the password was the only thing that could ever open the file.
/// From version 2 the archive is encrypted with a randomly generated data encryption key
/// (DEK), and the DEK itself is stored in the footer once per unlock path:
///
///   - wrapped under a PBKDF2 key derived from the user's password
///   - wrapped under the Argo Books recovery public key (see <see cref="RecoveryKeyProvider"/>)
///
/// Every wrap yields the same DEK, so the archive is encrypted only once no matter how many
/// unlock paths exist. Adding a path later costs one footer field and never requires
/// rewriting the archive, and changing a password becomes a rewrap rather than a full
/// re-encrypt of the user's data.
/// </summary>
public static class KeyEnvelope
{
    /// <summary>
    /// Generates a new random data encryption key (32 bytes, for AES-256).
    /// </summary>
    public static byte[] GenerateDataKey()
    {
        return RandomNumberGenerator.GetBytes(KeyDerivation.KeySize);
    }

    /// <summary>
    /// Generates a random nonce for a single key-wrapping operation.
    /// A wrap nonce must never be reused with the same wrapping key, so callers generate
    /// a fresh one every time they wrap.
    /// </summary>
    public static byte[] GenerateWrapNonce()
    {
        return RandomNumberGenerator.GetBytes(KeyDerivation.IvSize);
    }

    /// <summary>
    /// Encrypts a data encryption key under a wrapping key using AES-256-GCM.
    /// </summary>
    /// <param name="dataKey">The DEK to protect.</param>
    /// <param name="wrappingKey">32-byte key encryption key.</param>
    /// <param name="nonce">12-byte nonce, unique per wrap for this wrapping key.</param>
    /// <returns>Ciphertext with the 16-byte authentication tag appended.</returns>
    public static byte[] Wrap(byte[] dataKey, byte[] wrappingKey, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentNullException.ThrowIfNull(wrappingKey);
        ArgumentNullException.ThrowIfNull(nonce);

        return AesGcmBlob.Encrypt(dataKey, wrappingKey, nonce);
    }

    /// <summary>
    /// Recovers a data encryption key previously produced by <see cref="Wrap"/>.
    /// </summary>
    /// <param name="wrappedKey">Ciphertext with the authentication tag appended.</param>
    /// <param name="wrappingKey">The same 32-byte key encryption key used to wrap.</param>
    /// <param name="nonce">The same nonce used to wrap.</param>
    /// <returns>The original data encryption key.</returns>
    /// <exception cref="CryptographicException">
    /// Thrown if the wrapping key is wrong or the wrapped key has been tampered with.
    /// </exception>
    public static byte[] Unwrap(byte[] wrappedKey, byte[] wrappingKey, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);
        ArgumentNullException.ThrowIfNull(wrappingKey);
        ArgumentNullException.ThrowIfNull(nonce);

        if (wrappedKey.Length <= KeyDerivation.TagSize)
            throw new CryptographicException("Invalid wrapped key.");

        return AesGcmBlob.Decrypt(wrappedKey, wrappingKey, nonce);
    }
}
