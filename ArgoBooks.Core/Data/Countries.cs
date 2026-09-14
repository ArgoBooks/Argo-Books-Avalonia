using System.Globalization;

namespace ArgoBooks.Core.Data;

/// <summary>
/// Provides a shared list of countries with their dial codes and phone formats.
/// </summary>
public static class Countries
{
    /// <summary>
    /// ISO codes for priority/common countries shown at the top of country lists.
    /// </summary>
    private static readonly string[] PriorityCodes =
    [
        "US", // United States
        "DE", // Germany
        "GB", // United Kingdom
        "FR", // France
        "IT", // Italy
        "CA", // Canada
        "AU", // Australia
        "ES", // Spain
        "NL"  // Netherlands
    ];

    /// <summary>
    /// Complete list of country data with dial codes and phone format patterns.
    /// </summary>
    public static readonly IReadOnlyList<CountryInfo> All =
    [
        new("AF", "Afghanistan", "+93", "XX XXX XXXX"),
        new("AL", "Albania", "+355", "XX XXX XXXX"),
        new("DZ", "Algeria", "+213", "XXX XX XX XX"),
        new("AD", "Andorra", "+376", "XXX XXX"),
        new("AO", "Angola", "+244", "XXX XXX XXX"),
        new("AG", "Antigua and Barbuda", "+1", "XXX-XXXX"),
        new("AR", "Argentina", "+54", "XX XXXX-XXXX"),
        new("AM", "Armenia", "+374", "XX XXX XXX"),
        new("AU", "Australia", "+61", "XXXX XXX XXX"),
        new("AT", "Austria", "+43", "XXX XXXXXXX"),
        new("AZ", "Azerbaijan", "+994", "XX XXX XX XX"),
        new("BS", "Bahamas", "+1", "XXX-XXXX"),
        new("BH", "Bahrain", "+973", "XXXX XXXX"),
        new("BD", "Bangladesh", "+880", "XXXX-XXXXXX"),
        new("BB", "Barbados", "+1", "XXX-XXXX"),
        new("BY", "Belarus", "+375", "XX XXX-XX-XX"),
        new("BE", "Belgium", "+32", "XXX XX XX XX"),
        new("BZ", "Belize", "+501", "XXX-XXXX"),
        new("BJ", "Benin", "+229", "XX XX XX XX"),
        new("BT", "Bhutan", "+975", "XX XXX XXX"),
        new("BO", "Bolivia", "+591", "X XXX XXXX"),
        new("BA", "Bosnia and Herzegovina", "+387", "XX XXX-XXX"),
        new("BW", "Botswana", "+267", "XX XXX XXX"),
        new("BR", "Brazil", "+55", "(XX) XXXXX-XXXX"),
        new("BN", "Brunei", "+673", "XXX XXXX"),
        new("BG", "Bulgaria", "+359", "XX XXX XXXX"),
        new("BF", "Burkina Faso", "+226", "XX XX XX XX"),
        new("BI", "Burundi", "+257", "XX XX XXXX"),
        new("CV", "Cabo Verde", "+238", "XXX XX XX"),
        new("KH", "Cambodia", "+855", "XX XXX XXX"),
        new("CM", "Cameroon", "+237", "X XX XX XX XX"),
        new("CA", "Canada", "+1", "(XXX) XXX-XXXX"),
        new("CF", "Central African Republic", "+236", "XX XX XX XX"),
        new("TD", "Chad", "+235", "XX XX XX XX"),
        new("CL", "Chile", "+56", "X XXXX XXXX"),
        new("CN", "China", "+86", "XXX XXXX XXXX"),
        new("CO", "Colombia", "+57", "XXX XXX XXXX"),
        new("KM", "Comoros", "+269", "XXX XX XX"),
        new("CR", "Costa Rica", "+506", "XXXX XXXX"),
        new("HR", "Croatia", "+385", "XX XXX XXXX"),
        new("CU", "Cuba", "+53", "X XXX XXXX"),
        new("CY", "Cyprus", "+357", "XX XXXXXX"),
        new("CZ", "Czechia", "+420", "XXX XXX XXX"),
        new("DK", "Denmark", "+45", "XX XX XX XX"),
        new("DJ", "Djibouti", "+253", "XX XX XX XX"),
        new("DM", "Dominica", "+1", "XXX-XXXX"),
        new("DO", "Dominican Republic", "+1", "XXX-XXXX"),
        new("EC", "Ecuador", "+593", "XX XXX XXXX"),
        new("EG", "Egypt", "+20", "XXX XXX XXXX"),
        new("SV", "El Salvador", "+503", "XXXX XXXX"),
        new("GQ", "Equatorial Guinea", "+240", "XXX XXX XXX"),
        new("ER", "Eritrea", "+291", "X XXX XXX"),
        new("EE", "Estonia", "+372", "XXXX XXXX"),
        new("SZ", "Eswatini", "+268", "XXXX XXXX"),
        new("ET", "Ethiopia", "+251", "XX XXX XXXX"),
        new("FJ", "Fiji", "+679", "XXX XXXX"),
        new("FI", "Finland", "+358", "XX XXX XXXX"),
        new("FR", "France", "+33", "X XX XX XX XX"),
        new("GA", "Gabon", "+241", "X XX XX XX"),
        new("GM", "Gambia", "+220", "XXX XXXX"),
        new("GE", "Georgia", "+995", "XXX XXX XXX"),
        new("DE", "Germany", "+49", "XXX XXXXXXXX"),
        new("GH", "Ghana", "+233", "XX XXX XXXX"),
        new("GR", "Greece", "+30", "XXX XXX XXXX"),
        new("GD", "Grenada", "+1", "XXX-XXXX"),
        new("GT", "Guatemala", "+502", "XXXX XXXX"),
        new("GN", "Guinea", "+224", "XXX XX XX XX"),
        new("GW", "Guinea-Bissau", "+245", "XXX XXXX"),
        new("GY", "Guyana", "+592", "XXX XXXX"),
        new("HT", "Haiti", "+509", "XXXX XXXX"),
        new("HN", "Honduras", "+504", "XXXX XXXX"),
        new("HU", "Hungary", "+36", "XX XXX XXXX"),
        new("IS", "Iceland", "+354", "XXX XXXX"),
        new("IN", "India", "+91", "XXXXX XXXXX"),
        new("ID", "Indonesia", "+62", "XXX XXXX XXXX"),
        new("IR", "Iran", "+98", "XXX XXX XXXX"),
        new("IQ", "Iraq", "+964", "XXX XXX XXXX"),
        new("IE", "Ireland", "+353", "XX XXX XXXX"),
        new("IL", "Israel", "+972", "XX XXX XXXX"),
        new("IT", "Italy", "+39", "XXX XXX XXXX"),
        new("CI", "Ivory Coast", "+225", "XX XX XX XXXX"),
        new("JM", "Jamaica", "+1", "XXX-XXXX"),
        new("JP", "Japan", "+81", "XX XXXX XXXX"),
        new("JO", "Jordan", "+962", "X XXXX XXXX"),
        new("KZ", "Kazakhstan", "+7", "XXX XXX XX XX"),
        new("KE", "Kenya", "+254", "XXX XXXXXX"),
        new("KI", "Kiribati", "+686", "XXXX XXXX"),
        new("KW", "Kuwait", "+965", "XXXX XXXX"),
        new("KG", "Kyrgyzstan", "+996", "XXX XXXXXX"),
        new("LA", "Lao", "+856", "XX XX XXX XXX"),
        new("LV", "Latvia", "+371", "XX XXX XXX"),
        new("LB", "Lebanon", "+961", "XX XXX XXX"),
        new("LS", "Lesotho", "+266", "XX XXX XXX"),
        new("LR", "Liberia", "+231", "XX XXX XXXX"),
        new("LY", "Libya", "+218", "XX XXX XXXX"),
        new("LI", "Liechtenstein", "+423", "XXX XXXX"),
        new("LT", "Lithuania", "+370", "XXX XXXXX"),
        new("LU", "Luxembourg", "+352", "XXX XXX XXX"),
        new("MG", "Madagascar", "+261", "XX XX XXX XX"),
        new("MW", "Malawi", "+265", "X XXXX XXXX"),
        new("MY", "Malaysia", "+60", "XX XXXX XXXX"),
        new("MV", "Maldives", "+960", "XXX XXXX"),
        new("ML", "Mali", "+223", "XX XX XX XX"),
        new("MT", "Malta", "+356", "XXXX XXXX"),
        new("MH", "Marshall Islands", "+692", "XXX-XXXX"),
        new("MR", "Mauritania", "+222", "XX XX XX XX"),
        new("MU", "Mauritius", "+230", "XXXX XXXX"),
        new("MX", "Mexico", "+52", "XXX XXX XXXX"),
        new("FM", "Micronesia", "+691", "XXX XXXX"),
        new("MD", "Moldova", "+373", "XX XXX XXX"),
        new("MC", "Monaco", "+377", "XX XX XX XX"),
        new("MN", "Mongolia", "+976", "XX XX XXXX"),
        new("ME", "Montenegro", "+382", "XX XXX XXX"),
        new("MA", "Morocco", "+212", "XX XXX XXXX"),
        new("MZ", "Mozambique", "+258", "XX XXX XXXX"),
        new("MM", "Myanmar", "+95", "XX XXX XXXX"),
        new("NA", "Namibia", "+264", "XX XXX XXXX"),
        new("NR", "Nauru", "+674", "XXX XXXX"),
        new("NP", "Nepal", "+977", "XX XXX XXXX"),
        new("NL", "Netherlands", "+31", "XX XXXXXXXX"),
        new("NZ", "New Zealand", "+64", "XX XXX XXXX"),
        new("NI", "Nicaragua", "+505", "XXXX XXXX"),
        new("NE", "Niger", "+227", "XX XX XX XX"),
        new("NG", "Nigeria", "+234", "XXX XXX XXXX"),
        new("KP", "North Korea", "+850", "XXX XXXX XXXX"),
        new("MK", "North Macedonia", "+389", "XX XXX XXX"),
        new("NO", "Norway", "+47", "XXX XX XXX"),
        new("OM", "Oman", "+968", "XXXX XXXX"),
        new("PK", "Pakistan", "+92", "XXX XXX XXXX"),
        new("PW", "Palau", "+680", "XXX XXXX"),
        new("PA", "Panama", "+507", "XXXX XXXX"),
        new("PG", "Papua New Guinea", "+675", "XXX XXXX"),
        new("PY", "Paraguay", "+595", "XXX XXX XXX"),
        new("PE", "Peru", "+51", "XXX XXX XXX"),
        new("PH", "Philippines", "+63", "XXX XXX XXXX"),
        new("PL", "Poland", "+48", "XXX XXX XXX"),
        new("PT", "Portugal", "+351", "XXX XXX XXX"),
        new("QA", "Qatar", "+974", "XXXX XXXX"),
        new("RO", "Romania", "+40", "XXX XXX XXX"),
        new("RU", "Russia", "+7", "XXX XXX-XX-XX"),
        new("RW", "Rwanda", "+250", "XXX XXX XXX"),
        new("KN", "Saint Kitts and Nevis", "+1", "XXX-XXXX"),
        new("LC", "Saint Lucia", "+1", "XXX-XXXX"),
        new("VC", "Saint Vincent and the Grenadines", "+1", "XXX-XXXX"),
        new("WS", "Samoa", "+685", "XX XXXX"),
        new("SM", "San Marino", "+378", "XXXX XXXXXX"),
        new("ST", "Sao Tome and Principe", "+239", "XXX XXXX"),
        new("SA", "Saudi Arabia", "+966", "XX XXX XXXX"),
        new("SN", "Senegal", "+221", "XX XXX XX XX"),
        new("RS", "Serbia", "+381", "XX XXX XXXX"),
        new("SC", "Seychelles", "+248", "X XXX XXX"),
        new("SL", "Sierra Leone", "+232", "XX XXXXXX"),
        new("SG", "Singapore", "+65", "XXXX XXXX"),
        new("SK", "Slovakia", "+421", "XXX XXX XXX"),
        new("SI", "Slovenia", "+386", "XX XXX XXX"),
        new("SB", "Solomon Islands", "+677", "XXXXX"),
        new("SO", "Somalia", "+252", "XX XXX XXX"),
        new("ZA", "South Africa", "+27", "XX XXX XXXX"),
        new("KR", "South Korea", "+82", "XX XXXX XXXX"),
        new("SS", "South Sudan", "+211", "XX XXX XXXX"),
        new("ES", "Spain", "+34", "XXX XXX XXX"),
        new("LK", "Sri Lanka", "+94", "XX XXX XXXX"),
        new("SD", "Sudan", "+249", "XX XXX XXXX"),
        new("SR", "Suriname", "+597", "XXX XXXX"),
        new("SE", "Sweden", "+46", "XX XXX XX XX"),
        new("CH", "Switzerland", "+41", "XX XXX XX XX"),
        new("SY", "Syria", "+963", "XX XXXX XXXX"),
        new("TW", "Taiwan", "+886", "XXXX XXXX"),
        new("TJ", "Tajikistan", "+992", "XX XXX XXXX"),
        new("TZ", "Tanzania", "+255", "XXX XXX XXX"),
        new("TH", "Thailand", "+66", "XX XXX XXXX"),
        new("CD", "The Democratic Republic of the Congo", "+243", "XXX XXX XXX"),
        new("CG", "The Republic of the Congo", "+242", "XX XXX XXXX"),
        new("TL", "Timor-Leste", "+670", "XXX XXXX"),
        new("TG", "Togo", "+228", "XX XX XX XX"),
        new("TO", "Tonga", "+676", "XXXXX"),
        new("TT", "Trinidad and Tobago", "+1", "XXX-XXXX"),
        new("TN", "Tunisia", "+216", "XX XXX XXX"),
        new("TR", "Turkey", "+90", "XXX XXX XX XX"),
        new("TM", "Turkmenistan", "+993", "XX XXXXXX"),
        new("TV", "Tuvalu", "+688", "XXXXX"),
        new("UG", "Uganda", "+256", "XXX XXXXXX"),
        new("UA", "Ukraine", "+380", "XX XXX XX XX"),
        new("AE", "United Arab Emirates", "+971", "XX XXX XXXX"),
        new("GB", "United Kingdom", "+44", "XXXX XXX XXXX", "United Kingdom of Great Britain and Northern Ireland"),
        new("US", "United States", "+1", "(XXX) XXX-XXXX", "United States of America"),
        new("UY", "Uruguay", "+598", "X XXX XXXX"),
        new("UZ", "Uzbekistan", "+998", "XX XXX XX XX"),
        new("VU", "Vanuatu", "+678", "XXXXX"),
        new("VE", "Venezuela", "+58", "XXX XXX XXXX"),
        new("VN", "Vietnam", "+84", "XX XXXX XXXX"),
        new("EH", "Western Sahara", "+212", "XX XXX XXXX"),
        new("YE", "Yemen", "+967", "XXX XXX XXX"),
        new("ZM", "Zambia", "+260", "XX XXX XXXX"),
        new("ZW", "Zimbabwe", "+263", "XX XXX XXXX")
    ];

