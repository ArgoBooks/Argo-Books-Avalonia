using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Payroll;
using ArgoBooks.Localization;

namespace ArgoBooks.Services;

/// <summary>A choice in the CRA class drop-downs, where a null number means the asset is depreciated however the user sets it.</summary>
public record CcaClassOption(string? Number, string Label)
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

    /// <summary>Every class with today's rate, after a choice for setting the depreciation by hand.</summary>
    public static List<CcaClassOption> Options()
    {
        var options = new List<CcaClassOption> { new(null, "No CRA class (set it myself)".Translate()) };
        if (Rates.Current is { } file)
        {
            options.AddRange(file.Classes.Select(c => new CcaClassOption(c.Number,
                "Class {0}: {1} ({2}%)".TranslateFormat(c.Number, c.Description, Percent(file.RateOn(c.Number, DateTime.Today) ?? 0m)))));
        }
        return options;
    }

    public static CcaClassOption? DefaultFor(IEnumerable<CcaClassOption> options, AssetType type)
    {
        var number = Rates.Current?.DefaultClassFor(type);
        return options.FirstOrDefault(o => o.Number == number);
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
