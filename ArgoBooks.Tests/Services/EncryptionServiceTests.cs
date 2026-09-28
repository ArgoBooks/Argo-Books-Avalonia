using System.Security.Cryptography;
using ArgoBooks.Core.Security;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the EncryptionService class.
/// </summary>
public class EncryptionServiceTests
{
    private readonly EncryptionService _encryptionService = new();

    #region Key Generation Tests

    [Fact]
    public void GenerateSalt_ReturnsValidBase64()
    {
        var salt = _encryptionService.GenerateSalt();

        Assert.False(string.IsNullOrEmpty(salt));

        // Should be valid Base64
        var decoded = Convert.FromBase64String(salt);
        Assert.True(decoded.Length > 0);
    }

    [Fact]
    public void GenerateSalt_ReturnsUniqueValues()
    {
        var salt1 = _encryptionService.GenerateSalt();
        var salt2 = _encryptionService.GenerateSalt();

        Assert.NotEqual(salt1, salt2);
    }

    [Fact]
    public void GenerateIv_ReturnsValidBase64()
    {
        var iv = _encryptionService.GenerateIv();

        Assert.False(string.IsNullOrEmpty(iv));

        // Should be valid Base64
        var decoded = Convert.FromBase64String(iv);
        Assert.True(decoded.Length > 0);
    }

    [Fact]
    public void GenerateIv_ReturnsUniqueValues()
    {
        var iv1 = _encryptionService.GenerateIv();
        var iv2 = _encryptionService.GenerateIv();

        Assert.NotEqual(iv1, iv2);
    }

    #endregion

    #region Encryption/Decryption Tests (Byte Arrays)

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_RestoresOriginalData()
    {
        var originalData = "Hello, World! This is a test message."u8.ToArray();
        var password = "TestPassword123";
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        var encrypted = _encryptionService.Encrypt(originalData, password, salt, iv);
        var decrypted = _encryptionService.Decrypt(encrypted, password, salt, iv);

        Assert.True(originalData.SequenceEqual(decrypted));
    }

    [Fact]
    public void Encrypt_ProducesDifferentOutputFromInput()
    {
        var originalData = "Hello, World!"u8.ToArray();
        var password = "TestPassword123";
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        var encrypted = _encryptionService.Encrypt(originalData, password, salt, iv);

        Assert.False(originalData.SequenceEqual(encrypted));
    }

    [Fact]
    public void Encrypt_SameDataDifferentIv_ProducesDifferentCiphertext()
    {
        var originalData = "Hello, World!"u8.ToArray();
        var password = "TestPassword123";
        var salt = _encryptionService.GenerateSalt();
        var iv1 = _encryptionService.GenerateIv();
        var iv2 = _encryptionService.GenerateIv();

        var encrypted1 = _encryptionService.Encrypt(originalData, password, salt, iv1);
        var encrypted2 = _encryptionService.Encrypt(originalData, password, salt, iv2);

        Assert.False(encrypted1.SequenceEqual(encrypted2));
    }

    [Fact]
    public void Decrypt_WithWrongPassword_ThrowsCryptographicException()
    {
        var originalData = "Hello, World!"u8.ToArray();
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        var encrypted = _encryptionService.Encrypt(originalData, "CorrectPassword1", salt, iv);

        Assert.ThrowsAny<CryptographicException>(() =>
            _encryptionService.Decrypt(encrypted, "WrongPassword1", salt, iv));
    }

    [Fact]
    public void Decrypt_WithWrongIv_ThrowsCryptographicException()
    {
        var originalData = "Hello, World!"u8.ToArray();
        var password = "TestPassword123";
        var salt = _encryptionService.GenerateSalt();
        var iv1 = _encryptionService.GenerateIv();
        var iv2 = _encryptionService.GenerateIv();

        var encrypted = _encryptionService.Encrypt(originalData, password, salt, iv1);

        Assert.ThrowsAny<CryptographicException>(() =>
            _encryptionService.Decrypt(encrypted, password, salt, iv2));
    }

    [Fact]
    public void Encrypt_ThrowsOnNullData()
    {
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        Assert.Throws<ArgumentNullException>(() =>
            _encryptionService.Encrypt(null!, "password", salt, iv));
    }

    [Fact]
    public void Encrypt_ThrowsOnNullPassword()
    {
        var data = "test"u8.ToArray();
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        Assert.Throws<ArgumentNullException>(() =>
            _encryptionService.Encrypt(data, null!, salt, iv));
    }

    [Fact]
    public void Encrypt_ThrowsOnEmptyPassword()
    {
        var data = "test"u8.ToArray();
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        Assert.Throws<ArgumentException>(() =>
            _encryptionService.Encrypt(data, "", salt, iv));
    }