    /// <summary>
    /// Gets just the country names for simple dropdowns.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = All.Select(c => c.Name).ToList();

    /// <summary>
    /// Gets the priority/common countries that should appear at the top of country lists.
    /// </summary>
    public static IReadOnlyList<CountryInfo> Priority { get; } =
        PriorityCodes
            .Select(code => All.First(c => c.Code == code))
            .ToList();

    /// <summary>
    /// Gets all countries with priority countries listed first, followed by all countries alphabetically.
    /// </summary>
    public static IReadOnlyList<CountryInfo> AllWithPriorityFirst { get; } =
        Priority.Concat(All).ToList();

    /// <summary>
    /// Checks if a country is in the priority list.
    /// </summary>
    public static bool IsPriority(string code) =>
        PriorityCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Mapping of common country aliases, abbreviations, and alternative names to their canonical names.
    /// </summary>
    private static readonly Dictionary<string, string> CountryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // United States
        { "US", "United States" },
        { "USA", "United States" },
        { "U.S.", "United States" },
        { "U.S.A.", "United States" },
        { "United States of America", "United States" },
        { "America", "United States" },
        { "States", "United States" },
        { "The States", "United States" },

        // United Kingdom
        { "GB", "United Kingdom" },
        { "GBR", "United Kingdom" },
        { "UK", "United Kingdom" },
        { "U.K.", "United Kingdom" },
        { "Great Britain", "United Kingdom" },
        { "Britain", "United Kingdom" },
        { "England", "United Kingdom" },
        { "Scotland", "United Kingdom" },
        { "Wales", "United Kingdom" },
        { "Northern Ireland", "United Kingdom" },

