namespace ArgoBooks.Data;

/// <summary>
/// Provides a shared list of supported currencies.
/// </summary>
public static class Currencies
{
    /// <summary>
    /// Priority/common currencies shown at the top of dropdowns.
    /// </summary>
    public static readonly IReadOnlyList<string> Priority =
    [
        "USD - US Dollar ($)",
        "EUR - Euro (€)",
        "CAD - Canadian Dollar ($)",
        "AUD - Australian Dollar ($)"
    ];

    /// <summary>
    /// Complete list of supported currencies.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        "AED - UAE Dirham (AED)",
        "ALL - Albanian Lek (L)",
        "ARS - Argentine Peso (AR$)",
        "AUD - Australian Dollar ($)",
        "BAM - Bosnia-Herzegovina Mark (KM)",
        "BDT - Bangladeshi Taka (Tk)",
        "BGN - Bulgarian Lev (лв)",
        "BRL - Brazilian Real (R$)",
        "BYN - Belarusian Ruble (Br)",
        "CAD - Canadian Dollar ($)",
        "CHF - Swiss Franc (CHF)",
        "CLP - Chilean Peso (CL$)",
        "CNY - Chinese Yuan (¥)",
        "COP - Colombian Peso (CO$)",
        "CZK - Czech Koruna (Kč)",
        "DKK - Danish Krone (kr)",
        "EGP - Egyptian Pound (E£)",
        "EUR - Euro (€)",
        "GBP - British Pound (£)",
        "GHS - Ghanaian Cedi (₵)",
        "HKD - Hong Kong Dollar (HK$)",
        "HUF - Hungarian Forint (Ft)",
        "IDR - Indonesian Rupiah (Rp)",
        "ILS - Israeli Shekel (₪)",
        "INR - Indian Rupee (₹)",
        "ISK - Icelandic Króna (kr)",
        "JPY - Japanese Yen (¥)",
        "KES - Kenyan Shilling (KSh)",
        "KRW - South Korean Won (₩)",
        "LKR - Sri Lankan Rupee (Rs)",
        "MAD - Moroccan Dirham (MAD)",
        "MKD - Macedonian Denar (ден)",
        "MXN - Mexican Peso (MX$)",
        "MYR - Malaysian Ringgit (RM)",
        "NGN - Nigerian Naira (₦)",
        "NOK - Norwegian Krone (kr)",
        "NZD - New Zealand Dollar (NZ$)",
        "PEN - Peruvian Sol (S/)",
        "PHP - Philippine Peso (₱)",
        "PKR - Pakistani Rupee (₨)",
        "PLN - Polish Złoty (zł)",
        "QAR - Qatari Riyal (QAR)",
        "RON - Romanian Leu (lei)",
        "RSD - Serbian Dinar (дин)",
        "RUB - Russian Ruble (₽)",
        "SAR - Saudi Riyal (SAR)",
        "SEK - Swedish Krona (kr)",
        "SGD - Singapore Dollar (S$)",
        "THB - Thai Baht (THB)",
        "TRY - Turkish Lira (₺)",
        "TWD - Taiwan Dollar (NT$)",
        "UAH - Ukrainian Hryvnia (₴)",
        "USD - US Dollar ($)",
        "VND - Vietnamese Dong (₫)",
        "ZAR - South African Rand (R)"
    ];
}
