using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services.InvoiceTemplates;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The invoice document marks itself overdue from Invoice.IsOverdue, like every other overdue check
/// (docs/Calculations.md §6). It used to check the due date and balance itself, so a cancelled
/// invoice past its due date went out stamped OVERDUE.
/// </summary>
public class InvoiceOverdueRenderTests
{
    [Theory]
    [InlineData(InvoiceStatus.Sent, true)]
    [InlineData(InvoiceStatus.Partial, true)]
    [InlineData(InvoiceStatus.Cancelled, false)]
    [InlineData(InvoiceStatus.Draft, false)]
    [InlineData(InvoiceStatus.Refunded, false)]
    public void PastDueWithABalance_ShowsOverdueOnlyWhenTheInvoiceIsOverdue(InvoiceStatus status, bool overdue)
    {
        var invoice = new Invoice
        {
            Id = "INV-1", InvoiceNumber = "#INV-1", IssueDate = DateTime.Today.AddDays(-40),
            DueDate = DateTime.Today.AddDays(-10), Total = 100m, Balance = 100m, Status = status
        };

        var html = new InvoiceHtmlRenderer().RenderInvoice(
            invoice, InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData());

        Assert.Equal(overdue, html.Contains("OVERDUE"));
    }

    [Fact]
    public void QuotePastItsValidUntilDate_NeverShowsOverdue()
    {
        var quote = new Quote
        {
            Id = "QUO-1", QuoteNumber = "#QUO-1", IssueDate = DateTime.Today.AddDays(-40),
            ValidUntil = DateTime.Today.AddDays(-10), Total = 100m
        };

        var html = new InvoiceHtmlRenderer().RenderQuote(
            quote, InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData());

        Assert.DoesNotContain("OVERDUE", html);
    }
}
