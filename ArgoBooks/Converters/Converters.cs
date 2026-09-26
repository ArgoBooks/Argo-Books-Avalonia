using ArgoBooks.Services;
using Avalonia.Data.Converters;

namespace ArgoBooks.Converters;

/// <summary>
/// Static class providing common converter instances for XAML binding.
/// </summary>
public static class Converters
{
    /// <summary>
    /// Multi-value converter for theme border brush that updates when accent color changes.
    /// </summary>
    public static readonly IMultiValueConverter ThemeBorderBrushMulti = new ThemeBorderBrushMultiConverter();

    /// <summary>
    /// Converts a boolean to "Finish" or "Next" text. Used by tutorial overlays
    /// where the last step actually ends the flow (Products, Categories).
    /// </summary>
    public static readonly IValueConverter BoolToFinishNext = new BoolToFixedStringConverter("Finish", "Next");

    /// <summary>
    /// Formats an amount already in the company's currency, through <see cref="CurrencyService.Format(decimal, bool)"/>.
    /// </summary>
    public static readonly IValueConverter Money = new FuncValueConverter<decimal, string>(amount => CurrencyService.Format(amount));
}
