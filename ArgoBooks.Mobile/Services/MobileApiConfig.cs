namespace ArgoBooks.Mobile.Services;

/// <summary>
/// Sync-server base URL for the mobile app. Mirrors ArgoBooks.Core.Services.ApiConfig's
/// sandbox/production split, duplicated here rather than referenced because ArgoBooks.Mobile
/// only references ArgoBooks.Shared (not ArgoBooks.Core - the desktop-only
/// dependency graph shouldn't be pulled into the Android head). The QR/pairing payload carries
/// no host (see SyncCrypto.BuildQrPayload), so the phone and desktop must be built against the
/// same environment for pairing to work - the same convention ApiConfig already documents.
/// </summary>
public static class MobileApiConfig
{
    private const string ProductionHost = "https://argorobots.com";
    private const string SandboxHost = "https://dev.argorobots.com";

#if DEBUG
    public const string BaseUrl = SandboxHost;
#else
    public const string BaseUrl = ProductionHost;
#endif
}
