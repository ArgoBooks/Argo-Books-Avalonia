using ArgoBooks.Core.Models.Payroll;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Calculates source deductions for one employee for one pay period, following the structure
/// of CRA's T4127 Payroll Deductions Formulas.
///
/// Deliberately a pure function: no file access, no network, no clock, no company data. This
/// is the one part of payroll that must be provably correct, and purity is what lets it be
/// tested against CRA's published figures without standing up an application.
///
/// Year-to-date figures are an input rather than something looked up, because CPP, CPP2 and
/// EI all stop at annual maximums. Passing them in keeps the engine free of dependencies and
/// makes the maximum-reached cases trivial to test.
///
/// Quebec is not handled here. Revenu Québec does not use CRA's rate-and-constant structure,
/// so it needs its own implementation behind the same interface rather than branches inside
/// this one.
/// </summary>
public static class PayrollCalculator
{
    /// <summary>
    /// Deductions for a single pay period.
    /// </summary>
    /// <param name="input">Gross pay and the employee's circumstances for this period.</param>
    /// <param name="ytd">What has already been deducted this calendar year.</param>
    /// <param name="rates">The CRA edition in force on the pay date.</param>
    public static PayrollDeductions Calculate(PayrollInput input, PayrollYearToDate ytd, PayrollRateTable rates)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(ytd);
        ArgumentNullException.ThrowIfNull(rates);

        if (input.RegularPayPerPeriod > 0 && input.GrossPay > 0 && input.NonPeriodicPay >= input.GrossPay)
        {
            return CalculateBonusOnly(input, ytd, rates);
        }

        // Quebec is handed off whole rather than branched through. Its pension plan, its
        // parental insurance plan and its income tax formula are all different in kind, so
        // there is nothing below this line that would apply to it.
        if (string.Equals(input.Province, "QC", StringComparison.OrdinalIgnoreCase))
        {
            return QuebecPayrollCalculator.Calculate(input, ytd, rates);
        }

        if (!rates.Provinces.TryGetValue(input.Province, out ProvincialRates? province))
        {
            throw new NotSupportedException(
                $"No payroll rate table for province '{input.Province}' in edition {rates.EditionId}.");
        }

        int periods = input.PayPeriodsPerYear;
        decimal gross = input.GrossPay;

        decimal cpp = CppForPeriod(gross, periods, ytd, rates, input.IsCppExempt,
                                   out decimal cpp2, out decimal cppUncapped);
        decimal ei = EiForPeriod(gross, ytd, rates, input.IsEiExempt, out decimal eiUncapped);

        // The CPP enhancement, the part of the rate above the historical 4.95% base, is not a
        // tax credit. It is deducted from income before tax is worked out, and CPP2 is
        // deducted the same way. Only the base portion feeds the K2 credit below.
        //
        // Worth stating because the two effects cancel exactly inside the lowest tax bracket,
        // so getting this wrong looks correct until an employee earns enough to reach the
        // second bracket, and then the tax is out by a few dollars every period.
        decimal enhancedShare = rates.Cpp.RateEmployee > 0
            ? (rates.Cpp.RateEmployee - rates.Cpp.BaseRateEmployee) / rates.Cpp.RateEmployee
            : 0m;
        decimal enhancedCpp = Round(cpp * enhancedShare);

        // Split the period into the part that recurs and the part that does not. Annualising a
        // bonus as though it were paid every period is the single largest error a payroll
        // program can make: a $5,000 bonus on $2,400 biweekly annualises to $192,400 instead of
        // $67,400, pushing the whole year's income four brackets up and over-withholding by
        // hundreds of dollars on that one pay.
        //
        // T4127 factors: B is the non-periodic payment payable now, B1 is the non-periodic
        // payments already made this year, I is the regular remuneration for the period.
        (decimal periodicAnnual, decimal currentBonus, decimal priorBonuses) =
            SplitForBonus(input, ytd, gross, periods, enhancedCpp + cpp2);

