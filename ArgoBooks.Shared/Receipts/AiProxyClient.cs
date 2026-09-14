using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ArgoBooks.Core.Services;

/// <summary>One call to the argorobots.com AI completion endpoint.</summary>
public sealed record AiProxyRequest
{
    /// <summary>The server meters usage on this tag (receipt_scan, receipt_verify, completion, ...).</summary>
    public required string Operation { get; init; }

    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public required string Model { get; init; }
    public double Temperature { get; init; }

    /// <summary>Left out of the body when null, so the server chooses the output budget.</summary>
    public int? MaxTokens { get; init; }

    public string? Base64Image { get; init; }
    public string? MimeType { get; init; }

    /// <summary>Size hint for the server's timing records; the upload size is used when null.</summary>
    public long? SizeFeature { get; init; }
}

/// <summary>Server-measured compute time and load factor, from the response's timing block.</summary>
public sealed record AiProxyTiming(double ServerMs, double? LoadFactor);

/// <summary>
/// What the proxy said. <see cref="Content"/> on success; otherwise the server's own
/// <see cref="Message"/> and <see cref="Code"/> when it gave them. Code is separate from Message
/// because callers show one and branch on the other.
/// </summary>
public sealed record AiProxyResponse(
    bool Succeeded,
    string? Content,
    string? Message,
    string? Code,
    HttpStatusCode StatusCode,
    bool IsHttpSuccess,
    double WallClockMs,
    long UploadBytes,
    AiProxyTiming? Timing);

/// <summary>
/// Posts requests to <c>{baseUrl}/api/ai/completions.php</c>. Shared by the desktop's
/// <c>GeminiService</c> and <see cref="GeminiReceiptScannerService"/> (desktop and mobile).
/// Transport errors, cancellation and a non-JSON 200 body propagate to the caller.
/// </summary>
public sealed class AiProxyClient(HttpClient httpClient, string baseUrl, IApiAuth? apiAuth)
{
    /// <summary>Platform tag sent with each AI call for the server-side timing records.</summary>
    public static readonly string PlatformTag =
        OperatingSystem.IsAndroid() ? "android"
        : OperatingSystem.IsIOS() ? "ios"
        : OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos"
        : OperatingSystem.IsLinux() ? "linux" : "other";

    public async Task<AiProxyResponse> SendAsync(AiProxyRequest request, CancellationToken cancellationToken = default)
    {
        long uploadBytes = request.Base64Image != null ? (long)(request.Base64Image.Length * 0.75) : 0;

        var body = new Dictionary<string, object?>
        {
            ["systemPrompt"] = request.SystemPrompt,
            ["userPrompt"] = request.UserPrompt,
            ["model"] = request.Model,
        };
        if (request.MaxTokens is { } maxTokens)
            body["maxTokens"] = maxTokens;
        body["temperature"] = request.Temperature;
        if (request.Base64Image != null)
        {
            body["base64Image"] = request.Base64Image;
            body["mimeType"] = request.MimeType;
        }
        body["operation"] = request.Operation;
        body["sizeFeature"] = request.SizeFeature ?? (uploadBytes > 0 ? uploadBytes : null);
        body["platform"] = PlatformTag;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/ai/completions.php");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        apiAuth?.AddAuthHeaders(httpRequest);

        var wallClock = Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        wallClock.Stop();
        double wallClockMs = wallClock.Elapsed.TotalMilliseconds;

        // The body is read on the failure path too: the server explains a refusal there
        // ("Monthly scan limit reached ..."), and dropping it made every failure look the same.
        if (!response.IsSuccessStatusCode)
        {
            (string? serverMessage, string? serverCode) = ReadServerError(responseBody);
            return new AiProxyResponse(false, null, serverMessage, serverCode, response.StatusCode, false,
                wallClockMs, uploadBytes, null);
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        AiProxyTiming? timing = null;
        if (root.TryGetProperty("timing", out var timingProp))
        {
            double serverMs = timingProp.TryGetProperty("elapsed_ms", out var e) && e.TryGetDouble(out var ev) ? ev : 0;
            double? loadFactor = timingProp.TryGetProperty("load_factor", out var lf) && lf.TryGetDouble(out var lv) ? lv : null;
            timing = new AiProxyTiming(serverMs, loadFactor);
        }

        if (root.TryGetProperty("success", out var successProp) && successProp.GetBoolean()
            && root.TryGetProperty("content", out var contentProp))
        {
            return new AiProxyResponse(true, contentProp.GetString(), null, null, response.StatusCode, true,
                wallClockMs, uploadBytes, timing);
        }

        // A 200 that still says no.
        (string? message, string? code) = ReadServerError(responseBody);
        return new AiProxyResponse(false, null, message, code, response.StatusCode, true,
            wallClockMs, uploadBytes, timing);
    }

    /// <summary>
    /// The server's message and error code, from a body that may not be JSON at all: a proxy or
    /// host error page arrives as HTML, and the error path must not throw on it.
    /// </summary>
    private static (string? Message, string? Code) ReadServerError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            string? code = root.TryGetProperty("errorCode", out var c) ? c.GetString() : null;

            return (string.IsNullOrWhiteSpace(message) ? null : message, code);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
