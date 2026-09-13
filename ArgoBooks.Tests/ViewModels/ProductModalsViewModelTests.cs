using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class ProductModalsViewModelTests : ModalViewModelTestBase
{
    private static void Add(ProductModalsViewModel vm, string name, string id = "")
    {
        vm.ModalId = id;
        vm.ModalProductName = name;
        vm.ModalCategoryId = "CAT-PUR-001";
        vm.SaveNewProduct();
    }

    [Fact]
    public void SaveNew_BlankId_SkipsAnIdTypedEarlier()
    {
        Company.IdCounters.Product = 3;
        var vm = new ProductModalsViewModel();

        Add(vm, "Widget", "PRD-005");
        Add(vm, "Gadget");
        Add(vm, "Gizmo");

        Assert.Equal(["PRD-005", "PRD-004", "PRD-006"], Company.Products.Select(p => p.Id));
    }
}
