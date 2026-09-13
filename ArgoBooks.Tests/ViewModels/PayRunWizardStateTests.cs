using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// What the run payroll wizard holds on to between steps.
///
/// Step 1 rebuilds the amount rows every time it is left, so stepping Back to correct a date
/// and pressing Next again threw away every hour, bonus and vacation figure already typed,
/// with nothing on screen to say so. On a run with a dozen employees that is a full re-entry.
/// </summary>
public class PayRunWizardStateTests : ModalViewModelTestBase
{
    private static Employee Salaried(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Province = "AB",
        PayType = PayType.Salary,
        PayRate = 62_400m,
        PayFrequency = PayFrequency.Biweekly,
    };

    private static Employee Hourly(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Province = "AB",
        PayType = PayType.Hourly,
        PayRate = 28m,
        PayFrequency = PayFrequency.Biweekly,
    };

    private PayRunModalsViewModel OnStepTwo()
    {
        Company.Employees.Add(Salaried("EMP-001", "Dana Smith"));
        Company.Employees.Add(Hourly("EMP-002", "Chris Okafor"));

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PayDate = new DateTimeOffset(new DateTime(2026, 8, 14));
        vm.PeriodStart = new DateTimeOffset(new DateTime(2026, 8, 1));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 14));
        vm.NextCommand.Execute(null);

        Assert.Equal(2, vm.Step);
        Assert.Equal(2, vm.AmountRows.Count);

        return vm;
    }

    [Fact]
    public void SteppingBackAndForwardAgain_KeepsEveryTypedAmount()
    {
        PayRunModalsViewModel vm = OnStepTwo();

        vm.AmountRows[0].Bonus = "5000";
        vm.AmountRows[0].VacationPay = "240";
        vm.AmountRows[1].Hours = "72.5";
        vm.AmountRows[1].Bonus = "300";

        vm.BackCommand.Execute(null);
        Assert.Equal(1, vm.Step);

        // The reason anyone steps back: a date was wrong.
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 15));
        vm.NextCommand.Execute(null);

        Assert.Equal(2, vm.Step);
        Assert.Equal(2, vm.AmountRows.Count);

        Assert.Equal("5000", vm.AmountRows[0].Bonus);
        Assert.Equal("240", vm.AmountRows[0].VacationPay);
        Assert.Equal("72.5", vm.AmountRows[1].Hours);
        Assert.Equal("300", vm.AmountRows[1].Bonus);
    }

    /// <summary>
    /// Restored by employee, not by position. Dropping someone from the run on the way back
    /// moves everyone below them up a row, and a restore keyed on position would hand one
    /// person's bonus to somebody else.
    /// </summary>
    [Fact]
    public void TypedAmounts_FollowTheEmployeeRatherThanTheRow()
    {
        PayRunModalsViewModel vm = OnStepTwo();

        vm.AmountRows.First(r => r.EmployeeId == "EMP-002").Bonus = "300";

        vm.BackCommand.Execute(null);
        vm.SelectableEmployees.First(e => e.Id == "EMP-001").IsSelected = false;
        vm.NextCommand.Execute(null);

        PayRunAmountRow row = Assert.Single(vm.AmountRows);
        Assert.Equal("EMP-002", row.EmployeeId);
        Assert.Equal("300", row.Bonus);
    }

    /// <summary>Closing and opening again is a new run, so nothing carries over.</summary>
    [Fact]
    public void ClosingTheModal_DropsWhatWasTyped()
    {
        PayRunModalsViewModel vm = OnStepTwo();
        vm.AmountRows[0].Bonus = "5000";

        vm.CloseRunModalCommand.Execute(null);
        vm.OpenRunModal();

        Assert.Equal(1, vm.Step);
        Assert.Empty(vm.AmountRows);
    }

    /// <summary>
    /// A period that ends before it starts is refused once, beside the control.
    /// </summary>
    [Fact]
    public void APeriodEndingBeforeItStarts_IsRefusedOnceAndBlocksNext()
    {
        Company.Employees.Add(Salaried("EMP-001", "Dana Smith"));

        var vm = new PayRunModalsViewModel();
        vm.OpenRunModal();
        vm.PayDate = new DateTimeOffset(new DateTime(2026, 8, 14));
        vm.PeriodStart = new DateTimeOffset(new DateTime(2026, 8, 14));
        vm.PeriodEnd = new DateTimeOffset(new DateTime(2026, 8, 1));

        Assert.NotEmpty(vm.PeriodError);

        vm.NextCommand.Execute(null);

        Assert.Equal(1, vm.Step);
        Assert.Empty(vm.BlockingError);
    }
}
