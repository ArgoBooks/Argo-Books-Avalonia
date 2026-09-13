using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class BankStatementImportServiceTests
{
    // Headers already use the target column names, so an empty mapping leaves them untouched.
    private static SheetAnalysis EmptyMapping(string sheetName) => new()
    {
        SourceSheetName = sheetName,
        DetectedType = SpreadsheetSheetType.BankStatement,
        ColumnMappings = []
    };

    private static async Task<string> WriteTempCsvAsync(string content)
    {
        var path = Path.GetTempFileName() + ".csv";
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static string WriteTempXlsxWithPreamble()
    {
        var path = Path.GetTempFileName() + ".xlsx";
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("Statement");
        // Two metadata rows many banks put above the real header (each has >= 2 non-empty cells).
        ws.Cell(1, 1).Value = "Account:";
        ws.Cell(1, 2).Value = "Checking ****5678";
        ws.Cell(2, 1).Value = "Statement Period:";
        ws.Cell(2, 2).Value = "01/01 - 01/31";
        // The real header and data.
        ws.Cell(4, 1).Value = "Date";
        ws.Cell(4, 2).Value = "Description";
        ws.Cell(4, 3).Value = "Amount";
        ws.Cell(5, 1).Value = "2025-01-05";
        ws.Cell(5, 2).Value = "Coffee shop";
        ws.Cell(5, 3).Value = -12.50;
        ws.Cell(6, 1).Value = "2025-01-06";
        ws.Cell(6, 2).Value = "Client deposit";
        ws.Cell(6, 3).Value = 250.00;
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public async Task ParseExcelAsync_StatementWithPreambleRows_FindsHeaderAndImportsLines()
    {
        // Many banks export Excel statements with a couple of metadata rows before the column
        // header. The Excel path picks the first row with >= 2 cells as the header (the preamble),
        // fails to find Date/Amount, and silently imports nothing. The CSV path scans for the real
        // header; the Excel path should too.
        var path = WriteTempXlsxWithPreamble();
        try
        {
            var lines = await new BankStatementImportService().ParseExcelAsync(path);

            Assert.Equal(2, lines.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ParseCsvAsync_SignedAmountColumn_KeepsSignAndParses()
    {
        var path = await WriteTempCsvAsync(
            "Date,Description,Amount,Balance\n" +
            "2025-01-05,Coffee shop,-12.50,100.00\n" +
            "2025-01-06,Client deposit,250.00,350.00\n");
        try
        {
            var lines = await new BankStatementImportService().ParseCsvWithAnalysisAsync(path, EmptyMapping(Path.GetFileNameWithoutExtension(path)));

            Assert.Equal(2, lines.Count);
            Assert.Equal(-12.50m, lines[0].Amount);
            Assert.Equal("Coffee shop", lines[0].Description);
            Assert.Equal(250.00m, lines[1].Amount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ParseCsvAsync_DebitCreditColumns_NormalizesToSignedAmount()
    {
        var path = await WriteTempCsvAsync(
            "Date,Description,Debit,Credit,Balance\n" +
            "2025-02-03,Supplies,142.10,,8500.00\n" +
            "2025-02-06,Deposit,,980.00,9480.00\n");
        try
        {
            var lines = await new BankStatementImportService().ParseCsvWithAnalysisAsync(path, EmptyMapping(Path.GetFileNameWithoutExtension(path)));

            Assert.Equal(2, lines.Count);
            Assert.Equal(-142.10m, lines[0].Amount); // debit => money out (negative)
            Assert.Equal(142.10m, lines[0].Debit);
            Assert.Equal(980.00m, lines[1].Amount);  // credit => money in (positive)
            Assert.Equal(980.00m, lines[1].Credit);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ParseCsvAsync_ParenthesesNegativeAndCurrencySymbol_Parsed()
    {
        var path = await WriteTempCsvAsync(
            "Date,Description,Amount\n" +
            "2025-03-01,Refund,\"($45.00)\"\n");
        try
        {
            var lines = await new BankStatementImportService().ParseCsvWithAnalysisAsync(path, EmptyMapping(Path.GetFileNameWithoutExtension(path)));

            Assert.Single(lines);
            Assert.Equal(-45.00m, lines[0].Amount);
        }
        finally { File.Delete(path); }
    }

    // A file is written one way. 15/03 can only be day-first, so 05/03 in the same column is
    // 5 March; reading each row on its own made it 3 May.
    [Fact]
    public async Task ParseCsvAsync_DayFirstFile_ReadsEveryRowDayFirst()
    {
        var path = await WriteTempCsvAsync(
            "Date,Description,Amount\n" +
            "05/03/2024,Coffee shop,-4.50\n" +
            "15/03/2024,Client deposit,250.00\n");
        try
        {
            var lines = await new BankStatementImportService().ParseCsvAsync(path);

            Assert.Equal(new DateTime(2024, 3, 5), lines[0].Date);
            Assert.Equal(new DateTime(2024, 3, 15), lines[1].Date);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ParseCsvAsync_MonthFirstFile_StaysMonthFirst()
    {
        var path = await WriteTempCsvAsync(
            "Date,Description,Amount\n" +
            "03/05/2024,Coffee shop,-4.50\n" +
            "03/15/2024,Client deposit,250.00\n");
        try
        {
            var lines = await new BankStatementImportService().ParseCsvAsync(path);

            Assert.Equal(new DateTime(2024, 3, 5), lines[0].Date);
            Assert.Equal(new DateTime(2024, 3, 15), lines[1].Date);
        }
        finally { File.Delete(path); }
    }
}
