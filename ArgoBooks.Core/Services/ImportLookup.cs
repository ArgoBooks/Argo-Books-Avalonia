using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Finds the customer, supplier, product or category an imported record belongs to, so the bank,
/// Stripe and Argo Books API imports match what the company already has the same way.
/// </summary>
public static class ImportLookup
{
    /// <summary>By email when there is one, otherwise by name.</summary>
    public static Customer? FindCustomer(CompanyData data, string? email, string? name) =>
        !string.IsNullOrWhiteSpace(email)
            ? data.Customers.FirstOrDefault(c => string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase))
            : data.Customers.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>By email when there is one, otherwise by name.</summary>
    public static Supplier? FindSupplier(CompanyData data, string? email, string? name) =>
        !string.IsNullOrWhiteSpace(email)
            ? data.Suppliers.FirstOrDefault(s => string.Equals(s.Email, email, StringComparison.OrdinalIgnoreCase))
            : data.Suppliers.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>By name, among products of <paramref name="type"/> only when a type is given.</summary>
    public static Product? FindProduct(CompanyData data, string? name, CategoryType? type = null) =>
        data.Products.FirstOrDefault(p =>
            (type == null || p.Type == type) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The category of <paramref name="type"/> called <paramref name="name"/>, adding it to the company
    /// when there is none. <paramref name="created"/> says which, so an undo removes only what it added.
    /// </summary>
    public static Category FindOrCreateCategory(CompanyData data, CategoryType type, string name, out bool created)
    {
        var existing = data.Categories.FirstOrDefault(c =>
            c.Type == type && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        created = existing == null;
        if (existing != null) return existing;

        var category = new Category
        {
            Id = new IdGenerator(data).NextCategoryId(type),
            Name = name,
            Type = type
        };
        data.Categories.Add(category);
        return category;
    }

    /// <summary>
    /// The customer called <paramref name="name"/>, adding one to the company when there is none.
    /// <paramref name="created"/> says which, so an undo removes only what it added.
    /// </summary>
    public static Customer FindOrCreateCustomer(CompanyData data, string name, out bool created)
    {
        var existing = data.Customers.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        created = existing == null;
        if (existing != null) return existing;

        var customer = new Customer { Id = new IdGenerator(data).NextCustomerId(), Name = name };
        data.Customers.Add(customer);
        return customer;
    }

    /// <summary>
    /// The supplier called <paramref name="name"/>, adding one to the company when there is none.
    /// <paramref name="created"/> says which, so an undo removes only what it added.
    /// </summary>
    public static Supplier FindOrCreateSupplier(CompanyData data, string name, out bool created)
    {
        var existing = data.Suppliers.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        created = existing == null;
        if (existing != null) return existing;

        var supplier = new Supplier { Id = new IdGenerator(data).NextSupplierId(), Name = name };
        data.Suppliers.Add(supplier);
        return supplier;
    }

    public static string NormalizeCurrency(string? code, string fallback = "USD") =>
        string.IsNullOrWhiteSpace(code) ? fallback : code.ToUpperInvariant();
}
