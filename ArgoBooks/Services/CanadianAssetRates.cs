using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Payroll;
using ArgoBooks.Localization;

namespace ArgoBooks.Services;

/// <summary>A choice in the asset type lists, which for a Canadian company also names the CRA class it goes in.</summary>
public record AssetTypeOption(AssetType Type, string? CcaClass, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Fills in CRA's Capital Cost Allowance class and rate for a Canadian company's assets, shared by the asset form and the expense form.
/// </summary>
public static class CanadianAssetRates
{
    private static bool _updateChecked;

    public static CcaRateService Rates { get; } = new();

    /// <summary>Checks the website for newer rates once per run of the app, in the background.</summary>
    public static void CheckForUpdateOnce()
    {
        if (_updateChecked)
            return;
        _updateChecked = true;
        _ = new CcaRateUpdateService(Rates).TryUpdateAsync();
    }

    public static bool AppliesTo(CompanyData? data) => data != null && CraFormat.IsCanada(data.Settings.Company.Country);

    /// <summary>The types the asset forms offer, each with its CRA class and rate for a Canadian company, so one list picks both.</summary>
    public static List<AssetTypeOption> TypeOptions(CompanyData? data)
    {
        var file = AppliesTo(data) ? Rates.Current : null;
        var options = new List<AssetTypeOption>();

        foreach (var type in Enum.GetValues<AssetType>())
        {
            var name = type.ToDisplayName().Translate();
            options.Add(file?.DefaultClassFor(type) is { } number ? WithClass(file, type, number, name) : new AssetTypeOption(type, null, name));

            if (file == null)
                continue;
            foreach (var extra in file.Classes.Where(c => c.AssetType == type.ToString() && c.Label != null))
                options.Add(WithClass(file, type, extra.Number, extra.Label!.Translate()));
        }

        return options;
    }

    private static AssetTypeOption WithClass(CcaRateFile file, AssetType type, string number, string name) =>
        new(type, number, "{0} (Class {1}, {2}%)".TranslateFormat(name, number, Percent(file.RateOn(number, DateTime.Today) ?? 0m)));

    /// <summary>The choice for an asset's type and class, added to <paramref name="options"/> when it is a pairing the list does not offer, such as one imported from a sheet.</summary>
    public static AssetTypeOption Find(List<AssetTypeOption> options, AssetType type, string? ccaClass)
    {
        if (options.FirstOrDefault(o => o.Type == type && o.CcaClass == ccaClass) is { } found)
            return found;

        var name = type.ToDisplayName().Translate();
        var added = new AssetTypeOption(type, ccaClass, ccaClass == null ? name : "{0} (Class {1})".TranslateFormat(name, ccaClass));
        options.Add(added);
        return added;
    }

    /// <summary>The class with the rate and first-year share CRA gives it for an asset bought on <paramref name="purchaseDate"/>, or null when it has no rate then.</summary>
    public static ExpenseAssetLink.CcaChoice? Choice(string? classNumber, DateTime purchaseDate)
    {
        if (classNumber == null || Rates.Current is not { } file || file.RateOn(classNumber, purchaseDate) is not { } rate)
            return null;
        return new ExpenseAssetLink.CcaChoice(classNumber, rate, file.FirstYearFactorOn(classNumber, purchaseDate));
    }

    /// <summary>A line explaining the class's rate, such as "CRA Class 10: 30% a year, and half that in the year it was bought."</summary>
    public static string Describe(ExpenseAssetLink.CcaChoice choice)
    {
        if (choice.RatePercent * choice.FirstYearFactor >= 100m)
            return "CRA Class {0}: {1}% a year, and the whole cost in the year it was bought.".TranslateFormat(choice.ClassNumber, Percent(choice.RatePercent));

        var firstYear = choice.FirstYearFactor switch
        {
            0.5m => "half that in the year it was bought".Translate(),
            1m => "the full rate in the year it was bought too".Translate(),
            1.5m => "one and a half times that in the year it was bought".Translate(),
            _ => "{0} times that in the year it was bought".TranslateFormat(Percent(choice.FirstYearFactor))
        };
        return "CRA Class {0}: {1}% a year, and {2}.".TranslateFormat(choice.ClassNumber, Percent(choice.RatePercent), firstYear);
    }

    private static string Percent(decimal value) => value.ToString("0.##");
}
