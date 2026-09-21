using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Invoices;

namespace ArgoBooks.Core.Services.InvoiceTemplates;

/// <summary>
/// Keeps a document looking the way it was sent when the company logo changes.
///
/// A document stores a template id, not a picture, so it is drawn from the template every time it
/// is opened. A new logo would therefore redraw work the customer is already holding a copy of.
/// </summary>
public static class LogoHistory
{
    /// <summary>
    /// Call before replacing a template's logo. Documents already sent under the outgoing logo are
    /// stamped with it and keep it for good; unsent ones are left following the template, so they
    /// pick the new one up.
    /// </summary>
    public static void RetireLogo(CompanyData companyData, InvoiceTemplate template, string? incomingLogo)
    {
        var outgoing = template.LogoBase64;
        if (string.IsNullOrEmpty(outgoing) || outgoing == incomingLogo) return;

        var defaultId = companyData.InvoiceTemplates.FirstOrDefault(t => t.IsDefault)?.Id;

        // A document with no template of its own was drawn with whichever one is default.
        bool Uses(string templateId) =>
            templateId == template.Id || (string.IsNullOrEmpty(templateId) && template.Id == defaultId);

        var invoices = companyData.Invoices
            .Where(i => i.Status != InvoiceStatus.Draft && i.LogoId == null && Uses(i.TemplateId))
            .ToList();
        var quotes = companyData.Quotes
            .Where(q => q.SentAt.HasValue && q.LogoId == null && Uses(q.TemplateId))
            .ToList();

        if (invoices.Count == 0 && quotes.Count == 0) return;

        // The logo is set on every template at once, so the same image arrives here several times
        // over. It is kept once and shared, which is the point of storing an id on the document
        // rather than the picture itself.
        var kept = companyData.Settings.RetiredLogos.FirstOrDefault(l => l.Base64 == outgoing);
        if (kept == null)
        {
            kept = new RetiredLogo { Id = Guid.NewGuid().ToString("N"), Base64 = outgoing };
            companyData.Settings.RetiredLogos.Add(kept);
        }

        foreach (var invoice in invoices) invoice.LogoId = kept.Id;
        foreach (var quote in quotes) quote.LogoId = kept.Id;
    }

    /// <summary>
    /// The logo to draw a document with: the one it was sent under when it has one, otherwise
    /// whatever the template carries today.
    /// </summary>
    public static string? LogoFor(string? logoId, CompanySettings companySettings, InvoiceTemplate template)
    {
        if (!string.IsNullOrEmpty(logoId))
        {
            var kept = companySettings.RetiredLogos.FirstOrDefault(l => l.Id == logoId);
            if (kept != null) return kept.Base64;
        }

        return template.ShowLogo ? template.LogoBase64 : null;
    }
}
