using System.Globalization;

namespace ArgoBooks.Core.Services;

/// <summary>
/// How every amount is written out, on the desktop (through <c>CurrencyInfo.Format</c>) and on the
/// phone, which has no currency table of its own and gets the symbol from the snapshot.
/// </summary>
public static class MoneyFormat
{
    /// <summary>
    /// "$1,234.56", or "¥1,235" for a currency without minor units. Invariant grouping and decimal
    /// separators, so the machine's locale never produces a hybrid such as "$1.234,56".
    /// </summary>
    public static string Format(decimal amount, string symbol, int decimalPlaces) =>
        symbol + amount.ToString(decimalPlaces == 0 ? "N0" : "N2", CultureInfo.InvariantCulture);
}
