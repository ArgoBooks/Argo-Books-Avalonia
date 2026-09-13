using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the run payroll modal.
///
/// The empty cases are the ones worth pinning. An employer opening this for the first time, and
/// one who has archived their last employee, both saw an empty panel that read like a failure to
/// load rather than an answer, and the two are fixed differently.
/// </summary>
public class PayRunModalsViewModelTests : ModalViewModelTestBase
{
    private static Employee Person(string id = "EMP-001", string name = "Dana Smith", bool archived = false) => new()
    {
        Id = id,
        Name = name,
        Province = "AB",
        PayType = PayType.Salary,
        PayRate = 52000m,
        PayFrequency = PayFrequency.Biweekly,
        IsArchived = archived,
    };

    [Fact]
    public void ReopeningTheModal_LeavesNothingFromTheLastRun()
    {
        Company.Employees.Add(Person());

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.NextCommand.Execute(null);

        Assert.Equal(2, vm.Step);
        Assert.NotEmpty(vm.AmountRows);

        vm.CloseRunModalCommand.Execute(null);
        vm.OpenRunModal();

        Assert.Equal(1, vm.Step);
        Assert.Empty(vm.AmountRows);
        Assert.Empty(vm.ReviewRows);
    }

    [Fact]
    public void WithNoEmployeesAtAll_ItSaysToAddOne()
    {
        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();

        Assert.Empty(vm.SelectableEmployees);
        Assert.True(vm.HasNobodyToPay);
        Assert.Contains("no employees yet", vm.NoEmployeesMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithEveryEmployeeArchived_ItSaysToRestoreOne()
    {
        // Not "no employees": they have staff, and sending them off to add someone they already
        // have is the wrong instruction.
        Company.Employees.Add(Person(archived: true));
        Company.Employees.Add(Person("EMP-002", "Alex Jones", archived: true));

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();

        Assert.Empty(vm.SelectableEmployees);
        Assert.True(vm.HasNobodyToPay);
        Assert.Contains("archived", vm.NoEmployeesMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no employees yet", vm.NoEmployeesMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithSomebodyToPay_ThereIsNoMessage()
    {
        Company.Employees.Add(Person());

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();

        Assert.Single(vm.SelectableEmployees);
        Assert.False(vm.HasNobodyToPay);
        Assert.Empty(vm.NoEmployeesMessage);
    }

    [Fact]
    public void ArchivedStaffAreExcluded_ButDoNotSuppressTheActiveOnes()
    {
        Company.Employees.Add(Person("EMP-001", "Dana Smith"));
        Company.Employees.Add(Person("EMP-002", "Alex Jones", archived: true));

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();

        Assert.Single(vm.SelectableEmployees);
        Assert.Equal("EMP-001", vm.SelectableEmployees[0].Id);
        Assert.False(vm.HasNobodyToPay);
    }

    [Fact]
    public void ReopeningAfterAnEmployeeIsAdded_ClearsTheMessage()
    {
        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        Assert.True(vm.HasNobodyToPay);

        Company.Employees.Add(Person());
        vm.OpenRunModal();

        Assert.False(vm.HasNobodyToPay);
        Assert.Empty(vm.NoEmployeesMessage);
    }

    #region The pay period

    private static PayRun ApprovedRun(string id, DateTime start, DateTime end, string employeeId = "EMP-001") => new()
    {
        Id = id,
        PayDate = end,
        PeriodStart = start,
        PeriodEnd = end,
        Status = PayRunStatus.Approved,
        Lines = { new PayRunLine { EmployeeId = employeeId, EmployeeName = "Dana Smith", Province = "AB", GrossPay = 2000m, NetPay = 1600m } },
    };

    [Fact]
    public void APeriodThatEndsBeforeItStarts_IsRefused()
    {
        // Every annual figure is divided across the pay periods, so the period itself never
        // enters the arithmetic and a backwards one calculates perfectly happily. It only shows
        // up later, on a pay stub and in the 27 periods an ROE reads back.
        Company.Employees.Add(Person());

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PeriodStart = new DateTimeOffset(new DateTime(2026, 8, 14));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 1));

        Assert.NotEmpty(vm.PeriodError);

        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.Step);

        // Said once, next to the date that is wrong. Copying it into BlockingError as well put
        // the same sentence on screen twice, in two different places, the moment Next was
        // pressed.
        Assert.Empty(vm.BlockingError);
    }

    [Fact]
    public void APeriodOfASingleDay_IsAllowed()
    {
        // The boundary. A one day period is a real thing, so the check has to be "ends before it
        // starts" rather than "does not end after it starts".
        Company.Employees.Add(Person());

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PeriodStart = new DateTimeOffset(new DateTime(2026, 8, 14));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 14));

