using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Bulk edits on the Products page: moving many products into a category, or deleting the ones
/// nothing names any more, each as one undo step.
/// </summary>
public class BulkEditProductsTests : ModalViewModelTestBase
{
    private void AddCategoriesAndProducts()
    {
        Company.Categories.Add(new Category { Id = "CAT-1", Name = "Uncategorized", Type = CategoryType.Expense });
        Company.Categories.Add(new Category { Id = "CAT-2", Name = "Office Supplies", Type = CategoryType.Expense });
        Company.Products.Add(new Product { Id = "P1", Name = "Paper", CategoryId = "CAT-1" });
        Company.Products.Add(new Product { Id = "P2", Name = "Toner", CategoryId = "CAT-1" });
        Company.Products.Add(new Product { Id = "P3", Name = "Pens", CategoryId = "CAT-2" });
    }

    private Product Product(string id) => Company.Products.Single(p => p.Id == id);

    [Fact]
    public void SetCategory_MovesTheProducts_AsOneUndoStep()
    {
        AddCategoriesAndProducts();

        var result = new ProductModalsViewModel().SetCategory(["P1", "P2", "P3"], "CAT-2");

        Assert.Equal((2, 1), (result.Changed, result.Unchanged));
        Assert.All(Company.Products, p => Assert.Equal("CAT-2", p.CategoryId));
        Assert.Equal(1, App.UndoRedoManager.UndoCount);

        Undo();
        Assert.Equal(("CAT-1", "CAT-1", "CAT-2"), (Product("P1").CategoryId, Product("P2").CategoryId, Product("P3").CategoryId));

        Redo();
        Assert.Equal("CAT-2", Product("P1").CategoryId);
    }

    [Fact]
    public void SetCategory_WithNone_ClearsIt()
    {
        AddCategoriesAndProducts();

        new ProductModalsViewModel().SetCategory(["P1"], null);

        Assert.Null(Product("P1").CategoryId);
    }

    [Fact]
    public void SetCategory_IgnoresACategoryThatNoLongerExists()
    {
        AddCategoriesAndProducts();

        var result = new ProductModalsViewModel().SetCategory(["P1"], "CAT-GONE");

        Assert.Equal(0, result.Changed);
        Assert.Equal("CAT-1", Product("P1").CategoryId);
    }

    [Fact]
    public void DeleteMany_KeepsProductsStillInUse_AndDeletesTheRestAsOneUndoStep()
    {
        AddCategoriesAndProducts();
        Company.Expenses.Add(new Expense
        {
            Id = "PUR-1",
            Date = new DateTime(2025, 3, 1),
            LineItems = [new LineItem { ProductId = "P1", Description = "Paper", Quantity = 1, UnitPrice = 10m }]
        });

        var result = new ProductModalsViewModel().DeleteMany(["P1", "P2", "P3"]);

        Assert.Equal((2, 1), (result.Changed, result.MadeElsewhere));
        Assert.Equal(["P1"], Company.Products.Select(p => p.Id));

        Undo();
        Assert.Equal(["P1", "P2", "P3"], Company.Products.Select(p => p.Id).Order());
    }
}
