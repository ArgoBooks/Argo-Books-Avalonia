using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Builds a year's T4 return from the pay runs already recorded.
///
/// Everything here is derived. A T4 is a restatement of pay runs that were frozen when they
/// were approved, so nothing is stored: the return is assembled on demand and comes out
/// identical every time. Storing it would create a second copy that could drift from the
/// first, which is exactly the reconciliation failure T4s are supposed to prevent.
/// </summary>
public class T4Service
{
    private readonly PayrollRateService _rates;

    public T4Service(PayrollRateService? rates = null) => _rates = rates ?? new PayrollRateService();

    /// <summary>
    /// Assembles the return for a tax year. Draft runs are excluded, but a draft is also a
    /// reason to refuse to file at all, so callers should check <see cref="Validate"/> first
    /// rather than relying on the omission.
    /// </summary>
    public T4Return Build(CompanyData data, int taxYear)
    {
        ArgumentNullException.ThrowIfNull(data);

        var company = data.Settings.Company;

        var t4 = new T4Return
        {
            TaxYear = taxYear,
            PayrollAccountNumber = company.PayrollAccountNumber ?? string.Empty,
            EmployerName = company.Name,
            EmployerAddress = new Models.Common.Address
            {
                Street = company.Address ?? string.Empty,
                City = company.City ?? string.Empty,
                State = company.ProvinceState ?? string.Empty,
                Country = string.IsNullOrWhiteSpace(company.Country) ? "CAN" : company.Country,
                ZipCode = company.PostalCode ?? string.Empty,
            },
            ContactName = company.PayrollContactName ?? string.Empty,
            ContactPhone = company.PayrollContactPhone ?? company.Phone ?? string.Empty,

            // Both for the T619 transmittal that wraps the submission rather than for any slip.
            // Falls back to the company address, matching ContactPhone above and the year end
            // screen, which shows that fallback in its box. Without it a company that has only a
            // general email saw a valid address on screen while validation blocked filing over an
            // empty one, with nothing to say why.
            ContactEmail = company.PayrollContactEmail ?? company.Email ?? string.Empty,
            LanguageCode = string.Equals(data.Settings.Localization.Language, "French",
                StringComparison.OrdinalIgnoreCase) ? "F" : "E",
        };

        EarningsCeilings ceilings = EarningsCeilings.For(_rates, taxYear);

        // Everything except drafts, so a voided run and its reversal both count and cancel to
        // zero. Matches how the year-to-date figures are built.
        var lines = data.PayRuns
            .Where(r => r.Status != PayRunStatus.Draft && r.PayDate.Year == taxYear)
            .SelectMany(r => r.Lines.Select(l => (r.PayDate, Line: l)))
            .ToList();

        foreach (var group in lines.GroupBy(x => x.Line.EmployeeId))
        {
            Employee? employee = data.Employees.FirstOrDefault(e => e.Id == group.Key);
            if (employee == null)
            {
                continue;
            }

            // CRA wants a separate T4 for each province of employment, and each line records the
            // province it was earned in; the employee's own province is only where they work now.
            // Earlier slips take their share of the year's ceilings first, because contributions
            // stop at one annual maximum however many slips the year is split across.
            decimal earnedBefore = 0m;

            foreach (var province in group
                         .GroupBy(x => ProvinceOf(x.Line, employee))
                         .OrderBy(p => p.Min(x => x.PayDate)))
            {
                T4Slip slip = BuildSlip(employee, province.Key, province.Select(x => x.Line).ToList(),
                                        ceilings, earnedBefore);
                earnedBefore += slip.EmploymentIncome;

                // A slip whose every figure nets to zero is one whose runs were all voided. There
                // is nothing to report and CRA has no element that means "nil year".
                if (slip.EmploymentIncome > 0 || slip.IncomeTaxDeducted > 0)
                {
                    t4.Slips.Add(slip);
                }
            }
        }

        t4.Slips.Sort((a, b) =>
        {
            int bySurname = string.Compare(a.Surname, b.Surname, StringComparison.CurrentCultureIgnoreCase);
            return bySurname != 0
                ? bySurname
                : string.CompareOrdinal(a.ProvinceOfEmployment, b.ProvinceOfEmployment);
        });
        return t4;
    }

    /// <summary>
    /// The province a line was earned in. Falls back to the employee's for a line that never
    /// recorded one, which is the only information there is for it.
    /// </summary>
    internal static string ProvinceOf(PayRunLine line, Employee employee) =>
        (string.IsNullOrWhiteSpace(line.Province) ? employee.Province : line.Province)
        .Trim()
        .ToUpperInvariant();

