namespace ArgoBooks.Data;

/// <summary>
/// A region of a country, by the value saved for it and its local name. Where that differs from the
/// English name, the picker shows both, as "Sachsen (Saxony)", and search matches either.
/// </summary>
public sealed record Region(string Code, string Name, string? IsoCode = null, string? EnglishName = null,
    string? WikidataId = null)
{
    /// <summary>A region that addresses write out in full, so its name is what gets saved.</summary>
    public static Region Named(string name, string isoCode, string? englishName = null) =>
        new(name, name, isoCode, englishName);

    /// <summary>A region with no ISO 3166-2 code, such as an English county, known by its Wikidata id.</summary>
    public static Region Wikidata(string name, string wikidataId) => new(name, name, WikidataId: wikidataId);

    /// <summary>What names its image after the country: its ISO 3166-2 code, or its Wikidata id.</summary>
    public string ImageKey => WikidataId ?? IsoCode ?? Code;
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

    // England's ceremonial counties, Northern Ireland's counties, Scotland's council areas and Wales's
    // principal areas: the names UK addresses use.
    public static IReadOnlyList<Region> UnitedKingdom { get; } =
    [
        Region.Named("Aberdeen", "ABE"),
        Region.Named("Aberdeenshire", "ABD"),
        Region.Named("Angus", "ANS"),
        Region.Named("Argyll and Bute", "AGB"),
        Region.Wikidata("Bedfordshire", "Q23143"),
        Region.Wikidata("Berkshire", "Q23220"),
        Region.Named("Blaenau Gwent", "BGW"),
        Region.Named("Bridgend", "BGE"),
        Region.Wikidata("Bristol", "Q21693433"),
        Region.Wikidata("Buckinghamshire", "Q23229"),
        Region.Named("Caerphilly", "CAY"),
        Region.Wikidata("Cambridgeshire", "Q23112"),
        Region.Named("Cardiff", "CRF"),
        Region.Named("Carmarthenshire", "CMN"),
        Region.Named("Ceredigion", "CGN"),
        Region.Wikidata("Cheshire", "Q23064"),
        Region.Wikidata("City of London", "Q23311"),
        Region.Named("Clackmannanshire", "CLK"),
        Region.Named("Conwy", "CWY"),
        Region.Wikidata("Cornwall", "Q48790202"),
        Region.Wikidata("County Antrim", "Q189592"),
        Region.Wikidata("County Armagh", "Q192761"),
        Region.Wikidata("County Down", "Q190684"),
        Region.Wikidata("County Durham", "Q23082"),
        Region.Wikidata("County Fermanagh", "Q190678"),
        Region.Wikidata("County Londonderry", "Q192208"),
        Region.Wikidata("County Tyrone", "Q192229"),
        Region.Wikidata("Cumbria", "Q23066"),
        Region.Named("Denbighshire", "DEN"),
        Region.Wikidata("Derbyshire", "Q23098"),
        Region.Wikidata("Devon", "Q23156"),
        Region.Wikidata("Dorset", "Q23159"),
        Region.Named("Dumfries and Galloway", "DGY"),
        Region.Named("Dundee", "DND"),
        Region.Named("East Ayrshire", "EAY"),
        Region.Named("East Dunbartonshire", "EDU"),
        Region.Named("East Lothian", "ELN"),
        Region.Named("East Renfrewshire", "ERW"),
        Region.Wikidata("East Riding of Yorkshire", "Q23088"),
        Region.Wikidata("East Sussex", "Q23293"),
        Region.Named("Edinburgh", "EDH"),
        Region.Wikidata("Essex", "Q23240"),
        Region.Named("Falkirk", "FAL"),
        Region.Named("Fife", "FIF"),
        Region.Named("Flintshire", "FLN"),
        Region.Named("Glasgow", "GLG"),
        Region.Wikidata("Gloucestershire", "Q23165"),
        Region.Wikidata("Greater London", "Q23306"),
        Region.Wikidata("Greater Manchester", "Q23099"),
        Region.Named("Gwynedd", "GWN"),
        Region.Wikidata("Hampshire", "Q23204"),
        Region.Wikidata("Herefordshire", "Q23129"),
        Region.Wikidata("Hertfordshire", "Q3410"),
        Region.Named("Highland", "HLD"),
        Region.Named("Inverclyde", "IVC"),
        Region.Named("Isle of Anglesey", "AGY"),
        Region.Wikidata("Isle of Wight", "Q9679"),
        Region.Wikidata("Kent", "Q23298"),
        Region.Wikidata("Lancashire", "Q23077"),
        Region.Wikidata("Leicestershire", "Q23106"),
        Region.Wikidata("Lincolnshire", "Q23090"),
        Region.Wikidata("Merseyside", "Q23100"),
        Region.Named("Merthyr Tydfil", "MTY"),
        Region.Named("Midlothian", "MLN"),
        Region.Named("Monmouthshire", "MON"),
        Region.Named("Moray", "MRY"),
        Region.Named("Neath Port Talbot", "NTL"),
        Region.Named("Newport", "NWP"),
        Region.Wikidata("Norfolk", "Q23109"),
        Region.Named("North Ayrshire", "NAY"),
        Region.Named("North Lanarkshire", "NLK"),
        Region.Wikidata("North Yorkshire", "Q23086"),
        Region.Wikidata("Northamptonshire", "Q23115"),
        Region.Wikidata("Northumberland", "Q23079"),
        Region.Wikidata("Nottinghamshire", "Q23092"),
        Region.Named("Orkney Islands", "ORK"),
        Region.Named("Outer Hebrides", "ELS"),
        Region.Wikidata("Oxfordshire", "Q23169"),
        Region.Named("Pembrokeshire", "PEM"),
        Region.Named("Perth and Kinross", "PKN"),
        Region.Named("Powys", "POW"),
        Region.Named("Renfrewshire", "RFW"),
        Region.Named("Rhondda Cynon Taf", "RCT"),
        Region.Wikidata("Rutland", "Q23107"),
        Region.Named("Scottish Borders", "SCB"),
        Region.Named("Shetland Islands", "ZET"),
        Region.Wikidata("Shropshire", "Q23103"),
        Region.Wikidata("Somerset", "Q23157"),
        Region.Named("South Ayrshire", "SAY"),
        Region.Named("South Lanarkshire", "SLK"),
        Region.Wikidata("South Yorkshire", "Q23095"),
        Region.Wikidata("Staffordshire", "Q23105"),
        Region.Named("Stirling", "STG"),
        Region.Wikidata("Suffolk", "Q23111"),
        Region.Wikidata("Surrey", "Q23276"),
        Region.Named("Swansea", "SWA"),
        Region.Named("Torfaen", "TOF"),
        Region.Wikidata("Tyne and Wear", "Q23080"),
        Region.Named("Vale of Glamorgan", "VGL"),
        Region.Wikidata("Warwickshire", "Q23140"),
        Region.Named("West Dunbartonshire", "WDU"),
        Region.Named("West Lothian", "WLN"),
        Region.Wikidata("West Midlands", "Q23124"),
        Region.Wikidata("West Sussex", "Q23287"),
        Region.Wikidata("West Yorkshire", "Q23083"),
        Region.Wikidata("Wiltshire", "Q23183"),
        Region.Wikidata("Worcestershire", "Q23135"),
        Region.Named("Wrexham", "WRX"),
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
        ["United Kingdom"] = new(UnitedKingdom, "GB", "County", "Select a county"),
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

    /// <summary>
    /// The image name of every region: its country code, then its ISO 3166-2 code or, where it has none,
    /// its Wikidata id (CA-ON, GB-Q23298).
    /// </summary>
    public static IEnumerable<string> FlagCodes =>
        ByCountry.Values.SelectMany(entry => entry.List.Select(region => $"{entry.CountryCode}-{region.ImageKey}"));

    /// <summary>
    /// The image for a region, named as in <see cref="FlagCodes"/>, or null for a country without a list.
    /// The images come from tools/ArgoBooks.RegionFlags.
    /// </summary>
    public static string? FlagPathFor(string? country, Region region) =>
        Lookup(country) is { } entry
            ? $"avares://ArgoBooks/Assets/RegionFlags/{entry.CountryCode}-{region.ImageKey}.png"
            : null;

    private static CountryRegions? Lookup(string? country)
    {
        var name = Countries.NormalizeCountry(country) ?? country?.Trim();
        return name != null && ByCountry.TryGetValue(name, out var entry) ? entry : null;
    }
}
