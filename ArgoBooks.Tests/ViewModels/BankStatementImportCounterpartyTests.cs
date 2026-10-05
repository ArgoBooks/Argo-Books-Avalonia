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

    [Fact]
    public async Task AnExpenseRowSwitchedToRevenue_DoesNotPutItsSupplierOnTheSale()
    {
        Company.Categories.Add(new Category { Id = "CAT-SAL-001", Name = "Sales", Type = CategoryType.Revenue });
        var acme = new Supplier { Id = "SUP-001", Name = "Acme" };
        Company.Suppliers.Add(acme);

        var row = RowFor(-10m, "CAT-SAL-001");
        row.SetExistingSupplier(acme);
        row.CreateAsRevenue = true;

        await ImportAsync(row);

        var customer = Assert.Single(Company.Customers, c => c.Name == "Acme");
        Assert.Equal(customer.Id, Assert.Single(Company.Revenues).CustomerId);
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