        // A, for the REGULAR periodic withholding. T4127 chapter 4 step 1 is
        // A = [P x (I - F - F2 - F5A - U1)] - HD - F1, with no B1 term: year-to-date bonuses
        // appear only in the bonus calculation below, where they sit on both sides of a
        // subtraction. Folding B1 in here annualised a bonus that was already taxed in full when
        // it was paid, so every remaining period of the year re-taxed it.
        decimal annual = periodicAnnual;

        // Annual contributions, for the K2 credit. T4127 expresses this as the period's
        // contribution times the number of periods, capped at the annual maximum, and reduced
        // to the base portion because the enhancement was already relieved as a deduction.
        //
        // Two details decide whether this matches CRA, and both were got wrong once:
        //
        // Annualise the UNCAPPED contribution, the one the rate produces before the remaining
        // annual room is applied. Someone who has already reached the ceiling deducts almost
        // nothing this period, and annualising that near-zero figure collapses the credit and
        // spikes their tax. Annualising the uncapped figure and then capping the ANNUAL total
        // gives them the full maximum, which is what they will actually have contributed.
        //
        // Do NOT add the year-to-date figures. Verified against PDOC: the same employee on the
        // same pay gets the same tax in period 1 and period 7, with year-to-date CPP of 808.74
        // on the second. Adding it makes the projection creep up to the annual maximum partway
        // through the year and pin there, quietly under-withholding from that point on.
        // Annualise the RECURRING pay only. cppUncapped and eiUncapped are computed on the whole
        // period including any bonus, and a bonus is paid once: annualising it projects a year of
        // contributions nobody will make, which pins the credit at its maximum and under-withholds
        // every remaining period. Verified against PDOC, which puts the periodic federal tax on a
        // 2,000 biweekly with a 5,000 bonus at 163.09; annualising the full 7,000 gives 155.59.
        decimal recurring = Math.Max(0m, gross - Math.Clamp(input.NonPeriodicPay, 0m, Math.Max(0m, gross)));
        decimal recurringCppUncapped = cppUncapped;
        decimal recurringEiUncapped = eiUncapped;

        if (recurring < gross)
        {
            CppForPeriod(recurring, periods, ytd, rates, input.IsCppExempt, out _, out recurringCppUncapped);
            EiForPeriod(recurring, ytd, rates, input.IsEiExempt, out recurringEiUncapped);
        }

        decimal annualCpp = Math.Min(recurringCppUncapped * periods, rates.Cpp.MaxContributionEmployee)
                            * (1 - enhancedShare);
        decimal annualEi = Math.Min(recurringEiUncapped * periods, rates.Ei.MaxPremiumEmployee);

        // The contributions the bonus itself attracts, NOT annualised, because it is paid once.
        // They belong in K2 for the with-bonus step only, which is what makes the two steps
        // differ by more than the bonus. Leaving them out over-taxed a 5,000 bonus by 46.06
        // federal, the difference between the engine and PDOC that found this.
        decimal bonusCpp = Math.Max(0m, (cppUncapped - recurringCppUncapped)) * (1 - enhancedShare);
        decimal bonusEi = Math.Max(0m, eiUncapped - recurringEiUncapped);

        decimal federalAnnual = Math.Max(0, FederalTaxForYear(annual, annualCpp, annualEi, input, rates));
        decimal provincialAnnual = Math.Max(0, ProvincialTaxForYear(annual, annualCpp, annualEi, input, rates, province));

        decimal federal = Round(federalAnnual / periods);
        decimal provincial = Round(provincialAnnual / periods);

