using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class SupplierModalsViewModelTests : ModalViewModelTestBase
{
    private static async Task AddAsync(SupplierModalsViewModel vm, string name, string id = "")
    {
        vm.ModalId = id;
        vm.ModalSupplierName = name;
        await vm.SaveNewSupplierAsync();
    }

    [Fact]
    public async Task SaveNew_BlankId_SkipsAnIdTypedEarlier()
    {
        Company.IdCounters.Supplier = 3;
        var vm = new SupplierModalsViewModel();

        await AddAsync(vm, "Acme", "SUP-005");
        await AddAsync(vm, "Beta");
        await AddAsync(vm, "Gamma");

        Assert.Equal(["SUP-005", "SUP-004", "SUP-006"], Company.Suppliers.Select(s => s.Id));
    }
}
