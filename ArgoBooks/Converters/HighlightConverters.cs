using System.Globalization;
using ArgoBooks.Core;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ArgoBooks.Converters;

/// <summary>
/// Multi-value converter that returns a highlight brush if both values are equal, otherwise transparent.
/// Used for visual highlighting of selected items in lists. Matches the menu-item focus style.
/// </summary>
public class HighlightBrushMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || !ConverterUtils.AreEqual(values[0], values[1]))
            return Brushes.Transparent;

        return ConverterUtils.ThemeBrush("SurfaceHoverBrush", AppColors.GrayLightest);
    }
}

/// <summary>
/// Multi-value converter that returns PrimaryBrush if both values are equal, otherwise transparent.
/// Used for the left border highlight on selected items.
/// </summary>
public class HighlightBorderBrushMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || !ConverterUtils.AreEqual(values[0], values[1]))
            return Brushes.Transparent;

        return ConverterUtils.ThemeBrush("PrimaryBrush", AppColors.Primary);
    }
}

/// <summary>
/// Multi-value converter that returns a thickness with left border if both values are equal.
/// Used for the left border highlight on selected items.
/// </summary>
public class HighlightBorderThicknessMultiConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => values.Count >= 2 && ConverterUtils.AreEqual(values[0], values[1])
            ? new Thickness(2, 0, 0, 0)
            : new Thickness(0);
}
