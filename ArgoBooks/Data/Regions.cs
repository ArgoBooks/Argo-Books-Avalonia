namespace ArgoBooks.Data;

/// <summary>A province, territory or state, by its postal code and English name.</summary>
public sealed record Region(string Code, string Name);

/// <summary>
/// Provinces and states for the countries whose addresses pick from a list. The code is what gets
/// saved: payroll filings require a Canadian province as its two-letter code.
/// </summary>
public static class Regions
{
    public static IReadOnlyList<Region> Canada { get; } =
    [
        new("AB", "Alberta"),
        new("BC", "British Columbia"),
        new("MB", "Manitoba"),
        new("NB", "New Brunswick"),
        new("NL", "Newfoundland and Labrador"),
        new("NT", "Northwest Territories"),
        new("NS", "Nova Scotia"),
        new("NU", "Nunavut"),
        new("ON", "Ontario"),
        new("PE", "Prince Edward Island"),
        new("QC", "Quebec"),
        new("SK", "Saskatchewan"),
        new("YT", "Yukon"),
    ];

    public static IReadOnlyList<Region> UnitedStates { get; } =
    [
        new("AL", "Alabama"),
        new("AK", "Alaska"),
        new("AZ", "Arizona"),
        new("AR", "Arkansas"),
        new("CA", "California"),
        new("CO", "Colorado"),
        new("CT", "Connecticut"),
        new("DE", "Delaware"),
        new("DC", "District of Columbia"),
        new("FL", "Florida"),
        new("GA", "Georgia"),
        new("HI", "Hawaii"),
        new("ID", "Idaho"),
        new("IL", "Illinois"),
        new("IN", "Indiana"),
        new("IA", "Iowa"),
        new("KS", "Kansas"),
        new("KY", "Kentucky"),
        new("LA", "Louisiana"),
        new("ME", "Maine"),
        new("MD", "Maryland"),
        new("MA", "Massachusetts"),
        new("MI", "Michigan"),
        new("MN", "Minnesota"),
        new("MS", "Mississippi"),
        new("MO", "Missouri"),
        new("MT", "Montana"),
        new("NE", "Nebraska"),
        new("NV", "Nevada"),
        new("NH", "New Hampshire"),
        new("NJ", "New Jersey"),
        new("NM", "New Mexico"),
        new("NY", "New York"),
        new("NC", "North Carolina"),
        new("ND", "North Dakota"),
        new("OH", "Ohio"),
        new("OK", "Oklahoma"),
        new("OR", "Oregon"),
        new("PA", "Pennsylvania"),
        new("RI", "Rhode Island"),
        new("SC", "South Carolina"),
        new("SD", "South Dakota"),
        new("TN", "Tennessee"),
        new("TX", "Texas"),
        new("UT", "Utah"),
        new("VT", "Vermont"),
        new("VA", "Virginia"),
        new("WA", "Washington"),
        new("WV", "West Virginia"),
        new("WI", "Wisconsin"),
        new("WY", "Wyoming"),
    ];

    /// <summary>The list for a country, or null when its addresses take free text.</summary>
    public static IReadOnlyList<Region>? For(string? country)
    {
        var name = Countries.NormalizeCountry(country) ?? country?.Trim();
        if (string.Equals(name, "Canada", StringComparison.OrdinalIgnoreCase)) return Canada;
        if (string.Equals(name, "United States", StringComparison.OrdinalIgnoreCase)) return UnitedStates;
        return null;
    }

    /// <summary>The region in <paramref name="list"/> named by its code or its full name.</summary>
    public static Region? Find(IReadOnlyList<Region>? list, string? value)
    {
        if (list == null || string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return list.FirstOrDefault(r =>
            string.Equals(r.Code, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static string LabelFor(string? country)
    {
        var list = For(country);
        return list == Canada ? "Province" : list == UnitedStates ? "State" : "State/Province";
    }

    public static string PlaceholderFor(string? country)
    {
        var list = For(country);
        return list == Canada ? "Select a province" : list == UnitedStates ? "Select a state" : string.Empty;
    }
}
