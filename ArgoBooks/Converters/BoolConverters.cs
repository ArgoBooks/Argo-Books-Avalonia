using System.Globalization;
using ArgoBooks.Core;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ArgoBooks.Services;

namespace ArgoBooks.Converters;

/// <summary>
/// Bool converters for the Categories page and general use.
/// </summary>
public static class BoolConverters
{
    /// <summary>
    /// Converts bool to "Expense Products" (true) or "Revenue Products" (false).
    /// </summary>
    public static readonly IValueConverter ToExpensesOrRevenue =
        new FuncValueConverter<bool, string>(value => value ? "Expense Products" : "Revenue Products");

    /// <summary>
    /// Converts bool to "Expense Categories" (true) or "Revenue Categories" (false).
    /// </summary>
    public static readonly IValueConverter ToExpenseOrRevenueCategories =
        new FuncValueConverter<bool, string>(value => value ? "Expense Categories" : "Revenue Categories");

    /// <summary>
    /// Converts bool (isChild) to left margin indent for child rows.
    /// </summary>
    public static readonly IValueConverter ToChildIndent =
        new FuncValueConverter<bool, Thickness>(value => value ? new Thickness(24, 0, 0, 0) : new Thickness(0));

    /// <summary>
    /// Converts bool (isActive) to status badge background color.
    /// Active = green (#DCFCE7), Inactive = gray (#F3F4F6).
    /// </summary>
    public static readonly IValueConverter ToStatusBackground =
        new FuncValueConverter<bool, IBrush>(value =>
            new SolidColorBrush(Color.Parse(value ? AppColors.SuccessLight : AppColors.GrayLightest)));

    /// <summary>
    /// Converts bool (hasError) to border brush.
    /// Error = red (#dc2626), No error = default border color.
    /// </summary>
    public static readonly IValueConverter ToErrorBorderBrush =
        new FuncValueConverter<bool, IBrush>(value =>
        {
            if (value)
            {
                if (Application.Current?.Resources != null &&
                    Application.Current.Resources.TryGetResource("ErrorBrush", Application.Current.ActualThemeVariant, out var resource) &&
                    resource is IBrush brush)
                {
                    return brush;
                }
                return new SolidColorBrush(Color.Parse(AppColors.Error));
            }
            // Return BorderBrush for no error state to preserve the control's default border
            if (Application.Current?.Resources != null &&
                Application.Current.Resources.TryGetResource("BorderBrush", Application.Current.ActualThemeVariant, out var borderResource) &&
                borderResource is IBrush borderBrush)
            {
                return borderBrush;
            }
            return new SolidColorBrush(Color.Parse(AppColors.ChartGrid));
        });

    /// <summary>
    /// Converts bool (isActive) to status badge foreground color.
    /// Active = green (#166534), Inactive = gray (#4B5563).
    /// </summary>
    public static readonly IValueConverter ToStatusForeground =
        new FuncValueConverter<bool, IBrush>(value =>
            new SolidColorBrush(Color.Parse(value ? AppColors.SuccessText : AppColors.GrayText)));

    /// <summary>
    /// Converts bool (isPaid) to paid badge background.
    /// Paid = green (#DCFCE7), Unpaid = red (#FEF2F2).
    /// </summary>
    public static readonly IValueConverter ToPaidBackground =
        new FuncValueConverter<bool, IBrush>(value =>
            new SolidColorBrush(Color.Parse(value ? AppColors.SuccessLight : AppColors.ErrorLightest)));

    /// <summary>
    /// Converts bool (isPaid) to paid badge foreground.
    /// Paid = green (#166534), Unpaid = red (#991B1B).
    /// </summary>
    public static readonly IValueConverter ToPaidForeground =
        new FuncValueConverter<bool, IBrush>(value =>
            new SolidColorBrush(Color.Parse(value ? AppColors.SuccessText : AppColors.ErrorDarkest)));

    /// <summary>
    /// Converts bool (isPaid) to paid badge text.
    /// </summary>
    public static readonly IValueConverter ToPaidText =
        new FuncValueConverter<bool, string>(value =>
            LanguageService.Instance.Translate(value ? "Yes" : "No"));

    /// <summary>
    /// Converts bool (isFullscreen) to modal horizontal alignment.
    /// Both fullscreen and normal modes use Center alignment.
    /// </summary>
    public static readonly IValueConverter ToFullscreenHorizontalAlignment =
        new FuncValueConverter<bool, Avalonia.Layout.HorizontalAlignment>(value =>
            Avalonia.Layout.HorizontalAlignment.Center);

