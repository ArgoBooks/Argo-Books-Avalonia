using System.Globalization;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services.InvoiceTemplates;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// An invoice reads the same whatever machine it was produced on, and in the currency it was
/// issued in.
///
/// Amounts were hardcoded to two decimals, so a yen invoice printed "5,000.00" for a currency that
/// has no subunit. Dates were formatted with no culture, so a French or German machine printed
/// French or German month names on a document whose every label is English.
/// </summary>
public class InvoiceRenderCurrencyAndDateTests
{
    private readonly InvoiceHtmlRenderer _renderer = new();

    private static Invoice Yen() => new()
    {
        Id = "INV-2026-00020",
        InvoiceNumber = "INV-2026-00020",
        CustomerId = "CUS-001",
        IssueDate = new DateTime(2026, 8, 14),
        DueDate = new DateTime(2026, 9, 13),
        OriginalCurrency = "JPY",
        LineItems = { new LineItem { Description = "Consulting", Quantity = 1m, UnitPrice = 5000m } },
        Subtotal = 5000m,
        Total = 5000m,
        Balance = 5000m,
    };

    [Fact]
    public void AZeroDecimalCurrency_PrintsNoDecimals()
    {
        string html = _renderer.RenderInvoice(
            Yen(), InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData(), "¥");

        // The symbol itself is HTML-encoded on the way in ("&#165;"), so the amount is what to check.
        Assert.Contains("5,000", html, StringComparison.Ordinal);
        Assert.DoesNotContain("5,000.00", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroDecimalCurrency_PrintsNoDecimalsInThePlainTextCopy()
    {
        string text = _renderer.RenderPlainText(
            Yen(), InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData(), "¥");

        Assert.Contains("¥5,000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("5,000.00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATwoDecimalCurrency_IsUnchanged()
    {
        var invoice = Yen();
        invoice.OriginalCurrency = "CAD";

        string html = _renderer.RenderInvoice(
            invoice, InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData());

        Assert.Contains("$5,000.00", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDates_AreEnglishOnANonEnglishMachine()
    {
        string html = InFrench(() => _renderer.RenderInvoice(
            Yen(), InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData(), "¥"));

        Assert.Contains("August 14, 2026", html, StringComparison.Ordinal);
        Assert.DoesNotContain("août", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDatesInThePlainTextCopy_AreEnglishOnANonEnglishMachine()
    {
        string text = InFrench(() => _renderer.RenderPlainText(
            Yen(), InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData(), "¥"));

        Assert.Contains("August 14, 2026", text, StringComparison.Ordinal);
        Assert.DoesNotContain("août", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The editable paper feeds its raw numbers to the browser, which parses them as JavaScript
    /// numbers. A comma decimal separator from the machine locale would be read as a whole number.
    /// </summary>
    [Fact]
    public void TheRawNumbersOnTheEditablePaper_UseADotOnANonEnglishMachine()
    {
        var invoice = Yen();
        invoice.ShippingAmount = 12.5m;
        invoice.TaxRate = 8.25m;

        string html = InFrench(() => _renderer.RenderInvoice(
            invoice, InvoiceTemplateFactory.CreateProfessionalTemplate(), new CompanyData(), "¥",
            editable: true));

        Assert.Contains("12.5", html, StringComparison.Ordinal);
        Assert.Contains("8.25", html, StringComparison.Ordinal);
        Assert.DoesNotContain("12,5", html, StringComparison.Ordinal);
        Assert.DoesNotContain("8,25", html, StringComparison.Ordinal);
    }

    private static T InFrench<T>(Func<T> render)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
