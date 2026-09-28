using System.Net;

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

public class AccountantPackEmailResponse : IEmailApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Emails a year-end pack to the business's accountant through the website, which writes the
/// message itself. Like purchase order email it works on the free plan, with the device ID.
/// </summary>
public class AccountantPackEmailService(ArgoEmailClient client) : IDisposable
{
    private static string ApiEndpoint => $"{ApiConfig.BaseUrl}/api/accountant/send-email.php";

    // A pack can be several megabytes, so the upload gets far longer than a single invoice.
    public AccountantPackEmailService() : this(new ArgoEmailClient(TimeSpan.FromSeconds(120)))
    {
    }

    public Task<AccountantPackEmailResponse> SendAsync(AccountantPackEmailRequest request, CancellationToken cancellationToken = default) =>
        client.SendAsync<AccountantPackEmailRequest, AccountantPackEmailResponse>(
            ApiEndpoint, request, EmailAuth.LicenseKeyOrDevice, TooLargeMessage, cancellationToken);

    // The web server can turn an oversized upload away before the endpoint runs, with no JSON.
    internal static string? TooLargeMessage(HttpStatusCode status) =>
        status == HttpStatusCode.RequestEntityTooLarge ? "This pack is too large to email. Save it as a zip instead." : null;

    public void Dispose()
    {
        client.Dispose();
        GC.SuppressFinalize(this);
    }
}
