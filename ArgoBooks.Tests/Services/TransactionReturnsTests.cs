using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Returning some or all of a sale or purchase: the refund for the lines chosen, what is left to return,
/// and the stock and cost of goods sold a return moves (docs/Calculations.md §10 and §14).
/// </summary>
public class TransactionReturnsTests
{
    private static readonly DateTime ReturnDate = new(2026, 2, 1);

    private static CompanyData Company()
    {
        var data = new CompanyData();
        data.Products.Add(new Product { Id = "PRD-1", Name = "Flour", Sku = "FLR", TrackInventory = true, CostPrice = 2m });
        data.Products.Add(new Product { Id = "PRD-2", Name = "Lamp", Sku = "LMP" });
        return data;
    }

    // Two chairs at 50 and a lamp at 100, less a 20 discount, plus 18 tax and 10 shipping: 208.
    private static Revenue Sale() => new()
    {
        Id = "REV-1", Date = new DateTime(2026, 1, 10), OriginalCurrency = "USD", CustomerId = "CUS-1",
        Amount = 180m, Discount = 20m, TaxAmount = 18m, ShippingCost = 10m, Total = 208m, TotalUSD = 208m,
        PaymentStatus = RevenuePaymentStatus.Paid,
        LineItems =
        [
            new LineItem { Description = "Chair", Quantity = 2, UnitPrice = 50m },
            new LineItem { Description = "Lamp", ProductId = "PRD-2", Quantity = 1, UnitPrice = 100m }
        ]
    };

    private static TransactionReturns.Request Request(Dictionary<int, decimal> quantities, bool shipping = false, bool moveStock = true) =>
        new(quantities, shipping, false, 0m, moveStock, "Defective", "", ReturnDate);

    [Fact]
    public void ReturningOneChair_RefundsItsShareOfTheDiscountAndTax()
    {
        var amounts = TransactionReturns.AmountsFor(Sale(), new Dictionary<int, decimal> { [0] = 1 }, false, false);

        Assert.Equal(50m, amounts.Items);
        Assert.Equal(5m, amounts.Discount);
        Assert.Equal(4.5m, amounts.Tax);
        Assert.Equal(49.5m, amounts.Total);
    }

    [Fact]
    public void ReturningEverything_RefundsTheWholeSale()
    {
        var amounts = TransactionReturns.AmountsFor(Sale(), new Dictionary<int, decimal> { [0] = 2, [1] = 1 }, true, false);

        Assert.Equal(208m, amounts.Total);
    }

    [Fact]
    public void PartialReturn_RecordsTheRefund_AndLeavesTheRestToReturn()
    {
        var data = Company();
        var sale = Sale();
        data.Revenues.Add(sale);

        var change = TransactionReturns.Record(data, sale, Request(new() { [0] = 1 }));

        var payment = Assert.Single(data.Payments);
        Assert.Same(payment, change.Refund!.Payment);
        Assert.Equal(-49.5m, payment.Amount);
        Assert.Equal(4.5m, payment.RefundTaxAmount);
        Assert.Equal((true, false), TransactionReturns.ReturnedState(data, sale));
        var lines = TransactionReturns.LinesOf(data, sale);
        Assert.Equal(1m, lines[0].Remaining);
        Assert.Equal(1m, lines[1].Remaining);
    }

    [Fact]
    public void ReturningTheRest_RefundsWhatIsLeft_AndMarksTheSaleReturned()
    {
        var data = Company();
        var sale = Sale();
        data.Revenues.Add(sale);
        TransactionReturns.Record(data, sale, Request(new() { [0] = 1 }));

        // Asking for more chairs than are left returns only the one left.
        TransactionReturns.Record(data, sale, Request(new() { [0] = 5, [1] = 1 }, shipping: true));

        Assert.Equal(-208m, data.Payments.Sum(p => p.Amount));
        Assert.Equal((true, true), TransactionReturns.ReturnedState(data, sale));
        Assert.Equal((true, false), TransactionReturns.AlreadyRefunded(data, sale.Id));
    }

    [Fact]
    public void ReturnFromBeforePartialReturns_CountsTheWholeSaleReturned()
    {
        var data = Company();
        var sale = Sale();
        data.Revenues.Add(sale);
        data.Returns.Add(new Return
        {
            Id = "RET-1", OriginalTransactionId = sale.Id, ReturnType = "Customer", Status = ReturnStatus.Completed,
            Items = [new ReturnItem { Quantity = 3 }]
        });

        Assert.All(TransactionReturns.LinesOf(data, sale), l => Assert.Equal(0m, l.Remaining));
        Assert.True(TransactionReturns.ReturnedById(data, [sale])[sale.Id]);
    }

