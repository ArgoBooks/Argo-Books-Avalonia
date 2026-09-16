using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ArgoBooks.Converters;

/// <summary>
/// Multi-value converter that returns an item's image using the ImageMemberPath.
/// Values[0] = the item, Values[1] = the ImageMemberPath string.
/// </summary>
public class ImageMemberMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not { } item || values[1] is not string { Length: > 0 } path)
            return null;

        return item.GetType().GetProperty(path)?.GetValue(item) as IImage;
    }
}
