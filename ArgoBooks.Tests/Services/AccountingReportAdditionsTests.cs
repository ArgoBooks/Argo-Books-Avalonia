using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Reports;
using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The Trial Balance, Inventory Valuation, Open Invoices, Unpaid Bills and Sales Tax Liability reports, and the
/// Income Statement's comparison columns. Each one has to agree with the Balance Sheet or the aging report it details.
/// </summary>
public class AccountingReportAdditionsTests
{
    private static readonly DateTime YearEnd = new(2024, 12, 31);

    private static CompanyData Company()
    {
        var data = new CompanyData();
        data.Settings.Company.Country = "United States";
        data.Settings.Localization.Currency = "USD";
        data.Customers.Add(new Customer { Id = "C1", Name = "Acme" });
        data.Suppliers.Add(new Supplier { Id = "S1", Name = "Landlord" });
        return data;
    }

    private static Revenue Sale(string id, DateTime date, decimal subtotal, decimal tax = 0m) => new()
    {
        Id = id, Date = date, OriginalCurrency = "USD", Subtotal = subtotal, TaxAmount = tax,
        Total = subtotal + tax, TotalUSD = subtotal + tax
    };

    private static Expense Purchase(string id, DateTime date, decimal amount, DateTime? due = null, DateTime? paid = null) => new()
    {
        Id = id, SupplierId = "S1", Date = date, OriginalCurrency = "USD",
        Amount = amount, Total = amount, TotalUSD = amount, DueDate = due, PaidDate = paid
    };

    private static Invoice OpenInvoice(string id, DateTime issued, DateTime due, decimal total) => new()
    {
        Id = id, InvoiceNumber = $"INV-{id}", CustomerId = "C1", IssueDate = issued, DueDate = due,
        OriginalCurrency = "USD", Total = total, TotalUSD = total, Balance = total, BalanceUSD = total,
        Status = InvoiceStatus.Sent
    };

    private static ReportFilters Range(DateTime start, DateTime end) => new() { StartDate = start, EndDate = end };

    private static ReportFilters UpTo(DateTime end) => new() { EndDate = end };

    private static AccountingTableData Report(CompanyData data, AccountingReportType type, ReportFilters filters,
        IncomeComparison comparison = IncomeComparison.None) =>
        new AccountingReportDataService(data, filters).GetReportData(type, comparison);

    // Formatted amounts show a negative in parentheses.
    private static decimal ParseAmount(string formatted)
    {
        var digits = new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray());
        var value = digits.Length == 0 ? 0m : decimal.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    private static decimal Amount(AccountingTableData table, string label, int column = 0) =>
        ParseAmount(table.Rows.Single(r => r.Label == label).Values[column]);

    #region Trial Balance

    private static CompanyData BusyCompany()
    {
        var data = Company();
        data.Revenues.Add(Sale("R1", new DateTime(2023, 6, 1), 800m));
        data.Revenues.Add(Sale("R2", new DateTime(2024, 3, 1), 1000m, 130m));
        data.Expenses.Add(Purchase("E1", new DateTime(2023, 7, 1), 100m));
        data.Expenses.Add(Purchase("E2", new DateTime(2024, 4, 1), 300m, due: new DateTime(2024, 5, 1)));
        data.Invoices.Add(OpenInvoice("I1", new DateTime(2024, 5, 1), new DateTime(2024, 6, 1), 500m));
        return data;
    }

    [Fact]
    public void TrialBalance_DebitsEqualCredits()
    {
        var report = Report(BusyCompany(), AccountingReportType.TrialBalance, Range(new DateTime(2024, 1, 1), YearEnd));

        Assert.True(Amount(report, "TOTAL", 0) > 0);
        Assert.Equal(Amount(report, "TOTAL", 0), Amount(report, "TOTAL", 1));
    }