    /// <summary>
    /// Converts bool (isFullscreen) to modal vertical alignment.
    /// Fullscreen = Stretch, Normal = Center.
    /// </summary>
    public static readonly IValueConverter ToFullscreenVerticalAlignment =
        new FuncValueConverter<bool, Avalonia.Layout.VerticalAlignment>(value =>
            value ? Avalonia.Layout.VerticalAlignment.Stretch : Avalonia.Layout.VerticalAlignment.Center);

    /// <summary>
    /// Converts bool (isFullscreen) to modal width.
    /// Fullscreen = NaN (stretch), Normal = default or parameter value.
    /// Supports ConverterParameter for custom normal width.
    /// </summary>
    public static readonly IValueConverter ToFullscreenWidth = new FullscreenDimensionConverter(800);

    /// <summary>
    /// Converts bool (isFullscreen) to modal height.
    /// Fullscreen = NaN (stretch), Normal = default or parameter value.
    /// Supports ConverterParameter for custom normal height.
    /// </summary>
    public static readonly IValueConverter ToFullscreenHeight = new FullscreenDimensionConverter(650);

    /// <summary>
    /// Converts bool to height based on parameter "TrueValue,FalseValue".
    /// </summary>
    public static readonly IValueConverter ToHeight = new BoolToDoubleConverter();

    /// <summary>
    /// Converts bool (isFullscreen) to modal margin.
    /// Fullscreen = 40px top margin (clears title bar) + 24px bottom, Normal = 0.
    /// </summary>
    public static readonly IValueConverter ToFullscreenMargin =
        new FuncValueConverter<bool, Thickness>(value => value ? new Thickness(0, 40, 0, 24) : new Thickness(0));

    /// <summary>
    /// Converts bool (isFullscreen) to modal max width.
    /// Fullscreen = Infinity (no max), Normal = parameter or 1200.
    /// </summary>
    public static readonly IValueConverter ToFullscreenMaxWidth = new FullscreenMinMaxConverter(1200, false);

    /// <summary>
    /// Converts bool (isFullscreen) to fullscreen icon path data.
    /// Fullscreen = exit fullscreen icon, Normal = enter fullscreen icon.
    /// </summary>
    public static readonly IValueConverter ToFullscreenIcon =
        new FuncValueConverter<bool, string>(value => value ? Icons.FullscreenExit : Icons.FullscreenEnter);

    /// <summary>
    /// Converts a file path string to a Bitmap image.
    /// Returns null if the file doesn't exist or can't be loaded.
    /// </summary>
    public static readonly IValueConverter ToImageSource = new FilePathToImageConverter();

    /// <summary>
    /// Converts bool (isExpenseTab) to table title for returns page.
    /// True = "Expense Returns", False = "Customer Returns".
    /// </summary>
    public static readonly IValueConverter ToReturnTableTitle =
        new FuncValueConverter<bool, string>(value =>
            LanguageService.Instance.Translate(value ? "Expense Returns" : "Customer Returns"));

    /// <summary>
    /// Converts bool (isExpenseTab) to column header.
    /// True = "Supplier", False = "Customer".
    /// </summary>
    public static readonly IValueConverter ToSupplierOrCustomerHeader =
        new FuncValueConverter<bool, string>(value =>
            LanguageService.Instance.Translate(value ? "Supplier" : "Customer"));

    /// <summary>
    /// Converts bool to ScrollBarVisibility.
    /// True = Auto (show when needed), False = Disabled.
    /// </summary>
    public static readonly IValueConverter ToScrollBarVisibility =
        new FuncValueConverter<bool, Avalonia.Controls.Primitives.ScrollBarVisibility>(value =>
            value ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);

    /// <summary>
    /// Converts bool (isExpanded) to header border thickness.
    /// Expanded = bottom border only, Collapsed = no border.
    /// </summary>
    public static readonly IValueConverter ToHeaderBorderThickness =
        new FuncValueConverter<bool, Thickness>(value => value ? new Thickness(0, 0, 0, 1) : new Thickness(0));

    /// <summary>
    /// Converts bool (isCompleted) to circle border thickness.
    /// Completed = 0 (no border), Not completed = 2px border.
    /// </summary>
    public static readonly IValueConverter ToCircleBorderThickness =
        new FuncValueConverter<bool, Thickness>(value => value ? new Thickness(0) : new Thickness(2));

    /// <summary>
    /// Converts bool (isOutsideMonth) to opacity for calendar day cells.
    /// Outside month = 0.35 (dim), inside month = 1.0 (full).
    /// </summary>
    public static readonly IValueConverter OutsideMonthOpacity =
        new FuncValueConverter<bool, double>(value => value ? 0.35 : 1.0);

