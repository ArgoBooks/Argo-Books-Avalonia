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
    public void PartialRefund_GivesBackOnlyTheTaxOnWhatIsRefunded()
    {
        // 100 + 50 of items at 10% = 15 tax. Refunding only the 50 item returns 5 of it, not all 15.
        var invoice = new Invoice
        {
            Id = "INV-3", InvoiceNumber = "INV-3", Subtotal = 150m, TaxAmount = 15m, Total = 165m,
            LineItems =
            {
                new LineItem { Description = "Desk", Quantity = 1, UnitPrice = 100m },
                new LineItem { Description = "Lamp", Quantity = 1, UnitPrice = 50m }
            }
        };
        var payment = new Payment
        {
            Id = "PAY-3", InvoiceId = "INV-3", Amount = 165m, Source = PaymentSource.Online, ProviderPaymentId = "pi_3"
        };

        var vm = new RefundModalViewModel(null!, invoice, [payment], "Bob");
        Assert.Equal(165m, vm.RefundTotal);

        vm.LineRows.First(r => r.Label == "Desk").IsSelected = false;

        Assert.Equal(55m, vm.RefundTotal);
    }

    [Fact]
    public void LineWithItsOwnTaxRate_IsNotTaxedTwice()
    {
        // Imported lines can carry a tax rate. Two lines of 100 at 10% is 220 in total, not 240.
        var invoice = new Invoice
        {
            Id = "INV-4", InvoiceNumber = "INV-4", Subtotal = 200m, TaxAmount = 20m, Total = 220m,
            LineItems =
            {
                new LineItem { Description = "A", Quantity = 1, UnitPrice = 100m, TaxRate = 0.10m },
                new LineItem { Description = "B", Quantity = 1, UnitPrice = 100m, TaxRate = 0.10m }
            }
        };
        var payment = new Payment
        {
            Id = "PAY-4", InvoiceId = "INV-4", Amount = 220m, Source = PaymentSource.Online, ProviderPaymentId = "pi_4"
        };

        var vm = new RefundModalViewModel(null!, invoice, [payment], "Bob");

        Assert.Equal(220m, vm.RefundTotal);
    }

    // The fee the customer paid was never revenue and never taxed. Refunded by default, it came
    // off both.
    [Fact]
    public void CardFeeTheCustomerPaid_IsNotRefundedUnlessTicked()
    {
        var invoice = new Invoice
        {
            Id = "INV-7", InvoiceNumber = "INV-7", Subtotal = 100m, Total = 100m,
            LineItems = { new LineItem { Description = "Desk", Quantity = 1, UnitPrice = 100m } }
        };
        var payment = new Payment
        {
            Id = "PAY-7", InvoiceId = "INV-7", Amount = 103.20m, ProcessingFee = 3.20m,
            Source = PaymentSource.Online, ProviderPaymentId = "pi_7"
        };

        var vm = new RefundModalViewModel(null!, invoice, [payment], "Bob");

        Assert.Equal(100m, vm.RefundTotal);
        vm.LineRows.First(r => r.Kind == "processingFee").IsSelected = true;
        Assert.Equal(103.20m, vm.RefundTotal);
    }

    // The Tax box starts ticked. Refunding the card fee alone must not take the tax with it.
    [Fact]
    public void CardFeeRefundedAlone_WithTaxLeftTicked_GivesBackNoTax()
    {
        var invoice = new Invoice
        {
            Id = "INV-9", InvoiceNumber = "INV-9", Subtotal = 100m, TaxAmount = 10m, Total = 110m,
            LineItems = { new LineItem { Description = "Desk", Quantity = 1, UnitPrice = 100m } }
        };
        var payment = new Payment
        {
            Id = "PAY-9", InvoiceId = "INV-9", Amount = 113.50m, ProcessingFee = 3.50m,
            Source = PaymentSource.Online, ProviderPaymentId = "pi_9"
        };

        var vm = new RefundModalViewModel(null!, invoice, [payment], "Bob");
        vm.LineRows.First(r => r.Kind == "lineItem").IsSelected = false;
        vm.LineRows.First(r => r.Kind == "processingFee").IsSelected = true;

        Assert.Equal(3.50m, vm.RefundTotal);
    }

    // Refunding only the deposit, with the Tax box left ticked from the default. The deposit was
    // not taxed, so no tax goes back with it.
    [Fact]
    public void DepositRefundedWithTaxLeftTicked_GivesBackNoTax()
    {
        var invoice = new Invoice
        {
            Id = "INV-8", InvoiceNumber = "INV-8", Subtotal = 50m, TaxAmount = 5m, SecurityDeposit = 100m, Total = 155m,
            LineItems = { new LineItem { Description = "Ladder hire", Quantity = 1, UnitPrice = 50m } }
        };
        var payment = new Payment
        {
            Id = "PAY-8", InvoiceId = "INV-8", Amount = 155m, Source = PaymentSource.Online, ProviderPaymentId = "pi_8"
        };

        var vm = new RefundModalViewModel(null!, invoice, [payment], "Bob");
        vm.LineRows.First(r => r.Kind == "lineItem").IsSelected = false;

        Assert.Equal(100m, vm.RefundTotal);
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

    [Fact]
    public void FullRefund_WithAPercentDiscount_IsNotRefusedOverAFractionOfACent()
    {
        // 33.33 less 15% is 28.3305, plus 8% tax is 30.59694. The customer paid the 30.60 it reads.
        var invoice = new Invoice
        {
            Id = "INV-5", InvoiceNumber = "INV-5", Subtotal = 33.33m, DiscountAmount = 15m, DiscountIsPercent = true,
            TaxAmount = 2.26644m, Total = 30.59694m,
            LineItems = { new LineItem { Description = "Part", Quantity = 1, UnitPrice = 33.33m } }
        };
        var payment = new Payment
        {
            Id = "PAY-5", InvoiceId = "INV-5", Amount = 30.60m, Source = PaymentSource.Online, ProviderPaymentId = "pi_5"
        };

        var vm = new RefundModalViewModel(null!, invoice, [payment], "Bob");

        Assert.Equal(30.60m, vm.RefundTotal);
        Assert.True(vm.CanContinueFromLineItems);
    }

    [Fact]
    public void TaxTickedAlone_RefundsTheTax()
    {
        var invoice = new Invoice
        {
            Id = "INV-6", InvoiceNumber = "INV-6", Subtotal = 100m, TaxAmount = 10m, Total = 110m,
            LineItems = { new LineItem { Description = "Desk", Quantity = 1, UnitPrice = 100m } }
        };
        var payment = new Payment
        {
            Id = "PAY-6", InvoiceId = "INV-6", Amount = 110m, Source = PaymentSource.Online, ProviderPaymentId = "pi_6"
        };

        var vm = new RefundModalViewModel(null!, invoice, [payment], "Bob");
        vm.LineRows.First(r => r.Kind == "lineItem").IsSelected = false;

        Assert.Equal(10m, vm.RefundTotal);
    }
}
