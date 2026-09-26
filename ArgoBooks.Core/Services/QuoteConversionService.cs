using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Turns an accepted quote into a draft invoice. This is the only place a quote reaches the books,
/// and it stops at a draft: no revenue row, no recurring schedule, no portal publish. Everything
/// that makes money real still happens when the user sends the invoice.
/// </summary>
public static class QuoteConversionService
{
    /// <summary>
    /// Builds the draft invoice for a quote without touching the company data beyond taking the
    /// next invoice id.
    /// </summary>
    public static Invoice BuildDraftInvoice(Quote quote, CompanyData companyData)
    {
        var ids = new IdGenerator(companyData);
        var invoice = new Invoice
        {
            Id = ids.NextInvoiceId(),
            InvoiceNumber = ids.NextInvoiceNumber(),
            CustomerId = quote.CustomerId,
            IssueDate = DateTime.Today,
            // The default a hand-written invoice gets, so a converted one isn't dated differently
            // from one typed out the same afternoon.
            DueDate = DateTime.Now.AddMonths(1),
            LineItems = [.. quote.LineItems.Select(CopyLineItem)],
            Subtotal = quote.Subtotal,
            TaxRate = quote.TaxRate,
            TaxAmount = quote.TaxAmount,
            TaxIsFixed = quote.TaxIsFixed,
            CustomFeeLabel = quote.CustomFeeLabel,
            CustomFeeAmount = quote.CustomFeeAmount,
            CustomFeeIsPercent = quote.CustomFeeIsPercent,
            DiscountAmount = quote.DiscountAmount,
            DiscountIsPercent = quote.DiscountIsPercent,
            ShippingAmount = quote.ShippingAmount,
            Total = quote.Total,
            Balance = quote.Total,
            TemplateId = quote.TemplateId,
            Notes = quote.Notes,
            OriginalCurrency = quote.OriginalCurrency,
            Status = InvoiceStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        invoice.History.Add(new InvoiceHistoryEntry
        {
            Action = "Created",
            Details = $"Created from quote {quote.Id}",
            Timestamp = DateTime.UtcNow
        });
        return invoice;
    }

    /// <summary>
    /// Adds the draft invoice to the books and links the quote to it. Returns the new invoice, or
    /// null when the quote was already converted.
    /// </summary>
    public static Invoice? Convert(Quote quote, CompanyData companyData)
    {
        if (quote.Status == QuoteStatus.Converted) return null;

        var invoice = BuildDraftInvoice(quote, companyData);
        UsdConversion.Apply(companyData, invoice, UsdConversion.CachedRate(invoice.OriginalCurrency, invoice.IssueDate));
        companyData.Invoices.Add(invoice);

        quote.Status = QuoteStatus.Converted;
        quote.ConvertedInvoiceId = invoice.Id;
        quote.UpdatedAt = DateTime.UtcNow;
        quote.History.Add(new InvoiceHistoryEntry
        {
            Action = "Converted",
            Details = $"Converted to invoice {invoice.Id}",
            Timestamp = DateTime.UtcNow
        });

        return invoice;
    }

    /// <summary>
    /// A deep copy, so editing the invoice's lines later can't rewrite the quote the customer was
    /// shown. Stock and costing fields are left off: the quote never moved any.
    /// </summary>
    private static LineItem CopyLineItem(LineItem source) => new()
    {
        ProductId = source.ProductId,
        Description = source.Description,
        Quantity = source.Quantity,
        UnitPrice = source.UnitPrice,
        TaxRate = source.TaxRate,
        Discount = source.Discount
    };
}