    private static T4Slip BuildSlip(Employee employee, string province, List<PayRunLine> lines,
                                    EarningsCeilings ceilings, decimal earnedBefore)
    {
        (string surname, string given, string initial) = SplitName(employee.Name);

        decimal gross = lines.Sum(l => l.GrossPay);
        bool quebec = province == "QC";
        decimal qpip = lines.Sum(l => l.QpipEmployee);

        // Exempt for the WHOLE year, which is what boxes 28 and the nil earnings in 24 and 26
        // mean. Read from what was actually withheld and not from the employee's current flag:
        // someone who turns 70 in June has the flag ticked from then on, but contributed from
        // January to May. Reporting those contributions in box 16 against nil pensionable
        // earnings with the exemption ticked is the contradiction CRA's PIER review looks for.
        decimal cppWithheld = lines.Sum(l => l.CppEmployee) + lines.Sum(l => l.Cpp2Employee);
        decimal eiWithheld = lines.Sum(l => l.EiEmployee);

        bool cppExemptAllYear = employee.IsCppExempt && cppWithheld == 0m;
        bool eiExemptAllYear = employee.IsEiExempt && eiWithheld == 0m;

        return new T4Slip
        {
            EmployeeId = employee.Id,
            Surname = surname,
            GivenName = given,
            Initial = initial,
            Sin = employee.Sin,
            EmployeeNumber = employee.EmployeeNumber,
            Address = employee.Address,
            ProvinceOfEmployment = province,
            IsQuebec = quebec,
            EmploymentIncome = gross,
            CppContributions = lines.Sum(l => l.CppEmployee),
            Cpp2Contributions = lines.Sum(l => l.Cpp2Employee),
            EiPremiums = lines.Sum(l => l.EiEmployee),

            // Box 22 is federal, provincial and territorial tax together, with one exception
            // stated in as many words by RC4120: "This includes the federal, provincial (except
            // Quebec), and territorial taxes that apply."
            //
            // Quebec income tax is withheld in the same pay run and stored in the same column,
            // but it is remitted to Revenu Quebec and reported on RL-1 box E. Adding it here
            // reports the same money on both slips, and the employee claims credit for it twice.
            IncomeTaxDeducted = lines.Sum(l => l.FederalTax)
                                + (quebec ? 0m : lines.Sum(l => l.ProvincialTax)),

            // Exempt employment reports nil earnings rather than omitting the box, so these
            // deliberately go to zero rather than to gross.
            //
            // Capped at the year's ceiling. Someone earning above it stopped contributing part
            // way through the year, and reporting their whole salary here would have CRA expect
            // contributions on money that was never pensionable or insurable. The figure looks
            // right either way, which is exactly why it needs pinning.
            InsurableEarnings = eiExemptAllYear ? 0m : ceilings.CapEi(earnedBefore + gross) - ceilings.CapEi(earnedBefore),
            PensionableEarnings = cppExemptAllYear
                ? 0m
                : ceilings.CapPensionable(earnedBefore + gross) - ceilings.CapPensionable(earnedBefore),

            QpipPremiums = qpip,

            // Tied to whether a premium was actually withheld rather than to the province alone.
            // RC4120 pairs the two: "If you report an amount in box 55, you have to report
            // insurable earnings using box 56", and box 28's PPIP tick means no premium was
            // withheld for the whole period. Earnings with no premium against them is the one
            // combination that cannot be true.
            //
            // RC4120 also carries a list under box 56 of when NOT to report it, which includes
            // "the insurable earnings are the same as the employment income in box 14" and "the
            // insurable earnings are over the maximum for the year". Taken literally that would
            // suppress box 56 in every case this app can produce, since QPIP eligible earnings
            // here are always gross capped at the ceiling. The two instructions contradict each
            // other and both are CRA's.
            //
            // Settled by the XML specification, which marks prov_insu_ern_amt optional rather
            // than required. So neither choice is rejected on submission, which leaves the box 55
            // pairing as the only one of the two phrased as an obligation, and reporting a
            // redundant figure as the cheaper way to be wrong. Reported.
            QpipInsurableEarnings = quebec && qpip > 0 ? ceilings.CapQpip(gross) : 0m,
            EmployerQpip = lines.Sum(l => l.QpipEmployer),

            CppExemptAllYear = cppExemptAllYear,
            EiExemptAllYear = eiExemptAllYear,
            DentalBenefit = employee.DentalBenefit,
            EmployerCpp = lines.Sum(l => l.CppEmployer),
            EmployerCpp2 = lines.Sum(l => l.Cpp2Employer),
            EmployerEi = lines.Sum(l => l.EiEmployer),
        };
    }

