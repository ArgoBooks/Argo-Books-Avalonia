using System.Globalization;

namespace ArgoBooks.Core.Utilities;

/// <summary>
/// Turns a double into the decimal a person would write for it, so a spreadsheet cell holding
/// -1284.9 becomes -1284.9 and not -1284.9000000000000909494701773.
/// </summary>
public static class DecimalFromDouble
{
    /// <summary>
    /// Keeps the double's first 15 significant digits, the most a double holds reliably.
    /// Throws <see cref="OverflowException"/> for NaN, infinity, and values too large for a decimal.
    /// </summary>
    public static decimal Convert(double value)
    {
        if (!double.IsFinite(value))
            throw new OverflowException($"{value} has no decimal equivalent.");

        return decimal.Parse(value.ToString("G15", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
