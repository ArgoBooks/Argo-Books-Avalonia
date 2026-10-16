namespace ArgoBooks.Core.Services;

/// <summary>
/// Website pages the app links to. They are on the API's host, so a debug build stays on the
/// sandbox site, where pages that have not shipped to production yet already exist.
/// </summary>
public static class WebsiteUrls
{
    public const string WhatsNew = ApiConfig.BaseUrl + "/whats-new/";
    public const string ContactUs = ApiConfig.BaseUrl + "/contact-us/";
    public const string Pricing = ApiConfig.BaseUrl + "/pricing/";

    /// <summary>The setup steps rather than the marketing page, since someone in Settings has already decided to connect.</summary>
    public const string StripeGuide = ApiConfig.BaseUrl + "/documentation/pages/integrations/stripe-integration.php";

    /// <summary>The page's real path: these pages link their assets relatively, so a shorter alias loads unstyled.</summary>
    public const string ApiDocs = ApiConfig.BaseUrl + "/documentation/pages/api/overview.php";
}
