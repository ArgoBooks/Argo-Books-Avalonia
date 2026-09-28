namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for AES-256-GCM encryption and decryption of company files.
/// </summary>
public interface IEncryptionService
{
    #region Key Generation

    /// <summary>
    /// Generates a random salt for key derivation.
    /// </summary>
    /// <returns>Base64-encoded salt (32 bytes).</returns>
    string GenerateSalt();

    /// <summary>
    /// Generates a random IV/nonce for encryption.
    /// </summary>
    /// <returns>Base64-encoded IV (12 bytes for GCM).</returns>
    string GenerateIv();

    #endregion

    #region Encryption (Byte Arrays)

    /// <summary>
    /// Encrypts data using AES-256-GCM.
    /// </summary>
    /// <param name="data">Data to encrypt.</param>
    /// <param name="password">Password for encryption.</param>
    /// <param name="salt">Base64-encoded salt for key derivation.</param>
    /// <param name="iv">Base64-encoded IV/nonce.</param>
    /// <returns>Encrypted data with authentication tag appended.</returns>
    byte[] Encrypt(byte[] data, string password, string salt, string iv);

    /// <summary>
    /// Decrypts data that was encrypted with AES-256-GCM.
    /// </summary>
    /// <param name="encryptedData">Encrypted data with authentication tag.</param>
    /// <param name="password">Password for decryption.</param>
    /// <param name="salt">Base64-encoded salt for key derivation.</param>
    /// <param name="iv">Base64-encoded IV/nonce.</param>
    /// <returns>Decrypted data.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">Thrown if password is incorrect or data is tampered.</exception>
    byte[] Decrypt(byte[] encryptedData, string password, string salt, string iv);

    /// <summary>
    /// Encrypts data with an already-derived key using AES-256-GCM.
    ///
    /// Used by envelope encryption (file format version 2 and later), where the archive is
    /// encrypted with a random data key rather than one derived from the password.
    /// </summary>
    /// <param name="data">Data to encrypt.</param>
    /// <param name="key">32-byte encryption key.</param>
    /// <param name="nonce">12-byte nonce, never reused with this key.</param>
    /// <returns>Encrypted data with authentication tag appended.</returns>
    byte[] EncryptWithKey(byte[] data, byte[] key, byte[] nonce);

    /// <summary>
    /// Decrypts data produced by <see cref="EncryptWithKey"/>.
    /// </summary>
    /// <param name="encryptedData">Encrypted data with authentication tag.</param>
    /// <param name="key">The same 32-byte key used to encrypt.</param>
    /// <param name="nonce">The same nonce used to encrypt.</param>
    /// <returns>Decrypted data.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Thrown if the key is wrong or the data has been tampered with.
    /// </exception>
    byte[] DecryptWithKey(byte[] encryptedData, byte[] key, byte[] nonce);

    #endregion

    #region Encryption (Streams)

    /// <summary>
    /// Verifies the password against <paramref name="expectedPasswordHash"/> and
    /// decrypts the data in a single PBKDF2 pass. Verifies the stored hash and then
    /// decrypts (as <see cref="Decrypt"/> does), but derives the key material only once.
    /// </summary>
    /// <param name="encryptedData">Encrypted data with authentication tag.</param>
    /// <param name="password">Password for decryption.</param>
    /// <param name="salt">Base64-encoded salt for key derivation.</param>
    /// <param name="iv">Base64-encoded IV/nonce.</param>
    /// <param name="expectedPasswordHash">Base64-encoded stored password hash to verify against.</param>
    /// <returns>Decrypted data.</returns>
    /// <exception cref="System.UnauthorizedAccessException">Thrown if the password does not match.</exception>
    byte[] DecryptWithVerification(
        byte[] encryptedData, string password, string salt, string iv, string expectedPasswordHash);

    /// <summary>
    /// Stream overload of <see cref="DecryptWithVerification"/>.
    /// </summary>
    Task<MemoryStream> DecryptWithVerificationAsync(
        Stream encryptedStream, string password, string salt, string iv, string expectedPasswordHash);

    #endregion
}
