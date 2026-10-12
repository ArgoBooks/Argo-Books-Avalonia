using System.Globalization;
using Avalonia.Data.Converters;

namespace ArgoBooks.Converters;

/// <summary>
/// Converter for binding decimal properties to TextBox.Text.
/// Handles empty/null values by returning 0 for the decimal property.
/// </summary>
public class NullableDecimalConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance for use in XAML bindings.
    /// </summary>
    public static readonly NullableDecimalConverter Instance = new();

    /// <summary>
    /// Converts a decimal for display.
    /// For string targets (TextBox): formats as "0.00", or as the format passed as the converter
    /// parameter, which is how a box that shows something other than money picks its own places.
    /// For decimal targets: passes through directly.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter as string ?? "0.00";

        if (value is decimal d)
        {
            if (targetType == typeof(decimal?) || targetType == typeof(decimal))
                return d;

            return d.ToString(format, culture);
        }

        if (targetType == typeof(string))
            return 0m.ToString(format, culture);
        return 0m;
    }

    /// <summary>
    /// Converts back to decimal for binding.
    /// Returns 0 if value is null, empty string, or unparseable.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is decimal d) return d;
        if (value is string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return 0m;
            }
            if (decimal.TryParse(s, NumberStyles.Any, culture, out var result))
            {
                return result;
            }
        }
        return 0m;
    }
}
