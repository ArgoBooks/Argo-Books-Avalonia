using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Reads the transaction download most banks offer beside CSV: OFX, and Intuit's QFX (Quicken) and
/// QBO (QuickBooks Web Connect), which are OFX with a different extension. Covers both OFX 1.x, an
/// SGML form whose single-value tags are never closed, and OFX 2.x, which is XML.
/// </summary>
public static partial class OfxStatementReader
{
    public static readonly string[] Extensions = [".ofx", ".qfx", ".qbo"];

    public static bool IsOfxFile(string? path) =>
        Extensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

    public static List<BankStatementLine> Read(string filePath, CancellationToken cancellationToken = default) =>
        Parse(TextFileDecoder.ReadAllText(filePath), cancellationToken);

    public static List<BankStatementLine> Parse(string ofx, CancellationToken cancellationToken = default)
    {
        var lines = new List<BankStatementLine>();
        foreach (Match transaction in Transaction().Matches(ofx))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = transaction.Groups[1].Value;

            var date = ParseDate(Field(block, "DTPOSTED"));
            var amountText = Field(block, "TRNAMT");
            if (date == null || amountText.Length == 0) continue;
            var amount = CurrencyCellDetector.ParseAmount(amountText);

            lines.Add(new BankStatementLine
            {
                Id = Guid.NewGuid().ToString("N"),
                Date = date.Value,
                Description = Description(Field(block, "NAME"), Field(block, "MEMO")),
                Amount = amount,
                RawReference = FirstNonEmpty(Field(block, "CHECKNUM"), Field(block, "REFNUM")),
                MatchStatus = BankLineMatchStatus.Unmatched,
                SourceRowIndex = lines.Count
            });
        }

        return lines;
    }

    /// <summary>The value of the first tag of that name, closed or not, so it reads both OFX 1.x and 2.x.</summary>
    private static string Field(string block, string tag)
    {
        var match = Regex.Match(block, $@"<{tag}>([^<\r\n]*)", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : string.Empty;
    }

    /// <summary>
    /// The payee and, when it adds something, the memo. Banks split the text differently, some
    /// putting the merchant in the memo and only a transaction type in the name.
    /// </summary>
    private static string Description(string name, string memo)
    {
        if (memo.Length == 0 || name.Contains(memo, StringComparison.OrdinalIgnoreCase)) return name;
        if (name.Length == 0 || memo.Contains(name, StringComparison.OrdinalIgnoreCase)) return memo;
        return $"{name} {memo}";
    }

    /// <summary>OFX dates start YYYYMMDD and may go on with a time and a time zone, which a posting date does not need.</summary>
    private static DateTime? ParseDate(string value) =>
        value.Length >= 8 && DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => v.Length > 0) ?? string.Empty;

    [GeneratedRegex(@"<STMTTRN>(.*?)(?=</STMTTRN>|<STMTTRN>|</BANKTRANLIST>|\z)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Transaction();
}
