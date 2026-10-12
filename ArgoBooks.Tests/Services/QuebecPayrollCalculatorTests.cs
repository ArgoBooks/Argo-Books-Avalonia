using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the Quebec calculator, against TP-1015.F-V (2026-01).
///
/// The two intermediate figures the guide publishes in its own worked example, Appendix 1, are
/// checked directly. Those are the parts that could not have been guessed from the federal
/// formula: the deduction for workers, which comes off income and has no federal equivalent,
/// and the deductible share of QPP, which is the 1.00 percentage point inside the 6.30% rate
/// rather than CPP's split.
///
/// These do NOT prove agreement with Revenu Quebec on a whole pay stub. That needs fixtures
/// captured from WebRAS, which is Quebec's equivalent of PDOC. PDOC deliberately excludes
/// Quebec, so the fixtures gathered for the rest of Canada say nothing here.
/// </summary>
public class QuebecPayrollCalculatorTests
{
    private static PayrollRateTable Rates() => new PayrollRateService().GetForDate(new DateTime(2026, 10, 9))!;

    private static PayrollInput Input(decimal gross, int periods = 26) => new()
    {
        GrossPay = gross,
        Province = "QC",
        PayPeriodsPerYear = periods,
    };

    private static PayrollDeductions Calc(decimal gross, int periods = 26, PayrollYearToDate? ytd = null) =>
        PayrollCalculator.Calculate(Input(gross, periods), ytd ?? new PayrollYearToDate(), Rates());

    #region Against the guide's own worked example

    [Fact]
    public void QppMatchesTheGuidesWorkedExample()
    {
        // Appendix 1 uses $4,000 biweekly, where the guide's own intermediate of $38.65 implies a QPP contribution of $243.52.
        Assert.Equal(243.52m, Calc(4000m).CppEmployee);
    }

