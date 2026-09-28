using System.Text.Json;
using System.Text.Json.Serialization;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the pay run orchestration.
///
/// The year-to-date logic matters most. The calculator takes those totals as an input and
/// cannot check them, so a mistake here silently produces wrong deductions for anyone
/// approaching an annual ceiling, which is exactly where payroll errors cost real money.
/// </summary>
public class PayrollServiceTests
{
    private static readonly DateTime PayDate = new(2026, 8, 14);

    private static CompanyData DataWithEmployee(string id = "EMP-001") => new()
    {
        Employees =
        {
            new Employee
            {
                Id = id,
                Name = "Test Person",
                Province = "AB",
                PayType = PayType.Salary,
                PayRate = 52000m,
                PayFrequency = PayFrequency.Biweekly,
            },
        },
    };

    private static PayRun ApprovedRun(string id, DateTime payDate, string employeeId, decimal gross,
                                      decimal cpp, decimal ei) => new()
    {
        Id = id,
        PayDate = payDate,
        Status = PayRunStatus.Approved,
        Lines =
        {
            new PayRunLine
            {
                EmployeeId = employeeId,
                GrossPay = gross,
                CppEmployee = cpp,
                EiEmployee = ei,
                NetPay = gross - cpp - ei,
            },
        },
    };

    [Fact]
    public void CreateDraft_ProducesALineForEveryActiveEmployee()
    {
        CompanyData data = DataWithEmployee();
        data.Employees.Add(new Employee { Id = "EMP-002", Name = "Archived", Province = "AB", IsArchived = true });

        PayRun? run = new PayrollService().CreateDraft(data, PayDate, PayDate.AddDays(-13), PayDate);

        Assert.NotNull(run);
        Assert.Single(run.Lines);
        Assert.Equal("EMP-001", run.Lines[0].EmployeeId);
    }

    [Fact]
    public void CreateDraft_PrefillsSalariedPayFromTheAnnualRate()
    {
        PayRun? run = new PayrollService().CreateDraft(DataWithEmployee(), PayDate, PayDate.AddDays(-13), PayDate);

        // 52,000 a year over 26 periods.
        Assert.Equal(2000m, run!.Lines[0].BasePay);
    }

    [Fact]
    public void CreateDraft_RecordsWhichRateEditionWasUsed()
    {
        PayRun? run = new PayrollService().CreateDraft(DataWithEmployee(), PayDate, PayDate.AddDays(-13), PayDate);

        Assert.Equal("2026-07", run!.RateEditionId);
    }

    [Fact]
    public void CreateDraft_ReturnsNullWhenNoRateEditionCoversThePayDate()
    {
        // Deliberately not a fallback to the nearest edition: calculating with the wrong rates
        // produces figures that look plausible and are wrong.
        PayRun? run = new PayrollService()
            .CreateDraft(DataWithEmployee(), new DateTime(2030, 3, 1), new DateTime(2030, 2, 16), new DateTime(2030, 3, 1));

        Assert.Null(run);
    }

    #region Year to date

    [Fact]
    public void YearToDate_IsZeroWithNoPriorRuns()
    {
        PayrollYearToDate ytd = new PayrollService().YearToDateFor(DataWithEmployee(), "EMP-001");

        Assert.Equal(0m, ytd.CppEmployee);
        Assert.Equal(0m, ytd.EiEmployee);
        Assert.Equal(0m, ytd.PensionableEarnings);
    }

    [Fact]
    public void YearToDate_AccumulatesApprovedRuns()
    {
        CompanyData data = DataWithEmployee();
        data.PayRuns.Add(ApprovedRun("PR-0001", new DateTime(2026, 1, 16), "EMP-001", 2000m, 110.99m, 32.60m));
        data.PayRuns.Add(ApprovedRun("PR-0002", new DateTime(2026, 1, 30), "EMP-001", 2000m, 110.99m, 32.60m));

        PayrollYearToDate ytd = new PayrollService().YearToDateFor(data, "EMP-001");

        Assert.Equal(221.98m, ytd.CppEmployee);
        Assert.Equal(65.20m, ytd.EiEmployee);
        Assert.Equal(4000m, ytd.PensionableEarnings);
    }