        // Canada
        { "CA", "Canada" },
        { "CAN", "Canada" },

        // Australia
        { "AU", "Australia" },
        { "AUS", "Australia" },

        // Germany
        { "DE", "Germany" },
        { "DEU", "Germany" },
        { "Deutschland", "Germany" },
        { "Federal Republic of Germany", "Germany" },

        // France
        { "FR", "France" },
        { "FRA", "France" },
        { "French Republic", "France" },

        // Italy
        { "IT", "Italy" },
        { "ITA", "Italy" },
        { "Italian Republic", "Italy" },

        // Spain
        { "ES", "Spain" },
        { "ESP", "Spain" },
        { "Kingdom of Spain", "Spain" },

        // Netherlands
        { "NL", "Netherlands" },
        { "NLD", "Netherlands" },
        { "Holland", "Netherlands" },
        { "Kingdom of the Netherlands", "Netherlands" },

        // Belgium
        { "BE", "Belgium" },
        { "BEL", "Belgium" },
        { "Kingdom of Belgium", "Belgium" },

        // Switzerland
        { "CH", "Switzerland" },
        { "CHE", "Switzerland" },
        { "Swiss Confederation", "Switzerland" },

        // Austria
        { "AT", "Austria" },
        { "AUT", "Austria" },
        { "Republic of Austria", "Austria" },