    [Fact]
    public void TrialBalance_ShowsTheBalanceSheetsFigures()
    {
        var data = BusyCompany();
        var range = Range(new DateTime(2024, 1, 1), YearEnd);
        var report = Report(data, AccountingReportType.TrialBalance, range);
        var balanceSheet = Report(data, AccountingReportType.BalanceSheet, range);

        Assert.Equal(Amount(balanceSheet, "Cash (Estimated)"), Amount(report, "Cash (Estimated)", 0));
        Assert.Equal(Amount(balanceSheet, "Accounts Payable"), Amount(report, "Accounts Payable", 1));
        Assert.Equal(Amount(balanceSheet, "Sales Tax Payable"), Amount(report, "Sales Tax Payable", 1));
    }

    [Fact]
    public void TrialBalance_CountsIncomeForTheRange_AndEarlierProfitAsRetainedEarnings()
    {
        var report = Report(BusyCompany(), AccountingReportType.TrialBalance, Range(new DateTime(2024, 1, 1), YearEnd));

        // 800 of sales less 100 of expenses in 2023.
        Assert.Equal(700m, Amount(report, "Retained Earnings at Start of Period", 1));
        var expenses = report.Rows.SkipWhile(r => r.Label != "OPERATING EXPENSES");
        Assert.Equal(300m, ParseAmount(expenses.Single(r => r.Label == "Uncategorized").Values[0]));
    }

    [Fact]
    public void TrialBalance_PutsAnOverdrawnBankOnTheCreditSide()
    {
        var data = Company();
        data.Expenses.Add(Purchase("E1", new DateTime(2024, 2, 1), 250m));

        var report = Report(data, AccountingReportType.TrialBalance, UpTo(YearEnd));

        var cash = report.Rows.Single(r => r.Label == "Cash (Estimated)");
        Assert.Equal("", cash.Values[0]);
        Assert.Equal(250m, ParseAmount(cash.Values[1]));
        Assert.Equal(Amount(report, "TOTAL", 0), Amount(report, "TOTAL", 1));
    }

    #endregion

    #region Inventory Valuation

    private static CompanyData StockedCompany()
    {
        var data = Company();
        data.Products.Add(new Product { Id = "P1", Name = "Widget" });
        data.Products.Add(new Product { Id = "P2", Name = "Sold Out" });
        data.Locations.Add(new Location { Id = "L1", Name = "Warehouse" });
        data.Inventory.Add(new InventoryItem { Id = "INV1", ProductId = "P1", Sku = "W-1", LocationId = "L1", InStock = 10, UnitCost = 5m });
        data.Inventory.Add(new InventoryItem { Id = "INV2", ProductId = "P2", LocationId = "L1", InStock = 0, UnitCost = 9m });
        data.StockAdjustments.Add(new StockAdjustment
        {
            Id = "ADJ1", InventoryItemId = "INV1", AdjustmentType = AdjustmentType.Add, Quantity = 4,
            Timestamp = new DateTime(2024, 6, 1)
        });
        return data;
    }

    [Fact]
    public void InventoryValuation_ListsEachItemInStock_AndAddsUpToTheBalanceSheet()
    {
        var data = StockedCompany();
        var report = Report(data, AccountingReportType.InventoryValuation, UpTo(YearEnd));
        var balanceSheet = Report(data, AccountingReportType.BalanceSheet, UpTo(YearEnd));

        var widget = report.Rows.Single(r => r.Label == "Widget");
        Assert.Equal(["W-1", "Warehouse", "10", "$5.00", "$50.00"], widget.Values);
        Assert.DoesNotContain(report.Rows, r => r.Label == "Sold Out");
        Assert.Equal(Amount(balanceSheet, "Inventory"), Amount(report, "TOTAL", 4));
    }

    [Fact]
    public void InventoryValuation_RollsStockBackToTheReportDate()
    {
        var report = Report(StockedCompany(), AccountingReportType.InventoryValuation, UpTo(new DateTime(2024, 3, 1)));

        Assert.Equal("6", report.Rows.Single(r => r.Label == "Widget").Values[2]);
        Assert.Equal(30m, Amount(report, "TOTAL", 4));
    }

    #endregion

    #region Open Invoices

