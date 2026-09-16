using System.Globalization;
using ArgoBooks.Core;
using Avalonia.Data.Converters;

namespace ArgoBooks.Converters;

/// <summary>
/// Multi-value converter that returns PrimaryBrush if the selected theme matches the compare value.
/// Binds to both SelectedTheme and SelectedAccentColor so it updates when either changes.
/// Values[0] = SelectedTheme, Values[1] = SelectedAccentColor (used to trigger re-evaluation).
/// Parameter = the theme value to compare against (e.g., "Light", "Dark", "System").
/// </summary>
public class ThemeBorderBrushMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSelected = values.Count >= 1 &&
                         string.Equals(values[0] as string, parameter as string, StringComparison.OrdinalIgnoreCase);

        return isSelected
            ? ConverterUtils.ThemeBrush("PrimaryBrush", AppColors.Primary)
            : ConverterUtils.ThemeBrush("BorderBrush", AppColors.ChartGrid);
    }
}
