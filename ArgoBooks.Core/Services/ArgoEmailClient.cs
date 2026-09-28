using System.Net;
using System.Text;

namespace ArgoBooks.Core.Services;

/// <summary>What every send-email endpoint answers, whichever email it sent.</summary>
public interface IEmailApiResponse
{
    bool Success { get; set; }
    string Message { get; set; }
    string? ErrorCode { get; set; }
}

/// <summary>Which credential a send-email endpoint accepts.</summary>
public enum EmailAuth
{
    /// <summary>A Premium license key only (invoice email).</summary>
    LicenseKey,

    /// <summary>A license key, or the device ID on the free plan (purchase order and accountant email).</summary>
    LicenseKeyOrDevice
}

/// <summary>
/// Posts an email to one of the website's send-email endpoints and reads the answer, so the
/// invoice, purchase order and accountant pack emails fail in the same words for the same reason.
/// </summary>
public sealed class ArgoEmailClient : IDisposable
{
    public const string PremiumRequiredMessage = "Premium subscription required to send this email. Please activate your license key.";
    public const string NoIdentityMessage = "Sending email needs a license key or a registered device.";
    public const string CancelledMessage = "Request was cancelled.";
    public const string SentMessage = "Email sent successfully.";

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Func<(string? LicenseKey, string? DeviceId)> _identity;
    private readonly IConnectivityService? _connectivity;

    public ArgoEmailClient(TimeSpan timeout)
        : this(new HttpClient { Timeout = timeout })
    {
        _ownsHttpClient = true;
    }

    /// <summary>Creates a client over the given HttpClient; the identity and connectivity probe can be given for testing.</summary>
    public ArgoEmailClient(HttpClient httpClient, Func<(string? LicenseKey, string? DeviceId)>? identity = null,
        IConnectivityService? connectivity = null)
    {
        _httpClient = httpClient;
        _identity = identity ?? (() => (LicenseAuthHelper.GetLicenseKey(), LicenseAuthHelper.GetDeviceId()));
        _connectivity = connectivity;
    }

    /// <param name="statusMessage">
    /// The message for a status code the endpoint's own JSON never got to explain, such as the web
    /// server turning an upload away before the endpoint runs; null to use the general summary.
    /// </param>
    public async Task<TResponse> SendAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        EmailAuth auth,
        Func<HttpStatusCode, string?>? statusMessage = null,
        CancellationToken cancellationToken = default)
        where TResponse : IEmailApiResponse, new()
    {
        var (licenseKey, deviceId) = _identity();
        var hasKey = !string.IsNullOrEmpty(licenseKey);
        if (auth == EmailAuth.LicenseKey ? !hasKey : !hasKey && string.IsNullOrEmpty(deviceId))
        {
            return Failure<TResponse>(auth == EmailAuth.LicenseKey ? PremiumRequiredMessage : NoIdentityMessage, "NOT_CONFIGURED");
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(request, SerializeOptions), Encoding.UTF8, "application/json");
            LicenseAuthHelper.AddAuthHeaders(httpRequest, licenseKey, deviceId);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ReadAnswer<TResponse>(response, body, statusMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure<TResponse>(CancelledMessage, "CANCELLED");
        }
        catch (TaskCanceledException)
        {
            return Failure<TResponse>(await ResolveConnectivityAsync(), "TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return Failure<TResponse>(await ResolveConnectivityAsync(), "NETWORK_ERROR");
        }
        catch (Exception ex)
        {
            return Failure<TResponse>($"An error occurred: {ex.Message}", "UNKNOWN_ERROR");
        }
    }

    private static TResponse ReadAnswer<TResponse>(HttpResponseMessage response, string body, Func<HttpStatusCode, string?>? statusMessage)
        where TResponse : IEmailApiResponse, new()
    {
        try
        {
            if (JsonSerializer.Deserialize<TResponse>(body, DeserializeOptions) is { } answer)
                return answer;
        }
        catch (JsonException)
        {
            // Not JSON, for example a web server's error page. Summarized below.
        }

        if (response.IsSuccessStatusCode)
            return new TResponse { Success = true, Message = SentMessage };

        return Failure<TResponse>(
            statusMessage?.Invoke(response.StatusCode) ?? Summarize(response.StatusCode, body),
            ((int)response.StatusCode).ToString());
    }

    /// <summary>
    /// A short message for an answer that wasn't JSON. An HTML error page or a long body in the
    /// error banner is unreadable, so those come down to the status code.
    /// </summary>
    private static string Summarize(HttpStatusCode status, string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('<') || trimmed.Length > 300)
            return $"Server returned {(int)status} ({status}). Please try again later.";
        return $"Server returned {(int)status} ({status}): {trimmed}";
    }

    private Task<string> ResolveConnectivityAsync() => _connectivity is null
        ? ConnectivityMessage.ResolveAsync()
        : ConnectivityMessage.ResolveAsync(_connectivity);

    private static TResponse Failure<TResponse>(string message, string errorCode) where TResponse : IEmailApiResponse, new() =>
        new() { Success = false, Message = message, ErrorCode = errorCode };

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
