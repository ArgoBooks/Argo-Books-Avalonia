using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Runs real pay runs through the service, period after period, the way a year of payroll
/// actually happens.
///
/// The calculator can be perfectly correct and the payroll still wrong, because the calculator
/// only sees the year-to-date figures it is handed. This is the layer that produces them, by
/// summing prior approved runs, and it is the layer where a mistake is invisible: every
/// individual pay stub looks right, and the error only surfaces at year end when the T4 does
/// not reconcile or an employee has over-contributed.
///
/// The loaded edition covers the second half of 2026, so these simulate the thirteen biweekly
/// pay dates from July to December rather than a calendar year. That is enough periods at a
/// high salary to reach every annual ceiling, which is what the tests are for.
/// </summary>
public class PayrollYearSimulationTests
{
    private const string EmployeeId = "EMP-001";

    /// <summary>The thirteen biweekly pay dates the 2026-07 edition covers.</summary>
    private static List<DateTime> PayDates()
    {
        var dates = new List<DateTime>();
        var date = new DateTime(2026, 7, 3);

        while (date.Year == 2026)
        {
            dates.Add(date);
            date = date.AddDays(14);
        }

        return dates;
    }

    private static CompanyData DataWith(decimal annualSalary, bool cppExempt = false, bool eiExempt = false) => new()
    {
        Employees =
        {
            new Employee
            {
                Id = EmployeeId,
                Name = "Test Person",
                Province = "AB",
                PayType = PayType.Salary,
                PayRate = annualSalary,
                PayFrequency = PayFrequency.Biweekly,
                IsCppExempt = cppExempt,
                IsEiExempt = eiExempt,
            },
        },
    };

    /// <summary>Drafts and approves a run on every pay date, exactly as the modal does.</summary>
    private static List<PayRun> RunTheHalfYear(CompanyData data, PayrollService service)
    {
        var runs = new List<PayRun>();

        foreach (DateTime payDate in PayDates())
        {
            PayRun? run = service.CreateDraft(data, payDate, payDate.AddDays(-13), payDate);
            Assert.NotNull(run);

            data.PayRuns.Add(run);
            service.ApproveAndRecord(data, run);
            runs.Add(run);
        }

        return runs;
    }

    #region Ceilings across a sequence of real runs

    [Fact]
    public void AcrossASequenceOfRuns_CppNeverExceedsTheAnnualMaximum()
    {
        // 260,000 a year is 10,000 a period, so the ceiling is reached partway through. The
        // period that crosses it must deduct only the remainder, and every period after it
        // must deduct nothing.
        PayrollRateTable rates = new PayrollRateService().GetForDate(new DateTime(2026, 8, 15))!;
        CompanyData data = DataWith(260000m);
        var service = new PayrollService();

        List<PayRun> runs = RunTheHalfYear(data, service);
        decimal running = 0m;

        foreach (PayRun run in runs)
        {
            running += run.Lines[0].CppEmployee;
            Assert.True(running <= rates.Cpp.MaxContributionEmployee,
                $"CPP reached {running} against a maximum of {rates.Cpp.MaxContributionEmployee}.");
        }

        Assert.Equal(rates.Cpp.MaxContributionEmployee, running);
    }

    [Fact]
    public void AcrossASequenceOfRuns_EiNeverExceedsTheAnnualMaximum()
    {
        PayrollRateTable rates = new PayrollRateService().GetForDate(new DateTime(2026, 8, 15))!;
        CompanyData data = DataWith(260000m);

        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());
        decimal running = runs.Sum(r => r.Lines[0].EiEmployee);