        // Sweden
        { "SE", "Sweden" },
        { "SWE", "Sweden" },
        { "Kingdom of Sweden", "Sweden" },

        // Norway
        { "NO", "Norway" },
        { "NOR", "Norway" },
        { "Kingdom of Norway", "Norway" },

        // Denmark
        { "DK", "Denmark" },
        { "DNK", "Denmark" },
        { "Kingdom of Denmark", "Denmark" },

        // Finland
        { "FI", "Finland" },
        { "FIN", "Finland" },
        { "Republic of Finland", "Finland" },

        // Ireland
        { "IE", "Ireland" },
        { "IRL", "Ireland" },
        { "Republic of Ireland", "Ireland" },

        // Portugal
        { "PT", "Portugal" },
        { "PRT", "Portugal" },
        { "Portuguese Republic", "Portugal" },

        // Greece
        { "GR", "Greece" },
        { "GRC", "Greece" },
        { "Hellenic Republic", "Greece" },

        // Poland
        { "PL", "Poland" },
        { "POL", "Poland" },
        { "Republic of Poland", "Poland" },

        // Czechia
        { "CZ", "Czechia" },
        { "CZE", "Czechia" },
        { "Czech Republic", "Czechia" },

        // New Zealand
        { "NZ", "New Zealand" },
        { "NZL", "New Zealand" },
        { "Aotearoa", "New Zealand" },

