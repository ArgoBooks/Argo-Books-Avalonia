using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class BankStatementImportCounterpartyTests : ModalViewModelTestBase
{
    // The box keeps what was typed and drops the pick, so the import has to go by the text. Going
    // by the dropped pick instead would file the expense under a supplier the row no longer shows.
    [Fact]
    public async Task ASupplierTypedOverAPickedOne_IsCreatedOnImport_AndThePickIsNotUsed()
    {
        Company.Categories.Add(new Category { Id = "CAT-PUR-001", Name = "Office", Type = CategoryType.Expense });
        var acme = new Supplier { Id = "SUP-001", Name = "Acme" };
        Company.Suppliers.Add(acme);

        var row = RowFor(-10m, "CAT-PUR-001");
        row.SetExistingSupplier(acme);
        row.CounterpartySearchText = "Corner Shop";
        row.ResolvedSupplierObject = null;

        await ImportAsync(row);

        var created = Assert.Single(Company.Suppliers, s => s.Name == "Corner Shop");
        Assert.Equal(created.Id, Assert.Single(Company.Expenses).SupplierId);
    }

    // The sign of the amount is the only thing that says which kind of line this is.
    [Fact]
    public async Task AnExpenseRowWhoseAmountIsMadePositive_BecomesASale_AndDoesNotPutItsSupplierOnIt()
    {
        Company.Categories.Add(new Category { Id = "CAT-SAL-001", Name = "Sales", Type = CategoryType.Revenue });
        var acme = new Supplier { Id = "SUP-001", Name = "Acme" };
        Company.Suppliers.Add(acme);

        var row = RowFor(-10m, "CAT-SAL-001");
        row.SetExistingSupplier(acme);
        row.Amount = 10m;

        await ImportAsync(row);

        Assert.Empty(Company.Expenses);
        var customer = Assert.Single(Company.Customers, c => c.Name == "Acme");
        Assert.Equal(customer.Id, Assert.Single(Company.Revenues).CustomerId);
    }

    // A box emptied to retype the amount reads as zero for a moment, which must not turn an expense into a sale.
    [Fact]
    public async Task AnAmountClearedWhileRetyping_KeepsTheLineAnExpense()
    {
        Company.Categories.Add(new Category { Id = "CAT-PUR-001", Name = "Office", Type = CategoryType.Expense });
        var acme = new Supplier { Id = "SUP-001", Name = "Acme" };
        Company.Suppliers.Add(acme);

        var row = RowFor(-10m, "CAT-PUR-001");
        row.SetExistingSupplier(acme);
        row.Amount = 0m;
        row.Amount = -25m;

        await ImportAsync(row);

        Assert.Empty(Company.Revenues);
        var expense = Assert.Single(Company.Expenses);
        Assert.Equal(25m, expense.Total);
        Assert.Equal(acme.Id, expense.SupplierId);
    }

    // There is no separate form for a new product: the name typed in the product box and the category typed beside it are what the import creates.
    [Fact]
    public async Task AProductTypedInTheBox_IsCreatedOnImport_UnderTheCategoryTypedBesideIt()
    {
        var acme = new Supplier { Id = "SUP-001", Name = "Acme" };
        Company.Suppliers.Add(acme);

        var row = new ImportLineRow(new BankStatementLine
        {
            Id = Guid.NewGuid().ToString("N"),
            Date = new DateTime(2026, 5, 1),
            Description = "CARD PAYMENT",
            Amount = -10m
        });
        row.ProductSearchText = "Printer paper";
        row.CategorySearchText = "Stationery";
        row.SetExistingSupplier(acme);

        await ImportAsync(row);

        var category = Assert.Single(Company.Categories, c => c.Name == "Stationery");
        Assert.Equal(CategoryType.Expense, category.Type);
        var product = Assert.Single(Company.Products, p => p.Name == "Printer paper");
        Assert.Equal(category.Id, product.CategoryId);
        Assert.Contains(Assert.Single(Company.Expenses).LineItems, l => l.ProductId == product.Id);
    }

    // A product picked from the list keeps its own category, whatever was typed in the category box before the pick.
    [Fact]
    public async Task PickingAnExistingProduct_DropsTheCategoryTypedForANewOne()
    {
        Company.Categories.Add(new Category { Id = "CAT-PUR-001", Name = "Office", Type = CategoryType.Expense });
        var paper = new Product { Id = "PRD-001", Name = "Paper", Type = CategoryType.Expense, CategoryId = "CAT-PUR-001" };
        Company.Products.Add(paper);
        var acme = new Supplier { Id = "SUP-001", Name = "Acme" };
        Company.Suppliers.Add(acme);

        var row = new ImportLineRow(new BankStatementLine
        {
            Id = Guid.NewGuid().ToString("N"),
            Date = new DateTime(2026, 5, 1),
            Description = "CARD PAYMENT",
            Amount = -10m
        });
        row.ProductSearchText = "Something new";
        row.CategorySearchText = "Made up";
        row.ResolvedProductObject = paper;
        row.SetExistingSupplier(acme);

        await ImportAsync(row);

        Assert.DoesNotContain(Company.Categories, c => c.Name == "Made up");
        Assert.DoesNotContain(Company.Products, p => p.Name == "Something new");
        Assert.Contains(Assert.Single(Company.Expenses).LineItems, l => l.ProductId == paper.Id);
        Assert.Equal("CAT-PUR-001", paper.CategoryId);
    }

    private static async Task ImportAsync(ImportLineRow row)
    {
        var vm = new BankStatementImportModalViewModel();
        vm.Rows.Add(row);
        await vm.ImportCommand.ExecuteAsync(null);
    }

    private static ImportLineRow RowFor(decimal amount, string categoryId)
    {
        var row = new ImportLineRow(new BankStatementLine
        {
            Id = Guid.NewGuid().ToString("N"),
            Date = new DateTime(2026, 5, 1),
            Description = "CARD PAYMENT",
            Amount = amount
        });
        row.SetNewProduct("Widgets", categoryId, null, null);
        return row;
    }
}
