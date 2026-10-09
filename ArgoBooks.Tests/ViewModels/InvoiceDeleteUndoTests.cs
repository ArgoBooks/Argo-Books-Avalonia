using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Deleting an invoice also unlinks the rentals that referenced it and removes the revenue it
/// created, so undoing the delete has to put all three back. Restoring only the invoice would leave
/// the books short of its income and the rental no longer able to find what it was billed on.
/// </summary>
public class InvoiceDeleteUndoTests : ModalViewModelTestBase
{
    private Invoice Seed()
    {
        Company.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme" });

        var invoice = new Invoice
        {
            Id = "INV-1",
            InvoiceNumber = "INV-1",
            CustomerId = "CUS-1",
            Total = 100m,
            LineItems = { new LineItem { Description = "Hire", Quantity = 1, UnitPrice = 100m, RentalRecordId = "REN-1" } }
        };
        Company.Invoices.Add(invoice);

        // Auto-created from the invoice: nothing on the invoice points at it, which is what tells
        // the delete to remove it outright rather than only unlink it.
        Company.Revenues.Add(new Revenue { Id = "REV-1", Amount = 100m, InvoiceId = "INV-1" });
        Company.Rentals.Add(new RentalRecord { Id = "REN-1", InvoiceIds = { "INV-1" } });

        return invoice;
    }

    [Fact]
    public void DeletingAnInvoice_TakesItsRevenueAndRentalLinkWithIt()
    {
        var invoice = Seed();

        new InvoiceModalsViewModel().DeleteInvoice(invoice);

        Assert.Empty(Company.Invoices);
        Assert.Empty(Company.Revenues);
        Assert.Empty(Company.Rentals[0].InvoiceIds);
    }

    [Fact]
    public void UndoingTheDelete_PutsTheInvoiceRevenueAndRentalLinkBack()
    {
        var invoice = Seed();
        new InvoiceModalsViewModel().DeleteInvoice(invoice);

        Undo();

        var restored = Assert.Single(Company.Invoices);
        Assert.Equal("INV-1", restored.Id);

        var revenue = Assert.Single(Company.Revenues);
        Assert.Equal("INV-1", revenue.InvoiceId);

        Assert.Contains("INV-1", Company.Rentals[0].InvoiceIds);
    }

    [Fact]
    public void RedoingTheDelete_RemovesAllThreeAgain()
    {
        var invoice = Seed();
        new InvoiceModalsViewModel().DeleteInvoice(invoice);
        Undo();

        Redo();

        Assert.Empty(Company.Invoices);
        Assert.Empty(Company.Revenues);
        Assert.Empty(Company.Rentals[0].InvoiceIds);
    }

    [Fact]
    public void UndoingTwice_DoesNotAddTheInvoiceBackTwice()
    {
        var invoice = Seed();
        new InvoiceModalsViewModel().DeleteInvoice(invoice);

        Undo();
        Undo();

        Assert.Single(Company.Invoices);
        Assert.Single(Company.Revenues);
    }

    /// <summary>
    /// A revenue the user wrote first and then invoiced is only unlinked by the delete, never
    /// removed, so undo has to re-link that one rather than add a second copy of it.
    /// </summary>
    [Fact]
    public void UndoingTheDelete_RelinksARevenueTheUserCreated()
    {
        Company.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme" });
        Company.Revenues.Add(new Revenue { Id = "REV-1", Amount = 100m, InvoiceId = "INV-1" });

        var invoice = new Invoice
        {
            Id = "INV-1",
            InvoiceNumber = "INV-1",
            CustomerId = "CUS-1",
            Total = 100m,
            LineItems = { new LineItem { Description = "Work", Quantity = 1, UnitPrice = 100m, RevenueRecordId = "REV-1" } }
        };
        Company.Invoices.Add(invoice);

        var vm = new InvoiceModalsViewModel();
        vm.DeleteInvoice(invoice);

        var kept = Assert.Single(Company.Revenues);
        Assert.Null(kept.InvoiceId);

        Undo();

        var relinked = Assert.Single(Company.Revenues);
        Assert.Equal("INV-1", relinked.InvoiceId);
    }
}
