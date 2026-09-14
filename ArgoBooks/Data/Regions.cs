namespace ArgoBooks.Data;

/// <summary>
/// A region of a country, by the value saved for it and its local name. Where that differs from the
/// English name, the picker shows both, as "Sachsen (Saxony)", and search matches either.
/// </summary>
public sealed record Region(string Code, string Name, string? IsoCode = null, string? EnglishName = null)
{
    /// <summary>A region that addresses write out in full, so its name is what gets saved.</summary>
    public static Region Named(string name, string isoCode, string? englishName = null) =>
        new(name, name, isoCode, englishName);

    /// <summary>The part of its ISO 3166-2 code after the country.</summary>
    public string Iso => IsoCode ?? Code;
}

/// <summary>
/// Regions for the countries that pick from a list; any other country takes free text. Where addresses
/// use a short code, the code is what gets saved: payroll filings require a Canadian province as its
/// two-letter code.
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

    public static IReadOnlyList<Region> Australia { get; } =
    [
        new("ACT", "Australian Capital Territory"),
        new("NSW", "New South Wales"),
        new("NT", "Northern Territory"),
        new("QLD", "Queensland"),
        new("SA", "South Australia"),
        new("TAS", "Tasmania"),
        new("VIC", "Victoria"),
        new("WA", "Western Australia"),
    ];

    public static IReadOnlyList<Region> Ireland { get; } =
    [
        Region.Named("Carlow", "CW"),
        Region.Named("Cavan", "CN"),
        Region.Named("Clare", "CE"),
        Region.Named("Cork", "CO"),
        Region.Named("Donegal", "DL"),
        Region.Named("Dublin", "D"),
        Region.Named("Galway", "G"),
        Region.Named("Kerry", "KY"),
        Region.Named("Kildare", "KE"),
        Region.Named("Kilkenny", "KK"),
        Region.Named("Laois", "LS"),
        Region.Named("Leitrim", "LM"),
        Region.Named("Limerick", "LK"),
        Region.Named("Longford", "LD"),
        Region.Named("Louth", "LH"),
        Region.Named("Mayo", "MO"),
        Region.Named("Meath", "MH"),
        Region.Named("Monaghan", "MN"),
        Region.Named("Offaly", "OY"),
        Region.Named("Roscommon", "RN"),
        Region.Named("Sligo", "SO"),
        Region.Named("Tipperary", "TA"),
        Region.Named("Waterford", "WD"),
        Region.Named("Westmeath", "WH"),
        Region.Named("Wexford", "WX"),
        Region.Named("Wicklow", "WW"),
    ];

    public static IReadOnlyList<Region> Spain { get; } =
    [
        Region.Named("A Coruña", "C", "Corunna"),
        Region.Named("Álava", "VI"),
        Region.Named("Albacete", "AB"),
        Region.Named("Alicante", "A"),
        Region.Named("Almería", "AL"),
        Region.Named("Asturias", "AS"),
        Region.Named("Ávila", "AV"),
        Region.Named("Badajoz", "BA"),
        Region.Named("Barcelona", "B"),
        Region.Named("Bizkaia", "BI", "Biscay"),
        Region.Named("Burgos", "BU"),
        Region.Named("Cáceres", "CC"),
        Region.Named("Cádiz", "CA"),
        Region.Named("Cantabria", "CB"),
        Region.Named("Castellón", "CS"),
        Region.Named("Ceuta", "CE"),
        Region.Named("Ciudad Real", "CR"),
        Region.Named("Córdoba", "CO"),
        Region.Named("Cuenca", "CU"),
        Region.Named("Gipuzkoa", "SS"),
        Region.Named("Girona", "GI"),
        Region.Named("Granada", "GR"),
        Region.Named("Guadalajara", "GU"),
        Region.Named("Huelva", "H"),
        Region.Named("Huesca", "HU"),
        Region.Named("Illes Balears", "IB", "Balearic Islands"),
        Region.Named("Jaén", "J"),
        Region.Named("La Rioja", "RI"),
        Region.Named("Las Palmas", "GC"),
        Region.Named("León", "LE"),
        Region.Named("Lleida", "L"),
        Region.Named("Lugo", "LU"),
        Region.Named("Madrid", "MD"),
        Region.Named("Málaga", "MA"),
        Region.Named("Melilla", "ML"),
        Region.Named("Murcia", "MC"),
        Region.Named("Navarra", "NC", "Navarre"),
        Region.Named("Ourense", "OR"),
        Region.Named("Palencia", "P"),
        Region.Named("Pontevedra", "PO"),
        Region.Named("Salamanca", "SA"),
        Region.Named("Santa Cruz de Tenerife", "TF"),
        Region.Named("Segovia", "SG"),
        Region.Named("Sevilla", "SE", "Seville"),
        Region.Named("Soria", "SO"),
        Region.Named("Tarragona", "T"),
        Region.Named("Teruel", "TE"),
        Region.Named("Toledo", "TO"),
        Region.Named("Valencia", "V"),
        Region.Named("Valladolid", "VA"),
        Region.Named("Zamora", "ZA"),
        Region.Named("Zaragoza", "Z"),
    ];

    public static IReadOnlyList<Region> Italy { get; } =
    [
        new("AG", "Agrigento"),
        new("AL", "Alessandria"),
        new("AN", "Ancona"),
        new("AO", "Aosta"),
        new("AR", "Arezzo"),
        new("AP", "Ascoli Piceno"),
        new("AT", "Asti"),
        new("AV", "Avellino"),
        new("BA", "Bari"),
        new("BT", "Barletta-Andria-Trani"),
        new("BL", "Belluno"),
        new("BN", "Benevento"),
        new("BG", "Bergamo"),
        new("BI", "Biella"),
        new("BO", "Bologna"),
        new("BZ", "Bolzano", EnglishName: "South Tyrol"),
        new("BS", "Brescia"),
        new("BR", "Brindisi"),
        new("CA", "Cagliari"),
        new("CL", "Caltanissetta"),
        new("CB", "Campobasso"),
        new("CE", "Caserta"),
        new("CT", "Catania"),
        new("CZ", "Catanzaro"),
        new("CH", "Chieti"),
        new("CO", "Como"),
        new("CS", "Cosenza"),
        new("CR", "Cremona"),
        new("KR", "Crotone"),
        new("CN", "Cuneo"),
        new("EN", "Enna"),
        new("FM", "Fermo"),
        new("FE", "Ferrara"),
        new("FI", "Firenze", EnglishName: "Florence"),
        new("FG", "Foggia"),
        new("FC", "Forlì-Cesena"),
        new("FR", "Frosinone"),
        new("GE", "Genova", EnglishName: "Genoa"),
        new("GO", "Gorizia"),
        new("GR", "Grosseto"),
        new("IM", "Imperia"),
        new("IS", "Isernia"),
        new("AQ", "L'Aquila"),
        new("SP", "La Spezia"),
        new("LT", "Latina"),
        new("LE", "Lecce"),
        new("LC", "Lecco"),
        new("LI", "Livorno"),
        new("LO", "Lodi"),
        new("LU", "Lucca"),
        new("MC", "Macerata"),
        new("MN", "Mantova", EnglishName: "Mantua"),
        new("MS", "Massa-Carrara"),
        new("MT", "Matera"),
        new("ME", "Messina"),
        new("MI", "Milano", EnglishName: "Milan"),
        new("MO", "Modena"),
        new("MB", "Monza e della Brianza", EnglishName: "Monza and Brianza"),
        new("NA", "Napoli", EnglishName: "Naples"),
        new("NO", "Novara"),
        new("NU", "Nuoro"),
        new("OR", "Oristano"),
        new("PD", "Padova", EnglishName: "Padua"),
        new("PA", "Palermo"),
        new("PR", "Parma"),
        new("PV", "Pavia"),
        new("PG", "Perugia"),
        new("PU", "Pesaro e Urbino", EnglishName: "Pesaro and Urbino"),
        new("PE", "Pescara"),
        new("PC", "Piacenza"),
        new("PI", "Pisa"),
        new("PT", "Pistoia"),
        new("PN", "Pordenone"),
        new("PZ", "Potenza"),
        new("PO", "Prato"),
        new("RG", "Ragusa"),
        new("RA", "Ravenna"),
        new("RC", "Reggio Calabria"),
        new("RE", "Reggio Emilia"),
        new("RI", "Rieti"),
        new("RN", "Rimini"),
        new("RM", "Roma", EnglishName: "Rome"),
        new("RO", "Rovigo"),
        new("SA", "Salerno"),
        new("SS", "Sassari"),
        new("SV", "Savona"),
        new("SI", "Siena"),
        new("SR", "Siracusa", EnglishName: "Syracuse"),
        new("SO", "Sondrio"),
        new("SU", "Sud Sardegna", EnglishName: "South Sardinia"),
        new("TA", "Taranto"),
        new("TE", "Teramo"),
        new("TR", "Terni"),
        new("TO", "Torino", EnglishName: "Turin"),
        new("TP", "Trapani"),
        new("TN", "Trento"),
        new("TV", "Treviso"),
        new("TS", "Trieste"),
        new("UD", "Udine"),
        new("VA", "Varese"),
        new("VE", "Venezia", EnglishName: "Venice"),
        new("VB", "Verbano-Cusio-Ossola"),
        new("VC", "Vercelli"),
        new("VR", "Verona"),
        new("VV", "Vibo Valentia"),
        new("VI", "Vicenza"),
        new("VT", "Viterbo"),
    ];

    public static IReadOnlyList<Region> Germany { get; } =
    [
        Region.Named("Baden-Württemberg", "BW"),
        Region.Named("Bayern", "BY", "Bavaria"),
        Region.Named("Berlin", "BE"),
        Region.Named("Brandenburg", "BB"),
        Region.Named("Bremen", "HB"),
        Region.Named("Hamburg", "HH"),
        Region.Named("Hessen", "HE", "Hesse"),
        Region.Named("Mecklenburg-Vorpommern", "MV", "Mecklenburg-Western Pomerania"),
        Region.Named("Niedersachsen", "NI", "Lower Saxony"),
        Region.Named("Nordrhein-Westfalen", "NW", "North Rhine-Westphalia"),
        Region.Named("Rheinland-Pfalz", "RP", "Rhineland-Palatinate"),
        Region.Named("Saarland", "SL"),
        Region.Named("Sachsen", "SN", "Saxony"),
        Region.Named("Sachsen-Anhalt", "ST", "Saxony-Anhalt"),
        Region.Named("Schleswig-Holstein", "SH"),
        Region.Named("Thüringen", "TH", "Thuringia"),
    ];

    public static IReadOnlyList<Region> Austria { get; } =
    [
        Region.Named("Burgenland", "1"),
        Region.Named("Kärnten", "2", "Carinthia"),
        Region.Named("Niederösterreich", "3", "Lower Austria"),
        Region.Named("Oberösterreich", "4", "Upper Austria"),
        Region.Named("Salzburg", "5"),
        Region.Named("Steiermark", "6", "Styria"),
        Region.Named("Tirol", "7", "Tyrol"),
        Region.Named("Vorarlberg", "8"),
        Region.Named("Wien", "9", "Vienna"),
    ];

    public static IReadOnlyList<Region> Poland { get; } =
    [
        Region.Named("Dolnośląskie", "02", "Lower Silesia"),
        Region.Named("Kujawsko-pomorskie", "04", "Kuyavia-Pomerania"),
        Region.Named("Lubelskie", "06", "Lublin"),
        Region.Named("Lubuskie", "08", "Lubusz"),
        Region.Named("Łódzkie", "10", "Lodz"),
        Region.Named("Małopolskie", "12", "Lesser Poland"),
        Region.Named("Mazowieckie", "14", "Masovia"),
        Region.Named("Opolskie", "16", "Opole"),
        Region.Named("Podkarpackie", "18", "Subcarpathia"),
        Region.Named("Podlaskie", "20", "Podlachia"),
        Region.Named("Pomorskie", "22", "Pomerania"),
        Region.Named("Śląskie", "24", "Silesia"),
        Region.Named("Świętokrzyskie", "26", "Holy Cross"),
        Region.Named("Warmińsko-mazurskie", "28", "Warmia-Masuria"),
        Region.Named("Wielkopolskie", "30", "Greater Poland"),
        Region.Named("Zachodniopomorskie", "32", "West Pomerania"),
    ];

    public static IReadOnlyList<Region> France { get; } =
    [
        Region.Named("Auvergne-Rhône-Alpes", "ARA"),
        Region.Named("Bourgogne-Franche-Comté", "BFC", "Burgundy-Franche-Comte"),
        Region.Named("Bretagne", "BRE", "Brittany"),
        Region.Named("Centre-Val de Loire", "CVL"),
        Region.Named("Corse", "20R", "Corsica"),
        Region.Named("Grand Est", "GES"),
        Region.Named("Guadeloupe", "971"),
        Region.Named("Guyane", "973", "French Guiana"),
        Region.Named("Hauts-de-France", "HDF"),
        Region.Named("Île-de-France", "IDF"),
        Region.Named("La Réunion", "974"),
        Region.Named("Martinique", "972"),
        Region.Named("Mayotte", "976"),
        Region.Named("Normandie", "NOR", "Normandy"),
        Region.Named("Nouvelle-Aquitaine", "NAQ"),
        Region.Named("Occitanie", "OCC", "Occitania"),
        Region.Named("Pays de la Loire", "PDL"),
        Region.Named("Provence-Alpes-Côte d'Azur", "PAC"),
    ];

    public static IReadOnlyList<Region> Netherlands { get; } =
    [
        Region.Named("Drenthe", "DR"),
        Region.Named("Flevoland", "FL"),
        Region.Named("Friesland", "FR"),
        Region.Named("Gelderland", "GE"),
        Region.Named("Groningen", "GR"),
        Region.Named("Limburg", "LI"),
        Region.Named("Noord-Brabant", "NB", "North Brabant"),
        Region.Named("Noord-Holland", "NH", "North Holland"),
        Region.Named("Overijssel", "OV"),
        Region.Named("Utrecht", "UT"),
        Region.Named("Zeeland", "ZE"),
        Region.Named("Zuid-Holland", "ZH", "South Holland"),
    ];

    public static IReadOnlyList<Region> NewZealand { get; } =
    [
        Region.Named("Auckland", "AUK"),
        Region.Named("Bay of Plenty", "BOP"),
        Region.Named("Canterbury", "CAN"),
        Region.Named("Gisborne", "GIS"),
        Region.Named("Hawke's Bay", "HKB"),
        Region.Named("Manawatū-Whanganui", "MWT"),
        Region.Named("Marlborough", "MBH"),
        Region.Named("Nelson", "NSN"),
        Region.Named("Northland", "NTL"),
        Region.Named("Otago", "OTA"),
        Region.Named("Southland", "STL"),
        Region.Named("Taranaki", "TKI"),
        Region.Named("Tasman", "TAS"),
        Region.Named("Waikato", "WKO"),
        Region.Named("Wellington", "WGN"),
        Region.Named("West Coast", "WTC"),
    ];

    private sealed record CountryRegions(IReadOnlyList<Region> List, string CountryCode, string Label, string Placeholder);

    private static readonly Dictionary<string, CountryRegions> ByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Canada"] = new(Canada, "CA", "Province", "Select a province"),
        ["United States"] = new(UnitedStates, "US", "State", "Select a state"),
        ["Australia"] = new(Australia, "AU", "State", "Select a state"),
        ["Ireland"] = new(Ireland, "IE", "County", "Select a county"),
        ["Spain"] = new(Spain, "ES", "Province", "Select a province"),
        ["Italy"] = new(Italy, "IT", "Province", "Select a province"),
        ["Germany"] = new(Germany, "DE", "State", "Select a state"),
        ["Austria"] = new(Austria, "AT", "State", "Select a state"),
        ["Poland"] = new(Poland, "PL", "Province", "Select a province"),
        ["France"] = new(France, "FR", "Region", "Select a region"),
        ["Netherlands"] = new(Netherlands, "NL", "Province", "Select a province"),
        ["New Zealand"] = new(NewZealand, "NZ", "Region", "Select a region"),
    };

    /// <summary>The list for a country, or null when its addresses take free text.</summary>
    public static IReadOnlyList<Region>? For(string? country) => Lookup(country)?.List;

    /// <summary>The region in <paramref name="list"/> named by its code or its full name.</summary>
    public static Region? Find(IReadOnlyList<Region>? list, string? value)
    {
        if (list == null || string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return list.FirstOrDefault(r =>
            string.Equals(r.Code, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.EnglishName, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static string LabelFor(string? country) => Lookup(country)?.Label ?? "State/Province";

    public static string PlaceholderFor(string? country) => Lookup(country)?.Placeholder ?? string.Empty;

    /// <summary>The full ISO 3166-2 code of every region that can have a flag.</summary>
    public static IEnumerable<string> FlagCodes =>
        ByCountry.Values.SelectMany(entry => entry.List.Select(region => $"{entry.CountryCode}-{region.Iso}"));

    /// <summary>
    /// The flag image for a region, named by its full ISO 3166-2 code, or null for a country without a
    /// list. The images come from tools/ArgoBooks.RegionFlags.
    /// </summary>
    public static string? FlagPathFor(string? country, Region region) =>
        Lookup(country) is { } entry
            ? $"avares://ArgoBooks/Assets/RegionFlags/{entry.CountryCode}-{region.Iso}.png"
            : null;

    private static CountryRegions? Lookup(string? country)
    {
        var name = Countries.NormalizeCountry(country) ?? country?.Trim();
        return name != null && ByCountry.TryGetValue(name, out var entry) ? entry : null;
    }
}