    /// <summary>
    /// Everything that would make CRA reject the filing, or make it wrong. Returned as
    /// messages rather than thrown, because the year end screen shows them all at once: an
    /// employer missing three SINs wants to see three, not to fix one and be told about the
    /// next.
    /// </summary>
    public static List<string> Validate(CompanyData data, T4Return t4)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(t4);

        var problems = new List<string>();

        // A draft run means the year is not finished being recorded. Filing now would produce
        // a T4 that disagrees with the books as soon as the draft is approved.
        int drafts = data.PayRuns.Count(r => r.Status == PayRunStatus.Draft && r.PayDate.Year == t4.TaxYear);
        if (drafts > 0)
        {
            problems.Add($"{drafts} pay run(s) in {t4.TaxYear} are still drafts. Approve or delete them before filing.");
        }

        if (!IsPayrollAccountNumber(t4.PayrollAccountNumber))
        {
            problems.Add("The payroll account number is missing or not in the form 000000000RP0000. "
                         + "It is on your CRA statement of account and is required on every slip.");
        }

        if (string.IsNullOrWhiteSpace(t4.EmployerName))
        {
            problems.Add("The company name is required on the T4 Summary.");
        }

        if (string.IsNullOrWhiteSpace(t4.ContactName))
        {
            problems.Add("A contact name is required on the T4 Summary, so CRA knows who to call.");
        }

        if (new string(t4.ContactPhone.Where(char.IsAsciiDigit).ToArray()).Length < 10)
        {
            problems.Add("A ten digit contact phone number is required on the T4 Summary.");
        }

        // Required by the T619 transmittal record wrapping the submission. Without it CRA rejects
        // the upload, so it is refused here rather than discovered at the deadline.
        if (!IsEmailAddress(t4.ContactEmail))
        {
            problems.Add("A contact email address is required. CRA uses it to tell you how the "
                         + "filing was processed, and the submission is rejected without one.");
        }

        // The employer's own address, which BuildSummary writes as prov_cd, pstl_cd and cntry_cd
        // just as it does an employee's. Only the employees were being checked, so a company that
        // typed "Alberta" into a free-text province box passed validation, showed no problems, and
        // had CRA reject the file over a two-letter code of "AL".
        bool employerCanadian = string.IsNullOrWhiteSpace(t4.EmployerAddress.Country)
                                || CraFormat.IsCanada(t4.EmployerAddress.Country);

        if (!string.IsNullOrWhiteSpace(t4.EmployerAddress.State)
            && employerCanadian
            && !CraFormat.IsProvinceCode(t4.EmployerAddress.State))
        {
            problems.Add($"The company province \"{t4.EmployerAddress.State}\" is not a Canadian "
                         + "province or territory code. Use two letters, such as ON or QC.");
        }

        if (!string.IsNullOrWhiteSpace(t4.EmployerAddress.ZipCode)
            && !CraFormat.IsPostalCode(t4.EmployerAddress.ZipCode, t4.EmployerAddress.Country))
        {
            problems.Add($"The company postal code \"{t4.EmployerAddress.ZipCode}\" is not in a form "
                         + "CRA accepts. A Canadian postal code is six characters, such as K1A0B1.");
        }

        string badEmployer = CraFormat.DisallowedCharacters(
            t4.EmployerAddress.Street + " " + t4.EmployerAddress.City, address: true);

        if (badEmployer.Length > 0)
        {
            problems.Add($"The company address contains {Describe(badEmployer)}, which CRA does not accept.");
        }

