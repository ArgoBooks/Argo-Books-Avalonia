using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Payroll;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The refusals, the fallbacks and the lookup tables.
///
/// Payroll spends most of its lines on the happy path and most of its risk everywhere else.
/// These cover the branches taken when something is missing or malformed: an edition with no
/// Quebec rates, a line whose employee was deleted, a run that is not a draft. Each one is a
/// place where doing something reasonable-looking instead of nothing produces figures that get
/// filed.
///
/// The switch tables are here too. They read as though they cannot be wrong, but ROE alone
/// carries three separate frequency-to-count tables with different answers, and the compiler
/// has nothing to say if two of them get the same one.
/// </summary>
public class PayrollGuardTests
{
    private static PayrollRateTable Rates() => new PayrollRateService().GetForDate(new DateTime(2026, 8, 15))!;

    #region Pay frequency tables

    [Theory]
    [InlineData(PayFrequency.Weekly, 52)]
    [InlineData(PayFrequency.Biweekly, 26)]
    [InlineData(PayFrequency.SemiMonthly, 24)]
    [InlineData(PayFrequency.Monthly, 12)]
    public void PayPeriodsAYear_AreCraSCounts(PayFrequency frequency, int expected)
    {
        Assert.Equal(expected, frequency.PeriodsPerYear());
    }

    [Theory]
    [InlineData(PayFrequency.Weekly, "Weekly")]
    [InlineData(PayFrequency.Biweekly, "Biweekly")]
    [InlineData(PayFrequency.SemiMonthly, "Semi-monthly")]
    [InlineData(PayFrequency.Monthly, "Monthly")]
    public void EveryPayFrequency_HasAName(PayFrequency frequency, string expected)
    {
        Assert.Equal(expected, frequency.DisplayName());
    }

    [Theory]
    [InlineData(PayFrequency.Weekly, 53, 27)]
    [InlineData(PayFrequency.Biweekly, 27, 14)]
    [InlineData(PayFrequency.SemiMonthly, 25, 13)]
    [InlineData(PayFrequency.Monthly, 13, 7)]
    public void TheRoeWindows_AreDifferentLengthsFromEachOther(PayFrequency frequency, int hours, int earnings)
    {
        // Blocks 15A and 15C look back over the equivalent of 53 weeks; block 15B looks back
        // over roughly half of that. Using one window for both is the easy mistake and it
        // shortens somebody's claim.
        Assert.Equal(hours, RoeService.HoursPeriodCount(frequency));
        Assert.Equal(earnings, RoeService.EarningsPeriodCount(frequency));
        Assert.True(hours > earnings);
    }

    [Fact]
    public void AFrequencyThatIsNotOneOfTheFour_FallsBackToBiweekly()
    {
        // A company file can be edited by hand or written by an older version, so an enum value
        // outside the four defined ones does reach here. Every table has to answer rather than
        // throw, and biweekly is the answer that is right most often.
        const PayFrequency unknown = (PayFrequency)99;

        Assert.Equal(26, unknown.PeriodsPerYear());
        Assert.Equal("99", unknown.DisplayName());
        Assert.Equal(27, RoeService.HoursPeriodCount(unknown));
        Assert.Equal(14, RoeService.EarningsPeriodCount(unknown));
    }

    [Theory]
    [InlineData(PayFrequency.Weekly, 40)]
    [InlineData(PayFrequency.Biweekly, 80)]
    [InlineData(PayFrequency.SemiMonthly, 86.67)]
    [InlineData(PayFrequency.Monthly, 173.33)]
    [InlineData((PayFrequency)99, 80)]
    public void ASalariedEmployeesInsurableHours_ComeFromTheirContractWeek(
        PayFrequency frequency, decimal expected)
    {
        // Block 15A wants insurable hours and a salaried pay run records none, so Service
        // Canada's answer is to convert the contract week. That conversion has its own
        // frequency table, separate from the two window tables above and from CRA's.
        CompanyData data = Company(Person());
        data.Employees[0].PayFrequency = frequency;
        data.Employees[0].StandardHoursPerWeek = 40m;
        data.Employees[0].StartDate = new DateTime(2026, 1, 5);
        data.PayRuns.Add(new PayRun
        {
            Id = "PR-0001",
            PayDate = PayDate,
            PeriodStart = PayDate.AddDays(-13),
            PeriodEnd = PayDate,
            Status = PayRunStatus.Approved,
            Lines = { new PayRunLine { EmployeeId = "EMP-001", GrossPay = 2000m, NetPay = 1600m } },
        });

        RoeWorksheet worksheet = new RoeService().Build(data, "EMP-001");

        Assert.Equal(expected, worksheet.TotalInsurableHours);
    }