        // Japan
        { "JP", "Japan" },
        { "JPN", "Japan" },
        { "Nippon", "Japan" },

        // China
        { "CN", "China" },
        { "CHN", "China" },
        { "PRC", "China" },
        { "People's Republic of China", "China" },
        { "Mainland China", "China" },

        // South Korea
        { "KR", "South Korea" },
        { "KOR", "South Korea" },
        { "Korea", "South Korea" },
        { "Republic of Korea", "South Korea" },
        { "ROK", "South Korea" },

        // India
        { "IN", "India" },
        { "IND", "India" },
        { "Republic of India", "India" },
        { "Bharat", "India" },

        // Brazil
        { "BR", "Brazil" },
        { "BRA", "Brazil" },
        { "Federative Republic of Brazil", "Brazil" },

        // Mexico
        { "MX", "Mexico" },
        { "MEX", "Mexico" },
        { "United Mexican States", "Mexico" },

        // Argentina
        { "AR", "Argentina" },
        { "ARG", "Argentina" },
        { "Argentine Republic", "Argentina" },

        // Russia
        { "RU", "Russia" },
        { "RUS", "Russia" },
        { "Russian Federation", "Russia" },
        { "USSR", "Russia" },
        { "Soviet Union", "Russia" },

        // United Arab Emirates
        { "AE", "United Arab Emirates" },
        { "ARE", "United Arab Emirates" },
        { "UAE", "United Arab Emirates" },

