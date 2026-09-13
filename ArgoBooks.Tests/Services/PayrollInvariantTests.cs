using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Properties that must hold for EVERY input, not just the cases anyone thought to capture.
///
/// The PDOC fixtures prove the engine agrees with CRA on the handful of pay stubs that were
/// gathered by hand. These are the other half of the argument: they sweep whole ranges of
/// income and whole years of pay periods, and assert things that cannot be false if the
/// engine is right. A fixture catches a wrong constant. These catch a wrong shape.
///
/// The full-year simulations matter most. Annual maximums are the one place where a payroll
/// calculator goes wrong expensively and silently: over-deduct past a ceiling and the employer
/// owes the employee money back and the T4 does not reconcile. No single-period fixture can
/// reach that, because it takes a whole year of accumulated year-to-date figures to get there.
/// </summary>
public class PayrollInvariantTests
{
    private static PayrollRateTable Rates() => new PayrollRateService().GetForDate(new DateTime(2026, 8, 15))!;

    private static PayrollInput Input(decimal gross, int periods = 26) => new()
    {
        GrossPay = gross,
        Province = "AB",
        PayPeriodsPerYear = periods,
    };

    /// <summary>
    /// A spread of gross amounts covering every regime the engine has: below the CPP basic
    /// exemption, ordinary pay, above the EI maximum insurable earnings, between the two CPP
    /// ceilings, and past the top federal bracket.
    /// </summary>
    public static TheoryData<decimal> GrossSweep =>
    [
        0m, 0.01m, 50m, 134.61m, 134.62m, 250m, 500m, 1000m, 1500m, 2000m,
        2500m, 2650m, 2870m, 3000m, 4000m, 5000m, 7500m, 10000m, 20000m, 50000m,
    ];

    public static TheoryData<int> Frequencies => [52, 26, 24, 12];

    #region Per-period identities

