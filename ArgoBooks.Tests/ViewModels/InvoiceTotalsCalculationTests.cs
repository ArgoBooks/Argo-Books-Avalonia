using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Invoice totals must follow docs/Calculations.md §4: the invoice-level discount and custom fee
/// adjust the subtotal BEFORE tax, then tax is applied to that adjusted subtotal. These tests assert
/// the doc-correct numbers; they fail while the ViewModel taxes the raw line-item subtotal and nets
/// the discount/fee in only after tax.
/// </summary>
public class InvoiceTotalsCalculationTests
{
    [Fact]
    public void FlatDiscountWithTax_TaxesThePostDiscountSubtotal()
    {
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 1, UnitPrice = 100m });
        vm.TaxRate = 10m;            // 10%
        vm.DiscountIsPercent = false;
        vm.DiscountAmount = 20m;     // flat $20 invoice-level discount
        vm.CustomFeeAmount = 0m;
        vm.SecurityDeposit = 0m;

        // §4: subtotal 100 - 20 = 80; tax = 80 * 10% = 8; total = 80 + 8 = 88.
        Assert.Equal(8m, vm.TaxAmount);
        Assert.Equal(88m, vm.Total);
    }

    [Fact]
    public void CustomFeeWithTax_TaxesThePostFeeSubtotal()
    {
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 1, UnitPrice = 100m });
        vm.TaxRate = 10m;            // 10%
        vm.CustomFeeIsPercent = false;
        vm.CustomFeeAmount = 30m;    // flat $30 fee (taxable per §4)
        vm.DiscountAmount = 0m;
        vm.SecurityDeposit = 0m;

        // §4: subtotal 100 + 30 = 130; tax = 130 * 10% = 13; total = 130 + 13 = 143.
        Assert.Equal(13m, vm.TaxAmount);
        Assert.Equal(143m, vm.Total);
    }

    [Fact]
    public void PercentageDiscountWithTax_TaxesThePostDiscountSubtotal()
    {
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 1, UnitPrice = 100m });
        vm.TaxRate = 10m;
        vm.DiscountIsPercent = true;
        vm.DiscountAmount = 25m;     // 25% of the raw subtotal
        vm.SecurityDeposit = 0m;

        // §4: discount = 25; taxable base 100 - 25 = 75; tax = 7.5; total = 82.5. Subtotal stays raw.
        Assert.Equal(100m, vm.Subtotal);
        Assert.Equal(7.5m, vm.TaxAmount);
        Assert.Equal(82.5m, vm.Total);
    }

    [Fact]
    public void DiscountFeeTaxAndDeposit_Combine_PerSpec()
    {
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 1, UnitPrice = 100m });
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 1, UnitPrice = 50m });
        vm.TaxRate = 10m;
        vm.DiscountIsPercent = false;
        vm.DiscountAmount = 30m;     // flat discount
        vm.CustomFeeIsPercent = false;
        vm.CustomFeeAmount = 20m;    // taxable fee
        vm.SecurityDeposit = 40m;    // added to total, NOT taxed

        // taxable base = 150 - 30 + 20 = 140; tax = 14; total = 140 + 14 + 40 = 194.
        Assert.Equal(150m, vm.Subtotal);
        Assert.Equal(14m, vm.TaxAmount);
        Assert.Equal(194m, vm.Total);
    }

    #region Per-line discount

    [Fact]
    public void ALineDiscount_ComesOffTheSubtotal()
    {
        // §4: Subtotal = SUM over LineItem of (Quantity x UnitPrice - Discount). The form has no
        // box for a per-line discount, so this only ever arrives on an imported line; the
        // spreadsheet importer reads a Discount column for every invoice line and the exporter
        // writes one. It used to be ignored by every total in the app and then erased on the
        // next save.
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 10, UnitPrice = 100m, Discount = 50m });
        vm.TaxRate = 10m;

        // subtotal 1000 - 50 = 950; tax = 95; total = 1045.
        Assert.Equal(950m, vm.Subtotal);
        Assert.Equal(95m, vm.TaxAmount);
        Assert.Equal(1045m, vm.Total);
    }

    [Fact]
    public void APerLineTaxRate_DoesNotAddToTheSubtotal()
    {
        // §4 is explicit that the invoice header rate produces the stored tax: "line items each
        // carry a TaxRate but the invoice header rate is what's stored as the final tax". Rolling
        // a line's own rate into the subtotal would have the header rate charged on top of it.
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 10, UnitPrice = 100m, TaxRate = 0.13m });
        vm.TaxRate = 10m;

        Assert.Equal(1000m, vm.Subtotal);
        Assert.Equal(100m, vm.TaxAmount);
    }

    [Fact]
    public void ALineWithNoDiscount_IsStillQuantityTimesPrice()
    {
        // Every line this app creates has Discount = 0, so honouring the field must be a no-op
        // for existing invoices rather than a repricing of them. To the cent, as LineItem.Subtotal
        // has always been: the invoice prints a rounded amount per line, and the Subtotal beneath
        // them is their sum, so the form has to round where the paper does.
        var vm = new InvoiceModalsViewModel();
        vm.LineItems.Add(new LineItemDisplayModel { Quantity = 3, UnitPrice = 0.333m });

        Assert.Equal(1.00m, vm.Subtotal);
        Assert.Equal(1.00m, vm.LineItems[0].Amount);
    }

    #endregion
}
