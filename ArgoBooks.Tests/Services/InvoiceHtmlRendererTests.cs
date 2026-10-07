using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services.InvoiceTemplates;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the InvoiceHtmlRenderer class.
/// </summary>
public class InvoiceHtmlRendererTests
{
    private readonly InvoiceHtmlRenderer _renderer = new();

    #region RenderPlainText Tests

    [Fact]
    public void RenderPlainText_WithInvoice_ReturnsNonEmptyString()
    {
        var invoice = new Invoice
        {
            Id = "INV-001",
            InvoiceNumber = "INV-001",
            CustomerId = "CUS-001",
            Total = 100.00m
        };
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();
        var companyData = new CompanyData();

        var result = _renderer.RenderPlainText(invoice, template, companyData);

        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void RenderPlainText_ContainsInvoiceNumber()
    {
        var invoice = new Invoice
        {
            Id = "INV-001",
            InvoiceNumber = "INV-001",
            CustomerId = "CUS-001",
            Total = 50.00m
        };
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();
        var companyData = new CompanyData();

        var result = _renderer.RenderPlainText(invoice, template, companyData);

        Assert.Contains("INV-001", result);
    }

    // The stored total includes shipping and the HTML shows a shipping row, so the plain-text totals block has to as well.
    [Fact]
    public void RenderPlainText_WithShipping_ShowsAShippingLine()
    {
        var invoice = new Invoice
        {
            Id = "INV-002",
            InvoiceNumber = "INV-002",
            CustomerId = "CUS-001",
            Subtotal = 100.00m,
            ShippingAmount = 20.00m,
            Total = 120.00m,
            LineItems = { new LineItem { Description = "Widget", Quantity = 1, UnitPrice = 100.00m } }
        };
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();
        var companyData = new CompanyData();

        var result = _renderer.RenderPlainText(invoice, template, companyData);

        // Otherwise the customer sees "Subtotal: $100.00 ... TOTAL: $120.00" with an unexplained $20.
        Assert.Contains("Shipping", result);
    }

    // A fixed-amount tax stores the dollar figure in TaxRate; the plain-text path unconditionally
    // prints "(TaxRate%)", labelling a $50 flat tax as "(50%)". Regression test for that bug.
    [Fact]
    public void RenderPlainText_WithFixedTax_DoesNotLabelItAsAPercentage()
    {
        var invoice = new Invoice
        {
            Id = "INV-003",
            InvoiceNumber = "INV-003",
            CustomerId = "CUS-001",
            Subtotal = 100.00m,
            TaxIsFixed = true,
            TaxRate = 50.00m,
            TaxAmount = 50.00m,
            Total = 150.00m,
            LineItems = { new LineItem { Description = "Widget", Quantity = 1, UnitPrice = 100.00m } }
        };
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();
        var companyData = new CompanyData();

        var result = _renderer.RenderPlainText(invoice, template, companyData);

        // A fixed tax must not be labelled with "%". This invoice has no percent discount/fee, so any
        // "%" in the output is the mislabelled fixed tax.
        Assert.DoesNotContain("%", result);
    }

    #endregion

    #region RenderInvoice Tests

    [Fact]
    public void RenderInvoice_ReturnsHtml()
    {
        var invoice = new Invoice
        {
            Id = "INV-001",
            InvoiceNumber = "INV-001",
            CustomerId = "CUS-001",
            Total = 100.00m
        };
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();
        var companyData = new CompanyData();

        var result = _renderer.RenderInvoice(invoice, template, companyData);

        Assert.False(string.IsNullOrEmpty(result));
        Assert.Contains("<", result); // Should contain HTML tags
    }

    #endregion

    #region RenderPreview Tests

    [Fact]
    public void RenderPreview_ReturnsHtml()
    {
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();
        var companySettings = new CompanySettings();

        var result = _renderer.RenderPreview(template, companySettings, false);

        Assert.False(string.IsNullOrEmpty(result));
        Assert.Contains("<", result);
    }

    #endregion
}
