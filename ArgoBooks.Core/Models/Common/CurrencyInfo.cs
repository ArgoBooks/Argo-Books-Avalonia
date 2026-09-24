namespace ArgoBooks.Core.Models.Common;

/// <summary>
/// Represents information about a currency including its code, symbol, and display name.
/// </summary>
public class CurrencyInfo
{
    /// <summary>
    /// ISO 4217 currency code (e.g., "USD", "EUR", "CAD").
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Currency symbol (e.g., "$", "€", "£").
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Full display name (e.g., "US Dollar", "Euro", "Canadian Dollar").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Number of decimal places typically used (usually 2, but 0 for JPY, KRW, etc.).
    /// </summary>
    public int DecimalPlaces { get; }

    /// <summary>
    /// Creates a new CurrencyInfo instance.
    /// </summary>
    public CurrencyInfo(string code, string symbol, string name, int decimalPlaces = 2)
    {
        Code = code;
        Symbol = symbol;
        Name = name;
        DecimalPlaces = decimalPlaces;
    }

    /// <summary>
    /// Gets the display string for dropdown (e.g., "USD - US Dollar ($)").
    /// </summary>
    public string DisplayString => $"{Code} - {Name} ({Symbol})";

    /// <summary>
    /// Formats an amount with this currency's symbol.
    /// </summary>
    /// <param name="amount">The amount to format.</param>
    /// <param name="includeCode">Whether to include the currency code after the amount.</param>
    /// <returns>Formatted string like "$1,234.56" or "$1,234.56 USD".</returns>
    public string Format(decimal amount, bool includeCode = false)
    {
        // InvariantCulture so grouping/decimal separators are consistent ("$1,234.56") regardless of
        // the machine locale. With CurrentCulture a German machine would render "$1.234,56", a hybrid
        // that is wrong everywhere and would also appear on customer-facing invoices.
        var formatted = DecimalPlaces == 0
            ? $"{Symbol}{amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}"
            : $"{Symbol}{amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}";

        return includeCode ? $"{formatted} {Code}" : formatted;
    }

    public override string ToString() => DisplayString;

    /// <summary>
    /// All supported currencies with their information.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, CurrencyInfo> All = new Dictionary<string, CurrencyInfo>(StringComparer.OrdinalIgnoreCase)
    {
        ["AED"] = new("AED", "AED", "UAE Dirham"),
        ["ALL"] = new("ALL", "L", "Albanian Lek"),
        ["ARS"] = new("ARS", "AR$", "Argentine Peso"),
        ["AUD"] = new("AUD", "$", "Australian Dollar"),
        ["BAM"] = new("BAM", "KM", "Bosnia-Herzegovina Mark"),
        // "Tk" rather than ৳: macOS Helvetica, which the PDFs render with there, ships no
        // Bengali glyph, so the symbol prints as an empty box. See CurrencyListTests.
        ["BDT"] = new("BDT", "Tk", "Bangladeshi Taka"),
        ["BGN"] = new("BGN", "лв", "Bulgarian Lev"),
        ["BRL"] = new("BRL", "R$", "Brazilian Real"),
        ["BYN"] = new("BYN", "Br", "Belarusian Ruble"),
        ["CAD"] = new("CAD", "$", "Canadian Dollar"),
        ["CHF"] = new("CHF", "CHF", "Swiss Franc"),
        ["CLP"] = new("CLP", "CL$", "Chilean Peso", 0),
        ["CNY"] = new("CNY", "¥", "Chinese Yuan"),
        ["COP"] = new("COP", "CO$", "Colombian Peso"),
        ["CZK"] = new("CZK", "Kč", "Czech Koruna"),
        ["DKK"] = new("DKK", "kr", "Danish Krone"),
        ["EGP"] = new("EGP", "E£", "Egyptian Pound"),
        ["EUR"] = new("EUR", "€", "Euro"),
        ["GBP"] = new("GBP", "£", "British Pound"),
        ["GHS"] = new("GHS", "₵", "Ghanaian Cedi"),
        ["HKD"] = new("HKD", "HK$", "Hong Kong Dollar"),
        ["HUF"] = new("HUF", "Ft", "Hungarian Forint", 0),
        ["IDR"] = new("IDR", "Rp", "Indonesian Rupiah"),
        // "ILS" rather than ₪: same as THB, no glyph for it in the Mac PDF font.
        ["ILS"] = new("ILS", "ILS", "Israeli Shekel"),
        ["INR"] = new("INR", "₹", "Indian Rupee"),
        ["ISK"] = new("ISK", "kr", "Icelandic Króna", 0),
        ["JPY"] = new("JPY", "¥", "Japanese Yen", 0),
        ["KES"] = new("KES", "KSh", "Kenyan Shilling"),
        ["KRW"] = new("KRW", "₩", "South Korean Won", 0),
        ["LKR"] = new("LKR", "Rs", "Sri Lankan Rupee"),
        ["MAD"] = new("MAD", "MAD", "Moroccan Dirham"),
        ["MKD"] = new("MKD", "ден", "Macedonian Denar"),
        ["MXN"] = new("MXN", "MX$", "Mexican Peso"),
        ["MYR"] = new("MYR", "RM", "Malaysian Ringgit"),
        ["NGN"] = new("NGN", "₦", "Nigerian Naira"),
        ["NOK"] = new("NOK", "kr", "Norwegian Krone"),
        ["NZD"] = new("NZD", "NZ$", "New Zealand Dollar"),
        ["PEN"] = new("PEN", "S/", "Peruvian Sol"),
        ["PHP"] = new("PHP", "₱", "Philippine Peso"),
        ["PKR"] = new("PKR", "₨", "Pakistani Rupee"),
        ["PLN"] = new("PLN", "zł", "Polish Złoty"),
        ["QAR"] = new("QAR", "QAR", "Qatari Riyal"),
        ["RON"] = new("RON", "lei", "Romanian Leu"),
        ["RSD"] = new("RSD", "дин", "Serbian Dinar"),
        // "руб" rather than ₽: same as BGN and RSD, Cyrillic the Mac PDF font does ship.
        ["RUB"] = new("RUB", "руб", "Russian Ruble"),
        ["SAR"] = new("SAR", "SAR", "Saudi Riyal"),
        ["SEK"] = new("SEK", "kr", "Swedish Krona"),
        ["SGD"] = new("SGD", "S$", "Singapore Dollar"),
        // "THB" rather than ฿: same as BDT, no Thai glyph in the Mac PDF font.
        ["THB"] = new("THB", "THB", "Thai Baht"),
        ["TRY"] = new("TRY", "₺", "Turkish Lira"),
        ["TWD"] = new("TWD", "NT$", "Taiwan Dollar"),
        ["UAH"] = new("UAH", "₴", "Ukrainian Hryvnia"),
        ["USD"] = new("USD", "$", "US Dollar"),
        ["VND"] = new("VND", "₫", "Vietnamese Dong", 0),
        ["ZAR"] = new("ZAR", "R", "South African Rand")
    };

