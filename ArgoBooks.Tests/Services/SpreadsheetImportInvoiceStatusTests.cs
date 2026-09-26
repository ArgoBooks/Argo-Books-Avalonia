using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// An imported invoice's status is worked out from its amounts the way a recorded payment works it
/// out (docs/Calculations.md §6). Overdue is never saved, and a sheet's Overdue used to become Sent
/// even when half of it had been paid. A Draft, Cancelled or refund status is taken as the sheet
/// gives it: a cancelled invoice with a payment used to become Partial, and then overdue.
/// </summary>
public class SpreadsheetImportInvoiceStatusTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var file in _files)
            File.Delete(file);
    }

    [Theory]
    [InlineData("Overdue", 0, InvoiceStatus.Sent)]
    [InlineData("Overdue", 50, InvoiceStatus.Partial)]
    [InlineData("Overdue", 100, InvoiceStatus.Paid)]
    [InlineData("Sent", 50, InvoiceStatus.Partial)]
    [InlineData("Paid", 50, InvoiceStatus.Partial)]
    [InlineData("Draft", 0, InvoiceStatus.Draft)]
    [InlineData("Cancelled", 0, InvoiceStatus.Cancelled)]
    [InlineData("Cancelled", 50, InvoiceStatus.Cancelled)]
    [InlineData("Draft", 50, InvoiceStatus.Draft)]
    [InlineData("Refunded", 100, InvoiceStatus.Refunded)]
    [InlineData("Refunded", 50, InvoiceStatus.Refunded)]
    [InlineData("PartiallyRefunded", 100, InvoiceStatus.PartiallyRefunded)]
    public void AiImport_StatusFollowsTheAmountPaid(string sheetStatus, int paid, InvoiceStatus expected)
    {
        var data = new CompanyData();
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Invoices };
        chunk.Entities.Add(JsonDocument.Parse($$"""
            { "id": "INV-1", "customerId": "Acme", "issueDate": "2026-03-01", "dueDate": "2026-03-31",
              "total": 100, "amountPaid": {{paid}}, "balance": {{100 - paid}}, "status": "{{sheetStatus}}" }
            """).RootElement.Clone());

        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], "Invoices");

        Assert.Equal(expected, Assert.Single(data.Invoices).Status);
    }

    // A status the app has no name for used to be read as a Draft the sheet chose, which kept a
    // fully paid invoice a Draft. Now the amounts decide, and common spellings are understood.
    [Theory]
    [InlineData("Unpaid", 100, InvoiceStatus.Paid)]
    [InlineData("Open", 50, InvoiceStatus.Partial)]
    [InlineData("Open", 0, InvoiceStatus.Draft)]
    [InlineData("Canceled", 50, InvoiceStatus.Cancelled)]
    [InlineData("Paid in full", 100, InvoiceStatus.Paid)]
    [InlineData("Partially paid", 50, InvoiceStatus.Partial)]
    [InlineData("partially-refunded", 100, InvoiceStatus.PartiallyRefunded)]
    public void AiImport_StatusNotOneOfOurs_IsLeftToTheAmounts(string sheetStatus, int paid, InvoiceStatus expected)
    {
        var data = new CompanyData();
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Invoices };
        chunk.Entities.Add(JsonDocument.Parse($$"""
            { "id": "INV-1", "customerId": "Acme", "issueDate": "2026-03-01", "dueDate": "2026-03-31",
              "total": 100, "amountPaid": {{paid}}, "balance": {{100 - paid}}, "status": "{{sheetStatus}}" }
            """).RootElement.Clone());

        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], "Invoices");

        Assert.Equal(expected, Assert.Single(data.Invoices).Status);
    }

    [Fact]
    public async Task SheetImport_StatusNotOneOfOurs_IsLeftToTheAmounts()
    {
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(InvoiceSheet(
            ["ID", "Invoice #", "Customer ID", "Issue Date", "Due Date", "Total", "Paid", "Status"],
            ["INV-1", "#INV-1", "CUS-001", "2026-03-01", "2099-03-31", "100", "100", "Open"],
            ["INV-2", "#INV-2", "CUS-001", "2026-03-01", "2099-03-31", "100", "50", "Canceled"]), data);

        Assert.Equal(InvoiceStatus.Paid, data.Invoices.Single(i => i.Id == "INV-1").Status);
        Assert.Equal(InvoiceStatus.Cancelled, data.Invoices.Single(i => i.Id == "INV-2").Status);
    }

    // A row marked paid with no amounts used to owe its whole total, so it showed in the Outstanding
    // card. A refunded one was paid before it was refunded, so it owes nothing either.
    [Theory]
    [InlineData("Paid", InvoiceStatus.Paid)]
    [InlineData("Refunded", InvoiceStatus.Refunded)]
    [InlineData("PartiallyRefunded", InvoiceStatus.PartiallyRefunded)]
    public void AiImport_MarkedPaidWithNoAmounts_IsPaidInFull(string sheetStatus, InvoiceStatus expected)
    {
        var data = new CompanyData();
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Invoices };
        chunk.Entities.Add(JsonDocument.Parse($$"""
            { "id": "INV-1", "customerId": "Acme", "issueDate": "2026-03-01", "dueDate": "2026-03-31",
              "total": 100, "status": "{{sheetStatus}}", "originalCurrency": "USD" }
            """).RootElement.Clone());

        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], "Invoices");

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal(expected, invoice.Status);
        Assert.Equal(100m, invoice.AmountPaid);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(0m, invoice.BalanceUSD);
    }

    [Fact]
    public async Task SheetImport_MarkedPaidWithNoAmountColumns_IsPaidInFull()
    {
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(InvoiceSheet(
            ["ID", "Invoice #", "Customer ID", "Issue Date", "Due Date", "Total", "Status"],
            ["INV-1", "#INV-1", "CUS-001", "2026-03-01", "2026-03-31", "100", "Paid"]), data);

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(100m, invoice.AmountPaid);
        Assert.Equal(0m, invoice.Balance);
    }

    // With no balance given, the AI import left it at 0, so an invoice half paid read as paid in full.
    [Fact]
    public void AiImport_NoBalance_OwesTheTotalLessWhatWasPaid()
    {
        var data = new CompanyData();
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Invoices };
        chunk.Entities.Add(JsonDocument.Parse("""
            { "id": "INV-1", "customerId": "Acme", "issueDate": "2026-03-01", "dueDate": "2026-03-31",
              "total": 100, "amountPaid": 50, "status": "Sent" }
            """).RootElement.Clone());

        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], "Invoices");

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal(50m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
    }

    [Fact]
    public async Task SheetImport_NoBalanceColumn_OwesTheTotalLessWhatWasPaid()
    {
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(InvoiceSheet(
            ["ID", "Invoice #", "Customer ID", "Issue Date", "Due Date", "Total", "Status"],
            ["INV-1", "#INV-1", "CUS-001", "2026-03-01", "2099-03-31", "100", "Sent"]), data);

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal(100m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    [Fact]
    public async Task SheetImport_CancelledWithAPayment_StaysCancelled_AndIsNotOverdue()
    {
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(InvoiceSheet(
            ["ID", "Invoice #", "Customer ID", "Issue Date", "Due Date", "Total", "Paid", "Status"],
            ["INV-1", "#INV-1", "CUS-001", "2026-03-01", "2026-03-31", "100", "50", "Cancelled"]), data);

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.False(invoice.IsOverdue);
    }

    [Fact]
    public async Task SheetImport_OverduePartlyPaid_IsPartial_AndUnpaidIsSent()
    {
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(InvoiceSheet(
            ["ID", "Invoice #", "Customer ID", "Issue Date", "Due Date", "Total", "Paid", "Status"],
            ["INV-1", "#INV-1", "CUS-001", "2026-03-01", "2026-03-31", "100", "50", "Overdue"],
            ["INV-2", "#INV-2", "CUS-001", "2026-03-01", "2099-03-31", "100", "0", "Overdue"]), data);

        Assert.Equal(InvoiceStatus.Partial, data.Invoices.Single(i => i.Id == "INV-1").Status);
        var unpaid = data.Invoices.Single(i => i.Id == "INV-2");
        Assert.Equal(InvoiceStatus.Sent, unpaid.Status);
        Assert.False(unpaid.IsOverdue);
    }

    [Fact]
    public async Task StatusOnlyUpdate_ToOverdue_IsWorkedOutFromTheStoredAmounts()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1", InvoiceNumber = "#INV-1", CustomerId = "CUS-001", IssueDate = new DateTime(2026, 3, 1),
            Total = 100m, TotalUSD = 100m, AmountPaid = 50m, Balance = 50m, Status = InvoiceStatus.Sent
        });

        await new SpreadsheetImportService().ImportFromExcelAsync(InvoiceSheet(["ID", "Status"], ["INV-1", "Overdue"]), data);

        Assert.Equal(InvoiceStatus.Partial, Assert.Single(data.Invoices).Status);
    }

    private string InvoiceSheet(params string[][] cells)
    {
        var path = Path.Combine(Path.GetTempPath(), $"argo-status-{Guid.NewGuid():N}.xlsx");
        _files.Add(path);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Invoices");
        for (var r = 0; r < cells.Length; r++)
            for (var c = 0; c < cells[r].Length; c++)
                sheet.Cell(r + 1, c + 1).Value = cells[r][c];
        workbook.SaveAs(path);
        return path;
    }
}
