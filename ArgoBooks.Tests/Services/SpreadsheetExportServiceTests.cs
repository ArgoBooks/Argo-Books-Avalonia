using System.Reflection;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the SpreadsheetExportService class.
/// </summary>
public class SpreadsheetExportServiceTests
{
    private static readonly MethodInfo EscapeCsvMethod =
        typeof(SpreadsheetExportService).GetMethod("EscapeCsv", BindingFlags.NonPublic | BindingFlags.Static)!;


    private static readonly MethodInfo GetColumnLetterMethod =
        typeof(ChartExcelExportService).GetMethod("GetColumnLetter", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static string InvokeEscapeCsv(string value) =>
        (string)EscapeCsvMethod.Invoke(null, [value])!;

    private static string InvokeTruncateSheetName(string name) => ExcelSheetNames.Clean(name, "Sheet1");

    private static string InvokeGetColumnLetter(int columnNumber) =>
        (string)GetColumnLetterMethod.Invoke(null, [columnNumber])!;

    #region EscapeCsv Tests

    [Fact]
    public void EscapeCsv_ValueContainsComma_WrapsInQuotes()
    {
        var result = InvokeEscapeCsv("hello,world");

        Assert.Equal("\"hello,world\"", result);
    }

    [Fact]
    public void EscapeCsv_ValueContainsQuotes_DoublesThemAndWraps()
    {
        var result = InvokeEscapeCsv("say \"hello\"");

        Assert.Equal("\"say \"\"hello\"\"\"", result);
    }

    [Fact]
    public void EscapeCsv_ValueContainsNewline_WrapsInQuotes()
    {
        var result = InvokeEscapeCsv("line1\nline2");

        Assert.Equal("\"line1\nline2\"", result);
    }

    [Fact]
    public void EscapeCsv_PlainString_ReturnsUnchanged()
    {
        var result = InvokeEscapeCsv("simple text");

        Assert.Equal("simple text", result);
    }

    [Fact]
    public void EscapeCsv_EmptyString_ReturnsEmpty()
    {
        var result = InvokeEscapeCsv("");

        Assert.Equal("", result);
    }

    [Theory]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\nb", "\"a\nb\"")]
    [InlineData("plain", "plain")]
    public void EscapeCsv_VariousInputs_ReturnsExpected(string input, string expected)
    {
        var result = InvokeEscapeCsv(input);

        Assert.Equal(expected, result);
    }

    #endregion

    #region TruncateSheetName Tests

    [Fact]
    public void TruncateSheetName_CharactersExcelForbids_BecomeUnderscores()
    {
        Assert.Equal("Sales_Returns _Q1_", InvokeTruncateSheetName("Sales/Returns [Q1]"));
    }

    [Fact]
    public void TruncateSheetName_LongName_EnforcesThirtyOneCharLimit()
    {
        var longName = new string('A', 50);

        var result = InvokeTruncateSheetName(longName);

        Assert.Equal(31, result.Length);
    }

    [Fact]
    public void TruncateSheetName_ShortName_ReturnsUnchanged()
    {
        var result = InvokeTruncateSheetName("Customers");

        Assert.Equal("Customers", result);
    }

    [Fact]
    public void TruncateSheetName_ExactlyThirtyOneChars_ReturnsUnchanged()
    {
        var name = new string('B', 31);

        var result = InvokeTruncateSheetName(name);

        Assert.Equal(31, result.Length);
        Assert.Equal(name, result);
    }

    [Theory]
    [InlineData("Short", "Short")]
    [InlineData("Purchase Order Line Items Extra", "Purchase Order Line Items Extra")]
    public void TruncateSheetName_VariousLengths_ReturnsExpected(string input, string expected)
    {
        var result = InvokeTruncateSheetName(input);

        Assert.Equal(expected, result);
    }

    #endregion

    #region GetColumnLetter Tests

    [Theory]
    [InlineData(1, "A")]
    [InlineData(2, "B")]
    [InlineData(26, "Z")]
    public void GetColumnLetter_SingleLetterColumns_ReturnsCorrectLetter(int column, string expected)
    {
        var result = InvokeGetColumnLetter(column);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetColumnLetter_Column27_ReturnsAA()
    {
        var result = InvokeGetColumnLetter(27);

        Assert.Equal("AA", result);
    }

    [Theory]
    [InlineData(28, "AB")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    public void GetColumnLetter_MultiLetterColumns_ReturnsCorrectLetters(int column, string expected)
    {
        var result = InvokeGetColumnLetter(column);

        Assert.Equal(expected, result);
    }

    #endregion
}