    #endregion

    #region The calculator's refusals

    [Fact]
    public void AProvinceWithNoRateTable_IsRefusedRatherThanApproximated()
    {
        Assert.Throws<NotSupportedException>(() => PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 2000m, Province = "ZZ", PayPeriodsPerYear = 26 },
            new PayrollYearToDate(), Rates()));
    }

    [Fact]
    public void AnEditionCarryingNoQuebecRates_IsRefused()
    {
        // Quebec was added to the rate files after the rest of Canada, so an older edition can
        // legitimately have none. Falling back to the federal calculator would produce CPP and
        // no QPIP for someone who owes the opposite.
        PayrollRateTable rates = Rates();
        rates.Quebec = null;

        Assert.Throws<NotSupportedException>(() => PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 2000m, Province = "QC", PayPeriodsPerYear = 26 },
            new PayrollYearToDate(), rates));
    }

    [Theory]
    [InlineData("AB", true)]
    [InlineData("ON", true)]
    [InlineData("qc", true)]
    [InlineData("QC", true)]
    [InlineData("ONTARIO", false)]
    [InlineData("ZZ", false)]
    [InlineData("", false)]
    public void WhetherAProvinceCanBeCalculatedFor_CanBeAskedBeforeTheCalculatorThrows(
        string province, bool expected)
    {
        // The calculator throws for a province it has no table for, which is right for a pure
        // function and useless as a way to find out. That throw is reachable without touching the
        // province dropdown: the spreadsheet importer stores whatever is in the cell, so an
        // employee imported as "Ontario" is upper-cased to ONTARIO and can never be paid.
        //
        // Quebec must answer true. It is held outside the provinces dictionary, so a check that
        // only reads that dictionary reports a fully supported jurisdiction as missing.
        Assert.Equal(expected, new PayrollService().Supports(new DateTime(2026, 8, 15), province));
    }

    [Fact]
    public void ADateNoEditionCovers_SupportsNoProvinceAtAll()
    {
        Assert.False(new PayrollService().Supports(new DateTime(2020, 1, 15), "AB"));
    }

    [Fact]
    public void NullArguments_AreRejectedAtTheDoor()
    {
        var input = new PayrollInput { GrossPay = 1m, Province = "AB", PayPeriodsPerYear = 26 };

        Assert.Throws<ArgumentNullException>(() => PayrollCalculator.Calculate(null!, new PayrollYearToDate(), Rates()));
        Assert.Throws<ArgumentNullException>(() => PayrollCalculator.Calculate(input, null!, Rates()));
        Assert.Throws<ArgumentNullException>(() => PayrollCalculator.Calculate(input, new PayrollYearToDate(), null!));
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("QC")]
    public void ARateFileMissingItsTopBracket_UsesTheHighestOneItHasRatherThanCrashing(string province)
    {
        // Every published table ends with an open-ended bracket. A delivered file that does not
        // still has to produce a number for an income above its last ceiling, because the
        // alternative is an exception in the middle of approving a pay run.
        PayrollRateTable rates = Rates();
        rates.Federal.Brackets = [rates.Federal.Brackets[0]];
        rates.Provinces["AB"].Brackets = [rates.Provinces["AB"].Brackets[0]];
        rates.Quebec!.Brackets = [rates.Quebec.Brackets[0]];

        PayrollDeductions d = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 40000m, Province = province, PayPeriodsPerYear = 26 },
            new PayrollYearToDate(), rates);

        Assert.True(d.FederalTax > 0);
    }

    [Fact]
    public void OntarioDependants_ReduceTheTax()
    {
        // T4127's factor Y: "$554 multiplied by the number of disabled dependants" plus "$554
        // multiplied by the number of dependants under age 19", as shown on Form TD1ON. It feeds
        // Ontario's tax reduction, which is twice the basic amount plus Y, less the tax already
        // worked out. An income low enough for the reduction to still be alive, or the credit is
        // exhausted and dependants change nothing.
        PayrollRateTable rates = Rates();

        decimal none = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 900m, Province = "ON", PayPeriodsPerYear = 26 },
            new PayrollYearToDate(), rates).ProvincialTax;

        decimal two = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 900m, Province = "ON", PayPeriodsPerYear = 26, Dependants = 2 },
            new PayrollYearToDate(), rates).ProvincialTax;

        Assert.True(two < none, $"two dependants ({two}) should pay less than none ({none})");
    }

    [Fact]
    public void DependantsOutsideOntario_ChangeNothing()
    {
        // Only Ontario's reduction has a dependant component. BC's tapers on income alone, and
        // everywhere else has no reduction at all, so the field has to be inert there rather
        // than quietly applying.
        PayrollRateTable rates = Rates();

        foreach (string province in new[] { "AB", "BC", "SK" })
        {
            decimal none = PayrollCalculator.Calculate(
                new PayrollInput { GrossPay = 900m, Province = province, PayPeriodsPerYear = 26 },
                new PayrollYearToDate(), rates).ProvincialTax;

            decimal three = PayrollCalculator.Calculate(
                new PayrollInput { GrossPay = 900m, Province = province, PayPeriodsPerYear = 26, Dependants = 3 },
                new PayrollYearToDate(), rates).ProvincialTax;

            Assert.Equal(none, three);
        }
    }

    [Fact]
    public void AnEmployeesDependantCount_ReachesTheCalculator()
    {
        // The count is on the employee and the calculator takes it as an input, and for a long
        // while nothing joined the two, so the whole factor multiplied by zero.
        CompanyData data = Company(Person(province: "ON"));
        data.Employees[0].PayRate = 23400m;
        data.Employees[0].OntarioDependants = 2;

        var service = new PayrollService();
        PayRun withDependants = service.CreateDraft(data, PayDate, PayDate.AddDays(-13), PayDate)!;

        data.Employees[0].OntarioDependants = 0;
        PayRun without = service.CreateDraft(data, PayDate, PayDate.AddDays(-13), PayDate)!;

        Assert.True(withDependants.Lines[0].ProvincialTax < without.Lines[0].ProvincialTax);
    }

    [Fact]
    public void AnOntarioIncomeUnderEveryHealthPremiumBand_PaysNoPremium()
    {
        // The premium starts above $20,000, so someone under it falls off the end of the band
        // list. That has to be no premium rather than the first band's amount.
        PayrollRateTable rates = Rates();

        PayrollDeductions low = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 300m, Province = "ON", PayPeriodsPerYear = 26, IsCppExempt = true, IsEiExempt = true },
            new PayrollYearToDate(), rates);

        Assert.Equal(0m, low.ProvincialTax);
    }

    [Fact]
    public void QuebecEarningsPastTheFirstCeiling_StartContributingToQpp2()
    {
        PayrollRateTable rates = Rates();
        QuebecRates qc = rates.Quebec!;

        PayrollDeductions d = PayrollCalculator.Calculate(
            new PayrollInput { GrossPay = 5000m, Province = "QC", PayPeriodsPerYear = 26 },
            new PayrollYearToDate
            {
                PensionableEarnings = qc.Qpp.YmpeCeiling,
                CppEmployee = qc.Qpp.MaxContributionEmployee,
            },
            rates);

        Assert.True(d.Cpp2Employee > 0);
        Assert.Equal(0m, d.CppEmployee);
    }

    #endregion

    #region The pay run service

    private static CompanyData Company(params Employee[] employees)
    {
        var data = new CompanyData();
        data.Settings.Company.Name = "Test Company";
        data.Settings.Company.PayrollAccountNumber = "123456789RP0001";
        data.Settings.Company.PayrollContactName = "Pat Owner";
        data.Settings.Company.PayrollContactPhone = "4035551234";
        data.Employees.AddRange(employees);
        return data;
    }

    private static Employee Person(string id = "EMP-001", string name = "Dana Smith", string province = "AB") => new()
    {
        Id = id,
        Name = name,
        Sin = "046454286",
        Province = province,
        PayType = PayType.Salary,
        PayRate = 52000m,
        PayFrequency = PayFrequency.Biweekly,
    };

    private static readonly DateTime PayDate = new(2026, 8, 14);

    private static PayRun? Draft(PayrollService service, CompanyData data, IEnumerable<string>? only = null) =>
        service.CreateDraft(data, PayDate, PayDate.AddDays(-13), PayDate, only);

    [Fact]
    public void ARunForSelectedEmployees_LeavesTheRestOut()
    {
        CompanyData data = Company(Person(), Person("EMP-002", "Alex Roy"));

        PayRun run = Draft(new PayrollService(), data, ["EMP-002"])!;

        Assert.Equal("EMP-002", Assert.Single(run.Lines).EmployeeId);
    }

    [Fact]
    public void AnArchivedEmployee_IsNotPaid()
    {
        CompanyData data = Company(Person(), Person("EMP-002", "Alex Roy"));
        data.Employees[1].IsArchived = true;

        Assert.Equal("EMP-001", Assert.Single(Draft(new PayrollService(), data)!.Lines).EmployeeId);
    }

    [Fact]
    public void ARunOnADateNoEditionCovers_IsNotCreated()
    {
        CompanyData data = Company(Person());

        Assert.Null(new PayrollService().CreateDraft(
            data, new DateTime(2031, 3, 6), new DateTime(2031, 2, 21), new DateTime(2031, 3, 6)));
    }

    [Fact]
    public void RecalculatingOnADateNoEditionCovers_LeavesTheRunAlone()
    {
        CompanyData data = Company(Person());
        var service = new PayrollService();

        PayRun run = Draft(service, data)!;
        run.PayDate = new DateTime(2031, 3, 6);
        run.Lines[0].FederalTax = 12.34m;

        service.Recalculate(data, run);

        Assert.Equal(12.34m, run.Lines[0].FederalTax);
    }

    [Fact]
    public void ALineWhoseEmployeeWasDeleted_IsLeftAsItWas()
    {
        // Employees can be removed after a draft exists. The line has to survive untouched so
        // the user can see what it was and delete it, rather than the run failing to open.
        CompanyData data = Company(Person());
        var service = new PayrollService();

        PayRun run = Draft(service, data)!;
        decimal before = run.Lines[0].FederalTax;
        data.Employees.Clear();

        service.Recalculate(data, run);

        Assert.Equal(before, run.Lines[0].FederalTax);
    }

    [Fact]
    public void AnApprovedRun_IsNotRecalculated()
    {
        // The figures on an approved run are frozen so that a stub reprinted later matches the
        // one the employee was handed.
        CompanyData data = Company(Person());
        var service = new PayrollService();

        PayRun run = Draft(service, data)!;
        service.Approve(run);
        run.Lines[0].BasePay = 99999m;

        service.Recalculate(data, run);

        Assert.NotEqual(99999m, run.Lines[0].GrossPay);
    }

    [Fact]
    public void ApprovingAnAlreadyApprovedRun_ChangesNothing()
    {
        CompanyData data = Company(Person());
        var service = new PayrollService();

        PayRun run = Draft(service, data)!;
        service.Approve(run);
        DateTime? first = run.ApprovedAt;

        service.Approve(run);

        Assert.Equal(first, run.ApprovedAt);
    }

    [Fact]
    public void ApproveAndRecord_OnARunThatIsNotADraft_WritesNoExpenses()
    {
        CompanyData data = Company(Person());
        var service = new PayrollService();

        PayRun run = Draft(service, data)!;
        service.Approve(run);

        Assert.Empty(service.ApproveAndRecord(data, run));
        Assert.Empty(data.Expenses);
    }

    [Fact]
    public void ALineWithNoNetPay_GetsNoExpense()
    {
        // A zero line is a real case: an employee added to a run and then left at zero hours.
        // An expense for nothing would show up on every report.
        CompanyData data = Company(Person());
        var service = new PayrollService();

        PayRun run = Draft(service, data)!;
        run.Lines[0].NetPay = 0m;

        Assert.Empty(service.ApproveAndRecord(data, run));
    }

    [Fact]
    public void VoidingSomethingThatWasNeverApproved_DoesNothing()
    {
        CompanyData data = Company(Person());
        var service = new PayrollService();

        PayRun run = Draft(service, data)!;

        Assert.Null(service.Void(data, run));
        Assert.Equal(PayRunStatus.Draft, run.Status);
    }

    #endregion

    #region T4

    [Fact]
    public void APayRunLineForAnEmployeeWhoWasDeleted_ProducesNoSlip()
    {
        CompanyData data = Company();
        data.PayRuns.Add(new PayRun
        {
            Id = "PR-0001",
            PayDate = PayDate,
            Status = PayRunStatus.Approved,
            Lines = { new PayRunLine { EmployeeId = "GONE", GrossPay = 2000m, NetPay = 1600m } },
        });

        Assert.Empty(new T4Service().Build(data, 2026).Slips);
    }

    [Fact]
    public void ASlipWithNoProvinceOfEmployment_BlocksFiling()
    {
        // Box 10 is required and CRA rejects the submission without it, so this has to be a
        // problem rather than a warning.
        CompanyData data = Company(Person());
        data.PayRuns.Add(new PayRun
        {
            Id = "PR-0001",
            PayDate = PayDate,
            Status = PayRunStatus.Approved,
            Lines = { new PayRunLine { EmployeeId = "EMP-001", EmployeeName = "Dana Smith", GrossPay = 2000m, NetPay = 1600m } },
        });

        T4Return t4 = new T4Service().Build(data, 2026);
        t4.Slips[0].ProvinceOfEmployment = string.Empty;

        Assert.Contains(T4Service.Validate(data, t4), p => p.Contains("province of employment"));
    }

    #endregion

    #region RL-1

    [Fact]
    public void NoQuebecEmployees_MeansNoRl1AtAll()
    {
        CompanyData data = Company(Person());
        data.PayRuns.Add(new PayRun { Id = "PR-0001", PayDate = PayDate, Status = PayRunStatus.Approved });

        Assert.False(Rl1Service.HasQuebecEmployees(data, 2026));
    }

    [Fact]
    public void AQuebecEmployeeWithAnApprovedRun_MeansAnRl1IsDue()
    {
        CompanyData data = Company(Person(province: "QC"));
        data.PayRuns.Add(new PayRun
        {
            Id = "PR-0001",
            PayDate = PayDate,
            Status = PayRunStatus.Approved,
            Lines = { new PayRunLine { EmployeeId = "EMP-001", GrossPay = 2000m, NetPay = 1600m } },
        });

        Assert.True(Rl1Service.HasQuebecEmployees(data, 2026));
    }

    [Fact]
    public void AnRl1WithNoCompanyNameAndNoSlips_SaysBoth()
    {
        CompanyData data = Company();
        data.Settings.Company.Name = string.Empty;
        data.Settings.Company.QuebecIdentificationNumber = "1234567890RS0001";

        List<string> problems = Rl1Service.Validate(data, new Rl1Service().Build(data, 2026));

        Assert.Contains(problems, p => p.Contains("company name is required"));
        Assert.Contains(problems, p => p.Contains("nothing to file"));
    }

    [Theory]
    [InlineData("Dana Marie Smith", "Smith", "Dana Marie")]
    [InlineData("Dana Smith", "Smith", "Dana")]
    [InlineData("Prince", "Prince", "")]
    [InlineData("   ", "", "")]
    public void AnEmployeesName_IsSplitTheWayTheRl1WantsIt(string name, string surname, string given)
    {
        // The RL-1 has no initial field, so everything before the last word is the given name.
        // A single-word name and a blank one both have to produce something rather than
        // throwing halfway through building the return.
        CompanyData data = Company(Person(province: "QC"));
        data.Employees[0].Name = name;
        data.Settings.Company.QuebecIdentificationNumber = "1234567890RS0001";
        data.PayRuns.Add(new PayRun
        {
            Id = "PR-0001",
            PayDate = PayDate,
            Status = PayRunStatus.Approved,
            Lines = { new PayRunLine { EmployeeId = "EMP-001", Province = "QC", GrossPay = 2000m, NetPay = 1600m } },
        });

        Rl1Slip slip = new Rl1Service().Build(data, 2026).Slips.Single();

        Assert.Equal(surname, slip.Surname);
        Assert.Equal(given, slip.GivenName);
    }

    #endregion

    #region Record of employment

    [Fact]
    public void AnEmployeeWithNoApprovedRuns_IsToldWhyTheHoursAreBlank()
    {
        // Block 15A cannot be left empty without an explanation, and "no pay runs" is a real
        // answer for someone added to the app and never paid through it.
        CompanyData data = Company(Person());
        data.Employees[0].StartDate = new DateTime(2026, 1, 5);

        RoeWorksheet worksheet = new RoeService().Build(data, "EMP-001");

        Assert.Equal("This employee has no approved pay runs.", worksheet.HoursUnavailableReason);
        Assert.Empty(worksheet.Periods);
    }

    [Fact]
    public void AWorksheetWithNoStartDateAndNoRuns_ReportsBoth()
    {
        CompanyData data = Company(Person());

        List<string> problems = RoeService.Validate(new RoeService().Build(data, "EMP-001"));

        Assert.Contains(problems, p => p.Contains("no start date"));
        Assert.Contains(problems, p => p.Contains("nothing to report"));
    }

    #endregion
}
