using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Builds a year's RL-1 return from the pay runs already recorded.
///
/// Mirrors <see cref="T4Service"/> deliberately, including its refusal to store anything: the
/// RL-1 restates frozen pay runs, so it is assembled on demand and comes out identical every
/// time.
///
/// The one structural difference is who it covers. A T4 is filed for every employee; an RL-1 is
/// filed only for Quebec employees, so a mixed employer's two returns list different people and
/// their totals are supposed to disagree.
/// </summary>
public class Rl1Service(PayrollRateService? rates = null)
{
    private readonly PayrollRateService _rates = rates ?? new PayrollRateService();

    /// <summary>
    /// Assembles the return. Quebec employees only. Drafts are excluded here and refused
    /// outright by <see cref="Validate"/>, for the same reason as the T4.
    /// </summary>
    public Rl1Return Build(CompanyData data, int taxYear)
    {
        ArgumentNullException.ThrowIfNull(data);

        var company = data.Settings.Company;

        var rl1 = new Rl1Return
        {
            TaxYear = taxYear,
            QuebecIdentificationNumber = company.QuebecIdentificationNumber ?? string.Empty,
            EmployerName = company.Name,
            EmployerAddress = new Models.Common.Address
            {
                Street = company.Address ?? string.Empty,
                City = company.City ?? string.Empty,
                State = company.ProvinceState ?? string.Empty,
                Country = string.IsNullOrWhiteSpace(company.Country) ? "CAN" : company.Country,
                ZipCode = company.PostalCode ?? string.Empty,
            },
        };

        EarningsCeilings ceilings = EarningsCeilings.For(_rates, taxYear);

        // Everything except drafts, so a voided run and its reversal both count and cancel to
        // zero. Matches the T4 and the year-to-date figures.
        var lines = data.PayRuns
            .Where(r => r.Status != PayRunStatus.Draft && r.PayDate.Year == taxYear)
            .SelectMany(r => r.Lines)
            .ToList();

        foreach (var group in lines.GroupBy(l => l.EmployeeId))
        {
            Employee? employee = data.Employees.FirstOrDefault(e => e.Id == group.Key);
            if (employee == null)
            {
                continue;
            }

            // The Quebec part of the year only, read off each line rather than from where the
            // employee works now: someone who moved in files for their Quebec pay alone, and
            // someone who moved out still files for the months before they left.
            List<PayRunLine> quebecLines = group.Where(l => IsQuebec(l, employee)).ToList();
            if (quebecLines.Count == 0)
            {
                continue;
            }

            Rl1Slip slip = BuildSlip(employee, quebecLines, ceilings);

            // A slip whose every figure nets to zero is one whose runs were all voided. There
            // is nothing to report.
            if (slip.EmploymentIncome > 0 || slip.QuebecIncomeTax > 0)
            {
                rl1.Slips.Add(slip);
            }
        }

        rl1.Slips.Sort((a, b) => string.Compare(a.Surname, b.Surname, StringComparison.CurrentCultureIgnoreCase));
        return rl1;
    }

    /// <summary>True when this employer has anyone to file an RL-1 for at all.</summary>
    public static bool HasQuebecEmployees(CompanyData data, int taxYear)
    {
        ArgumentNullException.ThrowIfNull(data);

        return data.PayRuns.Any(r => r.Status != PayRunStatus.Draft
                                     && r.PayDate.Year == taxYear
                                     && r.Lines.Any(l => data.Employees.FirstOrDefault(e => e.Id == l.EmployeeId)
                                                             is { } employee
                                                         && IsQuebec(l, employee)));
    }

    private static bool IsQuebec(PayRunLine line, Employee employee) =>
        T4Service.ProvinceOf(line, employee) == "QC";

    private static Rl1Slip BuildSlip(Employee employee, List<PayRunLine> lines, EarningsCeilings ceilings)
    {
        (string surname, string given) = SplitName(employee.Name);

        decimal gross = lines.Sum(l => l.GrossPay);
        decimal qpip = lines.Sum(l => l.QpipEmployee);

        return new Rl1Slip
        {
            EmployeeId = employee.Id,
            Surname = surname,
            GivenName = given,
            Sin = employee.Sin,
            EmployeeNumber = employee.EmployeeNumber,
            Address = employee.Address,
            EmploymentIncome = gross,

            // The pay run stores Quebec's pension contributions in the CPP fields, because they
            // are the same column in the same run and only their destination differs. Boxes B.A
            // and B.B are where they land.
            QppContribution = lines.Sum(l => l.CppEmployee),
            AdditionalQppContribution = lines.Sum(l => l.Cpp2Employee),

            EiPremium = lines.Sum(l => l.EiEmployee),

            // Box E is the QUEBEC tax alone. The federal half was withheld in the same run but
            // it is remitted to CRA and reported on the T4, so putting the combined figure here
            // would have the employee claim a Quebec credit for money Quebec never received.
            QuebecIncomeTax = lines.Sum(l => l.ProvincialTax),

            // Capped at the year's ceilings, for the same reason as the T4's boxes 24 and 26:
            // someone earning above one stopped contributing part way through the year, and
            // reporting their whole salary would have Revenu Quebec expect contributions on
            // money that was never pensionable or eligible.
            QppPensionableSalary = employee.IsCppExempt ? 0m : ceilings.CapPensionable(gross),
            QpipPremium = qpip,

            // Revenu Quebec is explicit that box I takes "0" when there is none rather than
            // being left blank, so the figure is always set even when it is nil.
            //
            // Nil means no QPIP premium was withheld, NOT that the employee is exempt from EI.
            // Those were conflated here, and the two exemptions are not the same one: EI
            // exemption is the owner holding more than 40% of the voting shares, and QPIP has
            // its own rules and its own base, which is why the calculator withholds QPIP from an
            // EI exempt Quebec employee. Reading box H against box I is how Revenu Quebec checks
            // the slip, and a premium in H with a nil I is the one pair that cannot be right.
            QpipEligibleSalary = qpip > 0 ? ceilings.CapQpip(gross) : 0m,

            EmployerQpp = lines.Sum(l => l.CppEmployer),
            EmployerQpp2 = lines.Sum(l => l.Cpp2Employer),
            EmployerQpip = lines.Sum(l => l.QpipEmployer),
        };
    }

