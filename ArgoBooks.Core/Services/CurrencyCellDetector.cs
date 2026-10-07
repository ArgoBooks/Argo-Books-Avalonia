using System.Globalization;
using System.Text;
using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The result of inspecting a single amount cell for currency information.
/// </summary>
/// <param name="Amount">The numeric value parsed from the cell (0 when there is no number).</param>
/// <param name="Code">
/// The resolved ISO 4217 code when the currency is unambiguous (an explicit code, or a symbol
/// used by exactly one currency). <see langword="null"/> when the cell carries no currency
/// marker, or when the only marker is an ambiguous symbol.
/// </param>
/// <param name="AmbiguousSymbol">
/// The raw symbol (e.g. "$") when the cell's only currency marker is a symbol shared by several
/// currencies and no explicit code disambiguates it. <see langword="null"/> otherwise.
/// </param>
/// <param name="Candidates">
/// The candidate ISO codes for <see cref="AmbiguousSymbol"/> (priority-ordered), or empty.
/// </param>
public readonly record struct CurrencyDetection(
    decimal Amount,
    string? Code,
    string? AmbiguousSymbol,
    IReadOnlyList<string> Candidates);

/// <summary>
/// Deterministically reads currency information from a raw amount cell string, e.g. "$10 CAD",
/// "£100", "€50", "$10", "1,234.56". Pure and side-effect free; the single source of truth for
/// both interpreting in-cell currency and parsing the numeric amount.
///
/// <para>Precedence: an explicit ISO code in the cell wins; then an unambiguous symbol; then an
/// ambiguous symbol (reported for the caller to resolve); otherwise no currency.</para>
/// </summary>
public static class CurrencyCellDetector
{
    private static readonly IReadOnlyList<string> EmptyCodes = [];

    /// <summary>Glyph symbols (contain a non-letter char, e.g. "$", "€", "R$"), longest first.</summary>
    private static readonly string[] GlyphSymbols =
        CurrencyInfo.CodesBySymbol.Keys
            .Where(s => s.Any(ch => !char.IsLetter(ch)))
            .OrderByDescending(s => s.Length)
            .ToArray();

    /// <summary>Alphabetic-only symbols (e.g. "kr", "CHF", "lei"), matched as whole tokens.</summary>
    private static readonly HashSet<string> AlphaSymbols =
        new(CurrencyInfo.CodesBySymbol.Keys.Where(s => s.All(char.IsLetter)), StringComparer.OrdinalIgnoreCase);

