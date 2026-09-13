using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Invoices read by the AI importer are found by the payments that name them. A sheet from
/// another system usually has only an invoice number, and the importer gave such an invoice a
/// generated id instead of its number, so a payment naming "1001" found nothing and a blank
/// $0 invoice "1001" was created beside the real one.
/// </summary>
public class SpreadsheetImportAiInvoiceTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static void Import(SpreadsheetImportService svc, CompanyData data, SpreadsheetSheetType type, string sheet, JsonElement row)
    {
        var chunk = new LlmProcessedData { EntityType = type };
        chunk.Entities.Add(row);
        svc.ImportProcessedEntities(data, [chunk], sheet);
    }

    [Fact]
    public void AnInvoiceWithOnlyANumber_IsFoundByThePaymentThatNamesIt()
    {
        var data = new CompanyData();
        var svc = new SpreadsheetImportService();

        Import(svc, data, SpreadsheetSheetType.Invoices, "Invoices", Json("""
            { "invoiceNumber": "1001", "customerId": "Acme", "issueDate": "2026-03-01", "total": 500 }
            """));
        Import(svc, data, SpreadsheetSheetType.Payments, "Payments", Json("""
            { "id": "PAY-1", "invoiceId": "1001", "customerId": "Acme", "date": "2026-03-05", "amount": 500 }
            """));

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal("1001", invoice.Id);
        Assert.Equal(500m, invoice.Total);
        Assert.Equal("1001", Assert.Single(data.Payments).InvoiceId);
    }

    [Fact]
    public void APaymentNamingAnInvoiceByItsNumber_IsLinkedToThatInvoice()
    {
        var data = new CompanyData();
        var svc = new SpreadsheetImportService();

        Import(svc, data, SpreadsheetSheetType.Invoices, "Invoices", Json("""
            { "id": "INV-2026-00001", "invoiceNumber": "#INV-2026-00001", "customerId": "Acme", "issueDate": "2026-03-01", "total": 300 }
            """));
        Import(svc, data, SpreadsheetSheetType.Payments, "Payments", Json("""
            { "id": "PAY-2", "invoiceId": "#INV-2026-00001", "customerId": "Acme", "date": "2026-03-05", "amount": 300 }
            """));

        Assert.Equal("INV-2026-00001", Assert.Single(data.Invoices).Id);
        Assert.Equal("INV-2026-00001", Assert.Single(data.Payments).InvoiceId);
    }
}
