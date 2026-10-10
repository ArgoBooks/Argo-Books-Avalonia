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

    // The options beside the invoice are saved with it, so changing one is unsaved work. An
    // untouched draft must not count as changed, or closing it would ask for no reason.
    [Fact]
    public void ChangingOnlyAnOption_WhenContinuingADraft_CountsAsAChange()
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
        Assert.False(vm.HasEditModalChanges);

        vm.OptPassProcessingFee = !vm.OptPassProcessingFee;

        Assert.True(vm.HasEditModalChanges);
    }
}
