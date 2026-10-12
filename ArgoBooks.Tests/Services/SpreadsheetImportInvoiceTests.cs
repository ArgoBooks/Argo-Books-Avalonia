using System.Reflection;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the invoice rows of the spreadsheet importer. ImportInvoices is private (it's an
/// internal step of the larger import pipeline), so it's invoked here via reflection.
/// </summary>
public class SpreadsheetImportInvoiceTests
{
    private static void ImportInvoices(CompanyData data, List<string> headers, List<List<object?>> rows)
    {
        var svc = new SpreadsheetImportService();
        var method = typeof(SpreadsheetImportService).GetMethod(
            "ImportInvoices", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(svc, [data, headers, rows, null]);
    }

    [Fact]
    public void ImportInvoices_BalanceColumnPresentNoPaidColumn_KeepsImportedBalance()
    {
        // A third-party sheet supplies Total and Balance but no "Paid" column. The importer must not
        // assume nothing was paid and overwrite the imported Balance with the full Total.
        var data = new CompanyData();
        var headers = new List<string> { "Invoice #", "Total", "Balance" };
        var rows = new List<List<object?>>
        {
            new() { "INV-1", 1000m, 600m }
        };

        ImportInvoices(data, headers, rows);

        Assert.Single(data.Invoices);
        Assert.Equal(600m, data.Invoices[0].Balance);
    }

    [Fact]
    public void ImportInvoices_DayFirstIssueDate_ParsesInsteadOfDefaultingToMinValue()
    {
        // A UK date such as 15/03/2023 cannot be read month-first, so an invariant parse fails and the date would silently become MinValue.
        var data = new CompanyData();
        var headers = new List<string> { "Invoice #", "Issue Date", "Total" };
        var rows = new List<List<object?>>
        {
            new() { "INV-1", "15/03/2023", 100m }
        };

        ImportInvoices(data, headers, rows);

        Assert.Single(data.Invoices);
        Assert.Equal(new DateTime(2023, 3, 15), data.Invoices[0].IssueDate);
    }
}