    [Fact]
    public void RefundImportedFromStripe_CountsTheSaleReturned()
    {
        var data = Company();
        var sale = Sale();
        data.Revenues.Add(sale);
        data.Returns.Add(new Return
        {
            Id = "RET-1", OriginalTransactionId = sale.Id, ReturnType = "Customer", Status = ReturnStatus.Completed,
            RefundAmount = 208m
        });

        Assert.Equal((true, true), TransactionReturns.ReturnedState(data, sale));
    }

    [Fact]
    public void ReturnOfOnlyTheShipping_LeavesEveryLineToReturn()
    {
        var data = Company();
        var sale = Sale();
        data.Revenues.Add(sale);

        TransactionReturns.Record(data, sale, Request([], shipping: true));

        Assert.Equal((true, false), TransactionReturns.ReturnedState(data, sale));
        Assert.Equal(-10m, Assert.Single(data.Payments).Amount);
    }

    [Fact]
    public void TrackedProductWithNoStockRecord_MovesNoStock_AndKeepsItsCostOfGoodsSold()
    {
        var data = Company();
        var sale = new Revenue
        {
            Id = "REV-2", Date = new DateTime(2026, 1, 10), OriginalCurrency = "USD", Amount = 20m, Total = 20m, TotalUSD = 20m,
            PaymentStatus = RevenuePaymentStatus.Paid,
            LineItems = [new LineItem { ProductId = "PRD-1", Quantity = 2, UnitPrice = 10m, CostOfGoodsUSD = 4m }]
        };
        data.Revenues.Add(sale);

        var change = TransactionReturns.Record(data, sale, Request(new() { [0] = 1 }));

        Assert.False(change.Record.Restocked);
        Assert.Equal(0m, change.Record.CostOfGoodsReturnedUSD);
    }

