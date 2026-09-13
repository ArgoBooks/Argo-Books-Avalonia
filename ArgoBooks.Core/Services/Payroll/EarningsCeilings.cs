using ArgoBooks.Core.Models.Payroll;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// The annual ceilings a year end slip reports earnings against.
///
/// A slip does not report gross pay in the insurable and pensionable boxes. It reports the
/// portion that was actually subject to the deduction, which for anyone earning above a ceiling
/// is less than what they were paid: they stopped contributing part way through the year.
///
/// This is the kind of error that never announces itself. A slip showing $140,000 of insurable
/// earnings looks entirely reasonable, and nothing downstream objects until CRA compares the
/// premium against the earnings and finds the employer short.
///
/// Shared by the T4 and the RL-1 so the two filings cannot disagree about the same person.
/// </summary>
public sealed class EarningsCeilings
{
    private readonly decimal _ei;
    private readonly decimal _pensionable;
    private readonly decimal _qpip;

    private EarningsCeilings(decimal ei, decimal pensionable, decimal qpip)
    {
        _ei = ei;
        _pensionable = pensionable;
        _qpip = qpip;
    }

    /// <summary>
    /// Every ceiling zero, meaning no capping at all. Used when no rate edition covers the year,
    /// which happens for a year filed before its tables shipped or after they were pruned.
    /// Reporting gross is then the honest answer: it is what was paid, and inventing a ceiling
    /// from a neighbouring year would silently understate a real figure.
    /// </summary>
    public static EarningsCeilings None { get; } = new(0m, 0m, 0m);

    /// <summary>
    /// The ceilings in force for a tax year.
    ///
    /// Read from the DECEMBER edition. Ceilings are annual and do not move mid-year even when
    /// CRA publishes a July edition, but taking the last edition of the year is the reading that
    /// stays correct if one ever does.
    /// </summary>
    public static EarningsCeilings For(PayrollRateService rates, int taxYear)
    {
        ArgumentNullException.ThrowIfNull(rates);

        PayrollRateTable? table = rates.GetForDate(new DateTime(taxYear, 12, 31));

        return table == null
            ? None
            : new EarningsCeilings(
                table.Ei?.MaxInsurableEarnings ?? 0m,
                table.Cpp2?.YampeCeiling ?? 0m,
                table.Quebec?.Qpip?.MaxInsurableEarnings ?? 0m);
    }

    /// <summary>Box 24.</summary>
    public decimal CapEi(decimal gross) => Cap(gross, _ei);

    /// <summary>
    /// Box 26, and box G on the RL-1. Capped at the SECOND ceiling, the YAMPE.
    ///
    /// This is the opposite of what it looks like it should be. The intuition is that earnings between the two ceilings belong to
    /// CPP2 and are reported in box 16A, so box 26 should stop at the first ceiling. Both
    /// agencies say otherwise, in as many words:
    ///
    /// RC4120: report the pensionable earnings used to calculate the contributions in boxes 16,
    /// 16A, 17 and 17A "up to the additional maximum pensionable earnings for the year ($81,200
    /// for 2025)", which is the YAMPE and not the YMPE.
    ///
    /// RL-1.G-V section 5.9: the maximum for box G is the maximum pensionable earnings if an
    /// amount is entered in box B.A only, and the ADDITIONAL maximum if amounts are entered in
    /// boxes B.A and B.B.
    ///
    /// The RL-1's condition collapses into the same single ceiling here. Anyone paid above the
    /// YMPE has QPP2 withheld and so has an amount in box B.B, which selects the YAMPE; anyone
    /// paid below it is under both ceilings and the choice cannot bind. Someone exempt reports
    /// nil either way.
    ///
    /// Capping at the YMPE understates the box for every employee paid between the two, which
    /// is the population that has CPP2 in box 16A. CRA's PIER review then compares a CPP2
    /// contribution against earnings that do not reach the range it is charged on.
    /// </summary>
    public decimal CapPensionable(decimal gross) => Cap(gross, _pensionable);

    /// <summary>Box 56, and box I on the RL-1.</summary>
    public decimal CapQpip(decimal gross) => Cap(gross, _qpip);

    private static decimal Cap(decimal gross, decimal ceiling) =>
        ceiling <= 0m ? gross : Math.Min(gross, ceiling);
}
