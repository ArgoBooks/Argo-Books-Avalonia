using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class BankStatementImportUndoTests : ModalViewModelTestBase
{
    // Two lines from one merchant both update the same learned rule, so the second capture's prior
    // state is what the first line left. Undo has to walk the captures newest first, or the rule
    // ends up pointing at the product the undo just removed.
    [Fact]
    public async Task UndoingAnImport_PutsARuleTwoLinesUpdatedBackToItsStateBeforeTheImport()
    {
        Company.Categories.Add(new Category { Id = "CAT-PUR-001", Name = "Office", Type = CategoryType.Expense });
        Company.Products.Add(new Product { Id = "PRD-OLD", Name = "Paper", Type = CategoryType.Expense, CategoryId = "CAT-PUR-001" });
        var supplier = new Supplier { Id = "SUP-001", Name = "Acme" };
        Company.Suppliers.Add(supplier);
        var rule = new BankCategoryRule
        {
            Id = "R1",
            Pattern = "acme",
            CategoryId = "CAT-PUR-001",
            ProductId = "PRD-OLD",
            TransactionType = BookRecordType.Expense,
            CounterpartyId = supplier.Id
        };
        Company.BankCategoryRules.Add(rule);

        var vm = new BankStatementImportModalViewModel();
        vm.Rows.Add(RowFor("ACME", -10m, supplier));
        vm.Rows.Add(RowFor("ACME", -20m, supplier));

        await vm.ImportCommand.ExecuteAsync(null);
        Assert.NotEqual("PRD-OLD", rule.ProductId);

        Undo();

        Assert.Equal("PRD-OLD", rule.ProductId);
        Assert.DoesNotContain(Company.Products, p => p.Name == "Widgets");
    }

    private static ImportLineRow RowFor(string description, decimal amount, Supplier supplier)
    {
        var row = new ImportLineRow(new BankStatementLine
        {
            Id = Guid.NewGuid().ToString("N"),
            Date = new DateTime(2026, 5, 1),
            Description = description,
            Amount = amount
        });
        row.SetNewProduct("Widgets", "CAT-PUR-001", null, "Office");
        row.SetExistingSupplier(supplier);
        return row;
    }
}
