using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for fixed asset depreciation, the link to the expense that bought one, and how the reports count them.
/// </summary>
public class FixedAssetTests
{
    private static readonly ReportFilters Year2024 = new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31)
    };

    private static FixedAsset Asset(decimal cost, DateTime bought, decimal? lifeYears = 5, AssetType type = AssetType.Vehicle) => new()
    {
        Id = "AST-001",
        Name = "Delivery van",
        Type = type,
        PurchaseDate = bought,
        Cost = cost,
        Currency = "USD",
        Method = DepreciationMethod.StraightLine,
        UsefulLifeYears = lifeYears
    };

    private static Expense Purchase(string id, decimal total, decimal tax, DateTime date) => new()
    {
        Id = id, Date = date, Description = "Van", OriginalCurrency = "USD",
        Total = total, TotalUSD = total, TaxAmount = tax, TaxAmountUSD = tax
    };

    private static AccountingTableData Report(CompanyData data, AccountingReportType type) =>
        new AccountingReportDataService(data, Year2024).GetReportData(type);

    private static AccountingRow Row(AccountingTableData data, string label) => data.Rows.Single(r => r.Label == label);

    // Formatted amounts show a negative in parentheses.
    private static decimal ParseAmount(string formatted)
    {
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    #region Depreciation

    [Fact]
    public void StraightLine_DepreciatesEachMonthFromTheMonthItWasBought()
    {
        var van = Asset(12000m, new DateTime(2024, 3, 15));

        Assert.Equal(2000m, AssetDepreciation.Between(van, new DateTime(2024, 1, 1), new DateTime(2024, 12, 31)));
        Assert.Equal(2400m, AssetDepreciation.FirstYear(van));
        Assert.Equal(10000m, AssetDepreciation.BookValueAsOf(van, new DateTime(2024, 12, 31)));
        Assert.Equal(12000m, AssetDepreciation.BookValueAsOf(van, new DateTime(2024, 3, 30)));
    }

    [Fact]
    public void StraightLine_StopsAtTheEndValue()
    {
        var laptop = Asset(1000m, new DateTime(2024, 1, 1), lifeYears: 1);
        laptop.EndValue = 400m;

        Assert.Equal(12, AssetDepreciation.Months(laptop).Count());
        Assert.Equal(400m, AssetDepreciation.BookValueAsOf(laptop, new DateTime(2030, 1, 1)));
    }

    [Fact]
    public void DecliningBalance_TakesTheRateOfWhatIsLeftEachYear()
    {
        var truck = Asset(10000m, new DateTime(2024, 1, 1), lifeYears: null);
        truck.Method = DepreciationMethod.DecliningBalance;
        truck.RatePercent = 30m;

        var schedule = AssetDepreciation.Schedule(truck);

        Assert.Equal(3000m, schedule[0].Amount);
        Assert.Equal(2100m, schedule[1].Amount);
        Assert.Equal(4900m, schedule[1].BookValueAfter);
    }

    [Fact]
    public void Land_IsNeverDepreciated()
    {
        var lot = Asset(85000m, new DateTime(2020, 4, 1), lifeYears: 30, type: AssetType.Land);

        Assert.Empty(AssetDepreciation.Months(lot));
        Assert.Equal(85000m, AssetDepreciation.BookValueAsOf(lot, new DateTime(2024, 12, 31)));
    }

    [Fact]
    public void Sale_StopsDepreciationBeforeItsMonth_AndCountsTheGain()
    {
        var van = Asset(12000m, new DateTime(2024, 1, 1));
        van.DisposedDate = new DateTime(2024, 7, 10);
        van.DisposalKind = AssetDisposalKind.Sold;
        van.SalePrice = 11500m;

        Assert.Equal(10800m, AssetDepreciation.BookValueAtDisposal(van));
        Assert.Equal(700m, AssetDepreciation.GainOnDisposal(van));
        Assert.False(AssetDepreciation.IsHeldOn(van, new DateTime(2024, 7, 10)));
    }

    [Fact]
    public void Scrapped_LosesItsWholeBookValue()
    {
        var van = Asset(12000m, new DateTime(2024, 1, 1));
        van.DisposedDate = new DateTime(2024, 7, 10);
        van.DisposalKind = AssetDisposalKind.Scrapped;
        van.SalePrice = 500m;

        Assert.Equal(-10800m, AssetDepreciation.GainOnDisposal(van));
    }

    [Fact]
    public void PriorDepreciation_CountsBeforeTheBooksStart()
    {
        var shed = Asset(10000m, new DateTime(2020, 6, 1), lifeYears: 10, type: AssetType.Building);
        shed.TrackedFrom = new DateTime(2024, 1, 1);
        shed.PriorDepreciation = 3000m;

        Assert.Equal(1000m, AssetDepreciation.Between(shed, new DateTime(2024, 1, 1), new DateTime(2024, 12, 31)));
        Assert.Equal(6000m, AssetDepreciation.BookValueAsOf(shed, new DateTime(2024, 12, 31)));
        Assert.Equal(0m, AssetDepreciation.Between(shed, new DateTime(2023, 1, 1), new DateTime(2023, 12, 31)));
    }

    [Fact]
    public void CapitalizedShare_IsTheAssetsCostOverThePreTaxExpense()
    {
        var expense = Purchase("EXP-1", 1100m, 100m, new DateTime(2024, 1, 1));
        var part = Asset(600m, expense.Date);
        part.ExpenseId = expense.Id;

        Assert.Equal(0.6m, AssetDepreciation.CapitalizedShare(expense, [part]));
    }

    #endregion

    #region Expense link

    [Fact]
    public void ExpenseLink_CreatesTheAssetAtThePreTaxCost_AndUndoRemovesIt()
    {
        var data = new CompanyData();
        var expense = Purchase("EXP-1", 33600m, 1600m, new DateTime(2024, 2, 20));
        data.Expenses.Add(expense);

        var change = ExpenseAssetLink.Apply(data, expense, null, null, true, AssetType.Vehicle, 5m);

        var asset = Assert.Single(data.Assets);
        Assert.Equal(32000m, asset.Cost);
        Assert.Equal("EXP-1", asset.ExpenseId);
        Assert.Equal("AST-001", asset.Id);

        change!.Undo();
        Assert.Empty(data.Assets);
        change.Redo();
        Assert.Single(data.Assets);
    }

    [Fact]
    public void ExpenseLink_AssetThatTookTheWholeExpense_FollowsItsNewAmount()
    {
        var data = new CompanyData();
        var expense = Purchase("EXP-1", 1000m, 0m, new DateTime(2024, 2, 20));
        data.Expenses.Add(expense);
        ExpenseAssetLink.Apply(data, expense, null, null, true, AssetType.Equipment, 5m);

        expense.Total = 1200m;
        var change = ExpenseAssetLink.Apply(data, expense, 1000m, "USD", true, AssetType.Equipment, 4m);

        Assert.Equal(1200m, data.Assets[0].Cost);
        Assert.Equal(4m, data.Assets[0].UsefulLifeYears);
        change!.Undo();
        Assert.Equal(1000m, data.Assets[0].Cost);
        Assert.Equal("EXP-1", data.Assets[0].ExpenseId);
    }

    [Fact]
    public void ExpenseLink_AssetGivenPartOfTheExpense_KeepsItsCost()
    {
        var data = new CompanyData();
        var expense = Purchase("EXP-1", 1000m, 0m, new DateTime(2024, 2, 20));
        data.Expenses.Add(expense);
        ExpenseAssetLink.Apply(data, expense, null, null, true, AssetType.Equipment, 5m);
        data.Assets[0].Cost = 700m;

        expense.Total = 1200m;
        ExpenseAssetLink.Apply(data, expense, 1000m, "USD", true, AssetType.Equipment, 5m);

        Assert.Equal(700m, data.Assets[0].Cost);
    }

    [Fact]
    public void ExpenseLink_Unticked_RemovesTheAsset()
    {
        var data = new CompanyData();
        var expense = Purchase("EXP-1", 1000m, 0m, new DateTime(2024, 2, 20));
        data.Expenses.Add(expense);
        ExpenseAssetLink.Apply(data, expense, null, null, true, AssetType.Equipment, 5m);

        var change = ExpenseAssetLink.Apply(data, expense, 1000m, "USD", false, AssetType.Equipment, 5m);

        Assert.Empty(data.Assets);
        change!.Undo();
        Assert.Single(data.Assets);
    }

    [Fact]
    public void ExpenseLink_AssetPutBackAfterTheExpenseWasRenamed_TakesItsNewId()
    {
        var data = new CompanyData();
        var expense = Purchase("EXP-1", 1000m, 0m, new DateTime(2024, 2, 20));
        data.Expenses.Add(expense);
        ExpenseAssetLink.Apply(data, expense, null, null, true, AssetType.Equipment, 5m);
        var change = ExpenseAssetLink.Apply(data, expense, 1000m, "USD", false, AssetType.Equipment, 5m);

        expense.Id = "EXP-9";
        change!.Undo();

        Assert.Equal("EXP-9", Assert.Single(data.Assets).ExpenseId);
    }

    #endregion

    #region Reports

    /// <summary>A $12,000 van bought on 1 January 2024 over five years, and $500 of other spending.</summary>
    private static CompanyData VanAndRunningCosts()
    {
        var data = new CompanyData();
        var van = Purchase("EXP-1", 12000m, 0m, new DateTime(2024, 1, 1));
        data.Expenses.Add(van);
        data.Expenses.Add(Purchase("EXP-2", 500m, 0m, new DateTime(2024, 5, 1)));
        var asset = Asset(12000m, van.Date);
        asset.ExpenseId = van.Id;
        data.Assets.Add(asset);
        return data;
    }

    [Fact]
    public void IncomeStatement_CountsDepreciationInsteadOfTheAssetsCost()
    {
        var result = Report(VanAndRunningCosts(), AccountingReportType.IncomeStatement);

        Assert.Equal(500m, ParseAmount(Row(result, "Uncategorized").Values[0]));
        Assert.Equal(2400m, ParseAmount(Row(result, "Depreciation").Values[0]));
        Assert.Equal(2900m, ParseAmount(Row(result, "Total Operating Expenses").Values[0]));
        Assert.Equal(-2900m, ParseAmount(Row(result, "NET INCOME").Values[0]));
    }

    [Fact]
    public void IncomeStatement_ExpenseThatBoughtStockAndAnAsset_CountsNeitherAsAnExpense()
    {
        var data = new CompanyData();
        var expense = Purchase("EXP-1", 1500m, 0m, new DateTime(2024, 1, 1));
        expense.LineItems =
        [
            new LineItem { ProductId = "PRD-1", Quantity = 5, UnitPrice = 100m, IsStockPurchase = true },
            new LineItem { Description = "Van", Quantity = 1, UnitPrice = 1000m }
        ];
        data.Expenses.Add(expense);
        var asset = Asset(1000m, expense.Date);
        asset.ExpenseId = expense.Id;
        data.Assets.Add(asset);

        var result = Report(data, AccountingReportType.IncomeStatement);

        Assert.Equal(0m, result.Rows.Where(r => r.Label == "Uncategorized").Sum(r => ParseAmount(r.Values[0])));
        Assert.Equal(200m, ParseAmount(Row(result, "Depreciation").Values[0]));
    }

    [Fact]
    public void IncomeStatement_ShowsTheGainOnASale()
    {
        var data = VanAndRunningCosts();
        var asset = data.Assets[0];
        asset.DisposedDate = new DateTime(2024, 7, 10);
        asset.DisposalKind = AssetDisposalKind.Sold;
        asset.SalePrice = 11500m;

        var result = Report(data, AccountingReportType.IncomeStatement);

        Assert.Equal(1200m, ParseAmount(Row(result, "Depreciation").Values[0]));
        Assert.Equal(700m, ParseAmount(Row(result, "Gain on Sale of Assets").Values[0]));
        Assert.Equal(-1000m, ParseAmount(Row(result, "NET INCOME").Values[0]));
    }

    [Fact]
    public void BalanceSheet_ListsFixedAssetsAtCostLessDepreciation_AndStillBalances()
    {
        var result = Report(VanAndRunningCosts(), AccountingReportType.BalanceSheet);

        Assert.Equal(12000m, ParseAmount(Row(result, "Property and Equipment").Values[0]));
        Assert.Equal(-2400m, ParseAmount(Row(result, "Less Accumulated Depreciation").Values[0]));
        Assert.Equal(9600m, ParseAmount(Row(result, "Total Fixed Assets").Values[0]));
        Assert.Equal(
            ParseAmount(Row(result, "TOTAL ASSETS").Values[0]),
            ParseAmount(Row(result, "TOTAL LIABILITIES & EQUITY").Values[0]));
    }

    [Fact]
    public void BalanceSheet_CountsASalePriceAsCash()
    {
        var data = VanAndRunningCosts();
        var before = ParseAmount(Row(Report(data, AccountingReportType.BalanceSheet), "Cash (Estimated)").Values[0]);
        data.Assets[0].DisposedDate = new DateTime(2024, 7, 10);
        data.Assets[0].DisposalKind = AssetDisposalKind.Sold;
        data.Assets[0].SalePrice = 11500m;

        var result = Report(data, AccountingReportType.BalanceSheet);

        Assert.Equal(before + 11500m, ParseAmount(Row(result, "Cash (Estimated)").Values[0]));
        Assert.DoesNotContain(result.Rows, r => r.Label == "Fixed Assets");
    }

    [Fact]
    public void CashFlow_MovesTheAssetPurchaseToInvesting()
    {
        var result = Report(VanAndRunningCosts(), AccountingReportType.CashFlowStatement);

        Assert.Equal(-500m, ParseAmount(Row(result, "Cash Paid for Expenses").Values[0]));
        Assert.Equal(-12000m, ParseAmount(Row(result, "Purchase of Fixed Assets").Values[0]));
        Assert.Equal(-12500m, ParseAmount(Row(result, "NET CHANGE IN CASH").Values[0]));
    }

    [Fact]
    public void AssetRegister_ListsEachAssetWithItsBookValue()
    {
        var result = Report(VanAndRunningCosts(), AccountingReportType.AssetRegister);

        var row = Row(result, "Delivery van");
        Assert.Equal("Vehicle", row.Values[0]);
        Assert.Equal(12000m, ParseAmount(row.Values[2]));
        Assert.Equal(2400m, ParseAmount(row.Values[3]));
        Assert.Equal(2400m, ParseAmount(row.Values[4]));
        Assert.Equal(9600m, ParseAmount(row.Values[5]));
    }

    #endregion
}
