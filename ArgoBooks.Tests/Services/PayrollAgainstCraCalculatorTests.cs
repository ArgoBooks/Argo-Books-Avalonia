using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The engine against the tax authorities' own calculators.
///
/// Every other payroll test here proves the engine agrees with our reading of T4127. These prove
/// it agrees with CRA, which is a different claim and the one that matters: a misread formula
/// produces a plausible number that reconciles with itself all the way to a T4 and is only
/// caught, months later, by a PIER review against the employee's return.
///
/// The expected values are captured by hand from:
///
///   CRA, Payroll Deductions Online Calculator (PDOC)
///   https://www.canada.ca/en/revenue-agency/services/e-services/digital-services-businesses/payroll-deductions-online-calculator.html
///
///   Revenu Quebec, WebRAS, for the Quebec case
///   https://www.revenuquebec.ca/en/online-services/forms-and-publications/all-current/webras/
///
/// HOW TO FILL THESE IN
///
/// Each case below names exactly what to enter. Run it through the calculator once, then paste
/// the four figures into the Expected(...) call and delete the Skip on that row. A row still
/// carrying nulls is reported as not yet captured rather than passing quietly.
///
/// Use the payment date each case names, because the deduction tables change on 1 January and
/// 1 July and the calculator asks for the date rather than the year.
///
/// WHEN THEY DISAGREE
///
/// Assume the engine is wrong before assuming the calculator is. Tolerance is one cent, which is
/// rounding; anything larger is a real difference and worth understanding before it is adjusted
/// away. Do not edit an expected value to make a test pass.
///
/// Recapture these when the January rate edition lands. They are dated, so a stale row is
/// visible rather than silently wrong. See docs/Payroll rate updates.md.
/// </summary>
public class PayrollAgainstCraCalculatorTests
{
    /// <summary>One cent, which is rounding. Anything larger is a disagreement.</summary>
    private const decimal Tolerance = 0.01m;

    /// <summary>
    /// What the authority's calculator said. Null means nobody has captured this row yet, which
    /// the test reports rather than skipping silently.
    /// </summary>
    public sealed record Expected(decimal? Cpp, decimal? Ei, decimal? FederalTax, decimal? ProvincialTax)
    {
        public bool IsCaptured => Cpp != null && Ei != null && FederalTax != null && ProvincialTax != null;
    }

    /// <summary>One row: what to type into the calculator, and what it answered.</summary>
    public sealed record Case(
        string Name,
        string Province,
        decimal Gross,
        int PeriodsPerYear,
        DateTime PayDate,
        Expected Expected,
        decimal Bonus = 0m,
        decimal YtdPensionable = 0m,
        decimal YtdInsurable = 0m,
        decimal YtdCpp = 0m,
        decimal YtdCpp2 = 0m,
        decimal YtdEi = 0m);

    /// <summary>
    /// Twelve rows, chosen so each one can fail for a different reason: the provincial tables,
    /// both CPP ceilings, the EI maximum, the bonus method, and Quebec's separate system.
    /// </summary>
    /// <summary>
    /// Five rows, each able to fail for a different reason: the baseline, the annualisation
    /// factor, both CPP ceilings at once, the bonus method, and Quebec's separate system.
    ///
    /// Provinces beyond Ontario and Quebec are worth adding when there is an hour to spare.
    /// Add the row, run it through the calculator, paste the figures in. A row with no figures
    /// fails rather than skips, so an unfinished one is visible.
    /// </summary>
    public static readonly Case[] Cases =
    [
        new("Ontario, biweekly, 2000", "ON", 2000m, 26, new DateTime(2026, 8, 14), new Expected(110.99m, 32.60m, 163.23m, 91.60m)),

        new("Ontario, semi-monthly, 2500", "ON", 2500m, 24, new DateTime(2026, 8, 15), new Expected(140.07m, 40.75m, 222.43m, 124.24m)),

        new("Ontario, past the CPP ceiling into CPP2", "ON", 3000m, 26, new DateTime(2026, 12, 11), new Expected(120.00m, 0.00m, 322.42m, 173.12m),
            YtdPensionable: 78000m, YtdInsurable: 68900m, YtdCpp: 4230.45m, YtdCpp2: 136m, YtdEi: 1123.07m),

        new("Ontario, biweekly 2000 plus a 5000 bonus", "ON", 7000m, 26, new DateTime(2026, 8, 14),
            new Expected(408.49m, 114.10m, 810.16m, 429.04m),
            Bonus: 5000m, YtdPensionable: 40000m, YtdInsurable: 40000m, YtdCpp: 2171.75m, YtdEi: 652m),

        new("Quebec, biweekly, 2000", "QC", 2000m, 26, new DateTime(2026, 8, 14), new Expected(117.52m, 26.00m, 135.30m, 167.53m)),
    ];

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (Case c in Cases)
        {
            data.Add(c.Name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void MatchesTheAuthoritysCalculator(string name)
    {
        Case c = Cases.Single(x => x.Name == name);

        if (!c.Expected.IsCaptured)
        {
            // Not a pass. Reported so an uncaptured row is visible in the run rather than
            // looking like coverage that exists.
            Assert.Fail($"'{name}' has no captured figures yet. Run it through the calculator "
                        + "and fill in Expected(...). See the class comment for what to enter.");
        }

        PayrollRateTable rates = new PayrollRateService().GetForDate(c.PayDate)
                                ?? throw new InvalidOperationException($"No rate table covers {c.PayDate:yyyy-MM-dd}.");

        var input = new PayrollInput
        {
            GrossPay = c.Gross,
            Province = c.Province,
            PayPeriodsPerYear = c.PeriodsPerYear,
            NonPeriodicPay = c.Bonus,
        };

        var ytd = new PayrollYearToDate
        {
            PensionableEarnings = c.YtdPensionable,
            InsurableEarnings = c.YtdInsurable,
            CppEmployee = c.YtdCpp,
            Cpp2Employee = c.YtdCpp2,
            EiEmployee = c.YtdEi,
        };

        PayrollDeductions actual = PayrollCalculator.Calculate(input, ytd, rates);

        // CPP and CPP2 are one figure in the calculator's output, so they are compared as one.
        Close("CPP", c.Expected.Cpp!.Value, actual.CppEmployee + actual.Cpp2Employee);
        Close("EI", c.Expected.Ei!.Value, actual.EiEmployee);
        Close("Federal tax", c.Expected.FederalTax!.Value, actual.FederalTax);
        Close("Provincial tax", c.Expected.ProvincialTax!.Value, actual.ProvincialTax);
    }

    private static void Close(string label, decimal expected, decimal actual)
    {
        decimal difference = Math.Abs(expected - actual);

        Assert.True(difference <= Tolerance,
            $"{label}: the calculator says {expected:0.00}, the engine says {actual:0.00}, "
            + $"a difference of {difference:0.00}. Assume the engine is wrong before changing this number.");
    }
}