        Assert.Equal(rates.Ei.MaxPremiumEmployee, running);
    }

    [Fact]
    public void AcrossASequenceOfRuns_Cpp2NeverExceedsItsAnnualMaximum()
    {
        PayrollRateTable rates = new PayrollRateService().GetForDate(new DateTime(2026, 8, 15))!;
        CompanyData data = DataWith(260000m);

        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());
        decimal running = runs.Sum(r => r.Lines[0].Cpp2Employee);

        Assert.Equal(rates.Cpp2.MaxContributionEmployee, running);
    }

    [Fact]
    public void OnceACeilingIsReached_EveryLaterRunDeductsNothingForIt()
    {
        CompanyData data = DataWith(260000m);
        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());

        // Find the first run that stopped deducting, then assert none after it resumed.
        int firstZero = runs.FindIndex(r => r.Lines[0].CppEmployee == 0);
        Assert.True(firstZero > 0, "CPP never reached its ceiling, so this test proved nothing.");

        foreach (PayRun run in runs.Skip(firstZero))
        {
            Assert.Equal(0m, run.Lines[0].CppEmployee);
            Assert.Equal(0m, run.Lines[0].CppEmployer);
        }
    }

    [Fact]
    public void AModestSalary_NeverReachesACeilingAcrossTheWholeSequence()
    {
        PayrollRateTable rates = new PayrollRateService().GetForDate(new DateTime(2026, 8, 15))!;
        CompanyData data = DataWith(41600m);

        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());

        Assert.True(runs.Sum(r => r.Lines[0].CppEmployee) < rates.Cpp.MaxContributionEmployee);
        Assert.Equal(0m, runs.Sum(r => r.Lines[0].Cpp2Employee));
        Assert.True(runs.Sum(r => r.Lines[0].EiEmployee) < rates.Ei.MaxPremiumEmployee);
        Assert.All(runs, r => Assert.True(r.Lines[0].CppEmployee > 0));
    }

    #endregion

    #region Year to date fed forward

    [Fact]
    public void EachRunSeesEveryApprovedRunBeforeIt()
    {
        CompanyData data = DataWith(260000m);
        var service = new PayrollService();
        List<PayRun> runs = RunTheHalfYear(data, service);

        // Rebuild the year-to-date the way the service does and check it against the sum of
        // the runs that actually preceded each one.
        for (int i = 0; i < runs.Count; i++)
        {
            PayrollYearToDate ytd = service.YearToDateFor(data, EmployeeId, runs[i]);

            Assert.Equal(runs.Take(i).Sum(r => r.Lines[0].CppEmployee), ytd.CppEmployee);
            Assert.Equal(runs.Take(i).Sum(r => r.Lines[0].EiEmployee), ytd.EiEmployee);
            Assert.Equal(runs.Take(i).Sum(r => r.Lines[0].GrossPay), ytd.PensionableEarnings);
        }
    }

    [Fact]
    public void ADraftRunDoesNotInflateTheNextRunsYearToDate()
    {
        // A draft has not happened. Counting it would make the next run under-deduct, and the
        // shortfall would never be noticed until year end.
        CompanyData data = DataWith(104000m);
        var service = new PayrollService();

        PayRun? approved = service.CreateDraft(data, new DateTime(2026, 7, 3), new DateTime(2026, 6, 20), new DateTime(2026, 7, 3));
        data.PayRuns.Add(approved!);
        service.ApproveAndRecord(data, approved!);

        PayRun? draft = service.CreateDraft(data, new DateTime(2026, 7, 17), new DateTime(2026, 7, 4), new DateTime(2026, 7, 17));
        data.PayRuns.Add(draft!);

        PayRun? third = service.CreateDraft(data, new DateTime(2026, 7, 31), new DateTime(2026, 7, 18), new DateTime(2026, 7, 31));
        PayrollYearToDate ytd = service.YearToDateFor(data, EmployeeId, third);

        Assert.Equal(approved!.Lines[0].CppEmployee, ytd.CppEmployee);
    }

    [Fact]
    public void VoidingARun_ReturnsTheYearToDateToWhereItWas()
    {
        // The reversal's negative amounts have to cancel the original exactly, or every run
        // after a correction is calculated against a year-to-date that never existed.
        CompanyData data = DataWith(104000m);
        var service = new PayrollService();

        PayRun? first = service.CreateDraft(data, new DateTime(2026, 7, 3), new DateTime(2026, 6, 20), new DateTime(2026, 7, 3));
        data.PayRuns.Add(first!);
        service.ApproveAndRecord(data, first!);

        PayRun? second = service.CreateDraft(data, new DateTime(2026, 7, 17), new DateTime(2026, 7, 4), new DateTime(2026, 7, 17));
        data.PayRuns.Add(second!);
        service.ApproveAndRecord(data, second!);

        PayrollYearToDate before = service.YearToDateFor(data, EmployeeId, second);

        service.Void(data, second!);

        PayRun? third = service.CreateDraft(data, new DateTime(2026, 7, 31), new DateTime(2026, 7, 18), new DateTime(2026, 7, 31));
        PayrollYearToDate after = service.YearToDateFor(data, EmployeeId, third);

        Assert.Equal(before.CppEmployee, after.CppEmployee);
        Assert.Equal(before.EiEmployee, after.EiEmployee);
        Assert.Equal(before.PensionableEarnings, after.PensionableEarnings);
    }

    [Fact]
    public void ARunApprovedOutOfOrder_IsStillOnlyCountedByLaterPayDates()
    {
        // Someone catching up on a missed period must not retroactively change a run that was
        // already approved and whose stub is already in an employee's hands.
        CompanyData data = DataWith(104000m);
        var service = new PayrollService();

        PayRun? later = service.CreateDraft(data, new DateTime(2026, 9, 4), new DateTime(2026, 8, 22), new DateTime(2026, 9, 4));
        data.PayRuns.Add(later!);
        service.ApproveAndRecord(data, later!);

        PayRun? earlier = service.CreateDraft(data, new DateTime(2026, 7, 3), new DateTime(2026, 6, 20), new DateTime(2026, 7, 3));

        PayrollYearToDate ytd = service.YearToDateFor(data, EmployeeId, earlier);

        Assert.Equal(0m, ytd.CppEmployee);
    }

    #endregion

    #region What the books receive

    [Fact]
    public void TheExpensesWritten_SumToTheNetPayOfTheWholeSequence()
    {
        // What the books say left the bank has to equal what the employees were actually paid.
        CompanyData data = DataWith(260000m);
        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());

        decimal net = runs.Sum(r => r.TotalNetPay);
        decimal booked = data.Expenses.Sum(e => e.Total);

        Assert.Equal(net, booked);
    }

    [Fact]
    public void EveryExpense_CarriesAUniqueIdAndIsLinkedToItsLine()
    {
        CompanyData data = DataWith(260000m);
        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());

        List<string> ids = data.Expenses.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        foreach (PayRun run in runs)
        {
            Assert.Contains(data.Expenses, e => e.Id == run.Lines[0].ExpenseId);
        }
    }

    [Fact]
    public void EachRunsTotals_AgreeWithTheSumOfItsLines()
    {
        CompanyData data = DataWith(156000m);
        data.Employees.Add(new Employee
        {
            Id = "EMP-002",
            Name = "Second Person",
            Province = "AB",
            PayType = PayType.Salary,
            PayRate = 78000m,
            PayFrequency = PayFrequency.Biweekly,
        });

        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());

        foreach (PayRun run in runs)
        {
            Assert.Equal(2, run.Lines.Count);
            Assert.Equal(run.Lines.Sum(l => l.NetPay), run.TotalNetPay);
            Assert.Equal(run.Lines.Sum(l => l.GrossPay), run.TotalGross);
            Assert.Equal(run.Lines.Sum(l => l.TotalRemittance), run.TotalRemittance);
            Assert.Equal(run.Lines.Sum(l => l.TotalCost), run.TotalCost);
        }
    }

    [Fact]
    public void EveryLine_ReconcilesGrossAgainstItsEarningsAndItsDeductions()
    {
        // The identity a pay stub prints. If this fails the stub cannot be made to add up.
        CompanyData data = DataWith(156000m);
        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());

        foreach (PayRunLine line in runs.SelectMany(r => r.Lines))
        {
            Assert.Equal(line.BasePay + line.Bonus + line.VacationPay, line.GrossPay);

            decimal deductions = line.CppEmployee + line.Cpp2Employee + line.EiEmployee
                                 + line.FederalTax + line.ProvincialTax;

            Assert.Equal(line.GrossPay - deductions, line.NetPay);
        }
    }

    [Fact]
    public void AnHourlyEmployeesBasePay_IsStoredSoTheStubAddsUp()
    {
        CompanyData data = new()
        {
            Employees =
            {
                new Employee
                {
                    Id = EmployeeId,
                    Name = "Hourly Person",
                    Province = "AB",
                    PayType = PayType.Hourly,
                    PayRate = 32m,
                    PayFrequency = PayFrequency.Biweekly,
                },
            },
        };

        var service = new PayrollService();
        PayRun? run = service.CreateDraft(data, new DateTime(2026, 7, 3), new DateTime(2026, 6, 20), new DateTime(2026, 7, 3));

        run!.Lines[0].HoursWorked = 75m;
        run.Lines[0].Bonus = 100m;
        service.Recalculate(data, run);

        Assert.Equal(2400m, run.Lines[0].BasePay);
        Assert.Equal(2500m, run.Lines[0].GrossPay);
        Assert.Equal(run.Lines[0].BasePay + run.Lines[0].Bonus, run.Lines[0].GrossPay);
    }

    #endregion

    #region Exempt employees over a sequence

    [Fact]
    public void ACppExemptEmployee_NeverContributesAcrossTheWholeSequence()
    {
        CompanyData data = DataWith(260000m, cppExempt: true);
        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());

        Assert.Equal(0m, runs.Sum(r => r.Lines[0].CppEmployee));
        Assert.Equal(0m, runs.Sum(r => r.Lines[0].Cpp2Employee));
        Assert.Equal(0m, runs.Sum(r => r.Lines[0].CppEmployer));
        Assert.True(runs.Sum(r => r.Lines[0].EiEmployee) > 0);
    }

    [Fact]
    public void AnEiExemptOwner_NeverPaysPremiumsAcrossTheWholeSequence()
    {
        CompanyData data = DataWith(260000m, eiExempt: true);
        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());

        Assert.Equal(0m, runs.Sum(r => r.Lines[0].EiEmployee));
        Assert.Equal(0m, runs.Sum(r => r.Lines[0].EiEmployer));
        Assert.True(runs.Sum(r => r.Lines[0].CppEmployee) > 0);
    }

    #endregion

    #region The remittance figure

    [Fact]
    public void TheRemittanceForTheSequence_IsEverythingWithheldPlusTheEmployerShare()
    {
        CompanyData data = DataWith(260000m);
        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());

        decimal withheld = runs.Sum(r => r.Lines.Sum(l =>
            l.CppEmployee + l.Cpp2Employee + l.EiEmployee + l.FederalTax + l.ProvincialTax));

        decimal employer = runs.Sum(r => r.Lines.Sum(l =>
            l.CppEmployer + l.Cpp2Employer + l.EiEmployer));

        Assert.Equal(withheld + employer, runs.Sum(r => r.TotalRemittance));
    }

    [Fact]
    public void WhatTheEmployerPaysOut_EqualsNetPayPlusTheRemittance()
    {
        // Two withdrawals leave the bank: the net pay and, later, the CRA payment. Together
        // they have to equal the total cost of the payroll, or the books will not close.
        CompanyData data = DataWith(156000m);
        List<PayRun> runs = RunTheHalfYear(data, new PayrollService());

        decimal paidOut = runs.Sum(r => r.TotalNetPay) + runs.Sum(r => r.TotalRemittance);

        Assert.Equal(runs.Sum(r => r.TotalCost), paidOut);
    }

    #endregion
}
