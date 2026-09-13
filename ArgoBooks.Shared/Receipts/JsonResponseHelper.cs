using System.Text.RegularExpressions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Shared utilities for cleaning LLM JSON responses. Public: several ArgoBooks.Core callers (GeminiService, ReportRenderer,
/// SpreadsheetAnalysisService, etc.) are in a different assembly and need cross-assembly access.
/// </summary>
public static partial class JsonResponseHelper
{
    /// <summary>
    /// Matches numbers with thousand-separator commas in JSON values (e.g. 1,999 or 12,345.67).
    /// Only matches when preceded by a JSON value context (colon or array start) to avoid
    /// stripping commas that are actual JSON delimiters.
    /// </summary>
    [GeneratedRegex(@"(?<=[:,\[]\s*)(\d{1,3})(,\d{3})+(?=\.\d|\s*[,\]\}])")]
    private static partial Regex ThousandSeparatorRegex();

    /// <summary>
    /// Removes thousand-separator commas from numeric values in JSON so the parser doesn't choke.
    /// For example, "totalPrice": 1,999.99 becomes "totalPrice": 1999.99.
    /// </summary>
    public static string SanitizeJsonNumbers(string json)
    {
        return ThousandSeparatorRegex().Replace(json, m => m.Value.Replace(",", ""));
    }

    /// <summary>
    /// Strips markdown code block fences (```json ... ```) from an LLM response,
    /// returning clean JSON suitable for parsing.
    /// </summary>
    public static string StripMarkdownCodeBlock(string response)
    {
        var cleaned = response.Trim();

        // Strip leading ```json or ``` fence (with or without newline)
        if (cleaned.StartsWith("```"))
        {
            // Find end of opening fence line
            var fenceEnd = cleaned.IndexOf('\n');
            if (fenceEnd == -1)
            {
                // No newline, strip all backticks from start
                cleaned = cleaned.TrimStart('`').Trim();
            }
            else
            {
                // Check if fence tag has content on the same line (e.g. "```json{")
                var fenceLine = cleaned[..fenceEnd].TrimEnd();
                var afterTag = fenceLine.TrimStart('`').TrimStart();
                if (afterTag.Length > 0 && afterTag.Length <= 10 && !afterTag.StartsWith('{') && !afterTag.StartsWith('['))
                {
                    // Language tag like "json", skip the whole line
                    cleaned = cleaned[(fenceEnd + 1)..];
                }
                else if (afterTag.StartsWith('{') || afterTag.StartsWith('['))
                {
                    // JSON starts on the fence line, just strip the backticks
                    cleaned = cleaned[fenceLine.IndexOf(afterTag[0])..];
                }
                else
                {
                    cleaned = cleaned[(fenceEnd + 1)..];
                }
            }

            // Strip trailing ``` fence
            var closingFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
                cleaned = cleaned[..closingFence].Trim();
        }

        return cleaned;
    }
}
