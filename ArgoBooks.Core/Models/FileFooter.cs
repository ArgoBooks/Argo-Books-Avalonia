namespace ArgoBooks.Core.Models;

/// <summary>
/// Footer structure appended to .argo files.
/// Contains metadata needed to open the file without reading the entire contents.
/// </summary>
public class FileFooter
{
    /// <summary>
    /// Application version that created/last saved the file.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Layout version of this footer and of the encryption envelope.
    ///
    /// Absent in files written before envelope encryption existed, and System.Text.Json
    /// leaves it at the default of 1 for those, which is exactly the intended reading:
    /// no value means version 1.
    ///
    /// Version 1: the archive is encrypted directly with a key derived from the password.
    /// Version 2: the archive is encrypted with a random data key, which is itself stored
    /// wrapped in <see cref="WrappedKey"/> and optionally <see cref="RecoveryBlob"/>.
    /// </summary>
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    /// <summary>
    /// Whether the file contents are encrypted.
    /// </summary>
    [JsonPropertyName("isEncrypted")]
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// The file's data encryption key, wrapped under a key derived from the user's password
    /// (Base64 encoded). Format version 2 and later only.
    /// </summary>
    [JsonPropertyName("wrappedKey")]
    public string? WrappedKey { get; set; }

    /// <summary>
    /// Nonce used when wrapping <see cref="WrappedKey"/> (Base64 encoded).
    /// Distinct from <see cref="Iv"/>, which belongs to the archive itself.
    /// Format version 2 and later only.
    /// </summary>
    [JsonPropertyName("keyWrapNonce")]
    public string? KeyWrapNonce { get; set; }

    /// <summary>
    /// The same data encryption key, wrapped under the Argo Books recovery public key
    /// (Base64 encoded). Lets support recover a file whose password has been lost.
    ///
    /// Null when the build had no recovery key configured. The file is still valid and
    /// opens normally with its password; it simply has no recovery path.
    /// </summary>
    [JsonPropertyName("recoveryBlob")]
    public string? RecoveryBlob { get; set; }

    /// <summary>
    /// Identifies which recovery key pair <see cref="RecoveryBlob"/> was wrapped under,
    /// so the correct private key can be selected after a rotation.
    /// </summary>
    [JsonPropertyName("recoveryKeyId")]
    public string? RecoveryKeyId { get; set; }

    /// <summary>
    /// Salt used for password-based key derivation (Base64 encoded).
    /// Only present if IsEncrypted is true.
    /// </summary>
    [JsonPropertyName("salt")]
    public string? Salt { get; set; }

    /// <summary>
    /// Hash of the password for verification (Base64 encoded).
    /// Only present if IsEncrypted is true.
    /// </summary>
    [JsonPropertyName("passwordHash")]
    public string? PasswordHash { get; set; }

    /// <summary>
    /// IV/Nonce used for encryption (Base64 encoded).
    /// Only present if IsEncrypted is true.
    /// </summary>
    [JsonPropertyName("iv")]
    public string? Iv { get; set; }

    /// <summary>
    /// List of accountant names (for quick access without decryption).
    /// </summary>
    [JsonPropertyName("accountants")]
    public List<string> Accountants { get; set; } = [];

    /// <summary>
    /// Company name (for display in recent files without opening).
    /// </summary>
    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// When the file was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the file was last modified.
    /// </summary>
    [JsonPropertyName("modifiedAt")]
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether biometric authentication is enabled for this file.
    /// Stored in footer so it can be read before decryption.
    /// </summary>
    [JsonPropertyName("biometricEnabled")]
    public bool BiometricEnabled { get; set; } = false;

    /// <summary>
    /// Small Base64-encoded PNG thumbnail of the company logo (64x64 max).
    /// Stored in footer for instant access without decompressing the archive.
    /// Null if the company has no logo.
    /// </summary>
    [JsonPropertyName("logoThumbnail")]
    public string? LogoThumbnail { get; set; }
}

/// <summary>
/// Marker bytes and constants for file format.
/// </summary>
public static class FileFormatConstants
{
    /// <summary>
    /// Magic bytes at the end of footer to identify Argo files.
    /// "ARGO" in ASCII.
    /// </summary>
    public static readonly byte[] MagicBytes = "ARGO"u8.ToArray();

    /// <summary>
    /// File format version written by this build.
    ///
    /// 1: archive encrypted directly with the password-derived key.
    /// 2: envelope encryption, archive encrypted with a random data key that is stored
    ///    wrapped under the password and, when configured, under the recovery key.
    /// 3: same layout as 2. Stock quantities can hold decimals, which older builds cannot read.
    /// </summary>
    public const int FormatVersion = 3;

    /// <summary>
    /// Oldest format version this build can still read. Files at any version between this
    /// and <see cref="FormatVersion"/> open normally.
    /// </summary>
    public const int MinimumSupportedFormatVersion = 1;

    /// <summary>
    /// File extension for company files.
    /// </summary>
    public const string CompanyFileExtension = ".argo";

    /// <summary>
    /// File extension for backup files.
    /// </summary>
    public const string BackupFileExtension = ".argobk";
}
