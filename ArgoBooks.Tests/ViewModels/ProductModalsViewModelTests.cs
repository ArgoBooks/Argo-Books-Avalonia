using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
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

    // Typing a category that doesn't exist yet used to fail validation, so the product could not be saved.
    [Fact]
    public void SaveNew_TypedCategoryName_CreatesTheCategoryAndLinksIt()
    {
        var vm = new ProductModalsViewModel();
        vm.ModalProductName = "Flour";
        vm.ModalCategoryText = "Ingredients";

        vm.SaveNewProduct();

        var category = Assert.Single(Company.Categories);
        Assert.Equal("Ingredients", category.Name);
        Assert.Equal(category.Id, Assert.Single(Company.Products).CategoryId);

        Undo();
        Assert.Empty(Company.Products);
        Undo();
        Assert.Empty(Company.Categories);
    }

    [Fact]
    public void SaveNew_TypedCategoryNameThatExists_ReusesItWhateverTheCapitalisation()
    {
        Company.Categories.Add(new Category { Id = "CAT-PUR-001", Name = "Ingredients", Type = CategoryType.Expense });
        var vm = new ProductModalsViewModel();
        vm.ModalProductName = "Flour";
        vm.ModalCategoryText = "ingredients";

        vm.SaveNewProduct();

        Assert.Single(Company.Categories);
        Assert.Equal("CAT-PUR-001", Assert.Single(Company.Products).CategoryId);
    }

    [Fact]
    public void SaveNew_TypedSupplierName_CreatesTheSupplierAndLinksIt()
    {
        var vm = new ProductModalsViewModel();
        vm.ModalProductName = "Flour";
        vm.ModalCategoryText = "Ingredients";
        vm.ModalSupplierText = "Mill Co";

        vm.SaveNewProduct();

        var supplier = Assert.Single(Company.Suppliers);
        Assert.Equal("Mill Co", supplier.Name);
        Assert.Equal(supplier.Id, Assert.Single(Company.Products).SupplierId);
    }

    [Fact]
    public void FormSaveText_FollowsTheItemType()
    {
        var vm = new ProductModalsViewModel { ModalItemType = "Product" };
        Assert.Equal("Add Product", vm.FormSaveText);

        vm.ModalItemType = "Service";
        Assert.Equal("Add Service", vm.FormSaveText);
    }
}
