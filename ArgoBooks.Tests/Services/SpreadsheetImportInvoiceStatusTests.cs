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
/// even when half of it had been paid.
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
    [InlineData("Refunded", 100, InvoiceStatus.Refunded)]
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
