using System.Globalization;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Utilities;

namespace ArgoBooks.Core.Services.PurchaseOrders;

/// <summary>
/// Sends purchase order emails (with PDF attachment) through the website, which accepts a license
/// key or, on the free plan, the device ID.
/// </summary>
public class PurchaseOrderEmailService(ArgoEmailClient client) : IDisposable
{
    public PurchaseOrderEmailService() : this(new ArgoEmailClient(TimeSpan.FromSeconds(45)))
    {
    }

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
            PdfFilename = $"{SafeFileName.Create(order.PoNumber, "PurchaseOrder", replaceSpaces: true)}.pdf"
        };

        return await client.SendAsync<PurchaseOrderEmailRequest, PurchaseOrderEmailResponse>(
            PurchaseOrderEmailSettings.ApiEndpoint, request, EmailAuth.LicenseKeyOrDevice, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Fills subject/body templates with values from the order, supplier, and company settings.
    /// The total is written in the order's own currency.
    /// </summary>
    public static string FillTemplate(string template, PurchaseOrder order, CompanyData companyData)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var supplier = companyData.GetSupplier(order.SupplierId);
        return template
            .Replace("{PoNumber}", order.PoNumber)
            .Replace("{OrderId}", order.Id)
            .Replace("{CompanyName}", companyData.Settings.Company.Name)
            .Replace("{SupplierName}", supplier?.Name ?? string.Empty)
            .Replace("{OrderDate}", order.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Replace("{ExpectedDeliveryDate}", order.ExpectedDeliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Replace("{Total}", CurrencyInfo.FormatAmount(order.Total, order.OriginalCurrency));
    }

    public void Dispose()
    {
        client.Dispose();
        GC.SuppressFinalize(this);
    }
}
