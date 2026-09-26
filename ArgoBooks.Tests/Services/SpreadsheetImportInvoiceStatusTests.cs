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
/// even when half of it had been paid. A Cancelled or refund status is taken as the sheet gives it: a
/// cancelled invoice with a payment used to become Partial, and then overdue. A Draft moves on with
/// its amount paid, as it does at its first payment in the app.
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
    [InlineData("Draft", 50, InvoiceStatus.Partial)]
    [InlineData("Draft", 100, InvoiceStatus.Paid)]
    [InlineData("Refunded", 100, InvoiceStatus.Refunded)]
    [InlineData("Refunded", 50, InvoiceStatus.Refunded)]
    [InlineData("PartiallyRefunded", 100, InvoiceStatus.PartiallyRefunded)]
    [InlineData("Paid", 0, InvoiceStatus.Sent)]
    [InlineData("Paid in full", 0, InvoiceStatus.Sent)]
    [InlineData("Partial", 100, InvoiceStatus.Paid)]
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
    [InlineData("Void", 50, InvoiceStatus.Cancelled)]
    [InlineData("Voided", 0, InvoiceStatus.Cancelled)]
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

    // An update sheet with a Status column read a blank or unrecognised status as Draft, which
    // turned a sent invoice back into a draft.
    [Fact]
    public async Task UpdateSheet_BlankOrUnrecognisedStatus_KeepsTheInvoicesOwn()
    {
        var data = new CompanyData();
        foreach (var id in new[] { "INV-1", "INV-2" })
        {
            data.Invoices.Add(new Invoice
            {
                Id = id, InvoiceNumber = "#" + id, CustomerId = "CUS-001", IssueDate = new DateTime(2026, 3, 1),
                DueDate = new DateTime(2099, 3, 31), Total = 100m, TotalUSD = 100m, Balance = 100m, Status = InvoiceStatus.Sent
            });
        }

        await new SpreadsheetImportService().ImportFromExcelAsync(
            InvoiceSheet(["ID", "Status"], ["INV-1", ""], ["INV-2", "Open"]), data);

        Assert.All(data.Invoices, i => Assert.Equal(InvoiceStatus.Sent, i.Status));
    }

    // Marking an invoice paid with no amounts took it as paid in full even when its recorded
    // payments came to less, so its totals no longer agreed with its payments.
    [Fact]
    public async Task UpdateSheet_MarkedPaidWithNoAmounts_TakesTheAmountsFromItsPayments()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1", InvoiceNumber = "#INV-1", CustomerId = "CUS-001", IssueDate = new DateTime(2026, 3, 1),
            DueDate = new DateTime(2099, 3, 31), Total = 100m, TotalUSD = 100m, AmountPaid = 40m, Balance = 60m,
            Status = InvoiceStatus.Partial, OriginalCurrency = "USD"
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", Date = new DateTime(2026, 3, 5), Amount = 40m, AmountUSD = 40m,
            OriginalCurrency = "USD"
        });

        await new SpreadsheetImportService().ImportFromExcelAsync(
            InvoiceSheet(["ID", "Total", "Status"], ["INV-1", "100", "Paid"]), data);

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal((40m, 60m, InvoiceStatus.Partial), (invoice.AmountPaid, invoice.Balance, invoice.Status));
    }

    // A sheet marking an invoice paid while its amounts say nothing was paid kept it Paid with its
    // whole balance owing, since working the status out never took a Paid invoice back.
    [Fact]
    public async Task SheetImport_MarkedPaidWithNothingPaid_IsOwed()
    {
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(InvoiceSheet(
            ["ID", "Invoice #", "Customer ID", "Issue Date", "Due Date", "Total", "Paid", "Status"],
            ["INV-1", "#INV-1", "CUS-001", "2026-03-01", "2099-03-31", "100", "0", "Paid in full"]), data);

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal((InvoiceStatus.Sent, 100m), (invoice.Status, invoice.Balance));
    }

    // An existing invoice's own Cancelled status is kept like one the sheet gives: an update with
    // only a paid amount used to make it Partial, and so overdue.
    [Fact]
    public async Task UpdateSheet_CancelledInvoiceGivenAPaidAmount_StaysCancelled()
    {
        var data = new CompanyData();
        data.Invoices.Add(CancelledInvoice());

        await new SpreadsheetImportService().ImportFromExcelAsync(InvoiceSheet(["ID", "Paid"], ["INV-1", "50"]), data);

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal((InvoiceStatus.Cancelled, 50m, 50m), (invoice.Status, invoice.AmountPaid, invoice.Balance));
        Assert.False(invoice.IsOverdue);
    }

    [Fact]
    public void AiUpdate_CancelledInvoiceGivenAPaidAmount_StaysCancelled()
    {
        var data = new CompanyData();
        data.Invoices.Add(CancelledInvoice());

        ImportAi(data, """{ "id": "INV-1", "amountPaid": 50 }""");

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal((InvoiceStatus.Cancelled, 50m, 50m), (invoice.Status, invoice.AmountPaid, invoice.Balance));
    }

    // An existing Draft was kept a Draft whatever the sheet said was paid, while the import still
    // booked it a paid revenue. It now moves on as a Draft does at its first payment.
    [Fact]
    public async Task UpdateSheet_DraftInvoiceGivenAPaidAmount_MovesOnLikeAtItsFirstPayment()
    {
        var data = new CompanyData();
        data.Invoices.Add(DraftInvoice());

        await new SpreadsheetImportService().ImportFromExcelAsync(InvoiceSheet(["ID", "Paid"], ["INV-1", "100"]), data);

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal((InvoiceStatus.Paid, InvoiceStatus.Draft), (invoice.Status, invoice.StatusBeforePayment));
        Assert.Equal(RevenuePaymentStatus.Paid, Assert.Single(data.Revenues).PaymentStatus);
    }

    [Fact]
    public void AiUpdate_DraftInvoiceGivenAPaidAmount_MovesOnLikeAtItsFirstPayment()
    {
        var data = new CompanyData();
        data.Invoices.Add(DraftInvoice());

        ImportAi(data, """{ "id": "INV-1", "amountPaid": 40 }""");

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal((InvoiceStatus.Partial, 60m), (invoice.Status, invoice.Balance));
    }

    // The AI import replaced an existing invoice with the row, so a row giving only its notes left
    // it a Draft with nothing paid and nothing owed. It now changes only what the row gives, like
    // the column import.
    [Fact]
    public void AiUpdate_ChangesOnlyWhatTheRowGives()
    {
        var data = new CompanyData();
        var existing = new Invoice
        {
            Id = "INV-1", InvoiceNumber = "#INV-1", CustomerId = "CUS-001", IssueDate = new DateTime(2026, 3, 1),
            DueDate = new DateTime(2099, 3, 31), Total = 100m, TotalUSD = 100m, AmountPaid = 40m, Balance = 60m,
            BalanceUSD = 60m, Status = InvoiceStatus.Partial, StatusBeforePayment = InvoiceStatus.Sent,
            Notes = "Old", OriginalCurrency = "USD"
        };
        data.Invoices.Add(existing);

        ImportAi(data, """{ "id": "INV-1", "notes": "New" }""");

        var invoice = Assert.Single(data.Invoices);
        Assert.Same(existing, invoice);
        Assert.Equal("New", invoice.Notes);
        Assert.Equal((InvoiceStatus.Partial, 100m, 40m, 60m), (invoice.Status, invoice.Total, invoice.AmountPaid, invoice.Balance));
        Assert.Equal((100m, 60m), (invoice.TotalUSD, invoice.BalanceUSD));
        Assert.Equal("CUS-001", invoice.CustomerId);
    }

    [Fact]
    public void AiUpdate_GivingANewTotal_OwesItLessWhatWasPaid()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1", InvoiceNumber = "#INV-1", CustomerId = "CUS-001", IssueDate = new DateTime(2026, 3, 1),
            DueDate = new DateTime(2099, 3, 31), Total = 100m, TotalUSD = 100m, AmountPaid = 40m, Balance = 60m,
            Status = InvoiceStatus.Partial, OriginalCurrency = "USD"
        });

        ImportAi(data, """{ "id": "INV-1", "total": 40 }""");

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal((InvoiceStatus.Paid, 40m, 0m), (invoice.Status, invoice.AmountPaid, invoice.Balance));
    }

    [Fact]
    public void AiUpdate_OfOtherRecords_KeepsWhatTheRowDoesNotGive()
    {
        var data = new CompanyData();
        data.Customers.Add(new ArgoBooks.Core.Models.Entities.Customer { Id = "CUS-001", Name = "Acme", Email = "a@acme.test", Phone = "555" });
        data.Expenses.Add(new Expense
        {
            Id = "PUR-1", Date = new DateTime(2026, 3, 1), Description = "Paper", Total = 30m, TotalUSD = 30m,
            Amount = 30m, Quantity = 3, UnitPrice = 10m, OriginalCurrency = "USD", Notes = "Old"
        });

        ImportAi(data, """{ "id": "CUS-001", "phone": "777" }""", SpreadsheetSheetType.Customers);
        ImportAi(data, """{ "id": "PUR-1", "notes": "New" }""", SpreadsheetSheetType.Expenses);

        var customer = Assert.Single(data.Customers);
        Assert.Equal(("Acme", "a@acme.test", "777"), (customer.Name, customer.Email, customer.Phone));
        var expense = Assert.Single(data.Expenses);
        Assert.Equal(("Paper", 30m, 30m, "New"), (expense.Description, expense.Total, expense.TotalUSD, expense.Notes));
        Assert.Empty(data.Products);
    }

    // The balance was worked out from the invoice's payments before its currency was set, so a new
    // euro invoice matched none of its euro payments and was left Paid with its whole total owing.
    [Fact]
    public void AiImport_PaidInvoiceInItsOwnCurrency_TakesItsAmountsFromItsPayments()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "EUR";
        data.Payments.Add(new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", Date = new DateTime(2026, 3, 5), Amount = 40m, OriginalCurrency = "EUR"
        });

        ImportAi(data, """
            { "id": "INV-1", "customerId": "Acme", "issueDate": "2026-03-01", "dueDate": "2099-03-31",
              "total": 100, "status": "Paid", "originalCurrency": "EUR" }
            """);

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal("EUR", invoice.OriginalCurrency);
        Assert.Equal((InvoiceStatus.Partial, 40m, 60m), (invoice.Status, invoice.AmountPaid, invoice.Balance));
    }

    [Fact]
    public async Task SheetImport_PaidInvoiceInTheCompanysCurrency_TakesItsAmountsFromItsPayments()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "EUR";
        data.Payments.Add(new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", Date = new DateTime(2026, 3, 5), Amount = 40m, OriginalCurrency = "EUR"
        });

        await new SpreadsheetImportService().ImportFromExcelAsync(InvoiceSheet(
            ["ID", "Invoice #", "Customer ID", "Issue Date", "Due Date", "Total", "Status"],
            ["INV-1", "#INV-1", "CUS-001", "2026-03-01", "2099-03-31", "100", "Paid"]), data);

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal((InvoiceStatus.Partial, 40m, 60m), (invoice.Status, invoice.AmountPaid, invoice.Balance));
    }

    private static Invoice CancelledInvoice() => new()
    {
        Id = "INV-1", InvoiceNumber = "#INV-1", CustomerId = "CUS-001", IssueDate = new DateTime(2026, 3, 1),
        DueDate = new DateTime(2026, 3, 31), Total = 100m, TotalUSD = 100m, Balance = 100m,
        Status = InvoiceStatus.Cancelled, OriginalCurrency = "USD"
    };

    private static Invoice DraftInvoice() => new()
    {
        Id = "INV-1", InvoiceNumber = "#INV-1", CustomerId = "CUS-001", IssueDate = new DateTime(2026, 3, 1),
        DueDate = new DateTime(2099, 3, 31), Total = 100m, TotalUSD = 100m, Balance = 100m,
        Status = InvoiceStatus.Draft, OriginalCurrency = "USD"
    };

    private static void ImportAi(CompanyData data, string row, SpreadsheetSheetType type = SpreadsheetSheetType.Invoices)
    {
        var chunk = new LlmProcessedData { EntityType = type };
        chunk.Entities.Add(JsonDocument.Parse(row).RootElement.Clone());
        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], type.ToString());
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
