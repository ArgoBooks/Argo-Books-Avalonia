using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Marking a sale or purchase as returned refunds its items after the discount, plus tax, without
/// shipping or fees, the same way for both.
/// </summary>
public class ReturnRefundAmountTests
{
    [Fact]
    public void Sale_TakesTheDiscountOff_AndLeavesShippingAndFeesOut()
    {
        // 100 of items, 10 off, 4.50 tax, 5 shipping, 2 fee: the customer paid 101.50.
        var sale = new Revenue { Amount = 100m, Discount = 10m, TaxAmount = 4.5m, ShippingCost = 5m, Fee = 2m, Total = 101.5m };

        Assert.Equal(94.5m, ReturnLossAmounts.RefundFor(sale, sale.Amount));
    }

    [Fact]
    public void Purchase_FollowsTheSameRule()
    {
        var purchase = new Expense { Amount = 200m, Discount = 20m, TaxAmount = 9m, ShippingCost = 15m, Total = 204m };

        Assert.Equal(189m, ReturnLossAmounts.RefundFor(purchase, purchase.Amount));
    }

    [Fact]
    public void ADiscountLargerThanTheItems_NeverRefundsBelowZero()
    {
        var sale = new Revenue { Amount = 10m, Discount = 30m };

        Assert.Equal(0m, ReturnLossAmounts.RefundFor(sale, sale.Amount));
    }
}
