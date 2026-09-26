namespace ArgoBooks.Core.Platform;

/// <summary>
/// Base implementation of platform service with common functionality.
/// </summary>
public abstract class BasePlatformService : IPlatformService
{
    private const string AppName = "ArgoBooks";

    /// <inheritdoc />
    public abstract PlatformType Platform { get; }

    /// <inheritdoc />
    public abstract string GetAppDataPath();

    /// <inheritdoc />
    public virtual string GetTempPath()
    {
        return CombinePaths(Path.GetTempPath(), AppName);
    }

    /// <inheritdoc />
    public virtual string GetCachePath()
    {
        return CombinePaths(GetAppDataPath(), "Cache");
    }

    /// <inheritdoc />
    public void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    /// <inheritdoc />
    public virtual bool SupportsFileSystem => true;

    /// <inheritdoc />
    public virtual bool SupportsNativeDialogs => true;

    /// <inheritdoc />
    public virtual bool SupportsBiometrics => false;

    /// <inheritdoc />
    public virtual Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);

    /// <inheritdoc />
    public virtual Task<string> GetBiometricAvailabilityDetailsAsync() =>
        Task.FromResult("Biometric authentication is not supported on this platform.");

    /// <inheritdoc />
    public virtual Task<bool> AuthenticateWithBiometricAsync(string reason) => Task.FromResult(false);

    /// <inheritdoc />
    public virtual void StorePasswordForBiometric(string fileId, string password)
    {
        // Default implementation does nothing - platforms that support biometrics should override
    }

    /// <inheritdoc />
    public virtual string? GetPasswordForBiometric(string fileId)
    {
        // Default implementation returns null - platforms that support biometrics should override
        return null;
    }

    /// <inheritdoc />
    public virtual void ClearPasswordForBiometric(string fileId)
    {
        // Default implementation does nothing - platforms that support biometrics should override
    }

    /// <inheritdoc />
    public virtual bool SupportsAutoUpdate => true;

    /// <inheritdoc />
    public virtual int MaxRecentCompanies => 10;

    /// <inheritdoc />
    public virtual string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        return Path.GetFullPath(path);
    }

    /// <inheritdoc />
    public virtual string CombinePaths(params string[] paths)
    {
        return Path.Combine(paths);
    }

    /// <inheritdoc />
    public virtual string GetMachineId()
    {
        // Fallback implementation using machine name
        // Platform-specific implementations should override this with more stable identifiers
        return Environment.MachineName;
    }

    /// <inheritdoc />
    public virtual StringComparer PathComparer => StringComparer.Ordinal;

    /// <summary>
    /// Gets the application name used in paths.
    /// </summary>
    protected static string ApplicationName => AppName;
}
