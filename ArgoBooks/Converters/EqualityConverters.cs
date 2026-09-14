using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ArgoBooks.Converters;

/// <summary>
/// Converter that returns true if the bound value equals the parameter value.
/// A string parameter is parsed as an integer when the bound value is an int, so XAML can pass ConverterParameter=0.
/// ConvertBack returns the parameter when checked, for RadioButton IsChecked bindings.
/// </summary>
public class EqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string strParam && int.TryParse(strParam, out var paramInt))
            return intValue == paramInt;

        return Equals(value, parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter : BindingOperations.DoNothing;
}
