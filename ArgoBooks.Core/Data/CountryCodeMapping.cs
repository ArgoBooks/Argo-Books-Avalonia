namespace ArgoBooks.Core.Data;

/// <summary>
/// Maps country names (and common abbreviations) to ISO 3166-1 alpha-3 codes
/// for GeoMap rendering, plus the reverse mapping for export.
/// Shared by ChartLoaderService (UI on-screen charts) and ReportRenderer
/// (PDF/image report output) so both surfaces recognize the same names.
/// Built on <see cref="Countries"/>, so every name the country picker produces resolves.
/// </summary>
public static class CountryCodeMapping
{
    /// <summary>
    /// Returns the lowercase ISO 3166-1 alpha-3 code for a country name, or the lowercased
    /// input as a best-effort fallback. Returns empty string for null/empty input.
    /// </summary>
    public static string GetIsoCode(string? countryName)
    {
        if (string.IsNullOrEmpty(countryName))
            return string.Empty;

        return Countries.Find(countryName) is { } country && Countries.GetAlpha3Code(country.Code) is { } alpha3
            ? alpha3.ToLowerInvariant()
            : countryName.ToLowerInvariant();
    }

    /// <summary>
    /// Converts a GeoMap data dictionary keyed by ISO codes into one keyed by
    /// human-readable country names. Used when exporting GeoMap data to a
    /// spreadsheet where readers expect names, not codes. Drops zero values.
    /// </summary>
    public static Dictionary<string, double> ConvertGeoMapDataForExport(Dictionary<string, double> isoCodeData)
    {
        return isoCodeData
            .Where(kvp => kvp.Value > 0)
            .ToDictionary(
                kvp => Countries.GetByAlpha3Code(kvp.Key)?.Name ?? kvp.Key.ToUpperInvariant(),
                kvp => kvp.Value);
    }
}