    [Fact]
    public void OpenInvoices_ListsWhatIsOwed_AndAddsUpToAccountsReceivable()
    {
        var data = Company();
        data.Invoices.Add(OpenInvoice("I1", new DateTime(2024, 1, 1), new DateTime(2024, 2, 1), 500m));
        data.Invoices.Add(OpenInvoice("I2", new DateTime(2024, 3, 1), new DateTime(2024, 4, 1), 200m));
        var asOf = UpTo(new DateTime(2024, 3, 2));

        var report = Report(data, AccountingReportType.OpenInvoices, asOf);
        var balanceSheet = Report(data, AccountingReportType.BalanceSheet, asOf);

        var first = report.Rows.Single(r => r.Label == "INV-I1");
        Assert.Equal("Acme", first.Values[0]);
        Assert.Equal("30", first.Values[3]);
        Assert.Equal(500m, ParseAmount(first.Values[5]));
        Assert.Equal("0", report.Rows.Single(r => r.Label == "INV-I2").Values[3]);
        Assert.Equal(Amount(balanceSheet, "Accounts Receivable"), Amount(report, "TOTAL", 5));
    }

    [Fact]
    public void OpenInvoices_WithNothingOwed_SaysSo()
    {
        var report = Report(Company(), AccountingReportType.OpenInvoices, UpTo(YearEnd));

        Assert.Contains(report.Rows, r => r.Label == "No open invoices");
        Assert.Equal(0m, Amount(report, "TOTAL", 5));
    }

    #endregion

    #region Unpaid Bills

    [Fact]
    public void UnpaidBills_ListsWhatIsOwed_AndAddsUpToTheAgingReport()
    {
        var data = Company();
        data.Expenses.Add(Purchase("B1", new DateTime(2024, 1, 15), 300m, due: new DateTime(2024, 2, 14)));
        data.Expenses.Add(Purchase("B2", new DateTime(2024, 1, 20), 75m, due: new DateTime(2024, 2, 19), paid: new DateTime(2024, 2, 1)));
        var asOf = UpTo(new DateTime(2024, 3, 31));

        var report = Report(data, AccountingReportType.UnpaidBills, asOf);
        var aging = Report(data, AccountingReportType.AccountsPayableAging, asOf);

        var bill = report.Rows.Single(r => r.Label == "B1");
        Assert.Equal("Landlord", bill.Values[0]);
        Assert.Equal("46", bill.Values[3]);
        Assert.DoesNotContain(report.Rows, r => r.Label == "B2");
        Assert.Equal(Amount(aging, "TOTAL", 5), Amount(report, "TOTAL", 4));
    }

    [Fact]
    public void UnpaidBills_WithNothingOwed_SaysSo()
    {
        var report = Report(Company(), AccountingReportType.UnpaidBills, UpTo(YearEnd));

        Assert.Contains(report.Rows, r => r.Label == "No unpaid bills");
    }

    #endregion

    #region Sales Tax Liability

