using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the payroll deduction engine.
///
/// The federal formula test is the important one: it reproduces the constants from CRA's own
/// published worked example. The rest cover the annual maximums, which are where a payroll
/// calculator silently goes wrong: over-deducting past a ceiling produces money owed back to
/// an employee and a T4 that does not reconcile.
///
/// These do not prove the engine agrees with CRA on a whole pay stub. That needs fixtures
/// captured from CRA's Payroll Deductions Online Calculator, which is the oracle for this
/// kind of software.
/// </summary>
public class PayrollCalculatorTests
{
    private static PayrollRateTable Rates() => new PayrollRateService().GetForDate(new DateTime(2026, 8, 15))!;

    private static PayrollInput Input(decimal gross, int periods = 26) => new()
    {
        GrossPay = gross,
        Province = "AB",
        PayPeriodsPerYear = periods,
    };

    [Fact]
    public void RateTable_IsAvailableForAPayDateInTheSecondHalfOf2026()
    {
        Assert.NotNull(Rates());
    }

    #region Federal formula, against CRA's published constants

    [Fact]
    public void FederalCredits_MatchCraWorkedExample()
    {
        PayrollRateTable rates = Rates();
        decimal lowest = rates.Federal.LowestRateForCredits;

        // CRA's example states K1 = 2303.28 and K4 = 210.14. Both fall out of the table.
        Assert.Equal(2303.28m, Math.Round(lowest * rates.Federal.BasicPersonalAmount.Maximum, 2));
        Assert.Equal(210.14m, Math.Round(lowest * rates.Federal.CanadaEmploymentAmount, 2));
    }

    [Fact]
    public void FederalTax_ReproducesCraWorkedExampleForTheYear()
    {
        PayrollRateTable rates = Rates();
        decimal lowest = rates.Federal.LowestRateForCredits;

        // From CRA's bonus example: A = 55,450.76 with K2 = 491.63 gives T3 = 4,758.06.
        const decimal a = 55450.76m;
        const decimal k2 = 491.63m;

        decimal k1 = lowest * rates.Federal.BasicPersonalAmount.Maximum;
        decimal k4 = lowest * rates.Federal.CanadaEmploymentAmount;
        decimal t3 = rates.Federal.Brackets[0].Rate * a - rates.Federal.Brackets[0].ConstantK - k1 - k2 - k4;

        Assert.Equal(4758.06m, Math.Round(t3, 2));
    }

    #endregion

    #region Bracket continuity

    [Theory]
    [InlineData(58523)]
    [InlineData(117045)]
    [InlineData(181440)]
    [InlineData(258482)]
    public void FederalBrackets_AreContinuousAtEveryBoundary(decimal boundary)
    {
        List<TaxBracket> brackets = Rates().Federal.Brackets;
        int i = brackets.FindIndex(b => b.UpTo == boundary);
        Assert.True(i >= 0 && i + 1 < brackets.Count);

        decimal below = brackets[i].Rate * boundary - brackets[i].ConstantK;
        decimal above = brackets[i + 1].Rate * boundary - brackets[i + 1].ConstantK;

        // Constants are published rounded to whole dollars, so a boundary can differ by cents.
        Assert.True(Math.Abs(below - above) < 1m, $"discontinuity of {below - above:F2} at {boundary}");
    }

    #endregion

    #region Annual maximums

    [Fact]
    public void Cpp_StopsAtTheAnnualMaximum()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate { CppEmployee = rates.Cpp.MaxContributionEmployee };

        PayrollDeductions result = PayrollCalculator.Calculate(Input(3000m), ytd, rates);

