namespace ArgoBooks.Mobile.Converters;

/// <summary>Converts a bottom-nav "is active" bool into an accent brush (active) or a dim brush (inactive).</summary>
public sealed class BoolToNavBrushConverter : IValueConverter
{
    public static readonly BoolToNavBrushConverter Instance = new();

    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#2FB8A8"));
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse("#808A94"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
