using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.InvoiceTemplates;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Nothing a customer reads off an invoice can be a negative number.
///
/// A line floors at zero (see <see cref="InvoiceLineDiscountRenderTests"/>), but the figures
/// underneath it did not: the subtotal was summed from the raw quantity x price less discount, and
/// the invoice-level discount was subtracted whole however small the subtotal was. A line of 1 x 100
/// with a 150 discount printed 0.00 on the line and -50.00 as the Subtotal and Total beneath it.
/// </summary>
public class InvoiceNegativeTotalRenderTests
{
    private readonly InvoiceHtmlRenderer _renderer = new();

    private string Html(Invoice invoice) =>
        _renderer.RenderInvoice(invoice, InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData());

    /// <summary>
    /// A stored total that went negative before this fix still must not reach the customer.
    /// </summary>
    [Fact]
    public void AnInvoiceStoredWithANegativeTotal_PrintsZero()
    {
        var invoice = new Invoice
        {
            Id = "INV-2026-00010",
            InvoiceNumber = "INV-2026-00010",
            CustomerId = "CUS-001",
            IssueDate = new DateTime(2026, 8, 14),
            LineItems = { new LineItem { Description = "Goodwill", Quantity = 1m, UnitPrice = 100m, Discount = 150m } },
            Subtotal = -50m,
            Total = -50m,
        };

        string html = Html(invoice);

        Assert.DoesNotContain("-50.00", html, StringComparison.Ordinal);
        Assert.Contains("$0.00", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// An invoice-level discount bigger than the subtotal is worth at most the subtotal. Printing the
    /// full 150 against a 100 subtotal is a total of -50 in the customer's head whatever the Total row says.
    /// </summary>
    [Fact]
    public void AnInvoiceDiscountBiggerThanTheSubtotal_IsCappedAtTheSubtotal()
    {
        var invoice = new Invoice
        {
            Id = "INV-2026-00011",
            InvoiceNumber = "INV-2026-00011",
            CustomerId = "CUS-001",
            IssueDate = new DateTime(2026, 8, 14),
            LineItems = { new LineItem { Description = "Consulting", Quantity = 1m, UnitPrice = 100m } },
            Subtotal = 100m,
            DiscountAmount = 150m,
            DiscountIsPercent = false,
            Total = 0m,
        };

        string html = Html(invoice);

        Assert.Contains("-$100.00", html, StringComparison.Ordinal);
        Assert.DoesNotContain("-$150.00", html, StringComparison.Ordinal);
    }

    /// <summary>A percentage over 100 is the same hole with a different keystroke.</summary>
    [Fact]
    public void APercentageDiscountOverAHundred_IsCappedAtTheSubtotal()
    {
        var invoice = new Invoice
        {
            Id = "INV-2026-00012",
            InvoiceNumber = "INV-2026-00012",
            CustomerId = "CUS-001",
            IssueDate = new DateTime(2026, 8, 14),
            LineItems = { new LineItem { Description = "Consulting", Quantity = 1m, UnitPrice = 100m } },
            Subtotal = 100m,
            DiscountAmount = 150m,
            DiscountIsPercent = true,
            Total = 0m,
        };

        Assert.DoesNotContain("-$150.00", Html(invoice), StringComparison.Ordinal);
    }

    /// <summary>The plain-text copy emailed as a fallback carries the same figures.</summary>
    [Fact]
    public void ThePlainTextCopy_DoesNotPrintANegativeTotal()
    {
        var invoice = new Invoice
        {
            Id = "INV-2026-00013",
            InvoiceNumber = "INV-2026-00013",
            CustomerId = "CUS-001",
            IssueDate = new DateTime(2026, 8, 14),
            LineItems = { new LineItem { Description = "Goodwill", Quantity = 1m, UnitPrice = 100m, Discount = 150m } },
            Subtotal = -50m,
            Total = -50m,
        };

        string text = _renderer.RenderPlainText(
            invoice, InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData());

        Assert.DoesNotContain("-50.00", text, StringComparison.Ordinal);
        Assert.Contains("TOTAL: $0.00", text, StringComparison.Ordinal);
        // The line itself is quantity x price less its discount here too, as it is in the HTML.
        Assert.Contains("= $0.00", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An older save took tax on a taxable base the oversized discount had pushed below zero, so it
    /// stored a negative tax. The Total under it prints 0.00, and the tax line above it must too.
    /// </summary>
    private static Invoice StoredWithANegativeTax() => new()
    {
        Id = "INV-2026-00014",
        InvoiceNumber = "INV-2026-00014",
        CustomerId = "CUS-001",
        IssueDate = new DateTime(2026, 8, 14),
        LineItems = { new LineItem { Description = "Consulting", Quantity = 1m, UnitPrice = 100m } },
        Subtotal = 100m,
        DiscountAmount = 150m,
        DiscountIsPercent = false,
        TaxRate = 10m,
        TaxAmount = -5m,
        Total = 0m,
    };

    [Fact]
    public void AnInvoiceStoredWithANegativeTax_PrintsZeroTax()
    {
        string html = Html(StoredWithANegativeTax());

        Assert.DoesNotContain("-5.00", html, StringComparison.Ordinal);
        Assert.Matches("data-total=\"tax\"[^>]*>\\$0\\.00<", html);
    }

    [Fact]
    public void ThePlainTextCopy_DoesNotPrintANegativeTax()
    {
        string text = _renderer.RenderPlainText(
            StoredWithANegativeTax(), InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData());

        Assert.DoesNotContain("-5.00", text, StringComparison.Ordinal);
        Assert.Contains("Tax (10%): $0.00", text, StringComparison.Ordinal);
    }

    /// <summary>The payment portal shows the customer the figures it is sent, so they can't go negative either.</summary>
    [Fact]
    public void ThePortalCopy_DoesNotCarryANegativeFigure()
    {
        var invoice = StoredWithANegativeTax();
        invoice.Subtotal = -50m;
        invoice.Total = -55m;
        invoice.Balance = -55m;
        var data = new CompanyData();

        var published = PaymentPortalService.BuildPublishRequest(invoice, data, new Customer { Id = "CUS-001", Name = "Bob" });
        var synced = PaymentPortalService.BuildBalanceSyncItem(invoice, data);

        Assert.Equal((0m, 0m, 0m, 0m), (published.Subtotal, published.TaxAmount, published.Total, published.Balance));
        Assert.Equal(0m, synced.TotalAmount);
    }
}