    [Fact]
    public void TheDeductionForWorkersMatchesTheGuidesWorkedExample()
    {
        // The guide states H = $55.77 for this employee.
        PayrollRateTable rates = Rates();
        QuebecRates qc = rates.Quebec!;

        decimal expected = Math.Min(qc.WorkerDeductionRate * 4000m, qc.WorkerDeductionMaxAnnual / 26m);

        Assert.Equal(55.77m, Math.Round(expected, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void TheConstantsAreThePublishedWholeDollarOnes()
    {
        // Deriving these from bracket continuity gives 2,717.25 / 8,151.25 / 10,465.54. Revenu
        // Quebec publishes them rounded to whole dollars and those are what must be used.
        List<TaxBracket> brackets = Rates().Quebec!.Brackets;

        Assert.Equal(0m, brackets[0].ConstantK);
        Assert.Equal(2717m, brackets[1].ConstantK);
        Assert.Equal(8151m, brackets[2].ConstantK);
        Assert.Equal(10465m, brackets[3].ConstantK);
    }

    #endregion

    #region WebRAS fixtures, captured from Revenu Quebec's own calculator

    /// <summary>
    /// Captured from WebRAS 2026.01 (rates effective 2026-01-01, tool last updated 2026-02-25),
    /// biweekly, no TP-1015.3-V on file so the basic personal amount applies, no other
    /// deductions or credits.
    ///
    /// These are what the rest of Canada's PDOC fixtures are, and Quebec had none: PDOC
    /// deliberately excludes Quebec, so nothing gathered for the other provinces said anything
    /// here.
    ///
    /// Two incomes, deliberately in different tax brackets: $2,400 lands in Quebec's 19% band
    /// once annualised and $5,000 in the 24% band, so a wrong bracket constant cannot pass both.
    ///
    /// WebRAS is a check and NOT the oracle, which is the opposite of PDOC's role. Revenu Quebec
    /// states on the tool itself: "In the event of a discrepancy between the calculations using
    /// the formulas and those using WebRAS, the calculations using the formulas prevail." So a
    /// future failure here is a question to investigate against TP-1015.F, not a licence to edit
    /// the calculator until the numbers match.
    /// </summary>
    [Theory]
    [InlineData(2400, 142.72, 10.32, 234.55)]
    [InlineData(5000, 306.52, 21.50, 759.39)]
    public void MatchesWebRasOnTheEmployeeDeductions(
        decimal gross, decimal qpp, decimal qpip, decimal quebecTax)
    {
        PayrollDeductions d = Calc(gross);

        Assert.Equal(qpp, d.CppEmployee);
        Assert.Equal(0m, d.Cpp2Employee);
        Assert.Equal(qpip, d.QpipEmployee);
        Assert.Equal(quebecTax, d.ProvincialTax);
    }

    [Theory]
    [InlineData(2400, 142.72, 14.45)]
    [InlineData(5000, 306.52, 30.10)]
    public void MatchesWebRasOnTheEmployerContributions(decimal gross, decimal qpp, decimal qpip)
    {
        // The employer QPIP rate is its own published figure rather than a multiple of the employee's, and WebRAS prints both, so this is where that would show up if it were ever quietly derived.
        PayrollDeductions d = Calc(gross);

        Assert.Equal(qpp, d.CppEmployer);
        Assert.Equal(qpip, d.QpipEmployer);
    }

    [Fact]
    public void MatchesWebRasOnTheTaxableIncomeItArrivesAt()
    {
        // WebRAS prints the figure it taxes: $2,321.58 on a $2,400 pay.
        PayrollRateTable rates = Rates();
        QuebecRates qc = rates.Quebec!;

        decimal workerDeduction = Math.Min(qc.WorkerDeductionRate * 2400m, qc.WorkerDeductionMaxAnnual / 26m);
        decimal additionalShare = (qc.Qpp.RateEmployee - qc.Qpp.BaseRateEmployee) / qc.Qpp.RateEmployee;
        decimal deductibleQpp = Math.Round(Calc(2400m).CppEmployee * additionalShare, 2, MidpointRounding.AwayFromZero);

        decimal taxable = 2400m - Math.Round(workerDeduction, 2, MidpointRounding.AwayFromZero) - deductibleQpp;

        Assert.Equal(2321.58m, taxable);
    }

    #endregion

    #region What makes Quebec different

    [Fact]
    public void QuebecUsesQppRatesNotCpp()
    {
        // 6.30% against 5.95%. Same exemption and ceiling, so the only difference at this
        // income is the rate, and it is visible directly.
        decimal quebec = Calc(2400m).CppEmployee;
        decimal alberta = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 2400m, Province = "AB", PayPeriodsPerYear = 26 },
            new PayrollYearToDate(), Rates()).CppEmployee;

        Assert.True(quebec > alberta, $"QPP {quebec} should exceed CPP {alberta}");
        Assert.Equal(Math.Round((2400m - 3500m / 26m) * 0.0630m, 2, MidpointRounding.AwayFromZero), quebec);
    }

    [Fact]
    public void QuebecEmployeesPayQpip()
    {
        // No equivalent exists anywhere else in Canada.
        PayrollDeductions d = Calc(2400m);

        Assert.Equal(Math.Round(2400m * 0.00430m, 2, MidpointRounding.AwayFromZero), d.QpipEmployee);
        Assert.Equal(Math.Round(2400m * 0.00602m, 2, MidpointRounding.AwayFromZero), d.QpipEmployer);
    }

    [Fact]
    public void TheQpipEmployerShareIsReadFromItsOwnPublishedRate()
    {
        // Revenu Quebec publishes the employer rate and maximum separately, and as it happens both are exactly 1.4 times the employee's: 0.00430 x 1.4 = 0.00602 and 442.90 x 1.4 = 620.06.
        PayrollRateTable rates = Rates();
        QuebecRates qc = rates.Quebec!;

        Assert.Equal(0.00602m, qc.Qpip.RateEmployer);
        Assert.Equal(620.06m, qc.Qpip.MaxPremiumEmployer);

        PayrollDeductions d = Calc(2400m);
        Assert.Equal(Math.Round(2400m * qc.Qpip.RateEmployer, 2, MidpointRounding.AwayFromZero), d.QpipEmployer);
    }

    [Fact]
    public void QuebecEmployeesPayLessEiThanTheRestOfCanada()
    {
        // 1.30% against 1.63%, because QPIP covers the parental benefits EI covers elsewhere.
        decimal quebec = Calc(2400m).EiEmployee;
        decimal alberta = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 2400m, Province = "AB", PayPeriodsPerYear = 26 },
            new PayrollYearToDate(), Rates()).EiEmployee;

