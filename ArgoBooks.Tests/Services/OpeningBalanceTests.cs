using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The opening bank balance is where the Balance Sheet's cash starts. It already includes every
/// transaction dated before it, so only what moved on or after its date is added to it.
/// </summary>
public class OpeningBalanceTests
{
    private static readonly DateTime OpeningDay = new(2026, 3, 1);

    private static CompanyData Company(decimal? opening)
    {
        var data = new CompanyData();
        if (opening is { } amount)
            data.Settings.OpeningBalance = new OpeningBalance { Amount = amount, Currency = "USD", Date = OpeningDay };
        return data;
    }

    private static Payment Paid(string id, decimal amount, DateTime date) => new()
    {
        Id = id,
        Amount = amount,
        Date = date,
        InvoiceId = string.Empty,
        RevenueId = string.Empty,
    };

    private static AccountingTableData BalanceSheet(CompanyData data, DateTime? end = null) =>
        new AccountingReportDataService(data, new ReportFilters { StartDate = new DateTime(2026, 1, 1), EndDate = end ?? new DateTime(2026, 12, 31) })
            .GetReportData(AccountingReportType.BalanceSheet);

    private static decimal Row(AccountingTableData table, string label) =>
        Money(table.Rows.First(r => r.Label.StartsWith(label, StringComparison.Ordinal)).Values[0]);

    private static decimal Money(string formatted) =>
        decimal.Parse(new string([.. formatted.Where(c => char.IsAsciiDigit(c) || c is '.' or '-')]),
                      System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Cash_StartsFromTheOpeningBalance_AndAddsWhatCameInSince()
    {
        var data = Company(5000m);
        data.Payments.Add(Paid("PAY-0001", 500m, new DateTime(2026, 6, 15)));

        var sheet = BalanceSheet(data);

        Assert.Equal(5500m, Row(sheet, "Cash"));
        Assert.Equal(5000m, Row(sheet, "Opening Balance"));
        // A payment for no sale isn't profit, so it is the owner's equity rather than retained earnings.
        Assert.Equal(0m, Row(sheet, "Retained Earnings"));
        Assert.Equal(500m, Row(sheet, "Owner's Equity"));
        Assert.Equal(Row(sheet, "TOTAL ASSETS"), Row(sheet, "TOTAL LIABILITIES & EQUITY"));
    }

    [Fact]
    public void MoneyDatedBeforeTheOpeningBalance_IsNotCountedAgain()
    {
        var data = Company(5000m);
        data.Payments.Add(Paid("PAY-0001", 300m, new DateTime(2026, 2, 1)));
        data.Payments.Add(Paid("PAY-0002", 200m, OpeningDay));

        Assert.Equal(5200m, Row(BalanceSheet(data), "Cash"));
    }

    [Fact]
    public void AReportEndingBeforeTheOpeningBalance_WorksFromTransactionsAlone()
    {
        var data = Company(5000m);
        data.Payments.Add(Paid("PAY-0001", 300m, new DateTime(2026, 2, 1)));

        var sheet = BalanceSheet(data, end: new DateTime(2026, 2, 28));

        Assert.Equal(300m, Row(sheet, "Cash"));
        Assert.DoesNotContain(sheet.Rows, r => r.Label == "Opening Balance");
    }

    [Fact]
    public void WithoutAnOpeningBalance_CashStartsFromZero_AndTheFootnoteSaysWhereToAddOne()
    {
        var data = Company(null);
        data.Payments.Add(Paid("PAY-0001", 300m, new DateTime(2026, 2, 1)));

        var sheet = BalanceSheet(data);

        Assert.Equal(300m, Row(sheet, "Cash"));
        Assert.DoesNotContain(sheet.Rows, r => r.Label == "Opening Balance");
        Assert.Contains("Edit Company", sheet.Footnote);
    }

    [Fact]
    public void TheOpeningBalance_IsKeptWhenTheCompanyIsSavedAndOpenedAgain()
    {
        var settings = new CompanySettings
        {
            OpeningBalance = new OpeningBalance { Amount = 1234.56m, Currency = "CAD", Date = OpeningDay }
        };

        var reopened = JsonSerializer.Deserialize<CompanySettings>(JsonSerializer.Serialize(settings))!;

        Assert.Equal(settings.OpeningBalance, reopened.OpeningBalance);
    }

    [Fact]
    public void ACompanySavedBeforeOpeningBalancesExisted_HasNone()
    {
        Assert.Null(JsonSerializer.Deserialize<CompanySettings>("{}")!.OpeningBalance);
    }

    [Fact]
    public void AnEmptyAmount_MeansNoOpeningBalance()
    {
        Assert.True(OpeningBalanceInput.TryRead("  ", "USD", out var balance, out var error));
        Assert.Null(balance);
        Assert.Null(error);
    }

    [Fact]
    public void ATypedAmount_IsReadWithItsCurrency_AndDatedToday()
    {
        Assert.True(OpeningBalanceInput.TryRead("-250.75", "CAD", out var balance, out _));
        Assert.Equal(new OpeningBalance { Amount = -250.75m, Currency = "CAD", Date = DateTime.Today }, balance);
    }

    [Fact]
    public void ABalance_IsDescribedWithItsAmountAndDay_AndNoneAsNotSet()
    {
        Assert.Equal("$5,000.00 on Mar 1, 2026",
            OpeningBalanceInput.Describe(new OpeningBalance { Amount = 5000m, Currency = "USD", Date = OpeningDay }));
        Assert.Equal("Not set", OpeningBalanceInput.Describe(null));
    }

    [Fact]
    public void AnAmountThatIsNotANumber_IsRefusedWithAMessage()
    {
        Assert.False(OpeningBalanceInput.TryRead("abc", "USD", out var balance, out var error));
        Assert.Null(balance);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