    [Fact]
    public void CustomerReturn_PutsStockBack_AndTakesItsCostOffCostOfGoodsSold()
    {
        var data = Company();
        var item = new InventoryItem { Id = "INV-1", ProductId = "PRD-1", InStock = 8, UnitCost = 2m };
        data.Inventory.Add(item);
        var sale = new Revenue
        {
            Id = "REV-2", Date = new DateTime(2026, 1, 10), OriginalCurrency = "USD", Amount = 20m, Total = 20m, TotalUSD = 20m,
            PaymentStatus = RevenuePaymentStatus.Paid,
            LineItems = [new LineItem { ProductId = "PRD-1", Quantity = 2, UnitPrice = 10m, CostOfGoodsUSD = 4m }]
        };
        data.Revenues.Add(sale);
        var year = (new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        var profitBefore = ProfitCalculator.CalculateNetProfitUSD(data, year.Item1, year.Item2);

        var change = TransactionReturns.Record(data, sale, Request(new() { [0] = 1 }));

        Assert.Equal(9m, item.InStock);
        Assert.True(change.Record.Restocked);
        Assert.Equal(2m, change.Record.CostOfGoodsReturnedUSD);
        // The 10 refunded comes off profit, and the 2 the flour cost comes back, as it was not sold after all.
        Assert.Equal(profitBefore - 8m, ProfitCalculator.CalculateNetProfitUSD(data, year.Item1, year.Item2));

        change.TakeOut(data);
        Assert.Equal(8m, item.InStock);
        Assert.Empty(data.StockAdjustments);
        Assert.Empty(data.Payments);
        Assert.Empty(data.Returns);

        change.PutBack(data);
        Assert.Equal(9m, item.InStock);
        Assert.Single(data.StockAdjustments);
        Assert.Single(data.Payments);
    }

    [Fact]
    public void LeavingTheStockTickOff_LeavesStockAndCostOfGoodsSoldAlone()
    {
        var data = Company();
        var item = new InventoryItem { Id = "INV-1", ProductId = "PRD-1", InStock = 8, UnitCost = 2m };
        data.Inventory.Add(item);
        var sale = new Revenue
        {
            Id = "REV-2", Date = new DateTime(2026, 1, 10), OriginalCurrency = "USD", Amount = 20m, Total = 20m, TotalUSD = 20m,
            PaymentStatus = RevenuePaymentStatus.Paid,
            LineItems = [new LineItem { ProductId = "PRD-1", Quantity = 2, UnitPrice = 10m, CostOfGoodsUSD = 4m }]
        };
        data.Revenues.Add(sale);

        var change = TransactionReturns.Record(data, sale, Request(new() { [0] = 1 }, moveStock: false));

        Assert.Equal(8m, item.InStock);
        Assert.False(change.Record.Restocked);
        Assert.Equal(0m, change.Record.CostOfGoodsReturnedUSD);
    }

    [Fact]
    public void ReturnToSupplier_TakesStockOut_AndCreditsOnlyTheReturnedLines()
    {
        var data = Company();
        var item = new InventoryItem { Id = "INV-1", ProductId = "PRD-1", InStock = 10, UnitCost = 2m };
        data.Inventory.Add(item);
        var purchase = new Expense
        {
            Id = "PUR-1", Date = new DateTime(2026, 1, 5), OriginalCurrency = "USD", SupplierId = "SUP-1",
            Amount = 30m, Total = 30m, TotalUSD = 30m,
            LineItems =
            [
                new LineItem { ProductId = "PRD-1", Quantity = 10, UnitPrice = 2m },
                new LineItem { Description = "Lamp", ProductId = "PRD-2", Quantity = 1, UnitPrice = 10m }
            ]
        };
        data.Expenses.Add(purchase);

        var change = TransactionReturns.Record(data, purchase, Request(new() { [0] = 4 }));

        Assert.Equal(6m, item.InStock);
        var credit = change.Refund!.Expense!;
        Assert.Equal(-8m, credit.Total);
        var line = Assert.Single(credit.LineItems);
        Assert.Equal("PRD-1", line.ProductId);
        Assert.Equal(4m, line.Quantity);
        Assert.Contains(credit.Id, ReturnRefunds.SupplierCreditIds(data));
    }

    private static Expense Bill() => new()
    {
        Id = "PUR-1", Date = new DateTime(2026, 1, 5), DueDate = new DateTime(2026, 2, 5), OriginalCurrency = "USD",
        Amount = 30m, Total = 30m, TotalUSD = 30m,
        LineItems =
        [
            new LineItem { Description = "Paper", Quantity = 2, UnitPrice = 10m },
            new LineItem { Description = "Ink", Quantity = 1, UnitPrice = 10m }
        ]
    };

    [Fact]
    public void ReturnOnAnUnpaidBill_ComesOffWhatIsOwed()
    {
        var data = Company();
        var bill = Bill();
        data.Expenses.Add(bill);

        var change = TransactionReturns.Record(data, bill, Request(new() { [1] = 1 }));

        var credit = change.Refund!.Expense!;
        Assert.Equal(-10m, credit.Total);
        Assert.False(credit.IsPaid);
        Assert.Equal(20m, data.Expenses.Where(e => e.IsOwedOn(ReturnDate)).Sum(e => e.Total));
        // Neither counts as spent until the bill is paid.
        Assert.Equal(0m, ExpenseAggregator.SumExpensesUSD(data.Expenses, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
    }

    [Fact]
    public void PayingTheBill_PaysItsCredit_OnTheLaterOfThePaymentAndTheReturn()
    {
        var data = Company();
        var bill = Bill();
        data.Expenses.Add(bill);
        var credit = TransactionReturns.Record(data, bill, Request(new() { [1] = 1 })).Refund!.Expense!;

        bill.PaidDate = new DateTime(2026, 1, 20);
        ReturnRefunds.FollowBill(data, bill);
        Assert.Equal(ReturnDate, credit.PaidDate);
        Assert.Equal(20m, ExpenseAggregator.SumExpensesUSD(data.Expenses, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));

        bill.PaidDate = new DateTime(2026, 3, 1);
        ReturnRefunds.FollowBill(data, bill);
        Assert.Equal(new DateTime(2026, 3, 1), credit.PaidDate);

        bill.PaidDate = null;
        ReturnRefunds.FollowBill(data, bill);
        Assert.False(credit.IsPaid);
    }

    [Fact]
    public void ReturnOnAPaidPurchase_IsPaidOnTheReturnDate()
    {
        var data = Company();
        var purchase = Bill();
        purchase.DueDate = null;
        data.Expenses.Add(purchase);

        var credit = TransactionReturns.Record(data, purchase, Request(new() { [1] = 1 })).Refund!.Expense!;

        Assert.True(credit.IsPaid);
        Assert.Equal(ReturnDate, credit.CashDate);
    }
}