    [Theory]
    [MemberData(nameof(GrossSweep))]
    public void NetPay_IsExactlyGrossMinusEveryEmployeeDeduction(decimal gross)
    {
        // Not approximately. If this drifts by a cent, an employee is paid the wrong amount.
        PayrollDeductions d = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), Rates());

        decimal expected = d.GrossPay - d.CppEmployee - d.Cpp2Employee - d.EiEmployee
                           - d.FederalTax - d.ProvincialTax;

        Assert.Equal(expected, d.NetPay);
    }

    [Theory]
    [MemberData(nameof(GrossSweep))]
    public void EmployerCpp_MatchesTheEmployeeContributionExactly(decimal gross)
    {
        // The employer matches CPP and CPP2 dollar for dollar. Any difference is a bug in the
        // remittance total, which is money sent to CRA.
        PayrollDeductions d = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), Rates());

        Assert.Equal(d.CppEmployee, d.CppEmployer);
        Assert.Equal(d.Cpp2Employee, d.Cpp2Employer);
    }

    [Theory]
    [MemberData(nameof(GrossSweep))]
    public void EmployerEi_IsTheEmployeePremiumTimesTheStatutoryMultiplier(decimal gross)
    {
        PayrollRateTable rates = Rates();
        PayrollDeductions d = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), rates);

        decimal expected = Math.Round(d.EiEmployee * rates.Ei.EmployerMultiplier, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(expected, d.EiEmployer);
    }

    [Theory]
    [MemberData(nameof(GrossSweep))]
    public void EveryDeduction_IsNeverNegative(decimal gross)
    {
        PayrollDeductions d = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), Rates());

        Assert.True(d.CppEmployee >= 0);
        Assert.True(d.Cpp2Employee >= 0);
        Assert.True(d.EiEmployee >= 0);
        Assert.True(d.FederalTax >= 0);
        Assert.True(d.ProvincialTax >= 0);
    }

    [Theory]
    [MemberData(nameof(GrossSweep))]
    public void EveryAmount_LandsOnAWholeCent(decimal gross)
    {
        // Fractional cents stored on a pay run would make the run's totals disagree with the
        // sum of its stubs, and the difference would compound across the year.
        PayrollDeductions d = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), Rates());

        foreach (decimal value in new[]
                 {
                     d.CppEmployee, d.CppEmployer, d.Cpp2Employee, d.Cpp2Employer,
                     d.EiEmployee, d.EiEmployer, d.FederalTax, d.ProvincialTax,
                 })
        {
            Assert.Equal(value, Math.Round(value, 2));
        }
    }

    [Theory]
    [MemberData(nameof(GrossSweep))]
    public void TotalCostAndRemittance_AgreeWithTheirParts(decimal gross)
    {
        PayrollDeductions d = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), Rates());

        Assert.Equal(d.GrossPay + d.CppEmployer + d.Cpp2Employer + d.EiEmployer, d.TotalCost);
        Assert.Equal(
            d.CppEmployee + d.CppEmployer + d.Cpp2Employee + d.Cpp2Employer
            + d.EiEmployee + d.EiEmployer + d.FederalTax + d.ProvincialTax,
            d.TotalRemittance);
    }

    [Theory]
    [MemberData(nameof(GrossSweep))]
    public void NetPay_NeverExceedsGross(decimal gross)
    {
        PayrollDeductions d = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), Rates());

        Assert.True(d.NetPay <= d.GrossPay);
    }

    #endregion

    #region Monotonicity

    [Fact]
    public void NetPay_NeverFallsAsGrossRises()
    {
        // A raise must never leave someone with less in hand. This would fail if a bracket
        // constant were wrong, because the tax would step rather than bend at the boundary.
        PayrollRateTable rates = Rates();
        decimal previousNet = decimal.MinValue;

        for (decimal gross = 0m; gross <= 6000m; gross += 25m)
        {
            decimal net = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), rates).NetPay;
            Assert.True(net >= previousNet, $"Net fell from {previousNet} to {net} at gross {gross}.");
            previousNet = net;
        }
    }

    [Fact]
    public void EveryDeduction_NeverFallsAsGrossRises()
    {
        PayrollRateTable rates = Rates();
        decimal cpp = -1m, ei = -1m, federal = -1m, provincial = -1m;

        for (decimal gross = 0m; gross <= 6000m; gross += 25m)
        {
            PayrollDeductions d = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), rates);

            Assert.True(d.CppEmployee >= cpp, $"CPP fell at gross {gross}.");
            Assert.True(d.EiEmployee >= ei, $"EI fell at gross {gross}.");
            Assert.True(d.FederalTax >= federal, $"Federal tax fell at gross {gross}.");
            Assert.True(d.ProvincialTax >= provincial, $"Provincial tax fell at gross {gross}.");

            (cpp, ei, federal, provincial) = (d.CppEmployee, d.EiEmployee, d.FederalTax, d.ProvincialTax);
        }
    }

    [Fact]
    public void FederalTax_DoesNotJumpAcrossABracketBoundary()
    {
        // Tested on the engine's own output rather than the table, so a wrong annualisation
        // shows up as well as a wrong constant.
        PayrollRateTable rates = Rates();

        foreach (TaxBracket bracket in rates.Federal.Brackets.Where(b => b.UpTo != null))
        {
            decimal boundaryPerPeriod = bracket.UpTo!.Value / 26m;

            decimal below = PayrollCalculator.Calculate(Input(boundaryPerPeriod - 1m), new PayrollYearToDate(), rates).FederalTax;
            decimal above = PayrollCalculator.Calculate(Input(boundaryPerPeriod + 1m), new PayrollYearToDate(), rates).FederalTax;

            // Two dollars of extra income can never cost more than two dollars of extra tax.
            Assert.InRange(above - below, 0m, 2m);
        }
    }

    #endregion

    #region Annual ceilings

    [Fact]
    public void Cpp_StopsExactlyAtTheAnnualMaximum()
    {
        PayrollRateTable rates = Rates();
        decimal max = rates.Cpp.MaxContributionEmployee;

        var atMax = new PayrollYearToDate { CppEmployee = max };
        Assert.Equal(0m, PayrollCalculator.Calculate(Input(5000m), atMax, rates).CppEmployee);

        // A dollar short of the maximum, the next period deducts exactly that dollar.
        var nearlyMax = new PayrollYearToDate { CppEmployee = max - 1m };
        Assert.Equal(1m, PayrollCalculator.Calculate(Input(5000m), nearlyMax, rates).CppEmployee);
    }

    [Fact]
    public void Ei_StopsExactlyAtTheAnnualMaximum()
    {
        PayrollRateTable rates = Rates();
        decimal max = rates.Ei.MaxPremiumEmployee;

        var atMax = new PayrollYearToDate { EiEmployee = max };
        Assert.Equal(0m, PayrollCalculator.Calculate(Input(5000m), atMax, rates).EiEmployee);

        var nearlyMax = new PayrollYearToDate { EiEmployee = max - 0.50m };
        Assert.Equal(0.50m, PayrollCalculator.Calculate(Input(5000m), nearlyMax, rates).EiEmployee);
    }

    [Fact]
    public void Cpp2_StopsExactlyAtItsAnnualMaximum()
    {
        PayrollRateTable rates = Rates();

        var atMax = new PayrollYearToDate
        {
            PensionableEarnings = rates.Cpp.YmpeCeiling + 1000m,
            Cpp2Employee = rates.Cpp2.MaxContributionEmployee,
        };

        Assert.Equal(0m, PayrollCalculator.Calculate(Input(5000m), atMax, rates).Cpp2Employee);
    }

    [Fact]
    public void Cpp2_DoesNotStartUntilPensionableEarningsPassTheFirstCeiling()
    {
        PayrollRateTable rates = Rates();

        var below = new PayrollYearToDate { PensionableEarnings = rates.Cpp.YmpeCeiling - 5000m };
        Assert.Equal(0m, PayrollCalculator.Calculate(Input(1000m), below, rates).Cpp2Employee);

        var above = new PayrollYearToDate { PensionableEarnings = rates.Cpp.YmpeCeiling };
        Assert.True(PayrollCalculator.Calculate(Input(1000m), above, rates).Cpp2Employee > 0);
    }

    [Fact]
    public void Cpp2_StopsOnceEarningsPassTheSecondCeiling()
    {
        PayrollRateTable rates = Rates();

        var past = new PayrollYearToDate { PensionableEarnings = rates.Cpp2.YampeCeiling };
        Assert.Equal(0m, PayrollCalculator.Calculate(Input(5000m), past, rates).Cpp2Employee);
    }

    [Theory]
    [MemberData(nameof(GrossSweep))]
    public void NoDeduction_EverExceedsWhatIsLeftOfItsCeiling(decimal gross)
    {
        PayrollRateTable rates = Rates();

        var ytd = new PayrollYearToDate
        {
            CppEmployee = rates.Cpp.MaxContributionEmployee - 3m,
            Cpp2Employee = rates.Cpp2.MaxContributionEmployee - 3m,
            EiEmployee = rates.Ei.MaxPremiumEmployee - 3m,
            PensionableEarnings = rates.Cpp.YmpeCeiling + 1000m,
        };

        PayrollDeductions d = PayrollCalculator.Calculate(Input(gross), ytd, rates);

        Assert.True(d.CppEmployee <= 3m);
        Assert.True(d.Cpp2Employee <= 3m);
        Assert.True(d.EiEmployee <= 3m);
    }

    #endregion

    #region Full-year simulations

    /// <summary>
    /// Runs a whole year one period at a time, feeding each period's result forward as the
    /// next period's year-to-date, exactly as the pay run service does.
    /// </summary>
    private static (decimal Cpp, decimal Cpp2, decimal Ei, decimal Gross, decimal Net) RunYear(
        decimal grossPerPeriod, int periods, PayrollRateTable rates, bool cppExempt = false, bool eiExempt = false)
    {
        var ytd = new PayrollYearToDate();
        decimal net = 0m;

        for (int i = 0; i < periods; i++)
        {
            PayrollInput input = Input(grossPerPeriod, periods);
            input.IsCppExempt = cppExempt;
            input.IsEiExempt = eiExempt;

            PayrollDeductions d = PayrollCalculator.Calculate(input, ytd, rates);

            ytd.PensionableEarnings += d.GrossPay;
            ytd.InsurableEarnings += d.GrossPay;
            ytd.CppEmployee += d.CppEmployee;
            ytd.Cpp2Employee += d.Cpp2Employee;
            ytd.EiEmployee += d.EiEmployee;
            net += d.NetPay;
        }

        return (ytd.CppEmployee, ytd.Cpp2Employee, ytd.EiEmployee, ytd.PensionableEarnings, net);
    }

    [Theory]
    [MemberData(nameof(Frequencies))]
    public void AHighEarner_LandsExactlyOnEveryAnnualMaximum(int periods)
    {
        // 120,000 a year clears the EI maximum insurable earnings, the CPP ceiling and the
        // CPP2 ceiling, so all three must finish the year exactly on their maximum. Not near
        // it: over-deducting means money owed back, under-deducting means a T4 that fails.
        PayrollRateTable rates = Rates();
        var year = RunYear(120000m / periods, periods, rates);

        Assert.Equal(rates.Cpp.MaxContributionEmployee, year.Cpp);
        Assert.Equal(rates.Cpp2.MaxContributionEmployee, year.Cpp2);
        Assert.Equal(rates.Ei.MaxPremiumEmployee, year.Ei);
    }

    [Theory]
    [MemberData(nameof(Frequencies))]
    public void AModestEarner_NeverReachesAnyCeiling(int periods)
    {
        PayrollRateTable rates = Rates();
        var year = RunYear(31200m / periods, periods, rates);

        Assert.True(year.Cpp < rates.Cpp.MaxContributionEmployee);
        Assert.Equal(0m, year.Cpp2);
        Assert.True(year.Ei < rates.Ei.MaxPremiumEmployee);
    }

    [Theory]
    [MemberData(nameof(Frequencies))]
    public void AYearOfCpp_MatchesTheRateAppliedToPensionableEarnings(int periods)
    {
        // The year's contributions must equal (earnings - the annual basic exemption) times
        // the rate, give or take a cent of rounding per period. A basic exemption applied per
        // period rather than annually would show up here as a large gap.
        PayrollRateTable rates = Rates();
        var year = RunYear(50000m / periods, periods, rates);

        decimal expected = (year.Gross - rates.Cpp.BasicExemptionAnnual) * rates.Cpp.RateEmployee;

        Assert.InRange(year.Cpp, expected - periods * 0.01m, expected + periods * 0.01m);
    }

    [Theory]
    [MemberData(nameof(Frequencies))]
    public void AYearOfEi_MatchesTheRateAppliedToInsurableEarnings(int periods)
    {
        PayrollRateTable rates = Rates();
        var year = RunYear(50000m / periods, periods, rates);

        decimal expected = year.Gross * rates.Ei.RateEmployee;

        Assert.InRange(year.Ei, expected - periods * 0.01m, expected + periods * 0.01m);
    }

    [Fact]
    public void TheSameSalary_CostsTheSameTaxWhicheverFrequencyItIsPaidAt()
    {
        // 62,400 divides evenly into all four frequencies, so any difference here is the
        // engine's rather than an artefact of an uneven period amount.
        //
        // A dollar is the bound because that is what per-period rounding can produce across
        // fifty-two periods. Anything larger means the annualisation has become sensitive to
        // something it should not see.
        PayrollRateTable rates = Rates();
        var nets = new List<decimal>();

        foreach (int periods in new[] { 52, 26, 24, 12 })
        {
            nets.Add(RunYear(62400m / periods, periods, rates).Net);
        }

        Assert.InRange(nets.Max() - nets.Min(), 0m, 1m);
    }

    [Fact]
    public void ConstantPay_IsTaxedTheSameInEveryPeriodOfTheYear()
    {
        // The plainest statement of what the K2 fix was about, and the cheapest guard against
        // it regressing. Someone on unchanging pay must see the same deduction on every stub;
        // a credit that drifts as the year-to-date figures grow is exactly what CRA does not
        // do, and it under-withholds silently because each individual stub looks reasonable.
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();
        decimal? first = null;

        for (int period = 1; period <= 20; period++)
        {
            PayrollDeductions d = PayrollCalculator.Calculate(Input(2400m), ytd, rates);

            first ??= d.FederalTax;
            Assert.Equal(first.Value, d.FederalTax);

            ytd.PensionableEarnings += d.GrossPay;
            ytd.CppEmployee += d.CppEmployee;
            ytd.EiEmployee += d.EiEmployee;
        }
    }

    [Fact]
    public void AYearOfNetPay_ReconcilesWithGrossAndTheYearsDeductions()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();
        decimal net = 0m, tax = 0m;

        for (int i = 0; i < 26; i++)
        {
            PayrollDeductions d = PayrollCalculator.Calculate(Input(3000m), ytd, rates);

            ytd.PensionableEarnings += d.GrossPay;
            ytd.CppEmployee += d.CppEmployee;
            ytd.Cpp2Employee += d.Cpp2Employee;
            ytd.EiEmployee += d.EiEmployee;
            net += d.NetPay;
            tax += d.FederalTax + d.ProvincialTax;
        }

        decimal gross = 3000m * 26;
        Assert.Equal(gross - ytd.CppEmployee - ytd.Cpp2Employee - ytd.EiEmployee - tax, net);
    }

    #endregion

    #region Exemptions

    [Theory]
    [MemberData(nameof(GrossSweep))]
    public void CppExempt_RemovesBothCppAndCpp2ButLeavesEiAlone(decimal gross)
    {
        PayrollRateTable rates = Rates();

        PayrollInput input = Input(gross);
        input.IsCppExempt = true;

        PayrollDeductions exempt = PayrollCalculator.Calculate(input, new PayrollYearToDate(), rates);
        PayrollDeductions normal = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), rates);

        Assert.Equal(0m, exempt.CppEmployee);
        Assert.Equal(0m, exempt.CppEmployer);
        Assert.Equal(0m, exempt.Cpp2Employee);
        Assert.Equal(normal.EiEmployee, exempt.EiEmployee);
    }

    [Theory]
    [MemberData(nameof(GrossSweep))]
    public void EiExempt_RemovesBothSidesOfEiButLeavesCppAlone(decimal gross)
    {
        PayrollRateTable rates = Rates();

        PayrollInput input = Input(gross);
        input.IsEiExempt = true;

        PayrollDeductions exempt = PayrollCalculator.Calculate(input, new PayrollYearToDate(), rates);
        PayrollDeductions normal = PayrollCalculator.Calculate(Input(gross), new PayrollYearToDate(), rates);

        Assert.Equal(0m, exempt.EiEmployee);
        Assert.Equal(0m, exempt.EiEmployer);
        Assert.Equal(normal.CppEmployee, exempt.CppEmployee);
    }

    [Fact]
    public void ExemptFromBoth_LeavesOnlyIncomeTax()
    {
        PayrollInput input = Input(3000m);
        input.IsCppExempt = true;
        input.IsEiExempt = true;

        PayrollDeductions d = PayrollCalculator.Calculate(input, new PayrollYearToDate(), Rates());

        Assert.Equal(0m, d.CppEmployee);
        Assert.Equal(0m, d.EiEmployee);
        Assert.Equal(d.GrossPay - d.FederalTax - d.ProvincialTax, d.NetPay);
    }

    [Fact]
    public void ExemptEmployees_StillPayMoreTaxThanTheirContributingEquivalent()
    {
        // CPP and EI feed the K2 credit, so removing them raises tax. Getting the sign wrong
        // here would be invisible on a normal employee and wrong for every exempt owner.
        PayrollInput exempt = Input(3000m);
        exempt.IsCppExempt = true;
        exempt.IsEiExempt = true;

        PayrollRateTable rates = Rates();
        decimal exemptTax = PayrollCalculator.Calculate(exempt, new PayrollYearToDate(), rates).FederalTax;
        decimal normalTax = PayrollCalculator.Calculate(Input(3000m), new PayrollYearToDate(), rates).FederalTax;

        Assert.True(exemptTax > normalTax);
    }

    #endregion

    #region Claim amounts

    [Fact]
    public void ALargerTd1Claim_NeverIncreasesTax()
    {
        PayrollRateTable rates = Rates();
        decimal previous = decimal.MaxValue;

        foreach (decimal claim in new[] { 16452m, 20000m, 30000m, 50000m })
        {
            PayrollInput input = Input(4000m);
            input.FederalClaimAmount = claim;
            input.ProvincialClaimAmount = claim;

            decimal tax = PayrollCalculator.Calculate(input, new PayrollYearToDate(), rates).FederalTax;
            Assert.True(tax <= previous, $"Tax rose when the claim rose to {claim}.");
            previous = tax;
        }
    }

    [Fact]
    public void NoTd1OnFile_IsTreatedAsTheBasicPersonalAmount()
    {
        PayrollRateTable rates = Rates();

        PayrollInput explicitClaim = Input(3000m);
        explicitClaim.FederalClaimAmount = rates.Federal.BasicPersonalAmount.Maximum;
        explicitClaim.ProvincialClaimAmount = rates.Provinces["AB"].BasicPersonalAmount.Maximum;

        PayrollDeductions withClaim = PayrollCalculator.Calculate(explicitClaim, new PayrollYearToDate(), rates);
        PayrollDeductions without = PayrollCalculator.Calculate(Input(3000m), new PayrollYearToDate(), rates);

        Assert.Equal(withClaim.FederalTax, without.FederalTax);
        Assert.Equal(withClaim.ProvincialTax, without.ProvincialTax);
    }

    [Fact]
    public void AZeroTd1Claim_TakesThePersonalAmountAwayRatherThanDefaultingToIt()
    {
        // Someone with a second job claims nothing here because the other employer applies their
        // personal amount. Treating that zero as "no TD1" gave them the credit twice over.
        PayrollRateTable rates = Rates();
        ProvincialRates ab = rates.Provinces["AB"];

        PayrollInput zero = Input(3000m);
        zero.FederalClaimIsZero = true;
        zero.ProvincialClaimIsZero = true;

        PayrollDeductions claimsNothing = PayrollCalculator.Calculate(zero, new PayrollYearToDate(), rates);
        PayrollDeductions noTd1 = PayrollCalculator.Calculate(Input(3000m), new PayrollYearToDate(), rates);

        decimal federalCredit = rates.Federal.LowestRateForCredits * rates.Federal.BasicPersonalAmount.Maximum / 26m;
        decimal provincialCredit = ab.Brackets[0].Rate * ab.BasicPersonalAmount.Maximum / 26m;

        Assert.InRange(claimsNothing.FederalTax - noTd1.FederalTax, federalCredit - 0.01m, federalCredit + 0.01m);
        Assert.InRange(claimsNothing.ProvincialTax - noTd1.ProvincialTax, provincialCredit - 0.01m, provincialCredit + 0.01m);
    }

    [Fact]
    public void AZeroTd1Claim_TakesThePersonalAmountAwayInQuebecToo()
    {
        PayrollRateTable rates = Rates();
        QuebecRates qc = rates.Quebec!;

        PayrollInput zero = Input(3000m);
        zero.Province = "QC";
        zero.FederalClaimIsZero = true;
        zero.ProvincialClaimIsZero = true;

        PayrollInput noTd1 = Input(3000m);
        noTd1.Province = "QC";

        PayrollDeductions claimsNothing = PayrollCalculator.Calculate(zero, new PayrollYearToDate(), rates);
        PayrollDeductions basic = PayrollCalculator.Calculate(noTd1, new PayrollYearToDate(), rates);

        decimal federalCredit = rates.Federal.LowestRateForCredits * rates.Federal.BasicPersonalAmount.Maximum
                                * (1 - qc.FederalAbatement) / 26m;
        decimal quebecCredit = qc.CreditRate * qc.BasicPersonalAmount / 26m;

        Assert.InRange(claimsNothing.FederalTax - basic.FederalTax, federalCredit - 0.01m, federalCredit + 0.01m);
        Assert.InRange(claimsNothing.ProvincialTax - basic.ProvincialTax, quebecCredit - 0.01m, quebecCredit + 0.01m);
    }

    [Fact]
    public void AnAmountOnTheTd1_WinsOverTheZeroFlag()
    {
        PayrollRateTable rates = Rates();

        PayrollInput flagged = Input(3000m);
        flagged.FederalClaimAmount = 20000m;
        flagged.FederalClaimIsZero = true;

        PayrollInput plain = Input(3000m);
        plain.FederalClaimAmount = 20000m;

        Assert.Equal(
            PayrollCalculator.Calculate(plain, new PayrollYearToDate(), rates).FederalTax,
            PayrollCalculator.Calculate(flagged, new PayrollYearToDate(), rates).FederalTax);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void ZeroGross_ProducesNothingAtAll()
    {
        PayrollDeductions d = PayrollCalculator.Calculate(Input(0m), new PayrollYearToDate(), Rates());

        Assert.Equal(0m, d.CppEmployee);
        Assert.Equal(0m, d.EiEmployee);
        Assert.Equal(0m, d.FederalTax);
        Assert.Equal(0m, d.ProvincialTax);
        Assert.Equal(0m, d.NetPay);
    }

    [Fact]
    public void NegativeGross_ReversesCleanlyWithoutInventingDeductions()
    {
        // Voiding a run writes negative lines. They must not produce negative tax, which would
        // read as a refund the employer never made.
        PayrollDeductions d = PayrollCalculator.Calculate(Input(-2000m), new PayrollYearToDate(), Rates());

        Assert.Equal(0m, d.CppEmployee);
        Assert.Equal(0m, d.EiEmployee);
        Assert.Equal(0m, d.FederalTax);
        Assert.Equal(0m, d.ProvincialTax);
        Assert.Equal(-2000m, d.NetPay);
    }

    [Fact]
    public void AnUnsupportedProvince_RefusesRatherThanGuessing()
    {
        // Falling back to another province's table would produce figures that look plausible
        // and are wrong, which is worse than refusing.
        PayrollInput input = Input(2000m);
        input.Province = "ZZ";

        Assert.Throws<NotSupportedException>(
            () => PayrollCalculator.Calculate(input, new PayrollYearToDate(), Rates()));
    }

    [Fact]
    public void MissingArguments_ThrowRatherThanCalculatingOnNothing()
    {
        PayrollRateTable rates = Rates();

        Assert.Throws<ArgumentNullException>(() => PayrollCalculator.Calculate(null!, new PayrollYearToDate(), rates));
        Assert.Throws<ArgumentNullException>(() => PayrollCalculator.Calculate(Input(1000m), null!, rates));
        Assert.Throws<ArgumentNullException>(() => PayrollCalculator.Calculate(Input(1000m), new PayrollYearToDate(), null!));
    }

    [Fact]
    public void APayPeriodBelowTheBasicExemption_AttractsNoCpp()
    {
        // 3500 a year over 26 periods is 134.62 a period. Below that there is nothing
        // pensionable, and a negative pensionable amount must not become a negative deduction.
        PayrollDeductions d = PayrollCalculator.Calculate(Input(100m), new PayrollYearToDate(), Rates());

        Assert.Equal(0m, d.CppEmployee);
    }

    #endregion
}
