using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Payroll;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Assembles the figures for a Record of Employment.
///
/// An ROE is due within five calendar days of the END OF THE PAY PERIOD in which the
/// interruption of earnings happens, not five days from the last day worked, and it is what
/// Service Canada calculates an EI claim from. Getting the hours or the earnings wrong does not
/// bounce; it quietly shortens someone's benefit.
///
/// Two counts drive everything and they are NOT the same number, which is the trap:
///
/// - Blocks 15A and 15C cover the last 53 weeks of payroll: 53 weekly periods, 27 biweekly,
///   25 semi-monthly, 13 monthly.
/// - Block 15B covers a shorter window: 27 weekly, 14 biweekly, 13 semi-monthly, 7 monthly.
///
/// Both are read from Service Canada's charts rather than derived, because they are not a
/// clean function of the frequency. All eight cells were checked against the ROE guide on
/// canada.ca: the guide prints them as two separate charts and puts a footnote under each saying
/// the other one differs, which is as close as a government document comes to warning you not to
/// reuse a number.
///
/// The guide carries a fifth row both charts agree on, "13 pay periods a year", which is not a
/// frequency this app offers. If it is ever added, the counts are 14 for blocks 15A and 15C and
/// 7 for block 15B, and note that 14 is NOT the biweekly 27 despite both being roughly fortnightly.
/// </summary>
public class RoeService
{
    /// <summary>Blocks 15A and 15C. The equivalent of 53 weeks.</summary>
    public static int HoursPeriodCount(PayFrequency frequency) => frequency switch
    {
        PayFrequency.Weekly => 53,
        PayFrequency.Biweekly => 27,
        PayFrequency.SemiMonthly => 25,
        PayFrequency.Monthly => 13,
        _ => 27,
    };

    /// <summary>Block 15B. Deliberately shorter than the hours window.</summary>
    public static int EarningsPeriodCount(PayFrequency frequency) => frequency switch
    {
        PayFrequency.Weekly => 27,
        PayFrequency.Biweekly => 14,
        PayFrequency.SemiMonthly => 13,
        PayFrequency.Monthly => 7,
        _ => 14,
    };

    /// <summary>
    /// Hours in one pay period for a salaried employee, from their contract week. Service
    /// Canada's answer for an employer who does not track hours.
    /// </summary>
    private static decimal HoursPerPeriod(decimal weeklyHours, PayFrequency frequency) => frequency switch
    {
        PayFrequency.Weekly => weeklyHours,
        PayFrequency.Biweekly => weeklyHours * 2m,
        PayFrequency.SemiMonthly => Math.Round(weeklyHours * 52m / 24m, 2, MidpointRounding.AwayFromZero),
        PayFrequency.Monthly => Math.Round(weeklyHours * 52m / 12m, 2, MidpointRounding.AwayFromZero),
        _ => weeklyHours * 2m,
    };

