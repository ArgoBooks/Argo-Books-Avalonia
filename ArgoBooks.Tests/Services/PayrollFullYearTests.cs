using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Payroll run over a whole year, with year-to-date advanced between periods.
///
/// This is the gap every other payroll test file shares: they call Calculate two or three times
/// against a PayrollYearToDate they never update, so nothing that accumulates is ever exercised.
///
/// A single period cannot catch an error in what carries forward. These run the full 26 and
/// check the totals, which is the only place a carry-forward mistake shows up.
/// </summary>
public class PayrollFullYearTests
{
    private static readonly PayrollRateTable Rates =
        new PayrollRateService().GetForDate(new DateTime(2026, 8, 14))!;

    private const int Periods = 26;

    /// <summary>What a year of pay came to, once every period has been run in order.</summary>
    private sealed record YearTotals(
        decimal Gross,
        decimal Federal,
        decimal Provincial,
        decimal Cpp,
        decimal Cpp2,
        decimal Ei,
        decimal Net)
    {
        public decimal Tax => Federal + Provincial;
    }

    /// <summary>
    /// Runs a year, advancing year-to-date between periods exactly as PayrollService.YearToDateFor
    /// does when it re-reads the approved runs.
    /// </summary>
    private static YearTotals RunYear(
        decimal regular,
        string province = "AB",
        Func<int, decimal>? bonusFor = null,
        Func<int, decimal>? regularFor = null)
    {
        var ytd = new PayrollYearToDate();
        decimal gross = 0, federal = 0, provincial = 0, cpp = 0, cpp2 = 0, ei = 0, net = 0;

        for (int period = 0; period < Periods; period++)
        {
            decimal basePay = regularFor?.Invoke(period) ?? regular;
            decimal bonus = bonusFor?.Invoke(period) ?? 0m;
            decimal periodGross = basePay + bonus;

            PayrollDeductions d = PayrollCalculator.Calculate(
                new PayrollInput
                {
                    GrossPay = periodGross,
                    NonPeriodicPay = bonus,
                    Province = province,
                    PayPeriodsPerYear = Periods,
                },
                ytd,
                Rates);

            gross += d.GrossPay;
            federal += d.FederalTax;
            provincial += d.ProvincialTax;
            cpp += d.CppEmployee;
            cpp2 += d.Cpp2Employee;
            ei += d.EiEmployee;
            net += d.NetPay;

            ytd.PensionableEarnings += periodGross;
            ytd.InsurableEarnings += periodGross;
            ytd.CppEmployee += d.CppEmployee;
            ytd.Cpp2Employee += d.Cpp2Employee;
            ytd.EiEmployee += d.EiEmployee;
            ytd.NonPeriodicPay += bonus;
        }

        return new YearTotals(gross, federal, provincial, cpp, cpp2, ei, net);
    }

    /// <summary>The tax withheld in each period, in order.</summary>
    private static List<decimal> RunYearPerPeriod(decimal regular, string province = "AB")
    {
        var ytd = new PayrollYearToDate();
        var series = new List<decimal>();

        for (int period = 0; period < Periods; period++)
        {
            PayrollDeductions d = PayrollCalculator.Calculate(
                new PayrollInput { GrossPay = regular, Province = province, PayPeriodsPerYear = Periods },
                ytd,
                Rates);

            series.Add(d.FederalTax + d.ProvincialTax);

            ytd.PensionableEarnings += regular;
            ytd.InsurableEarnings += regular;
            ytd.CppEmployee += d.CppEmployee;
            ytd.Cpp2Employee += d.Cpp2Employee;
            ytd.EiEmployee += d.EiEmployee;
        }

        return series;
    }

