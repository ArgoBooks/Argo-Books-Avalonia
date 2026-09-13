using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Entities;
using Xunit;

namespace ArgoBooks.Tests.Models;

/// <summary>
/// Tests for the CompanyData container.
/// </summary>
public class CompanyDataTests
{
    #region GetCustomer Tests

    [Fact]
    public void GetCustomer_NonExistentId_ReturnsNull()
    {
        var data = new CompanyData();

        var result = data.GetCustomer("CUS-999");

        Assert.Null(result);
    }

    [Fact]
    public void GetCustomer_ExistingId_ReturnsCustomer()
    {
        var data = new CompanyData();
        var customer = new Customer { Id = "CUS-001", Name = "Test Customer" };
        data.Customers.Add(customer);

        var result = data.GetCustomer("CUS-001");

        Assert.NotNull(result);
        Assert.Equal("CUS-001", result.Id);
        Assert.Equal("Test Customer", result.Name);
    }

    #endregion

    #region GetProduct Tests

    [Fact]
    public void GetProduct_NonExistentId_ReturnsNull()
    {
        var data = new CompanyData();

        var result = data.GetProduct("PRD-999");

        Assert.Null(result);
    }

    [Fact]
    public void GetProduct_ExistingId_ReturnsProduct()
    {
        var data = new CompanyData();
        var product = new Product { Id = "PRD-001", Name = "Test Product" };
        data.Products.Add(product);

        var result = data.GetProduct("PRD-001");

        Assert.NotNull(result);
        Assert.Equal("PRD-001", result.Id);
    }

    #endregion

    #region GetSupplier Tests

    [Fact]
    public void GetSupplier_NonExistentId_ReturnsNull()
    {
        var data = new CompanyData();

        var result = data.GetSupplier("SUP-999");

        Assert.Null(result);
    }

    [Fact]
    public void GetSupplier_ExistingId_ReturnsSupplier()
    {
        var data = new CompanyData();
        var supplier = new Supplier { Id = "SUP-001", Name = "Test Supplier" };
        data.Suppliers.Add(supplier);

        var result = data.GetSupplier("SUP-001");

        Assert.NotNull(result);
        Assert.Equal("SUP-001", result.Id);
    }

    #endregion

    #region Lookup Cache Tests

    // Deleting one record and adding another leaves the count unchanged, which the lookup once
    // took to mean nothing had changed.
    [Fact]
    public void GetCategory_AfterDeleteThenAdd_FindsTheNewCategory()
    {
        var data = new CompanyData();
        var deleted = new Category { Id = "CAT-001", Name = "Old" };
        data.Categories.Add(deleted);
        data.Categories.Add(new Category { Id = "CAT-002", Name = "Kept" });
        Assert.Same(deleted, data.GetCategory("CAT-001"));

        data.Categories.Remove(deleted);
        var added = new Category { Id = "CAT-003", Name = "New" };
        data.Categories.Add(added);

        Assert.Same(added, data.GetCategory("CAT-003"));
        Assert.Null(data.GetCategory("CAT-001"));
    }

    [Fact]
    public void GetCustomer_AfterDeletingTheLastThenAdding_FindsTheNewCustomer()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "CUS-001", Name = "Kept" });
        var deleted = new Customer { Id = "CUS-002", Name = "Old" };
        data.Customers.Add(deleted);
        Assert.Same(deleted, data.GetCustomer("CUS-002"));

        data.Customers.Remove(deleted);
        var added = new Customer { Id = "CUS-003", Name = "New" };
        data.Customers.Add(added);

        Assert.Same(added, data.GetCustomer("CUS-003"));
        Assert.Null(data.GetCustomer("CUS-002"));
    }

    #endregion

    #region MarkAsModified / MarkAsSaved Tests

    [Fact]
    public void MarkAsModified_SetsChangesMadeToTrue()
    {
        var data = new CompanyData();

        data.MarkAsModified();

        Assert.True(data.ChangesMade);
    }

    [Fact]
    public void MarkAsSaved_SetsChangesMadeToFalse()
    {
        var data = new CompanyData();
        data.MarkAsModified();

        data.MarkAsSaved();

        Assert.False(data.ChangesMade);
    }

    /// <summary>
    /// An edit that lands while a save is writing may not be in the file, so the save must not
    /// clear the flag for it; otherwise the auto-save before locking skips it.
    /// </summary>
    [Fact]
    public void MarkAsSaved_ChangeDuringSave_KeepsChangesMade()
    {
        var data = new CompanyData();
        data.MarkAsModified();
        var changeCount = data.ChangeCount;

        data.Settings.ChangesMade = true;
        data.MarkAsSaved(changeCount);

        Assert.True(data.ChangesMade);
    }

    [Fact]
    public void MarkAsSaved_NoChangeDuringSave_ClearsChangesMade()
    {
        var data = new CompanyData();
        data.MarkAsModified();
        var changeCount = data.ChangeCount;

        data.MarkAsSaved(changeCount);

        Assert.False(data.ChangesMade);
    }

    #endregion
}
