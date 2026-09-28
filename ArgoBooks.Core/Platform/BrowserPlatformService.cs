namespace ArgoBooks.Core.Platform;

/// <summary>
/// Browser/WebAssembly-specific platform service implementation.
/// Uses virtual file system paths for browser storage.
/// </summary>
public class BrowserPlatformService : BasePlatformService
{
    private const string VirtualRoot = "/argobooks";

    /// <inheritdoc />
    public override PlatformType Platform => PlatformType.Browser;

    /// <inheritdoc />
    public override string GetAppDataPath()
    {
        // Virtual path for IndexedDB/LocalStorage
        return CombinePaths(VirtualRoot, "data");
    }

    /// <inheritdoc />
    public override string GetTempPath()
    {
        // Virtual temp storage
        return CombinePaths(VirtualRoot, "temp");
    }

    /// <inheritdoc />
    public override string GetCachePath()
    {
        // Browser cache storage
        return CombinePaths(VirtualRoot, "cache");
    }

    /// <inheritdoc />
    public override bool SupportsFileSystem => false; // Limited - uses File System Access API

    /// <inheritdoc />
    public override bool SupportsNativeDialogs => false; // Uses browser file picker

    /// <inheritdoc />
    public override bool SupportsBiometrics => false; // Web Authentication API is different

    /// <inheritdoc />
    public override bool SupportsAutoUpdate => false; // Browser handles updates

    /// <inheritdoc />
    public override int MaxRecentCompanies => 5; // Limited storage in browser

    /// <inheritdoc />
    public override string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // Use forward slashes for browser paths
        return path.Replace('\\', '/');
    }

    /// <inheritdoc />
    public override string CombinePaths(params string[] paths)
    {
        // Use forward slashes for browser
        return string.Join("/", paths.Where(p => !string.IsNullOrEmpty(p)));
    }
}
