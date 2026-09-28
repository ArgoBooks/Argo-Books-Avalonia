using System.Globalization;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services.InvoiceTemplates;

/// <summary>
/// Sends invoice emails through the website's invoice email endpoint, which needs a Premium
/// license key.
/// </summary>
public class InvoiceEmailService(ArgoEmailClient client) : IDisposable
{
    private readonly InvoiceHtmlRenderer _htmlRenderer = new();

    public InvoiceEmailService() : this(new ArgoEmailClient(TimeSpan.FromSeconds(30)))
    {
    }

    /// <summary>
    /// Sends an invoice email to the customer.
    /// </summary>
    /// <param name="invoice">The invoice to send.</param>
    /// <param name="template">The email template to use.</param>
    /// <param name="companyData">Company data for customer lookup and company info.</param>
    /// <param name="emailSettings">Email API settings.</param>
    /// <param name="currencySymbol">Currency symbol for formatting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The API response.</returns>
    public async Task<InvoiceEmailResponse> SendInvoiceAsync(
        Invoice invoice,
        InvoiceTemplate template,
        CompanyData companyData,
        InvoiceEmailSettings emailSettings,
        string currencySymbol = "$",
        CancellationToken cancellationToken = default)
    {
        var customer = companyData.GetCustomer(invoice.CustomerId);
        if (customer == null)
        {
            return new InvoiceEmailResponse
            {
                Success = false,
                Message = "Customer not found for this invoice.",
                ErrorCode = "CUSTOMER_NOT_FOUND"
            };
        }

        if (string.IsNullOrWhiteSpace(customer.Email))
        {
            return new InvoiceEmailResponse
            {
                Success = false,
                Message = $"Customer '{customer.Name}' does not have an email address.",
                ErrorCode = "NO_EMAIL"
            };
        }

        InvoiceEmailRequest request;
        try
        {
            request = new InvoiceEmailRequest
            {
                To = customer.Email,
                ToName = customer.Name,
                From = emailSettings.FromEmail,
                FromName = !string.IsNullOrWhiteSpace(emailSettings.FromName)
                    ? emailSettings.FromName
                    : companyData.Settings.Company.Name,
                ReplyTo = !string.IsNullOrWhiteSpace(emailSettings.ReplyToEmail)
                    ? emailSettings.ReplyToEmail
                    : null,
                Bcc = !string.IsNullOrWhiteSpace(emailSettings.BccEmail)
                    ? emailSettings.BccEmail
                    : null,
                Subject = BuildSubject(emailSettings.SubjectTemplate, invoice, companyData.Settings),
                Html = _htmlRenderer.RenderInvoice(invoice, template, companyData, currencySymbol),
                Text = _htmlRenderer.RenderPlainText(invoice, template, companyData, currencySymbol),
                InvoiceId = invoice.Id
            };
        }
        catch (Exception ex)
        {
            return new InvoiceEmailResponse
            {
                Success = false,
                Message = $"An error occurred: {ex.Message}",
                ErrorCode = "UNKNOWN_ERROR"
            };
        }

        return await client.SendAsync<InvoiceEmailRequest, InvoiceEmailResponse>(
            InvoiceEmailSettings.ApiEndpoint, request, EmailAuth.LicenseKey, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Renders invoice HTML for preview purposes.
    /// </summary>
    public string RenderInvoiceHtml(
        Invoice invoice,
        InvoiceTemplate template,
        CompanyData companyData,
        string currencySymbol = "$")
    {
        return _htmlRenderer.RenderInvoice(invoice, template, companyData, currencySymbol);
    }

    /// <summary>
    /// Renders a preview with sample data.
    /// </summary>
    public string RenderTemplatePreview(InvoiceTemplate template, CompanySettings companySettings, bool lockAspectRatio = true)
    {
        return _htmlRenderer.RenderPreview(template, companySettings, lockAspectRatio);
    }

    /// <summary>Fills the subject template. The total is written in the invoice's own currency.</summary>
    internal static string BuildSubject(string template, Invoice invoice, CompanySettings settings) =>
        template
            .Replace("{InvoiceNumber}", invoice.InvoiceNumber)
            .Replace("{InvoiceId}", invoice.Id)
            .Replace("{CompanyName}", settings.Company.Name)
            .Replace("{IssueDate}", invoice.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Replace("{DueDate}", invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Replace("{Total}", CurrencyInfo.FormatAmount(invoice.Total, invoice.OriginalCurrency));

    public void Dispose()
    {
        client.Dispose();
        GC.SuppressFinalize(this);
    }
}
