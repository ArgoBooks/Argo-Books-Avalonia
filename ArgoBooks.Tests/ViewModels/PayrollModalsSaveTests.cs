using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Saving, editing and undoing an employee.
///
/// The rest of the employee form's tests stop at validation, because everything past it needs an
/// open company. That left the half that actually writes to the file untested: what gets stored,
/// what undo puts back, and whether an edit strands the pay run lines that hold the employee id.
///
/// The undo cases are the ones worth having. An employee is edited in place rather than replaced,
/// so undo has to restore fields onto the SAME object; anything that swapped the instance out
/// would pass a naive "the name is back" check and still break the archive command and the list
/// that both hold the original reference.
/// </summary>
public class PayrollModalsSaveTests : ModalViewModelTestBase
{
    private static PayrollModalsViewModel Filled(PayrollModalsViewModel vm)
    {
        vm.Name = "  Dana Smith  ";
        vm.EmployeeNumber = " 42 ";
        vm.Province = "AB";
        vm.IsSalaried = true;
        vm.PayRate = "52000";
        vm.PayFrequency = PayFrequency.Biweekly;
        vm.StandardHoursPerWeek = "37.5";
        vm.FederalClaimAmount = "16500";
        vm.ProvincialClaimAmount = "22769";
        vm.IsCppExempt = true;
        vm.IsEiExempt = true;
        vm.StartDate = new DateTimeOffset(new DateTime(2026, 1, 5));
        vm.EndDate = new DateTimeOffset(new DateTime(2026, 12, 31));
        vm.Sin = "046 454 286";
        vm.AddressStreet = " 42 Employee Road ";
        vm.AddressCity = " Calgary ";
        vm.AddressProvince = " AB ";
        vm.AddressPostalCode = " T2P1A1 ";
        vm.DentalBenefit = DentalBenefitCode.PayeeAndSpouse;
        vm.Notes = " starts on the 5th ";
        return vm;
    }

    #region Adding

    [Fact]
    public void AddingAnEmployee_StoresEveryFieldOnTheForm()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        Filled(vm);

        vm.SaveEmployeeCommand.Execute(null);

