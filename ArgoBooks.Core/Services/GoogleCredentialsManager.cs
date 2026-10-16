using System.Diagnostics;
using System.Text;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Manages Google OAuth 2.0 credentials via the argorobots.com server proxy.
/// The server handles OAuth token storage and refresh.
/// Google Sheets is a free feature: authentication uses device ID.
/// </summary>
public static class GoogleCredentialsManager
{
    private static readonly string AuthEndpoint = $"{ApiConfig.BaseUrl}/api/google/auth.php";
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Initiates the Google OAuth flow by requesting an auth URL from the server.
    /// The user should be directed to open this URL in their browser.
    /// </summary>
    public static async Task<string?> InitiateAuthAsync(CancellationToken cancellationToken = default)
    {
        var requestBody = new { action = "initiate" };
        var json = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoint);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        AddAuthHeaders(request);

        var response = await SharedHttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("success", out var success) && success.GetBoolean()
            && root.TryGetProperty("authUrl", out var authUrl))
        {
            return authUrl.GetString();
        }

        return null;
    }

    /// <summary>
    /// Checks whether the user has completed Google OAuth and has valid tokens stored on the server.
    /// </summary>
    public static async Task<bool> CheckAuthStatusAsync(CancellationToken cancellationToken = default)
    {
        var requestBody = new { action = "status" };
        var json = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, AuthEndpoint);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        AddAuthHeaders(request);

        var response = await SharedHttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("success", out var success) && success.GetBoolean()
            && root.TryGetProperty("authenticated", out var authenticated))
        {
            return authenticated.GetBoolean();
        }

        return false;
    }

    /// <summary>
    /// Ensures the user is authenticated with Google. If not, initiates OAuth by opening
    /// the browser and polls for completion. Returns true if authenticated.
    /// </summary>
    public static async Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Already authenticated?
            if (await CheckAuthStatusAsync(cancellationToken))
                return true;

            // Initiate OAuth flow
            var authUrl = await InitiateAuthAsync(cancellationToken);
            if (string.IsNullOrEmpty(authUrl))
                return false;

            // Validate and open browser for user to authorize
            if (!Uri.TryCreate(authUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") ||
                !Platform.UrlHelper.SafeOpenUrl(authUrl))
                return false;

            // Poll for completion (up to 2 minutes, checking every 5 seconds)
            for (var i = 0; i < 24; i++)
            {
                await Task.Delay(5000, cancellationToken);
                if (await CheckAuthStatusAsync(cancellationToken))
                    return true;
            }

            return false;
        }
        catch (HttpRequestException)
        {
            // Offline / server unreachable: surface a clear message. Callers that catch
            // InvalidOperationException show this directly, without a raw-exception prefix.
            throw new InvalidOperationException(await ConnectivityMessage.ResolveAsync());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(await ConnectivityMessage.ResolveAsync());
        }
    }

    /// <summary>
    /// Adds device ID auth header for Google API requests (free feature).
    /// </summary>
    internal static void AddAuthHeaders(HttpRequestMessage request) =>
        LicenseAuthHelper.AddAuthHeaders(request, licenseKey: null, LicenseAuthHelper.GetDeviceId());
}
