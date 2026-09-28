using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// An invoice's total includes its shipping, so a full refund has to be able to give it back.
/// </summary>
public class RefundModalShippingTests
{
    [Fact]
    public void FullRefund_IncludesShipping_AndMatchesWhatWasPaid()
    {
        // 100 of items + 20 shipping, taxed at 10% on both = 12 tax, 132 in total.
        var invoice = new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", Subtotal = 100m, ShippingAmount = 20m, TaxAmount = 12m, Total = 132m,
            LineItems = { new LineItem { Description = "Chair", Quantity = 1, UnitPrice = 100m } }
        };
        var payment = new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", Amount = 132m, Source = PaymentSource.Online, ProviderPaymentId = "pi_1"
        };

        var vm = new RefundModalViewModel(null!, invoice, [payment], "Bob");

        var shipping = Assert.Single(vm.LineRows, r => r.Kind == "shipping");
        Assert.Equal(20m, shipping.Amount);
        Assert.True(shipping.IsSelected);
        Assert.Equal(132m, vm.RefundTotal);
        Assert.True(vm.CanContinueFromLineItems);
    }

    [Fact]
    public void NoShipping_NoShippingRow()
    {
        var invoice = new Invoice
        {
            Id = "INV-2", InvoiceNumber = "INV-2", Subtotal = 50m, Total = 50m,
            LineItems = { new LineItem { Description = "Lamp", Quantity = 1, UnitPrice = 50m } }
        };
        var payment = new Payment
        {
            Id = "PAY-2", InvoiceId = "INV-2", Amount = 50m, Source = PaymentSource.Online, ProviderPaymentId = "pi_2"
        };

        var vm = new RefundModalViewModel(null!, invoice, [payment], "Bob");

        Assert.DoesNotContain(vm.LineRows, r => r.Kind == "shipping");
    }
}
