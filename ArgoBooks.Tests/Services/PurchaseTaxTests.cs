using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// How much of the tax on its purchases a company claims back, from its country and province, and
/// what that does to its expenses, its tax owed and the cost of an asset (docs/Calculations.md Rule 1).
/// </summary>
public class PurchaseTaxTests
{
    private static readonly ReportFilters Year2024 = new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31)
    };

    private static Expense Purchase(decimal total, decimal tax, string currency = "USD", decimal? totalUSD = null, decimal? taxUSD = null) => new()
    {
        Id = "EXP-1", Date = new DateTime(2024, 3, 1), Description = "Printer", OriginalCurrency = currency,
        Total = total, TotalUSD = totalUSD ?? total, TaxAmount = tax, TaxAmountUSD = taxUSD ?? tax
    };

    private static CompanyData Company(string country, string? province = null)
    {
        var data = new CompanyData();
        data.Settings.Company.Country = country;
        data.Settings.Company.ProvinceState = province;
        return data;
    }

    private static AccountingTableData Report(CompanyData data, AccountingReportType type) =>
        new AccountingReportDataService(data, Year2024).GetReportData(type);

    private static decimal Amount(AccountingTableData data, string label)
    {
        var formatted = data.Rows.Single(r => r.Label == label).Values[0];
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    #region The rule

    [Theory]
    [InlineData("United States", null, 0.0)]
    [InlineData("USA", null, 0.0)]
    [InlineData("Canada", "BC", 0.05)]
    [InlineData("Canada", "Saskatchewan", 0.05)]
    [InlineData("Canada", "mb", 0.05)]
    public void RuleFor_ClaimsAtMostARate_WhereSomeTaxIsACost(string country, string? province, decimal rate)
    {
        Assert.Equal(new PurchaseTaxRule(rate), PurchaseTax.RuleFor(country, province));
    }

    // Ontario's HST, Alberta's GST, Quebec's QST and VAT are all claimed back. A Canadian company with no
    // province yet claims all of it, as every company did before the province was asked for.
    [Theory]
    [InlineData("Canada", "ON")]
    [InlineData("Canada", "AB")]
    [InlineData("Canada", "QC")]
    [InlineData("Canada", null)]
    [InlineData("United Kingdom", null)]
    [InlineData("Australia", null)]
    [InlineData(null, null)]
    public void RuleFor_ClaimsAll_ElsewhereSoFarAsTheAppKnows(string? country, string? province)
    {
        Assert.Equal(PurchaseTaxRule.All, PurchaseTax.RuleFor(country, province));
    }

    [Theory]
    [InlineData("Canada", true)]
    [InlineData("CA", true)]
    [InlineData("United States", false)]
    [InlineData(null, false)]
    public void NeedsProvince_OnlyInCanada(string? country, bool expected)
    {
        Assert.Equal(expected, PurchaseTax.NeedsProvince(country));
    }

    #endregion

    #region What a purchase costs

    [Fact]
    public void BritishColumbia_ClaimsTheGstButNotThePst()
    {
        // $100 taxed 5% GST and 7% PST.
        var expense = Purchase(112m, 12m);
        var rule = PurchaseTax.RuleFor("Canada", "BC");

        Assert.Equal(5m, PurchaseTax.Claimable(expense, rule));
        Assert.Equal(107m, PurchaseTax.Cost(expense, rule));
    }

    [Fact]
    public void BritishColumbia_ClaimsAllOfAPurchaseTaxedOnlyByGst()
    {
        var expense = Purchase(105m, 5m);

        Assert.Equal(100m, PurchaseTax.Cost(expense, PurchaseTax.RuleFor("Canada", "BC")));
    }

    [Fact]
    public void UnitedStates_SalesTaxIsPartOfTheCost()
    {
        var expense = Purchase(108m, 8m);

        Assert.Equal(0m, PurchaseTax.ClaimableUSD(expense, PurchaseTaxRule.None));
        Assert.Equal(108m, PurchaseTax.CostUSD(expense, PurchaseTaxRule.None));
    }

    [Fact]
    public void ClaimableUSD_TakesTheSameShareOfTheTaxInUSD()
    {
        var expense = Purchase(112m, 12m, "CAD", totalUSD: 56m, taxUSD: 6m);
        var rule = PurchaseTax.RuleFor("Canada", "BC");

        Assert.Equal(2.5m, PurchaseTax.ClaimableUSD(expense, rule));
        Assert.Equal(53.5m, PurchaseTax.CostUSD(expense, rule));
    }

    [Fact]
    public void AssetBoughtWithAnExpense_CostsWhatThePurchaseCostTheBusiness()
    {
        var data = Company("Canada", "BC");
        var expense = Purchase(112m, 12m);
        data.Expenses.Add(expense);

        ExpenseAssetLink.Apply(data, expense, null, null, true, AssetType.Equipment, 5m);

        Assert.Equal(107m, Assert.Single(data.Assets).Cost);
        Assert.Equal(1m, AssetDepreciation.CapitalizedShare(expense, data.Assets, PurchaseTax.RuleFor(data)));
    }

    #endregion

    #region Reports

    /// <summary>A $108 sale with $8 tax and a $54 purchase with $4 tax.</summary>
    private static CompanyData SaleAndPurchase(string country, string? province = null)
    {
        var data = Company(country, province);
        data.Revenues.Add(new Revenue
        {
            Id = "REV-1", Date = new DateTime(2024, 3, 2), OriginalCurrency = "USD",
            Total = 108m, TotalUSD = 108m, TaxAmount = 8m, TaxAmountUSD = 8m, PaymentStatus = RevenuePaymentStatus.Paid
        });
        data.Expenses.Add(Purchase(54m, 4m));
        return data;
    }

    [Fact]
    public void UnitedStates_IncomeStatementCountsTheSalesTaxPaidAsAnExpense()
    {
        var result = Report(SaleAndPurchase("United States"), AccountingReportType.IncomeStatement);

        Assert.Equal(54m, Amount(result, "Total Operating Expenses"));
        Assert.Equal(46m, Amount(result, "NET INCOME"));
    }

    [Fact]
    public void UnitedStates_TaxOwedIsAllTheTaxCollected()
    {
        var data = SaleAndPurchase("United States");

        var summary = Report(data, AccountingReportType.TaxSummary);
        Assert.Equal(8m, Amount(summary, "NET SALES TAX LIABILITY"));
        Assert.DoesNotContain(summary.Rows, r => r.Label == "SALES TAX PAID");

        var balanceSheet = Report(data, AccountingReportType.BalanceSheet);
        Assert.Equal(8m, Amount(balanceSheet, "Sales Tax Payable"));
        Assert.Equal(Amount(balanceSheet, "TOTAL ASSETS"), Amount(balanceSheet, "TOTAL LIABILITIES & EQUITY"));
    }

    [Fact]
    public void Ontario_LeavesTheHstOutOfExpenses_AndSetsItAgainstTheTaxCollected()
    {
        var data = SaleAndPurchase("Canada", "ON");

        Assert.Equal(50m, Amount(Report(data, AccountingReportType.IncomeStatement), "Total Operating Expenses"));
        Assert.Equal(4m, Amount(Report(data, AccountingReportType.TaxSummary), "NET GST/HST PAYABLE"));
    }

    [Fact]
    public void BritishColumbia_ClaimsOnlyTheGst_InTheTaxSummary()
    {
        var data = Company("Canada", "BC");
        data.Revenues.Add(new Revenue
        {
            Id = "REV-1", Date = new DateTime(2024, 3, 2), OriginalCurrency = "USD",
            Total = 112m, TotalUSD = 112m, TaxAmount = 12m, TaxAmountUSD = 12m, PaymentStatus = RevenuePaymentStatus.Paid
        });
        data.Expenses.Add(Purchase(112m, 12m));

        var summary = Report(data, AccountingReportType.TaxSummary);
        Assert.Equal(5m, Amount(summary, "ITC at 5%"));
        Assert.Equal(7m, Amount(summary, "NET GST/HST PAYABLE"));
        Assert.Equal(107m, Amount(Report(data, AccountingReportType.IncomeStatement), "Total Operating Expenses"));
    }

    #endregion
}
