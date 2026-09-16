using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A name typed into a picker becomes a record. Reusing an existing record whatever its capitalisation
/// is what keeps a second "Flour" out of the books, and each create has to undo on its own.
/// </summary>
public class QuickCreateTests : ModalViewModelTestBase
{
    [Fact]
    public void EnsureCategory_NewName_CreatesItAndUndoRemovesIt()
    {
        var category = QuickCreate.EnsureCategory(Company, CategoryType.Expense, " Ingredients ");

        Assert.NotNull(category);
        Assert.Equal("Ingredients", category.Name);
        Assert.Equal(CategoryType.Expense, category.Type);
        Assert.Same(category, Assert.Single(Company.Categories));

        Undo();
        Assert.Empty(Company.Categories);

        Redo();
        Assert.Same(category, Assert.Single(Company.Categories));
    }

    [Fact]
    public void EnsureCategory_ExistingNameInAnotherCase_ReusesItAndRecordsNothing()
    {
        var existing = new Category { Id = "CAT-PUR-001", Name = "Ingredients", Type = CategoryType.Expense };
        Company.Categories.Add(existing);

        var category = QuickCreate.EnsureCategory(Company, CategoryType.Expense, "INGREDIENTS");

        Assert.Same(existing, category);
        Assert.Single(Company.Categories);
        Assert.False(App.UndoRedoManager.CanUndo);
    }

    // A category of the other type with the same name is a different category.
    [Fact]
    public void EnsureCategory_SameNameOtherType_CreatesASeparateOne()
    {
        Company.Categories.Add(new Category { Id = "CAT-PUR-001", Name = "Shipping", Type = CategoryType.Expense });

        var category = QuickCreate.EnsureCategory(Company, CategoryType.Revenue, "Shipping");

        Assert.Equal(CategoryType.Revenue, category?.Type);
        Assert.Equal(2, Company.Categories.Count);
    }

    [Fact]
    public void EnsureCustomer_NewName_CreatesItAndUndoRemovesIt()
    {
        var customer = QuickCreate.EnsureCustomer(Company, "Bob's Bakery");

        Assert.Equal("Bob's Bakery", customer?.Name);
        Assert.Single(Company.Customers);

        Undo();
        Assert.Empty(Company.Customers);
    }

    [Fact]
    public void EnsureSupplier_ExistingNameInAnotherCase_ReusesIt()
    {
        var existing = new Supplier { Id = "SUP-001", Name = "Mill Co" };
        Company.Suppliers.Add(existing);

        Assert.Same(existing, QuickCreate.EnsureSupplier(Company, "mill co"));
        Assert.Single(Company.Suppliers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ensure_BlankText_CreatesNothing(string? typed)
    {
        Assert.Null(QuickCreate.EnsureCategory(Company, CategoryType.Expense, typed));
        Assert.Null(QuickCreate.EnsureCustomer(Company, typed));
        Assert.Null(QuickCreate.EnsureSupplier(Company, typed));

        Assert.Empty(Company.Categories);
        Assert.Empty(Company.Customers);
        Assert.Empty(Company.Suppliers);
    }
}
