using ArgoBooks.Core.Enums;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Which sales tax system a country uses. Invoices and accounting reports both read it here, so
/// an invoice's tax label always matches the reports.
/// </summary>
public static class TaxSystems
{
    public static TaxSystem For(string? country) => (country?.Trim().ToUpperInvariant() ?? "") switch
    {
        "UNITED KINGDOM" or "FRANCE" or "GERMANY" or "ITALY" or "SPAIN" or "NETHERLANDS"
        or "BELGIUM" or "AUSTRIA" or "SWEDEN" or "NORWAY" or "DENMARK" or "FINLAND"
        or "IRELAND" or "PORTUGAL" or "GREECE" or "SWITZERLAND" or "POLAND"
        or "CZECH REPUBLIC" or "CZECHIA" or "HUNGARY" or "ROMANIA" or "BULGARIA" or "CROATIA"
        or "SLOVAKIA" or "SLOVENIA" or "LITHUANIA" or "LATVIA" or "ESTONIA"
        or "LUXEMBOURG" or "MALTA" or "CYPRUS" or "SOUTH AFRICA" or "KENYA"
        or "NIGERIA" or "GHANA" or "ZIMBABWE" or "BOTSWANA"
        or "BANGLADESH" or "SRI LANKA" or "JAMAICA" or "TRINIDAD AND TOBAGO"
        or "TURKEY" or "RUSSIA" or "UKRAINE" or "BRAZIL"
        or "ARGENTINA" or "CHILE" or "COLOMBIA" or "MEXICO" or "PERU"
        or "ISRAEL" or "UNITED ARAB EMIRATES" or "SAUDI ARABIA" or "THAILAND"
        or "VIETNAM" or "INDONESIA" or "PHILIPPINES" or "SOUTH KOREA"
        or "CHINA" or "TAIWAN" or "ICELAND" => TaxSystem.VAT,

        "CANADA" => TaxSystem.GstHst,
        "INDIA" or "SINGAPORE" or "MALAYSIA" or "AUSTRALIA" or "NEW ZEALAND"
        or "PAKISTAN" => TaxSystem.GST,
        "JAPAN" => TaxSystem.JCT,
        "UNITED STATES" or "PUERTO RICO" => TaxSystem.SalesTax,

        _ => TaxSystem.Tax
    };

    /// <summary>The short name an invoice prints beside its tax line.</summary>
    public static string InvoiceLabel(TaxSystem system) => system switch
    {
        TaxSystem.VAT => "VAT",
        TaxSystem.GstHst => "GST/HST",
        TaxSystem.GST => "GST",
        TaxSystem.JCT => "Consumption Tax",
        TaxSystem.SalesTax => "Sales Tax",
        _ => "Tax"
    };
}