        // Singapore
        { "SG", "Singapore" },
        { "SGP", "Singapore" },
        { "Republic of Singapore", "Singapore" },

        // Israel
        { "IL", "Israel" },
        { "ISR", "Israel" },
        { "State of Israel", "Israel" },

        // South Africa
        { "ZA", "South Africa" },
        { "ZAF", "South Africa" },
        { "Republic of South Africa", "South Africa" },

        // Taiwan
        { "TW", "Taiwan" },
        { "TWN", "Taiwan" },
        { "ROC", "Taiwan" },
        { "Republic of China", "Taiwan" },

        // Thailand
        { "TH", "Thailand" },
        { "THA", "Thailand" },
        { "Kingdom of Thailand", "Thailand" },

        // Malaysia
        { "MY", "Malaysia" },
        { "MYS", "Malaysia" },

        // Indonesia
        { "ID", "Indonesia" },
        { "IDN", "Indonesia" },

        // Philippines
        { "PH", "Philippines" },
        { "PHL", "Philippines" },

        // Vietnam
        { "VN", "Vietnam" },
        { "VNM", "Vietnam" },
        { "Socialist Republic of Vietnam", "Vietnam" },

        // Egypt
        { "EG", "Egypt" },
        { "EGY", "Egypt" },
        { "Arab Republic of Egypt", "Egypt" },

        // Turkey
        { "TR", "Turkey" },
        { "TUR", "Turkey" },
        { "Republic of Turkey", "Turkey" },

        // Ukraine
        { "UA", "Ukraine" },
        { "UKR", "Ukraine" },

        // Hungary
        { "HU", "Hungary" },
        { "HUN", "Hungary" },

        // Romania
        { "RO", "Romania" },
        { "ROU", "Romania" },

        // Bulgaria
        { "BG", "Bulgaria" },
        { "BGR", "Bulgaria" },
        { "Republic of Bulgaria", "Bulgaria" },

        // Croatia
        { "HR", "Croatia" },
        { "HRV", "Croatia" },
        { "Republic of Croatia", "Croatia" },