    [Fact]
    public void YearToDate_IgnoresDrafts()
    {
        // An unapproved run must not inflate the totals, or the next run under-deducts.
        CompanyData data = DataWithEmployee();
        PayRun draft = ApprovedRun("PR-0001", new DateTime(2026, 1, 16), "EMP-001", 2000m, 110.99m, 32.60m);
        draft.Status = PayRunStatus.Draft;
        data.PayRuns.Add(draft);

        PayrollYearToDate ytd = new PayrollService().YearToDateFor(data, "EMP-001");

        Assert.Equal(0m, ytd.CppEmployee);
    }

    [Fact]
    public void YearToDate_IsUnaffectedByARunThatWasVoided()
    {
        // Voiding has to leave the year-to-date exactly where it was before the run existed.
        //
        // Note this goes through Void() rather than setting the status by hand. The reversal
        // is half the mechanism: the voided run still counts, and the reversal's negative
        // amounts are what cancel it. A Void status on its own is not a state the service can
        // produce.
        CompanyData data = DataWithEmployee();
        data.PayRuns.Add(ApprovedRun("PR-0001", new DateTime(2026, 1, 9), "EMP-001", 2000m, 110.99m, 32.60m));

        PayRun second = ApprovedRun("PR-0002", new DateTime(2026, 1, 23), "EMP-001", 2000m, 110.99m, 32.60m);
        data.PayRuns.Add(second);

        var service = new PayrollService();
        service.Void(data, second);

        PayrollYearToDate ytd = service.YearToDateFor(data, "EMP-001");

        // Just the first run. The second and its reversal net out.
        Assert.Equal(110.99m, ytd.CppEmployee);
        Assert.Equal(32.60m, ytd.EiEmployee);
        Assert.Equal(2000m, ytd.PensionableEarnings);
    }

    [Fact]
    public void YearToDate_IgnoresOtherCalendarYears()
    {
        CompanyData data = DataWithEmployee();
        data.PayRuns.Add(ApprovedRun("PR-0001", new DateTime(2025, 12, 19), "EMP-001", 2000m, 110.99m, 32.60m));

        PayRun? current = new PayrollService().CreateDraft(data, PayDate, PayDate.AddDays(-13), PayDate);
        PayrollYearToDate ytd = new PayrollService().YearToDateFor(data, "EMP-001", current);

        Assert.Equal(0m, ytd.CppEmployee);
    }

    [Fact]
    public void YearToDate_IgnoresOtherEmployees()
    {
        CompanyData data = DataWithEmployee();
        data.PayRuns.Add(ApprovedRun("PR-0001", new DateTime(2026, 1, 16), "EMP-999", 5000m, 300m, 80m));

        PayrollYearToDate ytd = new PayrollService().YearToDateFor(data, "EMP-001");

        Assert.Equal(0m, ytd.CppEmployee);
    }

    #endregion

    #region Approve and void

    [Fact]
    public void Approve_LocksTheRun()
    {
        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 2000m, 110.99m, 32.60m);
        run.Status = PayRunStatus.Draft;

        new PayrollService().Approve(run);

