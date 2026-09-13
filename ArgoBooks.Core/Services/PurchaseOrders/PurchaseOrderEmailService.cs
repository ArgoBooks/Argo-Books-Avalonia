using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Inventory;

namespace ArgoBooks.Core.Services.PurchaseOrders;

/// <summary>
/// Sends purchase order emails (with PDF attachment) via the licensed API.
/// Mirrors InvoiceEmailService.
/// </summary>
public class PurchaseOrderEmailService : IDisposable
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };
    private bool _disposed;

    /// <summary>
    /// Sends a purchase order email to the supplier with the PDF attached.
    /// </summary>
    public async Task<PurchaseOrderEmailResponse> SendAsync(
        PurchaseOrder order,
        CompanyData companyData,
        PurchaseOrderEmailSettings emailSettings,
        string recipientEmail,
        string subject,
        string body,
        string? cc,
        string? bcc,
        byte[] pdfBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return new PurchaseOrderEmailResponse
            {
                Success = false,
                Message = "Recipient email is required.",
                ErrorCode = "NO_EMAIL"
            };
        }

        var supplier = companyData.GetSupplier(order.SupplierId);
        var supplierName = supplier?.Name ?? string.Empty;

        var request = new PurchaseOrderEmailRequest
        {
            To = recipientEmail,
            ToName = supplierName,
            From = emailSettings.FromEmail,
            FromName = !string.IsNullOrWhiteSpace(emailSettings.FromName)
                ? emailSettings.FromName
                : companyData.Settings.Company.Name,
            ReplyTo = string.IsNullOrWhiteSpace(emailSettings.ReplyToEmail) ? null : emailSettings.ReplyToEmail,
            Cc = string.IsNullOrWhiteSpace(cc) ? null : cc,
            Bcc = !string.IsNullOrWhiteSpace(bcc)
                ? bcc
                : (string.IsNullOrWhiteSpace(emailSettings.BccEmail) ? null : emailSettings.BccEmail),
            Subject = subject,
            Text = body,
            PurchaseOrderId = order.Id,
            PdfAttachment = Convert.ToBase64String(pdfBytes),
            PdfFilename = $"{SanitizePoFilename(order.PoNumber)}.pdf"
        };

        try
        {
            return await SendRequestAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PurchaseOrderEmailResponse
            {
                Success = false,
                Message = await ConnectivityMessage.ResolveAsync(),
                ErrorCode = "TIMEOUT"
            };
        }
        catch (HttpRequestException)
        {
            return new PurchaseOrderEmailResponse
            {
                Success = false,
                Message = await ConnectivityMessage.ResolveAsync(),
                ErrorCode = "NETWORK_ERROR"
            };
        }
        catch (Exception ex)
        {
            return new PurchaseOrderEmailResponse
            {
                Success = false,
                Message = $"An error occurred: {ex.Message}",
                ErrorCode = "UNKNOWN_ERROR"
            };
        }
    }

    private async Task<PurchaseOrderEmailResponse> SendRequestAsync(
        PurchaseOrderEmailRequest request,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, SerializeOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, PurchaseOrderEmailSettings.ApiEndpoint);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        LicenseAuthHelper.AddAuthHeaders(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            try
            {
                var result = JsonSerializer.Deserialize<PurchaseOrderEmailResponse>(responseContent, DeserializeOptions);
                return result ?? new PurchaseOrderEmailResponse
                {
                    Success = true,
                    Message = "Email sent successfully.",
                    Timestamp = DateTime.UtcNow
                };
            }
            catch
            {
                return new PurchaseOrderEmailResponse
                {
                    Success = true,
                    Message = "Email sent successfully.",
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        try
        {
            var errorResult = JsonSerializer.Deserialize<PurchaseOrderEmailResponse>(responseContent, DeserializeOptions);
            if (errorResult != null) return errorResult;
        }
        catch
        {
            // Server didn't return JSON. Fall through to friendly status-code summary.
        }

        return new PurchaseOrderEmailResponse
        {
            Success = false,
            Message = SummarizeNonJsonError(response.StatusCode, responseContent),
            ErrorCode = ((int)response.StatusCode).ToString()
        };
    }

    /// <summary>
    /// Builds a short error message for cases where the server returned non-JSON (typically an
    /// HTML 404 page or generic error page). Dumping the full HTML into the UI error banner is
    /// unreadable, so we keep just a short hint and the status code.
    /// </summary>
    private static string SummarizeNonJsonError(System.Net.HttpStatusCode status, string body)
    {
        var looksLikeHtml = body.TrimStart().StartsWith('<');
        if (looksLikeHtml || body.Length > 300)
        {
            return $"Server returned {(int)status} ({status}). The endpoint may not be configured yet.";
        }
        return $"API error: {(int)status} - {body}";
    }

    /// <summary>
    /// Fills subject/body templates with values from the order, supplier, and company settings.
    /// </summary>
    public static string FillTemplate(
        string template,
        PurchaseOrder order,
        CompanyData companyData,
        string currencySymbol = "$")
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var supplier = companyData.GetSupplier(order.SupplierId);
        return template
            .Replace("{PoNumber}", order.PoNumber)
            .Replace("{OrderId}", order.Id)
            .Replace("{CompanyName}", companyData.Settings.Company.Name)
            .Replace("{SupplierName}", supplier?.Name ?? string.Empty)
            .Replace("{OrderDate}", order.OrderDate.ToString("yyyy-MM-dd"))
            .Replace("{ExpectedDeliveryDate}", order.ExpectedDeliveryDate.ToString("yyyy-MM-dd"))
            .Replace("{Total}", $"{currencySymbol}{order.Total:N2}");
    }

    private static string SanitizePoFilename(string poNumber)
    {
        if (string.IsNullOrWhiteSpace(poNumber)) return "PurchaseOrder";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(poNumber.Length);
        foreach (var ch in poNumber)
            sb.Append(invalid.Contains(ch) || ch == ' ' ? '-' : ch);
        var result = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "PurchaseOrder" : result;
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
