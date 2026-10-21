using System.Globalization;
using Avalonia.Data.Converters;

namespace ArgoBooks.Converters;

/// <summary>
/// Converter for binding an optional decimal to TextBox.Text, where an empty box means no value rather than 0.
/// </summary>
public class OptionalDecimalConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance for use in XAML bindings.
    /// </summary>
    public static readonly OptionalDecimalConverter Instance = new();

    /// <summary>
    /// Shows the amount in the format passed as the converter parameter ("0.00" when none is given), or nothing when there is no value.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal d ? d.ToString(parameter as string ?? "0.00", culture) : string.Empty;

    /// <summary>
    /// Reads the box back, treating empty or unreadable text as no value so the box never shows a conversion error.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && decimal.TryParse(s, NumberStyles.Any, culture, out var result) ? result : null;
}
