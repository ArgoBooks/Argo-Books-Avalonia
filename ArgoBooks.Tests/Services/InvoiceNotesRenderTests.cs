using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services.InvoiceTemplates;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The customer message on an invoice always reaches the customer.
///
/// It renders in the footer of the HTML invoice, unconditionally.
/// </summary>
public class InvoiceNotesRenderTests
{
    private const string Message = "Thank you for your business. Payment is due within 14 days.";

    private readonly InvoiceHtmlRenderer _renderer = new();

    private static Invoice WithNotes() => new()
    {
        Id = "INV-2026-00030",
        InvoiceNumber = "INV-2026-00030",
        CustomerId = "CUS-001",
        IssueDate = new DateTime(2026, 8, 14),
        LineItems = { new LineItem { Description = "Consulting", Quantity = 1m, UnitPrice = 100m } },
        Subtotal = 100m,
        Total = 100m,
        Notes = Message,
    };

    [Fact]
    public void TheCustomerMessage_RendersOnTheInvoice()
    {
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();

        string html = _renderer.RenderInvoice(WithNotes(), template, new CompanyData());

        Assert.Contains(Message, html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCustomerMessage_RendersOnThePlainTextCopyToo()
    {
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();

        string text = _renderer.RenderPlainText(WithNotes(), template, new CompanyData());

        Assert.Contains(Message, text, StringComparison.Ordinal);
    }
}