    /// <summary>
    /// Converts bool (exceedsLimit) to ErrorBrush when true, TextTertiaryBrush when false.
    /// Used to highlight hint labels that should turn red when their associated value is invalid.
    /// </summary>
    public static readonly IValueConverter ToErrorOrTertiaryBrush =
        new FuncValueConverter<bool, IBrush>(value =>
        {
            var key = value ? "ErrorBrush" : "TextTertiaryBrush";
            if (Application.Current?.Resources != null &&
                Application.Current.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var resource) &&
                resource is IBrush brush)
            {
                return brush;
            }
            return new SolidColorBrush(Color.Parse(value ? AppColors.Error : AppColors.GrayText));
        });

}

/// <summary>
/// Converter that returns one of two colors based on a boolean value.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public string TrueColor { get; set; } = AppColors.Error;
    public object? FalseColor { get; set; } = AppColors.ChartAxis;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue)
            return GetBrush(FalseColor);

        return boolValue ? new SolidColorBrush(Color.Parse(TrueColor)) : GetBrush(FalseColor);
    }

    private static IBrush GetBrush(object? colorValue)
    {
        if (colorValue is string colorString)
        {
            try
            {
                return new SolidColorBrush(Color.Parse(colorString));
            }
            catch
            {
                // Try to get from resources
            }
        }

        if (colorValue is IBrush brush)
            return brush;

        // Try to get TextPrimaryBrush from resources
        if (Application.Current?.Resources != null &&
            Application.Current.Resources.TryGetResource("TextPrimaryBrush", Application.Current.ActualThemeVariant, out var resource) &&
            resource is IBrush textBrush)
        {
            return textBrush;
        }

        return new SolidColorBrush(Color.Parse(AppColors.ChartAxis));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converter that returns one of two brushes based on a boolean value.
/// Useful for dynamically changing colors in XAML.
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    public object? TrueValue { get; set; }
    public object? FalseValue { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue)
            return FalseValue;

        var selectedValue = boolValue ? TrueValue : FalseValue;

        if (selectedValue is IBrush brush)
            return brush;

        if (selectedValue is string colorString)
        {
            try
            {
                return new SolidColorBrush(Color.Parse(colorString));
            }
            catch
            {
                return Brushes.Transparent;
            }
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converter that returns strikethrough text decoration when the bool value is true.
/// </summary>
public class BoolToTextDecorationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue)
        {
            return TextDecorations.Strikethrough;
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converter for fullscreen modal dimensions.
/// Parameter format: "normalValue" or "normalValue,fullscreenValue"
/// When fullscreen is true: returns fullscreenValue if provided, otherwise NaN (stretch).
/// When fullscreen is false: returns normalValue or the default.
/// </summary>
public class FullscreenDimensionConverter : IValueConverter
{
    private readonly double _defaultValue;

    public FullscreenDimensionConverter(double defaultValue)
    {
        _defaultValue = defaultValue;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool isFullscreen)
            return _defaultValue;

        double normalValue = _defaultValue;
        double? fullscreenValue = null;

        if (parameter is string stringParam)
        {
            var parts = stringParam.Split(',');
            if (double.TryParse(parts[0].Trim(), out var parsed))
                normalValue = parsed;
            if (parts.Length > 1 && double.TryParse(parts[1].Trim(), out var parsedFullscreen))
                fullscreenValue = parsedFullscreen;
        }
        else if (parameter is double doubleParam)
        {
            normalValue = doubleParam;
        }

        if (isFullscreen)
            return fullscreenValue ?? double.NaN;

        return normalValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converter that returns one of two double values based on a boolean.
/// Parameter format: "TrueValue,FalseValue" (e.g., "850,750")
/// </summary>
public class BoolToDoubleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue || parameter is not string paramString)
            return 0.0;

        var parts = paramString.Split(',');
        if (parts.Length != 2)
            return 0.0;

        if (double.TryParse(parts[0].Trim(), out var trueValue) &&
            double.TryParse(parts[1].Trim(), out var falseValue))
        {
            return boolValue ? trueValue : falseValue;
        }

        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converter for fullscreen modal min/max width constraints.
/// When fullscreen is true: returns 0 for min, Infinity for max (no constraints).
/// When fullscreen is false: returns the default or parameter value.
/// </summary>
public class FullscreenMinMaxConverter : IValueConverter
{
    private readonly double _defaultValue;
    private readonly bool _isMin;

    public FullscreenMinMaxConverter(double defaultValue, bool isMin)
    {
        _defaultValue = defaultValue;
        _isMin = isMin;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool isFullscreen)
            return _defaultValue;

        if (isFullscreen)
            return _isMin ? 0.0 : double.PositiveInfinity;

        // Use parameter if provided, otherwise use default
        if (parameter is double doubleParam)
            return doubleParam;

        if (parameter is string stringParam && double.TryParse(stringParam, out var parsedValue))
            return parsedValue;

        return _defaultValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

