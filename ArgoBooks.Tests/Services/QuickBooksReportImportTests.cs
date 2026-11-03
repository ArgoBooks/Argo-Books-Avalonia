using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// What a QuickBooks migration brings across. The AI gives some rows only a price and others only a
/// total, and an open invoices report gives only what is still owed, so the importer has to arrive
/// at the same money as the report it read.
/// </summary>
public class QuickBooksReportImportTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static void Import(CompanyData data, SpreadsheetSheetType type, string raw)
    {
        var chunk = new LlmProcessedData { EntityType = type };
        chunk.Entities.Add(Json(raw));
        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], "Report");
    }

    [Fact]
    public void ARevenueRowWithOnlyAPrice_GetsItsTotal()
    {
        var data = new CompanyData();

        Import(data, SpreadsheetSheetType.Revenue, """{ "id": "SAL-1", "date": "2026-01-05", "unitPrice": 450 }""");

        var revenue = Assert.Single(data.Revenues);
        Assert.Equal(450m, revenue.Amount);
        Assert.Equal(450m, revenue.Total);
    }

    [Fact]
    public void AnExpenseRowWithOnlyATotal_GetsItsAmountAndPrice()
    {
        var data = new CompanyData();

        Import(data, SpreadsheetSheetType.Expenses, """{ "id": "PUR-1", "date": "2026-01-05", "total": 637.75 }""");

        var expense = Assert.Single(data.Expenses);
        Assert.Equal(637.75m, expense.Amount);
        Assert.Equal(637.75m, expense.UnitPrice);
        Assert.Equal(637.75m, expense.Total);
    }

    [Fact]
    public void ARowWithBothAmountAndTotal_KeepsThemBoth()
    {
        var data = new CompanyData();

        Import(data, SpreadsheetSheetType.Revenue, """{ "id": "SAL-1", "date": "2026-01-05", "amount": 100, "taxAmount": 13, "total": 113 }""");

        var revenue = Assert.Single(data.Revenues);
        Assert.Equal(100m, revenue.Amount);
        Assert.Equal(113m, revenue.Total);
    }

    [Fact]
    public void AnOpenInvoiceWithOnlyABalance_IsOwedThatMuch()
    {
        var data = new CompanyData();

        Import(data, SpreadsheetSheetType.Invoices, """{ "id": "1060", "customerId": "Acme", "issueDate": "2026-10-03", "balance": 450 }""");

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal(450m, invoice.Total);
        Assert.Equal(450m, invoice.Balance);
        Assert.NotEqual(InvoiceStatus.Draft, invoice.Status);
    }

    [Fact]
    public void ATransactionNamingABusiness_FindsTheCustomerFiledUnderItsContact()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "CUS-1", Name = "Dana Whitfield", CompanyName = "Harbor Dental Clinic" });

        Import(data, SpreadsheetSheetType.Revenue, """{ "id": "SAL-1", "date": "2026-01-05", "customerId": "Harbor Dental Clinic", "total": 450 }""");

        Assert.Equal("CUS-1", Assert.Single(data.Revenues).CustomerId);
        Assert.Single(data.Customers);
    }

    [Fact]
    public void AnOpenInvoiceAlreadyInTheSales_IsNotCountedTwice()
    {
        var data = new CompanyData();

        Import(data, SpreadsheetSheetType.Revenue, """{ "id": "SAL-1060", "date": "2026-10-03", "customerId": "Acme", "invoiceId": "1060", "total": 450 }""");
        Import(data, SpreadsheetSheetType.Invoices, """{ "id": "1060", "customerId": "Acme", "issueDate": "2026-10-03", "balance": 450 }""");

        var revenue = Assert.Single(data.Revenues);
        Assert.Equal("1060", revenue.InvoiceId);
        Assert.Equal(RevenuePaymentStatus.Unpaid, revenue.PaymentStatus);
    }

    [Fact]
    public void TwoLinesOfOneInvoice_BothCountAfterTheirIdsAreMadeUnique()
    {
        var seen = new Dictionary<string, int>();
        var first = SpreadsheetAnalysisService.WithUniqueId(Json("""{ "id": "SAL-1", "date": "2026-01-05", "total": 450 }"""), seen);
        var second = SpreadsheetAnalysisService.WithUniqueId(Json("""{ "id": "SAL-1", "date": "2026-01-05", "total": 95 }"""), seen);

        var data = new CompanyData();
        Import(data, SpreadsheetSheetType.Revenue, first.GetRawText());
        Import(data, SpreadsheetSheetType.Revenue, second.GetRawText());

        Assert.Equal(545m, data.Revenues.Sum(r => r.Total));
    }

    [Fact]
    public void ReportsChosenAlphabetically_ImportListsFirstAndOpenInvoicesLast()
    {
        string[] chosen = ["Customer Contact List.xlsx", "Item Listing.xlsx", "Open Invoices.xlsx", "Profit and Loss Detail.xlsx", "Vendor Contact List.xlsx"];

        Assert.Equal(
            ["Customer Contact List.xlsx", "Item Listing.xlsx", "Vendor Contact List.xlsx", "Profit and Loss Detail.xlsx", "Open Invoices.xlsx"],
            QuickBooksImportModalViewModel.ImportOrder(chosen));
    }

    [Fact]
    public void AnOpenInvoice_BelongsToTheCustomerHeadingAboveIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "argo-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Open Invoices.xlsx");
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Sheet1");
            object?[][] rows =
            [
                ["Company"], ["Open Invoices Report"], [], [],
                [null, "Date", "Transaction type", "Num", "Open balance"],
                ["Amy's Bird Sanctuary"],
                [null, "09/14/2026", "Invoice", "1021", 239.0],
                ["Total for Amy's Bird Sanctuary", null, null, null, 239.0],
                ["Freeman Sporting Goods"],
                ["0969 Ocean View Road"],
                [null, "10/06/2026", "Invoice", "1036", 477.5],
                ["Total for 0969 Ocean View Road", null, null, null, 477.5],
                ["Total for Freeman Sporting Goods with sub-customers", null, null, null, 477.5],
                ["Geeta Kalapatapu"],
                [null, "10/05/2026", "Invoice", "1033", 629.1],
                ["Total for Geeta Kalapatapu", null, null, null, 629.1],
                ["TOTAL", null, null, null, 1345.6],
            ];
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    if (rows[r][c] is { } value)
                        sheet.Cell(r + 1, c + 1).Value = value is double d ? d : value.ToString();
            book.SaveAs(path);
        }

        try
        {
            var written = QuickBooksReportFlattener.FlattenOpenInvoices(path, Path.Combine(folder, "out"));

            Assert.NotNull(written);
            using var result = new XLWorkbook(written);
            var table = result.Worksheets.First();
            Assert.Equal("Customer", table.Cell(1, 1).GetString());
            Assert.Equal(["Amy's Bird Sanctuary", "Freeman Sporting Goods:0969 Ocean View Road", "Geeta Kalapatapu"],
                Enumerable.Range(2, 3).Select(r => table.Cell(r, 1).GetString()));
            Assert.Equal(["1021", "1036", "1033"], Enumerable.Range(2, 3).Select(r => table.Cell(r, 4).GetString()));
            Assert.True(table.Cell(5, 1).IsEmpty());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnOpenInvoicesReportOpenInExcel_IsStillFlattened()
    {
        var folder = Path.Combine(Path.GetTempPath(), "argo-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Open Invoices.xlsx");
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Sheet1");
            sheet.Cell(1, 2).Value = "Date";
            sheet.Cell(1, 3).Value = "Transaction type";
            sheet.Cell(1, 4).Value = "Num";
            sheet.Cell(1, 5).Value = "Open balance";
            sheet.Cell(2, 1).Value = "Amy's Bird Sanctuary";
            sheet.Cell(3, 2).Value = "09/14/2026";
            sheet.Cell(3, 3).Value = "Invoice";
            sheet.Cell(3, 4).Value = "1021";
            sheet.Cell(3, 5).Value = 239.0;
            book.SaveAs(path);
        }

        try
        {
            string? written;
            // Excel holds an open workbook for writing and lets other programs read it.
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                written = QuickBooksReportFlattener.FlattenOpenInvoices(path, Path.Combine(folder, "out"));

            Assert.NotNull(written);
            using var result = new XLWorkbook(written);
            Assert.Equal("Amy's Bird Sanctuary", result.Worksheets.First().Cell(2, 1).GetString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ReportsThatRepeatTheTransactions_AreLeftOutOfTheQuickBooksZip()
    {
        Assert.True(QuickBooksImportModalViewModel.IsAccountingSummary("General_ledger.xlsx"));
        Assert.True(QuickBooksImportModalViewModel.IsAccountingSummary("Journal.xlsx"));
        Assert.False(QuickBooksImportModalViewModel.IsAccountingSummary("Customers.xlsx"));
        Assert.False(QuickBooksImportModalViewModel.IsAccountingSummary("Vendors.xlsx"));
    }

    [Fact]
    public void ADateThatCouldBeReadEitherWay_FollowsTheRestOfTheReport()
    {
        List<List<string>> rows = [["Income", ""], ["01/03/2026", "450"], ["01/25/2026", "900"]];

        var written = SpreadsheetAnalysisService.WithUnambiguousDates(rows);

        Assert.Equal("2026-01-03", written[1][0]);
        Assert.Equal("2026-01-25", written[2][0]);
        Assert.Equal("Income", written[0][0]);
    }

    [Fact]
    public void DatesNothingCanSettle_AreLeftAsTheyAre()
    {
        List<List<string>> rows = [["01/03/2026", "450"], ["02/04/2026", "900"]];

        Assert.Same(rows, SpreadsheetAnalysisService.WithUnambiguousDates(rows));
    }

    [Fact]
    public void AReportOfDatedTransactions_IsNotTurnedAwayAsASummary()
    {
        var rejected = new RescueClassification { Reason = ImportRescueRejectionReason.SummaryOrReport };
        List<List<string>> detail = [["Income", ""], ["01/05/2026", "450"], ["01/06/2026", "900"], ["Total for Income", "1350"]];
        List<List<string>> summary = [["Income", ""], ["Sales", "1350"], ["Total for Income", "1350"], ["Net Income", "1350"]];

        Assert.True(SpreadsheetAnalysisService.IsDetailReport(rejected, detail));
        Assert.False(SpreadsheetAnalysisService.IsDetailReport(rejected, summary));
    }

    [Fact]
    public void AHeadingTheOutlinePutsOnADatedRow_IsNotAllowedToRemoveThatRow()
    {
        List<List<string>> rows =
        [
            ["Income", "", ""],
            ["01/05/2026", "Invoice", "450"],
            ["01/06/2026", "Invoice", "900"],
            ["Total for Income", "", "1350"],
        ];
        var markers = new List<MixedRowMarker>
        {
            new(0, MixedRowKind.IncomeSection, "Income"),
            new(2, MixedRowKind.Category, "Advertising"),
            new(3, MixedRowKind.Subtotal, "Total for Income"),
        };

        var buckets = SpreadsheetAnalysisService.BucketMixedRows(rows.Count, SpreadsheetAnalysisService.WithoutTransactionRows(markers, rows));

        Assert.Equal([1, 2], Assert.Single(buckets).Value);
    }
}