    /// <summary>
    /// The master invariant: below
    /// the annual ceilings, a year of steady pay withholds exactly 26 times one period.
    ///
    /// Only below the ceilings. Once CPP stops and CPP2 starts the deduction changes, and the
    /// credit for it changes with it, so the periodic tax legitimately settles somewhere new.
    /// That case is covered separately below.
    /// </summary>
    [Theory]
    [InlineData(1200)]
    [InlineData(1500)]
    [InlineData(2400)]
    public void BelowTheCeilings_SteadyPayWithholdsTheSameEveryPeriod(decimal perPeriod)
    {
        YearTotals year = RunYear(perPeriod);

        var ytd = new PayrollYearToDate();
        PayrollDeductions single = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = perPeriod, Province = "AB", PayPeriodsPerYear = Periods },
            ytd,
            Rates);

        Assert.True(year.Cpp < Rates.Cpp.MaxContributionEmployee, "this case must stay under the ceiling");
        Assert.Equal((single.FederalTax + single.ProvincialTax) * Periods, year.Tax);
    }

    /// <summary>
    /// Above the ceilings the periodic tax may change, but only once, around the ceiling. It must
    /// be steady before, steady after, and never drift period by period.
    ///
    /// Drift is what a carry-forward mistake looks like: the bonus bug moved the tax a little
    /// further every period rather than settling.
    /// </summary>
    [Theory]
    [InlineData(4000)]
    [InlineData(8000)]
    public void AboveTheCeilings_TheTaxSettlesRatherThanDrifting(decimal perPeriod)
    {
        List<decimal> perPeriodTax = RunYearPerPeriod(perPeriod);

        // The opening run and the closing run are each their own steady state.
        Assert.Equal(perPeriodTax[0], perPeriodTax[1]);
        Assert.Equal(perPeriodTax[0], perPeriodTax[5]);
        Assert.Equal(perPeriodTax[^1], perPeriodTax[^2]);
        Assert.Equal(perPeriodTax[^1], perPeriodTax[^3]);

        // A handful of distinct figures across the year, not twenty-six of them.
        Assert.True(perPeriodTax.Distinct().Count() <= 6,
            "the periodic tax took " + perPeriodTax.Distinct().Count()
            + " different values across the year, which is drift rather than a ceiling being reached");
    }

    /// <summary>
    /// A bonus must cost its own tax and nothing more. Anything the bonus leaks into the periodic
    /// base shows up here as the difference exceeding the bonus itself.
    /// </summary>
    [Theory]
    [InlineData(2400, 5000)]
    [InlineData(2400, 500)]
    [InlineData(4000, 20000)]
    public void ABonus_CostsOnlyItsOwnTax(decimal perPeriod, decimal bonus)
    {
        YearTotals without = RunYear(perPeriod);
        YearTotals with = RunYear(perPeriod, bonusFor: p => p == 0 ? bonus : 0m);

        Assert.True(with.Tax > without.Tax, "a bonus must be taxed at all");

        // The yardstick is the same money paid evenly across the year, which is the same annual
        // income and so must attract close to the same withholding. Only rounding and the timing
        // of the ceilings separate them.
        //
        // Deliberately not a loose cap like "less than half the bonus": the bug this covers added
        // about a quarter of the bonus again in tax, which a cap that generous would have waved
        // through.
        YearTotals evenly = RunYear(perPeriod + (bonus / Periods));

        decimal difference = Math.Abs(with.Tax - evenly.Tax);

        Assert.True(difference < Math.Max(50m, bonus * 0.06m),
            $"a {bonus:N0} bonus paid at once withheld {with.Tax:N2} across the year against "
            + $"{evenly.Tax:N2} for the same income paid evenly, a difference of {difference:N2}");
    }

    /// <summary>Where the bonus falls in the year must not change what it costs.</summary>
    [Fact]
    public void ABonus_CostsTheSameWheneverItIsPaid()
    {
        YearTotals early = RunYear(2400m, bonusFor: p => p == 0 ? 5000m : 0m);
        YearTotals late = RunYear(2400m, bonusFor: p => p == Periods - 1 ? 5000m : 0m);

        Assert.True(Math.Abs(early.Tax - late.Tax) < 5m,
            $"a bonus in period 1 cost {early.Tax:N2} for the year and the same bonus in period 26 "
            + $"cost {late.Tax:N2}");
    }

    /// <summary>
    /// The ceilings stop exactly at the maximum, reached by accumulating rather than by being
    /// handed a year-to-date that is already there.
    /// </summary>
    [Fact]
    public void AHighEarner_ReachesEveryCeilingExactly()
    {
        // Well above every 2026 maximum, so all three ceilings are hit part way through.
        YearTotals year = RunYear(8000m);

        Assert.Equal(Rates.Cpp.MaxContributionEmployee, year.Cpp);
        Assert.Equal(Rates.Ei.MaxPremiumEmployee, year.Ei);
        Assert.Equal(Rates.Cpp2.MaxContributionEmployee, year.Cpp2);
    }

    /// <summary>A modest salary must not reach them, or the ceiling logic is firing early.</summary>
    [Fact]
    public void AModestSalary_ReachesNoCeiling()
    {
        YearTotals year = RunYear(1200m);

        Assert.True(year.Cpp < Rates.Cpp.MaxContributionEmployee);
        Assert.True(year.Ei < Rates.Ei.MaxPremiumEmployee);
        Assert.Equal(0m, year.Cpp2);
    }

    /// <summary>
    /// A mid-year raise lands between the two steady years it sits between. Outside that range
    /// means something is carrying forward that should not.
    /// </summary>
    [Fact]
    public void AMidYearRaise_LandsBetweenTheTwoSalaries()
    {
        YearTotals lower = RunYear(2000m);
        YearTotals higher = RunYear(3000m);
        YearTotals raised = RunYear(0m, regularFor: p => p < 13 ? 2000m : 3000m);

        Assert.True(raised.Tax > lower.Tax && raised.Tax < higher.Tax,
            $"a year that rose from 2,000 to 3,000 withheld {raised.Tax:N2}, outside the "
            + $"{lower.Tax:N2} to {higher.Tax:N2} the two steady years withhold");
    }

    /// <summary>Net pay has to reconcile over the year, not just within a period.</summary>
    [Fact]
    public void TheYearsNetPay_IsGrossLessEveryDeduction()
    {
        YearTotals year = RunYear(2400m, bonusFor: p => p == 5 ? 3000m : 0m);

        Assert.Equal(
            year.Gross - year.Cpp - year.Cpp2 - year.Ei - year.Federal - year.Provincial,
            year.Net);
    }

    /// <summary>
    /// Every province runs a full year without the totals going somewhere impossible. Cheap
    /// breadth: a carry-forward mistake in one province's tax reduction or surtax would show up
    /// as tax above gross or below zero.
    /// </summary>
    [Theory]
    [InlineData("AB")]
    [InlineData("BC")]
    [InlineData("ON")]
    [InlineData("MB")]
    [InlineData("NS")]
    [InlineData("SK")]
    public void EveryProvince_WithholdsSomethingSane(string province)
    {
        YearTotals year = RunYear(2400m, province, bonusFor: p => p == 0 ? 4000m : 0m);

        Assert.True(year.Tax > 0, "a year on this salary owes some tax");
        Assert.True(year.Tax < year.Gross, "tax cannot exceed the pay it is taken from");
        Assert.True(year.Net > 0);
        Assert.Equal((2400m * Periods) + 4000m, year.Gross);
    }
}
