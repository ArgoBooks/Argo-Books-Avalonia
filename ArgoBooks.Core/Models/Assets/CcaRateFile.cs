namespace ArgoBooks.Core.Models.Assets;

/// <summary>
/// CRA's Capital Cost Allowance classes and first-year rules, delivered as data like the payroll tables (docs/AssetRateUpdates.md).
/// </summary>
public class CcaRateFile
{
    /// <summary>Raised by one with every change, so the app only replaces its copy with a newer one.</summary>
    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    /// <summary>When the figures were last checked against CRA, which the reminder email reads.</summary>
    [JsonPropertyName("reviewedOn")]
    public DateTime ReviewedOn { get; set; }

    [JsonPropertyName("classes")]
    public List<CcaClass> Classes { get; set; } = [];

    [JsonPropertyName("firstYearRules")]
    public List<CcaFirstYearRule> FirstYearRules { get; set; } = [];

    /// <summary>The class each asset type starts in, keyed by the <see cref="AssetType"/> name.</summary>
    [JsonPropertyName("typeDefaults")]
    public Dictionary<string, string> TypeDefaults { get; set; } = [];

    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = [];

    public CcaClass? GetClass(string? number) =>
        number == null ? null : Classes.FirstOrDefault(c => c.Number == number);

    public string? DefaultClassFor(AssetType type) =>
        TypeDefaults.TryGetValue(type.ToString(), out var number) && GetClass(number) != null ? number : null;

    /// <summary>The class's yearly rate as a percentage for an asset bought on <paramref name="date"/>, or null when none applies then.</summary>
    public decimal? RateOn(string classNumber, DateTime date) =>
        GetClass(classNumber)?.Rates.FirstOrDefault(r => r.Covers(date))?.Percent;

    /// <summary>
    /// How much of the rate the first year gets for an asset bought on <paramref name="date"/>: 0.5 under the half-year rule. A rule naming the class wins over a general one.
    /// </summary>
    public decimal FirstYearFactorOn(string classNumber, DateTime date)
    {
        var rules = FirstYearRules.Where(r => r.Covers(date)).ToList();
        var rule = rules.FirstOrDefault(r => r.Classes?.Contains(classNumber) == true)
                   ?? rules.FirstOrDefault(r => r.Classes is not { Count: > 0 });
        return rule?.Factor ?? 0.5m;
    }
}

public class CcaClass
{
    /// <summary>CRA's class number, such as "10" or "10.1".</summary>
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("rates")]
    public List<CcaRate> Rates { get; set; } = [];
}

public class CcaRate
{
    [JsonPropertyName("percent")]
    public decimal Percent { get; set; }

    /// <summary>The first purchase date this rate applies to, or null for any date before <see cref="To"/>.</summary>
    [JsonPropertyName("from")]
    public DateTime? From { get; set; }

    /// <summary>The last purchase date this rate applies to, or null when it still applies.</summary>
    [JsonPropertyName("to")]
    public DateTime? To { get; set; }

    public bool Covers(DateTime date) =>
        (From is not { } from || date.Date >= from.Date) && (To is not { } to || date.Date <= to.Date);
}

public class CcaFirstYearRule
{
    /// <summary>The share of the rate claimed in the year an asset is bought: 0.5 under the half-year rule, 1.5 under the Accelerated Investment Incentive.</summary>
    [JsonPropertyName("factor")]
    public decimal Factor { get; set; }

    [JsonPropertyName("from")]
    public DateTime? From { get; set; }

    [JsonPropertyName("to")]
    public DateTime? To { get; set; }

    /// <summary>The classes this rule is limited to, or null for every class without a rule of its own.</summary>
    [JsonPropertyName("classes")]
    public List<string>? Classes { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    public bool Covers(DateTime date) =>
        (From is not { } from || date.Date >= from.Date) && (To is not { } to || date.Date <= to.Date);
}