    /// <summary>
    /// The payroll contact split for block 16: every word but the last is the given name and
    /// the last is the surname. A single word is a surname, which is the half Service Canada
    /// matches on.
    /// </summary>
    private static (string? Given, string? Surname) SplitContactName(string? name)
    {
        string[] parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => (null, null),
            1 => (null, parts[0]),
            _ => (string.Join(' ', parts[..^1]), parts[^1]),
        };
    }

    private static string CollapseSpaces(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Keeps the contact from an exported ROE for the next one, in the same fields the T4 reads.
    ///
    /// Only what actually changed is written. The form is seeded from these fields split into
    /// two names and reduced to ten digits, so comparing the text as typed would rewrite a
    /// contact nobody edited, and a name with a middle name used to come back without it.
    /// </summary>
    /// <returns>True when anything was written.</returns>
    public static bool ApplyContact(CompanyInfo company, string? firstName, string? lastName, string? phone)
    {
        ArgumentNullException.ThrowIfNull(company);

        string name = CollapseSpaces($"{firstName} {lastName}");
        string number = (phone ?? string.Empty).Trim();

        if (name.Length == 0 || number.Length == 0)
        {
            return false;
        }

        bool nameChanged = CollapseSpaces(company.PayrollContactName) != name;

        string? storedDigits = RoeXmlWriter.NationalPhone(company.PayrollContactPhone);
        bool phoneChanged = storedDigits == null || storedDigits != RoeXmlWriter.NationalPhone(number);

        if (nameChanged)
        {
            company.PayrollContactName = name;
        }

        if (phoneChanged)
        {
            company.PayrollContactPhone = number;
        }

        return nameChanged || phoneChanged;
    }

    public RoeWorksheet Build(CompanyData data, string employeeId)
    {
        ArgumentNullException.ThrowIfNull(data);

        Employee employee = data.Employees.FirstOrDefault(e => e.Id == employeeId)
                            ?? throw new ArgumentException($"No employee with id {employeeId}.", nameof(employeeId));

        var company = data.Settings.Company;
        (string? contactGiven, string? contactSurname) = SplitContactName(company.PayrollContactName);

        var worksheet = new RoeWorksheet
        {
            EmployeeId = employee.Id,
            EmployeeName = employee.Name,
            Sin = employee.Sin,
            Address = employee.Address,
            EmployerName = company.Name,
            PayrollAccountNumber = company.PayrollAccountNumber ?? string.Empty,
            PayFrequency = employee.PayFrequency,
            FirstDayWorked = employee.StartDate,
            HoursPeriodCount = HoursPeriodCount(employee.PayFrequency),
            EarningsPeriodCount = EarningsPeriodCount(employee.PayFrequency),

            // Block 16's contact, seeded from the payroll contact already held for the T4 so it
            // is confirmed rather than retyped. Block 16 itself stays null: see RoeReason.
            //
            // The T4 screen keeps the phone as typed, and the form's phone box holds ten digits,
            // so it gets the ten digits. Given the typed text, it counted brackets and dashes
            // toward the ten and cut the number short.
            ContactPhone = RoeXmlWriter.NationalPhone(company.PayrollContactPhone) ?? company.PayrollContactPhone,
            ContactFirstName = contactGiven,
            ContactLastName = contactSurname,
        };

        // Everything except drafts, so a voided run and its reversal cancel. Ordered most
        // recent first, which is the order block 15C wants and the opposite of storage order.
        var runs = data.PayRuns
            .Where(r => r.Status != PayRunStatus.Draft && r.Lines.Any(l => l.EmployeeId == employeeId))
            .OrderByDescending(r => r.PeriodEnd)
            .ToList();

        if (runs.Count == 0)
        {
            worksheet.HoursUnavailableReason = "This employee has no approved pay runs.";
            return worksheet;
        }

        // Runs are grouped by period rather than taken one to one, because a void and its
        // reversal share a period and must net out instead of consuming two of the 27 slots.
        var periods = runs
            .GroupBy(r => r.PeriodEnd)
            .OrderByDescending(g => g.Key)
            .Select(g => new
            {
                PeriodEnd = g.Key,
                Lines = g.SelectMany(r => r.Lines).Where(l => l.EmployeeId == employeeId).ToList(),
            })
            .ToList();

        // A later period that paid nothing is not the final one. It is what someone who left
        // without an end date looks like on the next run, where they are still ticked, or a
        // voided final run netting out against its reversal. Taken as final, it moves blocks 11
        // and 12 past the last pay and shifts every period in block 15C by one. Nil periods
        // before the last pay are part of the history and stay.
        int lastPaid = periods.FindIndex(p => p.Lines.Sum(l => l.GrossPay) != 0m || p.Lines.Sum(l => l.HoursWorked) != 0m);
        if (lastPaid > 0)
        {
            periods.RemoveRange(0, lastPaid);
        }

        worksheet.LastDayPaid = employee.EndDate ?? periods[0].PeriodEnd;
        worksheet.FinalPeriodEnd = periods[0].PeriodEnd;

        // Block 12 can never be earlier than block 11. An end date after the last pay period,
        // which is what a final unpaid week looks like, would otherwise produce exactly that.
        if (worksheet.LastDayPaid > worksheet.FinalPeriodEnd)
        {
            worksheet.FinalPeriodEnd = worksheet.LastDayPaid;
        }

        worksheet.FirstDayWorked ??= runs.Min(r => r.PeriodStart);

        bool hoursKnown = employee.PayType == PayType.Hourly || employee.StandardHoursPerWeek > 0;

        if (!hoursKnown)
        {
            worksheet.HoursUnavailableReason =
                "This is a salaried employee and no standard hours per week are recorded, so block 15A "
                + "cannot be worked out. Enter their contract hours on the employee, or fill block 15A "
                + "from the employment contract.";
        }

        foreach (var period in periods.Take(worksheet.HoursPeriodCount))
        {
            decimal gross = period.Lines.Sum(l => l.GrossPay);

            worksheet.Periods.Add(new RoePayPeriod
            {
                PeriodEnd = period.PeriodEnd,

                // An EI exempt employee has no insurable earnings at all, whatever they were
                // paid, so reporting gross would overstate a claim they cannot make.
                InsurableEarnings = employee.IsEiExempt ? 0m : gross,
                InsurableHours = !hoursKnown
                    ? null
                    : employee.PayType == PayType.Hourly
                        ? period.Lines.Sum(l => l.HoursWorked)

                        // A salaried nil period earned nothing and so worked nothing. Crediting
                        // contract hours to it would invent hours nobody worked.
                        : gross == 0m
                            ? 0m
                            : HoursPerPeriod(employee.StandardHoursPerWeek ?? 0m, employee.PayFrequency),
            });
        }

        worksheet.TotalInsurableHours = hoursKnown
            ? worksheet.Periods.Sum(p => p.InsurableHours ?? 0m)
            : null;

        // The shorter window, and the reason the two counts are separate constants.
        worksheet.TotalInsurableEarnings = worksheet.Periods
            .Take(worksheet.EarningsPeriodCount)
            .Sum(p => p.InsurableEarnings);

        // The final period only, not the whole history. Service Canada's ROE guide defines block
        // 17A as vacation pay paid or payable BECAUSE OF the separation, and its chart is explicit
        // that vacation pay "included with each pay", the usual percentage added to every cheque,
        // must NOT be reported here. Summing every period reported exactly that.
        //
        // The final period is the best the recorded data supports: nothing distinguishes a
        // termination payout from an ordinary accrual except when it was paid. The two remaining
        // categories in the guide, a granted leave period and an anniversary payment falling after
        // the interruption, are future-dated and unknowable from pay runs, which is why the
        // worksheet asks the employer to confirm the figure.
        worksheet.VacationPay = periods[0].Lines.Sum(l => l.VacationPay);

        return worksheet;
    }

    /// <summary>
    /// What would make the worksheet wrong or unusable. Returned as messages so they can all be
    /// shown at once, matching the T4 and RL-1.
    /// </summary>
    public static List<string> Validate(RoeWorksheet worksheet)
    {
        ArgumentNullException.ThrowIfNull(worksheet);

        var problems = new List<string>();

        if (new string(worksheet.Sin.Where(char.IsAsciiDigit).ToArray()).Length != 9)
        {
            problems.Add("This employee has no social insurance number, which block 9 requires.");
        }

        if (worksheet.FirstDayWorked == null)
        {
            problems.Add("There is no start date for this employee, so block 10 cannot be filled.");
        }

        if (worksheet.Periods.Count == 0)
        {
            problems.Add("There are no approved pay runs for this employee, so there is nothing to report.");
        }

        if (worksheet.HoursUnavailableReason != null)
        {
            problems.Add(worksheet.HoursUnavailableReason);
        }

        // Not a rejection, but it is the number that decides how long someone is paid, and a
        // short history usually means pay runs were recorded elsewhere first.
        if (worksheet.Periods.Count > 0 && worksheet.Periods.Count < worksheet.EarningsPeriodCount)
        {
            problems.Add($"Only {worksheet.Periods.Count} pay period(s) are recorded, and block 15B normally covers "
                         + $"{worksheet.EarningsPeriodCount}. If this employee was paid before you started using Argo "
                         + "Books, add those periods by hand.");
        }

        return problems;
    }
}
