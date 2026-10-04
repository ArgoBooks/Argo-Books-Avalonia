using ArgoBooks.Core.Services;
using NPOI.HSSF.UserModel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A legacy .xls is a binary workbook that ClosedXML cannot open at all: it throws
/// FileFormatException, which the bank statement import reported as "unreadable file". The
/// spreadsheet import already converted these; this path did not, so no bank that exports .xls
/// could be imported here, and several still do.
/// </summary>
public class BankStatementLegacyXlsTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files)
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    /// <summary>Writes a real BIFF8 workbook, the format the old Excel and many banks produce.</summary>
    private string WriteLegacyXls(params string[][] rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stmt-{Guid.NewGuid():N}.xls");
        using (var workbook = new HSSFWorkbook())
        {
            var sheet = workbook.CreateSheet("Statement");
            for (var r = 0; r < rows.Length; r++)
            {
                var row = sheet.CreateRow(r);
                for (var c = 0; c < rows[r].Length; c++)
                    row.CreateCell(c).SetCellValue(rows[r][c]);
            }
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            workbook.Write(fs);
        }
        _files.Add(path);
        return path;
    }

    [Fact]
    public async Task ALegacyXlsStatementImports()
    {
        var path = WriteLegacyXls(
            ["Date", "Description", "Amount"],
            ["2026-03-01", "OPENING DEPOSIT", "1500.00"],
            ["2026-03-04", "HYDRO BILL", "-82.40"],
            ["2026-03-09", "CLEANING SERVICE", "-120.00"]);

        var lines = await new BankStatementImportService().ParseExcelAsync(path);

        Assert.Equal(3, lines.Count);
        Assert.Contains(lines, l => l.Description.Contains("HYDRO", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Many banks put account and period rows above the real header. The statement path scans for
    /// the header rather than assuming the first row, and that has to keep working after conversion.
    /// </summary>
    [Fact]
    public async Task ALegacyXlsWithPreambleRowsStillFindsItsHeader()
    {
        var path = WriteLegacyXls(
            ["Account:", "1234567890"],
            ["Statement Period:", "01 Mar 2026 to 31 Mar 2026"],
            [""],
            ["Date", "Description", "Amount"],
            ["2026-03-02", "PLATFORM FEE", "-31.50"],
            ["2026-03-15", "GUEST PAYOUT", "940.00"]);

        var lines = await new BankStatementImportService().ParseExcelAsync(path);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Description.Contains("PLATFORM FEE", StringComparison.OrdinalIgnoreCase));
    }
}
