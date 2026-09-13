using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the bonus method: T4127's "Tax calculation formulas for bonuses, retroactive pay
/// increases, and other non-periodic payments".
///
/// The rule being enforced is that a bonus is NOT annualised. Everything else in a pay period
/// recurs, so the engine multiplies it by the number of periods to find the year's income; a
/// bonus is paid once, and multiplying it by 26 invents income the employee will never have. A
/// $5,000 bonus on $2,400 biweekly annualises to $192,400 instead of $67,400, which is three
/// brackets up, and takes about 11% too much out of that one pay.
///
/// The overcharge comes back at year end, which is exactly why the error survives being looked
/// at: nobody is out of pocket in April, they are just short in August.
///
/// CRA's method is a difference: work out the annual tax with the bonus, work it out without,
/// and withhold the gap once rather than dividing it across the year.
///
/// Most cases here set the employee CPP and EI exempt. That is not a realistic pay run; it
/// removes the K2 credit and the enhanced-contribution deduction so that the bracket arithmetic
/// is the only thing left, and an expected figure can be derived from the published constants
/// instead of copied out of this implementation. The interaction with contributions is covered
/// separately, at the bottom.
/// </summary>
public class PayrollBonusTests
{
    private static PayrollRateTable Rates() => new PayrollRateService().GetForDate(new DateTime(2026, 8, 15))!;

    private static PayrollInput Input(decimal gross, decimal bonus = 0m, string province = "AB",
                                      int periods = 26, bool exempt = true) => new()
    {
        GrossPay = gross,
        NonPeriodicPay = bonus,
        Province = province,
        PayPeriodsPerYear = periods,
        IsCppExempt = exempt,
        IsEiExempt = exempt,
    };

    /// <summary>
    /// T4127 Step 4 without the credits: (R x A) - K. The credits are identical either side of
    /// the difference, so they cancel and only this part decides the tax on a bonus.
    /// </summary>
    private static decimal BasicTax(List<TaxBracket> brackets, decimal annual)
    {
        TaxBracket bracket = brackets.First(b => b.UpTo == null || annual <= b.UpTo);
        return bracket.Rate * annual - bracket.ConstantK;
    }

    #region The difference method

    [Fact]
    public void TheTaxOnABonus_IsTheYearWithItLessTheYearWithoutIt()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();

        PayrollDeductions regular = PayrollCalculator.Calculate(Input(2400m), ytd, rates);
        PayrollDeductions withBonus = PayrollCalculator.Calculate(Input(7400m, bonus: 5000m), ytd, rates);

        // Step 2 gives A = 62,400 and Step 1 gives A = 67,400. Both sit inside the same federal
        // and the same Alberta bracket, so the whole difference is the bracket rate on $5,000.
        decimal expectedFederal = Math.Round(
            BasicTax(rates.Federal.Brackets, 67400m) - BasicTax(rates.Federal.Brackets, 62400m), 2);
        decimal expectedProvincial = Math.Round(
            BasicTax(rates.Provinces["AB"].Brackets, 67400m) - BasicTax(rates.Provinces["AB"].Brackets, 62400m), 2);