        Assert.Empty(vm.PeriodError);
    }

    [Fact]
    public void CorrectingTheDates_ClearsTheRefusal()
    {
        Company.Employees.Add(Person());

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2020, 1, 1));
        Assert.NotEmpty(vm.PeriodError);

        vm.PeriodEnd = vm.PeriodStart!.Value.AddDays(13);
        Assert.Empty(vm.PeriodError);
    }

    [Fact]
    public void APayDateNowhereNearThePeriod_IsWarnedAbout()
    {
        // A pay date in 2026 against a period in 2024 calculates happily, because the pay date
        // only picks the rate edition and the period never enters the arithmetic. It is also
        // exactly what a mistyped year looks like, and the year is the digit nobody re-reads.
        Company.Employees.Add(Person());

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PayDate = new DateTimeOffset(new DateTime(2026, 8, 16));
        vm.PeriodStart = new DateTimeOffset(new DateTime(2024, 6, 3));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2024, 6, 16));

        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        Assert.Equal(3, vm.Step);
        Assert.Contains(vm.Warnings, w => w.Contains("pay date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void APayDateShortlyAfterThePeriod_IsNormalAndSaysNothing()
    {
        // Paying a week or two after the period ends is how payroll usually works, so the check
        // has to be loose enough not to fire on the ordinary case.
        Company.Employees.Add(Person());

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PeriodStart = new DateTimeOffset(new DateTime(2026, 8, 3));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 16));
        vm.PayDate = new DateTimeOffset(new DateTime(2026, 8, 21));

        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        Assert.DoesNotContain(vm.Warnings, w => w.Contains("pay date", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Paying the same period twice

    [Fact]
    public void APeriodAlreadyPaid_IsWarnedAboutRatherThanBlocked()
    {
        // Deliberately a warning. A second run over the same period is usually a mistake and
        // occasionally exactly right: a correction, or a bonus paid separately. Blocking it would
        // be wrong the first time somebody needs one, and there is no way for the app to tell the
        // two apart.
        Company.Employees.Add(Person());
        Company.PayRuns.Add(ApprovedRun("PR-0001", new DateTime(2026, 8, 3), new DateTime(2026, 8, 16)));

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PayDate = new DateTimeOffset(new DateTime(2026, 8, 16));
        vm.PeriodStart = new DateTimeOffset(new DateTime(2026, 8, 3));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 16));

        vm.NextCommand.Execute(null);
        Assert.Equal(2, vm.Step);
        vm.NextCommand.Execute(null);

        Assert.Equal(3, vm.Step);
        Assert.True(vm.HasWarnings);
        Assert.Contains(vm.Warnings, w => w.Contains("PR-0001"));
    }

    [Fact]
    public void APeriodThatMerelyOverlaps_IsStillWorthSaying()
    {
        // Overlap rather than an exact match, because paying 3 to 16 August and then 10 to 23
        // August pays a week twice just as surely, and is harder to spot by eye.
        Company.Employees.Add(Person());
        Company.PayRuns.Add(ApprovedRun("PR-0001", new DateTime(2026, 8, 3), new DateTime(2026, 8, 16)));

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PayDate = new DateTimeOffset(new DateTime(2026, 8, 23));
        vm.PeriodStart = new DateTimeOffset(new DateTime(2026, 8, 10));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 23));

        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        Assert.Contains(vm.Warnings, w => w.Contains("PR-0001"));
    }

    [Fact]
    public void APeriodNobodyHasBeenPaidFor_SaysNothing()
    {
        Company.Employees.Add(Person());
        Company.PayRuns.Add(ApprovedRun("PR-0001", new DateTime(2026, 7, 6), new DateTime(2026, 7, 19)));

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PayDate = new DateTimeOffset(new DateTime(2026, 8, 16));
        vm.PeriodStart = new DateTimeOffset(new DateTime(2026, 8, 3));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 16));

        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        Assert.DoesNotContain(vm.Warnings, w => w.Contains("PR-0001"));
    }

    [Fact]
    public void AVoidedRun_DoesNotCountAsHavingPaidThePeriod()
    {
        // Voiding is how a run is undone, so a period whose only run was voided has not been paid
        // and warning about it would send the employer looking for something that is not there.
        Company.Employees.Add(Person());

        PayRun voided = ApprovedRun("PR-0001", new DateTime(2026, 8, 3), new DateTime(2026, 8, 16));
        voided.Status = PayRunStatus.Void;
        Company.PayRuns.Add(voided);

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PayDate = new DateTimeOffset(new DateTime(2026, 8, 16));
        vm.PeriodStart = new DateTimeOffset(new DateTime(2026, 8, 3));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 16));

        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        Assert.DoesNotContain(vm.Warnings, w => w.Contains("PR-0001"));
    }

    [Fact]
    public void APeriodPaidToSomebodyElse_IsNotThisEmployeesProblem()
    {
        // The overlap only matters for the people actually in this run.
        Company.Employees.Add(Person());
        Company.Employees.Add(Person("EMP-002", "Alex Jones"));
        Company.PayRuns.Add(ApprovedRun("PR-0001", new DateTime(2026, 8, 3), new DateTime(2026, 8, 16), "EMP-002"));

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        foreach (PayRunEmployeeSelection s in vm.SelectableEmployees)
        {
            s.IsSelected = s.Id == "EMP-001";
        }

        vm.PayDate = new DateTimeOffset(new DateTime(2026, 8, 16));
        vm.PeriodStart = new DateTimeOffset(new DateTime(2026, 8, 3));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 16));

        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        Assert.DoesNotContain(vm.Warnings, w => w.Contains("PR-0001"));
    }

    #endregion
}
