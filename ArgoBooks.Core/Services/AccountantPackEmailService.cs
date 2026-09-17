using System.Text;

namespace ArgoBooks.Core.Services;

public class AccountantPackAttachment
{
    public string Filename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    /// <summary>The file contents as base64.</summary>
    public string Data { get; set; } = string.Empty;
}

public class AccountantPackEmailRequest
{
    public string To { get; set; } = string.Empty;
    public string? ToName { get; set; }
    public string? ReplyTo { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string? Note { get; set; }

    /// <summary>True when the receipts were left out because the email would have been too large.</summary>
    public bool ReceiptsOmitted { get; set; }

    public List<AccountantPackAttachment> Attachments { get; set; } = [];
}

public class AccountantPackEmailResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Emails a year-end pack to the business's accountant through the website, which writes the
/// message itself. Mirrors PurchaseOrderEmailService, and like it works without a license.
/// </summary>
public class AccountantPackEmailService : IDisposable
{
    private static string ApiEndpoint => $"{ApiConfig.BaseUrl}/api/accountant/send-email.php";

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // A pack can be several megabytes, so the upload gets far longer than a single invoice.
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };
    private bool _disposed;

    public async Task<AccountantPackEmailResponse> SendAsync(AccountantPackEmailRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendRequestAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AccountantPackEmailResponse
            {
                Success = false,
                Message = await ConnectivityMessage.ResolveAsync(),
                ErrorCode = "TIMEOUT"
            };
        }
        catch (HttpRequestException)
        {
            return new AccountantPackEmailResponse
            {
                Success = false,
                Message = await ConnectivityMessage.ResolveAsync(),
                ErrorCode = "NETWORK_ERROR"
            };
        }
        catch (Exception ex)
        {
            return new AccountantPackEmailResponse
            {
                Success = false,
                Message = $"An error occurred: {ex.Message}",
                ErrorCode = "UNKNOWN_ERROR"
            };
        }
    }

    private async Task<AccountantPackEmailResponse> SendRequestAsync(AccountantPackEmailRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, SerializeOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        LicenseAuthHelper.AddAuthHeaders(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            var result = JsonSerializer.Deserialize<AccountantPackEmailResponse>(responseContent, DeserializeOptions);
            if (result != null)
                return result;
        }
        catch
        {
            // Not JSON, for example a web server error page. Summarized below.
        }

        if (response.IsSuccessStatusCode)
            return new AccountantPackEmailResponse { Success = true, Message = "Email sent successfully." };

        // The web server can turn away an oversized upload before the endpoint ever runs.
        var message = response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge
            ? "This pack is too large to email. Save it as a zip instead."
            : $"Server returned {(int)response.StatusCode} ({response.StatusCode}).";

        return new AccountantPackEmailResponse
        {
            Success = false,
            Message = message,
            ErrorCode = ((int)response.StatusCode).ToString()
        };
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing) _httpClient.Dispose();
        _disposed = true;
    }
}
