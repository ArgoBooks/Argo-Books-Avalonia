using ArgoBooks.Core.Models.Payroll;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Source deductions for an employee working in Quebec, following Revenu Quebec's TP-1015.F.
///
/// A separate calculator rather than a branch inside the federal one, because Quebec is not a
/// province-shaped problem. Three things differ in kind, not degree:
///
/// The pension plan is QPP, at 6.30% split as a 5.30% base plus a 1.00% first additional
/// contribution. CPP is 5.95% split 4.95 and 1.00, so neither the rate nor the split carries
/// over.
///
/// There is a parental insurance plan, QPIP, with no equivalent anywhere else in Canada, and
/// Quebec employees pay a LOWER EI rate because of it.
///
/// The income tax formula is not CRA's shape at all. Quebec has a deduction for workers that
/// comes off income, relieves personal credits at a stated rate rather than at the lowest
/// bracket rate, and gives no credit for QPP or QPIP: the additional QPP is relieved as a
/// deduction instead. Writing this by analogy with the federal formula would produce numbers
/// that look reasonable and are wrong.
///
/// CRA reduces federal tax by an abatement for Quebec residents, to make room for Quebec's own
/// income tax. That is applied here rather than federally, because it only ever applies here.
/// </summary>
public static class QuebecPayrollCalculator
{
    public static PayrollDeductions Calculate(PayrollInput input, PayrollYearToDate ytd, PayrollRateTable rates)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(ytd);
        ArgumentNullException.ThrowIfNull(rates);

        if (rates.Quebec is not { } qc)
        {
            throw new NotSupportedException(
                $"Edition {rates.EditionId} carries no Quebec rates, so a Quebec pay run cannot be calculated.");
        }

        int periods = input.PayPeriodsPerYear;
        decimal gross = input.GrossPay;

        decimal qpp = QppForPeriod(gross, periods, ytd, qc, input.IsCppExempt,
                                   out decimal qpp2, out decimal qppUncapped);
        decimal qpip = QpipForPeriod(gross, ytd, qc, out decimal qpipUncapped);
        decimal ei = EiForPeriod(gross, ytd, rates, qc, input.IsEiExempt, out decimal eiUncapped);

        decimal quebecTax = QuebecTaxForPeriod(gross, periods, qpp, qpp2, input, ytd, qc);
        decimal federalTax = FederalTaxForPeriod(
            gross, periods, qpp, qpp2, qppUncapped, eiUncapped, qpipUncapped, input, ytd, rates, qc);

