using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Profit with cost of goods sold: tracked stock bought is not an expense until it sells
/// (docs/Calculations.md §14).
/// </summary>
public class CostOfGoodsProfitTests
{
    private static readonly DateTime Start = new(2026, 1, 1);
    private static readonly DateTime End = new(2026, 1, 31);

    private static Expense StockAndDeliveryPurchase() => new()
    {
        Id = "PUR-1",
        Date = new DateTime(2026, 1, 5),
        OriginalCurrency = "USD",
        Total = 120m,
        Amount = 120m,
        LineItems =
        [
            new LineItem { ProductId = "PRD-1", Quantity = 10, UnitPrice = 10m, IsStockPurchase = true },
            new LineItem { ProductId = "PRD-2", Quantity = 1, UnitPrice = 20m }
        ]
    };

    private static Revenue SaleCosting(decimal costOfGoods, RevenuePaymentStatus status = RevenuePaymentStatus.Paid) => new()
    {
        Id = "REV-1",
        Date = new DateTime(2026, 1, 10),
        OriginalCurrency = "USD",
        Total = 50m,
        Amount = 50m,
        PaymentStatus = status,
        LineItems = [new LineItem { ProductId = "PRD-1", Quantity = 3, UnitPrice = 50m / 3, CostOfGoodsUSD = costOfGoods }]
    };

    [Fact]
    public void StockLines_AreNotAnExpense()
    {
        var expense = StockAndDeliveryPurchase();

        Assert.Equal(100m, CostOfGoodsAggregator.StockPurchaseUSD(expense));
        Assert.Equal(20m, CostOfGoodsAggregator.OperatingExpenseUSD(expense));
    }

    [Fact]
    public void ExpenseWithoutStock_IsCountedInFull()
    {
        var expense = StockAndDeliveryPurchase();
        foreach (var line in expense.LineItems)
            line.IsStockPurchase = false;

        Assert.Equal(expense.EffectiveTotalUSD, CostOfGoodsAggregator.OperatingExpenseUSD(expense));
    }

    [Fact]
    public void NetProfit_SubtractsCostOfGoodsSold_InPlaceOfTheStockBought()
    {
        var data = new CompanyData();
        data.Expenses.Add(StockAndDeliveryPurchase());
        data.Revenues.Add(SaleCosting(30m));

        // 50 revenue, 20 delivery, 30 cost of the stock that sold. The 100 of stock bought is not an expense.
        Assert.Equal(0m, ProfitCalculator.CalculateNetProfitUSD(data, Start, End));
    }

    [Fact]
    public void UnpaidSale_CountsNeitherItsRevenueNorItsCost()
    {
        var data = new CompanyData();
        data.Revenues.Add(SaleCosting(30m, RevenuePaymentStatus.Unpaid));

        Assert.Equal(0m, ProfitCalculator.CalculateNetProfitUSD(data, Start, End));
        Assert.Equal(30m, CostOfGoodsAggregator.SumCostOfGoodsSoldUSD(data.Revenues, Start, End, collectedOnly: false));
    }

    [Fact]
    public void NetProfitByDay_IncludesCostOfGoodsSoldOnTheDayOfTheSale()
    {
        var data = new CompanyData();
        data.Revenues.Add(SaleCosting(30m));

        var byDay = ProfitCalculator.CalculateNetProfitByDayUSD(data, Start, End);

        Assert.Equal(20m, byDay[new DateTime(2026, 1, 10)]);
    }

    // A sale waiting for its stock's cost counts that cost as 0, so profit read high with nothing to
    // say so. It shows Pending instead, as a figure waiting for an exchange rate does.
    [Fact]
    public void SaleWaitingForItsStockCost_LeavesTheProfitCardsPending()
    {
        var data = new CompanyData();
        var sale = SaleCosting(0m);
        sale.LineItems[0].IsCostOfGoodsPending = true;
        data.Revenues.Add(sale);

        Assert.True(CostOfGoodsAggregator.IsCostOfGoodsPending(data.Revenues, Start, End, collectedOnly: true));
        Assert.Equal(ArgoBooks.Services.CurrencyService.PendingMarker,
            ArgoBooks.Services.CurrencyService.FormatNetProfitOrPending(data, Start, End));
    }

    [Fact]
    public void SaleWaitingForItsStockCost_LeavesGrossProfitAndNetIncomePending_OnTheIncomeStatement()
    {
        var data = new CompanyData();
        var sale = SaleCosting(0m);
        sale.LineItems[0].IsCostOfGoodsPending = true;
        data.Revenues.Add(sale);

        var statement = new AccountingReportDataService(data, new ReportFilters { StartDate = Start, EndDate = End })
            .GetReportData(AccountingReportType.IncomeStatement);

        Assert.Equal("Pending", statement.Rows.Single(r => r.Label == "Gross Profit").Values[0]);
        Assert.Equal("Pending", statement.Rows.Single(r => r.RowType == AccountingRowType.GrandTotalRow).Values[0]);
        Assert.DoesNotContain("Pending", statement.Rows.First(r => r.RowType == AccountingRowType.SubtotalRow).Values[0]);
    }

    [Fact]
    public void StartCostOfGoods_SetsAsideStockOnHandOnce()
    {
        var data = new CompanyData();
        var item = new InventoryItem { Id = "INV-1", ProductId = "PRD-1", InStock = 7 };
        data.Inventory.Add(item);

        CompanyManager.StartCostOfGoodsIfNeeded(data);

        Assert.Equal(7m, item.OpeningUnits);
        Assert.Equal(CompanyManager.CostOfGoodsStartVersion, data.Settings.CostOfGoodsStartedVersion);
        Assert.True(data.ChangesMade);

        item.InStock = 20;
        CompanyManager.StartCostOfGoodsIfNeeded(data);
        Assert.Equal(7m, item.OpeningUnits);
    }
}