    /// <summary>All currency tokens to strip when isolating the number (symbols + ISO codes), longest first.</summary>
    private static readonly string[] StripTokens =
        CurrencyInfo.CodesBySymbol.Keys
            .Concat(CurrencyInfo.All.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(s => s.Length)
            .ToArray();

    /// <summary>
    /// Inspects a cell and returns its amount plus any currency it carries (resolved or ambiguous).
    /// </summary>
    public static CurrencyDetection Detect(string? cell)
    {
        if (string.IsNullOrWhiteSpace(cell))
            return new CurrencyDetection(0m, null, null, EmptyCodes);

        var raw = cell.Trim();
        var amount = ParseAmount(raw);

        // 1. Explicit ISO code: a 3-letter alphabetic token that is a known currency code.
        string? explicitCode = null;
        bool conflictingCodes = false;
        foreach (var token in AlphaTokens(raw))
        {
            if (token.Length == 3 && CurrencyInfo.All.ContainsKey(token))
            {
                var up = token.ToUpperInvariant();
                if (explicitCode is null) explicitCode = up;
                else if (!string.Equals(explicitCode, up, StringComparison.Ordinal)) conflictingCodes = true;
            }
        }
        if (explicitCode is not null && !conflictingCodes)
            return new CurrencyDetection(amount, explicitCode, null, EmptyCodes);

        // 2. Symbol: prefer a glyph symbol (matched longest-first), else an alphabetic symbol token.
        var symbol = FindGlyphSymbol(raw) ?? FindAlphaSymbol(raw);
        if (symbol is not null)
        {
            var codes = CurrencyInfo.CandidatesForSymbol(symbol);
            if (codes.Count == 1)
                return new CurrencyDetection(amount, codes[0], null, EmptyCodes);
            if (codes.Count > 1)
                return new CurrencyDetection(amount, null, symbol, codes);
        }

        // 3. No currency marker.
        return new CurrencyDetection(amount, null, null, EmptyCodes);
    }

    /// <summary>
    /// Parses the numeric value from a possibly currency-decorated string. Strips known currency
    /// symbols and ISO codes, treats parentheses as negative, and parses with invariant culture.
    /// Shared by <see cref="SpreadsheetRowReader.ParseDecimalString"/> so amount parsing is uniform.
    /// </summary>
    public static decimal ParseAmount(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0m;

        var cleaned = s.Trim();

        // Parentheses denote a negative amount: (123.45) -> -123.45, "($100)" -> -100. Unwrap
        // them FIRST so any currency symbol they enclose becomes boundary-adjacent and strippable.
        bool negative = false;
        if (cleaned.StartsWith('(') && cleaned.EndsWith(')'))
        {
            negative = true;
            cleaned = cleaned[1..^1].Trim();
        }

        // A sign written before the symbol, "-$45.00". The symbol is only stripped from the very
        // start, so the sign in front of it left the whole amount unreadable and it came in as 0.
        if (cleaned.Length > 1 && cleaned[0] is '-' or '+' or '−' && !char.IsDigit(cleaned[1]) && cleaned[1] != '.' && cleaned[1] != ',')
        {
            if (cleaned[0] != '+') negative = !negative;
            cleaned = cleaned[1..].Trim();
        }

        // Strip currency symbols/codes only where they actually decorate the amount: at the start or end of the string.
        bool stripped = true;
        while (stripped && cleaned.Length > 0)
        {
            stripped = false;
            foreach (var token in StripTokens)
            {
                if (cleaned.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned[token.Length..].Trim();
                    stripped = true;
                }
                if (cleaned.EndsWith(token, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned[..^token.Length].Trim();
                    stripped = true;
                }
            }
        }

        if (!TryParseFlexible(cleaned, out var result))
            return 0m;
        return negative ? -result : result;
    }

    /// <summary>
    /// Parses a numeric string that may use either US (<c>1,234.56</c>) or European
    /// (<c>1.234,56</c> / <c>1234,56</c>) grouping and decimal separators, plus space grouping
    /// (<c>1 234,56</c>). The separator nearest the end is treated as the decimal separator; the
    /// other is treated as a thousands separator and removed. Single-separator strings stay on the
    /// US interpretation unless the pattern is unambiguously a decimal comma (one comma followed by
    /// one or two digits), so existing US inputs are never reinterpreted. Returns
    /// <see langword="false"/> when the string is not a number.
    /// </summary>
    private static bool TryParseFlexible(string s, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(s)) return false;

        // Whitespace (incl. NBSP) is only ever a thousands separator here; drop it.
        var t = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (t.Length == 0) return false;

        int lastDot = t.LastIndexOf('.');
        int lastComma = t.LastIndexOf(',');

        string normalized;
        if (lastDot >= 0 && lastComma >= 0)
        {
            // Both separators present: the last one is the decimal point, the other is grouping.
            char dec = lastDot > lastComma ? '.' : ',';
            char group = dec == '.' ? ',' : '.';
            normalized = t.Replace(group.ToString(), string.Empty);
            if (dec == ',') normalized = normalized.Replace(',', '.');
        }
        else if (lastComma >= 0)
        {
            // Only comma(s). A single comma with one or two trailing digits is a decimal comma
            // (e.g. "1234,56"); anything else (3-digit group, or multiple commas) is US grouping.
            int commaCount = t.Count(c => c == ',');
            int trailing = t.Length - lastComma - 1;
            normalized = commaCount == 1 && trailing is 1 or 2
                ? t.Replace(',', '.')
                : t.Replace(",", string.Empty);
        }
        else if (lastDot >= 0 && t.Count(c => c == '.') > 1)
        {
            // Only dots, more than one: dots are grouping (e.g. European "1.234.567").
            normalized = t.Replace(".", string.Empty);
        }
        else
        {
            // A single dot (decimal point) or no separator at all: already invariant.
            normalized = t;
        }

        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Returns the maximal runs of letters in the string (currency-code candidates).</summary>
    private static IEnumerable<string> AlphaTokens(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetter(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private static string? FindGlyphSymbol(string raw)
    {
        foreach (var sym in GlyphSymbols)
            if (raw.Contains(sym, StringComparison.Ordinal))
                return sym;
        return null;
    }

    private static string? FindAlphaSymbol(string raw)
    {
        foreach (var token in AlphaTokens(raw))
            if (AlphaSymbols.TryGetValue(token, out var canonical))
                return canonical;
        return null;
    }
}
