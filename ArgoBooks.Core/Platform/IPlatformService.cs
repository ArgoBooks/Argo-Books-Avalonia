namespace ArgoBooks.Core.Platform;

/// <summary>
/// Platform-specific service for handling file system paths and platform capabilities.
/// </summary>
public interface IPlatformService
{
    /// <summary>
    /// Gets the platform type.
    /// </summary>
    PlatformType Platform { get; }

    /// <summary>
    /// Gets the application data directory path.
    /// Windows: %APPDATA%/ArgoBooks
    /// macOS: ~/Library/Application Support/ArgoBooks
    /// Linux: ~/.config/ArgoBooks
    /// Browser: Virtual file system path
    /// </summary>
    string GetAppDataPath();

    /// <summary>
    /// Gets the path for temporary files during file operations.
    /// </summary>
    string GetTempPath();

    /// <summary>
    /// Gets the path for cached data (can be cleared without data loss).
    /// </summary>
    string GetCachePath();

    /// <summary>
    /// Ensures a directory exists, creating it if necessary.
    /// </summary>
    /// <param name="path">Directory path to ensure exists.</param>
    void EnsureDirectoryExists(string path);

    /// <summary>
    /// Gets whether the platform supports file system access.
    /// Browser platform may have limited file system access.
    /// </summary>
    bool SupportsFileSystem { get; }

    /// <summary>
    /// Gets whether the platform supports native file dialogs.
    /// </summary>
    bool SupportsNativeDialogs { get; }

    /// <summary>
    /// Gets whether the platform supports biometric authentication.
    /// </summary>
    bool SupportsBiometrics { get; }

    /// <summary>
    /// Checks if biometric authentication is available and configured on this device.
    /// </summary>
    /// <returns>True if biometric authentication is available.</returns>
    Task<bool> IsBiometricAvailableAsync();

    /// <summary>
    /// Gets detailed information about why biometric authentication is not available.
    /// </summary>
    /// <returns>A human-readable description of the availability status.</returns>
    Task<string> GetBiometricAvailabilityDetailsAsync();

    /// <summary>
    /// Authenticates the user using platform biometrics (Windows Hello, fingerprint, etc.).
    /// </summary>
    /// <param name="reason">The reason for authentication shown to the user.</param>
    /// <returns>True if authentication succeeded, false otherwise.</returns>
    Task<bool> AuthenticateWithBiometricAsync(string reason);

    /// <summary>
    /// Stores a password securely for biometric unlock.
    /// The password is encrypted using platform-specific secure storage.
    /// </summary>
    /// <param name="fileId">Unique identifier for the file (typically file path hash).</param>
    /// <param name="password">The password to store.</param>
    void StorePasswordForBiometric(string fileId, string password);

    /// <summary>
    /// Retrieves a stored password for biometric unlock.
    /// Should only be called after successful biometric authentication.
    /// </summary>
    /// <param name="fileId">Unique identifier for the file.</param>
    /// <returns>The stored password, or null if not found.</returns>
    string? GetPasswordForBiometric(string fileId);

    /// <summary>
    /// Clears a stored password for biometric unlock.
    /// </summary>
    /// <param name="fileId">Unique identifier for the file.</param>
    void ClearPasswordForBiometric(string fileId);

    /// <summary>
    /// Gets whether the platform supports automatic updates.
    /// </summary>
    bool SupportsAutoUpdate { get; }

    /// <summary>
    /// Gets the maximum recent companies to track.
    /// </summary>
    int MaxRecentCompanies { get; }

    /// <summary>
    /// Normalizes a file path for the current platform.
    /// </summary>
    /// <param name="path">Path to normalize.</param>
    /// <returns>Normalized path.</returns>
    string NormalizePath(string path);

    /// <summary>
    /// Combines path segments using the platform's path separator.
    /// </summary>
    /// <param name="paths">Path segments to combine.</param>
    /// <returns>Combined path.</returns>
    string CombinePaths(params string[] paths);

    /// <summary>
    /// Gets a stable, unique identifier for this machine.
    /// Used for machine-binding features like license encryption.
    /// </summary>
    /// <remarks>
    /// Platform implementations:
    /// - Windows: Uses MachineGuid from registry
    /// - Linux: Uses /etc/machine-id
    /// - macOS: Uses IOPlatformUUID
    /// - Browser: Uses a stored random ID
    /// </remarks>
    /// <returns>A stable machine identifier string.</returns>
    string GetMachineId();

    /// <summary>
    /// Gets the string comparer appropriate for file paths on this platform.
    /// Windows uses case-insensitive comparison, other platforms use case-sensitive.
    /// </summary>
    StringComparer PathComparer { get; }
}

/// <summary>
/// Supported platform types.
/// </summary>
public enum PlatformType
{
    /// <summary>
    /// Microsoft Windows.
    /// </summary>
    Windows,

    /// <summary>
    /// Apple macOS.
    /// </summary>
    MacOS,

    /// <summary>
    /// Linux distributions.
    /// </summary>
    Linux,

    /// <summary>
    /// Web browser (WebAssembly).
    /// </summary>
    Browser
}
