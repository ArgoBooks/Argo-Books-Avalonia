using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class CsvDelimiterDetectionTests : IDisposable
{
    /// <summary>The shape of a European bank export: account details above the header, semicolons, and decimal commas.</summary>
    private const string SemicolonStatementWithPreamble = """
        Account holder: Sample Trading GmbH
        IBAN: DE89 3704 0044 0532 0130 00

        Date;Description;Amount;Balance
        03.02.2025;KAFFEEHAUS MUELLER;-4,50;1.995,50
        05.02.2025;BUEROBEDARF SCHMIDT, BERLIN;-128,90;1.866,60
        14.02.2025;UEBERWEISUNG KUNDE 1042;2.200,00;4.066,60
        """;

    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files)
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    private string Write(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"delim-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        _files.Add(path);
        return path;
    }

    [Fact]
    public void ASemicolonFile_WithAccountDetailsAboveTheHeader_IsSplitOnSemicolons()
    {
        Assert.Equal(';', SpreadsheetAnalysisService.DetectCsvDelimiter(SemicolonStatementWithPreamble.Split('\n')));
    }

    [Fact]
    public void ACommaFile_StaysAComma()
    {
        Assert.Equal(',', SpreadsheetAnalysisService.DetectCsvDelimiter(["Date,Description,Amount", "2025-01-05,\"SUPPLY CO; POS\",-263.38", "2025-01-08,PAINT,-1204.00"]));
    }

    [Fact]
    public void ATabFile_IsSplitOnTabs()
    {
        Assert.Equal('\t', SpreadsheetAnalysisService.DetectCsvDelimiter(["Date\tDescription\tAmount", "2025-01-05\tSUPPLY, CO\t-263.38"]));
    }

    [Fact]
    public void NothingToGoOn_FallsBackToAComma()
    {
        Assert.Equal(',', SpreadsheetAnalysisService.DetectCsvDelimiter(["", "just one field"]));
    }

    [Fact]
    public async Task ASemicolonBankStatement_WithAccountDetailsAboveTheHeader_Imports()
    {
        var lines = await new BankStatementImportService().ParseCsvAsync(Write(SemicolonStatementWithPreamble));

        Assert.Equal(3, lines.Count);
        Assert.Equal(new DateTime(2025, 2, 5), lines[1].Date);
        Assert.Equal("BUEROBEDARF SCHMIDT, BERLIN", lines[1].Description);
        Assert.Equal(-128.90m, lines[1].Amount);
        Assert.Equal(2200.00m, lines[2].Amount);
    }

    [Fact]
    public void ASemicolonSpreadsheet_WithACommentAboveTheHeader_IsReadIntoColumns()
    {
        var rows = CsvReader.ReadAllRows(Write("# Customers\nName;City\nMüller GmbH;Berlin\nSchmidt AG;München\n"), out var headers);

        Assert.Equal(["Name", "City"], headers);
        Assert.Equal(["Schmidt AG", "München"], rows[1]);
    }
}