        // T4127's TB: tax on the bonus is the annual tax WITH it less the annual tax WITHOUT
        // it, taken whole rather than divided by the number of periods, because the bonus is
        // paid once. Everything else about the period is untouched, including the K2 credit,
        // which the guide leaves on the same annualised contribution for both steps.
        if (currentBonus > 0)
        {
            // Both of T4127's bonus steps put year-to-date bonuses (B1) into A: step 1 with the
            // payment being made now, step 2 without it. The difference is the tax on this
            // payment, and B1 cancels except for the bracket it pushes the bonus into, which is
            // the whole reason it is there.
            decimal bonusBase = annual + priorBonuses;
            decimal withBonus = bonusBase + currentBonus;

            if (withBonus <= rates.Federal.FlatBonusCeiling)
            {
                // CRA replaces the whole calculation with a flat rate at very low annual
                // income. Stated as one combined rate rather than a federal and a provincial
                // part, so it is withheld as federal tax: below this ceiling the formula
                // produces zero on both sides, and the T4 reports a single combined figure in
                // box 22 anyway. Total remittance is the same either way.
                federal += Round(currentBonus * rates.Federal.FlatBonusRate);
            }
            else
            {
                // Against the step 2 figure, not the periodic one. They are only the same when
                // there are no prior bonuses.
                decimal bonusCppCredit = Math.Min(annualCpp + bonusCpp,
                    rates.Cpp.MaxContributionEmployee * (1 - enhancedShare));
                decimal bonusEiCredit = Math.Min(annualEi + bonusEi, rates.Ei.MaxPremiumEmployee);

                decimal federalBase =
                    Math.Max(0, FederalTaxForYear(bonusBase, annualCpp, annualEi, input, rates));
                decimal provincialBase =
                    Math.Max(0, ProvincialTaxForYear(bonusBase, annualCpp, annualEi, input, rates, province));

                federal += Round(
                    Math.Max(0, FederalTaxForYear(withBonus, bonusCppCredit, bonusEiCredit, input, rates))
                    - federalBase);
                provincial += Round(
                    Math.Max(0, ProvincialTaxForYear(withBonus, bonusCppCredit, bonusEiCredit, input, rates, province))
                    - provincialBase);
            }
        }

