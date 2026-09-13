using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Regression tests for editing/saving existing draft invoices.
/// </summary>
[Collection("ModalViewModels")]
public class InvoiceDraftEditTests : ModalViewModelTestBase
{
    // "Continue" an existing draft, then click "Save as draft". This should update the same invoice,
    // not create a second one.
    [Fact]
    public async Task SaveAsDraft_WhenContinuingAnExistingDraft_DoesNotCreateADuplicate()
    {
        Company.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme" });
        Company.Invoices.Add(new Invoice
        {
            Id = "INV-1",
            InvoiceNumber = "INV-1",
            CustomerId = "CUS-1",
            Status = InvoiceStatus.Draft,
            Total = 100.00m,
            LineItems = { new LineItem { Description = "Widget", Quantity = 1, UnitPrice = 100.00m } }
        });

        var vm = new InvoiceModalsViewModel();
        vm.ContinueDraftInvoice(new InvoiceDisplayItem { Id = "INV-1" });

        await vm.SaveAsDraftCommand.ExecuteAsync(null);

        Assert.Single(Company.Invoices);
    }
}
