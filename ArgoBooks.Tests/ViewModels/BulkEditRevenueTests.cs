using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Bulk edits on the Revenue page: the same changes as on Expenses, with sales an invoice or a
/// rental made, and sales of tracked stock, left alone.
/// </summary>
public class BulkEditRevenueTests : ModalViewModelTestBase
{
    private void AddProductsAndCustomers()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Categories.Add(new Category { Id = "CAT-R", Name = "Services", Type = CategoryType.Revenue });
        Company.Products.Add(new Product { Id = "P1", Name = "Consulting", CategoryId = "CAT-R" });
        Company.Products.Add(new Product { Id = "P2", Name = "Training", CategoryId = "CAT-R" });
        Company.Products.Add(new Product { Id = "P-STOCK", Name = "Widget", CategoryId = "CAT-R", TrackInventory = true });
        Company.Customers.Add(new Customer { Id = "C1", Name = "Acme" });
        Company.Customers.Add(new Customer { Id = "C2", Name = "Birch Co" });
    }

    private Revenue AddSale(string id, string? customerId, string productId, string description)
    {
        var sale = new Revenue
        {
            Id = id,
            Date = new DateTime(2025, 3, 1),
            CustomerId = customerId,
            OriginalCurrency = "USD",
            Description = description,
            LineItems = [new LineItem { ProductId = productId, Description = description, Quantity = 1, UnitPrice = 50m }],
            Amount = 50m,
            Subtotal = 50m,
            Total = 50m,
            TotalUSD = 50m
        };
        Company.Revenues.Add(sale);
        return sale;
    }

    [Fact]
    public void SetProduct_LeavesSalesAnInvoiceOrRentalMade_AndSalesOfTrackedStock()
    {
        AddProductsAndCustomers();
        var plain = AddSale("SAL-1", "C1", "P1", "Consulting");
        var fromInvoice = AddSale("SAL-2", "C1", "P1", "Consulting");
        fromInvoice.InvoiceId = "INV-1";
        var fromRental = AddSale("SAL-3", "C1", "P1", "Consulting");
        Company.Rentals.Add(new RentalRecord { Id = "RNT-1", CustomerId = "C1", RevenueId = fromRental.Id });
        var stock = AddSale("SAL-4", "C1", "P-STOCK", "Widget");

        var result = new RevenueModalsViewModel().SetProduct([plain.Id, fromInvoice.Id, fromRental.Id, stock.Id], "P2");

        Assert.Equal((1, 2, 1), (result.Changed, result.MadeElsewhere, result.MovesStock));
        Assert.Equal(("P2", "Training"), (plain.LineItems[0].ProductId, plain.LineItems[0].Description));
        Assert.Equal("P1", fromInvoice.LineItems[0].ProductId);
        Assert.Equal("P1", fromRental.LineItems[0].ProductId);
        Assert.Equal("P-STOCK", stock.LineItems[0].ProductId);
    }

    [Fact]
    public void SetProduct_DoesNothingForAProductThatTracksStock()
    {
        AddProductsAndCustomers();
        var sale = AddSale("SAL-1", "C1", "P1", "Consulting");

        var result = new RevenueModalsViewModel().SetProduct([sale.Id], "P-STOCK");

        Assert.Equal(0, result.Changed);
        Assert.Equal("P1", sale.LineItems[0].ProductId);
        Assert.Equal(0, App.UndoRedoManager.UndoCount);
    }

    [Fact]
    public void SetCounterparty_ChangesTheCustomer_AndUndoes()
    {
        AddProductsAndCustomers();
        var first = AddSale("SAL-1", "C1", "P1", "Consulting");
        var second = AddSale("SAL-2", null, "P1", "Consulting");

        var result = new RevenueModalsViewModel().SetCounterparty([first.Id, second.Id], "C2");

        Assert.Equal(2, result.Changed);
        Assert.Equal(("C2", "C2"), (first.CustomerId, second.CustomerId));

        Undo();
        Assert.Equal(("C1", (string?)null), (first.CustomerId, second.CustomerId));
    }

    [Fact]
    public void DeleteMany_DeletesAsOneUndoStep_AndLeavesAnInvoicesSale()
    {
        AddProductsAndCustomers();
        var plain = AddSale("SAL-1", "C1", "P1", "Consulting");
        var other = AddSale("SAL-2", "C1", "P1", "Consulting");
        var fromInvoice = AddSale("SAL-3", "C1", "P1", "Consulting");
        fromInvoice.InvoiceId = "INV-1";

        var result = new RevenueModalsViewModel().DeleteMany([plain.Id, other.Id, fromInvoice.Id]);

        Assert.Equal((2, 1), (result.Changed, result.MadeElsewhere));
        Assert.Equal([fromInvoice.Id], Company.Revenues.Select(r => r.Id));
        Assert.Equal(1, App.UndoRedoManager.UndoCount);

        Undo();
        Assert.Equal(3, Company.Revenues.Count);
    }
}