        Assert.Equal(0m, result.CppEmployee);
    }

    [Fact]
    public void Cpp_IsPartiallyDeductedInThePeriodThatReachesTheMaximum()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate { CppEmployee = rates.Cpp.MaxContributionEmployee - 20m };

        PayrollDeductions result = PayrollCalculator.Calculate(Input(3000m), ytd, rates);

        Assert.Equal(20m, result.CppEmployee);
    }

    [Fact]
    public void Ei_StopsAtTheAnnualMaximum()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate
        {
            EiEmployee = rates.Ei.MaxPremiumEmployee,
            InsurableEarnings = rates.Ei.MaxInsurableEarnings,
        };

        PayrollDeductions result = PayrollCalculator.Calculate(Input(3000m), ytd, rates);

        Assert.Equal(0m, result.EiEmployee);
    }

    [Fact]
    public void Cpp2_DoesNotApplyBelowTheFirstCeiling()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate { PensionableEarnings = 10000m };

        PayrollDeductions result = PayrollCalculator.Calculate(Input(3000m), ytd, rates);

        Assert.Equal(0m, result.Cpp2Employee);
    }

    [Fact]
    public void Cpp2_AppliesOnEarningsAboveTheFirstCeiling()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate { PensionableEarnings = rates.Cpp.YmpeCeiling };

        PayrollDeductions result = PayrollCalculator.Calculate(Input(1000m), ytd, rates);

        Assert.Equal(Math.Round(1000m * rates.Cpp2.RateEmployee, 2), result.Cpp2Employee);
    }

    [Fact]
    public void Cpp2_StopsAtTheSecondCeiling()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate { PensionableEarnings = rates.Cpp2.YampeCeiling };

        PayrollDeductions result = PayrollCalculator.Calculate(Input(3000m), ytd, rates);

        Assert.Equal(0m, result.Cpp2Employee);
    }

    #endregion

    #region Exemptions and edges

    [Fact]
    public void ExemptEmployee_HasNoCppOrEi()
    {
        PayrollInput input = Input(3000m);
        input.IsCppExempt = true;
        input.IsEiExempt = true;

        PayrollDeductions result = PayrollCalculator.Calculate(input, new PayrollYearToDate(), Rates());

        Assert.Equal(0m, result.CppEmployee);
        Assert.Equal(0m, result.EiEmployee);
    }

    [Fact]
    public void ZeroGross_ProducesNoDeductions()
    {
        PayrollDeductions result = PayrollCalculator.Calculate(Input(0m), new PayrollYearToDate(), Rates());

        Assert.Equal(0m, result.CppEmployee);
        Assert.Equal(0m, result.EiEmployee);
        Assert.Equal(0m, result.NetPay);
    }

    [Fact]
    public void LowIncome_ProducesNoTaxRatherThanNegativeTax()
    {
        // Well under the basic personal amount, so the credits exceed the tax.
        PayrollDeductions result = PayrollCalculator.Calculate(Input(200m), new PayrollYearToDate(), Rates());

        Assert.Equal(0m, result.FederalTax);
        Assert.Equal(0m, result.ProvincialTax);
    }

    [Fact]
    public void AnUnknownProvinceCodeThrowsRatherThanGuessing()
    {
        // Falling back to some other province's table would produce figures that look
        // plausible and are wrong, which is worse than refusing.
        //
        // This test has now named two jurisdictions that stopped being unsupported: Ontario,
        // then Quebec, each of which I claimed at the time would not go stale. A code that is
        // not a jurisdiction at all is the only example that actually cannot.
        PayrollInput input = Input(2000m);
        input.Province = "ZZ";

        Assert.Throws<NotSupportedException>(
            () => PayrollCalculator.Calculate(input, new PayrollYearToDate(), Rates()));
    }

    [Fact]
    public void EmployerEiIsHigherThanTheEmployeePremium()
    {
        PayrollRateTable rates = Rates();

        PayrollDeductions result = PayrollCalculator.Calculate(Input(2000m), new PayrollYearToDate(), rates);

        Assert.Equal(Math.Round(result.EiEmployee * rates.Ei.EmployerMultiplier, 2), result.EiEmployer);
    }

    [Fact]
    public void NetPayIsGrossLessEveryEmployeeDeduction()
    {
        PayrollDeductions r = PayrollCalculator.Calculate(Input(2500m), new PayrollYearToDate(), Rates());

        decimal expected = r.GrossPay - r.CppEmployee - r.Cpp2Employee - r.EiEmployee - r.FederalTax - r.ProvincialTax;
        Assert.Equal(expected, r.NetPay);
    }

    [Fact]
    public void TotalCostIsGrossPlusEmployerContributions()
    {
        PayrollDeductions r = PayrollCalculator.Calculate(Input(2500m), new PayrollYearToDate(), Rates());

        Assert.Equal(r.GrossPay + r.CppEmployer + r.Cpp2Employer + r.EiEmployer, r.TotalCost);
    }

    #endregion

    #region PDOC fixtures, captured from CRA's own calculator

    /// <summary>
    /// Every figure CRA's Payroll Deductions Online Calculator produced for these inputs, in
    /// Alberta on the 2026-07 edition, with the basic claim amounts (16,452 federal, 22,769
    /// provincial) unless a case says otherwise.
    ///
    /// These are the only tests here that prove agreement with CRA rather than internal
    /// consistency.
    ///
    /// The at-cap rows were paid 2026-08-14 and the rest 2026-10-09. Both dates select the
    /// same edition, so the rates are identical and the two sets are directly comparable.
    ///
    /// gross, periods, ytd(pensionable, cpp, cpp2, insurable, ei), then expected
    /// federal, provincial, cpp, cpp2, ei, net.
    /// </summary>
    public static IEnumerable<object[]> PdocCases =>
    [
        ["biweekly 2000",        2000m, 26,     0m,      0m,   0m,     0m,      0m,  163.23m,  78.45m, 110.99m,   0m, 32.60m, 1614.73m],
        ["biweekly 3500",        3500m, 26,     0m,      0m,   0m,     0m,      0m,  442.63m, 215.21m, 200.24m,   0m, 57.05m, 2584.87m],
        ["weekly 1000",          1000m, 52,     0m,      0m,   0m,     0m,      0m,   81.61m,  39.23m,  55.50m,   0m, 16.30m,  807.36m],
        ["monthly 5000",         5000m, 12,     0m,      0m,   0m,     0m,      0m,  444.86m, 219.28m, 280.15m,   0m, 81.50m, 3974.21m],
        ["semi-monthly 2500",    2500m, 24,     0m,      0m,   0m,     0m,      0m,  222.43m, 109.64m, 140.07m,   0m, 40.75m, 1987.11m],
        ["biweekly 4000 at cap", 4000m, 26, 74000m,   4200m,   0m, 67500m,   1100m,  523.09m, 254.47m,  30.45m, 136m, 23.07m, 3032.92m],
        ["biweekly 500 at cap",   500m, 26, 74000m,   4200m,   0m, 67500m,   1100m,    0.00m,   0.00m,  21.74m,   0m,  8.15m,  470.11m],
        ["biweekly 8000 at cap", 8000m, 26, 74000m,   4200m,   0m, 67500m,   1100m, 1507.74m, 679.59m,  30.45m, 296m, 23.07m, 5463.15m],

        // Constant pay, four points across the year. CRA returns identical tax at every one.
        // This is the set that settled how K2 annualises: anything that makes the credit
        // depend on what has already been deducted this year breaks every row but the first,
        // and under-withholds for the rest of the year without disturbing any other case.
        ["2400 period 1",        2400m, 26,     0m,      0m,   0m,     0m,      0m,  223.20m, 108.50m, 134.79m,   0m, 39.12m, 1894.39m],
        ["2400 period 7",        2400m, 26, 14400m, 808.74m,   0m, 14400m, 234.72m,  223.20m, 108.50m, 134.79m,   0m, 39.12m, 1894.39m],
        ["2400 period 13",       2400m, 26, 28800m, 1617.48m,  0m, 28800m, 469.44m,  223.20m, 108.50m, 134.79m,   0m, 39.12m, 1894.39m],
        ["2400 period 20",       2400m, 26, 45600m, 2561.01m,  0m, 45600m, 743.28m,  223.20m, 108.50m, 134.79m,   0m, 39.12m, 1894.39m],

        // CPP2. The first row is the one that matters: year-to-date pensionable earnings sit
        // below the first ceiling and this single cheque carries them over it, so only the
        // 1,900 above 74,600 attracts CPP2. That partial crossing is the fiddliest arithmetic
        // in the engine.
        ["crossing into cpp2",   3500m, 26, 73000m,   4100m,   0m, 68900m, 1123.07m,  429.45m, 208.79m, 130.45m,  76m,  0.00m, 2655.31m],
        ["inside cpp2 band",     3500m, 26, 78000m, 4230.45m, 136m, 68900m, 1123.07m,  420.82m, 204.58m,   0.00m, 140m,  0.00m, 2734.60m],

        // One ceiling reached and not the other. EI stops at 68,900 of insurable earnings
        // while CPP runs to 74,600, so there is a real stretch of a high earner's year that
        // looks like the first of these. Every other fixture has the two moving together.
        ["ei done, cpp running", 3000m, 26, 70000m,   3957m,   0m, 68900m, 1123.07m,  341.15m, 165.71m, 170.49m,   0m,  0.00m, 2322.65m],
        ["cpp done, ei running", 3000m, 26, 74600m, 4230.45m,  0m, 40000m,    652m,  322.42m, 156.58m,   0.00m, 120m, 48.90m, 2352.10m],

        // Third federal bracket, with CPP and EI both still running. The rest cluster in the
        // lowest two.
        ["biweekly 6000",        6000m, 26,     0m,      0m,   0m,     0m,      0m, 1029.20m, 462.89m, 348.99m,   0m, 97.80m, 4061.12m],
    ];

    [Theory]
    [MemberData(nameof(PdocCases))]
    public void Deductions_MatchCraOnlineCalculator(
        string label, decimal gross, int periods,
        decimal ytdPensionable, decimal ytdCpp, decimal ytdCpp2, decimal ytdInsurable, decimal ytdEi,
        decimal federal, decimal provincial, decimal cpp, decimal cpp2, decimal ei, decimal net)
    {
        PayrollDeductions r = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = gross, Province = "AB", PayPeriodsPerYear = periods },
            new PayrollYearToDate
            {
                PensionableEarnings = ytdPensionable,
                CppEmployee = ytdCpp,
                Cpp2Employee = ytdCpp2,
                InsurableEarnings = ytdInsurable,
                EiEmployee = ytdEi,
            },
            Rates());

        // Compared as one tuple so a failure names the case and shows every figure at once.
        // Six separate asserts would stop at the first difference, which hides whether one
        // number drifted or the whole row did.
        Assert.Equal(
            (label, federal, provincial, cpp, cpp2, ei, net),
            (label, r.FederalTax, r.ProvincialTax, r.CppEmployee, r.Cpp2Employee, r.EiEmployee, r.NetPay));
    }

    /// <summary>
    /// The same oracle for the cases that need something other than the basic claim and two
    /// contributing sides. The invariant tests already pin the DIRECTION of all three of these
    /// (a larger claim can never raise tax, an exempt employee always pays more than a
    /// contributing one). These pin the figure.
    ///
    /// gross, federal claim, provincial claim, cpp exempt, ei exempt, then expected
    /// federal, provincial, cpp, ei, net. All biweekly, no year-to-date.
    /// </summary>
    public static IEnumerable<object[]> PdocOptionCases =>
    [
        ["td1 above basic", 2400m, 25000m, 30000m, false, false, 177.18m,  86.25m, 134.79m, 39.12m, 1962.66m],
        ["cpp exempt",      2400m,      0m,     0m,  true, false, 243.55m, 119.74m,   0.00m, 39.12m, 1997.59m],
        ["ei exempt",       2400m,      0m,     0m, false,  true, 228.68m, 111.63m, 134.79m,  0.00m, 1924.90m],
    ];

    /// <summary>
    /// One PDOC capture per province and territory, all on the same employee so the only thing
    /// that varies is the jurisdiction: $2,400 biweekly, paid 2026-10-09, no year-to-date, the
    /// basic claim amount for that province.
    ///
    /// The federal figure is 223.20 in every row, which is the point: it isolates the
    /// provincial calculation. A wrong bracket, constant or basic amount for one province shows
    /// up here and nowhere else.
    ///
    /// province, expected provincial tax, expected net.
    /// </summary>
    public static IEnumerable<object[]> PdocProvinceCases =>
    [
        ["AB", 108.50m, 1894.39m],
        ["BC", 112.33m, 1890.56m],
        ["MB", 185.96m, 1816.93m],
        ["NB", 176.63m, 1826.26m],
        ["NL", 181.71m, 1821.18m],
        ["NS", 228.36m, 1774.53m],
        ["NT",  99.19m, 1903.70m],
        ["NU",  65.74m, 1937.15m],
        ["ON", 122.74m, 1880.15m],
        ["PE", 199.24m, 1803.65m],
        ["SK", 157.02m, 1845.87m],
        ["YT", 101.55m, 1901.34m],
    ];

    [Theory]
    [MemberData(nameof(PdocProvinceCases))]
    public void EveryProvince_MatchesCraOnlineCalculator(string province, decimal provincial, decimal net)
    {
        PayrollDeductions r = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 2400m, Province = province, PayPeriodsPerYear = 26 },
            new PayrollYearToDate(),
            Rates());

        Assert.Equal((province, 223.20m, provincial, net), (province, r.FederalTax, r.ProvincialTax, r.NetPay));
    }

    /// <summary>
    /// The three provincial rules that only exist in one or two places, each at an income that
    /// actually triggers it. Every one of these exercises a code path Alberta can never reach.
    /// </summary>
    [Fact]
    public void OntarioSurtaxAndHealthPremium_MatchCraOnlineCalculator()
    {
        // $6,000 biweekly annualises to about $154,000, which clears both surtax thresholds and
        // lands in the second-highest health premium band. CRA returns 602.06.
        //
        // This is the case that caught the surtax being compounded: applying the 36% band to a
        // figure that already included the 20% band overstated Ontario tax by about $17 a period.
        PayrollDeductions r = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 6000m, Province = "ON", PayPeriodsPerYear = 26 },
            new PayrollYearToDate(),
            Rates());

        Assert.Equal(1029.20m, r.FederalTax);
        Assert.Equal(602.06m, r.ProvincialTax);
        Assert.Equal(3921.95m, r.NetPay);
    }

    [Fact]
    public void BritishColumbiaTaxReduction_MatchesCraOnlineCalculatorInsideThePhaseOut()
    {
        // $1,400 biweekly annualises to about $36,071, inside BC's 25,570 to 44,952 taper, so
        // the credit is partial: 805 - (36,071.10 - 25,570) x 3.56% = 431.16. At $2,400 the
        // employee is past the taper entirely and the credit is nil, so only an income in the
        // band tests this at all.
        PayrollDeductions r = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 1400m, Province = "BC", PayPeriodsPerYear = 26 },
            new PayrollYearToDate(),
            Rates());

        Assert.Equal(85.60m, r.FederalTax);
        Assert.Equal(32.14m, r.ProvincialTax);
        Assert.Equal(1184.15m, r.NetPay);
    }

    [Fact]
    public void YukonCanadaEmploymentAmount_MatchesCraOnlineCalculator()
    {
        // Yukon is the only jurisdiction granting a provincial Canada Employment Amount. Without
        // it the territorial tax comes out at 105.25 instead of 101.55, over-deducting $3.70 a
        // period for every Yukon employee.
        PayrollDeductions r = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 2400m, Province = "YT", PayPeriodsPerYear = 26 },
            new PayrollYearToDate(),
            Rates());

        Assert.Equal(101.55m, r.ProvincialTax);
    }

    [Theory]
    [MemberData(nameof(PdocOptionCases))]
    public void DeductionsWithClaimsAndExemptions_MatchCraOnlineCalculator(
        string label, decimal gross, decimal federalClaim, decimal provincialClaim,
        bool cppExempt, bool eiExempt,
        decimal federal, decimal provincial, decimal cpp, decimal ei, decimal net)
    {
        PayrollDeductions r = PayrollCalculator.Calculate(
            new PayrollInput
            {
                GrossPay = gross,
                Province = "AB",
                PayPeriodsPerYear = 26,
                FederalClaimAmount = federalClaim,
                ProvincialClaimAmount = provincialClaim,
                IsCppExempt = cppExempt,
                IsEiExempt = eiExempt,
            },
            new PayrollYearToDate(),
            Rates());

        Assert.Equal(
            (label, federal, provincial, cpp, ei, net),
            (label, r.FederalTax, r.ProvincialTax, r.CppEmployee, r.EiEmployee, r.NetPay));
    }

    #endregion
}