    private static CompanyData TaxedCompany()
    {
        var data = Company();
        data.Settings.Company.SalesTaxFrequency = SalesTaxFrequency.Quarterly;
        data.Revenues.Add(Sale("R1", new DateTime(2024, 2, 1), 1000m, 100m));
        data.Revenues.Add(Sale("R2", new DateTime(2024, 5, 1), 1000m, 50m));
        data.SalesTaxFilings.Add(new SalesTaxFiling
        {
            Id = "F1", Frequency = SalesTaxFrequency.Quarterly, PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 3, 31),
            DueDate = new DateTime(2024, 4, 30), PaidDate = new DateTime(2024, 4, 20), Currency = "USD", Collected = 100m
        });
        return data;
    }

    [Fact]
    public void SalesTaxLiability_ShowsEachPeriod_AndWhatIsStillOwed()
    {
        var report = Report(TaxedCompany(), AccountingReportType.SalesTaxLiability, Range(new DateTime(2024, 1, 1), new DateTime(2024, 6, 30)));

        Assert.Equal(100m, Amount(report, "Q1 2024", 4));
        Assert.Equal(0m, Amount(report, "Q1 2024", 5));
        Assert.Equal(50m, Amount(report, "Q2 2024", 3));
        Assert.Equal(50m, Amount(report, "Q2 2024", 5));
    }

    [Fact]
    public void SalesTaxLiability_AddsUpToTheBalanceSheet_WithEarlierPeriodsBroughtForward()
    {
        var data = TaxedCompany();
        var range = Range(new DateTime(2024, 4, 1), new DateTime(2024, 6, 30));
        data.SalesTaxFilings.Clear();

        var report = Report(data, AccountingReportType.SalesTaxLiability, range);
        var balanceSheet = Report(data, AccountingReportType.BalanceSheet, range);

        Assert.Equal(100m, Amount(report, "Brought forward", 5));
        Assert.DoesNotContain(report.Rows, r => r.Label == "Q1 2024");
        Assert.Equal(Amount(balanceSheet, "Sales Tax Payable"), Amount(report, "TOTAL", 5));
    }

    [Fact]
    public void SalesTaxLiability_StopsAtTheReportDate()
    {
        var report = Report(TaxedCompany(), AccountingReportType.SalesTaxLiability, UpTo(new DateTime(2024, 3, 15)));

        Assert.DoesNotContain(report.Rows, r => r.Label == "Q2 2024");
        Assert.Equal(100m, Amount(report, "TOTAL", 5));
    }

    [Fact]
    public void SalesTaxLiability_BeforeAFrequencyIsChosen_UsesQuartersAndSaysSo()
    {
        var data = TaxedCompany();
        data.Settings.Company.SalesTaxFrequency = null;

        var report = Report(data, AccountingReportType.SalesTaxLiability, Range(new DateTime(2024, 1, 1), new DateTime(2024, 6, 30)));

        Assert.Contains(report.Rows, r => r.Label == "Q2 2024");
        Assert.Contains("Sales Tax page", report.Footnote);
    }

    #endregion

    #region Income Statement comparison

    private static CompanyData TwoYears()
    {
        var data = Company();
        data.Revenues.Add(Sale("R1", new DateTime(2023, 2, 10), 400m));
        data.Revenues.Add(Sale("R2", new DateTime(2024, 1, 10), 100m));
        data.Revenues.Add(Sale("R3", new DateTime(2024, 2, 10), 200m));
        data.Expenses.Add(Purchase("E1", new DateTime(2024, 2, 20), 50m));
        return data;
    }

    [Fact]
    public void IncomeStatement_WithoutAComparison_KeepsOneAmountColumn()
    {
        var report = Report(TwoYears(), AccountingReportType.IncomeStatement, Range(new DateTime(2024, 1, 1), new DateTime(2024, 2, 29)));

        Assert.Equal(["Amount"], report.Rows[0].Values);
        Assert.Single(report.Rows.Single(r => r.Label == "NET INCOME").Values);
    }

    [Fact]
    public void IncomeStatement_ComparedWithLastYear_ShowsBothPeriodsAndTheChange()
    {
        var report = Report(TwoYears(), AccountingReportType.IncomeStatement,
            Range(new DateTime(2024, 1, 1), new DateTime(2024, 2, 29)), IncomeComparison.LastYear);

        Assert.Equal(["This Period", "Last Year", "Change"], report.Rows[0].Values);
        Assert.Equal(250m, Amount(report, "NET INCOME", 0));
        Assert.Equal(400m, Amount(report, "NET INCOME", 1));
        Assert.Equal(-150m, Amount(report, "NET INCOME", 2));
        Assert.Equal(4, report.ColumnWidthRatios.Count);
    }

    [Fact]
    public void IncomeStatement_ComparedWithLastYear_NeedsAStartDate()
    {
        var report = Report(TwoYears(), AccountingReportType.IncomeStatement, UpTo(YearEnd), IncomeComparison.LastYear);

        Assert.Equal(["Amount"], report.Rows[0].Values);
        Assert.Contains("date range", report.Footnote);
    }

    [Fact]
    public void IncomeStatement_ByMonth_HasAColumnPerMonthAndATotal()
    {
        var report = Report(TwoYears(), AccountingReportType.IncomeStatement,
            Range(new DateTime(2024, 1, 1), new DateTime(2024, 2, 29)), IncomeComparison.ByMonth);

        Assert.Equal(["Jan", "Feb", "Total"], report.Rows[0].Values);
        Assert.Equal(100m, Amount(report, "Total Revenue", 0));
        Assert.Equal(200m, Amount(report, "Total Revenue", 1));
        Assert.Equal(300m, Amount(report, "Total Revenue", 2));
        Assert.Equal(250m, Amount(report, "NET INCOME", 2));
    }

    [Fact]
    public void IncomeStatement_ByMonth_ShowsTheLast12MonthsOfALongerRange()
    {
        var report = Report(TwoYears(), AccountingReportType.IncomeStatement,
            Range(new DateTime(2023, 1, 1), new DateTime(2024, 2, 29)), IncomeComparison.ByMonth);

        Assert.Equal(13, report.Rows[0].Values.Count);
        Assert.Equal("Mar 23", report.Rows[0].Values[0]);
        Assert.Equal(300m, Amount(report, "Total Revenue", 12));
        Assert.Contains("12 months", report.Footnote);
    }

    [Fact]
    public void IncomeStatement_ByMonth_ForOneMonth_HasNoTotalColumn()
    {
        var report = Report(TwoYears(), AccountingReportType.IncomeStatement,
            Range(new DateTime(2024, 2, 1), new DateTime(2024, 2, 29)), IncomeComparison.ByMonth);

        Assert.Equal(["Feb"], report.Rows[0].Values);
        Assert.Equal(150m, Amount(report, "NET INCOME"));
    }

    [Fact]
    public void IncomeStatement_ByMonth_StartsAndEndsWithTheRange()
    {
        var report = Report(TwoYears(), AccountingReportType.IncomeStatement,
            Range(new DateTime(2024, 1, 15), new DateTime(2024, 2, 15)), IncomeComparison.ByMonth);

        // The sale on Jan 10 is before the range, and the expense on Feb 20 is after it.
        Assert.Equal(0m, Amount(report, "Total Revenue", 0));
        Assert.Equal(200m, Amount(report, "Total Revenue", 1));
        Assert.Equal(200m, Amount(report, "NET INCOME", 2));
    }

    [Fact]
    public void IncomeStatement_ComparedWithLastYear_KeepsAWholeLeapYearFebruary()
    {
        var data = Company();
        data.Revenues.Add(Sale("R1", new DateTime(2024, 2, 29), 100m));
        data.Revenues.Add(Sale("R2", new DateTime(2023, 2, 28), 70m));

        var thisYear = Report(data, AccountingReportType.IncomeStatement,
            Range(new DateTime(2024, 2, 1), new DateTime(2024, 2, 29)), IncomeComparison.LastYear);
        var nextYear = Report(data, AccountingReportType.IncomeStatement,
            Range(new DateTime(2025, 2, 1), new DateTime(2025, 2, 28)), IncomeComparison.LastYear);

        Assert.Equal(70m, Amount(thisYear, "Total Revenue", 1));
        Assert.Equal(100m, Amount(nextYear, "Total Revenue", 1));
    }

    [Fact]
    public void IncomeStatement_ComparedWithLastYear_ListsCategoriesFromEitherYear()
    {
        var data = Company();
        data.Categories.Add(new Category { Id = "CAT1", Name = "Old Line" });
        data.Products.Add(new Product { Id = "P1", Name = "Old", CategoryId = "CAT1" });
        var oldSale = Sale("R1", new DateTime(2023, 2, 1), 90m);
        oldSale.LineItems = [new LineItem { ProductId = "P1", Description = "Old", Quantity = 1, UnitPrice = 90m }];
        data.Revenues.Add(oldSale);

        var report = Report(data, AccountingReportType.IncomeStatement,
            Range(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31)), IncomeComparison.LastYear);

        Assert.Equal(0m, Amount(report, "Old Line", 0));
        Assert.Equal(90m, Amount(report, "Old Line", 1));
        Assert.Equal(-90m, Amount(report, "Old Line", 2));
    }

    [Theory]
    [InlineData(AccountingReportType.TrialBalance)]
    [InlineData(AccountingReportType.InventoryValuation)]
    [InlineData(AccountingReportType.OpenInvoices)]
    [InlineData(AccountingReportType.UnpaidBills)]
    [InlineData(AccountingReportType.SalesTaxLiability)]
    public void WithoutACompany_TheReportStillHasItsTitleAndATotalOfNothing(AccountingReportType type)
    {
        var report = new AccountingReportDataService(null, new ReportFilters()).GetReportData(type);

        Assert.Equal(type.GetDisplayName(), report.Title);
        Assert.Equal(0m, report.Rows.Single(r => r.Label == "TOTAL").Values.Where(v => v.Length > 0).Sum(ParseAmount));
    }

    [Theory]
    [InlineData(AccountingReportType.TrialBalance)]
    [InlineData(AccountingReportType.InventoryValuation)]
    [InlineData(AccountingReportType.OpenInvoices)]
    [InlineData(AccountingReportType.UnpaidBills)]
    [InlineData(AccountingReportType.SalesTaxLiability)]
    public void EveryRow_FitsTheColumnsOfItsReport(AccountingReportType type)
    {
        var data = BusyCompany();
        data.Settings.Company.SalesTaxFrequency = SalesTaxFrequency.Quarterly;

        var report = Report(data, type, Range(new DateTime(2024, 1, 1), YearEnd));

        Assert.Equal(report.ColumnHeaders.Count, report.ColumnWidthRatios.Count);
        Assert.Equal(1d, report.ColumnWidthRatios.Sum(), 6);
        Assert.All(report.Rows.Where(r => r.RowType != AccountingRowType.SectionHeader), r => Assert.Equal(report.ColumnHeaders.Count - 1, r.Values.Count));
    }

    [Theory]
    [InlineData("United Kingdom", "VAT Liability")]
    [InlineData("Canada", "GST/HST Liability")]
    [InlineData("United States", "Sales Tax Liability")]
    public void SalesTaxLiability_TakesTheCountrysNameForTheTax(string country, string title)
    {
        var data = Company();
        data.Settings.Company.Country = country;

        var report = Report(data, AccountingReportType.SalesTaxLiability, UpTo(YearEnd));

        Assert.Equal(title, report.Title);
    }

    [Fact]
    public void InventoryValuation_NamesAnItemWhoseProductWasDeleted()
    {
        var data = Company();
        data.Inventory.Add(new InventoryItem { Id = "INV1", ProductId = "GONE", InStock = 2, UnitCost = 5m });

        var report = Report(data, AccountingReportType.InventoryValuation, UpTo(YearEnd));

        Assert.Contains(report.Rows, r => r.Label == "(Product no longer on file)");
    }

    [Fact]
    public void ColumnWidths_DraggedByTheUser_AreKeptOnlyWhileTheColumnCountFits()
    {
        var plain = Report(TwoYears(), AccountingReportType.IncomeStatement, UpTo(YearEnd));
        var compared = Report(TwoYears(), AccountingReportType.IncomeStatement,
            Range(new DateTime(2024, 1, 1), YearEnd), IncomeComparison.LastYear);
        var table = new AccountingTableReportElement { ColumnWidthRatios = [0.5, 0.5] };

        Assert.Equal(table.ColumnWidthRatios, table.EffectiveColumnRatios(plain));
        Assert.Equal(compared.ColumnWidthRatios, table.EffectiveColumnRatios(compared));
    }

    [Theory]
    [InlineData(2024, null, "2024-01-01", "2024-12-31")]
    [InlineData(2024, 2, "2024-02-01", "2024-02-29")]
    [InlineData(2023, 2, "2023-02-01", "2023-02-28")]
    [InlineData(2026, null, "2026-01-01", "2026-06-15")]
    [InlineData(2026, 6, "2026-06-01", "2026-06-15")]
    [InlineData(2026, 5, "2026-05-01", "2026-05-31")]
    [InlineData(2026, 9, "2026-09-01", "2026-09-30")]
    public void YearOrMonth_IsTheWholePeriod_EndingTodayWhileItIsUnderWay(int year, int? month, string start, string end)
    {
        var (from, to) = DatePresetNames.YearOrMonth(year, month, new DateTime(2026, 6, 15));

        Assert.Equal(DateTime.Parse(start), from);
        Assert.Equal(DateTime.Parse(end), to);
    }

    [Fact]
    public void SalesTaxLiability_WithASaleWaitingForItsRate_KeepsItsFigures_AndSaysSo()
    {
        var data = TaxedCompany();
        var waiting = Sale("R3", new DateTime(2024, 5, 2), 200m, 20m);
        waiting.OriginalCurrency = "EUR";
        waiting.IsPendingConversion = true;
        data.Revenues.Add(waiting);
        var range = Range(new DateTime(2024, 1, 1), new DateTime(2024, 6, 30));

        var report = Report(data, AccountingReportType.SalesTaxLiability, range);

        Assert.Equal(50m, Amount(report, "Q2 2024", 3));
        Assert.Contains("exchange rate", report.Footnote);
        Assert.Equal(Amount(Report(data, AccountingReportType.BalanceSheet, range), "Sales Tax Payable"), Amount(report, "TOTAL", 5));
    }

    [Theory]
    [InlineData("United States")]
    [InlineData("Australia")]
    public void SalesTaxLiability_IgnoresARecordWithNoDate_WhenWorkingOutItsPeriods(string country)
    {
        var data = TaxedCompany();
        data.Settings.Company.Country = country;
        data.Revenues.Add(Sale("R9", default, 10m, 1m));

        var report = Report(data, AccountingReportType.SalesTaxLiability, Range(new DateTime(2024, 1, 1), new DateTime(2024, 6, 30)));

        Assert.True(report.Rows.Count < 10);
    }

    [Fact]
    public void SalesTaxLiability_WithoutTheRateOfAReturnBroughtForward_ShowsPending()
    {
        var data = TaxedCompany();
        data.SalesTaxFilings[0].Currency = "ZZZ";

        // A rate service that knows no rates has to be in place, because with no service at all a
        // conversion hands back the amount it was given and the return never counts as pending.
        var priorRates = RatesInstance.GetValue(null);
        RatesInstance.SetValue(null, new ExchangeRateService(new NoDiskPlatform(), new HttpClient(new NoRatesHandler())));
        try
        {
            var report = Report(data, AccountingReportType.SalesTaxLiability, Range(new DateTime(2024, 7, 1), new DateTime(2024, 9, 30)));

            Assert.Equal("Pending", report.Rows.Single(r => r.Label == "Brought forward").Values[5]);
            Assert.Equal("Pending", report.Rows.Single(r => r.Label == "TOTAL").Values[5]);
        }
        finally
        {
            RatesInstance.SetValue(null, priorRates);
        }
    }

    [Fact]
    public void SalesTaxLiability_BeforeTheFirstSale_HasNothingToShow()
    {
        var report = Report(TaxedCompany(), AccountingReportType.SalesTaxLiability, UpTo(new DateTime(2023, 6, 30)));

        Assert.Contains(report.Rows, r => r.Label == "No sales tax in this period");
        Assert.Equal(0m, Amount(report, "TOTAL", 5));
    }

    [Fact]
    public void SalesTaxLiability_CountsAReturnPaidAfterTheReportDateAsNotPaidYet()
    {
        var report = Report(TaxedCompany(), AccountingReportType.SalesTaxLiability, UpTo(new DateTime(2024, 4, 10)));

        Assert.Equal(0m, Amount(report, "Q1 2024", 4));
        Assert.Equal(100m, Amount(report, "Q1 2024", 5));
    }

    [Theory]
    [InlineData(ReportTemplateFactory.TemplateNames.TrialBalance, AccountingReportType.TrialBalance, true)]
    [InlineData(ReportTemplateFactory.TemplateNames.SalesTaxLiability, AccountingReportType.SalesTaxLiability, true)]
    [InlineData(ReportTemplateFactory.TemplateNames.InventoryValuation, AccountingReportType.InventoryValuation, false)]
    [InlineData(ReportTemplateFactory.TemplateNames.OpenInvoices, AccountingReportType.OpenInvoices, false)]
    [InlineData(ReportTemplateFactory.TemplateNames.UnpaidBills, AccountingReportType.UnpaidBills, false)]
    public void Template_BuildsItsReport_WithThePeriodOnlyWhereTheTableDoesNotGiveTheDate(string name, AccountingReportType type, bool showsPeriod)
    {
        var config = ReportTemplateFactory.CreateFromTemplate(name);

        Assert.True(ReportTemplateFactory.IsBuiltInTemplate(name));
        Assert.Equal(type, config.Elements.OfType<AccountingTableReportElement>().Single().ReportType);
        Assert.Equal(showsPeriod, config.Elements.OfType<DateRangeReportElement>().Any());
    }

    [Fact]
    public void Comparison_IsKeptWhenTheTableIsCopied()
    {
        var table = new AccountingTableReportElement { Comparison = IncomeComparison.ByMonth };

        Assert.Equal(IncomeComparison.ByMonth, ((AccountingTableReportElement)table.Clone()).Comparison);
    }

    [Fact]
    public async Task Comparison_IsKeptInASavedTemplate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ArgoBooks_Comparison_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var config = ReportTemplateFactory.CreateFromTemplate(ReportTemplateFactory.TemplateNames.IncomeStatement);
            config.Elements.OfType<AccountingTableReportElement>().Single().Comparison = IncomeComparison.LastYear;
            var storage = new ReportTemplateStorage(directory);

            await storage.SaveTemplateAsync(config, "Yearly");
            var loaded = await storage.LoadTemplateAsync("Yearly");

            Assert.Equal(IncomeComparison.LastYear, loaded!.Elements.OfType<AccountingTableReportElement>().Single().Comparison);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(40f, 50f, 10f)]
    [InlineData(100f, 50f, 6f)]
    [InlineData(60f, 50f, 8.3333f)]
    public void AmountsTooWideForTheirColumn_AreDrawnSmaller(float textWidth, float available, float expected)
    {
        Assert.Equal(expected, ReportRenderer.FittedFontSize(textWidth, available, 10f), 3);
    }

    [Fact]
    public void IncomeStatement_ByMonth_GivesTheTotalColumnMoreRoom()
    {
        var report = Report(TwoYears(), AccountingReportType.IncomeStatement,
            Range(new DateTime(2023, 3, 1), new DateTime(2024, 2, 29)), IncomeComparison.ByMonth);

        Assert.Equal(14, report.ColumnWidthRatios.Count);
        Assert.True(report.ColumnWidthRatios[^1] > report.ColumnWidthRatios[1]);
        Assert.Equal(1d, report.ColumnWidthRatios.Sum(), 6);
    }

    #endregion

    private static readonly PropertyInfo RatesInstance =
        typeof(ExchangeRateService).GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;

    private sealed class NoRatesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "success": true, "base": "USD", "rates": {} }""", Encoding.UTF8, "application/json")
            });
    }

    private sealed class NoDiskPlatform : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetCachePath() => Path.GetTempPath();
        public void EnsureDirectoryExists(string path) { }
        public bool SupportsFileSystem => false;
        public bool SupportsNativeDialogs => false;
        public bool SupportsBiometrics => false;
        public Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("");
        public Task<bool> AuthenticateWithBiometricAsync(string reason) => Task.FromResult(false);
        public void StorePasswordForBiometric(string fileId, string password) { }
        public string? GetPasswordForBiometric(string fileId) => null;
        public void ClearPasswordForBiometric(string fileId) { }
        public bool SupportsAutoUpdate => false;
        public int MaxRecentCompanies => 10;
        public string NormalizePath(string path) => path;
        public string CombinePaths(params string[] paths) => Path.Combine(paths);
        public string GetMachineId() => "test-machine-id";
        public StringComparer PathComparer => StringComparer.Ordinal;
    }
}
