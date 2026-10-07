using System.Globalization;
using ArgoBooks.Localization;
using Avalonia.Data.Converters;

namespace ArgoBooks.Converters;

/// <summary>
/// Shows a province or territory by its full name while the bound value stays the two letter
/// code.
///
/// The code is what everything downstream runs on: the rate table is keyed by it, the T4 carries
/// it in box 10, and the calculator dispatches on "QC" before it looks a province up. So only
/// the display goes through here, exactly as the dental code does.
///
/// The result is translated, and anything unrecognised is translated as-is. That is what lets
/// the filter list use this instead of the plain translate converter and still show its "All"
/// entry in the user's language.
/// </summary>
public class ProvinceNameConverter : IValueConverter
{
    public static readonly ProvinceNameConverter Instance = new();

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AB"] = "Alberta",
        ["BC"] = "British Columbia",
        ["MB"] = "Manitoba",
        ["NB"] = "New Brunswick",
        ["NL"] = "Newfoundland and Labrador",
        ["NS"] = "Nova Scotia",
        ["NT"] = "Northwest Territories",
        ["NU"] = "Nunavut",
        ["ON"] = "Ontario",
        ["PE"] = "Prince Edward Island",
        ["QC"] = "Quebec",
        ["SK"] = "Saskatchewan",
        ["YT"] = "Yukon",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string code || code.Length == 0)
        {
            return string.Empty;
        }

        // Translate falls back to the input when there is no entry, so an untranslated province name still reads correctly rather than coming out blank. Only the name is translated.
        return Names.TryGetValue(code, out string? name) ? name.Translate() : code;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("The combo box binds the code directly; only display goes through here.");
}
