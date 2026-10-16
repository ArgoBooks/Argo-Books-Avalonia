using System.Text.RegularExpressions;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services.InvoiceTemplates;

/// <summary>
/// Service for previewing invoice HTML in the system browser.
/// </summary>
public static class InvoicePreviewService
{
    private static readonly string TempDirectory = Path.Combine(AppPaths.Temp, "InvoicePreviews");

    /// <summary>
    /// Opens the invoice HTML in the default browser for preview.
    /// </summary>
    /// <param name="html">The HTML content to preview.</param>
    /// <param name="invoiceId">Optional invoice ID for the filename.</param>
    /// <param name="openBrowser">Whether to open the file in the default browser. Defaults to true.</param>
    /// <returns>True if the file was written (and browser opened, if requested) successfully.</returns>
    public static async Task<bool> PreviewInBrowserAsync(string html, string? invoiceId = null, bool openBrowser = true)
    {
        try
        {
            // Ensure the temp directory exists
            Directory.CreateDirectory(TempDirectory);

            // Sanitize the invoiceId to prevent path traversal
            var safeId = Regex.Replace(invoiceId ?? "", @"[^a-zA-Z0-9\-_]", "");
            if (string.IsNullOrEmpty(safeId))
                safeId = Guid.NewGuid().ToString("N");

            // Generate a filename
            var filename = $"invoice-preview-{safeId}.html";
            var filePath = Path.Combine(TempDirectory, filename);

            await File.WriteAllTextAsync(filePath, html);

            // Open in default browser
            if (openBrowser)
                UrlHelper.SafeOpenUrl(new Uri(filePath).AbsoluteUri);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return false;
        }
    }
}