        Employee saved = Assert.Single(Company.Employees);
        Assert.Equal("Dana Smith", saved.Name);
        Assert.Equal("42", saved.EmployeeNumber);
        Assert.Equal(PayType.Salary, saved.PayType);
        Assert.Equal(52000m, saved.PayRate);
        Assert.Equal(37.5m, saved.StandardHoursPerWeek);
        Assert.Equal(16500m, saved.FederalClaimAmount);
        Assert.Equal(22769m, saved.ProvincialClaimAmount);
        Assert.True(saved.IsCppExempt);
        Assert.True(saved.IsEiExempt);
        Assert.Equal(new DateTime(2026, 1, 5), saved.StartDate);
        Assert.Equal(new DateTime(2026, 12, 31), saved.EndDate);
        Assert.Equal(DentalBenefitCode.PayeeAndSpouse, saved.DentalBenefit);
        Assert.Equal("starts on the 5th", saved.Notes);
        Assert.False(vm.IsEmployeeModalOpen);
    }

    [Fact]
    public void ASocialInsuranceNumber_IsStrippedToDigits()
    {
        // People write it with spaces or dashes. Storing it as typed would fail the T4 check
        // that counts nine digits, months later and on a deadline.
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        Filled(vm).Sin = "046-454-286";

        vm.SaveEmployeeCommand.Execute(null);

        Assert.Equal("046454286", Company.Employees[0].Sin);
    }

    [Fact]
    public void AnAddressOnTheForm_IsTrimmedOntoTheEmployee()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        Filled(vm);

        vm.SaveEmployeeCommand.Execute(null);

        ArgoBooks.Core.Models.Common.Address address = Company.Employees[0].Address;
        Assert.Equal("42 Employee Road", address.Street);
        Assert.Equal("Calgary", address.City);
        Assert.Equal("AB", address.State);
        Assert.Equal("T2P1A1", address.ZipCode);
    }

    [Fact]
    public void AnHourlyEmployee_GetsNoContractHours()
    {
        // Their real hours go on every pay run. Storing a weekly figure as well would put a
        // number on the record of employment that nobody entered.
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        Filled(vm);
        vm.IsSalaried = false;
        vm.PayRate = "25";

        vm.SaveEmployeeCommand.Execute(null);

        Assert.Equal(PayType.Hourly, Company.Employees[0].PayType);
        Assert.Null(Company.Employees[0].StandardHoursPerWeek);
    }

    [Fact]
    public void BlankContractHours_AreNullRatherThanZero()
    {
        // Zero reads as "worked no hours" on an ROE, which costs the employee their claim.
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        Filled(vm).StandardHoursPerWeek = string.Empty;

        vm.SaveEmployeeCommand.Execute(null);

        Assert.Null(Company.Employees[0].StandardHoursPerWeek);
    }

    [Fact]
    public void EmployeeIds_CarryOnFromTheHighestAlreadyUsed()
    {
        Company.Employees.Add(new Employee { Id = "EMP-007" });
        Company.Employees.Add(new Employee { Id = "not-an-id" });

        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        Filled(vm);
        vm.SaveEmployeeCommand.Execute(null);

        Assert.Equal("EMP-008", Company.Employees[^1].Id);
    }

    [Fact]
    public void AddingAnEmployee_CanBeUndoneAndRedone()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        Filled(vm);
        vm.SaveEmployeeCommand.Execute(null);

        Undo();
        Assert.Empty(Company.Employees);

        Redo();
        Assert.Equal("Dana Smith", Assert.Single(Company.Employees).Name);
    }

    [Fact]
    public void AFormThatFailsValidation_SavesNothing()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        vm.Name = string.Empty;
        vm.PayRate = "0";

        vm.SaveEmployeeCommand.Execute(null);

        Assert.Empty(Company.Employees);
        Assert.NotEmpty(vm.NameError);
        Assert.NotEmpty(vm.PayRateError);
    }

    [Fact]
    public void ASocialInsuranceNumberOfTheWrongLength_StopsTheSave()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        Filled(vm).Sin = "12345";

        vm.SaveEmployeeCommand.Execute(null);

        Assert.NotEmpty(vm.SinError);
        Assert.Empty(Company.Employees);
    }

    #endregion

    #region Editing

    private Employee Existing()
    {
        var employee = new Employee
        {
            Id = "EMP-001",
            Name = "Dana Smith",
            EmployeeNumber = "42",
            Province = "AB",
            PayType = PayType.Salary,
            PayRate = 52000m,
            PayFrequency = PayFrequency.Biweekly,
            StandardHoursPerWeek = 40m,
            FederalClaimAmount = 16500m,
            Sin = "046454286",
            DentalBenefit = DentalBenefitCode.PayeeOnly,
            Notes = "original",
        };

        Company.Employees.Add(employee);
        return employee;
    }

    [Fact]
    public void OpeningAnEmployee_FillsTheFormFromTheRecord()
    {
        Employee employee = Existing();
        employee.StartDate = new DateTime(2024, 1, 8);
        employee.EndDate = new DateTime(2026, 7, 10);
        employee.Address.Street = "42 Employee Road";

        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(employee);

        Assert.Equal("Edit employee", vm.ModalTitle);
        Assert.Equal("Dana Smith", vm.Name);
        Assert.Equal("40", vm.StandardHoursPerWeek);
        Assert.Equal("42 Employee Road", vm.AddressStreet);
        Assert.Equal(new DateTimeOffset(new DateTime(2024, 1, 8)), vm.StartDate);
        Assert.Equal(new DateTimeOffset(new DateTime(2026, 7, 10)), vm.EndDate);
        Assert.True(vm.IsEmployeeModalOpen);
    }

    [Fact]
    public void AnEmployeeOnZeroPay_ShowsAnEmptyRateBoxRatherThanZero()
    {
        Employee employee = Existing();
        employee.PayRate = 0m;
        employee.FederalClaimAmount = 0m;

        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(employee);

        Assert.Empty(vm.PayRate);
        Assert.Empty(vm.FederalClaimAmount);
    }

    [Fact]
    public void EditingAnEmployee_ChangesTheSameObjectRatherThanReplacingIt()
    {
        // Pay run lines hold the id, but the employees list and the archive command both hold
        // this reference. Swapping the instance would strand them.
        Employee employee = Existing();

        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(employee);
        vm.Name = "Dana Smythe";
        vm.SaveEmployeeCommand.Execute(null);

        Assert.Same(employee, Assert.Single(Company.Employees));
        Assert.Equal("Dana Smythe", employee.Name);
    }

    [Fact]
    public void EditingAnEmployee_CanBeUndoneAndRedone()
    {
        Employee employee = Existing();

        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(employee);
        vm.Name = "Dana Smythe";
        vm.DentalBenefit = DentalBenefitCode.PayeeSpouseAndChildren;
        vm.Notes = "changed";
        vm.SaveEmployeeCommand.Execute(null);

        Undo();
        Assert.Equal("Dana Smith", employee.Name);
        Assert.Equal(DentalBenefitCode.PayeeOnly, employee.DentalBenefit);
        Assert.Equal("original", employee.Notes);

        Redo();
        Assert.Equal("Dana Smythe", employee.Name);
        Assert.Equal(DentalBenefitCode.PayeeSpouseAndChildren, employee.DentalBenefit);
        Assert.Equal("changed", employee.Notes);
    }

    [Fact]
    public void UndoingAnEdit_PutsTheAddressBackToo()
    {
        // The address is a nested object, so restoring it needs a copy rather than a reference
        // to the same one being edited.
        Employee employee = Existing();
        employee.Address.City = "Calgary";

        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(employee);
        vm.AddressCity = "Edmonton";
        vm.SaveEmployeeCommand.Execute(null);

        Assert.Equal("Edmonton", employee.Address.City);

        Undo();

        Assert.Equal("Calgary", employee.Address.City);
    }

    [Fact]
    public void ATypedZeroClaim_IsSavedAsAClaimOfNothing_AndABlankOneIsNot()
    {
        // A blank box has always meant "no TD1, use the basic personal amount". A zero typed in
        // is the employee claiming nothing, and has to reach the pay run as that.
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        Filled(vm);
        vm.FederalClaimAmount = "0";
        vm.ProvincialClaimAmount = string.Empty;

        vm.SaveEmployeeCommand.Execute(null);

        Employee saved = Assert.Single(Company.Employees);
        Assert.True(saved.FederalClaimIsZero);
        Assert.Equal(0m, saved.FederalClaimAmount);
        Assert.False(saved.ProvincialClaimIsZero);
    }

    [Fact]
    public void AZeroClaim_ShowsAsZeroWhenReopened()
    {
        Employee employee = Existing();
        employee.FederalClaimAmount = 0m;
        employee.FederalClaimIsZero = true;

        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(employee);

        Assert.Equal(0m.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture), vm.FederalClaimAmount);
        Assert.Empty(vm.ProvincialClaimAmount);
        Assert.False(vm.HasEmployeeModalChanges);

        vm.FederalClaimAmount = string.Empty;
        Assert.True(vm.HasEmployeeModalChanges);
    }

    [Fact]
    public void UndoingAZeroClaim_PutsTheBasicPersonalAmountBack()
    {
        Employee employee = Existing();
        employee.FederalClaimAmount = 0m;

        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(employee);
        vm.FederalClaimAmount = "0";
        vm.SaveEmployeeCommand.Execute(null);

        Assert.True(employee.FederalClaimIsZero);

        Undo();
        Assert.False(employee.FederalClaimIsZero);
    }

    #endregion

    #region Closing

    [Fact]
    public void ClosingAnUntouchedForm_JustCloses()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();

        vm.RequestCloseEmployeeModalCommand.Execute(null);

        Assert.False(vm.IsEmployeeModalOpen);
    }

    [Fact]
    public void ClosingAHalfFilledAddForm_GoesThroughTheDiscardPath()
    {
        // The prompt itself answers yes with no dialog present, so what this pins is that a
        // touched form takes the asking branch at all rather than closing silently.
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        vm.Name = "Half typed";

        Assert.True(vm.HasEmployeeModalChanges);
        vm.RequestCloseEmployeeModalCommand.Execute(null);

        Assert.False(vm.IsEmployeeModalOpen);
    }

    [Fact]
    public void ClosingATouchedEditForm_GoesThroughTheDiscardPath()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(Existing());
        vm.Name = "Someone Else";

        vm.RequestCloseEmployeeModalCommand.Execute(null);

        Assert.False(vm.IsEmployeeModalOpen);
    }

    [Fact]
    public void TheCloseCommand_ClosesWithoutAsking()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenAddEmployeeModal();
        vm.Name = "Half typed";

        vm.CloseEmployeeModalCommand.Execute(null);

        Assert.False(vm.IsEmployeeModalOpen);
    }

    #endregion

    #region Filters

    [Fact]
    public void TheProvinceFilter_OffersOnlyProvincesSomebodyWorksIn()
    {
        // Listing every province the rate table covers would be thirteen entries for a company
        // with two employees in one of them.
        Company.Employees.Add(new Employee { Id = "EMP-001", Province = "ON" });
        Company.Employees.Add(new Employee { Id = "EMP-002", Province = "AB" });
        Company.Employees.Add(new Employee { Id = "EMP-003", Province = "AB" });
        Company.Employees.Add(new Employee { Id = "EMP-004", Province = string.Empty });

        var vm = new PayrollModalsViewModel();
        vm.OpenFilterModal();

        Assert.Equal(["All", "AB", "ON"], vm.ProvinceFilterOptions);
        Assert.True(vm.IsFilterModalOpen);
    }

    [Fact]
    public void AFilterOnAProvinceNobodyWorksInAnyMore_FallsBackToAll()
    {
        Company.Employees.Add(new Employee { Id = "EMP-001", Province = "AB" });

        var vm = new PayrollModalsViewModel { FilterProvince = "ON" };
        vm.OpenFilterModal();

        Assert.Equal("All", vm.FilterProvince);
    }

    [Fact]
    public void ApplyingFilters_TellsThePageAndCloses()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenFilterModal();
        int applied = 0;
        vm.FiltersApplied += (_, _) => applied++;

        vm.ApplyFiltersCommand.Execute(null);

        Assert.Equal(1, applied);
        Assert.False(vm.IsFilterModalOpen);
    }

    [Fact]
    public void ClearingFilters_ResetsAllFourAndTellsThePage()
    {
        var vm = new PayrollModalsViewModel();
        vm.OpenFilterModal();
        vm.FilterStatus = "Archived";
        vm.FilterProvince = "All";
        vm.FilterPayType = "Hourly";
        vm.FilterFrequency = "Monthly";

        int cleared = 0;
        vm.FiltersCleared += (_, _) => cleared++;

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal("All", vm.FilterStatus);
        Assert.Equal("All", vm.FilterPayType);
        Assert.Equal("All", vm.FilterFrequency);
        Assert.Equal(1, cleared);
        Assert.False(vm.IsFilterModalOpen);
    }

    [Fact]
    public void ClosingAnUntouchedFilterModal_LeavesTheFiltersAlone()
    {
        var vm = new PayrollModalsViewModel { FilterStatus = "Archived" };
        vm.OpenFilterModal();

        vm.RequestCloseFilterModalCommand.Execute(null);

        Assert.Equal("Archived", vm.FilterStatus);
        Assert.False(vm.IsFilterModalOpen);
    }

    #endregion

    #region Supported provinces

    [Fact]
    public void AProvinceNoEditionCoversAnyMore_IsSwappedForOneThatWorks()
    {
        // Rate files are delivered rather than shipped, so the set can change under an employee
        // who was created against an older one. Leaving the form on a province no pay run could
        // calculate would fail at approval instead of here.
        var vm = new PayrollModalsViewModel { Province = "ZZ" };

        vm.OpenAddEmployeeModal();

        Assert.NotEqual("ZZ", vm.Province);
        Assert.Contains(vm.Province, vm.SupportedProvinces);
    }

    [Fact]
    public void WithNoRateEditionForToday_TheFormSaysSoRatherThanOfferingNothing()
    {
        // What the day after the last edition expires looks like. The form still has to work,
        // because the employer can enter people before the new rates arrive; it is the pay run
        // that refuses, and this note is what tells them why in advance.
        var vm = new PayrollModalsViewModel(new NoEditionsRateService()) { Province = "AB" };
        vm.OpenAddEmployeeModal();

        Assert.Equal(["AB"], vm.SupportedProvinces);
        Assert.Contains("cannot be calculated until the rates are updated", vm.ProvinceSupportNote,
            StringComparison.Ordinal);
    }

    private sealed class NoEditionsRateService : ArgoBooks.Core.Services.PayrollRateService
    {
        public override PayrollRateTable? GetForDate(DateTime payDate) => null;
    }

    #endregion

    #region Dropdown contents

    [Fact]
    public void TheFormOffersEveryChoiceTheModelsDefine()
    {
        // Box 45 has five codes and CRA rejects a slip carrying anything else, so a dropdown
        // that is short by one is a filing that cannot be made from the app at all.
        var vm = new PayrollModalsViewModel();

        Assert.Equal(5, vm.DentalOptions.Count);
        Assert.Equal(4, vm.Frequencies.Count);
        Assert.Equal(3, vm.StatusOptions.Count);
        Assert.Equal(3, vm.PayTypeOptions.Count);
        Assert.Equal(5, vm.FrequencyOptions.Count);
    }

    #endregion

    #region The rate hint

    [Fact]
    public void ThePayRateHint_SaysWhichOfTheTwoThingsTheBoxMeans()
    {
        var vm = new PayrollModalsViewModel { IsSalaried = true };
        Assert.Contains("Annual salary", vm.PayRateHint, StringComparison.Ordinal);

        vm.IsSalaried = false;
        Assert.Contains("per hour", vm.PayRateHint, StringComparison.Ordinal);
    }

    #endregion

    #region Standard hours

    /// <summary>
    /// Standard hours per week are part of the undo snapshot. Block 15A of a record of employment is calculated from this
    /// for a salaried employee, and an EI claim is calculated from block 15A.
    /// </summary>
    [Fact]
    public void UndoingAnEmployeeEdit_PutsTheStandardHoursBack()
    {
        Employee employee = Existing();

        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(employee);
        vm.StandardHoursPerWeek = "20";
        vm.SaveEmployeeCommand.Execute(null);

        Assert.Equal(20m, employee.StandardHoursPerWeek);

        Undo();
        Assert.Equal(40m, employee.StandardHoursPerWeek);

        Redo();
        Assert.Equal(20m, employee.StandardHoursPerWeek);
    }

    /// <summary>
    /// Cleared back to unknown, which is not the same as zero: the worksheet prints "unknown"
    /// rather than a number it would be guessing at. Undo has to restore the null too.
    /// </summary>
    [Fact]
    public void UndoingAClearedStandardHours_PutsTheNumberBack()
    {
        Employee employee = Existing();

        var vm = new PayrollModalsViewModel();
        vm.OpenEditEmployeeModal(employee);
        vm.StandardHoursPerWeek = string.Empty;
        vm.SaveEmployeeCommand.Execute(null);

        Assert.Null(employee.StandardHoursPerWeek);

        Undo();
        Assert.Equal(40m, employee.StandardHoursPerWeek);
    }

    #endregion
}
