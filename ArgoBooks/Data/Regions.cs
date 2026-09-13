namespace ArgoBooks.Data;

/// <summary>A region of a country, by the value saved for it and its English name.</summary>
public sealed record Region(string Code, string Name)
{
    /// <summary>A region that addresses write out in full, so its name is what gets saved.</summary>
    public Region(string name) : this(name, name) { }
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
        new("Carlow"),
        new("Cavan"),
        new("Clare"),
        new("Cork"),
        new("Donegal"),
        new("Dublin"),
        new("Galway"),
        new("Kerry"),
        new("Kildare"),
        new("Kilkenny"),
        new("Laois"),
        new("Leitrim"),
        new("Limerick"),
        new("Longford"),
        new("Louth"),
        new("Mayo"),
        new("Meath"),
        new("Monaghan"),
        new("Offaly"),
        new("Roscommon"),
        new("Sligo"),
        new("Tipperary"),
        new("Waterford"),
        new("Westmeath"),
        new("Wexford"),
        new("Wicklow"),
    ];

    public static IReadOnlyList<Region> Spain { get; } =
    [
        new("A Coruña"),
        new("Álava"),
        new("Albacete"),
        new("Alicante"),
        new("Almería"),
        new("Asturias"),
        new("Ávila"),
        new("Badajoz"),
        new("Barcelona"),
        new("Bizkaia"),
        new("Burgos"),
        new("Cáceres"),
        new("Cádiz"),
        new("Cantabria"),
        new("Castellón"),
        new("Ceuta"),
        new("Ciudad Real"),
        new("Córdoba"),
        new("Cuenca"),
        new("Gipuzkoa"),
        new("Girona"),
        new("Granada"),
        new("Guadalajara"),
        new("Huelva"),
        new("Huesca"),
        new("Illes Balears"),
        new("Jaén"),
        new("La Rioja"),
        new("Las Palmas"),
        new("León"),
        new("Lleida"),
        new("Lugo"),
        new("Madrid"),
        new("Málaga"),
        new("Melilla"),
        new("Murcia"),
        new("Navarra"),
        new("Ourense"),
        new("Palencia"),
        new("Pontevedra"),
        new("Salamanca"),
        new("Santa Cruz de Tenerife"),
        new("Segovia"),
        new("Sevilla"),
        new("Soria"),
        new("Tarragona"),
        new("Teruel"),
        new("Toledo"),
        new("Valencia"),
        new("Valladolid"),
        new("Zamora"),
        new("Zaragoza"),
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
        new("BZ", "Bolzano"),
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
        new("FI", "Firenze"),
        new("FG", "Foggia"),
        new("FC", "Forlì-Cesena"),
        new("FR", "Frosinone"),
        new("GE", "Genova"),
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
        new("MN", "Mantova"),
        new("MS", "Massa-Carrara"),
        new("MT", "Matera"),
        new("ME", "Messina"),
        new("MI", "Milano"),
        new("MO", "Modena"),
        new("MB", "Monza e della Brianza"),
        new("NA", "Napoli"),
        new("NO", "Novara"),
        new("NU", "Nuoro"),
        new("OR", "Oristano"),
        new("PD", "Padova"),
        new("PA", "Palermo"),
        new("PR", "Parma"),
        new("PV", "Pavia"),
        new("PG", "Perugia"),
        new("PU", "Pesaro e Urbino"),
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
        new("RM", "Roma"),
        new("RO", "Rovigo"),
        new("SA", "Salerno"),
        new("SS", "Sassari"),
        new("SV", "Savona"),
        new("SI", "Siena"),
        new("SR", "Siracusa"),
        new("SO", "Sondrio"),
        new("SU", "Sud Sardegna"),
        new("TA", "Taranto"),
        new("TE", "Teramo"),
        new("TR", "Terni"),
        new("TO", "Torino"),
        new("TP", "Trapani"),
        new("TN", "Trento"),
        new("TV", "Treviso"),
        new("TS", "Trieste"),
        new("UD", "Udine"),
        new("VA", "Varese"),
        new("VE", "Venezia"),
        new("VB", "Verbano-Cusio-Ossola"),
        new("VC", "Vercelli"),
        new("VR", "Verona"),
        new("VV", "Vibo Valentia"),
        new("VI", "Vicenza"),
        new("VT", "Viterbo"),
    ];

    public static IReadOnlyList<Region> Germany { get; } =
    [
        new("Baden-Württemberg"),
        new("Bayern"),
        new("Berlin"),
        new("Brandenburg"),
        new("Bremen"),
        new("Hamburg"),
        new("Hessen"),
        new("Mecklenburg-Vorpommern"),
        new("Niedersachsen"),
        new("Nordrhein-Westfalen"),
        new("Rheinland-Pfalz"),
        new("Saarland"),
        new("Sachsen"),
        new("Sachsen-Anhalt"),
        new("Schleswig-Holstein"),
        new("Thüringen"),
    ];

    public static IReadOnlyList<Region> Austria { get; } =
    [
        new("Burgenland"),
        new("Kärnten"),
        new("Niederösterreich"),
        new("Oberösterreich"),
        new("Salzburg"),
        new("Steiermark"),
        new("Tirol"),
        new("Vorarlberg"),
        new("Wien"),
    ];

    public static IReadOnlyList<Region> Poland { get; } =
    [
        new("Dolnośląskie"),
        new("Kujawsko-pomorskie"),
        new("Lubelskie"),
        new("Lubuskie"),
        new("Łódzkie"),
        new("Małopolskie"),
        new("Mazowieckie"),
        new("Opolskie"),
        new("Podkarpackie"),
        new("Podlaskie"),
        new("Pomorskie"),
        new("Śląskie"),
        new("Świętokrzyskie"),
        new("Warmińsko-mazurskie"),
        new("Wielkopolskie"),
        new("Zachodniopomorskie"),
    ];

    public static IReadOnlyList<Region> France { get; } =
    [
        new("Auvergne-Rhône-Alpes"),
        new("Bourgogne-Franche-Comté"),
        new("Bretagne"),
        new("Centre-Val de Loire"),
        new("Corse"),
        new("Grand Est"),
        new("Guadeloupe"),
        new("Guyane"),
        new("Hauts-de-France"),
        new("Île-de-France"),
        new("La Réunion"),
        new("Martinique"),
        new("Mayotte"),
        new("Normandie"),
        new("Nouvelle-Aquitaine"),
        new("Occitanie"),
        new("Pays de la Loire"),
        new("Provence-Alpes-Côte d'Azur"),
    ];

    public static IReadOnlyList<Region> Netherlands { get; } =
    [
        new("Drenthe"),
        new("Flevoland"),
        new("Friesland"),
        new("Gelderland"),
        new("Groningen"),
        new("Limburg"),
        new("Noord-Brabant"),
        new("Noord-Holland"),
        new("Overijssel"),
        new("Utrecht"),
        new("Zeeland"),
        new("Zuid-Holland"),
    ];

    public static IReadOnlyList<Region> NewZealand { get; } =
    [
        new("Auckland"),
        new("Bay of Plenty"),
        new("Canterbury"),
        new("Gisborne"),
        new("Hawke's Bay"),
        new("Manawatū-Whanganui"),
        new("Marlborough"),
        new("Nelson"),
        new("Northland"),
        new("Otago"),
        new("Southland"),
        new("Taranaki"),
        new("Tasman"),
        new("Waikato"),
        new("Wellington"),
        new("West Coast"),
    ];

    private sealed record CountryRegions(IReadOnlyList<Region> List, string Label, string Placeholder);

    private static readonly Dictionary<string, CountryRegions> ByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Canada"] = new(Canada, "Province", "Select a province"),
        ["United States"] = new(UnitedStates, "State", "Select a state"),
        ["Australia"] = new(Australia, "State", "Select a state"),
        ["Ireland"] = new(Ireland, "County", "Select a county"),
        ["Spain"] = new(Spain, "Province", "Select a province"),
        ["Italy"] = new(Italy, "Province", "Select a province"),
        ["Germany"] = new(Germany, "State", "Select a state"),
        ["Austria"] = new(Austria, "State", "Select a state"),
        ["Poland"] = new(Poland, "Province", "Select a province"),
        ["France"] = new(France, "Region", "Select a region"),
        ["Netherlands"] = new(Netherlands, "Province", "Select a province"),
        ["New Zealand"] = new(NewZealand, "Region", "Select a region"),
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
            string.Equals(r.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static string LabelFor(string? country) => Lookup(country)?.Label ?? "State/Province";

    public static string PlaceholderFor(string? country) => Lookup(country)?.Placeholder ?? string.Empty;

    private static CountryRegions? Lookup(string? country)
    {
        var name = Countries.NormalizeCountry(country) ?? country?.Trim();
        return name != null && ByCountry.TryGetValue(name, out var entry) ? entry : null;
    }
}