        Assert.Equal(PayRunStatus.Approved, run.Status);
        Assert.NotNull(run.ApprovedAt);
    }

    [Fact]
    public void ApproveAndRecord_WritesOneExpensePerEmployeeAtNetPay()
    {
        // Net rather than gross, so the books mirror the bank. The employer makes two separate
        // withdrawals, the net pay now and the CRA remittance later, and recording gross here
        // would count the deductions twice.
        CompanyData data = DataWithEmployee();
        data.Employees.Add(new Employee { Id = "EMP-002", Name = "Second Person", Province = "AB" });

        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 2000m, 110.99m, 32.60m);
        run.Lines.Add(new PayRunLine { EmployeeId = "EMP-002", EmployeeName = "Second Person", NetPay = 1500m });
        run.Status = PayRunStatus.Draft;

        List<Expense> created = new PayrollService().ApproveAndRecord(data, run);

        Assert.Equal(2, created.Count);
        Assert.Equal(2, data.Expenses.Count);
        Assert.Equal(1856.41m, created[0].Total);
        Assert.Equal(1500m, created[1].Total);
        Assert.Equal(PayDate, created[0].Date);
    }

    [Fact]
    public void ApproveAndRecord_LinksEachExpenseBackToItsLine()
    {
        // Without the link a void cannot find what to remove.
        CompanyData data = DataWithEmployee();
        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 2000m, 110.99m, 32.60m);
        run.Status = PayRunStatus.Draft;

        new PayrollService().ApproveAndRecord(data, run);

        Assert.NotNull(run.Lines[0].ExpenseId);
        Assert.Contains(data.Expenses, e => e.Id == run.Lines[0].ExpenseId);
    }

    [Fact]
    public void ApproveAndRecord_DoesNothingToAnAlreadyApprovedRun()
    {
        // Guards against approving twice and paying everyone a second time in the books.
        CompanyData data = DataWithEmployee();
        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 2000m, 110.99m, 32.60m);

        List<Expense> created = new PayrollService().ApproveAndRecord(data, run);

        Assert.Empty(created);
        Assert.Empty(data.Expenses);
    }

    [Fact]
    public void ApproveAndRecord_SkipsALineWithNothingToPay()
    {
        CompanyData data = DataWithEmployee();
        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 0m, 0m, 0m);
        run.Status = PayRunStatus.Draft;

        new PayrollService().ApproveAndRecord(data, run);

        Assert.Empty(data.Expenses);
    }

    [Fact]
    public void Void_RemovesTheWageExpensesTheRunCreated()
    {
        // A voided run is one whose money never left, so the expense goes with it. Leaving a
        // matching plus and minus pair would double the transaction count on every report.
        CompanyData data = DataWithEmployee();
        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 2000m, 110.99m, 32.60m);
        run.Status = PayRunStatus.Draft;

        var service = new PayrollService();
        service.ApproveAndRecord(data, run);
        Assert.Single(data.Expenses);

        service.Void(data, run);

        Assert.Empty(data.Expenses);
        Assert.Null(run.Lines[0].ExpenseId);
    }

    // Undoing an approval took the wages out and left their queued conversion behind, which a pass
    // then dropped as having nothing to convert. Redo put the wages back still waiting, with nothing
    // queued to convert them, so they counted as 0 for good.
    [Fact]
    public void UndoAndRedoOfWages_TakesTheQueuedConversionOutAndBack()
    {
        CompanyData data = DataWithEmployee();
        var wages = new Expense { Id = "PUR-2026-00001", Date = PayDate, OriginalCurrency = "CAD", Total = 1856.41m };
        UsdConversion.Apply(data, wages, rate: null);
        data.Expenses.Add(wages);

        PayrollService.RemoveWageExpenses(data, [wages]);

        Assert.Empty(data.Expenses);
        Assert.Empty(data.PendingConversions);

        PayrollService.RestoreWageExpenses(data, [wages]);

        Assert.Same(wages, Assert.Single(data.Expenses));
        var entry = Assert.Single(data.PendingConversions);
        Assert.Equal((wages.Id, "Expense", 1856.41m, PayDate), (entry.TransactionId, entry.TransactionType, entry.Total, entry.TransactionDate));
    }

    // Undoing an import restores the company from a snapshot while the pay run's undo still holds
    // the wages. When the restore swapped every expense for a copy, undoing the approval left the
    // wages in the books, and redoing it added a second expense with the same id.
    [Fact]
    public void UndoAndRedoOfWages_AfterASnapshotRestore_TakeAndReturnTheSameWages()
    {
        CompanyData data = DataWithEmployee();
        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 2000m, 110.99m, 32.60m);
        run.Status = PayRunStatus.Draft;
        List<Expense> wages = new PayrollService().ApproveAndRecord(data, run);
        App.RestoreCompanyDataFromSnapshot(data, App.CreateCompanyDataSnapshot(data));
        Assert.Same(wages[0], Assert.Single(data.Expenses));

        PayrollService.RemoveWageExpenses(data, wages);

        Assert.Empty(data.Expenses);

        PayrollService.RestoreWageExpenses(data, wages);
        App.RestoreCompanyDataFromSnapshot(data, App.CreateCompanyDataSnapshot(data));
        PayrollService.RestoreWageExpenses(data, wages);

        Assert.Same(wages[0], Assert.Single(data.Expenses));
    }

    [Fact]
    public void Void_DropsTheWagesQueuedConversion()
    {
        CompanyData data = DataWithEmployee();
        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 2000m, 110.99m, 32.60m);
        run.Status = PayRunStatus.Draft;
        var service = new PayrollService();
        Expense wages = Assert.Single(service.ApproveAndRecord(data, run));
        wages.OriginalCurrency = "CAD";
        UsdConversion.Apply(data, wages, rate: null);

        service.Void(data, run);

        Assert.Empty(data.Expenses);
        Assert.Empty(data.PendingConversions);
    }

    [Fact]
    public void Void_LeavesUnrelatedExpensesAlone()
    {
        CompanyData data = DataWithEmployee();
        data.Expenses.Add(new Expense { Id = "PUR-2026-00099", Total = 40m });

        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 2000m, 110.99m, 32.60m);
        run.Status = PayRunStatus.Draft;

        var service = new PayrollService();
        service.ApproveAndRecord(data, run);
        service.Void(data, run);

        Assert.Single(data.Expenses);
        Assert.Equal("PUR-2026-00099", data.Expenses[0].Id);
    }

    [Fact]
    public void Recalculate_DoesNothingToAnApprovedRun()
    {
        // Approved figures are frozen so a historical run always agrees with the pay stub the
        // employee was given.
        CompanyData data = DataWithEmployee();
        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 2000m, 110.99m, 32.60m);
        run.Lines[0].BasePay = 9999m;
        data.PayRuns.Add(run);

        new PayrollService().Recalculate(data, run);

        Assert.Equal(2000m, run.Lines[0].GrossPay);
    }

    [Fact]
    public void Void_WritesAReversalAndKeepsTheOriginal()
    {
        CompanyData data = DataWithEmployee();
        PayRun run = ApprovedRun("PR-0001", PayDate, "EMP-001", 2000m, 110.99m, 32.60m);
        data.PayRuns.Add(run);

        PayRun? reversal = new PayrollService().Void(data, run);

        Assert.NotNull(reversal);
        Assert.Equal("PR-0001", reversal.VoidsPayRunId);
        Assert.Equal(-2000m, reversal.Lines[0].GrossPay);
        Assert.Equal(-110.99m, reversal.Lines[0].CppEmployee);
        Assert.Equal(PayRunStatus.Void, run.Status);
        Assert.Equal(2, data.PayRuns.Count);
    }

    [Fact]
    public void Void_AndItsReversalCancelOutInTheYearToDate()
    {
        CompanyData data = DataWithEmployee();
        PayRun run = ApprovedRun("PR-0001", new DateTime(2026, 1, 16), "EMP-001", 2000m, 110.99m, 32.60m);
        data.PayRuns.Add(run);

        var service = new PayrollService();
        service.Void(data, run);

        PayrollYearToDate ytd = service.YearToDateFor(data, "EMP-001");

        // Exactly zero. The voided run and its reversal are both counted and cancel. Skipping
        // the voided run as well would subtract the same payroll twice and leave the year to
        // date at -110.99, so the next run would be calculated against a ceiling that had
        // moved further away than it ever was.
        Assert.Equal(0m, ytd.CppEmployee);
        Assert.Equal(0m, ytd.EiEmployee);
        Assert.Equal(0m, ytd.PensionableEarnings);
    }

    [Fact]
    public void ADraftRunIsStillIgnoredEvenThoughVoidedRunsAreNot()
    {
        // The two exclusions are different in kind and it would be easy to widen one into the
        // other: a void has happened and been reversed, a draft has not happened at all.
        CompanyData data = DataWithEmployee();
        PayRun draft = ApprovedRun("PR-0001", new DateTime(2026, 1, 16), "EMP-001", 2000m, 110.99m, 32.60m);
        draft.Status = PayRunStatus.Draft;
        data.PayRuns.Add(draft);

        Assert.Equal(0m, new PayrollService().YearToDateFor(data, "EMP-001").CppEmployee);
    }

    #endregion

    #region TD1 claims

    private static (decimal Federal, decimal Provincial) TaxFor(Employee employee)
    {
        var data = new CompanyData { Employees = { employee } };
        PayRun run = new PayrollService().CreateDraft(data, PayDate, PayDate.AddDays(-13), PayDate)!;
        return (run.Lines[0].FederalTax, run.Lines[0].ProvincialTax);
    }

    [Fact]
    public void AZeroClaim_ReachesTheCalculator()
    {
        Employee claimsNothing = DataWithEmployee().Employees[0];
        claimsNothing.FederalClaimIsZero = true;
        claimsNothing.ProvincialClaimIsZero = true;

        (decimal federal, decimal provincial) = TaxFor(claimsNothing);
        (decimal basicFederal, decimal basicProvincial) = TaxFor(DataWithEmployee().Employees[0]);

        Assert.True(federal > basicFederal);
        Assert.True(provincial > basicProvincial);
    }

    [Fact]
    public void AnEmployeeFromAFileWrittenBeforeZeroClaims_StillGetsTheBasicPersonalAmount()
    {
        // Every existing file stores 0 for "no TD1 on file". Reading one must not turn that into
        // a claim of nothing, which would raise the tax of every such employee on the next run.
        // The options are the ones FileService reads a company file with.
        const string json = """
            {"id":"EMP-001","name":"Test Person","province":"AB","payType":"Salary","payRate":52000,
             "payFrequency":"Biweekly","federalClaimAmount":0,"provincialClaimAmount":0}
            """;
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };

        Employee loaded = JsonSerializer.Deserialize<Employee>(json, options)!;

        PayrollRateTable rates = new PayrollRateService().GetForDate(PayDate)!;
        Employee explicitBasic = DataWithEmployee().Employees[0];
        explicitBasic.FederalClaimAmount = rates.Federal.BasicPersonalAmount.Maximum;
        explicitBasic.ProvincialClaimAmount = rates.Provinces["AB"].BasicPersonalAmount.Maximum;

        Assert.False(loaded.FederalClaimIsZero);
        Assert.False(loaded.ProvincialClaimIsZero);
        Assert.Equal(TaxFor(explicitBasic), TaxFor(loaded));
    }

    #endregion

    #region What is owed to CRA next

    /// <summary>A run whose lines carry a known remittance, so the totals are readable.</summary>
    private static PayRun RunOn(string id, DateTime payDate) =>
        ApprovedRun(id, payDate, "EMP-001", 2000m, 100m, 30m);

    [Fact]
    public void TheNextRemittance_CoversTheMonthBeforeItsDeadline()
    {
        // A regular remitter sends what they withheld in a month by the 15th of the NEXT month.
        // So in the middle of August, what is coming due is August's payroll, on 15 September.
        var data = new List<PayRun> { RunOn("PR-0001", new DateTime(2026, 8, 14)) };

        (decimal amount, DateTime due) = PayrollService.NextRemittance(data, new DateTime(2026, 8, 16));

        Assert.Equal(new DateTime(2026, 9, 15), due);
        Assert.True(amount > 0);
    }

    [Fact]
    public void BeforeTheFifteenth_TheDeadlineIsStillLastMonths()
    {
        // On 10 September the 15 September deadline has not passed, and what it covers is
        // AUGUST's payroll. Showing September's here would name a figure that is not yet due and
        // hide the one that is.
        var data = new List<PayRun>
        {
            RunOn("PR-0001", new DateTime(2026, 8, 14)),
            RunOn("PR-0002", new DateTime(2026, 9, 4)),
        };

        (decimal august, DateTime due) = PayrollService.NextRemittance(data, new DateTime(2026, 9, 10));

        Assert.Equal(new DateTime(2026, 9, 15), due);
        Assert.Equal(PayrollService.NextRemittance(
            [RunOn("PR-0001", new DateTime(2026, 8, 14))], new DateTime(2026, 9, 10)).Amount, august);
    }

    [Fact]
    public void AfterTheFifteenth_ItRollsOnToTheNextDeadline()
    {
        var data = new List<PayRun>
        {
            RunOn("PR-0001", new DateTime(2026, 8, 14)),
            RunOn("PR-0002", new DateTime(2026, 9, 4)),
        };

        (decimal amount, DateTime due) = PayrollService.NextRemittance(data, new DateTime(2026, 9, 16));

        Assert.Equal(new DateTime(2026, 10, 15), due);
        Assert.Equal(RunOn("PR-0002", new DateTime(2026, 9, 4)).TotalRemittance, amount);
    }

    [Fact]
    public void ADeadlineOnTheFifteenthItself_HasNotPassedYet()
    {
        // The boundary, and the day it matters most: the payment is due today, not overdue.
        var data = new List<PayRun> { RunOn("PR-0001", new DateTime(2026, 8, 14)) };

        (_, DateTime due) = PayrollService.NextRemittance(data, new DateTime(2026, 9, 15));

        Assert.Equal(new DateTime(2026, 9, 15), due);
    }

    [Fact]
    public void NoPayrollInTheCoveredMonth_OwesNothing()
    {
        var data = new List<PayRun> { RunOn("PR-0001", new DateTime(2026, 3, 14)) };

        (decimal amount, _) = PayrollService.NextRemittance(data, new DateTime(2026, 8, 16));

        Assert.Equal(0m, amount);
    }

    [Fact]
    public void ADraftRun_IsNotOwedYet()
    {
        PayRun draft = RunOn("PR-0001", new DateTime(2026, 8, 14));
        draft.Status = PayRunStatus.Draft;

        (decimal amount, _) = PayrollService.NextRemittance([draft], new DateTime(2026, 8, 16));

        Assert.Equal(0m, amount);
    }

    [Fact]
    public void AVoidedRunAndItsReversal_CancelToNothingOwed()
    {
        // Both are counted, exactly as the year-to-date figures count them, so the pair nets to
        // zero rather than the void being skipped and its reversal subtracting a second time.
        var data = new CompanyData();
        data.PayRuns.Add(RunOn("PR-0001", new DateTime(2026, 8, 14)));
        new PayrollService().Void(data, data.PayRuns[0]);

        (decimal amount, _) = PayrollService.NextRemittance(data.PayRuns, new DateTime(2026, 8, 16));

        Assert.Equal(0m, amount);
    }

    #endregion

    #region Who is owed what

    /// <summary>
    /// Revenu Quebec collects Quebec income tax, QPP and QPIP; CRA collects federal tax and EI.
    /// Counting it all as CRA's asked the employer to send CRA money CRA is not owed, and never
    /// mentioned the payment Revenu Quebec was waiting for.
    /// </summary>
    private static PayRunLine QuebecLine() => new()
    {
        EmployeeId = "EMP-001",
        Province = "QC",
        GrossPay = 2000m,
        CppEmployee = 100m,
        CppEmployer = 100m,
        Cpp2Employee = 5m,
        Cpp2Employer = 5m,
        EiEmployee = 25m,
        EiEmployer = 35m,
        QpipEmployee = 9m,
        QpipEmployer = 12.60m,
        FederalTax = 180m,
        ProvincialTax = 140m,
    };

    [Fact]
    public void AQuebecLinesRemittance_IsSplitBetweenCraAndRevenuQuebec()
    {
        PayRunLine line = QuebecLine();

        Assert.Equal(180m + 25m + 35m, line.CraRemittance);
        Assert.Equal(140m + 100m + 100m + 5m + 5m + 9m + 12.60m, line.QuebecRemittance);
        Assert.Equal(line.TotalRemittance, line.CraRemittance + line.QuebecRemittance);
    }

    [Fact]
    public void ALineOutsideQuebec_IsOwedEntirelyToCra()
    {
        PayRunLine line = QuebecLine();
        line.Province = "ON";
        line.QpipEmployee = 0m;
        line.QpipEmployer = 0m;

        Assert.Equal(line.TotalRemittance, line.CraRemittance);
        Assert.Equal(0m, line.QuebecRemittance);
    }

    [Fact]
    public void TheNextRemittanceToCra_LeavesOutWhatIsOwedToRevenuQuebec()
    {
        var run = new PayRun
        {
            Id = "PR-0001",
            PayDate = new DateTime(2026, 8, 14),
            Status = PayRunStatus.Approved,
            Lines = { QuebecLine() },
        };

        (decimal cra, DateTime due) = PayrollService.NextRemittance([run], new DateTime(2026, 8, 16));
        (decimal craAgain, decimal quebec, DateTime dueAgain) =
            PayrollService.NextRemittanceByAgency([run], new DateTime(2026, 8, 16));

        Assert.Equal(240m, cra);
        Assert.Equal(cra, craAgain);
        Assert.Equal(371.60m, quebec);
        Assert.Equal(due, dueAgain);
    }

    #endregion

    #region Remitter types

    /// <summary>
    /// Every case here is read off CRA's own table of remitter types. The app assumed regular
    /// for everyone, which is right for most small employers and reports a deadline weeks late
    /// for an accelerated one, and CRA charges 3% to 10% on a late remittance.
    /// </summary>
    [Theory]
    // Regular: the 15th of the following month.
    [InlineData(RemitterType.Regular, "2026-08-16", "2026-09-15")]
    [InlineData(RemitterType.Regular, "2026-09-10", "2026-09-15")]
    // Quarterly: 15 April, July, October and January.
    [InlineData(RemitterType.Quarterly, "2026-08-16", "2026-10-15")]
    [InlineData(RemitterType.Quarterly, "2026-11-02", "2027-01-15")]
    [InlineData(RemitterType.Quarterly, "2026-04-15", "2026-04-15")]
    // Accelerated threshold 1: the 25th for the first half, the 10th of the next month for the
    // second. On 16 August the first half of August has closed and its deadline is 25 August.
    [InlineData(RemitterType.AcceleratedThreshold1, "2026-08-16", "2026-08-25")]
    [InlineData(RemitterType.AcceleratedThreshold1, "2026-08-26", "2026-09-10")]
    public void TheDeadline_FollowsTheRemitterType(RemitterType type, string today, string expected) =>
        Assert.Equal(
            DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            PayrollService.NextRemittance([], DateTime.Parse(today, System.Globalization.CultureInfo.InvariantCulture), type).DueDate);

    /// <summary>
    /// Threshold 2 is the 3rd WORKING day after each period, so a period ending on a Friday does
    /// not fall due on the Monday. 7 August 2026 is a Friday, so counting Mon, Tue, Wed gives
    /// 12 August.
    /// </summary>
    [Fact]
    public void AcceleratedThreshold2_CountsWorkingDaysNotCalendarDays()
    {
        DateTime seventh = new(2026, 8, 7);
        Assert.Equal(DayOfWeek.Friday, seventh.DayOfWeek);

        (_, DateTime due) = PayrollService.NextRemittance(
            [], new DateTime(2026, 8, 8), RemitterType.AcceleratedThreshold2);

        Assert.Equal(new DateTime(2026, 8, 12), due);
    }

    /// <summary>
    /// The amount has to follow the period, not just the deadline. A quarterly remitter's October
    /// deadline covers July, August and September together, so a run in each must all be counted.
    /// </summary>
    [Fact]
    public void AQuarterlyRemittance_CoversTheWholeQuarter()
    {
        var data = new List<PayRun>
        {
            RunOn("PR-0001", new DateTime(2026, 7, 10)),
            RunOn("PR-0002", new DateTime(2026, 8, 10)),
            RunOn("PR-0003", new DateTime(2026, 9, 10)),

            // October belongs to the NEXT quarter and must not be swept in.
            RunOn("PR-0004", new DateTime(2026, 10, 10)),
        };

        (decimal quarterly, DateTime due) = PayrollService.NextRemittance(
            data, new DateTime(2026, 10, 1), RemitterType.Quarterly);

        (decimal monthly, _) = PayrollService.NextRemittance(
            data, new DateTime(2026, 10, 1));

        Assert.Equal(new DateTime(2026, 10, 15), due);

        // Three months against the regular remitter's one.
        Assert.Equal(monthly * 3m, quarterly);
    }

    /// <summary>
    /// Which deadline the deductions on a given pay DATE fall under, which is a different question
    /// from which deadline has not passed yet.
    ///
    /// Asking NextRemittance with a pay date answers the wrong one: a regular remitter paid on
    /// 10 August is inside July's deadline of 15 August, so it returns that, when August's
    /// deductions are due 15 September.
    /// </summary>
    [Theory]
    [InlineData(RemitterType.Regular, "2026-08-10", "2026-09-15")]
    [InlineData(RemitterType.Regular, "2026-08-20", "2026-09-15")]
    [InlineData(RemitterType.Regular, "2026-08-31", "2026-09-15")]
    [InlineData(RemitterType.Quarterly, "2026-08-10", "2026-10-15")]
    [InlineData(RemitterType.AcceleratedThreshold1, "2026-08-10", "2026-08-25")]
    [InlineData(RemitterType.AcceleratedThreshold1, "2026-08-20", "2026-09-10")]
    public void TheDeadlineForAPayDate_IsThePeriodItFallsIn(RemitterType type, string payDate, string expected) =>
        Assert.Equal(
            DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            PayrollService.RemittanceDueFor(
                DateTime.Parse(payDate, System.Globalization.CultureInfo.InvariantCulture), type));

    /// <summary>The default has to stay regular, or every existing company file changes meaning.</summary>
    [Fact]
    public void TheDefault_IsStillRegular()
    {
        var remitterType = typeof(PayrollService).GetMethod(nameof(PayrollService.NextRemittance))!
            .GetParameters()
            .Single(p => p.ParameterType == typeof(RemitterType));

        Assert.Equal(RemitterType.Regular, (RemitterType)remitterType.DefaultValue!);
    }

    #endregion
}
