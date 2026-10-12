using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A product box drops its pick, by setting it to null, when the name in it is typed over or
/// deleted. A form that kept the id anyway would save a product the box no longer shows.
/// </summary>
public class ClearedProductPickTests : ModalViewModelTestBase
{
    [Fact]
    public void APurchaseOrderLine_WhoseProductIsCleared_NoLongerHoldsIt()
    {
        var line = new OrderLineItemViewModel
        {
            SelectedProduct = new Product { Id = "PRD-001", Name = "Paper", CostPrice = 4m }
        };
        Assert.Equal("PRD-001", line.ProductId);

        line.SelectedProduct = null;

        Assert.Equal(string.Empty, line.ProductId);
    }

    [Fact]
    public async Task ABankStatementRow_WhoseProductIsCleared_IsNotImportedUnderIt()
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
        row.SetExistingProduct(paper, "Office");
        row.SetExistingSupplier(acme);
        row.ProductSearchText = "Pens";
        row.ResolvedProductObject = null;

        var vm = new BankStatementImportModalViewModel();
        vm.Rows.Add(row);
        await vm.ImportCommand.ExecuteAsync(null);

        Assert.False(row.HasProduct);
        Assert.Empty(Company.Expenses);
    }
}