        Assert.Equal(regular.FederalTax + expectedFederal, withBonus.FederalTax);
        Assert.Equal(regular.ProvincialTax + expectedProvincial, withBonus.ProvincialTax);
    }

    [Fact]
    public void ABonus_IsNotAnnualised()
    {
        // The regression this whole file exists for. Same $7,400 pay period twice: once with
        // $5,000 of it declared as a bonus, once treated as though it recurred every period.
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();

        PayrollDeductions asBonus = PayrollCalculator.Calculate(Input(7400m, bonus: 5000m), ytd, rates);
        PayrollDeductions annualised = PayrollCalculator.Calculate(Input(7400m), ytd, rates);

        decimal bonusTax = asBonus.FederalTax + asBonus.ProvincialTax;
        decimal annualisedTax = annualised.FederalTax + annualised.ProvincialTax;

        // On these figures it is $1,896.88 against $2,101.08: annualising takes an extra $204
        // out of one pay, around 11%. The employee gets it back at year end, which is exactly
        // why the error survives being looked at.
        Assert.True(annualisedTax > bonusTax * 1.10m,
            $"a bonus withheld {bonusTax:F2}, annualising it withheld {annualisedTax:F2}");
    }

    [Fact]
    public void ABonus_DoesNotChangeTheTaxOnTheRegularPay()
    {
        // The employee's normal withholding must not move because they also got a bonus. If it
        // did, every pay period after a bonus would be wrong too.
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();

        PayrollDeductions before = PayrollCalculator.Calculate(Input(2400m), ytd, rates);
        PayrollDeductions during = PayrollCalculator.Calculate(Input(7400m, bonus: 5000m), ytd, rates);

        // The year-to-date has to move, or the third call never learns a bonus was paid and this
        // test cannot fail for the reason it exists. It did not, which is how a bonus being
        // re-taxed in every later period got through with a test named for exactly that.
        ytd.PensionableEarnings += 7400m;
        ytd.InsurableEarnings += 7400m;
        ytd.CppEmployee += during.CppEmployee;
        ytd.EiEmployee += during.EiEmployee;
        ytd.NonPeriodicPay += 5000m;

        PayrollDeductions after = PayrollCalculator.Calculate(Input(2400m), ytd, rates);

        Assert.Equal(before.FederalTax, after.FederalTax);
        Assert.True(during.FederalTax > before.FederalTax);
    }

    [Fact]
    public void ABonusThatCrossesABracket_IsTaxedAcrossBothParts()
    {
        // $58,523 is the top of the lowest federal bracket. A bonus that straddles it must be
        // taxed partly at 14% and partly at 20.5%, which is what taking the difference of two
        // annual figures does for free and what a single marginal rate would get wrong.
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();

        PayrollDeductions regular = PayrollCalculator.Calculate(Input(2200m), ytd, rates);
        PayrollDeductions withBonus = PayrollCalculator.Calculate(Input(12200m, bonus: 10000m), ytd, rates);

        decimal onBonus = withBonus.FederalTax - regular.FederalTax;

        // A = 57,200 without and 67,200 with, so 1,323 of the bonus is taxed at 14% and the
        // remaining 8,677 at 20.5%.
        decimal expected = Math.Round(
            BasicTax(rates.Federal.Brackets, 67200m) - BasicTax(rates.Federal.Brackets, 57200m), 2);

        Assert.Equal(expected, onBonus);
        Assert.NotEqual(Math.Round(0.205m * 10000m, 2), onBonus);
        Assert.NotEqual(Math.Round(0.14m * 10000m, 2), onBonus);
    }

    [Fact]
    public void ASecondBonus_IsStackedOnTheFirstRatherThanTaxedAsIfItWereTheOnlyOne()
    {
        // T4127's B1. Without it, an employee paid two $10,000 bonuses has the second taxed from
        // the same starting point as the first, and is under-withheld on it.
        PayrollRateTable rates = Rates();

        PayrollDeductions first = PayrollCalculator.Calculate(
            Input(12200m, bonus: 10000m), new PayrollYearToDate(), rates);

        PayrollDeductions second = PayrollCalculator.Calculate(
            Input(12200m, bonus: 10000m),
            new PayrollYearToDate { NonPeriodicPay = 10000m },
            rates);

        PayrollDeductions regular = PayrollCalculator.Calculate(Input(2200m), new PayrollYearToDate(), rates);

        decimal onFirst = first.FederalTax - regular.FederalTax;
        decimal onSecond = second.FederalTax - regular.FederalTax;

        Assert.True(onSecond > onFirst,
            $"the second bonus was taxed at {onSecond:F2}, the first at {onFirst:F2}");
    }

    #endregion

    #region The flat rate below CRA's ceiling

    [Fact]
    public void ABonusOnAVeryLowAnnualIncome_IsTaxedAtTheFlatRate()
    {
        // T4127: where annual taxable income including the bonus is $5,000 or less, the whole
        // calculation is replaced by a flat rate rather than run through the brackets, which
        // would produce nothing at all this far below the personal amount.
        PayrollRateTable rates = Rates();

        PayrollDeductions d = PayrollCalculator.Calculate(
            Input(150m, bonus: 50m, periods: 12), new PayrollYearToDate(), rates);

        Assert.Equal(Math.Round(50m * rates.Federal.FlatBonusRate, 2), d.FederalTax);
        Assert.Equal(0m, d.ProvincialTax);
    }

    [Fact]
    public void ABonusJustOverTheCeiling_GoesBackToTheFormula()
    {
        // Immediately above the ceiling the brackets take over, and this far below the personal
        // amount they produce nothing. The flat rate is a floor CRA applies only underneath it,
        // so the tax must DROP as income crosses the line rather than continuing to climb.
        PayrollRateTable rates = Rates();

        PayrollDeductions under = PayrollCalculator.Calculate(
            Input(450m, bonus: 50m, periods: 12), new PayrollYearToDate(), rates);
        PayrollDeductions over = PayrollCalculator.Calculate(
            Input(500m, bonus: 50m, periods: 12), new PayrollYearToDate(), rates);

        Assert.Equal(Math.Round(50m * rates.Federal.FlatBonusRate, 2), under.FederalTax);
        Assert.Equal(0m, over.FederalTax);
    }

    #endregion

    #region Splitting the period

    [Fact]
    public void WithNoBonus_TheSplitIsTheWholePeriodAnnualised()
    {
        (decimal periodic, decimal bonus, decimal prior) = PayrollCalculator.SplitForBonus(
            Input(1000m), new PayrollYearToDate(), gross: 1000m, periods: 26, additionalContributions: 10m);

        Assert.Equal((1000m - 10m) * 26m, periodic);
        Assert.Equal(0m, bonus);
        Assert.Equal(0m, prior);
    }

    [Fact]
    public void TheDeductionForAdditionalContributions_IsSplitInProportionToPay()
    {
        // T4127's F5A and F5B: F5A = F5 x ((PI - B) / PI) and F5B = F5 x (B / PI). The split
        // matters out of proportion to its size, because F5A gets multiplied by the number of
        // pay periods and F5B does not.
        (decimal periodic, decimal bonus, _) = PayrollCalculator.SplitForBonus(
            Input(1000m, bonus: 400m), new PayrollYearToDate(),
            gross: 1000m, periods: 26, additionalContributions: 10m);

        // F5B = 10 x 0.4 = 4.00, so F5A = 6.00.
        Assert.Equal((600m - 6m) * 26m, periodic);
        Assert.Equal(400m - 4m, bonus);
    }

    [Fact]
    public void ABonusLargerThanThePeriodsPay_IsCappedAtIt()
    {
        // A bonus paid with no regular pay in the period. The regular side must be zero rather
        // than negative, which would otherwise subtract invented income from the year.
        (decimal periodic, decimal bonus, _) = PayrollCalculator.SplitForBonus(
            Input(1000m, bonus: 5000m), new PayrollYearToDate(),
            gross: 1000m, periods: 26, additionalContributions: 0m);

        Assert.Equal(0m, periodic);
        Assert.Equal(1000m, bonus);
    }

    [Fact]
    public void ANegativeBonus_IsIgnoredRatherThanCreditedAgainstThePay()
    {
        (decimal periodic, decimal bonus, _) = PayrollCalculator.SplitForBonus(
            Input(1000m, bonus: -500m), new PayrollYearToDate(),
            gross: 1000m, periods: 26, additionalContributions: 0m);

        Assert.Equal(1000m * 26m, periodic);
        Assert.Equal(0m, bonus);
    }

    [Fact]
    public void PreviousBonuses_ComeFromTheYearToDateFigures()
    {
        (_, _, decimal prior) = PayrollCalculator.SplitForBonus(
            Input(1000m), new PayrollYearToDate { NonPeriodicPay = 7500m },
            gross: 1000m, periods: 26, additionalContributions: 0m);

        Assert.Equal(7500m, prior);
    }

    #endregion

    #region Contributions

    [Fact]
    public void CppAndEi_AreStillChargedOnTheWholeBonus()
    {
        // The split is an income tax rule only. A bonus is pensionable and insurable in full,
        // and it is included with the regular pay for the period here, so the pay period's
        // basic exemption is allowed exactly once as normal.
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();

        PayrollDeductions withBonus = PayrollCalculator.Calculate(
            Input(7400m, bonus: 5000m, exempt: false), ytd, rates);
        PayrollDeductions asRegular = PayrollCalculator.Calculate(
            Input(7400m, exempt: false), ytd, rates);

        Assert.Equal(asRegular.CppEmployee, withBonus.CppEmployee);
        Assert.Equal(asRegular.Cpp2Employee, withBonus.Cpp2Employee);
        Assert.Equal(asRegular.EiEmployee, withBonus.EiEmployee);
        Assert.Equal(7400m, withBonus.GrossPay);
    }

    [Fact]
    public void ARealPayRunWithABonus_StillWithholdsLessThanAnnualisingIt()
    {
        // The same comparison as above but with contributions live, so the K2 credit and the
        // enhanced-contribution deduction are both in play. The conclusion must not depend on
        // having switched them off.
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();

        PayrollDeductions asBonus = PayrollCalculator.Calculate(
            Input(7400m, bonus: 5000m, exempt: false), ytd, rates);
        PayrollDeductions annualised = PayrollCalculator.Calculate(
            Input(7400m, exempt: false), ytd, rates);

        Assert.True(asBonus.FederalTax + asBonus.ProvincialTax
                    < (annualised.FederalTax + annualised.ProvincialTax) / 1.10m);
        Assert.True(asBonus.NetPay > annualised.NetPay);
    }

    #endregion

    #region Provinces with their own shapes

    [Theory]
    [InlineData("ON")]  // surtax and a health premium, both non-linear in income
    [InlineData("BC")]  // a tax reduction that tapers away
    [InlineData("NS")]
    [InlineData("YT")]
    public void EveryProvincesOwnRules_ApplyToTheBonusToo(string province)
    {
        // The difference is taken on the finished provincial tax, so a surtax, a health premium
        // or a tapering reduction is reflected in what a bonus costs. Taking the difference on
        // the basic tax and adding the extras afterwards would get all three wrong.
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();

        PayrollDeductions regular = PayrollCalculator.Calculate(Input(3000m, province: province), ytd, rates);
        PayrollDeductions withBonus = PayrollCalculator.Calculate(
            Input(8000m, bonus: 5000m, province: province), ytd, rates);
        PayrollDeductions annualised = PayrollCalculator.Calculate(Input(8000m, province: province), ytd, rates);

        Assert.True(withBonus.ProvincialTax >= regular.ProvincialTax);
        Assert.True(withBonus.ProvincialTax < annualised.ProvincialTax);
    }

    #endregion

    #region Quebec

    private static PayrollInput Quebec(decimal gross, decimal bonus = 0m, int periods = 26) =>
        Input(gross, bonus, province: "QC", periods: periods);

    [Fact]
    public void AQuebecBonusUnderRevenuQuebecsThreshold_IsTaxedAtTheFlatRate()
    {
        // Revenu Quebec states the rule the other way round from CRA and at a much higher
        // ceiling: where annual remuneration including the bonus is $18,952 or less, withhold a
        // flat 7% rather than running the formula.
        PayrollRateTable rates = Rates();
        QuebecRates qc = rates.Quebec!;
        var ytd = new PayrollYearToDate();

        PayrollDeductions regular = PayrollCalculator.Calculate(Quebec(600m), ytd, rates);
        PayrollDeductions withBonus = PayrollCalculator.Calculate(Quebec(1600m, bonus: 1000m), ytd, rates);

        (_, decimal taxableBonus, _) = PayrollCalculator.SplitForBonus(
            Quebec(1600m, bonus: 1000m), ytd, gross: 1600m, periods: 26,
            additionalContributions: Math.Min(qc.WorkerDeductionRate * 1600m, qc.WorkerDeductionMaxAnnual / 26m));

        Assert.Equal(regular.ProvincialTax + Math.Round(taxableBonus * qc.FlatBonusRate, 2),
                     withBonus.ProvincialTax);
    }

    [Fact]
    public void AQuebecBonusOverTheThreshold_UsesTheDifferenceInstead()
    {
        PayrollRateTable rates = Rates();
        QuebecRates qc = rates.Quebec!;
        var ytd = new PayrollYearToDate();

        PayrollDeductions regular = PayrollCalculator.Calculate(Quebec(3000m), ytd, rates);
        PayrollDeductions withBonus = PayrollCalculator.Calculate(Quebec(8000m, bonus: 5000m), ytd, rates);

        decimal onBonus = withBonus.ProvincialTax - regular.ProvincialTax;

        // Well past the flat rate: at this income Quebec's marginal rate is 19% or more, so the
        // formula must be producing considerably more than 7% of the bonus.
        Assert.True(onBonus > 5000m * qc.FlatBonusRate * 2,
            $"expected the formula rather than the flat rate, the bonus attracted {onBonus:F2}");
    }

    [Fact]
    public void AQuebecBonus_IsNotAnnualisedEitherFederallyOrProvincially()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();

        PayrollDeductions asBonus = PayrollCalculator.Calculate(Quebec(8000m, bonus: 5000m), ytd, rates);
        PayrollDeductions annualised = PayrollCalculator.Calculate(Quebec(8000m), ytd, rates);

        Assert.True(asBonus.FederalTax < annualised.FederalTax);
        Assert.True(asBonus.ProvincialTax < annualised.ProvincialTax);
    }

    [Fact]
    public void AQuebecBonusOnAVeryLowIncome_TakesCraSTenPercentFederally()
    {
        // T4127 publishes 10% where it publishes 15% for the rest of Canada. It is a stated
        // figure, not 15% with the Quebec abatement applied to it.
        PayrollRateTable rates = Rates();

        PayrollDeductions d = PayrollCalculator.Calculate(
            Quebec(150m, bonus: 50m, periods: 12), new PayrollYearToDate(), rates);

        // The federal side deducts only the additional QPP, which is nil for an exempt
        // employee, so the whole $50 is taxable there. Quebec's deduction for workers comes off
        // the provincial calculation and not this one.
        Assert.Equal(Math.Round(50m * rates.Quebec!.FederalFlatBonusRate, 2), d.FederalTax);
    }

    #endregion

    #region Through a pay run

    [Fact]
    public void APayRunWithABonus_CarriesItThroughToTheCalculator()
    {
        CompanyData data = CompanyWithEmployee();
        var service = new PayrollService();

        PayRun withBonus = service.CreateDraft(data, new DateTime(2026, 8, 14),
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 14))!;
        withBonus.Lines[0].Bonus = 5000m;
        service.Recalculate(data, withBonus);

        PayRun without = service.CreateDraft(data, new DateTime(2026, 8, 14),
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 14))!;
        without.Lines[0].BasePay += 5000m;
        service.Recalculate(data, without);

        Assert.Equal(without.Lines[0].GrossPay, withBonus.Lines[0].GrossPay);
        Assert.True(withBonus.Lines[0].FederalTax < without.Lines[0].FederalTax,
            "declaring the extra pay as a bonus must withhold less than treating it as a raise");
    }

    [Fact]
    public void EarlierBonuses_ReachTheNextRunAsYearToDate()
    {
        CompanyData data = CompanyWithEmployee();
        var service = new PayrollService();

        PayRun first = service.CreateDraft(data, new DateTime(2026, 7, 17),
            new DateTime(2026, 7, 4), new DateTime(2026, 7, 17))!;
        first.Lines[0].Bonus = 4000m;
        service.Recalculate(data, first);
        service.Approve(first);
        data.PayRuns.Add(first);

        PayrollYearToDate ytd = service.YearToDateFor(data, "EMP-001",
            new PayRun { Id = "PR-9999", PayDate = new DateTime(2026, 8, 14) });

        Assert.Equal(4000m, ytd.NonPeriodicPay);
    }

    [Fact]
    public void VacationPay_IsLeftAsRegularIncome()
    {
        // CRA's definition of a non-periodic payment covers vacation pay taken as money instead
        // of time off. The field here is more often the percentage added to every cheque, which
        // does recur, so it is deliberately annualised. Pinned because it is a judgement rather
        // than something the formula decides.
        CompanyData data = CompanyWithEmployee();
        var service = new PayrollService();

        PayRun asVacation = service.CreateDraft(data, new DateTime(2026, 8, 14),
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 14))!;
        asVacation.Lines[0].VacationPay = 5000m;
        service.Recalculate(data, asVacation);

        PayRun asBonus = service.CreateDraft(data, new DateTime(2026, 8, 14),
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 14))!;
        asBonus.Lines[0].Bonus = 5000m;
        service.Recalculate(data, asBonus);

        Assert.Equal(asVacation.Lines[0].GrossPay, asBonus.Lines[0].GrossPay);
        Assert.True(asVacation.Lines[0].FederalTax > asBonus.Lines[0].FederalTax);
    }

    #endregion

    #region A bonus paid in a run of its own

    /// <summary>
    /// The tax a bonus adds when it is paid alongside the regular pay: the difference between a
    /// run with both and a run with the regular pay alone.
    /// </summary>
    private static (decimal Federal, decimal Provincial) TaxTheBonusAdds(
        CompanyData data, decimal regular, decimal bonus)
    {
        var service = new PayrollService();

        PayRun alone = service.CreateDraft(data, new DateTime(2026, 8, 14),
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 14))!;
        alone.Lines[0].BasePay = regular;
        service.Recalculate(data, alone);

        PayRun together = service.CreateDraft(data, new DateTime(2026, 8, 14),
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 14))!;
        together.Lines[0].BasePay = regular;
        together.Lines[0].Bonus = bonus;
        service.Recalculate(data, together);

        return (together.Lines[0].FederalTax - alone.Lines[0].FederalTax,
                together.Lines[0].ProvincialTax - alone.Lines[0].ProvincialTax);
    }

    [Fact]
    public void ABonusPaidInARunOfItsOwn_IsTaxedOnTopOfTheSalary()
    {
        // $10,000 to an Alberta employee on $62,400 a year, paid on its own cheque. With no
        // regular pay in the run there was nothing to annualise, so the bonus was taxed as though
        // it were the whole year's income and fell under the personal amount: nothing withheld,
        // against roughly $2,900 when the same bonus rides on a regular pay.
        CompanyData data = CompanyWithEmployee();
        var service = new PayrollService();

        PayRun bonusOnly = service.CreateDraft(data, new DateTime(2026, 8, 14),
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 14))!;
        bonusOnly.Lines[0].BasePay = 0m;
        bonusOnly.Lines[0].Bonus = 10_000m;
        service.Recalculate(data, bonusOnly);

        (decimal federal, decimal provincial) = TaxTheBonusAdds(data, 2400m, 10_000m);
        PayRunLine line = bonusOnly.Lines[0];

        Assert.True(federal + provincial > 2_500m);
        Assert.Equal(federal, line.FederalTax);
        Assert.Equal(provincial, line.ProvincialTax);
    }

    [Fact]
    public void AnHourlyEmployeesBonusOnItsOwn_IsTaxedOnTopOfTheirLastRegularPay()
    {
        // An hourly employee has no salary to read, so their normal pay is the regular part of
        // the last pay run they were actually paid.
        CompanyData data = CompanyWithEmployee();
        Employee employee = data.Employees[0];
        employee.PayType = PayType.Hourly;
        employee.PayRate = 30m;

        var service = new PayrollService();

        PayRun earlier = service.CreateDraft(data, new DateTime(2026, 7, 31),
            new DateTime(2026, 7, 18), new DateTime(2026, 7, 31))!;
        earlier.Lines[0].HoursWorked = 80m;
        service.Recalculate(data, earlier);
        service.Approve(earlier);
        data.PayRuns.Add(earlier);

        PayRun bonusOnly = service.CreateDraft(data, new DateTime(2026, 8, 14),
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 14))!;
        bonusOnly.Lines[0].HoursWorked = 0m;
        bonusOnly.Lines[0].Bonus = 10_000m;
        service.Recalculate(data, bonusOnly);

        PayrollRateTable rates = Rates();
        PayrollYearToDate ytd = service.YearToDateFor(data, employee.Id, bonusOnly);
        PayrollInput Hourly(decimal gross, decimal bonus) => new()
        {
            GrossPay = gross,
            NonPeriodicPay = bonus,
            Province = "AB",
            PayPeriodsPerYear = 26,
        };

        PayrollDeductions alone = PayrollCalculator.Calculate(Hourly(2400m, 0m), ytd, rates);
        PayrollDeductions together = PayrollCalculator.Calculate(Hourly(12_400m, 10_000m), ytd, rates);

        Assert.Equal(together.FederalTax - alone.FederalTax, bonusOnly.Lines[0].FederalTax);
        Assert.Equal(together.ProvincialTax - alone.ProvincialTax, bonusOnly.Lines[0].ProvincialTax);
    }

    [Fact]
    public void AQuebecBonusOnItsOwn_IsTaxedOnTopOfTheRegularPayOnBothSides()
    {
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();
        PayrollInput Qc(decimal gross, decimal bonus, decimal usual = 0m) => new()
        {
            GrossPay = gross,
            NonPeriodicPay = bonus,
            RegularPayPerPeriod = usual,
            Province = "QC",
            PayPeriodsPerYear = 26,
        };

        PayrollDeductions bonusOnly = PayrollCalculator.Calculate(Qc(10_000m, 10_000m, usual: 2400m), ytd, rates);
        PayrollDeductions alone = PayrollCalculator.Calculate(Qc(2400m, 0m), ytd, rates);
        PayrollDeductions together = PayrollCalculator.Calculate(Qc(12_400m, 10_000m), ytd, rates);

        Assert.True(bonusOnly.ProvincialTax > 1_500m);
        Assert.Equal(together.FederalTax - alone.FederalTax, bonusOnly.FederalTax);
        Assert.Equal(together.ProvincialTax - alone.ProvincialTax, bonusOnly.ProvincialTax);
    }

    [Fact]
    public void ABonusRunStillChargesCppAndEiOnTheBonusAlone()
    {
        // Only income tax borrows the regular pay. The contributions are on what this run
        // actually pays, or the employee would be charged twice for the same salary.
        PayrollRateTable rates = Rates();
        var ytd = new PayrollYearToDate();

        PayrollDeductions borrowed = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 10_000m, NonPeriodicPay = 10_000m, RegularPayPerPeriod = 2400m, Province = "AB" },
            ytd, rates);
        PayrollDeductions plain = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 10_000m, NonPeriodicPay = 10_000m, Province = "AB" },
            ytd, rates);

        Assert.Equal(plain.CppEmployee, borrowed.CppEmployee);
        Assert.Equal(plain.Cpp2Employee, borrowed.Cpp2Employee);
        Assert.Equal(plain.EiEmployee, borrowed.EiEmployee);
        Assert.Equal(10_000m, borrowed.GrossPay);
    }

    private static CompanyData CompanyWithEmployee()
    {
        var data = new CompanyData();
        data.Employees.Add(new Employee
        {
            Id = "EMP-001",
            Name = "Dana Smith",
            Sin = "046454286",
            Province = "AB",
            PayType = PayType.Salary,
            PayRate = 62400m,
            PayFrequency = PayFrequency.Biweekly,
        });
        return data;
    }

    #endregion
}
