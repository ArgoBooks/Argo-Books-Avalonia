using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The invoice form cannot show a negative subtotal or total.
///
/// A line floors at zero (LineItem.Subtotal), but the form summed the raw quantity x price less
/// discount, so a 150 discount on a 100 line printed 0.00 on the line and -50 as the Subtotal and
/// Total underneath it. The invoice-level discount had the same hole: it was subtracted in full
/// however small the subtotal was.
/// </summary>
public class InvoiceNegativeTotalTests
{
    [Fact]
    public void ALineDiscountBiggerThanTheLine_LeavesTheSubtotalAtZero()
    {
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 1, UnitPrice = 100m, Discount = 150m });

        Assert.Equal(0m, vm.Subtotal);
        Assert.Equal(0m, vm.Total);
    }

    [Fact]
    public void ALineDiscountBiggerThanTheLine_DoesNotEatIntoTheOtherLines()
    {
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 1, UnitPrice = 100m, Discount = 150m });
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 2, UnitPrice = 50m });

        Assert.Equal(100m, vm.Subtotal);
        Assert.Equal(100m, vm.Total);
    }

    [Fact]
    public void AnInvoiceDiscountBiggerThanTheSubtotal_IsCappedAtTheSubtotal()
    {
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 1, UnitPrice = 100m });
        vm.DiscountIsPercent = false;
        vm.DiscountAmount = 150m;

        Assert.Equal(100m, vm.DiscountCalculated);
        Assert.Equal(0m, vm.TaxableBase);
        Assert.Equal(0m, vm.Total);
    }

    [Fact]
    public void APercentageDiscountOverAHundred_IsCappedAtTheSubtotal()
    {
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 1, UnitPrice = 100m });
        vm.TaxRate = 10m;
        vm.DiscountIsPercent = true;
        vm.DiscountAmount = 150m;

        Assert.Equal(100m, vm.DiscountCalculated);
        Assert.Equal(0m, vm.TaxAmount);
        Assert.Equal(0m, vm.Total);
    }

    /// <summary>
    /// Shipping is still owed when the discount wipes out the goods, so the discount caps at the
    /// subtotal rather than at everything on the invoice.
    /// </summary>
    [Fact]
    public void AnOversizedDiscount_StillLeavesShippingPayable()
    {
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 1, UnitPrice = 100m });
        vm.ShippingAmount = 20m;
        vm.DiscountIsPercent = false;
        vm.DiscountAmount = 500m;

        Assert.Equal(20m, vm.Total);
    }
}
