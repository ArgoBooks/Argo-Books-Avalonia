using ArgoBooks.Core.Models.Payroll;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Checks that a rate table is not self-contradictory, before it is allowed to decide anyone's
/// withholding.
///
/// This exists because a rate file can now arrive over the network. Every other download in
/// this app degrades gracefully if it is wrong: a bad translation shows English, a missing
/// exchange rate shows Pending. A bad rate table produces a deduction that looks entirely
/// ordinary on a pay stub and is wrong by a few dollars a period for a real person, and there
/// is nothing downstream that would question it.
///
/// The checks are the ones docs/PayrollRateUpdates.md already tells a human to run by hand
/// before trusting a new edition. Both are arithmetic on the file's own contents, so nothing
/// here needs to know what the correct 2027 figures are: a table that contradicts itself is
/// wrong whatever the source, and one that does not at least cannot be quietly truncated,
/// mistyped or half-transcribed.
///
/// What this deliberately does NOT do is check the numbers against CRA. It cannot: that is
/// what the verification pass with the published tables is for. A table can pass every check
/// here and still carry last year's figures. This is a floor, not a proof.
/// </summary>
public static class PayrollRateValidator
{
    /// <summary>
    /// Bracket constants are published rounded to whole dollars, so continuity at a boundary is
    /// only ever exact to within the rounding. Anything past a dollar is a wrong rate or a
    /// wrong constant rather than the rounding.
    /// </summary>
    private const decimal ContinuityTolerance = 1m;

    /// <summary>Derived maximums are published to the cent and reproduce exactly.</summary>
    private const decimal MoneyTolerance = 0.01m;

    /// <summary>
    /// Everything wrong with this table, or an empty list when it is fit to use. Messages
    /// rather than an exception, so a caller can log all of them at once: a truncated file
    /// fails several checks and seeing one at a time would take several downloads to diagnose.
    /// </summary>
    public static List<string> Validate(PayrollRateTable? table)
    {
        var problems = new List<string>();

        if (table == null)
        {
            problems.Add("There is no rate table to check.");
            return problems;
        }

        if (string.IsNullOrWhiteSpace(table.EditionId))
        {
            problems.Add("The table has no edition id, so nothing can say which rules produced a pay run.");
        }

        if (table.EffectiveFrom.Date >= table.EffectiveTo.Date)
        {
            problems.Add($"The effective range runs backwards or covers nothing: "
                         + $"{table.EffectiveFrom:yyyy-MM-dd} to {table.EffectiveTo:yyyy-MM-dd}.");
        }

        if (table.Provinces.Count == 0)
        {
            problems.Add("The table carries no province at all, so no pay run outside Quebec could be calculated.");
        }

        CheckMaximum(problems, "CPP maximum",
            (table.Cpp.YmpeCeiling - table.Cpp.BasicExemptionAnnual) * table.Cpp.RateEmployee,
            table.Cpp.MaxContributionEmployee);

        CheckMaximum(problems, "CPP2 maximum",
            (table.Cpp2.YampeCeiling - table.Cpp.YmpeCeiling) * table.Cpp2.RateEmployee,
            table.Cpp2.MaxContributionEmployee);

        CheckMaximum(problems, "EI maximum",
            table.Ei.MaxInsurableEarnings * table.Ei.RateEmployee,
            table.Ei.MaxPremiumEmployee);

        CheckBrackets(problems, "Federal", table.Federal.Brackets);

        foreach ((string code, ProvincialRates province) in table.Provinces)
        {
            CheckBrackets(problems, code, province.Brackets);
        }

        // Quebec is optional. An edition carrying none is refused at calculation time with a
        // message naming the province, which is a different problem from a malformed file.
        if (table.Quebec is { } quebec)
        {
            CheckBrackets(problems, "Quebec", quebec.Brackets);

            CheckMaximum(problems, "QPP maximum",
                (quebec.Qpp.YmpeCeiling - quebec.Qpp.BasicExemptionAnnual) * quebec.Qpp.RateEmployee,
                quebec.Qpp.MaxContributionEmployee);

            CheckMaximum(problems, "QPIP maximum",
                quebec.Qpip.MaxInsurableEarnings * quebec.Qpip.RateEmployee,
                quebec.Qpip.MaxPremiumEmployee);

            // Against Quebec's own reduced rate, not the federal one.
            CheckMaximum(problems, "Quebec EI maximum",
                table.Ei.MaxInsurableEarnings * table.Ei.QuebecRateEmployee,
                quebec.EiMaxPremiumEmployee);
        }

        return problems;
    }

    /// <summary>
    /// A published maximum has to be the product of the rate and the earnings it applies to.
    /// Nothing fabricated survives this, because the maximum and the rate would both have to be
    /// invented consistently.
    /// </summary>
    private static void CheckMaximum(List<string> problems, string what, decimal derived, decimal published)
    {
        // A zero rate or ceiling means the section is absent rather than wrong. Quebec editions
        // predating a plan, and test fixtures, both look like this.
        if (derived <= 0m && published <= 0m)
        {
            return;
        }

        if (Math.Abs(derived - published) > MoneyTolerance)
        {
            problems.Add($"The {what} of {published:N2} does not follow from its own rate, "
                         + $"which gives {derived:N2}.");
        }
    }

    /// <summary>
    /// Brackets have to ascend, end open, and meet. The meeting is the one that matters: at each
    /// boundary the band below and the band above must charge the same tax, or every income
    /// above that point is out by the gap and the table still looks perfectly ordinary.
    /// </summary>
    private static void CheckBrackets(List<string> problems, string who, List<TaxBracket> brackets)
    {
        if (brackets.Count == 0)
        {
            problems.Add($"{who} has no tax brackets.");
            return;
        }

        if (brackets[^1].UpTo != null)
        {
            problems.Add($"{who}'s top bracket is not open ended, so the highest incomes fall off "
                         + "the end of the table.");
        }

        for (int i = 0; i < brackets.Count - 1; i++)
        {
            decimal? boundary = brackets[i].UpTo;

            if (boundary == null)
            {
                problems.Add($"{who} has an open ended bracket at position {i + 1} with more below it.");
                return;
            }

            if (i > 0 && brackets[i - 1].UpTo >= boundary)
            {
                problems.Add($"{who}'s brackets do not ascend: {brackets[i - 1].UpTo:N0} is not below {boundary:N0}.");
                return;
            }

            decimal below = brackets[i].Rate * boundary.Value - brackets[i].ConstantK;
            decimal above = brackets[i + 1].Rate * boundary.Value - brackets[i + 1].ConstantK;

            if (Math.Abs(below - above) > ContinuityTolerance)
            {
                problems.Add($"{who}'s brackets do not meet at {boundary:N0}: the band below gives "
                             + $"{below:N2} and the band above gives {above:N2}.");
            }
        }
    }
}