        // Slovakia
        { "SK", "Slovakia" },
        { "SVK", "Slovakia" },
        { "Slovak Republic", "Slovakia" },

        // Slovenia
        { "SI", "Slovenia" },
        { "SVN", "Slovenia" },
        { "Republic of Slovenia", "Slovenia" },

        // Estonia
        { "EE", "Estonia" },
        { "EST", "Estonia" },
        { "Republic of Estonia", "Estonia" },

        // Latvia
        { "LV", "Latvia" },
        { "LVA", "Latvia" },
        { "Republic of Latvia", "Latvia" },

        // Lithuania
        { "LT", "Lithuania" },
        { "LTU", "Lithuania" },
        { "Republic of Lithuania", "Lithuania" },

        // Luxembourg
        { "LU", "Luxembourg" },
        { "LUX", "Luxembourg" },

        // Iceland
        { "IS", "Iceland" },
        { "ISL", "Iceland" },

        // Malta
        { "MT", "Malta" },
        { "MLT", "Malta" },

        // Cyprus
        { "CY", "Cyprus" },
        { "CYP", "Cyprus" },

        // North Macedonia
        { "MK", "North Macedonia" },
        { "MKD", "North Macedonia" },
        { "Macedonia", "North Macedonia" },

        // Serbia
        { "RS", "Serbia" },
        { "SRB", "Serbia" },

        // Montenegro
        { "ME", "Montenegro" },
        { "MNE", "Montenegro" },

        // Bosnia and Herzegovina
        { "BA", "Bosnia and Herzegovina" },
        { "BIH", "Bosnia and Herzegovina" },

        // Albania
        { "AL", "Albania" },
        { "ALB", "Albania" },

        // Moldova
        { "MD", "Moldova" },
        { "MDA", "Moldova" },
        { "Republic of Moldova", "Moldova" },

        // Belarus
        { "BY", "Belarus" },
        { "BLR", "Belarus" },
        { "Republic of Belarus", "Belarus" },

        // North Korea
        { "KP", "North Korea" },
        { "PRK", "North Korea" },
        { "Democratic People's Republic of Korea", "North Korea" },
        { "DPRK", "North Korea" },

        // Ivory Coast
        { "CI", "Ivory Coast" },
        { "CIV", "Ivory Coast" },
        { "Côte d'Ivoire", "Ivory Coast" },

        // Democratic Republic of the Congo
        { "CD", "The Democratic Republic of the Congo" },
        { "COD", "The Democratic Republic of the Congo" },
        { "DRC", "The Democratic Republic of the Congo" },
        { "Congo-Kinshasa", "The Democratic Republic of the Congo" },

        // Republic of the Congo
        { "CG", "The Republic of the Congo" },
        { "COG", "The Republic of the Congo" },
        { "Congo-Brazzaville", "The Republic of the Congo" },

        // Myanmar
        { "MM", "Myanmar" },
        { "MMR", "Myanmar" },
        { "Burma", "Myanmar" },

        // Lao
        { "LA", "Lao" },
        { "LAO", "Lao" },
        { "Laos", "Lao" },

        // Timor-Leste
        { "TL", "Timor-Leste" },
        { "TLS", "Timor-Leste" },
        { "East Timor", "Timor-Leste" },

        // Eswatini
        { "SZ", "Eswatini" },
        { "SWZ", "Eswatini" },
        { "Swaziland", "Eswatini" },