    /// <summary>
    /// Priority/common currencies shown at the top of dropdowns.
    /// </summary>
    public static readonly IReadOnlyList<string> PriorityCodes = ["USD", "EUR", "CAD", "AUD", "GBP"];

    /// <summary>
    /// Reverse index of <see cref="All"/>: a currency symbol mapped to every ISO code that uses it.
    /// Built by inverting <see cref="All"/>, so ambiguity is data-driven rather than hardcoded
    /// (e.g. "$" -> [USD, CAD, AUD], "¥" -> [JPY, CNY], "kr" -> [DKK, ISK, NOK, SEK], "£" -> [GBP]).
    /// Within each symbol the codes are ordered by <see cref="PriorityCodes"/> first, then
    /// alphabetically, so callers can treat the first entry as the sensible default.
    /// Keyed case-insensitively to match <see cref="All"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CodesBySymbol = BuildSymbolIndex();

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildSymbolIndex()
    {
        int PriorityRank(string code)
        {
            for (int i = 0; i < PriorityCodes.Count; i++)
                if (string.Equals(PriorityCodes[i], code, StringComparison.OrdinalIgnoreCase))
                    return i;
            return int.MaxValue;
        }

        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in All.Values.GroupBy(c => c.Symbol, StringComparer.Ordinal))
        {
            var codes = group
                .Select(c => c.Code)
                .OrderBy(PriorityRank)
                .ThenBy(c => c, StringComparer.Ordinal)
                .ToList();
            map[group.Key] = codes;
        }
        return map;
    }

    /// <summary>
    /// When the symbol maps to exactly one currency, returns that code via <paramref name="code"/>
    /// and <see langword="true"/>. Otherwise returns <see langword="false"/> (unknown or ambiguous).
    /// </summary>
    public static bool TryResolveSymbol(string symbol, out string code)
    {
        if (CodesBySymbol.TryGetValue(symbol, out var codes) && codes.Count == 1)
        {
            code = codes[0];
            return true;
        }
        code = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns every ISO code that uses the given symbol (priority-ordered), or an empty list
    /// when the symbol is not recognized.
    /// </summary>
    public static IReadOnlyList<string> CandidatesForSymbol(string symbol) =>
        CodesBySymbol.TryGetValue(symbol, out var codes) ? codes : [];

    /// <summary>
    /// Gets currency info by code, or USD as fallback.
    /// </summary>
    public static CurrencyInfo GetByCode(string code)
    {
        if (string.IsNullOrEmpty(code))
            return All["USD"];

        return All.TryGetValue(code, out var info) ? info : All["USD"];
    }

    /// <summary>
    /// Gets the currency code from a display string like "USD - US Dollar ($)".
    /// </summary>
    public static string ParseCodeFromDisplayString(string displayString)
    {
        if (string.IsNullOrEmpty(displayString))
            return "USD";

        // Extract the code (first 3 characters before the dash)
        var dashIndex = displayString.IndexOf('-');
        if (dashIndex > 0)
        {
            return displayString[..dashIndex].Trim().ToUpperInvariant();
        }

        // If it's just a code, return it uppercase
        if (displayString.Length == 3)
        {
            return displayString.ToUpperInvariant();
        }

        return "USD";
    }

    /// <summary>
    /// Gets the symbol for a currency code.
    /// </summary>
    public static string GetSymbol(string code)
    {
        return GetByCode(code).Symbol;
    }

    /// <summary>
    /// Formats an amount using the specified currency code.
    /// </summary>
    /// <param name="amount">The amount to format.</param>
    /// <param name="currencyCode">The currency code (e.g., "USD", "EUR").</param>
    /// <returns>Formatted currency string.</returns>
    public static string FormatAmount(decimal amount, string currencyCode)
    {
        return GetByCode(currencyCode).Format(amount);
    }
}
