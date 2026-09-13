namespace ArgoBooks.Core.Models;

/// <summary>
/// The industries a company can choose. The chosen name itself is what the company file stores, so
/// the create wizard, the edit dialog and the starter categories all read these rather than each
/// spelling them out, and rewording one can't leave them disagreeing.
/// </summary>
public static class IndustryNames
{
    public const string Retail = "Retail";
    public const string Services = "Services";
    public const string Manufacturing = "Manufacturing";
    public const string Technology = "Technology";
    public const string Healthcare = "Healthcare";
    public const string FoodAndBeverage = "Food & Beverage";
    public const string Construction = "Construction";
    public const string Transportation = "Transportation";
    public const string RealEstate = "Real Estate";
    public const string Other = "Other";

    public static readonly string[] All =
    [
        Retail,
        Services,
        Manufacturing,
        Technology,
        Healthcare,
        FoodAndBeverage,
        Construction,
        Transportation,
        RealEstate,
        Other
    ];
}
