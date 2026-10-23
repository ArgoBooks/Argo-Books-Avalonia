using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// How the bank, Stripe and Argo Books API imports find the customer, supplier, product or category
/// a record belongs to, and add one only when there is none.
/// </summary>
public class ImportLookupTests
{
    private static CompanyData Company()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "CUS-1", Name = "Jane Smith", Email = "jane@example.com" });
        data.Customers.Add(new Customer { Id = "CUS-2", Name = "Jane Smith", Email = "other.jane@example.com" });
        data.Suppliers.Add(new Supplier { Id = "SUP-1", Name = "Paper Mill", Email = "orders@papermill.com" });
        data.Products.Add(new Product { Id = "PRD-1", Name = "Widget", Type = CategoryType.Expense });
        data.Products.Add(new Product { Id = "PRD-2", Name = "Widget", Type = CategoryType.Revenue });
        data.Categories.Add(new Category { Id = "CAT-1", Name = "Office", Type = CategoryType.Expense });
        return data;
    }

    [Fact]
    public void FindCustomer_ByEmail_TellsApartCustomersWithTheSameName()
    {
        Assert.Equal("CUS-2", ImportLookup.FindCustomer(Company(), "OTHER.JANE@example.com", "Jane Smith")?.Id);
    }

    [Fact]
    public void FindCustomer_WithAnEmailNobodyHas_FindsNobody_EvenWhenTheNameMatches()
    {
        Assert.Null(ImportLookup.FindCustomer(Company(), "new@example.com", "Jane Smith"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindCustomer_WithoutAnEmail_MatchesTheNameInAnyCase(string? email)
    {
        Assert.Equal("CUS-1", ImportLookup.FindCustomer(Company(), email, "JANE SMITH")?.Id);
    }

    [Fact]
    public void FindSupplier_ByEmailOrElseName()
    {
        var data = Company();

        Assert.Equal("SUP-1", ImportLookup.FindSupplier(data, "Orders@PaperMill.com", null)?.Id);
        Assert.Equal("SUP-1", ImportLookup.FindSupplier(data, null, "paper mill")?.Id);
        Assert.Null(ImportLookup.FindSupplier(data, null, "Paper Mills"));
    }

    [Fact]
    public void FindProduct_WithAType_LooksOnlyAmongProductsOfThatType()
    {
        var data = Company();

        Assert.Equal("PRD-2", ImportLookup.FindProduct(data, "widget", CategoryType.Revenue)?.Id);
        Assert.Equal("PRD-1", ImportLookup.FindProduct(data, "widget", CategoryType.Expense)?.Id);
        Assert.Equal("PRD-1", ImportLookup.FindProduct(data, "widget")?.Id);
        Assert.Null(ImportLookup.FindProduct(data, "Gadget"));
    }

    [Fact]
    public void FindOrCreateCategory_FindsOneOfTheSameTypeAndName_WithoutAdding()
    {
        var data = Company();

        var category = ImportLookup.FindOrCreateCategory(data, CategoryType.Expense, "OFFICE", out var created);

        Assert.Equal("CAT-1", category.Id);
        Assert.False(created);
        Assert.Single(data.Categories);
    }

    [Fact]
    public void FindOrCreateCategory_AddsOne_WhenOnlyAnotherTypeHasTheName()
    {
        var data = Company();

        var category = ImportLookup.FindOrCreateCategory(data, CategoryType.Revenue, "Office", out var created);

        Assert.True(created);
        Assert.Equal((CategoryType.Revenue, "Office"), (category.Type, category.Name));
        Assert.Contains(category, data.Categories);
        Assert.NotEqual("CAT-1", category.Id);
    }

    [Fact]
    public void FindOrCreateCustomer_FindsByNameInAnyCase_OrAddsOneWithANewId()
    {
        var data = Company();

        Assert.Equal("CUS-1", ImportLookup.FindOrCreateCustomer(data, "jane smith", out var found).Id);
        Assert.False(found);

        var added = ImportLookup.FindOrCreateCustomer(data, "New Customer", out var created);
        Assert.True(created);
        Assert.Equal(3, data.Customers.Count);
        Assert.DoesNotContain(data.Customers.Where(c => c != added), c => c.Id == added.Id);
    }

    [Fact]
    public void FindOrCreateSupplier_FindsByNameInAnyCase_OrAddsOneWithANewId()
    {
        var data = Company();

        Assert.Equal("SUP-1", ImportLookup.FindOrCreateSupplier(data, "PAPER MILL", out var found).Id);
        Assert.False(found);

        var added = ImportLookup.FindOrCreateSupplier(data, "Hardware Store", out var created);
        Assert.True(created);
        Assert.Equal(2, data.Suppliers.Count);
        Assert.NotEqual("SUP-1", added.Id);
    }

    [Theory]
    [InlineData("cad", "USD", "CAD")]
    [InlineData(null, "USD", "USD")]
    [InlineData("  ", "EUR", "EUR")]
    public void NormalizeCurrency_UppercasesTheCode_OrUsesTheFallback(string? code, string fallback, string expected)
    {
        Assert.Equal(expected, ImportLookup.NormalizeCurrency(code, fallback));
    }
}