        // Cabo Verde
        { "CV", "Cabo Verde" },
        { "CPV", "Cabo Verde" },
        { "Cape Verde", "Cabo Verde" },
    };

    /// <summary>
    /// Normalizes a country input string to its canonical country name.
    /// Handles ISO codes (US, USA), common abbreviations, and alternative names.
    /// Returns null if no match is found.
    /// </summary>
    /// <param name="input">The country input to normalize (can be code, abbreviation, or name).</param>
    /// <returns>The canonical country name, or null if not found.</returns>
    public static string? NormalizeCountry(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var trimmed = input.Trim();

        // First check if it's already a canonical name
        if (All.Any(c => c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            return All.First(c => c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)).Name;

        // Check aliases
        if (CountryAliases.TryGetValue(trimmed, out var canonicalName))
            return canonicalName;

        if (trimmed.Length == 2 && All.FirstOrDefault(c => c.Code.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) is { } byCode)
            return byCode.Name;

        if (trimmed.Length == 3 && GetByAlpha3Code(trimmed) is { } byAlpha3)
            return byAlpha3.Name;

        // No match found
        return null;
    }

    /// <summary>
    /// The country for a name, alias, or 2- or 3-letter ISO code, or null if not recognised.
    /// </summary>
    public static CountryInfo? Find(string? input) =>
        NormalizeCountry(input) is { } name ? All.FirstOrDefault(c => c.Name == name) : null;

    /// <summary>
    /// Uppercase ISO 3166-1 alpha-3 code for a country's alpha-2 code, or null if unknown.
    /// </summary>
    public static string? GetAlpha3Code(string alpha2) =>
        Alpha3ByAlpha2.Value.TryGetValue(alpha2, out var alpha3) ? alpha3 : null;

    /// <summary>
    /// The country with this ISO 3166-1 alpha-3 code (any case), or null if unknown.
    /// </summary>
    public static CountryInfo? GetByAlpha3Code(string alpha3) =>
        CountryByAlpha3.Value.TryGetValue(alpha3, out var country) ? country : null;

    // Canonical countries the runtime's region data does not carry (Windows ICU has no Western Sahara).
    private static readonly Dictionary<string, string> Alpha3Fallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EH"] = "ESH",
    };

    // RegionInfo is built from the alpha-2 code directly, which covers far more regions than
    // the list of installed cultures does.
    private static readonly Lazy<Dictionary<string, string>> Alpha3ByAlpha2 = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var country in All)
        {
            try
            {
                var alpha3 = new RegionInfo(country.Code).ThreeLetterISORegionName;
                if (alpha3.Length == 3 && alpha3.All(char.IsAsciiLetter))
                    map[country.Code] = alpha3.ToUpperInvariant();
            }
            catch (ArgumentException)
            {
            }

            if (!map.ContainsKey(country.Code) && Alpha3Fallbacks.TryGetValue(country.Code, out var fallback))
                map[country.Code] = fallback;
        }
        return map;
    });

    private static readonly Lazy<Dictionary<string, CountryInfo>> CountryByAlpha3 = new(() =>
        All.Where(c => Alpha3ByAlpha2.Value.ContainsKey(c.Code))
            .ToDictionary(c => Alpha3ByAlpha2.Value[c.Code], StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Normalizes a country input string, returning the original value if no match is found.
    /// </summary>
    /// <param name="input">The country input to normalize.</param>
    /// <returns>The canonical country name, or the original input if not found.</returns>
    public static string NormalizeCountryOrKeep(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return NormalizeCountry(input) ?? input.Trim();
    }
}

/// <summary>
/// Represents country information including dial code and phone format.
/// </summary>
public class CountryInfo(string code, string name, string dialCode, string phoneFormat, string? flagFileName = null)
{
    /// <summary>
    /// ISO country code (e.g., US, GB).
    /// </summary>
    public string Code { get; } = code;

    /// <summary>
    /// Country name for display.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Phone dial code (e.g., +1, +44).
    /// </summary>
    public string DialCode { get; } = dialCode;

    /// <summary>
    /// Phone number format pattern using X for digits.
    /// </summary>
    public string PhoneFormat { get; } = phoneFormat;

    /// <summary>
    /// Flag file name (defaults to Name if not specified).
    /// </summary>
    public string FlagFileName { get; } = flagFileName ?? name;

    public override string ToString() => Name;
}
