using System.Text;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class OfxStatementReaderTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files)
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    /// <summary>OFX 1.x is SGML: a plain-text header, then tags whose single values are never closed.</summary>
    private const string SgmlStatement = """
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102
        SECURITY:NONE
        ENCODING:USASCII
        CHARSET:1252
        COMPRESSION:NONE
        OLDFILEUID:NONE
        NEWFILEUID:NONE

        <OFX>
        <SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>20250131120000<LANGUAGE>ENG</SONRS></SIGNONMSGSRSV1>
        <BANKMSGSRSV1><STMTTRNRS><TRNUID>1<STATUS><CODE>0<SEVERITY>INFO</STATUS>
        <STMTRS><CURDEF>CAD
        <BANKACCTFROM><BANKID>000<ACCTID>12345<ACCTTYPE>CHECKING</BANKACCTFROM>
        <BANKTRANLIST><DTSTART>20250101<DTEND>20250131
        <STMTTRN>
        <TRNTYPE>DEBIT
        <DTPOSTED>20250105120000[-5:EST]
        <TRNAMT>-263.38
        <FITID>2025010501
        <NAME>MILWAUKEE SUPPLY CO
        <MEMO>POS PURCHASE
        </STMTTRN>
        <STMTTRN>
        <TRNTYPE>CHECK
        <DTPOSTED>20250108
        <TRNAMT>-1204.00
        <FITID>2025010801
        <CHECKNUM>1055
        <NAME>SHERWIN WILLIAMS
        </STMTTRN>
        <STMTTRN>
        <TRNTYPE>CREDIT
        <DTPOSTED>20250110
        <TRNAMT>+1343.75
        <FITID>2025011001
        <NAME>ACH DEPOSIT
        <MEMO>ACH DEPOSIT ALICE JOHNSON
        </STMTTRN>
        </BANKTRANLIST>
        <LEDGERBAL><BALAMT>8876.37<DTASOF>20250131</LEDGERBAL>
        </STMTRS></STMTTRNRS></BANKMSGSRSV1>
        </OFX>
        """;

    [Fact]
    public void AnSgmlStatement_IsReadWithSignedAmounts()
    {
        var lines = OfxStatementReader.Parse(SgmlStatement);

        Assert.Equal(3, lines.Count);
        Assert.Equal(new DateTime(2025, 1, 5), lines[0].Date);
        Assert.Equal(-263.38m, lines[0].Amount);
        Assert.Equal(-1204.00m, lines[1].Amount);
        Assert.Equal(1343.75m, lines[2].Amount);
    }

    [Fact]
    public void TheDescription_JoinsNameAndMemo_OnlyWhenTheMemoAddsSomething()
    {
        var lines = OfxStatementReader.Parse(SgmlStatement);

        Assert.Equal("MILWAUKEE SUPPLY CO POS PURCHASE", lines[0].Description);
        Assert.Equal("SHERWIN WILLIAMS", lines[1].Description);
        Assert.Equal("ACH DEPOSIT ALICE JOHNSON", lines[2].Description);
    }

    [Fact]
    public void ACheckNumber_IsKeptAsTheReference()
    {
        var lines = OfxStatementReader.Parse(SgmlStatement);

        Assert.Equal("1055", lines[1].RawReference);
        Assert.Equal("", lines[0].RawReference);
    }

    [Fact]
    public void AnXmlStatement_WithAPayeeAndACreditCard_IsRead()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="no"?>
            <?OFX OFXHEADER="200" VERSION="220" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
            <OFX>
              <CREDITCARDMSGSRSV1><CCSTMTTRNRS><CCSTMTRS>
                <BANKTRANLIST>
                  <STMTTRN>
                    <TRNTYPE>DEBIT</TRNTYPE>
                    <DTPOSTED>20250214</DTPOSTED>
                    <TRNAMT>-45.10</TRNAMT>
                    <FITID>A1</FITID>
                    <PAYEE><NAME>Café &amp; Bistro</NAME><ADDR1>1 Main St</ADDR1></PAYEE>
                  </STMTTRN>
                </BANKTRANLIST>
              </CCSTMTRS></CCSTMTTRNRS></CREDITCARDMSGSRSV1>
            </OFX>
            """;

        var line = Assert.Single(OfxStatementReader.Parse(xml));

        Assert.Equal(new DateTime(2025, 2, 14), line.Date);
        Assert.Equal(-45.10m, line.Amount);
        Assert.Equal("Café & Bistro", line.Description);
    }

    [Fact]
    public void ADecimalCommaAmount_IsRead()
    {
        var line = Assert.Single(OfxStatementReader.Parse("<STMTTRN><DTPOSTED>20250301<TRNAMT>-12,50<NAME>BOULANGERIE</STMTTRN>"));

        Assert.Equal(-12.50m, line.Amount);
    }

    [Fact]
    public void ATransactionWithoutADateOrAmount_IsSkipped()
    {
        var lines = OfxStatementReader.Parse(
            "<STMTTRN><TRNAMT>-1.00<NAME>NO DATE</STMTTRN><STMTTRN><DTPOSTED>20250301<NAME>NO AMOUNT</STMTTRN>");

        Assert.Empty(lines);
    }

    [Fact]
    public void AWindows1252File_KeepsItsAccents()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stmt-{Guid.NewGuid():N}.qfx");
        _files.Add(path);
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(
            "OFXHEADER:100\nCHARSET:1252\n\n<OFX><STMTTRN><DTPOSTED>20250301<TRNAMT>-8.00<NAME>DÉPANNEUR CHÂTEAU</STMTTRN></OFX>"));

        var line = Assert.Single(OfxStatementReader.Read(path));

        Assert.Equal("DÉPANNEUR CHÂTEAU", line.Description);
    }

    [Theory]
    [InlineData("statement.ofx", true)]
    [InlineData("statement.QFX", true)]
    [InlineData("statement.qbo", true)]
    [InlineData("statement.csv", false)]
    [InlineData(null, false)]
    public void OfxFiles_AreRecognisedByExtension(string? path, bool expected)
    {
        Assert.Equal(expected, OfxStatementReader.IsOfxFile(path));
    }
}
