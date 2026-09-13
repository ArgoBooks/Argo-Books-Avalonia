using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class LocationsModalsViewModelTests : ModalViewModelTestBase
{
    private static void Add(LocationsModalsViewModel vm, string name, string code = "")
    {
        vm.OpenAddModal();
        vm.ModalName = name;
        vm.ModalCode = code;
        vm.SaveNewLocationCommand.Execute(null);
    }

    // A location added without a code must get one that's free, not fail with "already exists"
    // because the next number was typed by hand earlier.
    [Fact]
    public void SaveNew_BlankCode_SkipsACodeTypedEarlier()
    {
        var vm = new LocationsModalsViewModel();

        Add(vm, "Main", "LOC-002");
        Add(vm, "Back room");
        Add(vm, "Garage");

        Assert.Equal(["LOC-002", "LOC-001", "LOC-003"], Company.Locations.Select(l => l.Id));
    }
}
