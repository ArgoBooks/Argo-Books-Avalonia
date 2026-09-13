namespace ArgoBooks.Core.Models.Payroll;

/// <summary>
/// Block 16, the reason the ROE is being issued.
///
/// This is the one block the app cannot work out. It knows somebody stopped being paid; it does
/// not know whether they quit, were dismissed or went on leave, and those are different legal
/// statements with different consequences for the claim. So it is asked, never defaulted.
///
/// ROE Web takes a three character separation code rather than the single letter printed on the
/// paper form. The base code for each reason is the letter followed by "00"; the sub-codes
/// narrow it further and are not offered here, because picking the wrong one is worse than
/// picking the general one and the general one is always valid.
/// </summary>
public enum RoeReason
{
    /// <summary>A. The default for a layoff, and by far the most common.</summary>
    ShortageOfWork,

    /// <summary>B.</summary>
    StrikeOrLockout,

    /// <summary>C.</summary>
    ReturnToSchool,

    /// <summary>D.</summary>
    IllnessOrInjury,

    /// <summary>E.</summary>
    Quit,

    /// <summary>F.</summary>
    Maternity,

    /// <summary>G. Mandatory retirement only. An ordinary retirement is a quit or a shortage of work.</summary>
    Retirement,

    /// <summary>H.</summary>
    WorkSharing,

    /// <summary>J.</summary>
    ApprenticeTraining,

    /// <summary>K. Service Canada asks that this be used only when nothing else fits, and that
    /// block 18 explain it. A comment also sends the ROE to manual review.</summary>
    Other,

    /// <summary>M.</summary>
    DismissalOrSuspension,

    /// <summary>N.</summary>
    LeaveOfAbsence,

    /// <summary>P.</summary>
    Parental,

    /// <summary>Z.</summary>
    CompassionateCare,
}

/// <summary>Block 14, whether the employee is expected back.</summary>
public enum RoeRecall
{
    /// <summary>U. Service Canada's default when the employer does not know.</summary>
    Unknown,

    /// <summary>N.</summary>
    NotReturning,

    /// <summary>Y. Requires a date.</summary>
    ExpectedDate,
}

/// <summary>Which language Service Canada prints the ROE and writes to the employee in.</summary>
public enum RoeLanguage
{
    English,
    French,
}

public static class RoeCodes
{
    /// <summary>Block 16. The three character separation code ROE Web expects.</summary>
    public static string Separation(RoeReason reason) => reason switch
    {
        RoeReason.ShortageOfWork => "A00",
        RoeReason.StrikeOrLockout => "B00",
        RoeReason.ReturnToSchool => "C00",
        RoeReason.IllnessOrInjury => "D00",
        RoeReason.Quit => "E00",
        RoeReason.Maternity => "F00",
        RoeReason.Retirement => "G00",
        RoeReason.WorkSharing => "H00",
        RoeReason.ApprenticeTraining => "J00",
        RoeReason.Other => "K00",
        RoeReason.DismissalOrSuspension => "M00",
        RoeReason.LeaveOfAbsence => "N00",
        RoeReason.Parental => "P00",
        RoeReason.CompassionateCare => "Z00",
        _ => "K00",
    };

    /// <summary>Block 14.</summary>
    public static string Recall(RoeRecall recall) => recall switch
    {
        RoeRecall.NotReturning => "N",
        RoeRecall.ExpectedDate => "Y",
        _ => "U",
    };

    /// <summary>Block 20, and the ROE element's PrintingLanguage attribute.</summary>
    public static string Language(RoeLanguage language) => language == RoeLanguage.French ? "F" : "E";

    /// <summary>
    /// Block 6, the pay period type.
    ///
    /// ROE Web separates the standard forms from the non-standard ones (O and E), which apply
    /// where the period does not end on the same day each month. Argo Books only produces
    /// standard periods, so those two are never emitted. Thirteen periods a year is not a
    /// frequency this app offers either; if it is ever added, the code is H.
    /// </summary>
    public static string PayPeriodType(PayFrequency frequency) => frequency switch
    {
        PayFrequency.Weekly => "W",
        PayFrequency.Biweekly => "B",
        PayFrequency.SemiMonthly => "S",
        PayFrequency.Monthly => "M",
        _ => "B",
    };
}
