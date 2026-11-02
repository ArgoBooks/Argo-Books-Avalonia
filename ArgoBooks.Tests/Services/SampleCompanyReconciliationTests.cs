using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The formal reports for the whole sample company agree with each other, as a set of books has to:
/// the Balance Sheet balances, its cash and retained earnings match the Cash Flow Statement and the
/// Income Statement, its tax owed matches the Tax Summary, and the sales reports add up to revenue.
/// </summary>
[Collection("ExchangeRateSingleton")]
public class SampleCompanyReconciliationTests
{
    private static readonly Lazy<Task<(CompanyData Data, ExchangeRateService Rates)>> Sample = new(BuildSampleAsync);

    // Without a rate for every date the sample's CAD amounts wait for conversion and every report reads zero.
    private static async Task<(CompanyData, ExchangeRateService)> BuildSampleAsync()
    {
        var path = SampleInventoryResolutionTests.SampleXlsxPath();
        Assert.True(File.Exists(path), $"Sample workbook not found from {Directory.GetCurrentDirectory()}");

        var rates = new ExchangeRateService(new BrowserPlatformService(), new HttpClient(new FixedRateHandler()));
        var prior = SetInstance(rates);
        try
        {
            var probe = await ImportAsync(path, rates);
            var dates = DisplayCurrency.ReportDates(probe, null)
                .Concat(probe.Inventory.Select(i => i.LastUpdated))
                .Concat(probe.Assets.Select(a => a.PurchaseDate))
                .Append(DateTime.Today)
                .Select(d => d.Date)
                .Distinct();
            foreach (var date in dates)
                await rates.GetExchangeRateAsync("USD", "CAD", date);

            return (await ImportAsync(path, rates), rates);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    private static async Task<CompanyData> ImportAsync(string path, ExchangeRateService rates)
    {
        var data = SampleCompanyService.CreateSampleCompanyData();
        await new SpreadsheetImportService(exchangeRateService: rates)
            .ImportFromExcelAsync(path, data, new ImportOptions { AutoCreateMissingReferences = true });
        SampleCompanyService.CompleteImportedData(data);
        return data;
    }

    private static ExchangeRateService? SetInstance(ExchangeRateService? service)
    {
        var prop = typeof(ExchangeRateService)
            .GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;
        var prior = (ExchangeRateService?)prop.GetValue(null);
        prop.SetValue(null, service);
        return prior;
    }

    private static async Task<AccountingTableData> Report(AccountingReportType type, ReportFilters? filters = null, IncomeComparison comparison = IncomeComparison.None)
    {
        var (data, rates) = await Sample.Value;
        var prior = SetInstance(rates);
        try
        {
            return new AccountingReportDataService(data, filters ?? new ReportFilters()).GetReportData(type, comparison);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    private sealed class FixedRateHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string payload = """{ "success": true, "base": "USD", "rates": { "CAD": 1.35, "EUR": 0.92, "GBP": 0.79, "AUD": 1.52, "MXN": 17.5, "JPY": 150 } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    // Formatted amounts show a negative in parentheses.
    private static decimal ParseAmount(string formatted)
    {
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    private static decimal Amount(AccountingTableData table, string label, int column = 0) =>
        ParseAmount(table.Rows.Single(r => r.Label == label).Values[column]);

    private static decimal GrandTotal(AccountingTableData table) =>
        ParseAmount(table.Rows.Single(r => r.RowType == AccountingRowType.GrandTotalRow).Values[0]);

    [Fact]
    public async Task Sample_HasSalesExpensesAndTax_SoTheChecksBelowMeanSomething()
    {
        var incomeStatement = await Report(AccountingReportType.IncomeStatement);
        var balanceSheet = await Report(AccountingReportType.BalanceSheet);

        Assert.True(Amount(incomeStatement, "Total Revenue") > 0);
        Assert.True(Amount(incomeStatement, "Total Operating Expenses") > 0);
        Assert.NotEqual(0m, Amount(balanceSheet, "GST/HST Payable"));
    }

    [Fact]
    public async Task BalanceSheet_Balances()
    {
        var balanceSheet = await Report(AccountingReportType.BalanceSheet);

        Assert.Equal(Amount(balanceSheet, "TOTAL ASSETS"), Amount(balanceSheet, "TOTAL LIABILITIES & EQUITY"));
    }

    [Fact]
    public async Task BalanceSheet_RetainedEarnings_AreTheIncomeStatementsNetIncomeToDate()
    {
        var balanceSheet = await Report(AccountingReportType.BalanceSheet);
        var incomeStatement = await Report(AccountingReportType.IncomeStatement);

        Assert.Equal(Amount(incomeStatement, "NET INCOME"), Amount(balanceSheet, "Retained Earnings"));
    }

    [Fact]
    public async Task BalanceSheet_Cash_IsTheCashFlowStatementsNetChangeSinceTheFirstRecord()
    {
        Assert.Null((await Sample.Value).Data.Settings.OpeningBalance);
        var balanceSheet = await Report(AccountingReportType.BalanceSheet);
        var cashFlow = await Report(AccountingReportType.CashFlowStatement);

        Assert.Equal(GrandTotal(cashFlow), Amount(balanceSheet, "Cash (Estimated)"));
    }

    [Fact]
    public async Task BalanceSheet_TaxOwed_IsTheTaxSummarysNetTax()
    {
        var balanceSheet = await Report(AccountingReportType.BalanceSheet);
        var taxSummary = await Report(AccountingReportType.TaxSummary);

        Assert.Equal(GrandTotal(taxSummary), Amount(balanceSheet, "GST/HST Payable"));
    }

    [Fact]
    public async Task SalesByCustomer_AddsUpToTheIncomeStatementsRevenue()
    {
        var salesByCustomer = await Report(AccountingReportType.SalesByCustomer);
        var incomeStatement = await Report(AccountingReportType.IncomeStatement);

        Assert.Equal(Amount(incomeStatement, "Total Revenue"), Amount(salesByCustomer, "Total", 2));
    }

    [Fact]
    public async Task SalesByCustomer_EachCustomersSales_AddUpToTheirRowOnTheSummary()
    {
        var (data, _) = await Sample.Value;
        var summary = await Report(AccountingReportType.SalesByCustomer);
        var uniqueNames = data.Customers.GroupBy(c => c.Name).Where(g => g.Count() == 1).Select(g => g.Single());

        var checkedAny = false;
        foreach (var customer in uniqueNames)
        {
            if (summary.Rows.All(r => r.Label != customer.Name))
                continue;

            var detail = await Report(AccountingReportType.SalesByCustomer, new ReportFilters { CustomerId = customer.Id });
            Assert.True(Amount(summary, customer.Name, 2) == Amount(detail, "Net Sales", 2), customer.Name);
            checkedAny = true;
        }

        Assert.True(checkedAny);
    }

    [Fact]
    public async Task TrialBalance_Balances_AndAgreesWithTheBalanceSheet()
    {
        var trialBalance = await Report(AccountingReportType.TrialBalance);
        var balanceSheet = await Report(AccountingReportType.BalanceSheet);

        Assert.True(Amount(trialBalance, "TOTAL", 0) > 0);
        Assert.Equal(Amount(trialBalance, "TOTAL", 0), Amount(trialBalance, "TOTAL", 1));
        Assert.Equal(Amount(balanceSheet, "Cash (Estimated)"), Amount(trialBalance, "Cash (Estimated)", 0));
    }

    [Fact]
    public async Task TrialBalance_ForPartOfTheYear_StillBalances()
    {
        var range = new ReportFilters { StartDate = DateTime.Today.AddMonths(-6), EndDate = DateTime.Today };

        var trialBalance = await Report(AccountingReportType.TrialBalance, range);

        Assert.Equal(Amount(trialBalance, "TOTAL", 0), Amount(trialBalance, "TOTAL", 1));
    }

    [Fact]
    public async Task OpenInvoices_AddUpToAccountsReceivable()
    {
        Assert.Equal(
            Amount(await Report(AccountingReportType.BalanceSheet), "Accounts Receivable"),
            Amount(await Report(AccountingReportType.OpenInvoices), "TOTAL", 5));
    }

    [Fact]
    public async Task UnpaidBills_AddUpToTheAgingReport()
    {
        Assert.Equal(
            Amount(await Report(AccountingReportType.AccountsPayableAging), "TOTAL", 5),
            Amount(await Report(AccountingReportType.UnpaidBills), "TOTAL", 4));
    }

    [Fact]
    public async Task InventoryValuation_AddsUpToTheBalanceSheet()
    {
        Assert.Equal(
            Amount(await Report(AccountingReportType.BalanceSheet), "Inventory"),
            Amount(await Report(AccountingReportType.InventoryValuation), "TOTAL", 4));
    }

    [Fact]
    public async Task SalesTaxLiability_AddsUpToSalesTaxPayable()
    {
        var balanceSheet = await Report(AccountingReportType.BalanceSheet);
        var taxPayable = balanceSheet.Rows.Single(r => r.Label.EndsWith("Payable", StringComparison.Ordinal) && r.IndentLevel > 0 && !r.Label.StartsWith("Accounts", StringComparison.Ordinal));

        Assert.Equal(ParseAmount(taxPayable.Values[0]), Amount(await Report(AccountingReportType.SalesTaxLiability), "TOTAL", 5));
    }

    [Fact]
    public async Task IncomeStatement_Comparisons_KeepTheSameNetIncome()
    {
        var range = new ReportFilters { StartDate = DateTime.Today.AddMonths(-6), EndDate = DateTime.Today };
        var plain = Amount(await Report(AccountingReportType.IncomeStatement, range), "NET INCOME");

        var lastYear = await Report(AccountingReportType.IncomeStatement, range, IncomeComparison.LastYear);
        var byMonth = await Report(AccountingReportType.IncomeStatement, range, IncomeComparison.ByMonth);

        Assert.Equal(plain, Amount(lastYear, "NET INCOME"));
        Assert.Equal(Math.Round(plain, MidpointRounding.AwayFromZero), ParseAmount(byMonth.Rows.Single(r => r.Label == "NET INCOME").Values[^1]));
    }
}
