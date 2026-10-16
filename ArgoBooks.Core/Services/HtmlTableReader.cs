using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Reads the tables of an HTML page saved as <c>.xls</c>, stacked onto one grid the way Excel shows them.
/// </summary>
public static partial class HtmlTableReader
{
    /// <summary>True when the bytes are an HTML page containing at least one table.</summary>
    public static bool LooksLikeHtmlTable(byte[] content)
    {
        var text = TextFileDecoder.Decode(content);
        var start = text.AsSpan().TrimStart();
        return start.StartsWith("<") && text.Contains("<table", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every non-empty row, with a spanned cell's text in its first column and blanks after it.</summary>
    public static List<List<string>> ReadRows(byte[] content)
    {
        var html = StripNonContent().Replace(TextFileDecoder.Decode(content), " ");

        var rows = new List<List<string>>();
        List<string>? row = null;
        StringBuilder? cell = null;
        var cellSpan = 1;

        void EndCell()
        {
            if (cell == null) return;
            row ??= [];
            row.Add(Clean(cell.ToString()));
            for (var i = 1; i < cellSpan; i++) row.Add("");
            cell = null;
        }

        void EndRow()
        {
            EndCell();
            if (row != null && row.Any(c => c.Length > 0)) rows.Add(row);
            row = null;
        }

        var position = 0;
        foreach (Match tag in Tag().Matches(html))
        {
            cell?.Append(html, position, tag.Index - position);
            position = tag.Index + tag.Length;

            var closing = tag.Groups[1].Value == "/";
            switch (tag.Groups[2].Value.ToLowerInvariant())
            {
                // Closing tags for rows and cells are optional in HTML, so a new row or cell also ends the one before it.
                case "tr":
                case "table":
                case "tbody":
                case "thead":
                case "tfoot":
                    EndRow();
                    if (!closing && tag.Groups[2].Value.Equals("tr", StringComparison.OrdinalIgnoreCase)) row = [];
                    break;
                case "td":
                case "th":
                    EndCell();
                    if (!closing)
                    {
                        cell = new StringBuilder();
                        cellSpan = ColSpan(tag.Groups[3].Value);
                    }
                    break;
                case "br":
                case "p":
                case "div":
                case "li":
                    cell?.Append(' ');
                    break;
            }
        }

        EndRow();
        return rows;
    }

    private static int ColSpan(string attributes)
    {
        var match = ColSpanAttribute().Match(attributes);
        return match.Success && int.TryParse(match.Groups[1].Value, out var span) ? Math.Clamp(span, 1, 100) : 1;
    }

    private static string Clean(string raw) =>
        Whitespace().Replace(WebUtility.HtmlDecode(raw).Replace(' ', ' '), " ").Trim();

    [GeneratedRegex(@"<!--.*?-->|<(script|style|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StripNonContent();

    [GeneratedRegex(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*)>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"colspan\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ColSpanAttribute();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
