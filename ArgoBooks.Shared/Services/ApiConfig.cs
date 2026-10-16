namespace ArgoBooks.Core.Services;

/// <summary>
/// Centralized API configuration. Controls whether the app targets
/// production (argorobots.com) or the sandbox/dev environment (dev.argorobots.com).
///
/// Debug builds automatically use the sandbox environment.
/// Release builds always use production: users can never access sandbox mode.
/// It lives in Shared so the phone app is built against the same hosts, which pairing needs.
/// </summary>
public static class ApiConfig
{
    private const string ProductionHost = "https://argorobots.com";
    private const string SandboxHost = "https://dev.argorobots.com";

#if DEBUG
    public static bool IsSandbox => true;
    public const string BaseUrl = SandboxHost;
#else
    public static bool IsSandbox => false;
    public const string BaseUrl = ProductionHost;
#endif
}
