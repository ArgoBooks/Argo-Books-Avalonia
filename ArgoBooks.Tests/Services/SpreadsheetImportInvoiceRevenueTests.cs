using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// An imported invoice gets a linked Revenue the moment it is issued, as sending one from the app
/// does. Only an invoice that has been paid used to get one, and recording a payment later updates
/// the revenue already there rather than creating it, so an invoice imported unpaid could be paid in
/// full and never count as income anywhere. A Draft or Cancelled invoice has not been issued and
/// still gets nothing.
/// </summary>
public class SpreadsheetImportInvoiceRevenueTests
{
    private static CompanyData Import(string status, int paid, int total = 100)
    {
        var data = new CompanyData();
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Invoices };
        chunk.Entities.Add(JsonDocument.Parse($$"""
            { "id": "INV-1", "customerId": "Acme", "issueDate": "2026-03-01", "dueDate": "2026-03-31",
              "total": {{total}}, "amountPaid": {{paid}}, "balance": {{total - paid}}, "status": "{{status}}" }
            """).RootElement.Clone());

        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], "Invoices");
        return data;
    }

    [Fact]
    public void AnUnpaidSentInvoice_GetsAnUnpaidRevenue()
    {
        var revenue = Assert.Single(Import("Sent", 0).Revenues);

        Assert.Equal("INV-1", revenue.InvoiceId);
        Assert.Equal(RevenuePaymentStatus.Unpaid, revenue.PaymentStatus);
    }

    [Theory]
    [InlineData("Sent", 50, RevenuePaymentStatus.Partial)]
    [InlineData("Paid", 100, RevenuePaymentStatus.Paid)]
    public void AnInvoiceWithPayments_GetsARevenueMatchingWhatIsPaid(
        string status, int paid, RevenuePaymentStatus expected)
    {
        Assert.Equal(expected, Assert.Single(Import(status, paid).Revenues).PaymentStatus);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Cancelled")]
    public void AnInvoiceThatWasNeverIssued_GetsNoRevenue(string status)
    {
        Assert.Empty(Import(status, 0).Revenues);
    }

    [Fact]
    public void ImportedUnpaidThenPaidInALaterImport_UpdatesTheRevenueInsteadOfAddingASecond()
    {
        var data = Import("Sent", 0);
        Assert.Equal(RevenuePaymentStatus.Unpaid, Assert.Single(data.Revenues).PaymentStatus);

        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Invoices };
        chunk.Entities.Add(JsonDocument.Parse("""
            { "id": "INV-1", "customerId": "Acme", "issueDate": "2026-03-01", "dueDate": "2026-03-31",
              "total": 100, "amountPaid": 100, "balance": 0, "status": "Paid" }
            """).RootElement.Clone());
        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], "Invoices");

        Assert.Equal(RevenuePaymentStatus.Paid, Assert.Single(data.Revenues).PaymentStatus);
    }
}
