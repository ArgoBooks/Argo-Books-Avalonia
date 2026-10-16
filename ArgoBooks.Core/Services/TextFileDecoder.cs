using System.Text;

namespace ArgoBooks.Core.Services;

/// <summary>Turns an imported text file, such as a CSV or a bank download, into text whichever program wrote it.</summary>
public static class TextFileDecoder
{
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    /// <summary>
    /// UTF-8, or the encoding a byte order mark names, and otherwise Windows-1252, which Excel's plain
    /// "CSV" save and many older bank exports write.
    /// </summary>
    public static string Decode(byte[] content)
    {
        using (var reader = new StreamReader(new MemoryStream(content), new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true))
        {
            try { return reader.ReadToEnd(); }
            catch (DecoderFallbackException) { }
        }
        return Windows1252.GetString(content);
    }

    /// <summary>Reads and decodes a whole file, sharing it so it still reads while open in Excel or another program.</summary>
    public static string ReadAllText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Decode(buffer.ToArray());
    }
}
