using System.Globalization;
using System.Text;
using ArgoBooks.Core.Data;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// What CRA will accept in the text fields of an information return.
///
/// The XML specification is stricter than any of these fields look. A postal code must be six
/// characters with no space, a country must be an ISO 3166 three letter code, and a name may
/// contain letters, digits, an apostrophe, an ampersand, a period, a hyphen and a space, and
/// nothing else. A comma in a name rejects the whole submission.
///
/// None of that is obvious to someone typing an employee in, and all of it is discovered at the
/// February deadline if it is not caught earlier. So the rules live here once and are applied in
/// three places: the employee form refuses bad input, year end validation catches employees
/// entered before that form did, and the XML writer cleans whatever still reaches it so a file
/// is never emitted with a character CRA will bounce.
/// </summary>
public static class CraFormat
{
    /// <summary>Box 10's list, which is also the set of provinces a T4 address may use.</summary>
    public static readonly IReadOnlyList<string> ProvinceCodes =
        ["AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT"];

    private const string FrenchLower = "éàâçèêëîïôùüû";
    private const string FrenchUpper = "ÉÀÂÇÈÊËÎÏÔÙÜÛ";

    /// <summary>CRA's "Name specific acceptable characters", minus the letters and digits.</summary>
    private const string NamePunctuation = "'&.- ";

    /// <summary>The address set is the name set plus these two.</summary>
    private const string AddressExtra = "/#";

    public static bool IsProvinceCode(string? value) =>
        value != null && ProvinceCodes.Contains(value.Trim().ToUpperInvariant());

    #region Countries

    /// <summary>
    /// The three letter ISO 3166 code CRA wants, from whatever the app happens to be holding.
    ///
    /// The app stores a country NAME, because that is what every other address in it stores and
    /// what the country picker produces. Truncating that name to three characters is right
    /// only by coincidence: Canada gives CAN and Mexico gives
    /// MEX, but Germany gives GER where ISO says DEU, and the United States gives UNI.
    ///
    /// Returns null rather than a guess when the country cannot be identified, because the
    /// element is optional and omitting it is accepted while a wrong code is not.
    /// </summary>
    public static string? Alpha3Country(string? value)
    {
        string v = (value ?? string.Empty).Trim();

        if (v.Length == 0)
        {
            return null;
        }

        // The app's own country list first: the names the picker produces plus the aliases people
        // type (U.S., England, Korea), none of which is what the regional data calls them. Its
        // code is still checked against the regional data rather than trusted.
        if (Countries.Find(v) is { } country
            && Countries.GetAlpha3Code(country.Code) is { } fromCountry
            && Alpha3Codes.Value.Contains(fromCountry))
        {
            return fromCountry;
        }

        string upper = v.ToUpperInvariant();

        if (upper.Length == 3 && Alpha3Codes.Value.Contains(upper))
        {
            return upper;
        }

        if (upper.Length == 2 && Alpha2ToAlpha3.Value.TryGetValue(upper, out string? fromAlpha2))
        {
            return fromAlpha2;
        }

        return NameToAlpha3.Value.TryGetValue(upper, out string? fromName) ? fromName : null;
    }

