using System.Net;
using System.Net.Sockets;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Best-effort favicon downloader. Used to give a supplier a default avatar derived
/// from its website when the user has not picked one. Direct GET against
/// <c>{scheme}://{host}/favicon.ico</c> on the supplier's own domain, no third-party
/// service is involved, which keeps the supplier list private and matches what the
/// user's browser already does for that site.
/// </summary>
public static class FaviconService
{
    private const int MaxFaviconBytes = 2 * 1024 * 1024; // 2 MB hard cap
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);

    private static readonly Lazy<HttpClient> _client = new(() =>
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        };
        var client = new HttpClient(handler) { Timeout = FetchTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ArgoBooks/1.0 (favicon fetcher)");
        return client;
    });

    /// <summary>
    /// Tries to download <c>/favicon.ico</c> for the host implied by <paramref name="websiteUrl"/>.
    /// Returns the raw bytes (which may be ICO/PNG/etc) or null on any failure.
    /// Always swallows exceptions: the caller treats null as "no favicon available".
    /// </summary>
    public static async Task<byte[]?> TryFetchFaviconAsync(string? websiteUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(websiteUrl))
            return null;

        var input = websiteUrl.Trim();
        if (!input.Contains("://", StringComparison.Ordinal))
            input = "https://" + input;

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return null;
        if (string.IsNullOrEmpty(uri.Host))
            return null;

        // Reject loopback, link-local, and private-network hosts so a hostile supplier URL
        // in a shared/imported .argo file can't probe the user's own LAN (SSRF defense).
        if (await IsRiskyHostAsync(uri, cancellationToken).ConfigureAwait(false))
            return null;

        var faviconUrl = $"{uri.Scheme}://{uri.Host}/favicon.ico";

        try
        {
            // ResponseHeadersRead returns as soon as the headers arrive.
            using var response = await _client.Value.GetAsync(faviconUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            // Reject obviously-non-image responses (e.g. an HTML 200 page when the
            // server doesn't have a real favicon and serves the index instead).
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType != null
                && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Reject upfront when the server is honest about an oversized payload.
            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength.HasValue && declaredLength.Value > MaxFaviconBytes)
                return null;

            // Stream into a bounded buffer; abort the moment we cross the cap so a
            // server lying about / omitting Content-Length can't force a huge alloc.
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream(capacity: declaredLength.HasValue ? (int)Math.Min(declaredLength.Value, MaxFaviconBytes) : 16 * 1024);
            var chunk = new byte[8192];
            while (true)
            {
                var read = await contentStream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read <= 0) break;
                if (buffer.Length + read > MaxFaviconBytes)
                    return null; // Server overran the cap mid-stream.
                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length == 0)
                return null;
            return buffer.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true if the host resolves to a loopback / link-local / private-network
    /// address. Such hosts must never be fetched, because a crafted supplier URL in a
    /// shared .argo file would otherwise let an attacker probe the user's local network
    /// (classic SSRF). DNS-failure is treated as risky (skip rather than try).
    /// </summary>
    private static async Task<bool> IsRiskyHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.IsLoopback)
            return true;

        IPAddress[] addresses;
        if (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
        {
            if (!IPAddress.TryParse(uri.Host, out var literal))
                return true;
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return true;
            }
        }

        foreach (var addr in addresses)
        {
            if (IsRiskyAddress(addr))
                return true;
        }
        return false;
    }

    private static bool IsRiskyAddress(IPAddress addr)
    {
        if (IPAddress.IsLoopback(addr))
            return true;

        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = addr.GetAddressBytes();
            return bytes[0] switch
            {
                0 => true,                                              // 0.0.0.0/8 (this network)
                10 => true,                                             // 10.0.0.0/8 (private)
                127 => true,                                            // 127.0.0.0/8 (loopback)
                169 when bytes[1] == 254 => true,                       // 169.254.0.0/16 (link-local)
                172 when bytes[1] >= 16 && bytes[1] <= 31 => true,      // 172.16.0.0/12 (private)
                192 when bytes[1] == 168 => true,                       // 192.168.0.0/16 (private)
                _ => false
            };
        }

        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (addr.IsIPv6LinkLocal || addr.IsIPv6SiteLocal)
                return true;
            if (addr.Equals(IPAddress.IPv6Any))
                return true;
            // Recursively unwrap IPv4-mapped form (::ffff:127.0.0.1) so callers can't
            // smuggle a private IPv4 through an IPv6 literal.
            if (addr.IsIPv4MappedToIPv6)
                return IsRiskyAddress(addr.MapToIPv4());
            // Unique local addresses fc00::/7
            var bytes = addr.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC)
                return true;
        }

        return false;
    }
}
