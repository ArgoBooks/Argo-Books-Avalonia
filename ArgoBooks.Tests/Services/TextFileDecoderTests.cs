using System.Text;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class TextFileDecoderTests : IDisposable
{
    private const string Accented = "Café Dépanneur, €12 – O’Brien’s";

    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files)
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    private string Write(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"decode-{Guid.NewGuid():N}.csv");
        File.WriteAllBytes(path, content);
        _files.Add(path);
        return path;
    }

    [Fact]
    public void Utf8WithoutAByteOrderMark_IsReadAsUtf8()
    {
        Assert.Equal(Accented, TextFileDecoder.Decode(new UTF8Encoding(false).GetBytes(Accented)));
    }

    [Fact]
    public void Utf8WithAByteOrderMark_DropsTheMark()
    {
        Assert.Equal(Accented, TextFileDecoder.Decode(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Accented)).ToArray()));
    }

    [Fact]
    public void Utf16_IsReadFromItsByteOrderMark()
    {
        Assert.Equal(Accented, TextFileDecoder.Decode(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Accented)).ToArray()));
    }

    [Fact]
    public void Windows1252_KeepsAccentsTheEuroSignAndCurlyQuotes()
    {
        Assert.Equal(Accented, TextFileDecoder.Decode(Windows1252.GetBytes(Accented)));
    }

    [Fact]
    public void AWindows1252Csv_ImportsWithItsAccents()
    {
        var path = Write(Windows1252.GetBytes("Date,Description,Amount\r\n2025-03-01,Café Dépanneur,-8.00\r\n"));

        var rows = CsvReader.ReadAllRows(path, out var headers);

        Assert.Equal(["Date", "Description", "Amount"], headers);
        Assert.Equal("Café Dépanneur", Assert.Single(rows)[1]);
    }

    [Fact]
    public async Task AWindows1252BankStatement_ImportsWithItsAccents()
    {
        var path = Write(Windows1252.GetBytes("Date,Description,Amount\r\n2025-03-01,Café Dépanneur,-8.00\r\n"));

        var lines = await new BankStatementImportService().ParseCsvAsync(path);

        Assert.Equal("Café Dépanneur", Assert.Single(lines).Description);
    }
}
