using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Many banks' "Export to Excel" saves an HTML page as .xls, and the statement below has the shape of one.
/// </summary>
public class BankStatementHtmlXlsTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files)
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    private const string Statement = """



        <!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.01 Transitional//EN">
        <html xmlns="http://www.w3.org/1999/xhtml">
            <head>
                <META HTTP-EQUIV="Cache-Control" CONTENT="no-cache,must-revalidate">
                <title>Statement</title>
            </head>
            <body>
                <table>
                    <tr><td colspan="9" align="center"><span style="font-weight: bold">Account Statement</span></td></tr>
                    <tr><th>Account Title :</th><td colspan="2">Sample Trading Co.</td></tr>
                    <tr><th>Currency :</th><td colspan="2">PKR</td></tr>
                    <tr><th>Statement Period :</th><td colspan="2">01-Mar-2026 to 31-Mar-2026</td></tr>
                </table>
                <table>
                    <tbody>
                    <tr><th>Date</th><th>Particulars</th><th>Debit</th><th>Credit</th><th>Balance</th>
                    <tr><td colspan="4">Opening Balance :</td><td>1,000.00</td>
                    <tr><td>02-Mar-2026</td><td>CHEQUE PAID&nbsp;#100231 <br>OFFICE RENT</td><td>12,500.00</td><td></td><td>-11,500.00</td>
                    <tr><td>05-Mar-2026</td><td>FUNDS TRANSFER FROM A &amp; B TRADERS</td><td></td><td>40,000.00</td><td>28,500.00</td>
                    <tr><td>09-Mar-2026</td><td>BANK CHARGES</td><td>45.50</td><td></td><td>28,454.50</td>
                    </tbody>
                </table>
                <table><tr><td colspan="5">Closing balance as of 31-Mar-2026</td></tr></table>
            </body>
        </html>
        """;

    private string Write(string extension, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stmt-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        _files.Add(path);
        return path;
    }

    [Fact]
    public async Task AnHtmlPageNamedXlsImportsItsTransactions()
    {
        var path = Write(".xls", System.Text.Encoding.UTF8.GetBytes(Statement));

        var lines = await new BankStatementImportService().ParseExcelAsync(path);

        Assert.Equal(3, lines.Count);
        var rent = Assert.Single(lines, l => l.Description.Contains("OFFICE RENT"));
        Assert.Equal(new DateTime(2026, 3, 2), rent.Date);
        Assert.Equal(-12500.00m, rent.Amount);
        var transfer = Assert.Single(lines, l => l.Description == "FUNDS TRANSFER FROM A & B TRADERS");
        Assert.Equal(40000.00m, transfer.Amount);
    }

    [Fact]
    public void RowsThatNeverCloseStillSplitIntoSeparateRows()
    {
        var rows = HtmlTableReader.ReadRows(System.Text.Encoding.UTF8.GetBytes(Statement));

        Assert.Contains(rows, r => r.SequenceEqual(["Date", "Particulars", "Debit", "Credit", "Balance"]));
        Assert.Contains(rows, r => r.SequenceEqual(["09-Mar-2026", "BANK CHARGES", "45.50", "", "28,454.50"]));
    }

    [Fact]
    public void ASpannedCellKeepsTheColumnsAfterItInPlace()
    {
        var rows = HtmlTableReader.ReadRows(System.Text.Encoding.UTF8.GetBytes(Statement));

        Assert.Contains(rows, r => r.SequenceEqual(["Opening Balance :", "", "", "", "1,000.00"]));
    }

    [Fact]
    public void AnXlsxNamedXlsIsReadAsAnXlsx()
    {
        byte[] content;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Statement");
            sheet.Cell(1, 1).Value = "Date";
            sheet.Cell(2, 1).Value = "2026-03-01";
            using var buffer = new MemoryStream();
            workbook.SaveAs(buffer);
            content = buffer.ToArray();
        }
        var path = Write(".xls", content);

        var converted = LegacyXlsConverter.ConvertXlsToTempXlsx(path);
        _files.Add(converted);

        using var result = new XLWorkbook(converted);
        Assert.Equal("Date", result.Worksheet(1).Cell(1, 1).GetString());
    }

    [Fact]
    public void AnHtmlPageWithoutATableIsNotTakenForAStatement()
    {
        Assert.False(HtmlTableReader.LooksLikeHtmlTable("<html><body>Session expired</body></html>"u8.ToArray()));
    }
}
