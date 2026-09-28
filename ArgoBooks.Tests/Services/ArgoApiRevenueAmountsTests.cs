using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A revenue pushed through the Argo Books API is stored the way the entry form stores one:
/// Subtotal before the discount (Total = Subtotal − Discount + Tax) and TaxRate as a percentage.
/// </summary>
public class ArgoApiRevenueAmountsTests
{
    [Fact]
    public void DiscountedTaxedSale_StoresSubtotalBeforeTheDiscount_AndAPercentageRate()
    {
        var data = new CompanyData();
        var sale = new ArgoRevenue(
            Id: "rev_1", Description: "Order #7",
            Amount: 10800, Currency: "USD", TaxAmount: 800, DiscountAmount: 1000, FeeAmount: 0,
            OccurredOn: "2026-08-14", Customer: null, Category: null, PaymentMethod: null,
            Reference: null, Notes: null, LineItems: null,
            Import: new ArgoImportState("pending", null, null, null));

        new ArgoApiImporter().Import(data, new ArgoApiSyncPreview([], [], [], [], [], [sale], [], new Dictionary<string, ArgoExternalRef>()), new ArgoApiImportCreation());

        var revenue = Assert.Single(data.Revenues);
        Assert.Equal(110m, revenue.Subtotal);             // 108 charged + 10 discount − 8 tax
        Assert.Equal(110m, revenue.LineItems[0].Subtotal);
        Assert.Equal(8m, revenue.TaxRate);                // 8 on a taxable 100
        Assert.Equal(108m, revenue.Total);
    }
}