        return new PayrollDeductions
        {
            GrossPay = gross,
            CppEmployee = cpp,
            CppEmployer = cpp,
            Cpp2Employee = cpp2,
            Cpp2Employer = cpp2,
            EiEmployee = ei,
            EiEmployer = Round(ei * rates.Ei.EmployerMultiplier),
            FederalTax = federal,
            ProvincialTax = provincial,
        };
    }

    /// <summary>
    /// A period that pays a bonus and nothing else.
    ///
    /// Income tax is what the bonus would add to a period of regular pay: the tax on the regular
    /// pay with the bonus, less the tax on the regular pay alone. Working it from this period's
    /// own figures annualised a regular pay of zero, so the bonus was taxed as though it were the
    /// employee's whole year and mostly vanished under the personal amount.
    ///
    /// Contributions are still charged on what this period pays. The regular pay is borrowed only
    /// to place the bonus in the right bracket, and was never paid in this run.
    /// </summary>
    private static PayrollDeductions CalculateBonusOnly(PayrollInput input, PayrollYearToDate ytd, PayrollRateTable rates)
    {
        decimal bonus = input.GrossPay;
        decimal regular = input.RegularPayPerPeriod;

        PayrollDeductions actual = Calculate(input.WithPay(bonus, bonus), ytd, rates);
        PayrollDeductions alone = Calculate(input.WithPay(regular, 0m), ytd, rates);
        PayrollDeductions together = Calculate(input.WithPay(regular + bonus, bonus), ytd, rates);

        actual.FederalTax = Math.Max(0m, together.FederalTax - alone.FederalTax);
        actual.ProvincialTax = Math.Max(0m, together.ProvincialTax - alone.ProvincialTax);
        return actual;
    }

    /// <summary>
    /// Splits one period's pay into the annualised recurring part and the one-off part, the way
    /// T4127's bonus steps do.
    ///
    /// Shared with the Quebec calculator, because the split itself is arithmetic on the pay
    /// rather than anything jurisdictional: only the tax formula applied to the two results
    /// differs.
    /// </summary>
    /// <param name="additionalContributions">
    /// The pension contributions that are relieved as a DEDUCTION rather than a credit: the
    /// enhanced portion of CPP or QPP, plus all of CPP2 or QPP2.
    /// </param>
    /// <returns>
    /// The annualised regular income, the taxable bonus payable now, and the bonuses already
    /// paid this year. T4127 calls these [P x (I - F5A)], (B - F5B) and (B1 - F5BYTD).
    /// </returns>
    internal static (decimal PeriodicAnnual, decimal CurrentBonus, decimal PriorBonuses) SplitForBonus(
        PayrollInput input, PayrollYearToDate ytd, decimal gross, int periods, decimal additionalContributions)
    {
        decimal bonus = Math.Clamp(input.NonPeriodicPay, 0m, Math.Max(0m, gross));
        decimal regular = gross - bonus;

        // F5A and F5B. The deduction for the additional pension contributions is split between
        // the recurring and the one-off pay in proportion to pensionable income, which is
        // T4127 verbatim:  F5A = F5 x ((PI - B) / PI)  and  F5B = F5 x (B / PI).
        //
        // It matters more than its size suggests: F5A is multiplied by the number of pay
        // periods and F5B is not, so charging the bonus's share to the periodic side would
        // annualise a deduction that was only ever taken once.
        decimal f5b = gross > 0 ? Round(additionalContributions * (bonus / gross)) : 0m;
        decimal f5a = additionalContributions - f5b;

        return (
            Math.Max(0m, (regular - f5a) * periods),
            Math.Max(0m, bonus - f5b),
            Math.Max(0m, ytd.NonPeriodicPay));
    }

    /// <summary>
    /// Base CPP for the period, and second additional CPP as an out parameter.
    ///
    /// The basic exemption is annual and spread across pay periods. Contributions stop once
    /// the annual maximum is reached, which is why the year-to-date figure is required rather
    /// than optional.
    /// </summary>
    private static decimal CppForPeriod(
        decimal gross, int periods, PayrollYearToDate ytd, PayrollRateTable rates, bool exempt,
        out decimal cpp2, out decimal cppUncapped)
    {
        cpp2 = 0m;
        cppUncapped = 0m;
        if (exempt || gross <= 0)
        {
            return 0m;
        }

        decimal periodExemption = rates.Cpp.BasicExemptionAnnual / periods;
        decimal pensionable = Math.Max(0, gross - periodExemption);

        // Reported UNCAPPED, before the remaining annual room is applied. What is deducted
        // this period is capped; what the K2 credit needs to annualise is not. See the note
        // on annualCpp at the call site.
        cppUncapped = pensionable * rates.Cpp.RateEmployee;

        decimal cpp = Round(cppUncapped);
        decimal remaining = Math.Max(0, rates.Cpp.MaxContributionEmployee - ytd.CppEmployee);
        cpp = Math.Min(cpp, remaining);

        // CPP2 applies to earnings between the two ceilings, so it only begins once the
        // employee's pensionable earnings for the year pass the first ceiling.
        decimal earnedBefore = ytd.PensionableEarnings;
        decimal earnedAfter = earnedBefore + gross;
        decimal above = Math.Max(0, Math.Min(earnedAfter, rates.Cpp2.YampeCeiling) - Math.Max(earnedBefore, rates.Cpp.YmpeCeiling));

        if (above > 0)
        {
            decimal remaining2 = Math.Max(0, rates.Cpp2.MaxContributionEmployee - ytd.Cpp2Employee);
            cpp2 = Math.Min(Round(above * rates.Cpp2.RateEmployee), remaining2);
        }

        return cpp;
    }

    private static decimal EiForPeriod(decimal gross, PayrollYearToDate ytd, PayrollRateTable rates,
                                       bool exempt, out decimal uncapped)
    {
        uncapped = 0m;
        if (exempt || gross <= 0)
        {
            return 0m;
        }

        // Capped on the premium rather than on remaining insurable earnings. Those two are
        // only equivalent when the year-to-date figures agree perfectly, and they rarely do
        // in real records. CRA's calculator caps the premium, so the last pay period of the
        // year lands exactly on the annual maximum.
        decimal premium = Round(gross * rates.Ei.RateEmployee);
        uncapped = premium;

        decimal remaining = Math.Max(0, rates.Ei.MaxPremiumEmployee - ytd.EiEmployee);
        return Math.Min(premium, remaining);
    }

    /// <summary>
    /// Annual federal tax. T4127 expresses this as T3 = (R x A) - K - K1 - K2 - K4.
    ///
    /// This shape is confirmed against CRA's published worked example, which it reproduces to
    /// the cent, including K1 as the lowest rate times the basic personal amount and K4 as the
    /// lowest rate times the Canada Employment Amount.
    /// </summary>
    private static decimal FederalTaxForYear(
        decimal annual, decimal annualCpp, decimal annualEi, PayrollInput input, PayrollRateTable rates)
    {
        FederalRates federal = rates.Federal;
        (decimal rate, decimal k) = BracketFor(federal.Brackets, annual);
        decimal lowest = federal.LowestRateForCredits;

        // The employee's TD1 claim, or the full basic personal amount when none is on file.
        // Deliberately not phased down for income: an employer applies the figure the employee
        // wrote on their TD1, and reflecting the high-income reduction is the employee's job
        // when completing that form. CRA's own calculator behaves this way.
        decimal claim = ClaimOrBasic(input.FederalClaimAmount, input.FederalClaimIsZero,
                                     federal.BasicPersonalAmount.Maximum);

        decimal k1 = lowest * claim;
        decimal k2 = lowest * (annualCpp + annualEi);
        decimal k4 = lowest * Math.Min(annual, federal.CanadaEmploymentAmount);

        return rate * annual - k - k1 - k2 - k4;
    }

    /// <summary>
    /// Annual provincial tax, the same shape as federal with provincial constants, then
    /// Ontario's surtax and health premium added and any tax reduction subtracted.
    /// </summary>
    private static decimal ProvincialTaxForYear(
        decimal annual, decimal annualCpp, decimal annualEi,
        PayrollInput input, PayrollRateTable rates, ProvincialRates province)
    {
        (decimal rate, decimal k) = BracketFor(province.Brackets, annual);
        decimal lowest = province.Brackets.Count > 0 ? province.Brackets[0].Rate : 0m;

        decimal claim = ClaimOrBasic(input.ProvincialClaimAmount, input.ProvincialClaimIsZero,
                                     province.BasicPersonalAmount.Maximum);

        // Yukon alone grants a provincial Canada Employment Amount. Elsewhere the amount is
        // zero, so this term is zero and costs nothing.
        decimal employmentCredit = lowest * Math.Min(annual, province.CanadaEmploymentAmount);

        decimal tax = rate * annual - k - lowest * claim - lowest * (annualCpp + annualEi) - employmentCredit;
        tax = Math.Max(0, tax);

        // Ontario charges its surtax on provincial tax rather than on income, so it comes
        // after. Every band is measured against the ORIGINAL tax: CRA's formula is
        // 0.20 x (T4 - 5,818) + 0.36 x (T4 - 7,446), both terms reading the same T4.
        // Accumulating into `tax` inside the loop would feed the first band's result into the
        // second and overstate the surtax.
        if (province.Surtax is { } surtax)
        {
            decimal basicTax = tax;
            decimal surtaxDue = 0m;

            for (int i = 0; i < surtax.Thresholds.Count && i < surtax.Rates.Count; i++)
            {
                if (basicTax > surtax.Thresholds[i])
                {
                    surtaxDue += (basicTax - surtax.Thresholds[i]) * surtax.Rates[i];
                }
            }

            tax += surtaxDue;
        }

        // The low-income reduction, which applies to tax plus surtax and can only cancel tax
        // owing, never create a refund. Two provinces have one and they are different shapes.
        if (province.TaxReduction is { } reduction)
        {
            decimal credit;

            if (reduction.PhaseOutStart > 0)
            {
                // British Columbia: a flat credit that tapers away over an income band, and
                // stops entirely above the legislated maximum.
                credit = annual > reduction.PhaseOutEnd
                    ? 0m
                    : reduction.Basic - Math.Max(0, annual - reduction.PhaseOutStart) * reduction.PhaseOutRate;
            }
            else
            {
                // Ontario: twice the personal amount, less the tax already worked out, so the
                // credit runs out once tax passes twice that amount.
                credit = (reduction.Basic + reduction.PerDependant * input.Dependants) * 2 - tax;
            }

            tax -= Math.Max(0, Math.Min(tax, credit));
        }

        // Added last and deliberately after the reduction: CRA states the health premium is not
        // reduced by the Ontario tax reduction.
        if (province.HealthPremium is { Count: > 0 } bands)
        {
            tax += HealthPremiumFor(annual, bands);
        }

        return tax;
    }

    /// <summary>
    /// The TD1 claim, or the basic personal amount when none was filed. Shared with the Quebec
    /// calculator. A zero amount means no TD1 unless the form itself claims nothing.
    /// </summary>
    internal static decimal ClaimOrBasic(decimal claim, bool claimsZero, decimal basic) =>
        claim > 0 ? claim : claimsZero ? 0m : basic;

    private static decimal HealthPremiumFor(decimal annual, List<HealthPremiumBand> bands)
    {
        foreach (HealthPremiumBand band in bands)
        {
            bool inBand = annual > band.IncomeOver && (band.IncomeUpTo == null || annual <= band.IncomeUpTo);
            if (!inBand)
            {
                continue;
            }

            decimal premium = band.Premium + (annual - band.IncomeOver) * band.RateOnExcess;
            return Math.Min(premium, band.MaxPremium);
        }

        return 0m;
    }

    /// <summary>The rate and constant for the bracket containing this annual income.</summary>
    private static (decimal Rate, decimal ConstantK) BracketFor(List<TaxBracket> brackets, decimal annual)
    {
        foreach (TaxBracket bracket in brackets)
        {
            if (bracket.UpTo == null || annual <= bracket.UpTo)
            {
                return (bracket.Rate, bracket.ConstantK);
            }
        }

        return brackets.Count > 0
            ? (brackets[^1].Rate, brackets[^1].ConstantK)
            : (0m, 0m);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>What is known about one employee for one pay period.</summary>
public class PayrollInput
{
    public decimal GrossPay { get; set; }

    /// <summary>Two letter province of employment, which decides the tax table.</summary>
    public string Province { get; set; } = string.Empty;

    /// <summary>52 weekly, 26 biweekly, 24 semi-monthly, 12 monthly.</summary>
    public int PayPeriodsPerYear { get; set; } = 26;

    /// <summary>TD1 total. Zero means use the basic personal amount.</summary>
    public decimal FederalClaimAmount { get; set; }

    /// <summary>TD1P total. Zero means use the basic personal amount.</summary>
    public decimal ProvincialClaimAmount { get; set; }

    /// <summary>The TD1 claims nothing, so a zero <see cref="FederalClaimAmount"/> is a real zero.</summary>
    public bool FederalClaimIsZero { get; set; }

    /// <summary>The provincial TD1 claims nothing.</summary>
    public bool ProvincialClaimIsZero { get; set; }

    /// <summary>
    /// T4127's B: the part of <see cref="GrossPay"/> that is a bonus, retroactive pay increase,
    /// vacation pay for vacation not taken, accumulated overtime or any other payment that does
    /// not recur every period.
    ///
    /// Part of gross rather than on top of it, because CPP, CPP2 and EI are charged on the
    /// whole amount regardless. Only income tax cares about the split, and it cares a great
    /// deal: the rest of the period is annualised and this is not.
    /// </summary>
    public decimal NonPeriodicPay { get; set; }

    /// <summary>
    /// What the employee is normally paid for a period, bonuses excluded. Read only when this
    /// period pays a bonus and nothing else, because T4127 taxes a bonus on top of the regular
    /// annual income and a run with no regular pay has none to annualise.
    /// </summary>
    public decimal RegularPayPerPeriod { get; set; }

    /// <summary>Only used by provinces whose tax reduction has a dependant component.</summary>
    public int Dependants { get; set; }

    public bool IsCppExempt { get; set; }

    public bool IsEiExempt { get; set; }

    /// <summary>The same employee and period with different pay and no borrowed regular pay.</summary>
    internal PayrollInput WithPay(decimal gross, decimal nonPeriodic)
    {
        var copy = (PayrollInput)MemberwiseClone();
        copy.GrossPay = gross;
        copy.NonPeriodicPay = nonPeriodic;
        copy.RegularPayPerPeriod = 0m;
        return copy;
    }
}

/// <summary>
/// Totals for the calendar year before this pay period. Required rather than optional,
/// because CPP, CPP2 and EI all stop at annual maximums and cannot be computed correctly
/// from a single period in isolation.
/// </summary>
public class PayrollYearToDate
{
    public decimal PensionableEarnings { get; set; }

    public decimal InsurableEarnings { get; set; }

    public decimal CppEmployee { get; set; }

    public decimal Cpp2Employee { get; set; }

    public decimal EiEmployee { get; set; }

    /// <summary>Quebec only. Needed because QPIP stops at its own annual maximum.</summary>
    public decimal QpipEmployee { get; set; }

    public decimal QpipEmployer { get; set; }

    /// <summary>
    /// T4127's B1: bonuses and other non-periodic payments already made this year, before this
    /// period.
    ///
    /// Needed because a second bonus must be taxed on top of the first rather than as though it
    /// were the only one. Without it, two $5,000 bonuses in a year are each taxed as the first,
    /// and the employee is under-withheld on the second.
    /// </summary>
    public decimal NonPeriodicPay { get; set; }
}

/// <summary>
/// The result for one employee for one pay period. These values are stored on the pay run
/// when it is approved and never recalculated, so that a historical run always agrees with
/// the stub the employee was given.
/// </summary>
public class PayrollDeductions
{
    public decimal GrossPay { get; set; }

    public decimal CppEmployee { get; set; }

    public decimal CppEmployer { get; set; }

    public decimal Cpp2Employee { get; set; }

    public decimal Cpp2Employer { get; set; }

    public decimal EiEmployee { get; set; }

    public decimal EiEmployer { get; set; }

    /// <summary>Quebec parental insurance plan. Zero everywhere outside Quebec.</summary>
    public decimal QpipEmployee { get; set; }

    public decimal QpipEmployer { get; set; }

    public decimal FederalTax { get; set; }

    public decimal ProvincialTax { get; set; }

    /// <summary>What the employee receives.</summary>
    public decimal NetPay =>
        GrossPay - CppEmployee - Cpp2Employee - EiEmployee - QpipEmployee - FederalTax - ProvincialTax;

    /// <summary>What must be remitted to CRA for this employee: withheld plus employer share.</summary>
    public decimal TotalRemittance =>
        CppEmployee + CppEmployer + Cpp2Employee + Cpp2Employer
        + EiEmployee + EiEmployer + QpipEmployee + QpipEmployer + FederalTax + ProvincialTax;

    /// <summary>What this employee actually costs, gross plus the employer contributions.</summary>
    public decimal TotalCost =>
        GrossPay + CppEmployer + Cpp2Employer + EiEmployer + QpipEmployer;
}
