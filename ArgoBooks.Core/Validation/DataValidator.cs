using System.Text.RegularExpressions;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Entities;

namespace ArgoBooks.Core.Validation;

/// <summary>
/// Validates data models before saving.
/// </summary>
public partial class DataValidator(CompanyData companyData)
{
    #region Entity Validation

    /// <summary>
    /// Validates a customer.
    /// </summary>
    public ValidationResult ValidateCustomer(Customer customer)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(customer.Name))
            result.AddError(nameof(customer.Name), "Customer name is required.");

        if (!string.IsNullOrWhiteSpace(customer.Email) && !IsValidEmail(customer.Email))
            result.AddError(nameof(customer.Email), "Invalid email address format.");

        // Check for duplicate name (excluding self)
        if (companyData.Customers.Any(c => c.Id != customer.Id &&
            c.Name.Equals(customer.Name, StringComparison.OrdinalIgnoreCase)))
            result.AddError(nameof(customer.Name), "A customer with this name already exists.");

        return result;
    }

    /// <summary>
    /// Validates a product.
    /// </summary>
    public ValidationResult ValidateProduct(Product product)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(product.Name))
            result.AddError(nameof(product.Name), "Product name is required.");

        if (product.UnitPrice < 0)
            result.AddError(nameof(product.UnitPrice), "Unit price cannot be negative.");

        if (product.CostPrice < 0)
            result.AddError(nameof(product.CostPrice), "Cost price cannot be negative.");

        if (product.TaxRate < 0 || product.TaxRate > 1)
            result.AddError(nameof(product.TaxRate), "Tax rate must be between 0 and 1 (0% to 100%).");

        // Check for duplicate SKU (excluding self)
        if (!string.IsNullOrWhiteSpace(product.Sku) &&
            companyData.Products.Any(p => p.Id != product.Id &&
            p.Sku.Equals(product.Sku, StringComparison.OrdinalIgnoreCase)))
            result.AddError(nameof(product.Sku), "A product with this SKU already exists.");

        // Validate supplier exists
        if (!string.IsNullOrWhiteSpace(product.SupplierId) &&
            companyData.GetSupplier(product.SupplierId) == null)
            result.AddError(nameof(product.SupplierId), "Supplier not found.");

        // Validate category exists
        if (!string.IsNullOrWhiteSpace(product.CategoryId) &&
            companyData.GetCategory(product.CategoryId) == null)
            result.AddError(nameof(product.CategoryId), "Category not found.");

        return result;
    }

    /// <summary>
    /// Validates a supplier.
    /// </summary>
    public ValidationResult ValidateSupplier(Supplier supplier)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(supplier.Name))
            result.AddError(nameof(supplier.Name), "Supplier name is required.");

        if (!string.IsNullOrWhiteSpace(supplier.Email) && !IsValidEmail(supplier.Email))
            result.AddError(nameof(supplier.Email), "Invalid email address format.");

        // Check for duplicate name (excluding self)
        if (companyData.Suppliers.Any(s => s.Id != supplier.Id &&
            s.Name.Equals(supplier.Name, StringComparison.OrdinalIgnoreCase)))
            result.AddError(nameof(supplier.Name), "A supplier with this name already exists.");

        return result;
    }

    /// <summary>
    /// Validates a category.
    /// </summary>
    public ValidationResult ValidateCategory(Category category)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(category.Name))
            result.AddError(nameof(category.Name), "Category name is required.");

        // Check for duplicate name within same type (excluding self)
        if (companyData.Categories.Any(c => c.Id != category.Id &&
            c.Type == category.Type &&
            c.Name.Equals(category.Name, StringComparison.OrdinalIgnoreCase)))
            result.AddError(nameof(category.Name), "A category with this name already exists.");

        // Validate parent category exists and is same type
        if (!string.IsNullOrWhiteSpace(category.ParentId))
        {
            var parent = companyData.GetCategory(category.ParentId);
            if (parent == null)
                result.AddError(nameof(category.ParentId), "Parent category not found.");
            else if (parent.Type != category.Type)
                result.AddError(nameof(category.ParentId), "Parent category must be the same type.");
            else if (parent.Id == category.Id)
                result.AddError(nameof(category.ParentId), "Category cannot be its own parent.");
        }

        return result;
    }

    /// <summary>
    /// Validates a location.
    /// </summary>
    public ValidationResult ValidateLocation(Location location)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(location.Name))
            result.AddError(nameof(location.Name), "Location name is required.");

        if (location.Capacity < 0)
            result.AddError(nameof(location.Capacity), "Capacity cannot be negative.");

        // Check for duplicate name (excluding self)
        if (companyData.Locations.Any(l => l.Id != location.Id &&
            l.Name.Equals(location.Name, StringComparison.OrdinalIgnoreCase)))
            result.AddError(nameof(location.Name), "A location with this name already exists.");

        return result;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Validates an email address format. Canonical email check used across the app:
    /// requires non-empty local part, '@', non-empty domain, '.', and non-empty TLD.
    /// </summary>
    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return EmailRegex().IsMatch(email.Trim());
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    #endregion
}
