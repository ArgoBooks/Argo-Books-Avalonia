using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Sales tax filing periods, what each owes, and how a filed return's payment reaches the books
/// (docs/Calculations.md §17). Every amount is in USD.
/// </summary>
public class SalesTaxFilingTests
{
    private static readonly ReportFilters Year2024 = new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31)
    };

    private static decimal Amount(CompanyData data, AccountingReportType type, string label)
    {
        var report = new AccountingReportDataService(data, Year2024).GetReportData(type);
        var formatted = report.Rows.Single(r => r.Label == label).Values[0];
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') || formatted.StartsWith('-') ? -value : value;
    }

    // A sale in February with 13 tax, and a purchase in March with 5 tax to claim back: 8 owed for Q1.
    private static CompanyData Books()
    {
        var data = new CompanyData();
        data.Revenues.Add(new Revenue
        {
            Id = "SAL-1", Date = new DateTime(2024, 2, 10), OriginalCurrency = "USD",
            Amount = 100m, TaxAmount = 13m, Total = 113m, TotalUSD = 113m, PaymentStatus = RevenuePaymentStatus.Paid,
            LineItems = [new LineItem { Description = "Chair", Quantity = 1, UnitPrice = 100m }]
        });
        data.Expenses.Add(new Expense
        {
            Id = "PUR-1", Date = new DateTime(2024, 3, 5), OriginalCurrency = "USD",
            Amount = 50m, TaxAmount = 5m, Total = 55m, TotalUSD = 55m,
            LineItems = [new LineItem { Description = "Paper", Quantity = 1, UnitPrice = 50m }]
        });
        return data;
    }

    private static SalesTaxFiling Filing(DateTime paid) => new()
    {
        Id = "STX-001", Frequency = SalesTaxFrequency.Quarterly,
        PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 3, 31), DueDate = new DateTime(2024, 4, 30),
        PaidDate = paid, Currency = "USD", Collected = 13m, ClaimedBack = 5m
    };

    [Theory]
    [InlineData(SalesTaxFrequency.Monthly, "2024-05-01", "2024-05-31", "2024-06-30")]
    [InlineData(SalesTaxFrequency.Quarterly, "2024-04-01", "2024-06-30", "2024-07-31")]
    [InlineData(SalesTaxFrequency.Yearly, "2024-01-01", "2024-12-31", "2025-03-31")]
    public void Periods_EndOnTheirLastDay_AndAreDueAfter(SalesTaxFrequency frequency, string start, string end, string due)
    {
        var period = SalesTaxPeriods.Containing(new DateTime(2024, 5, 10), frequency);

        Assert.Equal(DateTime.Parse(start), period.Start);
        Assert.Equal(DateTime.Parse(end), period.End);
        Assert.Equal(DateTime.Parse(due), period.DueDate);
    }

    [Theory]
    [InlineData("Canada", SalesTaxFrequency.Quarterly, "2024-06-30", "2024-07-31")]
    [InlineData("United States", SalesTaxFrequency.Quarterly, "2024-06-30", "2024-07-20")]
    [InlineData("United Kingdom", SalesTaxFrequency.Quarterly, "2024-06-30", "2024-08-07")]
    [InlineData("United Kingdom", SalesTaxFrequency.Yearly, "2024-12-31", "2025-02-28")]
    [InlineData("Australia", SalesTaxFrequency.Quarterly, "2024-06-30", "2024-07-28")]
    [InlineData("Australia", SalesTaxFrequency.Quarterly, "2024-12-31", "2025-02-28")]
    [InlineData("Australia", SalesTaxFrequency.Monthly, "2024-05-31", "2024-06-21")]
    [InlineData("Australia", SalesTaxFrequency.Yearly, "2024-06-30", "2024-10-31")]
    [InlineData("Germany", SalesTaxFrequency.Monthly, "2024-05-31", "2024-06-30")]
    public void Returns_AreDueByTheCountrysUsualDeadline(string country, SalesTaxFrequency frequency, string end, string due)
    {
        Assert.Equal(DateTime.Parse(due), SalesTaxPeriods.DueDate(DateTime.Parse(end), frequency, country));
    }

    [Fact]
    public void BankLine_MatchesTheReturnItPaid_AndARefundMatchesMoneyIn()
    {
        var data = Books();
        var paid = Filing(new DateTime(2024, 4, 20));
        data.SalesTaxFilings.Add(paid);
        var line = new ArgoBooks.Core.Models.BankMatching.BankStatementLine
        {
            Id = "L1", Date = new DateTime(2024, 4, 20), Amount = -8m, Description = "RECEIVER GENERAL GST"
        };

        var options = new ArgoBooks.Core.Models.BankMatching.BankMatchingOptions();
        var service = new BankMatchingService();
        var candidate = Assert.Single(service.GetManualMatchOptions(line, data, options), c => c.RecordType == BookRecordType.SalesTaxFiling);
        Assert.Equal(-8m, candidate.RecordAmount);
        Assert.Equal("Sales tax", candidate.RecordTypeName);

        service.ConfirmMatch(line, candidate, data);
        Assert.True(paid.BankMatched);

        var moneyIn = new ArgoBooks.Core.Models.BankMatching.BankStatementLine { Id = "L2", Date = paid.PaidDate, Amount = 8m };
        Assert.DoesNotContain(service.GetManualMatchOptions(moneyIn, data, options), c => c.RecordType == BookRecordType.SalesTaxFiling);
    }

    [Fact]
    public async Task Export_WritesEachFiledReturn()
    {
        var data = Books();
        data.SalesTaxFilings.Add(Filing(new DateTime(2024, 4, 20)));
        var path = Path.Combine(Path.GetTempPath(), $"salestax_{Guid.NewGuid():N}.csv");
        try
        {
            await new SpreadsheetExportService().ExportToCsvAsync(path, data, ["Sales Tax Filings"], null, null);
            var rows = CsvReader.ReadAllRows(path, out var headers);
            var row = Assert.Single(rows);
            Assert.Equal("Q1 2024", row[headers.IndexOf("Period")]);
            Assert.Equal("8", row[headers.IndexOf("Amount Paid")].TrimEnd('0').TrimEnd('.'));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SwitchingFrequency_AfterFilingPartOfAPeriod_ListsTheRestOfIt()
    {
        var data = Books();
        data.SalesTaxFilings.Add(new SalesTaxFiling
        {
            Id = "STX-001", Frequency = SalesTaxFrequency.Monthly,
            PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 31), DueDate = new DateTime(2024, 2, 29),
            PaidDate = new DateTime(2024, 2, 15), Currency = "USD"
        });

        var rows = SalesTaxPeriods.Rows(data, SalesTaxFrequency.Quarterly, new DateTime(2024, 5, 15));

        Assert.Equal(["Q2 2024", "Feb - Mar 2024", "January 2024"], rows.Select(r => SalesTaxPeriods.NameOf(r.Period, r.Frequency)));
        Assert.Equal(8m, SalesTaxPeriods.For(data, rows[1].Period, (usd, _) => usd).Net);
    }

    [Fact]
    public void FilingAPeriodWithFractionsOfACent_LeavesNothingPayable()
    {
        var data = Books();
        data.Revenues[0].TaxAmount = 13.334m;
        data.Revenues[0].Total = 113.334m;
        data.Revenues[0].TotalUSD = 113.334m;
        var filing = Filing(new DateTime(2024, 4, 20));
        filing.Collected = 13.33m;
        data.SalesTaxFilings.Add(filing);

        Assert.DoesNotContain(data.Rows(AccountingReportType.BalanceSheet), r => r.Label == "Sales Tax Payable");
    }

    [Fact]
    public void AustralianYear_RunsJulyToJune_UnlessAnotherStartIsChosen()
    {
        var data = Books();
        data.Settings.Company.Country = "Australia";

        var year = SalesTaxPeriods.Rows(data, SalesTaxFrequency.Yearly, new DateTime(2024, 5, 15)).Single();
        Assert.Equal(new DateTime(2023, 7, 1), year.Period.Start);
        Assert.Equal(new DateTime(2024, 6, 30), year.Period.End);
        Assert.Equal(new DateTime(2024, 10, 31), year.Period.DueDate);
        Assert.Equal("2023-24", SalesTaxPeriods.NameOf(year.Period, year.Frequency));

        data.Settings.Company.SalesTaxYearStartMonth = 1;
        Assert.Equal(new DateTime(2024, 1, 1), SalesTaxPeriods.Rows(data, SalesTaxFrequency.Yearly, new DateTime(2024, 5, 15)).Single().Period.Start);
    }

    [Fact]
    public void Quarters_CountFromTheTaxYearsStart()
    {
        var quarter = SalesTaxPeriods.Containing(new DateTime(2024, 3, 10), SalesTaxFrequency.Quarterly, "Canada", yearStartMonth: 2);

        Assert.Equal(new DateTime(2024, 2, 1), quarter.Start);
        Assert.Equal(new DateTime(2024, 4, 30), quarter.End);
        Assert.Equal(new DateTime(2024, 5, 31), quarter.DueDate);
        Assert.Equal("Feb - Apr 2024", SalesTaxPeriods.NameOf(quarter, SalesTaxFrequency.Quarterly));

        var january = SalesTaxPeriods.Containing(new DateTime(2025, 1, 10), SalesTaxFrequency.Quarterly, "Canada", yearStartMonth: 2);
        Assert.Equal(new DateTime(2024, 11, 1), january.Start);
        Assert.Equal(new DateTime(2025, 1, 31), january.End);
    }

    [Fact]
    public void Quarter_OwesTaxCollected_LessTaxClaimedBackOnPurchases()
    {
        var amounts = SalesTaxPeriods.For(Books(), SalesTaxPeriods.Containing(new DateTime(2024, 2, 1), SalesTaxFrequency.Quarterly), (usd, _) => usd);

        Assert.Equal(13m, amounts.Collected);
        Assert.Equal(5m, amounts.ClaimedBack);
        Assert.Equal(8m, amounts.Net);
    }

    [Fact]
    public void FiledReturn_ReplacesThePeriodsItCovers_EvenAtAnotherFrequency()
    {
        var data = Books();
        data.SalesTaxFilings.Add(Filing(new DateTime(2024, 4, 20)));

        var rows = SalesTaxPeriods.Rows(data, SalesTaxFrequency.Monthly, new DateTime(2024, 5, 15));

        Assert.Equal(["May 2024", "April 2024", "Q1 2024"], rows.Select(r => SalesTaxPeriods.NameOf(r.Period, r.Frequency)));
        Assert.NotNull(rows[2].Filing);
    }

    [Fact]
    public void PayingTheReturn_TakesItOffSalesTaxPayableAndCash_NotProfit()
    {
        var data = Books();
        var payableBefore = Amount(data, AccountingReportType.BalanceSheet, "Sales Tax Payable");
        var cashBefore = Amount(data, AccountingReportType.BalanceSheet, "Cash (Estimated)");
        var profitBefore = ProfitCalculator.CalculateNetProfitUSD(data, Year2024.StartDate!.Value, Year2024.EndDate!.Value);

        data.SalesTaxFilings.Add(Filing(new DateTime(2024, 4, 20)));

        Assert.Equal(8m, payableBefore);
        Assert.False(data.Rows(AccountingReportType.BalanceSheet).Any(r => r.Label == "Sales Tax Payable"));
        Assert.Equal(cashBefore - 8m, Amount(data, AccountingReportType.BalanceSheet, "Cash (Estimated)"));
        Assert.Equal(profitBefore, ProfitCalculator.CalculateNetProfitUSD(data, Year2024.StartDate!.Value, Year2024.EndDate!.Value));
        Assert.Equal(-8m, Amount(data, AccountingReportType.CashFlowStatement, "Sales Tax Paid"));
    }

    [Fact]
    public void Reminder_WaitsUntilAFrequencyIsChosen_AndStopsOnceFiled()
    {
        var data = Books();
        var today = new DateTime(2024, 4, 20);

        Assert.Null(SalesTaxPeriods.ComingDue(data, today));

        data.Settings.Company.SalesTaxFrequency = SalesTaxFrequency.Quarterly;
        var due = SalesTaxPeriods.ComingDue(data, today);
        Assert.Equal(new DateTime(2024, 4, 30), due!.Period.DueDate);

        data.SalesTaxFilings.Add(Filing(today));
        Assert.Null(SalesTaxPeriods.ComingDue(data, today));
    }

    [Fact]
    public void Reminder_LeavesOutAPeriodThatOwesNothing()
    {
        var data = new CompanyData();
        data.Settings.Company.SalesTaxFrequency = SalesTaxFrequency.Quarterly;
        data.Revenues.Add(new Revenue { Id = "SAL-1", Date = new DateTime(2024, 2, 10), OriginalCurrency = "USD", Amount = 100m, Total = 100m, TotalUSD = 100m });

        Assert.Null(SalesTaxPeriods.ComingDue(data, new DateTime(2024, 4, 20)));
    }
}

internal static class ReportRows
{
    public static List<AccountingRow> Rows(this CompanyData data, AccountingReportType type) =>
        new AccountingReportDataService(data, new ReportFilters { StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31) })
            .GetReportData(type).Rows;
}