        return new PayrollDeductions
        {
            GrossPay = gross,
            CppEmployee = qpp,
            CppEmployer = qpp,
            Cpp2Employee = qpp2,
            Cpp2Employer = qpp2,
            EiEmployee = ei,
            EiEmployer = Round(ei * rates.Ei.QuebecEmployerMultiplier),
            QpipEmployee = qpip,
            QpipEmployer = QpipEmployerForPeriod(gross, ytd, qc),
            FederalTax = federalTax,
            ProvincialTax = quebecTax,
        };
    }

    /// <summary>
    /// Quebec income tax for one period. TP-1015.F works annually and divides at the end:
    ///
    ///   I = P x (G - F - H - CSA)
    ///   Y = (T x I) - K - (creditRate x E)
    ///   A = Y / P
    ///
    /// F, J, J1, K1, Q and Q1 are pension contributions, Revenu Quebec authorisations and
    /// labour-sponsored fund share purchases. This app collects none of them, so they are zero
    /// and left out rather than carried as dead terms.
    ///
    /// A bonus is not annualised, for the same reason it is not federally. Revenu Quebec states
    /// the rule the other way round from CRA: below a threshold on annual remuneration
    /// INCLUDING the bonus, withhold a flat rate; above it, work the tax out with and without
    /// the bonus and take the difference.
    /// </summary>
    private static decimal QuebecTaxForPeriod(
        decimal gross, int periods, decimal qpp, decimal qpp2, PayrollInput input,
        PayrollYearToDate ytd, QuebecRates qc)
    {
        if (gross <= 0)
        {
            return 0m;
        }

        // H, the deduction for workers: a share of pay, capped for the YEAR and so divided
        // across periods rather than capped per period.
        decimal workerDeduction = Math.Min(
            qc.WorkerDeductionRate * gross,
            periods > 0 ? qc.WorkerDeductionMaxAnnual / periods : 0m);

        // CSA, the deductible part of QPP. Only the first additional contribution comes off
        // income, which is the 1.00 percentage point inside the 6.30% rate, plus all of QPP2.
        decimal additionalShare = qc.Qpp.RateEmployee > 0
            ? (qc.Qpp.RateEmployee - qc.Qpp.BaseRateEmployee) / qc.Qpp.RateEmployee
            : 0m;
        decimal deductibleQpp = Round(qpp * additionalShare) + qpp2;

        // Both H and CSA are period-level deductions charged on the whole of this period's pay,
        // so both are split between the recurring and the one-off part in proportion to it.
        // T4127 spells that split out for the federal equivalent of CSA; TP-1015.F does not
        // state it for H, and splitting is the only reading that does not annualise a deduction
        // taken once.
        (decimal periodicAnnual, decimal bonus, decimal priorBonuses) =
            PayrollCalculator.SplitForBonus(input, ytd, gross, periods, deductibleQpp + workerDeduction);

        // Year-to-date bonuses stay out of the periodic base. They belong to the bonus
        // calculation below, on both sides of its subtraction, and folding them in here re-taxed
        // a bonus already taxed in full when it was paid, every period for the rest of the year.
        decimal annual = periodicAnnual;
        decimal annualTax = AnnualQuebecTax(annual, input, qc);
        decimal tax = Round(annualTax / periods);

        if (bonus > 0)
        {
            decimal bonusBase = annual + priorBonuses;
            decimal withBonus = bonusBase + bonus;

            tax += withBonus <= qc.FlatBonusCeiling
                ? Round(bonus * qc.FlatBonusRate)
                : Round(AnnualQuebecTax(withBonus, input, qc) - AnnualQuebecTax(bonusBase, input, qc));
        }

        return tax;
    }

    private static decimal AnnualQuebecTax(decimal annual, PayrollInput input, QuebecRates qc)
    {
        (decimal rate, decimal k) = BracketFor(qc.Brackets, annual);

        decimal claim = PayrollCalculator.ClaimOrBasic(
            input.ProvincialClaimAmount, input.ProvincialClaimIsZero, qc.BasicPersonalAmount);

        return Math.Max(0, rate * annual - k - qc.CreditRate * claim);
    }

    /// <summary>
    /// Federal tax for a Quebec employee: CRA's own formula, then reduced by the abatement.
    ///
    /// The credit is T4127's K2Q, which is not K2 with QPP substituted for CPP. It has THREE
    /// terms where K2 has two, and the guide spells all three out:
    ///
    ///   K2Q = [(rate x (P x C x (base/total), maximum ...))
    ///        + (rate x (P x EI, maximum ...))
    ///        + (rate x (P x IE x qpipRate, maximum ...))]
    ///
    /// The third is QPIP. It has no federal equivalent anywhere else in Canada, which is
    /// precisely why it is easy to leave out, and leaving it out over-withholds federal tax from
    /// every Quebec employee for the whole year.
    /// </summary>
    /// <param name="eiUncapped">
    /// The period's EI premium BEFORE the remaining annual room is applied, and likewise for
    /// <paramref name="qpipUncapped"/> and <paramref name="qppUncapped"/>. T4127 is explicit
    /// that once a maximum is reached, the annualised term "is replaced by the employee's
    /// maximum annual contribution or premium ... to ensure that the employee will get the
    /// maximum CPP, EI, and QPIP tax credit for the rest of the pay periods in the year."
    ///
    /// Annualising what was actually deducted does the opposite: it collapses to nothing in the
    /// period the ceiling is reached and stays there, so the credit vanishes and tax jumps for
    /// the rest of the year. Annualising the uncapped figure and capping the ANNUAL total is
    /// what the guide asks for.
    /// </param>
    private static decimal FederalTaxForPeriod(
        decimal gross, int periods, decimal qpp, decimal qpp2, decimal qppUncapped,
        decimal eiUncapped, decimal qpipUncapped,
        PayrollInput input, PayrollYearToDate ytd, PayrollRateTable rates, QuebecRates qc)
    {
        if (gross <= 0)
        {
            return 0m;
        }

        decimal additionalShare = qc.Qpp.RateEmployee > 0
            ? (qc.Qpp.RateEmployee - qc.Qpp.BaseRateEmployee) / qc.Qpp.RateEmployee
            : 0m;

        decimal enhancedQpp = Round(qpp * additionalShare);

        (decimal periodicAnnual, decimal bonus, decimal priorBonuses) =
            PayrollCalculator.SplitForBonus(input, ytd, gross, periods, enhancedQpp + qpp2);

        // As above: the periodic base excludes year-to-date bonuses.
        decimal annual = periodicAnnual;

        decimal annualQpp = Math.Min(qppUncapped * periods, qc.Qpp.MaxContributionEmployee) * (1 - additionalShare);
        decimal annualEi = Math.Min(eiUncapped * periods, qc.EiMaxPremiumEmployee);
        decimal annualQpip = Math.Min(qpipUncapped * periods, qc.Qpip.MaxPremiumEmployee);

        decimal annualTax = AnnualFederalTax(annual, annualQpp, annualEi, annualQpip, input, rates, qc);
        decimal tax = Round(annualTax / periods);

        // The federal share of a bonus, on CRA's rule rather than Quebec's: T4127 states the
        // same flat-rate shortcut under the same ceiling, at 10% for Quebec instead of 15%.
        // That 10% is a published figure, not the 15% with the abatement applied to it.
        if (bonus > 0)
        {
            decimal bonusBase = annual + priorBonuses;
            decimal withBonus = bonusBase + bonus;

            tax += withBonus <= rates.Federal.FlatBonusCeiling
                ? Round(bonus * qc.FederalFlatBonusRate)
                : Round(AnnualFederalTax(withBonus, annualQpp, annualEi, annualQpip, input, rates, qc)
                        - AnnualFederalTax(bonusBase, annualQpp, annualEi, annualQpip, input, rates, qc));
        }

        return tax;
    }

    private static decimal AnnualFederalTax(
        decimal annual, decimal annualQpp, decimal annualEi, decimal annualQpip,
        PayrollInput input, PayrollRateTable rates, QuebecRates qc)
    {
        FederalRates federal = rates.Federal;
        (decimal rate, decimal k) = BracketFor(federal.Brackets, annual);
        decimal lowest = federal.LowestRateForCredits;

        decimal claim = PayrollCalculator.ClaimOrBasic(
            input.FederalClaimAmount, input.FederalClaimIsZero, federal.BasicPersonalAmount.Maximum);

        decimal t3 = rate * annual
                     - k
                     - lowest * claim
                     - lowest * (annualQpp + annualEi + annualQpip)
                     - lowest * Math.Min(annual, federal.CanadaEmploymentAmount);

        // The abatement. CRA collects less federal tax from Quebec residents because Quebec
        // collects its own, and it applies to the tax rather than to the income.
        return Math.Max(0, t3) * (1 - qc.FederalAbatement);
    }

    /// <summary>QPP for the period. Same arithmetic as CPP, different constants.</summary>
    private static decimal QppForPeriod(
        decimal gross, int periods, PayrollYearToDate ytd, QuebecRates qc, bool exempt,
        out decimal qpp2, out decimal qppUncapped)
    {
        qpp2 = 0m;
        qppUncapped = 0m;

        if (exempt || gross <= 0)
        {
            return 0m;
        }

        decimal periodExemption = qc.Qpp.BasicExemptionAnnual / periods;
        decimal pensionable = Math.Max(0, gross - periodExemption);

        qppUncapped = pensionable * qc.Qpp.RateEmployee;

        decimal qpp = Round(qppUncapped);
        decimal remaining = Math.Max(0, qc.Qpp.MaxContributionEmployee - ytd.CppEmployee);
        qpp = Math.Min(qpp, remaining);

        decimal earnedBefore = ytd.PensionableEarnings;
        decimal above = Math.Max(0,
            Math.Min(earnedBefore + gross, qc.Qpp2.YampeCeiling) - Math.Max(earnedBefore, qc.Qpp.YmpeCeiling));

        if (above > 0)
        {
            decimal remaining2 = Math.Max(0, qc.Qpp2.MaxContributionEmployee - ytd.Cpp2Employee);
            qpp2 = Math.Min(Round(above * qc.Qpp2.RateEmployee), remaining2);
        }

        return qpp;
    }

    /// <summary>
    /// QPIP. Capped on the premium rather than on insurable earnings, for the same reason EI
    /// is: the two only agree when the year-to-date figures are perfect, and they rarely are.
    ///
    /// Deliberately takes no exemption flag, which looks like an oversight beside QPP and EI and
    /// is not one. Revenu Quebec: "employment that is not insurable under the Employment
    /// Insurance Act is not necessarily excluded employment under the Act respecting parental
    /// insurance ... you must withhold and pay QPIP premiums respecting salary or wages paid to
    /// a shareholder (or a shareholder's spouse) as an employee, REGARDLESS of the number of
    /// shares held by that person."
    ///
    /// So the owner-manager this app marks EI exempt, the one holding more than 40% of the
    /// voting shares, still pays QPIP. Passing IsEiExempt through here would stop withholding
    /// from exactly the person who owes it.
    ///
    /// The $2,000 annual threshold is likewise not applied here. Revenu Quebec is explicit that
    /// it is settled on the employee's own return: "regardless of the $2,000 threshold, you must
    /// start withholding and paying QPIP premiums as soon as you pay the employee one dollar of
    /// eligible salary or wages."
    /// </summary>
    /// <param name="uncapped">
    /// The premium before the remaining annual room is applied. What is withheld this period is
    /// capped; what the K2Q credit annualises is not.
    /// </param>
    private static decimal QpipForPeriod(decimal gross, PayrollYearToDate ytd, QuebecRates qc,
                                         out decimal uncapped)
    {
        uncapped = 0m;

        if (gross <= 0)
        {
            return 0m;
        }

        decimal premium = Round(gross * qc.Qpip.RateEmployee);
        uncapped = premium;

        decimal remaining = Math.Max(0, qc.Qpip.MaxPremiumEmployee - ytd.QpipEmployee);
        return Math.Min(premium, remaining);
    }

    private static decimal QpipEmployerForPeriod(decimal gross, PayrollYearToDate ytd, QuebecRates qc)
    {
        if (gross <= 0)
        {
            return 0m;
        }

        // The employer side has its own rate and its own maximum. It is not the employee
        // premium times a multiplier, which is how EI works and is the easy mistake here.
        decimal premium = Round(gross * qc.Qpip.RateEmployer);
        decimal remaining = Math.Max(0, qc.Qpip.MaxPremiumEmployer - ytd.QpipEmployer);
        return Math.Min(premium, remaining);
    }

    /// <summary>EI at Quebec's reduced rate and maximum, because QPIP covers parental benefits.</summary>
    /// <param name="uncapped">The premium before the remaining annual room is applied.</param>
    private static decimal EiForPeriod(
        decimal gross, PayrollYearToDate ytd, PayrollRateTable rates, QuebecRates qc, bool exempt,
        out decimal uncapped)
    {
        uncapped = 0m;

        if (exempt || gross <= 0)
        {
            return 0m;
        }

        decimal premium = Round(gross * rates.Ei.QuebecRateEmployee);
        uncapped = premium;

        decimal remaining = Math.Max(0, qc.EiMaxPremiumEmployee - ytd.EiEmployee);
        return Math.Min(premium, remaining);
    }

    private static (decimal Rate, decimal ConstantK) BracketFor(List<TaxBracket> brackets, decimal annual)
    {
        foreach (TaxBracket bracket in brackets)
        {
            if (bracket.UpTo == null || annual <= bracket.UpTo)
            {
                return (bracket.Rate, bracket.ConstantK);
            }
        }

        return brackets.Count > 0 ? (brackets[^1].Rate, brackets[^1].ConstantK) : (0m, 0m);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