    [Fact]
    public void Decrypt_ThrowsOnInvalidEncryptedData()
    {
        var invalidData = new byte[5]; // Too small to contain auth tag
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        Assert.Throws<CryptographicException>(() =>
            _encryptionService.Decrypt(invalidData, "password", salt, iv));
    }

    [Fact]
    public void Encrypt_WorksWithEmptyData()
    {
        var emptyData = Array.Empty<byte>();
        var password = "TestPassword123";
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        var encrypted = _encryptionService.Encrypt(emptyData, password, salt, iv);
        var decrypted = _encryptionService.Decrypt(encrypted, password, salt, iv);

        Assert.Empty(decrypted);
    }

    [Fact]
    public void Encrypt_WorksWithLargeData()
    {
        var largeData = new byte[1024 * 1024]; // 1 MB
        new Random(42).NextBytes(largeData);
        var password = "TestPassword123";
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        var encrypted = _encryptionService.Encrypt(largeData, password, salt, iv);
        var decrypted = _encryptionService.Decrypt(encrypted, password, salt, iv);

        Assert.True(largeData.SequenceEqual(decrypted));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void FullWorkflow_CreateHashEncryptDecryptWithVerification()
    {
        // Simulate a full file encryption workflow
        var password = "SecurePassword123!";
        var fileContent = "This is confidential file content."u8.ToArray();

        // Step 1: Generate salt and hash password for storage
        var salt = _encryptionService.GenerateSalt();
        var passwordHash = KeyDerivation.ComputePasswordHashBase64(password, salt);

        // Step 2: Generate IV and encrypt file
        var iv = _encryptionService.GenerateIv();
        var encryptedContent = _encryptionService.Encrypt(fileContent, password, salt, iv);

        // Step 3: Later, verify the password and decrypt in one pass
        var decryptedContent = _encryptionService.DecryptWithVerification(
            encryptedContent, password, salt, iv, passwordHash);

        Assert.True(fileContent.SequenceEqual(decryptedContent));
    }

    [Fact]
    public void TamperedData_FailsAuthentication()
    {
        var originalData = "Secret message"u8.ToArray();
        var password = "TestPassword123";
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();

        var encrypted = _encryptionService.Encrypt(originalData, password, salt, iv);

        // Tamper with the ciphertext
        encrypted[0] ^= 0xFF;

        // Should fail authentication
        Assert.ThrowsAny<CryptographicException>(() =>
            _encryptionService.Decrypt(encrypted, password, salt, iv));
    }

    #endregion

    #region DecryptWithVerification Tests

    [Fact]
    public void DecryptWithVerification_CorrectPassword_RoundTrips()
    {
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();
        var hash = KeyDerivation.ComputePasswordHashBase64("pw-correct", salt);
        var plaintext = "hello argo"u8.ToArray();
        var encrypted = _encryptionService.Encrypt(plaintext, "pw-correct", salt, iv);

        var result = _encryptionService.DecryptWithVerification(
            encrypted, "pw-correct", salt, iv, hash);

        Assert.Equal(plaintext, result);
    }

    [Fact]
    public void DecryptWithVerification_MatchesPlainDecrypt()
    {
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();
        var hash = KeyDerivation.ComputePasswordHashBase64("pw", salt);
        var plaintext = "some longer confidential content for the file"u8.ToArray();
        var encrypted = _encryptionService.Encrypt(plaintext, "pw", salt, iv);

        var viaSeparate = _encryptionService.Decrypt(encrypted, "pw", salt, iv);
        var viaCombined = _encryptionService.DecryptWithVerification(encrypted, "pw", salt, iv, hash);

        Assert.Equal(viaSeparate, viaCombined);
    }

    [Fact]
    public void DecryptWithVerification_WrongPassword_ThrowsUnauthorized()
    {
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();
        var hash = KeyDerivation.ComputePasswordHashBase64("pw-correct", salt);
        var encrypted = _encryptionService.Encrypt("hello"u8.ToArray(), "pw-correct", salt, iv);

        Assert.Throws<UnauthorizedAccessException>(() =>
            _encryptionService.DecryptWithVerification(encrypted, "pw-wrong", salt, iv, hash));
    }

    [Fact]
    public async Task DecryptWithVerificationAsync_CorrectPassword_RoundTrips()
    {
        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();
        var hash = KeyDerivation.ComputePasswordHashBase64("pw", salt);
        var plaintext = "stream content"u8.ToArray();
        var encrypted = _encryptionService.Encrypt(plaintext, "pw", salt, iv);

        using var input = new MemoryStream(encrypted);
        using var result = await _encryptionService.DecryptWithVerificationAsync(
            input, "pw", salt, iv, hash);

        Assert.Equal(plaintext, result.ToArray());
        Assert.Equal(0, result.Position);
    }

    #endregion
}
