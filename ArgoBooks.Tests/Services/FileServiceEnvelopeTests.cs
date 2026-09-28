using System.Security.Cryptography;
using System.Text.Json;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Security;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for envelope encryption (file format version 2) and for the guarantee that files
/// written under format version 1 keep opening exactly as they always did.
/// </summary>
public class FileServiceEnvelopeTests : IDisposable
{
    private const string Password = "CorrectHorse#1";
    private const string PayloadFileName = "payload.txt";
    private const string PayloadContents = "ledger contents that must survive a round trip";

    private readonly string _workDirectory;
    private readonly CompressionService _compressionService = new();
    private readonly FooterService _footerService = new();
    private readonly EncryptionService _encryptionService = new();
    private readonly FileService _fileService;

    public FileServiceEnvelopeTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "argo-envelope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
        _fileService = new FileService(_compressionService, _footerService, _encryptionService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workDirectory))
                Directory.Delete(_workDirectory, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }

        GC.SuppressFinalize(this);
    }

    #region Format version 2 round trips

    [Fact]
    public async Task SaveThenOpen_WithPassword_RoundTripsContents()
    {
        var filePath = await SaveCompanyAsync(Password);

        var openedDirectory = await _fileService.OpenCompanyAsync(filePath, Password);

        Assert.Equal(PayloadContents, ReadPayload(openedDirectory));
    }

    [Fact]
    public async Task SaveThenOpen_WithoutPassword_RoundTripsContents()
    {
        var filePath = await SaveCompanyAsync(password: null);

        var openedDirectory = await _fileService.OpenCompanyAsync(filePath);

        Assert.Equal(PayloadContents, ReadPayload(openedDirectory));
    }

    [Fact]
    public async Task Save_WithPassword_WritesEnvelopeFooter()
    {
        var filePath = await SaveCompanyAsync(Password);

        var footer = await _footerService.ReadFooterAsync(filePath);

        Assert.NotNull(footer);
        Assert.Equal(FileFormatConstants.FormatVersion, footer.FormatVersion);
        Assert.True(footer.IsEncrypted);
        Assert.False(string.IsNullOrEmpty(footer.WrappedKey));
        Assert.False(string.IsNullOrEmpty(footer.KeyWrapNonce));
    }

    [Fact]
    public async Task Save_WithoutPassword_WritesNoKeyMaterial()
    {
        var filePath = await SaveCompanyAsync(password: null);

        var footer = await _footerService.ReadFooterAsync(filePath);

        Assert.NotNull(footer);
        Assert.False(footer.IsEncrypted);
        Assert.Null(footer.WrappedKey);
        Assert.Null(footer.KeyWrapNonce);
        Assert.Null(footer.RecoveryBlob);
    }

    [Fact]
    public async Task Open_WithWrongPassword_ThrowsUnauthorized()
    {
        var filePath = await SaveCompanyAsync(Password);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _fileService.OpenCompanyAsync(filePath, "NotThePassword#9"));
    }

    [Fact]
    public async Task Open_EncryptedFileWithoutPassword_ThrowsUnauthorized()
    {
        var filePath = await SaveCompanyAsync(Password);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _fileService.OpenCompanyAsync(filePath));
    }

    [Fact]
    public async Task Save_TwiceWithSamePassword_UsesDifferentDataKeys()
    {
        var first = await _footerService.ReadFooterAsync(await SaveCompanyAsync(Password, "one.argo"));
        var second = await _footerService.ReadFooterAsync(await SaveCompanyAsync(Password, "two.argo"));

        // A fresh salt, nonce and data key every save means two saves of identical content
        // never produce the same wrapped key.
        Assert.NotEqual(first!.WrappedKey, second!.WrappedKey);
        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Iv, second.Iv);
    }

    [Fact]
    public async Task RecoveryKey_OpensAFileWhosePasswordIsLost()
    {
        // The whole point of the feature, proven end to end against a real saved file:
        // the blob in the footer must hold the same data key that encrypted the archive,
        // so support can decrypt without ever knowing the password.
        using var rsa = RSA.Create(4096);
        var fileService = new FileService(
            _compressionService, _footerService, _encryptionService, rsa.ExportSubjectPublicKeyInfoPem());

        var sourceDirectory = Path.Combine(_workDirectory, $"recov-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, PayloadFileName), PayloadContents);

        var filePath = Path.Combine(_workDirectory, "lost-password.argo");
        await fileService.SaveCompanyAsync(filePath, sourceDirectory, "APasswordNobodyRemembers#7");

        var footer = await _footerService.ReadFooterAsync(filePath);
        Assert.NotNull(footer);
        Assert.False(string.IsNullOrEmpty(footer.RecoveryBlob));
        Assert.Equal(RecoveryKeyProvider.CurrentKeyId, footer.RecoveryKeyId);

        // Recover the data key with the private half, then decrypt the archive with it.
        var dataKey = RecoveryKeyProvider.UnwrapDataKey(footer.RecoveryBlob!, rsa.ExportPkcs8PrivateKeyPem());
        await using var content = await _footerService.ReadContentAsync(filePath);
        var gzipBytes = _encryptionService.DecryptWithKey(
            content.ToArray(), dataKey, Convert.FromBase64String(footer.Iv!));

        // It should be a real gzip archive, not noise that happened to decrypt.
        await using var decompressed = await _compressionService.DecompressGZipAsync(new MemoryStream(gzipBytes));
        var extractTo = Path.Combine(_workDirectory, $"extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractTo);
        await _compressionService.ExtractTarArchiveAsync(decompressed, extractTo);

        Assert.Equal(PayloadContents, ReadPayload(extractTo));
    }

    [Fact]
    public async Task Save_OnAConfiguredBuild_WritesARecoveryBlob()
    {
        // Proves the shipped build really does attach a recovery path to new files,
        // using the embedded key rather than an injected test key.
        if (!RecoveryKeyProvider.IsConfigured)
            return;

        var footer = await _footerService.ReadFooterAsync(await SaveCompanyAsync(Password, "shipped.argo"));

        Assert.False(string.IsNullOrEmpty(footer!.RecoveryBlob));
        Assert.Equal(RecoveryKeyProvider.CurrentKeyId, footer.RecoveryKeyId);
    }

    [Fact]
    public async Task Save_WithNoRecoveryKeyConfigured_StillProducesAWorkingFile()
    {
        // An unconfigured build must keep saving and opening files normally, just with
        // no recovery path. Empty string means "no key", as distinct from null, which
        // means "use whatever this build has embedded".
        var fileService = new FileService(
            _compressionService, _footerService, _encryptionService, recoveryPublicKeyPem: "");

        var sourceDirectory = Path.Combine(_workDirectory, $"norecov-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, PayloadFileName), PayloadContents);

        var filePath = Path.Combine(_workDirectory, "no-recovery.argo");
        await fileService.SaveCompanyAsync(filePath, sourceDirectory, Password);

        var footer = await _footerService.ReadFooterAsync(filePath);
        Assert.Null(footer!.RecoveryBlob);
        Assert.Null(footer.RecoveryKeyId);

        Assert.Equal(PayloadContents, ReadPayload(await fileService.OpenCompanyAsync(filePath, Password)));
    }

    [Fact]
    public async Task Save_WithPasswordButNoEncryptionService_RefusesInsteadOfWritingPlaintext()
    {
        // The footer records IsEncrypted from the presence of a password alone. Without this
        // guard the archive would be written in the clear under a footer claiming encryption,
        // and the file could never be opened again.
        var fileService = new FileService(_compressionService, _footerService, encryptionService: null);

        var sourceDirectory = Path.Combine(_workDirectory, $"noservice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, PayloadFileName), PayloadContents);

        var filePath = Path.Combine(_workDirectory, "no-service.argo");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fileService.SaveCompanyAsync(filePath, sourceDirectory, Password));

        Assert.False(File.Exists(filePath), "A file must not be left behind when encryption is unavailable.");
    }

    [Fact]
    public async Task Save_WithoutPassword_DoesNotAdvertiseBiometricUnlock()
    {
        // A file returned by support recovery has no password but still carries the old
        // biometric setting inside its archive. The footer must not offer an unlock that
        // cannot possibly succeed.
        var filePath = await SaveWithBiometricSettingAsync(password: null, "recovered-bio.argo");

        var footer = await _footerService.ReadFooterAsync(filePath);

        Assert.False(footer!.IsEncrypted);
        Assert.False(footer.BiometricEnabled);
    }

    [Fact]
    public async Task Save_WithPassword_KeepsBiometricSetting()
    {
        var filePath = await SaveWithBiometricSettingAsync(Password, "password-bio.argo");

        var footer = await _footerService.ReadFooterAsync(filePath);

        Assert.True(footer!.IsEncrypted);
        Assert.True(footer.BiometricEnabled);
    }

    private async Task<string> SaveWithBiometricSettingAsync(string? password, string fileName)
    {
        var sourceDirectory = Path.Combine(_workDirectory, $"bio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, PayloadFileName), PayloadContents);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "appSettings.json"),
            """{"security":{"biometricEnabled":true}}""");

        var filePath = Path.Combine(_workDirectory, fileName);
        await _fileService.SaveCompanyAsync(filePath, sourceDirectory, password);
        return filePath;
    }

    #endregion

    #region Backward compatibility

    [Fact]
    public async Task LegacyVersion1File_StillOpensWithItsPassword()
    {
        var filePath = await WriteLegacyVersion1FileAsync(Password);

        var openedDirectory = await _fileService.OpenCompanyAsync(filePath, Password);

        Assert.Equal(PayloadContents, ReadPayload(openedDirectory));
    }

    [Fact]
    public async Task LegacyVersion1File_WithWrongPassword_ThrowsUnauthorized()
    {
        var filePath = await WriteLegacyVersion1FileAsync(Password);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _fileService.OpenCompanyAsync(filePath, "NotThePassword#9"));
    }

    [Fact]
    public void LegacyFooter_WithNoFormatVersion_DefaultsToOne()
    {
        // Files written before envelope encryption have no formatVersion field at all.
        // Deserialization must read that absence as version 1, not as 0.
        const string legacyJson = """{"version":"2.0.10","isEncrypted":false,"companyName":"Acme"}""";

        var footer = JsonSerializer.Deserialize<FileFooter>(
            legacyJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.NotNull(footer);
        Assert.Equal(1, footer.FormatVersion);
    }

    [Fact]
    public async Task Open_FileFromNewerFormatVersion_ThrowsCompanyFileTooNew()
    {
        var filePath = Path.Combine(_workDirectory, "future.argo");
        var footer = new FileFooter
        {
            Version = "99.0.0",
            FormatVersion = FileFormatConstants.FormatVersion + 1,
            IsEncrypted = true,
            CompanyName = "From The Future"
        };

        await using (var stream = File.Create(filePath))
        {
            await stream.WriteAsync("not readable by this build"u8.ToArray());
            await _footerService.WriteFooterAsync(stream, footer);
        }

        // Must fail as "your app is too old", not as a spurious wrong-password error.
        await Assert.ThrowsAsync<CompanyFileTooNewException>(
            () => _fileService.OpenCompanyAsync(filePath, Password));
    }

    #endregion

    #region Helpers

    private async Task<string> SaveCompanyAsync(string? password, string fileName = "company.argo")
    {
        var sourceDirectory = Path.Combine(_workDirectory, $"src-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, PayloadFileName), PayloadContents);

        var filePath = Path.Combine(_workDirectory, fileName);
        await _fileService.SaveCompanyAsync(filePath, sourceDirectory, password);
        return filePath;
    }

    /// <summary>
    /// Writes a file exactly the way format version 1 did: the archive encrypted directly
    /// with the password-derived key, and no envelope fields in the footer.
    /// </summary>
    private async Task<string> WriteLegacyVersion1FileAsync(string password)
    {
        var sourceDirectory = Path.Combine(_workDirectory, $"legacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, PayloadFileName), PayloadContents);

        await using var tar = await _compressionService.CreateTarArchiveAsync(sourceDirectory, includeBaseDirectory: false);
        await using var gzip = await _compressionService.CompressGZipAsync(tar);

        var salt = _encryptionService.GenerateSalt();
        var iv = _encryptionService.GenerateIv();
        var passwordHash = KeyDerivation.ComputePasswordHashBase64(password, salt);

        using var plaintext = new MemoryStream();
        gzip.Position = 0;
        await gzip.CopyToAsync(plaintext);
        var ciphertext = _encryptionService.Encrypt(plaintext.ToArray(), password, salt, iv);

        var footer = new FileFooter
        {
            Version = "2.0.10",
            FormatVersion = 1,
            IsEncrypted = true,
            Salt = salt,
            Iv = iv,
            PasswordHash = passwordHash,
            CompanyName = "Legacy Co"
        };

        var filePath = Path.Combine(_workDirectory, $"legacy-{Guid.NewGuid():N}.argo");
        await using (var stream = File.Create(filePath))
        {
            await stream.WriteAsync(ciphertext);
            await _footerService.WriteFooterAsync(stream, footer);
        }

        return filePath;
    }

    private static string ReadPayload(string directory)
    {
        var path = Directory.GetFiles(directory, PayloadFileName, SearchOption.AllDirectories).Single();
        return File.ReadAllText(path);
    }

    #endregion
}

/// <summary>
/// Tests for the key wrapping primitives that envelope encryption is built on.
/// </summary>
public class KeyEnvelopeTests
{
    [Fact]
    public void WrapThenUnwrap_ReturnsOriginalKey()
    {
        var dataKey = KeyEnvelope.GenerateDataKey();
        var wrappingKey = RandomNumberGenerator.GetBytes(32);
        var nonce = KeyEnvelope.GenerateWrapNonce();

        var wrapped = KeyEnvelope.Wrap(dataKey, wrappingKey, nonce);

        Assert.Equal(dataKey, KeyEnvelope.Unwrap(wrapped, wrappingKey, nonce));
    }

    [Fact]
    public void GenerateDataKey_Is256Bit()
    {
        Assert.Equal(32, KeyEnvelope.GenerateDataKey().Length);
    }

    [Fact]
    public void GenerateDataKey_ReturnsUniqueValues()
    {
        Assert.NotEqual(KeyEnvelope.GenerateDataKey(), KeyEnvelope.GenerateDataKey());
    }

    [Fact]
    public void Wrap_DoesNotLeakTheKey()
    {
        var dataKey = KeyEnvelope.GenerateDataKey();
        var wrapped = KeyEnvelope.Wrap(dataKey, RandomNumberGenerator.GetBytes(32), KeyEnvelope.GenerateWrapNonce());

        // The wrapped form is ciphertext plus a 16-byte tag, and must share no prefix
        // with the plaintext key.
        Assert.Equal(dataKey.Length + 16, wrapped.Length);
        Assert.NotEqual(dataKey, wrapped[..dataKey.Length]);
    }

    [Fact]
    public void Unwrap_WithWrongWrappingKey_Throws()
    {
        var dataKey = KeyEnvelope.GenerateDataKey();
        var nonce = KeyEnvelope.GenerateWrapNonce();
        var wrapped = KeyEnvelope.Wrap(dataKey, RandomNumberGenerator.GetBytes(32), nonce);

        // AES-GCM signals this as AuthenticationTagMismatchException, a CryptographicException
        // subclass, so match the family rather than the exact type.
        Assert.ThrowsAny<CryptographicException>(
            () => KeyEnvelope.Unwrap(wrapped, RandomNumberGenerator.GetBytes(32), nonce));
    }

    [Fact]
    public void Unwrap_WithTamperedCiphertext_Throws()
    {
        var wrappingKey = RandomNumberGenerator.GetBytes(32);
        var nonce = KeyEnvelope.GenerateWrapNonce();
        var wrapped = KeyEnvelope.Wrap(KeyEnvelope.GenerateDataKey(), wrappingKey, nonce);

        wrapped[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => KeyEnvelope.Unwrap(wrapped, wrappingKey, nonce));
    }

    [Fact]
    public void RecoveryKeyProvider_WhenUnconfigured_ReturnsNullInsteadOfThrowing()
    {
        // An unconfigured build must still be able to save files.
        if (RecoveryKeyProvider.IsConfigured)
            return;

        Assert.Null(RecoveryKeyProvider.TryWrapDataKey(KeyEnvelope.GenerateDataKey()));
    }

    [Fact]
    public void EmbeddedRecoveryKey_WhenConfigured_IsParseableAndWraps()
    {
        // Guards against a malformed paste into RecoveryKeyProvider.PublicKeyPem.
        // TryWrapDataKey swallows parse failures and returns null by design, so without
        // this test a mangled key would silently ship with recovery disabled and nobody
        // would find out until a customer needed their file back.
        if (!RecoveryKeyProvider.IsConfigured)
            return;

        // Must import cleanly as an RSA public key.
        using var rsa = RSA.Create();
        rsa.ImportFromPem(RecoveryKeyProvider.EmbeddedPublicKeyPem);
        Assert.True(rsa.KeySize >= 2048, $"Recovery key is only {rsa.KeySize} bits.");

        // And must actually produce a wrap through the normal code path.
        Assert.NotNull(RecoveryKeyProvider.TryWrapDataKey(KeyEnvelope.GenerateDataKey()));
    }

    [Fact]
    public void RecoveryKey_WrapThenUnwrap_ReturnsOriginalKey()
    {
        // The single most important test here. Wrapping happens on every save while
        // unwrapping only ever happens in the support tool, so a mismatch between the two
        // would stay invisible until a customer needed recovery and could not get it.
        using var rsa = RSA.Create(4096);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();

        var dataKey = KeyEnvelope.GenerateDataKey();
        var wrapped = RecoveryKeyProvider.WrapDataKey(dataKey, publicKeyPem);

        Assert.Equal(dataKey, RecoveryKeyProvider.UnwrapDataKey(wrapped, privateKeyPem));
    }

    [Fact]
    public void RecoveryKey_UnwrapWithWrongPrivateKey_Throws()
    {
        using var realKey = RSA.Create(4096);
        using var otherKey = RSA.Create(4096);

        var wrapped = RecoveryKeyProvider.WrapDataKey(
            KeyEnvelope.GenerateDataKey(), realKey.ExportSubjectPublicKeyInfoPem());

        Assert.ThrowsAny<CryptographicException>(
            () => RecoveryKeyProvider.UnwrapDataKey(wrapped, otherKey.ExportPkcs8PrivateKeyPem()));
    }

    [Fact]
    public void RecoveryKey_RecoveredDataKeyStillDecryptsTheArchive()
    {
        // End to end: the key recovered through the vendor path must decrypt content that
        // was encrypted under the data key, which is what the support tool relies on.
        using var rsa = RSA.Create(4096);
        var encryptionService = new EncryptionService();

        var dataKey = KeyEnvelope.GenerateDataKey();
        var nonce = KeyEnvelope.GenerateWrapNonce();
        var plaintext = "the customer's books"u8.ToArray();

        var ciphertext = encryptionService.EncryptWithKey(plaintext, dataKey, nonce);
        var wrapped = RecoveryKeyProvider.WrapDataKey(dataKey, rsa.ExportSubjectPublicKeyInfoPem());

        var recoveredKey = RecoveryKeyProvider.UnwrapDataKey(wrapped, rsa.ExportPkcs8PrivateKeyPem());

        Assert.Equal(plaintext, encryptionService.DecryptWithKey(ciphertext, recoveredKey, nonce));
    }
}
