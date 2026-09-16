using Avalonia;
using Avalonia.Media;

namespace ArgoBooks.Converters;

/// <summary>
/// Shared utility methods for converters.
/// </summary>
internal static class ConverterUtils
{
    public static bool AreEqual(object? value1, object? value2) => Equals(value1, value2);

    /// <summary>
    /// Looks up a brush resource for the current theme variant, falling back to a hex color.
    /// </summary>
    public static IBrush ThemeBrush(string key, string fallbackHex)
    {
        var app = Application.Current;
        if (app?.Resources != null &&
            app.Resources.TryGetResource(key, app.ActualThemeVariant, out var resource) &&
            resource is IBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Color.Parse(fallbackHex));
    }
}
