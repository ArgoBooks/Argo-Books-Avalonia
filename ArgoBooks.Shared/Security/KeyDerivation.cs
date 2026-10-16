using System.Security.Cryptography;

namespace ArgoBooks.Core.Security;

/// <summary>
/// Provides key derivation functionality using PBKDF2.
/// </summary>
public static class KeyDerivation
{
    /// <summary>
    /// Number of PBKDF2 iterations.
    /// OWASP recommends at least 600,000 for SHA-256 as of 2023.
    /// </summary>
    public const int Iterations = 600_000;

    /// <summary>
    /// Salt size in bytes (256 bits).
    /// </summary>
    public const int SaltSize = 32;

    /// <summary>
    /// Derived key size in bytes (256 bits for AES-256).
    /// </summary>
    public const int KeySize = 32;

    /// <summary>
    /// Hash size for password verification in bytes.
    /// </summary>
    public const int HashSize = 32;

    /// <summary>
    /// Master key size in bytes (64 bytes = 32 for encryption + 32 for verification).
    /// </summary>
    public const int MasterKeySize = 64;

    /// <summary>
    /// IV/Nonce size for AES-GCM in bytes (96 bits recommended).
    /// </summary>
    public const int IvSize = 12;

    /// <summary>
    /// Authentication tag size for AES-GCM in bytes (128 bits).
    /// </summary>
    public const int TagSize = 16;

    /// <summary>
    /// Derives an encryption key from a password using PBKDF2-SHA256.
    /// Uses a 64-byte master key derivation, returning only the first 32 bytes
    /// for encryption. The last 32 bytes are used separately for password verification.
    /// </summary>
    /// <param name="password">The password to derive the key from.</param>
    /// <param name="salt">The salt bytes.</param>
    /// <returns>The derived key bytes (32 bytes for AES-256).</returns>
    public static byte[] DeriveKey(string password, byte[] salt)
    {
        DeriveKeyAndHash(password, salt, out var encryptionKey, out var verificationHash);
        CryptographicOperations.ZeroMemory(verificationHash);
        return encryptionKey;
    }

    /// <summary>
    /// Derives BOTH the AES encryption key (first 32 bytes) and the password
    /// verification hash (last 32 bytes) from a single PBKDF2 master-key pass,
    /// for a caller that needs both and would otherwise run PBKDF2 twice.
    /// </summary>
    /// <param name="password">The password to derive from.</param>
    /// <param name="salt">The salt bytes.</param>
    /// <param name="encryptionKey">Outputs the 32-byte AES encryption key.</param>
    /// <param name="verificationHash">Outputs the 32-byte verification hash.</param>
    public static void DeriveKeyAndHash(
        string password, byte[] salt, out byte[] encryptionKey, out byte[] verificationHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentNullException.ThrowIfNull(salt);

        var masterKey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            MasterKeySize);
        try
        {
            encryptionKey = new byte[KeySize];
            verificationHash = new byte[HashSize];

            // First 32 bytes are the encryption key, last 32 are the verification hash.
            Buffer.BlockCopy(masterKey, 0, encryptionKey, 0, KeySize);
            Buffer.BlockCopy(masterKey, KeySize, verificationHash, 0, HashSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    /// <summary>
    /// Derives an encryption key from a password using PBKDF2-SHA256.
    /// </summary>
    /// <param name="password">The password to derive the key from.</param>
    /// <param name="saltBase64">Base64-encoded salt.</param>
    /// <returns>The derived key bytes.</returns>
    public static byte[] DeriveKey(string password, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        return DeriveKey(password, salt);
    }

    /// <summary>
    /// Generates a cryptographically secure random salt.
    /// </summary>
    /// <returns>Random salt bytes.</returns>
    public static byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(SaltSize);
    }

    /// <summary>
    /// Generates a cryptographically secure random salt as Base64.
    /// </summary>
    /// <returns>Base64-encoded random salt.</returns>
    public static string GenerateSaltBase64()
    {
        return Convert.ToBase64String(GenerateSalt());
    }

    /// <summary>
    /// Generates a cryptographically secure random IV/nonce for AES-GCM.
    /// </summary>
    /// <returns>Random IV bytes.</returns>
    public static byte[] GenerateIv()
    {
        return RandomNumberGenerator.GetBytes(IvSize);
    }

    /// <summary>
    /// Generates a cryptographically secure random IV/nonce as Base64.
    /// </summary>
    /// <returns>Base64-encoded random IV.</returns>
    public static string GenerateIvBase64()
    {
        return Convert.ToBase64String(GenerateIv());
    }

    /// <summary>
    /// Computes a password hash for storage/verification using PBKDF2.
    /// Derives a 64-byte master key and returns the last 32 bytes as the verification hash.
    /// This ensures the verification hash is distinct from the encryption key (first 32 bytes).
    /// </summary>
    /// <param name="password">The password to hash.</param>
    /// <param name="salt">The salt bytes.</param>
    /// <returns>The password hash bytes (32 bytes, distinct from the encryption key).</returns>
    public static byte[] ComputePasswordHash(string password, byte[] salt)
    {
        DeriveKeyAndHash(password, salt, out var encryptionKey, out var verificationHash);
        CryptographicOperations.ZeroMemory(encryptionKey);
        return verificationHash;
    }

    /// <summary>
    /// Computes a password hash for storage/verification using PBKDF2.
    /// </summary>
    /// <param name="password">The password to hash.</param>
    /// <param name="saltBase64">Base64-encoded salt.</param>
    /// <returns>Base64-encoded password hash.</returns>
    public static string ComputePasswordHashBase64(string password, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var hash = ComputePasswordHash(password, salt);
        return Convert.ToBase64String(hash);
    }
}
