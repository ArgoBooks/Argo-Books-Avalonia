using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// When a transaction references a customer/supplier by NAME (not an existing id),
/// the importer links to the matching existing record instead of creating a placeholder stub.
/// Ambiguous/unmatched references keep the placeholder behavior but are reported as warnings,
/// and are never mis-linked to a guess.
/// </summary>
public class NameResolutionImportTests
{
    private static JsonElement Json(string raw) =>
        JsonDocument.Parse(raw).RootElement.Clone();

    private static LlmProcessedData Chunk(SpreadsheetSheetType type, params JsonElement[] entities)
    {
        var chunk = new LlmProcessedData { EntityType = type };
        foreach (var e in entities)
            chunk.Entities.Add(e);
        return chunk;
    }

    [Fact]
    public void Invoice_ReferencingCustomerByName_LinksToExistingId_NoPlaceholder()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme Ltd" });

        var svc = new SpreadsheetImportService();

        // Invoice references the customer by NAME (different casing/whitespace), not by id.
        var invoice = Json("""
            { "id": "INV-100", "customerId": "acme ltd", "total": 50 }
            """);

        var result = svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Invoices, invoice)], "Invoices");

        // The invoice's CustomerId is rewritten to the existing id.
        var imported = Assert.Single(data.Invoices);
        Assert.Equal("CUS-1", imported.CustomerId);

        // No placeholder customer was created; only the original seeded one remains.
        Assert.Single(data.Customers);
        Assert.Equal("Acme Ltd", data.Customers[0].Name);

        // No warning for a clean link.
        Assert.DoesNotContain(result.Warnings, w => w.Contains("placeholder"));
    }

    [Fact]
    public void Expense_ReferencingSupplierByName_LinksToExistingId_NoPlaceholder()
    {
        var data = new CompanyData();
        data.Suppliers.Add(new Supplier { Id = "SUP-1", Name = "Globex" });

        var svc = new SpreadsheetImportService();

        var expense = Json("""
            { "id": "EXP-1", "supplierId": "Globex", "amount": 10, "total": 10 }
            """);

        svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Expenses, expense)], "Expenses");

        var imported = Assert.Single(data.Expenses);
        Assert.Equal("SUP-1", imported.SupplierId);
        Assert.Single(data.Suppliers);
    }

    [Fact]
    public void AmbiguousCustomerName_CreatesNewCustomer_WarnsAndDoesNotMislink()
    {
        var data = new CompanyData();
        // Two existing customers whose names differ by a single character. The resolver is
        // pure-Levenshtein with a 0.92 accept threshold and a 0.05 tie margin, so a reference
        // that sits one edit from BOTH scores a dead tie at the top and is genuinely ambiguous
        // (a short substring like "Smith" against longer names would just score too low to match).
        data.Customers.Add(new Customer { Id = "CUS-1", Name = "Northwind Tradimg" });
        data.Customers.Add(new Customer { Id = "CUS-2", Name = "Northwind Tradinh" });

        var svc = new SpreadsheetImportService();

        // "Northwind Trading" is one edit from each existing name -> ambiguous, must not guess.
        var invoice = Json("""
            { "id": "INV-200", "customerId": "Northwind Trading", "total": 25 }
            """);

        var result = svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Invoices, invoice)], "Invoices");

        var imported = Assert.Single(data.Invoices);

        // Conservatism: NOT linked to either existing customer.
        Assert.NotEqual("CUS-1", imported.CustomerId);
        Assert.NotEqual("CUS-2", imported.CustomerId);

        // A new customer was created, named after the unresolved reference value (not a
        // misleading "Customer (...)" placeholder, and not linked to either ambiguous match).
        Assert.Equal("Northwind Trading", imported.CustomerId);
        Assert.Equal(3, data.Customers.Count);
        Assert.Contains(data.Customers, c => c.Id == "Northwind Trading" && c.Name == "Northwind Trading");

        // The ambiguity is surfaced as a warning.
        Assert.Contains(result.Warnings, w => w.Contains("Northwind Trading") && w.Contains("more than one"));
    }

    [Fact]
    public void UnknownCustomerName_CreatesNewCustomer_AndWarns()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme Ltd" });

        var svc = new SpreadsheetImportService();

        var invoice = Json("""
            { "id": "INV-300", "customerId": "Totally Unrelated Co", "total": 5 }
            """);

        var result = svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Invoices, invoice)], "Invoices");

        var imported = Assert.Single(data.Invoices);
        Assert.Equal("Totally Unrelated Co", imported.CustomerId);
        Assert.Equal(2, data.Customers.Count);
        Assert.Contains(data.Customers, c => c.Id == "Totally Unrelated Co" && c.Name == "Totally Unrelated Co");
        Assert.Contains(result.Warnings, w => w.Contains("was not found"));
    }

    [Fact]
    public void Customer_WithEmailButNoName_FallsBackToUnknown()
    {
        var data = new CompanyData();
        var svc = new SpreadsheetImportService();

        // A real customer row that carries an email but no name should not import nameless.
        var customer = Json("""
            { "id": "CUST-9", "email": "jane@example.com" }
            """);

        svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Customers, customer)], "Customers");

        var imported = Assert.Single(data.Customers);
        Assert.Equal("Unknown", imported.Name);
        Assert.Equal("jane@example.com", imported.Email);
    }

    [Fact]
    public void ExistingId_IsLeftUnchanged_NoResolverWarning()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme Ltd" });

        var svc = new SpreadsheetImportService();

        // References by the real id - the common re-import case. Must not be touched.
        var invoice = Json("""
            { "id": "INV-400", "customerId": "CUS-1", "total": 5 }
            """);

        var result = svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Invoices, invoice)], "Invoices");

        var imported = Assert.Single(data.Invoices);
        Assert.Equal("CUS-1", imported.CustomerId);
        Assert.Single(data.Customers);
        Assert.Empty(result.Warnings);
    }
}