        foreach (T4Slip slip in t4.Slips)
        {
            string who = string.IsNullOrWhiteSpace(slip.Surname) ? slip.EmployeeId : slip.Surname;

            if (string.IsNullOrWhiteSpace(slip.ProvinceOfEmployment))
            {
                problems.Add($"{who} has no province of employment, which is required on the slip.");
            }

            // The employee form refuses all of the below. These catch anyone entered before it
            // did, since a file that has been sitting on disk since an earlier version is exactly
            // what gets filed without anybody opening the employee again.
            string badName = CraFormat.DisallowedCharacters(slip.Surname + " " + slip.GivenName);
            if (badName.Length > 0)
            {
                problems.Add($"{who}'s name contains {Describe(badName)}, which CRA does not accept. "
                             + "A name may only contain letters, digits, an apostrophe, an ampersand, "
                             + "a period, a hyphen and a space.");
            }

            string badAddress = CraFormat.DisallowedCharacters(
                slip.Address.Street + " " + slip.Address.City, address: true);
            if (badAddress.Length > 0)
            {
                problems.Add($"{who}'s address contains {Describe(badAddress)}, which CRA does not accept.");
            }

            // The address is optional in full, so these only apply once something has been
            // entered: an employee with no address at all files perfectly well. The province is
            // only held to the Canadian list when the address is Canadian, or when no country was
            // recorded, which on a T4 means Canadian.
            bool canadianAddress = string.IsNullOrWhiteSpace(slip.Address.Country)
                                   || CraFormat.IsCanada(slip.Address.Country);

            if (!string.IsNullOrWhiteSpace(slip.Address.State)
                && canadianAddress
                && !CraFormat.IsProvinceCode(slip.Address.State))
            {
                problems.Add($"{who}'s address province \"{slip.Address.State}\" is not a Canadian "
                             + "province or territory code. Use two letters, such as ON or QC.");
            }

            if (!string.IsNullOrWhiteSpace(slip.Address.ZipCode)
                && !CraFormat.IsPostalCode(slip.Address.ZipCode, slip.Address.Country))
            {
                problems.Add($"{who}'s postal code \"{slip.Address.ZipCode}\" is not in a form CRA "
                             + "accepts. A Canadian postal code is six characters, such as K1A0B1.");
            }
        }

        if (t4.Slips.Count == 0)
        {
            problems.Add($"There are no approved pay runs in {t4.TaxYear}, so there is nothing to file.");
        }

        // Someone who changed province has a slip for each, and one missing address is one problem.
        return problems.Distinct().ToList();
    }

    /// <summary>
    /// Things worth knowing before filing that do NOT stop it.
    ///
    /// Kept apart from <see cref="Validate"/> because conflating the two is what made the export
    /// button impossible to enable: a missing social insurance number was blocking a return that
    /// CRA actually accepts. CRA defines all zeroes for exactly this case, and
    /// <see cref="T4XmlWriter"/> writes it, so refusing to file was this app inventing a rule
    /// stricter than the one it was implementing.
    ///
    /// It still has to be said, because the employee loses the credit for their contributions.
    /// </summary>
    public static List<string> Warnings(T4Return t4)
    {
        ArgumentNullException.ThrowIfNull(t4);

        var warnings = new List<string>();

        foreach (T4Slip slip in t4.Slips)
        {
            string who = string.IsNullOrWhiteSpace(slip.Surname) ? slip.EmployeeId : slip.Surname;

            if (new string(slip.Sin.Where(char.IsAsciiDigit).ToArray()).Length != 9)
            {
                warnings.Add($"{who} has no social insurance number. The slip can still be filed, "
                             + "but their CPP contributions will not be credited to them.");
            }
        }

        return warnings.Distinct().ToList();
    }

    /// <summary>
    /// Names the offending characters rather than only saying there are some, since a comma or a
    /// curly apostrophe in a name is invisible to the person being told to remove it.
    /// </summary>
    private static string Describe(string characters) =>
        string.Join(" and ", characters.Select(c => c == ' ' ? "a space" : $"\"{c}\""));

    /// <summary>
    /// Something plausible enough to be an address, capped at the 60 characters CRA allows.
    ///
    /// Deliberately loose. The point is to catch an empty box or an obvious slip, not to
    /// adjudicate the grammar of email addresses, and a filing blocked by a validator that is
    /// stricter than CRA's is worse than one CRA bounces.
    /// </summary>
    public static bool IsEmailAddress(string? value)
    {
        string v = (value ?? string.Empty).Trim();

        if (v.Length is 0 or > 60 || v.Any(char.IsWhiteSpace))
        {
            return false;
        }

        int at = v.IndexOf('@');

        return at > 0
               && at == v.LastIndexOf('@')
               && v.IndexOf('.', at) > at + 1
               && !v.EndsWith('.');
    }

    /// <summary>Nine digits, then RP, then four digits.</summary>
    public static bool IsPayrollAccountNumber(string? value)
    {
        string v = (value ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

        return v.Length == 15
               && v[..9].All(char.IsAsciiDigit)
               && v[9..11] == "RP"
               && v[11..].All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Splits a display name into the surname, first given name and second initial the T4
    /// wants. The app stores one name field, so the last word is taken as the surname, which
    /// is right far more often than not and is visible on the slip for correcting when it is
    /// not.
    /// </summary>
    private static (string Surname, string Given, string Initial) SplitName(string name)
    {
        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => (string.Empty, string.Empty, string.Empty),
            1 => (parts[0], string.Empty, string.Empty),
            2 => (parts[1], parts[0], string.Empty),
            _ => (parts[^1], parts[0], parts[1][..1]),
        };
    }
}
