using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for obtaining anonymous geographic location data.
/// </summary>
public class GeoLocationService : IGeoLocationService
{
    private const string IpApiCoUrl = "https://ipapi.co/json/";
    private const string IpInfoUrl = "https://ipinfo.io/json";
    private const string IpApiUrl = "http://ip-api.com/json/?fields=status,country,countryCode,region,city,timezone,proxy"; // HTTP-only fallback (free tier)
    private const string IpifyUrl = "https://api.ipify.org";
    private const string HashSalt = "ArgoBooks2024GeoHash"; // Salt for IP hashing
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(12);

    private readonly HttpClient _httpClient;
    private readonly IErrorLogger? _errorLogger;
    private readonly object _inFlightLock = new();

    private GeoLocationData? _cachedData;
    private DateTime _cacheTime = DateTime.MinValue;
    private Task<GeoLocationData>? _inFlightLookup;

    /// <summary>
    /// Initializes a new instance of the GeoLocationService.
    /// </summary>
    public GeoLocationService(HttpClient? httpClient = null, IErrorLogger? errorLogger = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _errorLogger = errorLogger;
    }

    /// <inheritdoc />
    public Task<GeoLocationData> GetLocationAsync(CancellationToken cancellationToken = default)
    {
        // Return cached data if still valid
        if (_cachedData != null && DateTime.UtcNow - _cacheTime < CacheDuration)
        {
            return Task.FromResult(_cachedData);
        }

        // Single-flight: if a lookup is already running, return its task instead of hitting the upstream APIs again.
        Task<GeoLocationData> lookupTask;
        lock (_inFlightLock)
        {
            if (_cachedData != null && DateTime.UtcNow - _cacheTime < CacheDuration)
            {
                return Task.FromResult(_cachedData);
            }
            if (_inFlightLookup != null)
            {
                lookupTask = _inFlightLookup;
            }
            else
            {
                // Run the shared lookup with CancellationToken.None so one caller cancelling their await cannot poison the result for every other caller waiting on the same in-flight task.
                _inFlightLookup = LookupAsync();
                lookupTask = _inFlightLookup;
            }
        }

        return lookupTask.WaitAsync(cancellationToken);
    }

    private async Task<GeoLocationData> LookupAsync()
    {
        try
        {
            var locationData = new GeoLocationData();

            // Try HTTPS APIs first, fall back to HTTP-only ip-api.com last
            var gotLocation = await TryIpApiCoAsync(locationData, CancellationToken.None) ||
                              await TryIpInfoAsync(locationData, CancellationToken.None) ||
                              await TryIpApiAsync(locationData, CancellationToken.None);

            // Get and hash IP address
            var ip = await GetIpAddressAsync(CancellationToken.None);
            if (!string.IsNullOrEmpty(ip))
            {
                locationData.HashedIp = HashIpAddress(ip);
            }

            // Only cache when we actually got location data.
            if (gotLocation)
            {
                _cachedData = locationData;
                _cacheTime = DateTime.UtcNow;
            }

            return locationData;
        }
        finally
        {
            lock (_inFlightLock)
            {
                _inFlightLookup = null;
            }
        }
    }

    private async Task<bool> TryIpApiAsync(GeoLocationData data, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<IpApiResponse>(IpApiUrl, cancellationToken);
            if (response?.Status == "success")
            {
                data.Country = response.Country ?? "Unknown";
                data.CountryCode = response.CountryCode ?? "Unknown";
                data.Region = response.Region ?? "Unknown";
                data.City = response.City ?? "Unknown";
                data.Timezone = response.Timezone ?? "Unknown";
                return true;
            }
        }
        catch (Exception ex)
        {
            _errorLogger?.LogDebug($"ip-api.com failed: {ex.Message}");
        }
        return false;
    }

    private async Task<bool> TryIpApiCoAsync(GeoLocationData data, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<IpApiCoResponse>(IpApiCoUrl, cancellationToken);
            if (response != null && !string.IsNullOrEmpty(response.CountryName))
            {
                data.Country = response.CountryName ?? "Unknown";
                data.CountryCode = response.CountryCode ?? "Unknown";
                data.Region = response.Region ?? "Unknown";
                data.City = response.City ?? "Unknown";
                data.Timezone = response.Timezone ?? "Unknown";
                return true;
            }
        }
        catch (Exception ex)
        {
            _errorLogger?.LogDebug($"ipapi.co failed: {ex.Message}");
        }
        return false;
    }

    private async Task<bool> TryIpInfoAsync(GeoLocationData data, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<IpInfoResponse>(IpInfoUrl, cancellationToken);
            if (response != null && !string.IsNullOrEmpty(response.Country))
            {
                data.Country = "Unknown"; // ipinfo.io only provides country code, not full name
                data.CountryCode = response.Country ?? "Unknown";
                data.Region = response.Region ?? "Unknown";
                data.City = response.City ?? "Unknown";
                data.Timezone = response.Timezone ?? "Unknown";
                return true;
            }
        }
        catch (Exception ex)
        {
            _errorLogger?.LogDebug($"ipinfo.io failed: {ex.Message}");
        }
        return false;
    }

    private async Task<string?> GetIpAddressAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(IpifyUrl, cancellationToken);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _errorLogger?.LogDebug($"Failed to get IP address: {ex.Message}");
            return null;
        }
    }

    private static string HashIpAddress(string ipAddress)
    {
        var input = $"{HashSalt}{ipAddress}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return hash[..16]; // Truncate to 16 characters
    }

    #region API Response Models

    private class IpApiResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("countryCode")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
    }

    private class IpApiCoResponse
    {
        [JsonPropertyName("country_name")]
        public string? CountryName { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
    }

    private class IpInfoResponse
    {
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
    }

    #endregion
}

/// <summary>
/// Interface for geolocation services.
/// </summary>
public interface IGeoLocationService
{
    /// <summary>
    /// Gets anonymous geographic location data based on IP address.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Geographic location data.</returns>
    Task<GeoLocationData> GetLocationAsync(CancellationToken cancellationToken = default);
}
