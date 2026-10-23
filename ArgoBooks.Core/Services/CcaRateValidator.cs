using ArgoBooks.Core.Models.Assets;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Checks a CCA rate file before the app uses it, the same check whether it was built into the app or downloaded.
/// </summary>
public static class CcaRateValidator
{
    /// <summary>Everything wrong with the file, or an empty list when it is fit to use.</summary>
    public static List<string> Validate(CcaRateFile? file)
    {
        var problems = new List<string>();
        if (file == null)
        {
            problems.Add("The file is empty or could not be read.");
            return problems;
        }

        if (file.Revision < 1)
            problems.Add("The revision must be 1 or more.");

        if (file.Classes.Count == 0)
            problems.Add("There are no classes.");

        foreach (var duplicate in file.Classes.GroupBy(c => c.Number).Where(g => g.Count() > 1))
            problems.Add($"Class {duplicate.Key} appears more than once.");

        foreach (var cca in file.Classes)
        {
            if (string.IsNullOrWhiteSpace(cca.Number))
                problems.Add("A class has no number.");
            if (string.IsNullOrWhiteSpace(cca.Description))
                problems.Add($"Class {cca.Number} has no description.");
            if (cca.Rates.Count == 0)
                problems.Add($"Class {cca.Number} has no rate.");

            foreach (var rate in cca.Rates)
            {
                if (rate.Percent is <= 0 or > 100)
                    problems.Add($"Class {cca.Number} has a rate of {rate.Percent}%, which is not above 0 and at most 100.");
                if (rate.From is { } from && rate.To is { } to && to < from)
                    problems.Add($"Class {cca.Number} has a rate that ends before it starts.");
            }

            if (Overlaps(cca.Rates.Select(r => (r.From, r.To)).ToList()))
                problems.Add($"Class {cca.Number} has rates whose dates overlap.");

            if (cca.AssetType != null && (!Enum.TryParse<AssetType>(cca.AssetType, out var offeredAs) || offeredAs == AssetType.Land))
                problems.Add($"Class {cca.Number} is offered as \"{cca.AssetType}\", which is not an asset type that depreciates.");
            if (cca.AssetType != null && string.IsNullOrWhiteSpace(cca.Label))
                problems.Add($"Class {cca.Number} is offered as a type but has no label for the list.");
        }

        foreach (var rule in file.FirstYearRules)
        {
            if (rule.Factor is <= 0 or > 3)
                problems.Add($"The first-year rule \"{rule.Name}\" has a factor of {rule.Factor}, which is not above 0 and at most 3.");
            if (rule.From is { } from && rule.To is { } to && to < from)
                problems.Add($"The first-year rule \"{rule.Name}\" ends before it starts.");
            foreach (var number in rule.Classes ?? [])
            {
                if (file.GetClass(number) == null)
                    problems.Add($"The first-year rule \"{rule.Name}\" names class {number}, which is not in the file.");
            }
        }

        // Two general rules covering the same day would make the first year depend on their order in the file.
        if (Overlaps(file.FirstYearRules.Where(r => r.Classes is not { Count: > 0 }).Select(r => (r.From, r.To)).ToList()))
            problems.Add("Two first-year rules for every class cover the same dates.");

        foreach (var (type, number) in file.TypeDefaults)
        {
            if (!Enum.TryParse<AssetType>(type, out var parsed) || parsed == AssetType.Land)
                problems.Add($"The default for \"{type}\" is not for an asset type that depreciates.");
            if (file.GetClass(number) == null)
                problems.Add($"The default for {type} is class {number}, which is not in the file.");
        }

        return problems;
    }

    private static bool Overlaps(List<(DateTime? From, DateTime? To)> ranges)
    {
        var ordered = ranges.OrderBy(r => r.From ?? DateTime.MinValue).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var previousEnd = ordered[i - 1].To ?? DateTime.MaxValue;
            var start = ordered[i].From ?? DateTime.MinValue;
            if (start <= previousEnd)
                return true;
        }
        return false;
    }
}
