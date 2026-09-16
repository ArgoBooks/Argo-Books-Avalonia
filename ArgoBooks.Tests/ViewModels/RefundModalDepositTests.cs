using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Returning a rental opens the invoice's refund window for its deposit, which must refund only the
/// deposit, at the amount given back, not the whole invoice.
/// </summary>
public class RefundModalDepositTests
{
    [Fact]
    public void DepositOnly_SelectsJustTheDeposit_AtTheAmountRefunded()
    {
        var invoice = new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", Subtotal = 50m, TaxAmount = 5m, SecurityDeposit = 40m, Total = 95m,
            LineItems = { new LineItem { Description = "Tent", Quantity = 1, UnitPrice = 50m } }
        };
        var payment = new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", Amount = 95m, Source = PaymentSource.Online, ProviderPaymentId = "pi_1"
        };

        var vm = new RefundModalViewModel(null!, invoice, [payment], "Bob", depositOnly: 25m, reason: "Security deposit");

        var selected = Assert.Single(vm.LineRows, r => r.IsSelected);
        Assert.Equal(("deposit", 25m), (selected.Kind, selected.Amount));
        Assert.Equal(25m, vm.RefundTotal);
        Assert.True(vm.CanContinueFromLineItems);
    }
}