    /// <summary>
    /// Everything that would make Revenu Quebec reject the filing, or make it wrong. Returned
    /// as messages rather than thrown, so the year end screen can show them all at once.
    /// </summary>
    public static List<string> Validate(CompanyData data, Rl1Return rl1)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(rl1);

        var problems = new List<string>();

        int drafts = data.PayRuns.Count(r => r.Status == PayRunStatus.Draft && r.PayDate.Year == rl1.TaxYear);
        if (drafts > 0)
        {
            problems.Add($"{drafts} pay run(s) in {rl1.TaxYear} are still drafts. Approve or delete them before filing.");
        }

        if (!IsQuebecIdentificationNumber(rl1.QuebecIdentificationNumber))
        {
            problems.Add("The Revenu Quebec identification number is missing or not in the form 1234567890RS0001. "
                         + "It is on your Revenu Quebec statement and is not the same as your CRA payroll account number.");
        }

        if (string.IsNullOrWhiteSpace(rl1.EmployerName))
        {
            problems.Add("The company name is required on the RL-1 Summary.");
        }

        foreach (Rl1Slip slip in rl1.Slips)
        {
            string who = string.IsNullOrWhiteSpace(slip.Surname) ? slip.EmployeeId : slip.Surname;

            if (new string(slip.Sin.Where(char.IsAsciiDigit).ToArray()).Length != 9)
            {
                problems.Add($"{who} has no social insurance number, which is required on the RL-1.");
            }

            // Required, unlike the T4, where the whole address block is optional. RL-1.G-V
            // section 5.27.1: enter "the individual's last name, followed by the first name and
            // last known address (including the postal code)".
            if (string.IsNullOrWhiteSpace(slip.Address.City))
            {
                problems.Add($"{who} has no address. Revenu Quebec requires one on every RL-1 slip.");
            }
        }

        if (rl1.Slips.Count == 0)
        {
            problems.Add($"There are no approved pay runs for Quebec employees in {rl1.TaxYear}, so there is nothing to file.");
        }

        return problems;
    }

    /// <summary>
    /// What these PDFs are, which is not a filable RL-1.
    ///
    /// Revenu Quebec accepts three routes and this app is none of them: its own online service,
    /// software it has authorized, or software the employer developed that meets its
    /// requirements. A paper slip prepared by software is only accepted when it carries an
    /// authorization number, two letters and seven digits like FS9999999, which Revenu Quebec
    /// issues to a developer per taxation year after certifying the software, plus a
    /// two-dimensional barcode on copy 1. Argo Books holds neither.
    ///
    /// "The RL slip does not have an authorization number" is the first entry on Revenu Quebec's
    /// own list of the most common reasons a slip is rejected.
    ///
    /// This used to be a blocking problem raised only above five slips, on the understanding
    /// that paper was fine below that. It is not: the authorization rule applies to every paper
    /// slip a piece of software prints, so the old message let four slips through with the
    /// implication that mailing them would work. Stated as a permanent notice instead, because
    /// it is a fact about the app rather than a fault in the employer's data, and blocking the
    /// download would take away the figures they still need in order to file properly.
    /// </summary>
    public const string FilingNotice =
        "These are worksheets, not RL-1 slips that can be filed. Revenu Quebec only accepts a "
        + "paper slip printed by software if it carries an authorization number and a barcode "
        + "that Revenu Quebec issues to certified software, and Argo Books has neither. Use "
        + "these figures to complete the RL-1 in My Account for businesses, or hand them to "
        + "your accountant.";

    /// <summary>Ten digits, then two letters, then four digits. Usually RS, sometimes RR.</summary>
    public static bool IsQuebecIdentificationNumber(string? value)
    {
        string v = (value ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

        return v.Length == 16
               && v[..10].All(char.IsAsciiDigit)
               && v[10..12].All(char.IsAsciiLetter)
               && v[12..].All(char.IsAsciiDigit);
    }

    /// <summary>
    /// The RL-1 wants a surname and given name, with no initial field, so this differs from the
    /// T4's split: everything before the last word is the given name rather than being trimmed
    /// to a single initial.
    /// </summary>
    private static (string Surname, string Given) SplitName(string name)
    {
        string[] parts = (name)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (parts[0], string.Empty),
            _ => (parts[^1], string.Join(' ', parts[..^1])),
        };
    }
}
