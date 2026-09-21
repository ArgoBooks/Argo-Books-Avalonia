using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the employee form's validation.
///
/// Only the validation is exercised. Saving needs an open company, and the form deliberately
/// sets its error messages and returns before it touches company data, so the rules can be
/// checked on their own.
/// </summary>
public class PayrollModalsViewModelTests
{
    private static PayrollModalsViewModel FormWith(DateTimeOffset? start, DateTimeOffset? end)
    {
        var vm = new PayrollModalsViewModel
        {
            Name = "Test Person",
            PayRate = "50000",
            StartDate = start,
            EndDate = end,
        };

        vm.SaveEmployeeCommand.Execute(null);
        return vm;
    }

    [Fact]
    public void AnEndDateBeforeTheStartDate_IsRejected()
    {
        // It would put pay periods outside the employment and make a record of employment
        // nonsense.
        PayrollModalsViewModel vm = FormWith(
            new DateTimeOffset(new DateTime(2026, 6, 1)),
            new DateTimeOffset(new DateTime(2026, 5, 1)));

        Assert.NotEmpty(vm.EndDateError);
        Assert.True(vm.IsEmployeeModalOpen == false || vm.EndDateError.Length > 0);
    }

    [Fact]
    public void AnEndDateAfterTheStartDate_IsAccepted()
    {
        PayrollModalsViewModel vm = FormWith(
            new DateTimeOffset(new DateTime(2026, 1, 5)),
            new DateTimeOffset(new DateTime(2026, 9, 30)));

        Assert.Empty(vm.EndDateError);
    }

    [Fact]
    public void AnEndDateOnTheStartDate_IsAccepted()
    {
        // Someone who worked a single day still has to be payable.
        var day = new DateTimeOffset(new DateTime(2026, 3, 2));
        PayrollModalsViewModel vm = FormWith(day, day);

        Assert.Empty(vm.EndDateError);
    }

    [Fact]
    public void AnEmployeeWhoHasNotLeft_NeedsNoEndDate()
    {
        PayrollModalsViewModel vm = FormWith(new DateTimeOffset(new DateTime(2026, 1, 5)), null);

        Assert.Empty(vm.EndDateError);
    }

    [Fact]
    public void AnEndDateWithNoStartDate_IsAccepted()
    {
        // Start date is optional, so an end date on its own cannot be contradicted by anything
        // and must not be rejected.
        PayrollModalsViewModel vm = FormWith(null, new DateTimeOffset(new DateTime(2026, 9, 30)));

        Assert.Empty(vm.EndDateError);
    }

    [Fact]
    public void OpeningTheAddForm_ClearsAPreviousEmployeesDates()
    {
        // The form is shell-hosted and reused, so a leaver's end date must not follow the next
        // person who gets added.
        var vm = new PayrollModalsViewModel
        {
            StartDate = new DateTimeOffset(new DateTime(2026, 1, 5)),
            EndDate = new DateTimeOffset(new DateTime(2026, 9, 30)),
        };

        vm.OpenAddEmployeeModal();

        Assert.Null(vm.StartDate);
        Assert.Null(vm.EndDate);
        Assert.Empty(vm.EndDateError);
    }

    [Fact]
    public void EveryProvinceAndTerritory_IsOffered()
    {
        // Thirteen, including Quebec. Quebec is not in the rate table's provinces block, because
        // it administers its own tax, pension plan and parental insurance, so building the list
        // from that block alone left a fully supported jurisdiction unselectable.
        var vm = new PayrollModalsViewModel();

        Assert.Equal(13, vm.SupportedProvinces.Count);
        Assert.Contains("QC", vm.SupportedProvinces);
    }

    [Fact]
    public void WithRatesLoaded_ThereIsNoProvinceNote()
    {
        var vm = new PayrollModalsViewModel();

        Assert.Empty(vm.ProvinceSupportNote);
    }

    [Fact]
    public void OnlyProvincesThatCanBeCalculated_AreOffered()
    {
        // An employee that no pay run could ever include must not be creatable.
        var vm = new PayrollModalsViewModel();

        Assert.NotEmpty(vm.SupportedProvinces);
        Assert.Contains(vm.Province, vm.SupportedProvinces);
    }
}

/// <summary>
/// Tests for the unsaved-changes detection behind the discard prompt.
///
/// The prompt itself cannot be exercised here, because the confirm helper returns true when
/// there is no dialog and tests have none. What is worth pinning is the detection: if it stops
/// noticing a field, the modal silently goes back to throwing away work on a backdrop click,
/// which is the bug this was added to fix and which nothing else would catch.
/// </summary>
public class PayrollModalsDiscardTests
{
    private static Employee Person() => new()
    {
        Id = "EMP-001",
        Name = "Dana Smith",
        Province = "AB",
        PayType = PayType.Salary,
        PayRate = 52000m,
        PayFrequency = PayFrequency.Biweekly,
    };

    [Fact]
    public void AFreshAddForm_HasNothingToDiscard()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();

        Assert.False(vm.HasEmployeeModalChanges);
    }

    [Fact]
    public void TypingIntoTheAddForm_CountsAsChanges()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();

        vm.Name = "Dana Smith";

        Assert.True(vm.HasEmployeeModalChanges);
    }

    [Fact]
    public void AnUntouchedEditForm_HasNothingToDiscard()
    {
        // The snapshot is taken after the employee is loaded, so merely opening an existing
        // record must not look like an edit.
        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(Person());

        Assert.False(vm.HasEmployeeModalChanges);
    }

    [Fact]
    public void EditingAnyFieldCounts_IncludingTheOnesAddedLast()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(Person());

        // A per-field original list is exactly what would have missed it.
        vm.StandardHoursPerWeek = "37.5";

        Assert.True(vm.HasEmployeeModalChanges);
    }

    [Fact]
    public void TypingAndThenUndoingIt_IsNotAChange()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(Person());

        vm.Name = "Someone Else";
        Assert.True(vm.HasEmployeeModalChanges);

        vm.Name = "Dana Smith";

        // Nothing would be lost by closing now, so nothing should be asked.
        Assert.False(vm.HasEmployeeModalChanges);
    }

    [Fact]
    public void ReopeningTheAddFormAfterAnEdit_StartsClean()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(Person());
        vm.Name = "Someone Else";

        vm.OpenAddEmployeeModal();

        Assert.False(vm.HasEmployeeModalChanges);
    }

    [Fact]
    public void AbandoningTheFilterModal_PutsTheFiltersBack()
    {
        // Filters are live properties, so leaving a cancelled choice in place would keep the
        // page filtered by something the user backed out of.
        var vm = new PayrollModalsViewModel();
        vm.OpenFilterModal();
        vm.FilterStatus = "Archived";
        vm.FilterPayType = "Hourly";

        vm.RequestCloseFilterModalCommand.Execute(null);

        Assert.Equal("All", vm.FilterStatus);
        Assert.Equal("All", vm.FilterPayType);
        Assert.False(vm.IsFilterModalOpen);
    }
}
