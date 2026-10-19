using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class BankStatementDebitSignTests
{
    private static async Task<string> WriteTempCsvAsync(string content)
    {
        var path = Path.GetTempFileName() + ".csv";
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    /// <summary>
    /// Some banks write withdrawals in the Debit column as negative numbers. A Debit column only
    /// ever holds money going out, so a -50.00 debit must still come in as -50 and not flip into a
    /// $50 deposit, and a positive credit beside it must stay money in.
    /// </summary>
    [Fact]
    public async Task ParseCsvAsync_DebitColumnWithNegativeNumbers_StillReadsAsMoneyOut()
    {
        var path = await WriteTempCsvAsync(
            "Date,Description,Debit,Credit\n" +
            "2025-02-03,Supplies,-50.00,\n" +
            "2025-02-06,Deposit,,980.00\n");
        try
        {
            var lines = await new BankStatementImportService().ParseCsvAsync(path);

            Assert.Equal(2, lines.Count);
            Assert.Equal(-50.00m, lines[0].Amount);
            Assert.Equal(980.00m, lines[1].Amount);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// The usual layout, positive numbers in both columns, keeps working: the debit is money out and
    /// the credit is money in, and a row carrying both nets to credit minus debit.
    /// </summary>
    [Fact]
    public async Task ParseCsvAsync_RowWithBothDebitAndCredit_NetsCreditMinusDebit()
    {
        var path = await WriteTempCsvAsync(
            "Date,Description,Debit,Credit\n" +
            "2025-02-03,Fee and refund,12.00,30.00\n");
        try
        {
            var lines = await new BankStatementImportService().ParseCsvAsync(path);

            Assert.Equal(18.00m, Assert.Single(lines).Amount);
        }
        finally { File.Delete(path); }
    }
}