        Assert.True(quebec < alberta, $"Quebec EI {quebec} should be below {alberta}");
        Assert.Equal(Math.Round(2400m * 0.0130m, 2, MidpointRounding.AwayFromZero), quebec);
    }

    [Fact]
    public void FederalTaxIsReducedByTheAbatement()
    {
        // CRA collects less from Quebec residents because Quebec collects its own income tax.
        decimal quebec = Calc(2400m).FederalTax;
        decimal alberta = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 2400m, Province = "AB", PayPeriodsPerYear = 26 },
            new PayrollYearToDate(), Rates()).FederalTax;

        Assert.True(quebec < alberta * 0.9m,
            $"Quebec federal tax {quebec} should be well below {alberta} after the 16.5% abatement");
    }

    [Fact]
    public void QuebecProvincialTaxIsChargedAndIsSubstantial()
    {
        PayrollDeductions d = Calc(2400m);

        Assert.True(d.ProvincialTax > 0);
    }

    #endregion

    #region Ceilings

    [Fact]
    public void QppStopsAtItsOwnMaximumNotCpps()
    {
        PayrollRateTable rates = Rates();
        var atMax = new PayrollYearToDate { CppEmployee = rates.Quebec!.Qpp.MaxContributionEmployee };

        Assert.Equal(0m, Calc(5000m, ytd: atMax).CppEmployee);

        // And CPP's lower maximum must NOT stop it: an employee between the two ceilings is
        // still contributing.
        var atCppMax = new PayrollYearToDate { CppEmployee = rates.Cpp.MaxContributionEmployee };
        Assert.True(Calc(5000m, ytd: atCppMax).CppEmployee > 0);
    }

    [Fact]
    public void QpipStopsAtItsAnnualMaximum()
    {
        PayrollRateTable rates = Rates();
        var atMax = new PayrollYearToDate { QpipEmployee = rates.Quebec!.Qpip.MaxPremiumEmployee };

        Assert.Equal(0m, Calc(5000m, ytd: atMax).QpipEmployee);
    }

    [Fact]
    public void EiStopsAtQuebecsLowerMaximum()
    {
        PayrollRateTable rates = Rates();
        var atMax = new PayrollYearToDate { EiEmployee = rates.Quebec!.EiMaxPremiumEmployee };

        Assert.Equal(0m, Calc(5000m, ytd: atMax).EiEmployee);
    }

    #endregion

    #region The K2Q credit

    [Fact]
    public void ReachingTheEiMaximum_DoesNotChangeFederalTax()
    {
        // T4127 on all three terms of K2Q: once contributions reach the annual maximum, the rest of the year's periods use the capped figure.
        PayrollRateTable rates = Rates();
        var atMax = new PayrollYearToDate { EiEmployee = rates.Quebec!.EiMaxPremiumEmployee };

        Assert.Equal(0m, Calc(5000m, ytd: atMax).EiEmployee);
        Assert.Equal(Calc(5000m).FederalTax, Calc(5000m, ytd: atMax).FederalTax);
    }

    [Fact]
    public void ReachingTheQpipMaximum_DoesNotChangeFederalTax()
    {
        PayrollRateTable rates = Rates();
        var atMax = new PayrollYearToDate { QpipEmployee = rates.Quebec!.Qpip.MaxPremiumEmployee };

        Assert.Equal(0m, Calc(5000m, ytd: atMax).QpipEmployee);
        Assert.Equal(Calc(5000m).FederalTax, Calc(5000m, ytd: atMax).FederalTax);
    }

    [Fact]
    public void FederalTax_CreditsQpipAsWellAsQppAndEi()
    {
        // K2Q has three terms where the rest of Canada's K2 has two, and the third is QPIP.
        Assert.Equal(185.17m, Calc(2400m).FederalTax);
    }

    #endregion

    #region Identities that must hold here too

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(2400)]
    [InlineData(4000)]
    [InlineData(10000)]
    public void NetPayIsGrossMinusEveryEmployeeDeductionIncludingQpip(decimal gross)
    {
        PayrollDeductions d = Calc(gross);

        Assert.Equal(
            d.GrossPay - d.CppEmployee - d.Cpp2Employee - d.EiEmployee - d.QpipEmployee
            - d.FederalTax - d.ProvincialTax,
            d.NetPay);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(2400)]
    [InlineData(10000)]
    public void TotalCostAndRemittanceIncludeTheEmployerQpipShare(decimal gross)
    {
        PayrollDeductions d = Calc(gross);

        Assert.Equal(d.GrossPay + d.CppEmployer + d.Cpp2Employer + d.EiEmployer + d.QpipEmployer, d.TotalCost);
        Assert.True(d.TotalRemittance > d.FederalTax + d.ProvincialTax);
    }

    [Fact]
    public void ZeroGrossProducesNothing()
    {
        PayrollDeductions d = Calc(0m);

        Assert.Equal(0m, d.CppEmployee);
        Assert.Equal(0m, d.QpipEmployee);
        Assert.Equal(0m, d.EiEmployee);
        Assert.Equal(0m, d.FederalTax);
        Assert.Equal(0m, d.ProvincialTax);
        Assert.Equal(0m, d.NetPay);
    }

    [Fact]
    public void ALowEarnerPaysNoQuebecTaxRatherThanNegativeTax()
    {
        // Well under the $18,952 personal amount once annualised.
        Assert.Equal(0m, Calc(200m).ProvincialTax);
    }

    [Fact]
    public void QuebecIsReachedThroughTheOrdinaryEntryPoint()
    {
        // Callers do not choose the calculator. PayrollCalculator dispatches on the province,
        // so a Quebec employee cannot accidentally be run through the federal formula.
        PayrollDeductions d = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 2400m, Province = "qc", PayPeriodsPerYear = 26 },
            new PayrollYearToDate(), Rates());

        Assert.True(d.QpipEmployee > 0, "lower case province code should still reach the Quebec calculator");
    }

    #endregion

    // Moved to Quebec part way through the year.
    [Fact]
    public void EiPaidInAnotherProvince_CountsAtTheQuebecRate()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate { EiEmployee = 782.40m, EiEmployeeQuebec = 0m };
        decimal premium = Math.Round(20000m * rates.Ei.QuebecRateEmployee, 2, MidpointRounding.AwayFromZero);
        decimal room = Math.Round(
            rates.Quebec!.EiMaxPremiumEmployee - 782.40m * rates.Ei.QuebecRateEmployee / rates.Ei.RateEmployee,
            2, MidpointRounding.AwayFromZero);

        decimal ei = Calc(20000m, periods: 12, ytd: ytd).EiEmployee;

        Assert.Equal(Math.Min(premium, room), ei);
        Assert.True(ei > rates.Quebec.EiMaxPremiumEmployee - 782.40m);
    }
}
