using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// What the desktop tells the payment portal an invoice has been paid outside it. The server
/// subtracts this from the balance it chases with reminder emails, so a figure that is too low
/// chases a customer who already paid, and one that is too high stops chasing one who has not.
/// </summary>
public class PortalBalanceSyncItemTests
{
    private static (CompanyData company, Invoice invoice) Seed(string currency = "USD")
    {
        var invoice = new Invoice
        {
            Id = "INV-001",
            InvoiceNumber = "INV-2026-001",
            CustomerId = "CUS-001",
            Total = 100m,
            OriginalCurrency = currency,
            Status = InvoiceStatus.Sent,
            DueDate = new DateTime(2026, 11, 5),
        };
        var company = new CompanyData();
        company.Invoices.Add(invoice);
        return (company, invoice);
    }

    private static Payment Pay(string invoiceId, decimal amount, PaymentSource source = PaymentSource.Manual,
        string currency = "USD", bool isRefund = false) => new()
    {
        Id = $"PAY-{Guid.NewGuid():N}",
        InvoiceId = invoiceId,
        Amount = amount,
        Source = source,
        OriginalCurrency = currency,
        IsRefund = isRefund,
    };

    /// <summary>
    /// Cash, cheque and bank transfers recorded on this device are the only payments the server
    /// cannot see, so every one of them on the invoice adds up into the amount paid outside the portal.
    /// </summary>
    [Fact]
    public void BuildBalanceSyncItem_ManualPaymentsInTheInvoiceCurrency_AddUpAsPaidOutsideThePortal()
    {
        var (company, invoice) = Seed();
        company.Payments.Add(Pay(invoice.Id, 30m));
        company.Payments.Add(Pay(invoice.Id, 20m));

        var item = PaymentPortalService.BuildBalanceSyncItem(invoice, company);

        Assert.Equal(50m, item.ExternalPaid);
        Assert.Equal(100m, item.TotalAmount);
        Assert.Equal(invoice.Id, item.InvoiceId);
    }

    /// <summary>
    /// The server recorded a portal payment itself. Sending it back as paid outside the portal would
    /// subtract the same money twice and stop chasing a customer who still owes the rest.
    /// </summary>
    [Fact]
    public void BuildBalanceSyncItem_PortalPayment_IsLeftOut()
    {
        var (company, invoice) = Seed();
        company.Payments.Add(Pay(invoice.Id, 40m, PaymentSource.Online));
        company.Payments.Add(Pay(invoice.Id, 25m));

        var item = PaymentPortalService.BuildBalanceSyncItem(invoice, company);

        Assert.Equal(25m, item.ExternalPaid);
    }

    /// <summary>
    /// A refund gives money back for something the customer no longer owes for, so it must not turn
    /// into a balance the portal starts chasing again. Manual and portal refunds are both left out.
    /// </summary>
    [Fact]
    public void BuildBalanceSyncItem_Refunds_AreLeftOut()
    {
        var (company, invoice) = Seed();
        company.Payments.Add(Pay(invoice.Id, 100m));
        company.Payments.Add(Pay(invoice.Id, -40m, isRefund: true));
        company.Payments.Add(Pay(invoice.Id, -10m, PaymentSource.Online, isRefund: true));

        var item = PaymentPortalService.BuildBalanceSyncItem(invoice, company);

        Assert.Equal(100m, item.ExternalPaid);
    }

    /// <summary>
    /// The server compares this figure with the invoice total in the invoice's own currency. Adding
    /// 50 US dollars to a balance kept in Canadian dollars would be a meaningless number, so a payment
    /// in another currency is left out, as it is when the invoice's own totals are worked out.
    /// </summary>
    [Fact]
    public void BuildBalanceSyncItem_PaymentInAnotherCurrency_IsLeftOut()
    {
        var (company, invoice) = Seed("CAD");
        company.Payments.Add(Pay(invoice.Id, 50m, currency: "USD"));
        company.Payments.Add(Pay(invoice.Id, 30m, currency: "CAD"));

        var item = PaymentPortalService.BuildBalanceSyncItem(invoice, company);

        Assert.Equal(30m, item.ExternalPaid);
        Assert.Equal("CAD", item.Currency);
    }

    /// <summary>
    /// Rows saved before invoices had a currency carry a blank code and are US dollars, and a code can
    /// be stored in any case. Neither may drop a real payment out of the amount paid.
    /// </summary>
    [Theory]
    [InlineData("", "USD", "USD")]
    [InlineData("USD", "", "USD")]
    [InlineData("  ", "usd", "USD")]
    [InlineData("CAD", "cad", "CAD")]
    public void BuildBalanceSyncItem_BlankOrDifferentlyCasedCurrency_StillMatches(
        string invoiceCurrency, string paymentCurrency, string expectedCurrency)
    {
        var (company, invoice) = Seed(invoiceCurrency);
        company.Payments.Add(Pay(invoice.Id, 60m, currency: paymentCurrency));

        var item = PaymentPortalService.BuildBalanceSyncItem(invoice, company);

        Assert.Equal(60m, item.ExternalPaid);
        Assert.Equal(expectedCurrency, item.Currency);
    }

    /// <summary>A payment recorded against a different invoice says nothing about this one.</summary>
    [Fact]
    public void BuildBalanceSyncItem_PaymentOnAnotherInvoice_IsLeftOut()
    {
        var (company, invoice) = Seed();
        company.Payments.Add(Pay("INV-002", 80m));
        company.Payments.Add(Pay(invoice.Id, 15m));

        var item = PaymentPortalService.BuildBalanceSyncItem(invoice, company);

        Assert.Equal(15m, item.ExternalPaid);
    }

    /// <summary>
    /// The server stops reminders for a cancelled invoice and schedules them from the due date, so
    /// both must reach it exactly. The date is sent in one fixed format whatever the machine's
    /// language, or the server would misread it.
    /// </summary>
    [Fact]
    public void BuildBalanceSyncItem_CancelledInvoice_IsSentAsCancelledWithAnIsoDueDate()
    {
        var (company, invoice) = Seed();
        invoice.Status = InvoiceStatus.Cancelled;

        var item = PaymentPortalService.BuildBalanceSyncItem(invoice, company);

        Assert.True(item.Cancelled);
        Assert.Equal("2026-11-05", item.DueDate);
    }
}