    /// <summary>
    /// Every region the runtime knows. Built once, and defensively: constructing a RegionInfo
    /// throws for the culture names that do not describe a country, and a country lookup is not
    /// worth failing an export over.
    ///
    /// Declared before the three lookups built from it. Static initializers run in declaration
    /// order, so reading it from above would have the compiler treat it as possibly null even
    /// though the lambdas do not run until first use.
    /// </summary>
    private static readonly Lazy<List<RegionInfo>> Regions = new(() =>
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regions = new List<RegionInfo>();

        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                if (seen.Add(region.TwoLetterISORegionName))
                {
                    regions.Add(region);
                }
            }
            catch (ArgumentException)
            {
                // Not a region. Nothing to record.
            }
        }

        return regions;
    });

    private static readonly Lazy<HashSet<string>> Alpha3Codes = new(() =>
        [.. Regions.Value.Select(r => r.ThreeLetterISORegionName.ToUpperInvariant())]);

    private static readonly Lazy<Dictionary<string, string>> Alpha2ToAlpha3 = new(() =>
        Regions.Value
            .GroupBy(r => r.TwoLetterISORegionName.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().ThreeLetterISORegionName.ToUpperInvariant()));

    private static readonly Lazy<Dictionary<string, string>> NameToAlpha3 = new(() =>
        Regions.Value
            .GroupBy(r => r.EnglishName.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().ThreeLetterISORegionName.ToUpperInvariant()));

    public static bool IsCanada(string? country) => Alpha3Country(country) == "CAN";

    public static bool IsUnitedStates(string? country) => Alpha3Country(country) == "USA";

    #endregion

    #region Postal codes

    /// <summary>
    /// A postal code in the shape CRA's parser expects.
    ///
    /// The Canadian format is six characters, "alpha, numeric, alpha, numeric, alpha, numeric",
    /// and the specification allows a dash only for a USA or foreign code. It says nothing about
    /// a space, and a space is how almost every Canadian writes their postal code, so the space
    /// is removed here rather than sent and bounced.
    /// </summary>
    public static string NormalizePostalCode(string? value, string? country)
    {
        string v = (value ?? string.Empty).Trim().ToUpperInvariant();

        if (v.Length == 0)
        {
            return string.Empty;
        }

        if (IsUnitedStates(country))
        {
            string digits = new(v.Where(char.IsAsciiDigit).ToArray());

            return digits.Length switch
            {
                9 => $"{digits[..5]}-{digits[5..]}",
                5 => digits,
                _ => v,
            };
        }

        // Canada, and anything with no country recorded: a T4 address is Canadian unless it says
        // otherwise, and stripping the separators from a foreign code that turns out to be
        // Canadian is the fix, while leaving them in is the rejection.
        if (country == null || IsCanada(country) || Alpha3Country(country) == null)
        {
            return new string(v.Where(char.IsAsciiLetterOrDigit).ToArray());
        }

        return v;
    }

    /// <summary>Whether the code is one CRA will accept for that country.</summary>
    public static bool IsPostalCode(string? value, string? country)
    {
        string v = NormalizePostalCode(value, country);

        if (v.Length == 0)
        {
            return false;
        }

        if (IsUnitedStates(country))
        {
            string digits = new(v.Where(char.IsAsciiDigit).ToArray());
            return digits.Length is 5 or 9;
        }

        if (country != null && !IsCanada(country) && Alpha3Country(country) != null)
        {
            return v.Length <= 10;
        }

        return v.Length == 6
               && char.IsAsciiLetter(v[0]) && char.IsAsciiDigit(v[1])
               && char.IsAsciiLetter(v[2]) && char.IsAsciiDigit(v[3])
               && char.IsAsciiLetter(v[4]) && char.IsAsciiDigit(v[5]);
    }

    #endregion

    #region Names and addresses

    /// <summary>Strips whatever CRA does not accept in a name, and collapses the result.</summary>
    public static string CleanName(string? value) => Clean(value, address: false);

    /// <summary>As <see cref="CleanName"/>, for the fields that may also carry / and #.</summary>
    public static string CleanAddress(string? value) => Clean(value, address: true);

    /// <summary>
    /// The characters in this value that CRA would reject, in the order they appear and without
    /// repeats, or empty when there are none. Used to tell someone what to take out rather than
    /// only that something is wrong.
    /// </summary>
    public static string DisallowedCharacters(string? value, bool address = false)
    {
        var bad = new List<char>();

        foreach (char c in Normalize(value))
        {
            if (!IsAllowed(c, address) && !bad.Contains(c))
            {
                bad.Add(c);
            }
        }

        return new string([.. bad]);
    }

    private static string Clean(string? value, bool address)
    {
        var builder = new StringBuilder();

        foreach (char c in Normalize(value))
        {
            if (IsAllowed(c, address))
            {
                builder.Append(c);
            }
        }

        // Removing a character can leave a double space behind, and CRA counts the field length
        // in characters it accepts.
        return string.Join(' ', builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// Folds the punctuation a word processor substitutes back to what CRA lists. A curly
    /// apostrophe is not the apostrophe in the specification, and Windows produces one by
    /// default the moment anybody types O'Brien into anything but a plain text box.
    /// </summary>
    private static string Normalize(string? value) => (value ?? string.Empty)
        .Replace('‘', '\'')
        .Replace('’', '\'')
        .Replace('′', '\'')
        .Replace('‐', '-')
        .Replace('‑', '-')
        .Replace('‒', '-')
        .Replace('–', '-')
        .Replace('—', '-')
        .Trim();

    private static bool IsAllowed(char c, bool address) =>
        char.IsAsciiLetterOrDigit(c)
        || FrenchLower.Contains(c)
        || FrenchUpper.Contains(c)
        || NamePunctuation.Contains(c)
        || (address && AddressExtra.Contains(c));

    #endregion
}
